using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public abstract class DrawPairBase
{
    protected readonly ApiController _apiController;
    protected readonly UidDisplayHandler _displayHandler;
    protected readonly MareMediator _mediator;
    protected readonly UiSharedService _uiSharedService;
    protected Pair _pair;
    private readonly string _id;

    protected DrawPairBase(string id, Pair entry, ApiController apiController, UidDisplayHandler uIDDisplayHandler, MareMediator mareMediator, UiSharedService uiSharedService)
    {
        _id = id;
        _pair = entry;
        _apiController = apiController;
        _displayHandler = uIDDisplayHandler;
        _mediator = mareMediator;
        _uiSharedService = uiSharedService;
    }

    public string ImGuiID => _id;
    public string UID => _pair.UserData.UID;

    public void DrawPairedClient()
    {
        var originalY = ImGui.GetCursorPosY();
        var pauseIconSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Play);
        var textSize = ImGui.CalcTextSize(_pair.UserData.AliasOrUID);

        var startPos = ImGui.GetCursorStartPos();

        var framePadding = ImGui.GetStyle().FramePadding;
        var lineHeight = textSize.Y + framePadding.Y * 2;

        var off = startPos.Y;
        var height = UiSharedService.GetWindowContentRegionHeight();

        if ((originalY + off) < -lineHeight || (originalY + off) > height)
        {
            ImGui.Dummy(new System.Numerics.Vector2(0f, lineHeight));
            return;
        }

        var textPosY = originalY + pauseIconSize.Y / 2 - textSize.Y / 2;
        DrawLeftSide(textPosY, originalY);
        ImGui.SameLine();
        var posX = ImGui.GetCursorPosX();
        var rightSide = DrawRightSide(textPosY, originalY);
        DrawName(originalY, posX, rightSide);
    }

    protected abstract void DrawLeftSide(float textPosY, float originalY);

    protected abstract float DrawRightSide(float textPosY, float originalY);

    private void DrawName(float originalY, float leftSide, float rightSide)
    {
        _displayHandler.DrawPairText(_id, _pair, leftSide, originalY, () => rightSide - leftSide);
    }

    protected void DrawVisibleIcon()
    {
        var uid = _pair.UserData.AliasOrUID;

        ImGui.PushFont(UiBuilder.IconFont);
        UiSharedService.ColorText(FontAwesomeIcon.Eye.ToIconString(), ImGuiColors.ParsedGreen);
        ImGui.PopFont();

        if (ImGui.IsItemClicked())
            _mediator.Publish(new TargetPairMessage(_pair));

        string tooltipText = uid + " is visible: " + _pair.PlayerName + Environment.NewLine + "Click to target this player";

        if (_pair.LastAppliedDataBytes >= 0)
        {
            tooltipText += UiSharedService.TooltipSeparator;
            tooltipText += ((!_pair.IsVisible) ? "(Last) " : string.Empty) + "Mods Info" + Environment.NewLine;
            tooltipText += "Files Size: " + UiSharedService.ByteToString(_pair.LastAppliedDataBytes, true);
            if (_pair.LastAppliedApproximateVRAMBytes >= 0)
            {
                tooltipText += Environment.NewLine + "Approx. VRAM Usage: " + UiSharedService.ByteToString(_pair.LastAppliedApproximateVRAMBytes, true);
            }
            if (_pair.LastAppliedDataTris >= 0)
            {
                tooltipText += Environment.NewLine + "Triangle Count (excl. Vanilla): "
                    + (_pair.LastAppliedDataTris > 1000 ? (_pair.LastAppliedDataTris / 1000d).ToString("0.0'k'") : _pair.LastAppliedDataTris);
            }
        }
        else if (UiSharedService.CtrlPressed())
        {
            var debugText = "";
            if (!_pair.HasCachedData) debugText += Environment.NewLine + "* No data received";
            if (!_pair.HasPenumbraCollection) debugText += Environment.NewLine + "* No penumbra collection";
            if (!_pair.HasInitializedCharacter) debugText += Environment.NewLine + "* CharaHandler not initialized";
            if (!_pair.HasCachedPlayer) debugText += Environment.NewLine + "* No CachedPlayer";
            if (!_pair.IsVisible) debugText += Environment.NewLine + "* Not visible";
            if (_pair.IsDownloadBlocked) debugText += Environment.NewLine + "* DownloadBlocked";
            else if (_pair.IsApplicationBlocked) debugText += Environment.NewLine + "* ApplicationBlocked";
            if (_pair.HasPendingCombatData) debugText += Environment.NewLine + "* Pending CombatData";
            if (_pair.HasPendingDeferred) debugText += Environment.NewLine + "* Deferred application pending";
            if (_pair.HasActiveApplicationTask) debugText += Environment.NewLine + "* Application task is running";
            if (_pair.HasHaltedProcessing) debugText += Environment.NewLine + "* Halted processing (redraw or cutscene)";
            var drawCondition = _pair.CurrentDrawCondition;
            if (drawCondition != PlayerData.Handlers.GameObjectHandler.DrawCondition.None)
                debugText += Environment.NewLine + $"* DrawCondition: {drawCondition}";
            if (string.IsNullOrEmpty(debugText))
                debugText = "* No mod files";
            tooltipText += UiSharedService.TooltipSeparator + "Debug Info";
            tooltipText += debugText;
        }

        UiSharedService.AttachToolTip(tooltipText);
    }
}