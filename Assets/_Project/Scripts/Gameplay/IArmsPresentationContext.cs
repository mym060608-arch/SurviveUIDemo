using UnityEngine;

/// <summary>
/// What the block-arm presentation needs from the environment it runs in.
///
/// <see cref="FirstPersonBlockArmsController"/> and
/// <see cref="WristPresentationBridge"/> are environment INDEPENDENT: the ship
/// interior and the planet surface both use the exact same geometry, rest /
/// walk behaviour, wrist raise / hold / lower and frosted presentation. What
/// differs is only the context around them, and that is what this interface
/// exposes:
///
///   * the player rig (and therefore the eye camera) the arms hang from,
///   * whether gameplay is frozen (pause),
///   * whether a modal mode is hiding first-person presentation
///     (ship: entry eyelids, console view, device panels),
///   * how to reveal the environment's own wrist terminal, which stays the
///     authoritative UI in both environments.
///
/// Nothing here reads input, owns wrist state or changes gameplay: the
/// environment keeps every decision, the arms only present it.
/// </summary>
public interface IArmsPresentationContext
{
    /// <summary>The authoritative first-person rig the arms belong to.</summary>
    SurfacePlayerController Player { get; }

    /// <summary>True while gameplay is frozen (pause menu).</summary>
    bool IsPaused { get; }

    /// <summary>
    /// True while a mode needs an unobstructed view and the arms must be hidden
    /// (ship: entry eyelids, console view, device panels; surface: never).
    /// </summary>
    bool HidesFirstPersonPresentation { get; }

    /// <summary>
    /// Shows or hides this environment's own wrist terminal. The environment
    /// owns the terminal; the presentation only decides WHEN the raise has
    /// arrived far enough for it to appear.
    /// </summary>
    void SetWristTerminalVisible(bool visible);
}
