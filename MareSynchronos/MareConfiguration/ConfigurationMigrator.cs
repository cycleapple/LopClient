using MareSynchronos.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.MareConfiguration;

public class ConfigurationMigrator(ILogger<ConfigurationMigrator> logger,
    NotesConfigService notesConfig) : IHostedService
{
    private readonly ILogger<ConfigurationMigrator> _logger = logger;

    public void Migrate()
    {
        var oldUri = ApiController.LoporritServiceUriOld;
        var newUri = ApiController.LoporritServiceUri;

        if (notesConfig.Current.ServerNotes.TryGetValue(oldUri, out var old))
        {
            _logger.LogDebug("Migrating server notes {old} => {new}", oldUri, newUri);
            notesConfig.Current.ServerNotes.TryAdd(newUri, new());
            var merged = notesConfig.Current.ServerNotes.GetValueOrDefault(newUri, new());
            foreach (var (k, v) in old.GidServerComments)
                merged.GidServerComments.TryAdd(k, v);
            foreach (var (k, v) in old.UidServerComments)
                merged.UidServerComments.TryAdd(k, v);
            notesConfig.Current.ServerNotes.Remove(oldUri);
            notesConfig.Save();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Migrate();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
