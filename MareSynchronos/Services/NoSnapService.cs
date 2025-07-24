using Dalamud.Plugin;
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

    public static bool AnyLoaded { get; private set; } = false;

    public MareMediator Mediator { get; init; }

    public NoSnapService(ILogger<NoSnapService> logger, IDalamudPluginInterface pi, MareMediator mediator)
    {
        _logger = logger;
        Mediator = mediator;

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

        Update();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
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
                var pluginList = string.Join(", ", _listOfPlugins.Where(p => p.Value).Select(p => p.Key));
                Mediator.Publish(new NotificationMessage("Incompatible plugin loaded", $"Synced player appearances will not apply until incompatible plugins are disabled: {pluginList}.",
                    NotificationType.Error));
            }
        }
    }
}