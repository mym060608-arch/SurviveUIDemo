using UnityEngine;

/// <summary>
/// Wrist presentation bridge (Phase 09A_2, contract §27 / §28).
///
///     existing Wrist authority state
///             |
///             v
///     first-person arms animation
///             |
///             v
///     existing Wrist UI reveal timing
///
/// Ownership is deliberately asymmetric:
///
///   * <see cref="ShipGameController"/> stays the AUTHORITY - it decides
///     whether the wrist is open, who owns the pointer, who owns focus and
///     whether the player can move. None of that is duplicated here.
///   * this bridge only decides WHEN the already-authoritative terminal
///     becomes visible, so the left wrist really rises first and the existing
///     wrist UI appears as the arm arrives (contract §29) instead of popping
///     on the key press.
///
/// The bridge is OPTIONAL by construction: with no arms instance (or this
/// component disabled) <see cref="ShipGameController.SetWristOpen"/> reveals
/// the terminal immediately, i.e. exactly the validated Phase 08 behaviour.
/// The existing wrist system therefore never depends on the arms to work
/// (contract §58).
/// </summary>
[DisallowMultipleComponent]
public class WristPresentationBridge : MonoBehaviour
{
    /// <summary>
    /// Fraction of the raise after which the existing terminal UI starts to fade
    /// in.
    ///
    /// Phase 09A_3 final polish: 0.82 - the hologram begins to appear while the
    /// wrist is still arriving, so the motion and the UI finish TOGETHER instead
    /// of "arm arrives, wait, then UI". The arm is already close to the final
    /// pose by then (the raise eases out), so the device reads as the surface the
    /// interface unfolds from.
    /// </summary>
    public const float UiRevealNormalizedTime = 0.82f;

    /// <summary>
    /// Safety net: if the presentation never reports the raise state, the
    /// terminal still reveals so the wrist can never become unreachable. It sits
    /// above raise + hold beat (0.58 s + 0.04 s) so it can never cut the staged
    /// presentation short.
    /// </summary>
    public const float RaiseRevealTimeoutSeconds = 1.6f;

    /// <summary>
    /// Tiny beat between the arm reaching the hold pose and the last of the
    /// hologram fade-in. The reveal itself already starts at 0.82 of the raise,
    /// so this only keeps the very end of the two motions aligned instead of
    /// adding a visible pause (the old 0.30 s wait is gone).
    /// </summary>
    public const float HoldSettleSeconds = 0.04f;

    private IArmsPresentationContext _context;
    private FirstPersonBlockArmsController _arms;
    private SurfaceWristController _wrist;

    private bool _awaitingReveal;
    private float _awaitElapsed;
    private bool _lowerStateSeen;
    private float _settleStart = -1f;

    /// <summary>True while the terminal is held back until the arm arrives.</summary>
    public bool AwaitingUiReveal => _awaitingReveal;

    /// <summary>Raise animation progress at the last tick (-1 when not raising).</summary>
    public float LastRaiseProgress { get; private set; } = -1f;

    /// <summary>
    /// Raise progress at the moment the hologram was revealed. This is the number
    /// that proves the reveal starts BEFORE the raise ends; LastRaiseProgress
    /// itself already reads -1 once the presentation moved on to the hold phase.
    /// </summary>
    public float LastRevealProgress { get; private set; } = -1f;

    /// <summary>Reason the last reveal fired (diagnostics / tests).</summary>
    public string LastRevealReason { get; private set; } = "<never>";

    /// <summary>
    /// Wires the presentation to one environment. The context (ship interior or
    /// planet surface) supplies the player rig, the pause state, the modal-
    /// presentation state and its own wrist terminal; the arms and this bridge
    /// stay environment independent.
    /// </summary>
    public void Configure(IArmsPresentationContext context,
        FirstPersonBlockArmsController arms, SurfaceWristController wrist)
    {
        _context = context;
        _arms = arms;
        _wrist = wrist;
    }

    /// <summary>
    /// Called by the authority when a wrist open was requested. Returns true
    /// when the reveal was deferred to the raise animation (false = the caller
    /// must reveal the terminal immediately).
    /// </summary>
    public bool BeginWristRaise()
    {
        if (_arms == null)
        {
            return false;
        }

        _arms.SetWristPhase(FirstPersonBlockArmsController.WristPhase.Raise);
        _awaitingReveal = true;
        _awaitElapsed = 0f;
        _lowerStateSeen = false;
        _settleStart = -1f;
        LastRevealReason = "<pending>";
        return true;
    }

    /// <summary>
    /// Called by the authority when the wrist was closed. The existing terminal
    /// has already stopped taking input and released focus by then (the close
    /// path hides it synchronously), so the arm only has to come back down.
    /// </summary>
    public void BeginWristLower()
    {
        _awaitingReveal = false;
        LastRaiseProgress = -1f;
        LastRevealReason = "<cancelled by close>";
        _lowerStateSeen = false;
        _settleStart = -1f;
        if (_arms != null)
        {
            _arms.SetWristPhase(FirstPersonBlockArmsController.WristPhase.Lower);
        }
    }

    /// <summary>
    /// Per-frame presentation tick, driven by the ship coordinator so update
    /// order is never a question (the bridge consumes state, it never polls a
    /// device).
    /// </summary>
    public void Tick(bool paused)
    {
        if (_arms == null)
        {
            return;
        }

        if (paused)
        {
            // Freeze with the rest of the gameplay context: no reveal, no
            // parameter changes, no state pushes (contract §44).
            _arms.SetFrozen(true);
            return;
        }

        _arms.SetFrozen(false);
        UpdateVisibility();
        UpdateWristScreen();
        UpdateWristPhaseLifetime();

        if (!_awaitingReveal)
        {
            LastRaiseProgress = -1f;
            return;
        }

        _awaitElapsed += Time.unscaledDeltaTime;
        float progress = _arms.WristRaiseProgress;
        LastRaiseProgress = progress;
        bool arrived = progress >= UiRevealNormalizedTime;
        bool timedOut = _awaitElapsed >= RaiseRevealTimeoutSeconds;

        // Stage A ends with the arm HELD crisp in the horizontal pose: give that
        // pose a short beat before the hologram appears over it (Stage B).
        if (arrived && _settleStart < 0f)
        {
            _settleStart = Time.unscaledTime;
        }

        bool settled = _settleStart >= 0f
                       && Time.unscaledTime - _settleStart >= HoldSettleSeconds;
        if ((!arrived || !settled) && !timedOut)
        {
            return;
        }

        _awaitingReveal = false;
        LastRevealProgress = progress;
        LastRevealReason = arrived
            ? "raise reached " + progress.ToString("0.00")
            : "raise timeout after " + _awaitElapsed.ToString("0.00") + "s";
        if (_context != null)
        {
            _context.SetWristTerminalVisible(true);
        }

        // Re-evaluate the screens in the SAME frame as the reveal, so the device
        // lights up together with the terminal instead of one frame later.
        UpdateWristScreen();

        _arms.SetWristPhase(FirstPersonBlockArmsController.WristPhase.Hold);
    }

    /// <summary>
    /// Two-stage presentation (Phase 09A_3 final):
    ///
    ///   Stage A - Raise: the arm is crisp and the player watches it come up; the
    ///           hologram is not on screen yet.
    ///   Stage B - Focus / Hold: the terminal is presented, so the UI becomes the
    ///           subject and the physical arm drops to a translucent ghost
    ///           (softened / desaturated / no emissive accents).
    ///
    /// The softening follows the HOLOGRAM being visible, so closing restores the
    /// crisp arm immediately (the terminal is released first) and only then does
    /// the arm lower. Presentation only - the UI layer above stays sharp and is
    /// never touched.
    /// </summary>
    private void UpdateWristScreen()
    {
        bool presented = _wrist != null && _wrist.Display != null && _wrist.Display.Visible;
        _arms.SetWristScreenActive(_arms.CurrentWristPhase != FirstPersonBlockArmsController.WristPhase.Rest);
        _arms.SetSoftened(presented);

        // The hologram itself becomes the frosted lens while the focus is on it:
        // inside the map disc the sheet lets the physical wrist read through as a
        // soft cyan glow, and it follows the same crossfade as the arm so the two
        // never step in one frame.
        if (_wrist != null && _wrist.Display != null)
        {
            _wrist.Display.SetFrost(_arms.FocusBlend);
        }
    }

    /// <summary>
    /// Arms visibility per game mode (contract §15 / §42 / §43): the entry
    /// eyelids, the console view and the shipboard device panels all need an
    /// unobstructed screen, so the arms are hidden for exactly those modes and
    /// restored afterwards. Pause never hides them - it freezes them.
    /// </summary>
    private void UpdateVisibility()
    {
        bool hiddenByMode = _context != null && _context.HidesFirstPersonPresentation;
        _arms.SetVisible(!hiddenByMode);
    }

    /// <summary>
    /// Returns the presentation phase to Rest once the lower blend has finished
    /// and the arm is hanging again, so the next open starts from a clean state
    /// without any snap.
    /// </summary>
    private void UpdateWristPhaseLifetime()
    {
        FirstPersonBlockArmsController.WristPhase phase = _arms.CurrentWristPhase;
        if (phase != FirstPersonBlockArmsController.WristPhase.Lower
            && phase != FirstPersonBlockArmsController.WristPhase.Hold)
        {
            return;
        }

        // The block arms report the phase itself: Hold holds, Lower runs down to
        // zero, and Rest resumes automatic movement presentation.
        if (phase == FirstPersonBlockArmsController.WristPhase.Lower)
        {
            _lowerStateSeen = true;
        }

        if (!_lowerStateSeen)
        {
            return;
        }

        if (_arms.WristPresentationBlend > 0.001f)
        {
            return;
        }

        _arms.SetWristPhase(FirstPersonBlockArmsController.WristPhase.Rest);
        _lowerStateSeen = false;
    }
}
