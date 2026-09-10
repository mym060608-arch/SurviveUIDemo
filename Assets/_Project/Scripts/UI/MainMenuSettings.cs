/// <summary>
/// Phase 08 presentation settings for the Main Menu.
///
/// Scope discipline (Phase 08 §16): this is PRESENTATION state only. It is not
/// gameplay state, it never touches GameSession, and it deliberately has no
/// save framework - the values live for the process lifetime and reset to their
/// defaults on launch, exactly like the rest of the demo's presentation.
///
/// Every switch here drives something that really exists:
/// - <see cref="MotionEnabled"/> pauses the cover's motion (hero ship bob/sway,
///   thruster flicker, starfield parallax, scan sweep, status pulse, modal and
///   transition timing); the layout never changes and the ship, the plumes and
///   the starfield all stay on screen.
/// - <see cref="HintsVisible"/> hides the peripheral technical annotation
///   layer only; every primary element stays visible and functional.
/// </summary>
public static class MainMenuSettings
{
    /// <summary>Fallback cover fade duration in seconds (used only when the
    /// scene has no MainMenuCinematic).</summary>
    public const float TransitionSeconds = 0.18f;

    /// <summary>Modal open/close animation duration in seconds.</summary>
    public const float ModalSeconds = 0.14f;

    /// <summary>True while ambient motion is allowed.</summary>
    public static bool MotionEnabled { get; private set; } = true;

    /// <summary>True while peripheral technical annotations are drawn.</summary>
    public static bool HintsVisible { get; private set; } = true;

    /// <summary>
    /// Applies the motion preference. It is read by the menu, the hero ship and
    /// the starfield every frame; nothing else is coupled to it (the shared
    /// Overview planet rig is NOT a Main Menu element any more, so this setting
    /// no longer reaches it).
    /// </summary>
    public static void SetMotion(bool enabled)
    {
        MotionEnabled = enabled;
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
