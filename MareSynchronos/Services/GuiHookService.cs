using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public class GuiHookService : DisposableMediatorSubscriberBase
{
    private readonly ILogger<GuiHookService> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareConfigService _configService;
    private readonly INamePlateGui _namePlateGui;
    private readonly PairManager _pairManager;

    private bool _isModified = false;

    public GuiHookService(ILogger<GuiHookService> logger, DalamudUtilService dalamudUtil, MareMediator mediator, MareConfigService configService,
        INamePlateGui namePlateGui, PairManager pairManager)
        : base(logger, mediator)
    {
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _configService = configService;
        _namePlateGui = namePlateGui;
        _pairManager = pairManager;

        _namePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;
        _namePlateGui.RequestRedraw();

        Mediator.Subscribe<PairHandlerVisibleMessage>(this, (_) => RequestRedraw());
        Mediator.Subscribe<NameplateRedrawMessage>(this, (_) => RequestRedraw());
    }

    public void RequestRedraw(bool force = false)
    {
        if (!_configService.Current.UseNameColors)
        {
            if (!_isModified && !force)
                return;
            _isModified = false;
        }

        Task.Run(async () => {
            await _dalamudUtil.RunOnFrameworkThread(() => _namePlateGui.RequestRedraw());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _namePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;

        Task.Run(async () => {
            await _dalamudUtil.RunOnFrameworkThread(() => _namePlateGui.RequestRedraw());
        });
    }

    private void OnNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (!_configService.Current.UseNameColors)
            return;

        var visibleUsers = _pairManager.GetOnlineUserPairs().Where(u => u.IsVisible && u.PlayerCharacterId != uint.MaxValue);
        var visibleUsersIds = visibleUsers.Select(u => (ulong)u.PlayerCharacterId).ToHashSet();

        var visibleUsersDict = visibleUsers.ToDictionary(u => (ulong)u.PlayerCharacterId);

        foreach (var handler in handlers)
        {
            if (visibleUsersIds.Contains(handler.GameObjectId))
            {
                var pair = visibleUsersDict[handler.GameObjectId];
                var colors = !pair.IsApplicationBlocked ? _configService.Current.NameColors : _configService.Current.BlockedNameColors;
                handler.NameParts.TextWrap = (
                    BuildColorStartSeString(colors),
                    BuildColorEndSeString(colors)
                );
                _isModified = true;
            }
        }
    }

    #region Colored SeString
    private const byte _colorTypeForeground = 0x13;
    private const byte _colorTypeGlow = 0x14;

    private static SeString BuildColorStartSeString(DtrEntry.Colors colors)
    {
        var ssb = new SeStringBuilder();
        if (colors.Foreground != default)
            ssb.Add(BuildColorStartPayload(_colorTypeForeground, colors.Foreground));
        if (colors.Glow != default)
            ssb.Add(BuildColorStartPayload(_colorTypeGlow, colors.Glow));
        return ssb.Build();
    }

    private static SeString BuildColorEndSeString(DtrEntry.Colors colors)
    {
        var ssb = new SeStringBuilder();
        if (colors.Glow != default)
            ssb.Add(BuildColorEndPayload(_colorTypeGlow));
        if (colors.Foreground != default)
            ssb.Add(BuildColorEndPayload(_colorTypeForeground));
        return ssb.Build();
    }

    private static RawPayload BuildColorStartPayload(byte colorType, uint color)
        => new(unchecked([0x02, colorType, 0x05, 0xF6, byte.Max((byte)color, 0x01), byte.Max((byte)(color >> 8), 0x01), byte.Max((byte)(color >> 16), 0x01), 0x03]));

    private static RawPayload BuildColorEndPayload(byte colorType)
        => new([0x02, colorType, 0x02, 0xEC, 0x03]);
    #endregion
}
