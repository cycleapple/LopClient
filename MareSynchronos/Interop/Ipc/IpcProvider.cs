using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public class IpcProvider : IHostedService, IMediatorSubscriber
{
    private readonly ILogger<IpcProvider> _logger;
    private readonly IDalamudPluginInterface _pi;
    private readonly MareConfigService _mareConfig;
    private readonly MareCharaFileManager _mareCharaFileManager;
    private readonly DalamudUtilService _dalamudUtil;
    private ICallGateProvider<string, IGameObject, bool>? _loadFileProvider;
    private ICallGateProvider<string, IGameObject, Task<bool>>? _loadFileAsyncProvider;
    private ICallGateProvider<List<nint>>? _handledGameAddresses;
    private readonly List<GameObjectHandler> _activeGameObjectHandlers = [];

    private ICallGateProvider<string, IGameObject, bool>? _loadFileProviderMare;
    private ICallGateProvider<string, IGameObject, Task<bool>>? _loadFileAsyncProviderMare;
    private ICallGateProvider<List<nint>>? _handledGameAddressesMare;

    private bool _marePluginEnabled = false;
    private bool _impersonating = false;

    public bool MarePluginEnabled => _marePluginEnabled;
    public bool ImpersonationActive => _impersonating;

    public MareMediator Mediator { get; init; }

    public IpcProvider(ILogger<IpcProvider> logger, IDalamudPluginInterface pi, MareConfigService mareConfig,
        MareCharaFileManager mareCharaFileManager, DalamudUtilService dalamudUtil,
        MareMediator mareMediator)
    {
        _logger = logger;
        _pi = pi;
        _mareConfig = mareConfig;
        _mareCharaFileManager = mareCharaFileManager;
        _dalamudUtil = dalamudUtil;
        Mediator = mareMediator;

        Mediator.Subscribe<GameObjectHandlerCreatedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.Add(msg.GameObjectHandler);
        });
        Mediator.Subscribe<GameObjectHandlerDestroyedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.Remove(msg.GameObjectHandler);
        });

        _marePluginEnabled = pi.InstalledPlugins.Any(p => p.InternalName == "MareSynchronos" && p.IsLoaded);
        Mediator.Subscribe<PluginChangeMessage>(this, p => {
            if (p.InternalName == "MareSynchronos")
                _marePluginEnabled = p.IsLoaded;
            HandleMareImpersonation();
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting IpcProvider Service");
        _loadFileProvider = _pi.GetIpcProvider<string, IGameObject, bool>("LoporritSync.LoadMcdf");
        _loadFileProvider.RegisterFunc(LoadMcdf);
        _loadFileAsyncProvider = _pi.GetIpcProvider<string, IGameObject, Task<bool>>("LoporritSync.LoadMcdfAsync");
        _loadFileAsyncProvider.RegisterFunc(LoadMcdfAsync);
        _handledGameAddresses = _pi.GetIpcProvider<List<nint>>("LoporritSync.GetHandledAddresses");
        _handledGameAddresses.RegisterFunc(GetHandledAddresses);

        _loadFileProviderMare = _pi.GetIpcProvider<string, IGameObject, bool>("MareSynchronos.LoadMcdf");
        _loadFileAsyncProviderMare = _pi.GetIpcProvider<string, IGameObject, Task<bool>>("MareSynchronos.LoadMcdfAsync");
        _handledGameAddressesMare = _pi.GetIpcProvider<List<nint>>("MareSynchronos.GetHandledAddresses");
        HandleMareImpersonation();

        _logger.LogInformation("Started IpcProviderService");
        return Task.CompletedTask;
    }

    public void HandleMareImpersonation()
    {
        if (_marePluginEnabled)
        {
            if (_impersonating)
            {
                // Do not attempt to unregister the functions at this point, otherwise we risk unregistering Mare's
                // This may leave the APIs hanging if Mare did not initialize to the point of registering its API though
                _impersonating = false;
                _logger.LogDebug("Releasing MareSynchronos API");
            }
        }
        else
        {
            if (_mareConfig.Current.MareAPI)
            {
                _loadFileProviderMare?.RegisterFunc(LoadMcdf);
                _loadFileAsyncProviderMare?.RegisterFunc(LoadMcdfAsync);
                _handledGameAddressesMare?.RegisterFunc(GetHandledAddresses);
                _impersonating = true;
                _logger.LogDebug("Registered MareSynchronos API");
            }
            else
            {
                _loadFileProviderMare?.UnregisterFunc();
                _loadFileAsyncProviderMare?.UnregisterFunc();
                _handledGameAddressesMare?.UnregisterFunc();
                _impersonating = false;
                _logger.LogDebug("Unregistered MareSynchronos API");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping IpcProvider Service");
        _loadFileProvider?.UnregisterFunc();
        _loadFileAsyncProvider?.UnregisterFunc();
        _handledGameAddresses?.UnregisterFunc();

        if (_impersonating)
        {
            _loadFileProviderMare?.UnregisterFunc();
            _loadFileAsyncProviderMare?.UnregisterFunc();
            _handledGameAddressesMare?.UnregisterFunc();
        }

        Mediator.UnsubscribeAll(this);
        return Task.CompletedTask;
    }

    private async Task<bool> LoadMcdfAsync(string path, IGameObject target)
    {
        if (_mareCharaFileManager.CurrentlyWorking || !_dalamudUtil.IsInGpose)
            return false;

        await ApplyFileAsync(path, target).ConfigureAwait(false);

        return true;
    }

    private bool LoadMcdf(string path, IGameObject target)
    {
        if (_mareCharaFileManager.CurrentlyWorking || !_dalamudUtil.IsInGpose)
            return false;

        _ = Task.Run(async () => await ApplyFileAsync(path, target).ConfigureAwait(false)).ConfigureAwait(false);

        return true;
    }

    private async Task ApplyFileAsync(string path, IGameObject target)
    {
        try
        {
            var expectedLength = _mareCharaFileManager.LoadMareCharaFile(path);
            await _mareCharaFileManager.ApplyMareCharaFile(target, expectedLength).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failure of IPC call");
        }
        finally
        {
            _mareCharaFileManager.ClearMareCharaFile();
        }
    }

    private List<nint> GetHandledAddresses()
    {
        return _activeGameObjectHandlers.Where(g => g.Address != nint.Zero).Select(g => g.Address).Distinct().ToList();
    }
}
