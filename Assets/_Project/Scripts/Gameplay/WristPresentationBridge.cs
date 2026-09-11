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
    /// Fraction of the Wrist_Raise clip (0.800 s) after which the existing
    /// terminal UI fades in. The HANDOFF sweet spot is 70-90% of the motion;
    /// 0.78 puts the reveal just before the pose settles, so the 0.2 s display
    /// pop the Phase 06 chain already performs lands together with the arm.
    /// </summary>
    public const float UiRevealNormalizedTime = 0.78f;

    /// <summary>
    /// Safety net: if the Animator never reports the raise state (for example
    /// a controller asset without the wrist states, or a broken transition),
    /// the terminal still reveals so the wrist can never become unreachable.
    /// </summary>
    public const float RaiseRevealTimeoutSeconds = 1.25f;

    /// <summary>
    /// Seconds the arm mount takes to move from the rest framing to the asset
    /// framing once the terminal is presented, and back again while the wrist
    /// lowers. Both transitions happen while the arm is on screen, so they are
    /// deliberately slow enough to read as the arm settling into (or leaving)
    /// the reading position - see FirstPersonArmsController.RestPitchDegrees for
    /// why the rest and wrist presentations need different framing.
    /// </summary>
    [SerializeField]
    private float holdSettleSeconds = 0.45f;

    [SerializeField]
    private float lowerRestoreSeconds = 0.45f;

    private ShipGameController _ship;
    private FirstPersonArmsController _arms;
    private SurfaceWristController _wrist;

    private bool _awaitingReveal;
    private float _awaitElapsed;
    private bool _lowerStateSeen;

    // Framing blend towards the wrist presentation (1 = asset framing).
    private float _framingBlend = 1f;

    /// <summary>Current framing blend (0 = rest pose framing, 1 = asset framing).</summary>
    public float FramingBlend => _framingBlend;

    /// <summary>True while the terminal is held back until the arm arrives.</summary>
    public bool AwaitingUiReveal => _awaitingReveal;

    /// <summary>Raise animation progress at the last tick (-1 when not raising).</summary>
    public float LastRaiseProgress { get; private set; } = -1f;

    /// <summary>Reason the last reveal fired (diagnostics / tests).</summary>
    public string LastRevealReason { get; private set; } = "<never>";

    public void Configure(ShipGameController ship, FirstPersonArmsController arms,
        SurfaceWristController wrist)
    {
        _ship = ship;
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

        _arms.SetWristPhase(FirstPersonArmsController.WristPhase.Raise);
        _awaitingReveal = true;
        _awaitElapsed = 0f;
        _lowerStateSeen = false;
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
        if (_arms != null)
        {
            _arms.SetWristPhase(FirstPersonArmsController.WristPhase.Lower);
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
        UpdateFraming();
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
        if (!arrived && !timedOut)
        {
            return;
        }

        _awaitingReveal = false;
        LastRevealReason = arrived
            ? "raise reached " + progress.ToString("0.00")
            : "raise timeout after " + _awaitElapsed.ToString("0.00") + "s";
        if (_ship != null)
        {
            _ship.RevealWristTerminal(true);
        }

        // Re-evaluate the screens in the SAME frame as the reveal, so the device
        // lights up together with the terminal instead of one frame later.
        UpdateWristScreen();

        _arms.SetWristPhase(FirstPersonArmsController.WristPhase.Hold);
    }

    /// <summary>
    /// Keeps the arm mount framed for the state that is actually being shown:
    ///
    ///   Rest / Raise  -> rest framing (the arm is visible in the lower frame
    ///                    and the raise plays as a real rise towards the eye),
    ///   Hold          -> eases to the asset's own framing, i.e. exactly the
    ///                    composition the frozen Wrist_Idle pose and the existing
    ///                    wrist UI were validated with,
    ///   Lower         -> eases back to the rest framing, so the arm finishes
    ///                    its descent already in the ordinary idle composition
    ///                    instead of popping when the clip ends.
    ///
    /// The blend is presentation only: clips, rig and asset framing are intact.
    /// </summary>
    private void UpdateFraming()
    {
        FirstPersonArmsController.WristPhase phase = _arms.CurrentWristPhase;
        float target;
        float duration;
        if (phase == FirstPersonArmsController.WristPhase.Hold)
        {
            target = 1f;
            duration = holdSettleSeconds;
        }
        else if (phase == FirstPersonArmsController.WristPhase.Lower)
        {
            target = 0f;
            duration = lowerRestoreSeconds;
        }
        else
        {
            // Rest, and the raise (which must keep the idle framing so the rise
            // is visible on screen).
            target = 0f;
            duration = 0.2f;
        }

        if (Mathf.Abs(_framingBlend - target) < 0.0005f)
        {
            _framingBlend = target;
            return;
        }

        float step = duration > 0.001f ? Time.unscaledDeltaTime / duration : 1f;
        _framingBlend = Mathf.MoveTowards(_framingBlend, target, step);
        _arms.SetFramingBlend(_framingBlend);
    }

    /// <summary>
    /// Arms visibility per game mode (contract §15 / §42 / §43): the entry
    /// eyelids, the console view and the shipboard device panels all need an
    /// unobstructed screen, so the arms are hidden for exactly those modes and
    /// restored afterwards. Pause never hides them - it freezes them.
    /// </summary>
    private void UpdateVisibility()
    {
        bool hiddenByMode = _ship != null
                            && (_ship.IsEntryInputLocked
                                || _ship.IsConsoleFocusActive
                                || _ship.IsOverlayOpen);
        _arms.SetVisible(!hiddenByMode);
    }

    /// <summary>
    /// Cyan wrist screens follow the EXISTING terminal presentation: dim while
    /// the terminal is closed, brighter once it is presented (contract §33).
    /// </summary>
    private void UpdateWristScreen()
    {
        bool presented = _wrist != null && _wrist.Display != null && _wrist.Display.Visible;
        _arms.SetWristScreenActive(presented);
    }

    /// <summary>
    /// Returns the presentation phase to Rest once the Wrist_Lower state has
    /// played out and the animator is back on Idle / Walk, so the next open
    /// starts from a clean state without any snap.
    /// </summary>
    private void UpdateWristPhaseLifetime()
    {
        FirstPersonArmsController.WristPhase phase = _arms.CurrentWristPhase;
        if (phase != FirstPersonArmsController.WristPhase.Lower)
        {
            return;
        }

        string state = _arms.CurrentStateName;
        if (state == FirstPersonArmsController.StateWristLower)
        {
            _lowerStateSeen = true;
            return;
        }

        if (!_lowerStateSeen)
        {
            return; // the lower transition has not started yet
        }

        _arms.SetWristPhase(FirstPersonArmsController.WristPhase.Rest);
        _lowerStateSeen = false;
    }
}
