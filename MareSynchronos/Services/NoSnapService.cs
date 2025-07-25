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
            try
            {
                Guid applicationId = Guid.NewGuid();
                _ipcManager.Glamourer.RevertNow(_logger, applicationId, objectIndex);
                _ipcManager.Penumbra.RedrawNow(_logger, applicationId, objectIndex);
            }
            catch { }
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

    private void ClearGposeList()
    {
        if (_gposers.Count > 0)
            _logger.LogInformation("Clearing gposer list");
        lock (_gposers)
            _gposers.Clear();
    }

    private void RevertGposers()
    {
        List<int>? gposersList = null;

        lock (_gposers)
        {
            if (_gposers.Count > 0)
            {
                _logger.LogInformation("Reverting gposers");
                gposersList = _gposers.ToList();
                _gposers.Clear();
            }
        }

        if (gposersList == null)
            return;

        _dalamudUtilService.RunOnFrameworkThread(() =>
        {
            Guid applicationId = Guid.NewGuid();

            foreach (var gposer in gposersList)
            {
                try
                {
                    _ipcManager.Glamourer.RevertNow(_logger, applicationId, gposer);
                    _ipcManager.Penumbra.RedrawNow(_logger, applicationId, gposer);
                }
                catch { }
            }
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