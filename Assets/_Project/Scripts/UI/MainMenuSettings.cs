/// <summary>
/// Phase 08 presentation settings for the Main Menu.
///
/// Scope discipline (Phase 08 §16): this is PRESENTATION state only. It is not
/// gameplay state, it never touches GameSession, and it deliberately has no
/// save framework - the values live for the process lifetime and reset to their
/// defaults on launch, exactly like the rest of the demo's presentation.
///
/// Every switch here drives something that really exists:
/// - <see cref="MotionEnabled"/> pauses the Main Menu's own motion (hero scan
///   sweep, status pulse, modal/transition timing) AND the shared rotating 3D
///   planet preview spin; the layout never changes.
/// - <see cref="HintsVisible"/> hides the peripheral technical annotation
///   layer only; every primary element stays visible and functional.
/// </summary>
public static class MainMenuSettings
{
    /// <summary>Main Menu -> Overview fade duration in seconds (Phase 08 §26).</summary>
    public const float TransitionSeconds = 0.18f;

    /// <summary>Modal open/close animation duration in seconds.</summary>
    public const float ModalSeconds = 0.14f;

    /// <summary>True while ambient motion is allowed.</summary>
    public static bool MotionEnabled { get; private set; } = true;

    /// <summary>True while peripheral technical annotations are drawn.</summary>
    public static bool HintsVisible { get; private set; } = true;

    /// <summary>Applies the motion preference to the menu and the shared planet rig.</summary>
    public static void SetMotion(bool enabled)
    {
        MotionEnabled = enabled;
        // The planet preview is the same authoritative rig the Overview uses;
        // pausing it is a presentation choice, never a lifecycle change.
        OverviewPlanetPreview.MotionEnabled = enabled;
    }

    /// <summary>Applies the peripheral-annotation preference.</summary>
    public static void SetHints(bool visible)
    {
        HintsVisible = visible;
    }

    /// <summary>
    /// Restores launch defaults. Used by automated validation so a suite that
    /// exercises the switches can never leak state into another suite.
    /// </summary>
    public static void ResetToDefaults()
    {
        SetMotion(true);
        SetHints(true);
    }
}
