/// <summary>
/// Single source of truth for MainMenu.uxml element names (Phase 08).
///
/// Runtime binding, the structural validator and the Play Mode smoke / shot
/// suites all reference these constants, so a rename fails loudly instead of
/// silently unbinding the menu.
/// </summary>
public static class MainMenuNames
{
    // Layout shell.
    public const string Root = "MainMenuRoot";
    public const string TopBar = "MenuTopBar";
    public const string Frame = "MenuFrame";
    public const string ContentColumn = "MenuContentColumn";
    public const string HeroColumn = "MenuHeroColumn";
    public const string Footer = "MenuFooter";

    // Brand block.
    public const string Eyebrow = "MenuEyebrow";
    public const string Title = "MainMenuTitle";
    public const string Subtitle = "MainMenuSubtitle";
    public const string Tagline = "MainMenuTagline";
    public const string Rule = "MenuRule";
    public const string MissionCodeValue = "MissionCodeValue";
    public const string SectorCodeValue = "SectorCodeValue";

    // Primary CTA (the only primary action).
    public const string EnterMissionButton = "EnterMissionButton";
    public const string EnterMissionLabel = "EnterMissionLabel";
    public const string EnterMissionSubLabel = "EnterMissionSubLabel";
    public const string EnterMissionMicro = "EnterMissionMicro";

    // Secondary navigation.
    public const string SecondaryNav = "SecondaryNav";
    public const string SettingsButton = "SettingsButton";
    public const string CreditsButton = "CreditsButton";
    public const string ExitButton = "ExitButton";

    // Hero visual (shared rotating 3D planet preview + orbital framing).
    public const string HeroStage = "HeroStage";
    public const string HeroPlanetDisc = "HeroPlanetDisc";
    public const string HeroScanSweep = "HeroScanSweep";
    public const string HeroRingInner = "HeroRingInner";
    public const string HeroRingOuter = "HeroRingOuter";
    public const string HeroRailLeft = "HeroRailLeft";
    public const string HeroRailRight = "HeroRailRight";
    public const string HintLayer = "MenuHintLayer";

    // Peripheral metadata.
    public const string BuildStampValue = "BuildStampValue";
    public const string FooterSystemValue = "FooterSystemValue";
    public const string StatusDot = "MenuStatusDot";

    // Settings modal.
    public const string SettingsModal = "SettingsModal";
    public const string SettingsDim = "SettingsDim";
    public const string SettingsPanel = "SettingsPanel";
    public const string SettingsCloseButton = "SettingsCloseButton";
    public const string MotionOnButton = "MotionOnButton";
    public const string MotionOffButton = "MotionOffButton";
    public const string HintsShowButton = "HintsShowButton";
    public const string HintsHideButton = "HintsHideButton";

    // Credits modal.
    public const string CreditsModal = "CreditsModal";
    public const string CreditsDim = "CreditsDim";
    public const string CreditsPanel = "CreditsPanel";
    public const string CreditsCloseButton = "CreditsCloseButton";

    // Transition overlay (Main Menu -> Overview fade).
    public const string TransitionOverlay = "MenuTransitionOverlay";

    /// <summary>Every element the Main Menu runtime binds to.</summary>
    public static readonly string[] All =
    {
        Root,
        TopBar,
        Frame,
        ContentColumn,
        HeroColumn,
        Footer,
        Eyebrow,
        Title,
        Subtitle,
        Tagline,
        Rule,
        MissionCodeValue,
        SectorCodeValue,
        EnterMissionButton,
        EnterMissionLabel,
        EnterMissionSubLabel,
        EnterMissionMicro,
        SecondaryNav,
        SettingsButton,
        CreditsButton,
        ExitButton,
        HeroStage,
        HeroPlanetDisc,
        HeroScanSweep,
        HeroRingInner,
        HeroRingOuter,
        HeroRailLeft,
        HeroRailRight,
        HintLayer,
        BuildStampValue,
        FooterSystemValue,
        StatusDot,
        SettingsModal,
        SettingsDim,
        SettingsPanel,
        SettingsCloseButton,
        MotionOnButton,
        MotionOffButton,
        HintsShowButton,
        HintsHideButton,
        CreditsModal,
        CreditsDim,
        CreditsPanel,
        CreditsCloseButton,
        TransitionOverlay
    };

    /// <summary>
    /// Phase 08 composition elements that must keep their stable structure
    /// (checked by name only, never by pixel values).
    /// </summary>
    public static readonly string[] Structure =
    {
        Root,
        TopBar,
        Frame,
        ContentColumn,
        HeroColumn,
        Footer,
        Title,
        Subtitle,
        MissionCodeValue,
        SectorCodeValue,
        EnterMissionButton,
        SecondaryNav,
        SettingsButton,
        CreditsButton,
        ExitButton,
        HeroStage,
        HeroPlanetDisc,
        HeroRingInner,
        HeroRingOuter,
        HeroRailLeft,
        HeroRailRight,
        SettingsModal,
        SettingsPanel,
        MotionOnButton,
        MotionOffButton,
        HintsShowButton,
        HintsHideButton,
        CreditsModal,
        CreditsPanel
    };
}
