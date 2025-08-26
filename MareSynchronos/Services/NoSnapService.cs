using Dalamud.Plugin;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MareSynchronos.Services;

public sealed class NoSnapService : IHostedService, IMediatorSubscriber
{
    private record NoSnapConfig
    {
        [JsonPropertyName("pcp")]
        public bool? PCP { get; set; } = true;

        [JsonPropertyName("listOfPlugins")]
        public string[]? ListOfPlugins { get; set; }
    }

    private readonly ILogger<NoSnapService> _logger;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Dictionary<string, bool> _listOfPlugins = new(StringComparer.Ordinal)
    {
        ["Snapper"] = false,
        ["Snappy"] = false,
        ["Meddle.Plugin"] = false,
    };
    private static readonly HashSet<int> _gposers = new();
    private static readonly HashSet<string> _gposersNamed = new(StringComparer.Ordinal);
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly IpcManager _ipcManager;
    private readonly RemoteConfigurationService _remoteConfig;

    public static bool AnyLoaded { get; private set; } = false;
    public static string ActivePlugins { get; private set; } = string.Empty;

    public MareMediator Mediator { get; init; }

    private FileSystemWatcher? _penumbraConfigWatcher { get; set; }
    private FileSystemWatcher? _penumbraExportWatcher { get; set; }
    private string _penumbraExportDirectory = string.Empty;
    private bool _pcp = true;
    private bool _isConnected = false;

    public NoSnapService(ILogger<NoSnapService> logger, IDalamudPluginInterface pluginInterface, MareMediator mediator,
        IHostApplicationLifetime hostApplicationLifetime, DalamudUtilService dalamudUtilService, IpcManager ipcManager,
        RemoteConfigurationService remoteConfig)
    {
        _logger = logger;
        _pluginInterface = pluginInterface;
        Mediator = mediator;
        _hostApplicationLifetime = hostApplicationLifetime;
        _dalamudUtilService = dalamudUtilService;
        _ipcManager = ipcManager;
        _remoteConfig = remoteConfig;

        Mediator.Subscribe<GposeEndMessage>(this, msg => ClearGposeList());
        Mediator.Subscribe<CutsceneEndMessage>(this, msg => ClearGposeList());

        Mediator.Subscribe<ConnectedMessage>(this, msg => _isConnected = true);
        Mediator.Subscribe<AfterDisconnectMessage>(this, msg => _isConnected = false);

        Mediator.Subscribe<PenumbraInitializedMessage>(this, msg =>
        {
            var configPath = pluginInterface.ConfigDirectory.Parent!.FullName;
            var penumbraConfig = Path.Combine(configPath, "Penumbra.json");
            _ = Task.Run(async () => await HandleUpdatedConfigFile(penumbraConfig).ConfigureAwait(false));
            StartPenumbraConfigWatcher(configPath);
        });
    }

    private static string? ReadExportDirectory(string configPath)
    {
        try
        {
            var jsonText = File.ReadAllText(configPath);

            if (jsonText.Length == 0)
                return null;

            var jsonDoc = JsonNode.Parse(jsonText) as JsonObject;

            if (jsonDoc == null)
                return null;

            var exportDir = jsonDoc["ExportDirectory"]?.AsValue()?.ToString();

            if (!string.IsNullOrEmpty(exportDir))
                return exportDir;
            else
                return jsonDoc["ModDirectory"]?.AsValue()?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private void StartPenumbraConfigWatcher(string configPath)
    {
        _penumbraConfigWatcher?.Dispose();
        _logger.LogDebug("Initializing Penumbra Config FSW on {path}", configPath);
        _penumbraConfigWatcher = new()
        {
            Path = configPath,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.Size,
            Filter = "Penumbra.json",
            IncludeSubdirectories = false,
        };
        _penumbraConfigWatcher.Created += PenumbraConfig_Changed;
        _penumbraConfigWatcher.Changed += PenumbraConfig_Changed;
        _penumbraConfigWatcher.Renamed += PenumbraConfig_Changed;
        _penumbraConfigWatcher.EnableRaisingEvents = true;
    }

    private void StartPenumbraExportWatcher(string exportDir)
    {
        _penumbraExportWatcher?.Dispose();
        _logger.LogDebug("Initializing Penumbra Export FSW on {path}", exportDir);
        _penumbraExportWatcher = new()
        {
            Path = exportDir,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.Size,
            Filter = "*.pcp",
            IncludeSubdirectories = false,
        };
        _penumbraExportWatcher.Created += PenumbraExport_Created;
        _penumbraExportWatcher.EnableRaisingEvents = true;
    }

    private async Task HandleUpdatedConfigFile(string configFile)
    {
        int retry = 10000;
        while (retry-- > 0)
        {
            try
            {
                var exportDirectory = ReadExportDirectory(configFile);

                if (exportDirectory == null)
                {
                    await Task.Delay(10).ConfigureAwait(false);
                    continue;
                }

                if (!string.Equals(exportDirectory, _penumbraExportDirectory, StringComparison.Ordinal))
                {
                    _penumbraExportDirectory = exportDirectory;
                    StartPenumbraExportWatcher(exportDirectory);
                }

                retry = 0;
            }
            catch
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
        }
    }

    private void PenumbraConfig_Changed(object sender, FileSystemEventArgs e)
    {
        _logger.LogTrace("Reading updated Penumbra.config");
        _ = Task.Run(async () => await HandleUpdatedConfigFile(e.FullPath).ConfigureAwait(false));
    }

    private void PenumbraExport_Created(object sender, FileSystemEventArgs e)
    {
        if (!_isConnected || !_pcp)
            return;
        if (!e.FullPath.EndsWith(".pcp", StringComparison.OrdinalIgnoreCase))
            return;
        _ = Task.Run(async () =>
        {
            _logger.LogInformation("Deleting created file {pcp}", e.FullPath);
            int retry = 10000;
            while (retry-- > 0)
            {
                try
                {
                    File.Delete(e.FullPath);
                    retry = 0;
                    Mediator.Publish(new NotificationMessage("PCP File Deleted",
                        "Please disconnect from the sync service before exporting PCP files.",
                        NotificationType.Error, TimeSpan.FromSeconds(10)));
                }
                catch
                {
                    await Task.Delay(10).ConfigureAwait(false);
                }
            }
        });
    }

    public void AddGposer(int objectIndex)
    {
        if (AnyLoaded || _hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _logger.LogTrace("Immediately reverting object index {id}", objectIndex);
            RevertAndRedraw(objectIndex);
            return;
        }

        _logger.LogTrace("Registering gposer object index {id}", objectIndex);
        lock (_gposers)
            _gposers.Add(objectIndex);
    }

    public void RemoveGposer(int objectIndex)
    {
        _logger.LogTrace("Un-registering gposer object index {id}", objectIndex);
        lock (_gposers)
            _gposers.Remove(objectIndex);
    }

    public void AddGposerNamed(string name)
    {
        if (AnyLoaded || _hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _logger.LogTrace("Immediately reverting {name}", name);
            RevertAndRedraw(name);
            return;
        }

        _logger.LogTrace("Registering gposer {name}", name);
        lock (_gposers)
            _gposersNamed.Add(name);
    }

    private void ClearGposeList()
    {
        if (_gposers.Count > 0 || _gposersNamed.Count > 0)
            _logger.LogTrace("Clearing gposer list");
        lock (_gposers)
            _gposers.Clear();
        lock (_gposersNamed)
            _gposersNamed.Clear();
    }

    private void RevertAndRedraw(int objIndex, Guid applicationId = default)
    {
        if (applicationId == default)
            applicationId = Guid.NewGuid();

        try
        {
            _ipcManager.Glamourer.RevertNow(_logger, applicationId, objIndex);
            _ipcManager.Penumbra.RedrawNow(_logger, applicationId, objIndex);
        }
        catch { }
    }

    private void RevertAndRedraw(string name, Guid applicationId = default)
    {
        if (applicationId == default)
            applicationId = Guid.NewGuid();

        try
        {
            _ipcManager.Glamourer.RevertByNameNow(_logger, applicationId, name);
            var addr = _dalamudUtilService.GetPlayerCharacterFromCachedTableByName(name);
            if (addr != 0)
            {
                var obj = _dalamudUtilService.CreateGameObject(addr);
                if (obj != null)
                    _ipcManager.Penumbra.RedrawNow(_logger, applicationId, obj.ObjectIndex);
            }
        }
        catch { }
    }

    private void RevertGposers()
    {
        List<int>? gposersList = null;
        List<string>? gposersList2 = null;

        lock (_gposers)
        {
            if (_gposers.Count > 0)
            {
                gposersList = _gposers.ToList();
                _gposers.Clear();
            }
        }

        lock (_gposersNamed)
        {
            if (_gposersNamed.Count > 0)
            {
                gposersList2 = _gposersNamed.ToList();
                _gposersNamed.Clear();
            }
        }

        if (gposersList == null && gposersList2 == null)
            return;

        _logger.LogInformation("Reverting gposers");

        _dalamudUtilService.RunOnFrameworkThread(() =>
        {
            Guid applicationId = Guid.NewGuid();

            foreach (var gposer in gposersList ?? [])
                RevertAndRedraw(gposer, applicationId);

            foreach (var gposerName in gposersList2 ?? [])
                RevertAndRedraw(gposerName, applicationId);
        }).GetAwaiter().GetResult();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = await _remoteConfig.GetConfigAsync<NoSnapConfig>("noSnap").ConfigureAwait(false) ?? new();

        _pcp = config.PCP ?? true;

        if (config.ListOfPlugins != null)
        {
            _listOfPlugins.Clear();
            foreach (var pluginName in config.ListOfPlugins)
                _listOfPlugins.TryAdd(pluginName, value: false);
        }

        foreach (var pluginName in _listOfPlugins.Keys)
        {
            _listOfPlugins[pluginName] = PluginWatcherService.GetInitialPluginState(_pluginInterface, pluginName)?.IsLoaded ?? false;
            Mediator.SubscribeKeyed<PluginChangeMessage>(this, pluginName, (msg) =>
            {
                _listOfPlugins[pluginName] = msg.IsLoaded;
                _logger.LogDebug("{pluginName} isLoaded = {isLoaded}", pluginName, msg.IsLoaded);
                Update();
            });
        }

        Update();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        RevertGposers();
        _penumbraConfigWatcher?.Dispose();
        _penumbraExportWatcher?.Dispose();
        return Task.CompletedTask;
    }

    private void Update()
    {
        bool anyLoadedNow = _listOfPlugins.Values.Any(p => p);

        if (AnyLoaded != anyLoadedNow)
        {
            AnyLoaded = anyLoadedNow;
            Mediator.Publish(new RecalculatePerformanceMessage(null));

            if (AnyLoaded)
            {
                RevertGposers();
                var pluginList = string.Join(", ", _listOfPlugins.Where(p => p.Value).Select(p => p.Key));
                Mediator.Publish(new NotificationMessage("Incompatible plugin loaded", $"Synced player appearances will not apply until incompatible plugins are disabled: {pluginList}.",
                    NotificationType.Error));
                ActivePlugins = pluginList;
            }
            else
            {
                ActivePlugins = string.Empty;
            }
        }
    }
}