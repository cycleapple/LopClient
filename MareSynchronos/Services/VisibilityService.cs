using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.Services;

// Detect when players of interest are visible
public class VisibilityService : DisposableMediatorSubscriberBase
{
    private readonly DalamudUtilService _dalamudUtil;
    private ConcurrentDictionary<string, bool> _trackedPlayerVisibility = new();
    private ConcurrentQueue<string> _removedPlayerMessageQueue = new();

    public VisibilityService(ILogger<PairAnalyzer> logger, MareMediator mediator, DalamudUtilService dalamudUtil)
        : base(logger, mediator)
    {
        _dalamudUtil = dalamudUtil;
        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => FrameworkUpdate());
    }

    public void StartTracking(string ident)
    {
        _trackedPlayerVisibility.TryAdd(ident, false);
    }

    public void StopTracking(string ident)
    {
        // No PairVisibilityMessage is emitted if the player was visible when removed
        _trackedPlayerVisibility.TryRemove(ident, out _);
    }

    private void FrameworkUpdate()
    {
        foreach (var player in _trackedPlayerVisibility)
        {
            string ident = player.Key;
            var findResult = _dalamudUtil.FindPlayerByNameHash(ident);

            if (!player.Value && findResult.ObjectId != 0)
            {
                if (_trackedPlayerVisibility.TryUpdate(ident, newValue: true, comparisonValue: false))
                    Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: true));
            }
            else if (player.Value && findResult.ObjectId == 0)
            {
                if (_trackedPlayerVisibility.TryUpdate(ident, newValue: false, comparisonValue: true))
                    Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: false));
            }
        }
    }
}