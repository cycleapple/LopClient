using Dalamud.Plugin;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using CapturedPluginState = (string InternalName, System.Version Version, bool IsLoaded);

namespace MareSynchronos.PlayerData.Pairs;

/* Parts of this code from ECommons DalamudReflector

MIT License

Copyright (c) 2023 NightmareXIV

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

public class PluginWatcherService : MediatorSubscriberBase
{
    private readonly ConcurrentDictionary<UserData, OptionalPluginWarning> _cachedOptionalPluginWarnings = new(UserDataComparer.Instance);
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly MareConfigService _mareConfigService;

    private CapturedPluginState[] _prevInstalledPluginState = [];

    private readonly static System.Version VersionZero = new(0, 0, 0, 0);

    private bool ExposedPluginsEqual(IEnumerable<IExposedPlugin> plugins, IEnumerable<CapturedPluginState> other)
    {
        if(plugins.Count() != other.Count()) return false;
        var enumeratorOriginal = plugins.GetEnumerator();
        var enumeratorOther = other.GetEnumerator();
        while(true)
        {
            var move1 = enumeratorOriginal.MoveNext();
            var move2 = enumeratorOther.MoveNext();
            if(move1 != move2) return false;
            if(move1 == false) return true;
            if(enumeratorOriginal.Current.IsLoaded != enumeratorOther.Current.IsLoaded) return false;
            if(enumeratorOriginal.Current.Version != enumeratorOther.Current.Version) return false;
            if(enumeratorOriginal.Current.InternalName != enumeratorOther.Current.InternalName) return false;
        }
    }

    public PluginWatcherService(ILogger<PluginWatcherService> logger, MareConfigService mareConfigService,
        IDalamudPluginInterface pluginInterface, MareMediator mediator) : base(logger, mediator)
    {
        _mareConfigService = mareConfigService;
        _pluginInterface = pluginInterface;

        Mediator.Subscribe<PriorityFrameworkUpdateMessage>(this, (_) => {
            try
            {
                Update();
            }
            catch (Exception e)
            {
                Logger.LogError(e, "PluginWatcherService exception");
            }
        });
    }

    private void Update()
    {
        if (!ExposedPluginsEqual(_pluginInterface.InstalledPlugins, _prevInstalledPluginState))
        {
            var state = _pluginInterface.InstalledPlugins.Select(x => new CapturedPluginState(x.InternalName, x.Version ?? VersionZero, x.IsLoaded)).ToArray();

            // The same plugin can be installed multiple times -- InternalName is not unique

            var oldDict = _prevInstalledPluginState.Where(x => x.InternalName.Length > 0)
                .GroupBy(x => x.InternalName)
                .ToDictionary(x => x.Key);

            var newDict = state.Where(x => x.InternalName.Length > 0)
                .GroupBy(x => x.InternalName)
                .ToDictionary(x => x.Key);

            _prevInstalledPluginState = state;

            foreach (var internalName in newDict.Keys.Except(oldDict.Keys))
            {
                var p = newDict[internalName].OrderBy(p => (!p.IsLoaded, p.Version)).First();
                Mediator.Publish(new PluginChangeMessage(internalName, p.Version, p.IsLoaded));
            }

            foreach (var internalName in oldDict.Keys.Except(newDict.Keys))
            {
                var p = newDict[internalName].OrderBy(p => (!p.IsLoaded, p.Version)).First();
                Mediator.Publish(new PluginChangeMessage(p.InternalName, p.Version, p.IsLoaded));
            }

            foreach (var changedGroup in newDict.Where(p => oldDict.TryGetValue(p.Key, out var old) && !old.SequenceEqual(p.Value)))
            {
                var internalName = changedGroup.Value.First().InternalName;
                var p = newDict[internalName].OrderBy(p => (!p.IsLoaded, p.Version)).First();
                Mediator.Publish(new PluginChangeMessage(p.InternalName, p.Version, p.IsLoaded));
            }
        }
    }
}