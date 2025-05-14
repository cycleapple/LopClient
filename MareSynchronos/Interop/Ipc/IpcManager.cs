using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public sealed partial class IpcManager : DisposableMediatorSubscriberBase
{
    public IpcManager(ILogger<IpcManager> logger, MareMediator mediator,
        IpcCallerPenumbra penumbraIpc, IpcCallerGlamourer glamourerIpc, IpcCallerCustomize customizeIpc, IpcCallerHeels heelsIpc,
        IpcCallerHonorific honorificIpc, IpcCallerPetNames ipcCallerPetNames, IpcCallerMoodles moodlesIpc) : base(logger, mediator)
    {
        CustomizePlus = customizeIpc;
        Heels = heelsIpc;
        Glamourer = glamourerIpc;
        Penumbra = penumbraIpc;
        Honorific = honorificIpc;
        PetNames = ipcCallerPetNames;
        Moodles = moodlesIpc;

        if (Initialized)
        {
            Mediator.Publish(new PenumbraInitializedMessage());
        }

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => PeriodicApiStateCheck());

        try
        {
            PeriodicApiStateCheck();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check for some IPC, plugin not installed?");
        }
    }

    public bool Initialized => Penumbra.APIAvailable && Glamourer.APIAvailable;

    public IpcCallerCustomize CustomizePlus { get; init; }
    public IpcCallerHonorific Honorific { get; init; }
    public IpcCallerHeels Heels { get; init; }
    public IpcCallerGlamourer Glamourer { get; }
    public IpcCallerPenumbra Penumbra { get; }
    public IpcCallerPetNames PetNames { get; }
    public IpcCallerMoodles Moodles { get; }

    private int _stateCheckCounter = -1;

    private void PeriodicApiStateCheck()
    {
        // Stagger API checks
        if (++_stateCheckCounter > 7)
            _stateCheckCounter = 0;
        int i = _stateCheckCounter;
        if (i == 0) Penumbra.CheckAPI();
        if (i == 1) Penumbra.CheckModDirectory();
        if (i == 2) Glamourer.CheckAPI();
        if (i == 3) Heels.CheckAPI();
        if (i == 4) CustomizePlus.CheckAPI();
        if (i == 5) Honorific.CheckAPI();
        if (i == 6) PetNames.CheckAPI();
        if (i == 7) Moodles.CheckAPI();
    }
}