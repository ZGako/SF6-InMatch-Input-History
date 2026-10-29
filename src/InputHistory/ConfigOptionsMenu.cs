using SF6_Plugin_Core.UI.InjectionShared;
using SF6_Plugin_Core.UI.Localization;
using SF6_Plugin_Core.UI.OptionsSettings.Core;
using SF6_Plugin_Core.UI.OptionsSettings.CustomOptions;
using SF6_Plugin_Core.UI.OptionsSettings.Requests;

namespace SF6_MIH.InputHistory;

public static class ConfigOptionsMenu
{
    private static readonly Guid OPEN_IH_SETTINGS_BUTTON = Guid.NewGuid();
    private static readonly Guid DISPLAY_POSITION_SPINBOX = Guid.NewGuid();
    private static readonly Guid SHOW_CPU_INPUTS = Guid.NewGuid();
    private static readonly Guid APPLY_POSITION_TO_CPU = Guid.NewGuid();
    private static readonly Guid SHOW_LINE_TEXTURE = Guid.NewGuid();

    private static readonly Guid OPEN_APPLIED_GAMEMODES = Guid.NewGuid();

    private static readonly Guid ENABLE_IN_LOCALVS = Guid.NewGuid();
    private static readonly Guid ENABLE_IN_SIMSIM = Guid.NewGuid();
    private static readonly Guid ENABLE_IN_RANKED = Guid.NewGuid();
    private static readonly Guid ENABLE_IN_CASUALS = Guid.NewGuid();
    private static readonly Guid ENABLE_IN_BATTLEHUD = Guid.NewGuid();
    private static readonly Guid ENABLE_IN_CUSTOM = Guid.NewGuid();

    private static readonly Guid OPEN_LEFT_TRANSFORM = Guid.NewGuid();

    private static readonly Guid LEFT_POSITION_X = Guid.NewGuid();
    private static readonly Guid LEFT_POSITION_Y = Guid.NewGuid();
    private static readonly Guid LEFT_SCALE = Guid.NewGuid();
    private static readonly Guid LEFT_MAX_ROWS = Guid.NewGuid();
    private static readonly Guid LEFT_TRANSPARENCY = Guid.NewGuid();

    private static readonly Guid OPEN_RIGHT_TRANSFORM = Guid.NewGuid();

    private static readonly Guid RIGHT_POSITION_X = Guid.NewGuid();
    private static readonly Guid RIGHT_POSITION_Y = Guid.NewGuid();
    private static readonly Guid RIGHT_SCALE = Guid.NewGuid();
    private static readonly Guid RIGHT_MAX_ROWS = Guid.NewGuid();
    private static readonly Guid RIGHT_TRANSPARENCY = Guid.NewGuid();

    private static readonly List<IUIModificationRequest> _optionsModificationRequests = [];

    private static InputHistoryClass? _dummyInputHistory;

    private static void BindValueChange(UICustomOptionsValueNode node, Action<int> applyChange)
    {
        node.AddHook(new OnValueTypeEventHookRequest((_, value, _) =>
        {
            applyChange(value);
            ModConfig.NotifyChanged();
        }));
    }

    public static void Init()
    {
        API.LogInfo("Initializing InputHistory config options menu");

        using var _ = LocalizationManager.UseFileScope("InMatchInputHistory");

        var openingInputHistorySettings = new UICustomOptionsGroupNode(OPEN_IH_SETTINGS_BUTTON,
            new OpenBattleHudOptionInitializer(
                CustomMessage.FromKey("IH_Menu_Title"),
                CustomMessage.FromKey("IH_Menu_Desc")));

        var displayPositionSpinBox = new UICustomOptionsValueNode(DISPLAY_POSITION_SPINBOX,
            new SpinBoxOptionInitializer(
                CustomMessage.FromKey("DisplayPos_Title"),
                CustomMessage.FromKey("DisplayPos_Desc"),
                [
                    CustomMessage.FromKey("DisplayPos_SameAsPlayer"),
                    CustomMessage.FromKey("DisplayPos_AlwaysLeft"),
                    CustomMessage.FromKey("DisplayPos_AlwaysRight")
                ],
                (int)ModConfig.Instance.DisplayPosition));
        BindValueChange(displayPositionSpinBox, val => ModConfig.Instance.DisplayPosition = (OnlinePosition)val);

        var showCPUInputs = new UICustomOptionsValueNode(SHOW_CPU_INPUTS,
            new OnOffSpinBoxOptionInitializer(
                CustomMessage.FromKey("ShowCPU_Title"),
                CustomMessage.FromKey("ShowCPU_Desc"),
                ModConfig.Instance.OfflineShowCPU,
                true));
        BindValueChange(showCPUInputs, val => ModConfig.Instance.OfflineShowCPU = val == 0);

        var applyPositionToCPU = new UICustomOptionsValueNode(APPLY_POSITION_TO_CPU,
            new OnOffSpinBoxOptionInitializer(
                CustomMessage.FromKey("ApplyPosCPU_Title"),
                CustomMessage.FromKey("ApplyPosCPU_Desc"),
                ModConfig.Instance.ApplyPositionAgainstCPU));
        BindValueChange(applyPositionToCPU, val => ModConfig.Instance.ApplyPositionAgainstCPU = val == 0);
        applyPositionToCPU.AddHook(new OnDisableReasonHookRequest(() => ModConfig.Instance.OfflineShowCPU));

        var showLineTexture = new UICustomOptionsValueNode(SHOW_LINE_TEXTURE,
            new OnOffSpinBoxOptionInitializer(
                CustomMessage.FromKey("ShowLineTex_Title"),
                CustomMessage.FromKey("ShowLineTex_Desc"),
                ModConfig.Instance.ShowBackgroundTexture,
                true));
        BindValueChange(showLineTexture, val => ModConfig.Instance.ShowBackgroundTexture = val == 0);

        // game modes
        var openAppliedGameModesSubmenu = new UICustomOptionsGroupNode(OPEN_APPLIED_GAMEMODES,
            new OpenBattleHudOptionSubmenuInitializer(
                CustomMessage.FromKey("GameModes_Menu_Title"),
                CustomMessage.FromKey("GameModes_Menu_Desc")));

        var enableLocalVS = new UICustomOptionsValueNode(ENABLE_IN_LOCALVS,
            new OnOffSpinBoxOptionInitializer(
                CustomMessage.FromKey("Mode_LocalVS_Title"),
                CustomMessage.FromKey("Mode_LocalVS_Desc"),
                ModConfig.Instance.AppliedGameModes.LocalVS,
                true));
        BindValueChange(enableLocalVS, val => ModConfig.Instance.AppliedGameModes.LocalVS = val == 0);

        var enableSimSim = new UICustomOptionsValueNode(ENABLE_IN_SIMSIM,
            new OnOffSpinBoxOptionInitializer(
                CustomMessage.FromKey("Mode_SimSim_Title"),
                CustomMessage.FromKey("Mode_SimSim_Desc"),
                ModConfig.Instance.AppliedGameModes.SimSimBattle,
                true));
        BindValueChange(enableSimSim, val => ModConfig.Instance.AppliedGameModes.SimSimBattle = val == 0);

        var enableRanked = new UICustomOptionsValueNode(ENABLE_IN_RANKED,
            new OnOffSpinBoxOptionInitializer(
                CustomMessage.FromKey("Mode_Ranked_Title"),
                CustomMessage.FromKey("Mode_Ranked_Desc"),
                ModConfig.Instance.AppliedGameModes.RankedMatch,
                true));
        BindValueChange(enableRanked, val => ModConfig.Instance.AppliedGameModes.RankedMatch = val == 0);

        var enableCasuals = new UICustomOptionsValueNode(ENABLE_IN_CASUALS,
            new OnOffSpinBoxOptionInitializer(
                CustomMessage.FromKey("Mode_Casual_Title"),
                CustomMessage.FromKey("Mode_Casual_Desc"),
                ModConfig.Instance.AppliedGameModes.CasualMatch,
                true));
        BindValueChange(enableCasuals, val => ModConfig.Instance.AppliedGameModes.CasualMatch = val == 0);

        var enableBattleHub = new UICustomOptionsValueNode(ENABLE_IN_BATTLEHUD,
            new OnOffSpinBoxOptionInitializer(
                CustomMessage.FromKey("Mode_BattleHub_Title"),
                CustomMessage.FromKey("Mode_BattleHub_Desc"),
                ModConfig.Instance.AppliedGameModes.BattleHubMatch,
                true));
        BindValueChange(enableBattleHub, val => ModConfig.Instance.AppliedGameModes.BattleHubMatch = val == 0);

        var enableCustom = new UICustomOptionsValueNode(ENABLE_IN_CUSTOM,
            new OnOffSpinBoxOptionInitializer(
                CustomMessage.FromKey("Mode_CustomRoom_Title"),
                CustomMessage.FromKey("Mode_CustomRoom_Desc"),
                ModConfig.Instance.AppliedGameModes.CustomRoomMatch,
                true));
        BindValueChange(enableCustom, val => ModConfig.Instance.AppliedGameModes.CustomRoomMatch = val == 0);

        openAppliedGameModesSubmenu.AddChildren([
            enableRanked,
            enableCasuals,
            enableCustom,
            enableBattleHub,
            enableLocalVS,
            enableSimSim
        ]);

        // left panel stuff
        var openLeftPanelSettings = new UICustomOptionsGroupNode(OPEN_LEFT_TRANSFORM,
            new OpenBattleHudOptionSubmenuInitializer(
                CustomMessage.FromKey("LeftPanel_Menu_Title"),
                CustomMessage.FromKey("LeftPanel_Menu_Desc")));

        var leftPositionX = new UICustomOptionsValueNode(LEFT_POSITION_X,
            new SliderOptionInitializer(
                CustomMessage.FromKey("LeftPanel_PosX_Title"),
                CustomMessage.FromKey("LeftPanel_PosX_Desc"),
                -50, 200, (int)ModConfig.Instance.LeftPanelTransform.OffsetX, 0));
        BindValueChange(leftPositionX, val => ModConfig.Instance.LeftPanelTransform.OffsetX = val);

        var leftPositionY = new UICustomOptionsValueNode(LEFT_POSITION_Y,
            new SliderOptionInitializer(
                CustomMessage.FromKey("LeftPanel_PosY_Title"),
                CustomMessage.FromKey("LeftPanel_PosY_Desc"),
                -200, 200, (int)ModConfig.Instance.LeftPanelTransform.OffsetY, 0));
        BindValueChange(leftPositionY, val => ModConfig.Instance.LeftPanelTransform.OffsetY = val);

        var leftScale = new UICustomOptionsValueNode(LEFT_SCALE,
            new SliderOptionInitializer(
                CustomMessage.FromKey("LeftPanel_Scale_Title"),
                CustomMessage.FromKey("LeftPanel_Scale_Desc"),
                10, 150, (int)(ModConfig.Instance.LeftPanelTransform.Scale * 100), 100));
        BindValueChange(leftScale, val => ModConfig.Instance.LeftPanelTransform.Scale = val / 100f);

        var leftMaxRows = new UICustomOptionsValueNode(LEFT_MAX_ROWS,
            new SliderOptionInitializer(
                CustomMessage.FromKey("LeftPanel_MaxRows_Title"),
                CustomMessage.FromKey("LeftPanel_MaxRows_Desc"),
                1, 19, Math.Clamp(ModConfig.Instance.LeftPanelTransform.MaxVisibleRows, 1, 19), 19));
        BindValueChange(leftMaxRows, val => ModConfig.Instance.LeftPanelTransform.MaxVisibleRows = val);

        var leftTransparency = new UICustomOptionsValueNode(LEFT_TRANSPARENCY,
            new SliderOptionInitializer(
                CustomMessage.FromKey("LeftPanel_Trans_Title"),
                CustomMessage.FromKey("LeftPanel_Trans_Desc"),
                1, 100, (int)(ModConfig.Instance.LeftPanelTransform.Transparency * 100), 100));
        BindValueChange(leftTransparency, val => ModConfig.Instance.LeftPanelTransform.Transparency = val / 100f);

        openLeftPanelSettings.AddChildren([leftPositionX, leftPositionY, leftScale, leftMaxRows, leftTransparency]);

        // right panel stuff
        var openRightPanelSettings = new UICustomOptionsGroupNode(OPEN_RIGHT_TRANSFORM,
            new OpenBattleHudOptionSubmenuInitializer(
                CustomMessage.FromKey("RightPanel_Menu_Title"),
                CustomMessage.FromKey("RightPanel_Menu_Desc")));

        // --- Right Panel ---
        var rightPositionX = new UICustomOptionsValueNode(RIGHT_POSITION_X,
            new SliderOptionInitializer(
                CustomMessage.FromKey("RightPanel_PosX_Title"),
                CustomMessage.FromKey("RightPanel_PosX_Desc"),
                -200, 50, (int)ModConfig.Instance.RightPanelTransform.OffsetX, 0));
        BindValueChange(rightPositionX, val => ModConfig.Instance.RightPanelTransform.OffsetX = val);

        var rightPositionY = new UICustomOptionsValueNode(RIGHT_POSITION_Y,
            new SliderOptionInitializer(
                CustomMessage.FromKey("RightPanel_PosY_Title"),
                CustomMessage.FromKey("RightPanel_PosY_Desc"),
                -200, 200, (int)ModConfig.Instance.RightPanelTransform.OffsetY, 0));
        BindValueChange(rightPositionY, val => ModConfig.Instance.RightPanelTransform.OffsetY = val);

        var rightScale = new UICustomOptionsValueNode(RIGHT_SCALE,
            new SliderOptionInitializer(
                CustomMessage.FromKey("RightPanel_Scale_Title"),
                CustomMessage.FromKey("RightPanel_Scale_Desc"),
                10, 150, (int)(ModConfig.Instance.RightPanelTransform.Scale * 100), 100));
        BindValueChange(rightScale, val => ModConfig.Instance.RightPanelTransform.Scale = val / 100f);

        var rightMaxRows = new UICustomOptionsValueNode(RIGHT_MAX_ROWS,
            new SliderOptionInitializer(
                CustomMessage.FromKey("RightPanel_MaxRows_Title"),
                CustomMessage.FromKey("RightPanel_MaxRows_Desc"),
                1, 19, Math.Clamp(ModConfig.Instance.RightPanelTransform.MaxVisibleRows, 1, 19), 19));
        BindValueChange(rightMaxRows, val => ModConfig.Instance.RightPanelTransform.MaxVisibleRows = val);

        var rightTransparency = new UICustomOptionsValueNode(RIGHT_TRANSPARENCY,
            new SliderOptionInitializer(
                CustomMessage.FromKey("RightPanel_Trans_Title"),
                CustomMessage.FromKey("RightPanel_Trans_Desc"),
                1, 100, (int)(ModConfig.Instance.RightPanelTransform.Transparency * 100), 100));
        BindValueChange(rightTransparency, val => ModConfig.Instance.RightPanelTransform.Transparency = val / 100f);

        openRightPanelSettings.AddChildren(
            [rightPositionX, rightPositionY, rightScale, rightMaxRows, rightTransparency]);

        openingInputHistorySettings.AddChildren([
            openAppliedGameModesSubmenu,
            displayPositionSpinBox,
            showCPUInputs,
            applyPositionToCPU,
            openLeftPanelSettings,
            openRightPanelSettings,
            showLineTexture
        ]);

        var appendToOptions = new OptionsAppendElementsRequest([(-1, openingInputHistorySettings)]);
        try
        {
            OptionsSettingsManager.ModifyUI(() =>
            {
                OptionsSettingsManager.RegisterModification([1], appendToOptions);
            });
        }
        catch (Exception e)
        {
            API.LogError($"Error setting options menu: {e.Message}");
        }

        _optionsModificationRequests.Add(appendToOptions);
    }

    public static void Unload()
    {
        API.LogInfo("Unloading InputHistory config options menu");
        try
        {
            OptionsSettingsManager.ModifyUI(() =>
            {
                foreach (var request in _optionsModificationRequests)
                    OptionsSettingsManager.UnregisterModification(request);
            });

            ulong a = 0;
            OnExitBGDialog(ref a);
        }
        catch (Exception e)
        {
            API.LogError($"Error unloading options menu: {e.Message}");
        }
    }

    [MethodHook(typeof(app.UIFlowOptionBGDialog.Param), "BeforeShowObject",
        MethodHookType.Pre)]
    public static PreHookResult OnEnterBGDialog(Span<ulong> args)
    {
        try
        {
            var paramMo = ManagedObject.FromAddress(args[1]);
        }
        catch (Exception e)
        {
            API.LogError($"Error setting options menu: {e.Message}");
        }

        _dummyInputHistory?.Dispose();
        _dummyInputHistory = new InputHistoryClass(true);

        return PreHookResult.Continue;
    }

    [MethodHook(typeof(app.UIFlowOptionBGDialog.Param), "BeforeHideObject", MethodHookType.Post)]
    public static void OnExitBGDialog(ref ulong retval)
    {
        ModConfig.Save();

        _dummyInputHistory?.Dispose();
        _dummyInputHistory = null;
    }

    [MethodHook(typeof(app.UIFlowOptionBGDialog.Param),
        nameof(app.UIFlowOptionBGDialog.Param.FlowEvent_OpenSelectedGroupPage), MethodHookType.Pre)]
    public static PreHookResult OnOpenSelectedGroupPage(Span<ulong> args)
    {
        if (_dummyInputHistory == null) return PreHookResult.Continue;

        try
        {
            var param = ManagedObject.ToManagedObject(args[1])?.As<app.UIFlowOptionBGDialog.Param>();
            var focusUnit = param?.GetFocusUnit();
            if (focusUnit?.Setting != null)
            {
                var typeId = focusUnit.Setting.TypeId;
                if (OptionsHelpers.TryGetOptionId(typeId, out var optionGuid))
                {
                    if (OPEN_LEFT_TRANSFORM == optionGuid)
                        _dummyInputHistory.SetPreviewForceMode(InputHistoryClass.PreviewForceMode.ForceLeft);
                    else if (OPEN_RIGHT_TRANSFORM == optionGuid)
                        _dummyInputHistory.SetPreviewForceMode(InputHistoryClass.PreviewForceMode.ForceRight);
                }
            }
        }
        catch (Exception e)
        {
            API.LogError($"Error in OnOpenSelectedGroupPage: {e.Message}");
        }

        return PreHookResult.Continue;
    }

    [MethodHook(typeof(app.UIFlowOptionBGDialog.Param), nameof(app.UIFlowOptionBGDialog.Param.FlowEvent_ReturnPage),
        MethodHookType.Post)]
    public static void OnReturnPage(ref ulong retval)
    {
        // When backing out of a submenu to the main mod settings page, restore normal DisplayPosition preview
        _dummyInputHistory?.SetPreviewForceMode(InputHistoryClass.PreviewForceMode.None);
    }
}