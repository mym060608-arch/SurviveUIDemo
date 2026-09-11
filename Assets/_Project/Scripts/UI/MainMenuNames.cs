/// <summary>
/// Single source of truth for MainMenu.uxml element names (Phase 08, Revision 2
/// title-screen composition).
///
/// Runtime binding, the structural validator and the Play Mode suites all
/// reference these constants, so a rename fails loudly instead of silently
/// unbinding the menu.
///
/// Revision 2 note: the cover is now a minimal console title screen - 活下去
/// plus 新游戏 / 继续游戏 / 选项 / 退出 - so the previous chrome (top bar,
/// footer, telemetry strip, hint rails, scan sweep, corner brackets) and the
/// 制作信息 modal are gone on purpose.
/// </summary>
public static class MainMenuNames
{
    // Layout shell.
    public const string Root = "MainMenuRoot";
    public const string Layer = "MenuLayer";

    /// <summary>LEFT: transparent window onto the 3D hero ship.</summary>
    public const string HeroColumn = "MenuHeroColumn";

    /// <summary>RIGHT: title + menu.</summary>
    public const string ContentColumn = "MenuContentColumn";

    // Title (the only headline on the screen).
    public const string Title = "MainMenuTitle";

    // The four title-screen options.
    public const string MenuList = "MenuList";
    public const string NewGameButton = "NewGameButton";
    public const string NewGameLabel = "NewGameLabel";
    public const string ContinueButton = "ContinueButton";
    public const string ContinueLabel = "ContinueLabel";
    public const string OptionsButton = "OptionsButton";
    public const string OptionsLabel = "OptionsLabel";
    public const string ExitButton = "ExitButton";
    public const string ExitLabel = "ExitLabel";

    /// <summary>Device-aware controller hint (empty for mouse/keyboard).</summary>
    public const string InputHintLabel = "InputHintLabel";

    // Options modal (the Phase 08 settings, simplified).
    public const string OptionsModal = "OptionsModal";
    public const string OptionsDim = "OptionsDim";
    public const string OptionsPanel = "OptionsPanel";
    public const string OptionsTitle = "OptionsTitle";
    public const string OptionsCloseButton = "OptionsCloseButton";
    public const string MotionOnButton = "MotionOnButton";
    public const string MotionOffButton = "MotionOffButton";
    public const string HintsShowButton = "HintsShowButton";
    public const string HintsHideButton = "HintsHideButton";

    // Start cinematic blackout (新游戏 / 继续游戏 -> Ship Interior).
    public const string TransitionOverlay = "MenuTransitionOverlay";

    /// <summary>Every element the Main Menu runtime binds to.</summary>
    public static readonly string[] All =
    {
        Root,
        Layer,
        HeroColumn,
        ContentColumn,
        Title,
        MenuList,
        NewGameButton,
        NewGameLabel,
        ContinueButton,
        ContinueLabel,
        OptionsButton,
        OptionsLabel,
        ExitButton,
        ExitLabel,
        InputHintLabel,
        OptionsModal,
        OptionsDim,
        OptionsPanel,
        OptionsTitle,
        OptionsCloseButton,
        MotionOnButton,
        MotionOffButton,
        HintsShowButton,
        HintsHideButton,
        TransitionOverlay
    };

    /// <summary>
    /// Composition elements that must keep their stable structure (checked by
    /// name and containment only, never by pixel values).
    /// </summary>
    public static readonly string[] Structure =
    {
        Root,
        Layer,
        HeroColumn,
        ContentColumn,
        Title,
        MenuList,
        NewGameButton,
        ContinueButton,
        OptionsButton,
        ExitButton,
        OptionsModal,
        OptionsPanel,
        MotionOnButton,
        MotionOffButton,
        HintsShowButton,
        HintsHideButton
    };

    /// <summary>
    /// The four title-screen options, in navigation order (top to bottom).
    /// </summary>
    public static readonly string[] MenuItemOrder =
    {
        NewGameButton,
        ContinueButton,
        OptionsButton,
        ExitButton
    };

    /// <summary>
    /// Copy that must NOT appear on the title screen any more (Revision 2 §3):
    /// the previous cover's HUD, telemetry and system text.
    /// </summary>
    public static readonly string[] RemovedCopy =
    {
        "进入任务",
        "制作信息",
        "系统设置",
        "SURVIVE // DRIFT",
        "边境生存协议",
        "深空前哨生存与生产控制系统",
        "FRONTIER OPERATIONS INTERFACE",
        "HULL SURVEY",
        "FLIGHT DECK",
        "MISSION READY",
        "BUILD // PHASE 08",
        "SYSTEM // READY",
        "FRONTIER-08",
        "N-04",
        "ARK-07"
    };
}
