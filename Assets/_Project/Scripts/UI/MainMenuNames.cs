/// <summary>
/// Single source of truth for MainMenu.uxml element names (Phase 08, revised
/// composition in the Phase 08 Main Menu revision).
///
/// Runtime binding, the structural validator and the Play Mode suites all
/// reference these constants, so a rename fails loudly instead of silently
/// unbinding the menu.
///
/// Revision note: the old composition was "brand + navigation on the left, hero
/// planet on the right". The cover is now LEFT hero ship / RIGHT menu, so the
/// planet-only nodes (HeroPlanetDisc, HeroRingInner/Outer) are gone and the
/// fadeable <see cref="Layer"/> subtree plus the hero telemetry strip are new.
/// </summary>
public static class MainMenuNames
{
    // Layout shell.
    public const string Root = "MainMenuRoot";
    public const string Layer = "MenuLayer";
    public const string TopBar = "MenuTopBar";
    public const string Frame = "MenuFrame";

    /// <summary>RIGHT column: the menu itself.</summary>
    public const string ContentColumn = "MenuContentColumn";

    /// <summary>LEFT column: transparent window onto the 3D hero ship.</summary>
    public const string HeroColumn = "MenuHeroColumn";

    public const string Footer = "MenuFooter";

    // Brand block (inside the right column).
    public const string Eyebrow = "MenuEyebrow";
    public const string Title = "MainMenuTitle";
    public const string Subtitle = "MainMenuSubtitle";
    public const string Tagline = "MainMenuTagline";
    public const string Rule = "MenuRule";

    // Hero telemetry strip (left, under the ship).
    public const string HeroTelemetry = "HeroTelemetry";
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

    // Hero window (the 3D ship is behind it; only its chrome is UI).
    public const string HeroStage = "HeroStage";
    public const string HeroScanSweep = "HeroScanSweep";
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

    // Start cinematic blackout (EnterMission -> Ship Interior).
    public const string TransitionOverlay = "MenuTransitionOverlay";

    /// <summary>Every element the Main Menu runtime binds to.</summary>
    public static readonly string[] All =
    {
        Root,
        Layer,
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
        HeroTelemetry,
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
        HeroScanSweep,
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
    /// Composition elements that must keep their stable structure (checked by
    /// name and containment only, never by pixel values).
    /// </summary>
    public static readonly string[] Structure =
    {
        Root,
        Layer,
        TopBar,
        Frame,
        HeroColumn,
        ContentColumn,
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
        HeroTelemetry,
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
