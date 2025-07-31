using Dalamud.Plugin;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public class NoSnapService : IHostedService, IMediatorSubscriber
{
    private readonly ILogger<NoSnapService> _logger;
    private readonly Dictionary<string, bool> _listOfPlugins = new(StringComparer.Ordinal)
    {
        ["Snapper"] = false,
        ["Snappy"] = false,
        ["Meddle.Plugin"] = false
    };
    private static readonly HashSet<int> _gposers = new();
    private static readonly HashSet<string> _gposersNamed = new(StringComparer.Ordinal);
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly IpcManager _ipcManager;

    public static bool AnyLoaded { get; private set; } = false;

    public MareMediator Mediator { get; init; }

    public NoSnapService(ILogger<NoSnapService> logger, IDalamudPluginInterface pi, MareMediator mediator,
        IHostApplicationLifetime hostApplicationLifetime, DalamudUtilService dalamudUtilService, IpcManager ipcManager)
    {
        _logger = logger;
        Mediator = mediator;
        _hostApplicationLifetime = hostApplicationLifetime;
        _dalamudUtilService = dalamudUtilService;
        _ipcManager = ipcManager;
        foreach (var pluginName in _listOfPlugins.Keys)
        {
            var plugin = pi.InstalledPlugins.FirstOrDefault(p => p.InternalName.Equals(pluginName, StringComparison.Ordinal));
            if (plugin?.IsLoaded ?? false)
                _listOfPlugins[pluginName] = true;
            Mediator.SubscribeKeyed<PluginChangeMessage>(this, pluginName, (msg) =>
            {
                _logger.LogInformation("{pluginName} isLoaded = {isLoaded}", pluginName, msg.IsLoaded);
                _listOfPlugins[pluginName] = msg.IsLoaded;
                Update();
            });
        }

        Mediator.Subscribe<GposeEndMessage>(this, msg => ClearGposeList());
        Mediator.Subscribe<CutsceneEndMessage>(this, msg => ClearGposeList());

        Update();
    }

    public void AddGposer(int objectIndex)
    {
        if (AnyLoaded || _hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _logger.LogInformation("Immediately reverting object index {id}", objectIndex);
            RevertAndRedraw(objectIndex);
            return;
        }

        _logger.LogInformation("Registering gposer object index {id}", objectIndex);
        lock (_gposers)
            _gposers.Add(objectIndex);
    }

    public void RemoveGposer(int objectIndex)
    {
        _logger.LogInformation("Un-registering gposer object index {id}", objectIndex);
        lock (_gposers)
            _gposers.Remove(objectIndex);
    }

    public void AddGposerNamed(string name)
    {
        if (AnyLoaded || _hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _logger.LogInformation("Immediately reverting {name}", name);
            RevertAndRedraw(name);
            return;
        }

        _logger.LogInformation("Registering gposer {name}", name);
        lock (_gposers)
            _gposersNamed.Add(name);
    }

    private void ClearGposeList()
    {
        if (_gposers.Count > 0 || _gposersNamed.Count > 0)
            _logger.LogInformation("Clearing gposer list");
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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        RevertGposers();
        return Task.CompletedTask;
    }

    private void Update()
    {
        bool anyLoadedNow = _listOfPlugins.Values.Any(p => p);

        if (AnyLoaded != anyLoadedNow)
        {
            _logger.LogInformation("AnyLoaded is now {AnyLoaded}", AnyLoaded);
            AnyLoaded = anyLoadedNow;
            Mediator.Publish(new RecalculatePerformanceMessage(null));

            if (AnyLoaded)
            {
                RevertGposers();
                var pluginList = string.Join(", ", _listOfPlugins.Where(p => p.Value).Select(p => p.Key));
                Mediator.Publish(new NotificationMessage("Incompatible plugin loaded", $"Synced player appearances will not apply until incompatible plugins are disabled: {pluginList}.",
                    NotificationType.Error));
            }
        }
    }
}