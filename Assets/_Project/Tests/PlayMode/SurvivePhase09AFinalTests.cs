using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Phase 09A final polish: keyboard terminal input, single-timeline wrist motion,
/// early UI reveal and the frosted focus presentation.
///
/// The keyboard path is driven with a REAL Input System keyboard device and
/// queued state events - the same route a physical keyboard takes - so the
/// regression that made keyboard E unable to open a terminal is covered by the
/// real input route rather than by calling the controller directly.
/// </summary>
public class SurvivePhase09AFinalTests
{
    private const int MaxWaitFrames = 600;

    private Gamepad _pad;
    private Keyboard _keyboard;
    private readonly List<System.IDisposable> _leases = new List<System.IDisposable>();

    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;
        InputDeviceTracker.ResetToDefaults();
        AppQuit.ResetBookkeepingForAutomation();
        AppQuit.SuppressForAutomation = true;
        UiInput.ResetLayersForAutomation();

        GameSession session = GameSession.Instance;
        if (session != null)
        {
            Object.Destroy(session.gameObject);
        }

        _pad = InputSystem.AddDevice<Gamepad>();
        _keyboard = InputSystem.AddDevice<Keyboard>();
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;

        if (_keyboard != null && _keyboard.added)
        {
            InputSystem.RemoveDevice(_keyboard);
        }

        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _keyboard = null;
        _pad = null;
        UiInput.ResetLayersForAutomation();
        AppQuit.ResetBookkeepingForAutomation();
        InputDeviceTracker.ResetToDefaults();
    }

    // ====================================================================
    // PASS 1 - keyboard terminal
    // ====================================================================

    [UnityTest]
    public IEnumerator KeyboardE_OpensConsoleFocus_AndEscClosesItWithoutPause_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);

        // The physical mapping stays the authoritative table (nothing in the
        // suites names a key directly).
        Assert.Contains("Interact=Gamepad.buttonSouth,Keyboard.eKey",
            new List<string>(UiInput.BindingTable),
            "keyboard E is the physical Interact binding");

        yield return StandAtConsole(ship);
        Assert.IsTrue(ship.Interactor.HasValidTarget, "the gaze resolves the terminal");
        Assert.IsTrue(ship.Interactor.ResolvePrompt().Contains("[E]"),
            "the keyboard prompt offers the interaction ("
            + ship.Interactor.ResolvePrompt() + ")");
        Assert.IsFalse(ship.IsConsoleFocusActive, "no console view before the press");

        // ---- keyboard E (physical press injected: this harness cannot deliver a
        //      synthetic keyboard EDGE - see UiInput.InjectInteractForAutomation)
        yield return PressInteract();
        yield return WaitUntil(() => ship.IsConsoleFocusActive, 3f);

        Assert.IsTrue(ship.IsConsoleFocusActive,
            "keyboard E opens the console view (state=" + ship.Focus.State + ")");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "opening a terminal never pauses");
        yield return WaitUntil(() => ship.Focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus, 4f);
        Assert.IsFalse(ship.Player.InputEnabled, "the console view owns the input");

        // ---- keyboard Escape leaves it and does NOT pause ----
        yield return PressEscape();
        yield return WaitUntil(() => !ship.IsConsoleFocusActive, 4f);

        Assert.IsFalse(ship.IsConsoleFocusActive, "keyboard Esc leaves the console view");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "Esc on a console never opens the pause");
        Assert.AreEqual(UiInput.UiLayer.Gameplay, UiInput.ActiveBackLayer,
            "free first person owns Back again");
        yield return WaitUntil(() => ship.Player.InputEnabled, 3f);
        Assert.IsTrue(ship.Player.InputEnabled, "movement and look are restored");

        // ---- and Esc now means pause again ----
        yield return PressEscape();
        yield return WaitUntil(() => ship.PauseMenu.IsOpen, 3f);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "Esc in free first person is the pause");
        yield return PressEscape();
        yield return WaitUntil(() => !ship.PauseMenu.IsOpen, 3f);
        Assert.IsFalse(ship.PauseMenu.IsOpen, "Esc in the pause continues");
    }

    /// <summary>
    /// Regression guard for the reported defect: the SAME interact press that
    /// opens the console view used to be readable as that view's historical E exit
    /// during the entry transition (the interactor and the focus controller have
    /// no defined update order), so the view was entered and immediately
    /// cancelled. Gamepad A never hit it because A is not part of the exit
    /// binding.
    /// </summary>
    [UnityTest]
    public IEnumerator ConsoleEntry_IsNotCancelledByThePressThatStartedIt_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);
        yield return StandAtConsole(ship);

        UiInput.InjectInteractForAutomation();
        yield return null; // the entry frame: RequestEnter + the transition tick

        Assert.IsTrue(ship.Focus.FocusActive,
            "the entry press does not cancel its own transition (state="
            + ship.Focus.State + ")");
        Assert.AreNotEqual(ShipConsoleFocusController.ShipConsoleFocusState.TransitionOut,
            ship.Focus.State, "the view is not leaving again");

        yield return WaitUntil(() => ship.Focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus, 4f);
        Assert.AreEqual(ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus,
            ship.Focus.State, "the console view reaches its focused state");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "and never paused on the way in");
    }

    [UnityTest]
    public IEnumerator KeyboardE_WithoutATarget_DoesNothing_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);

        // Stand in the open, away from every interactable.
        ship.Player.TeleportTo(new Vector3(0f, 0f, 0f));
        ship.Player.SetFacing(180f, 0f);
        yield return null;
        yield return null;
        Physics.SyncTransforms();
        ship.Interactor.RefreshTarget();
        Assert.IsFalse(ship.Interactor.HasValidTarget,
            "nothing is in reach from the empty spot");

        yield return PressInteract();
        yield return null;
        yield return null;

        Assert.IsFalse(ship.IsConsoleFocusActive, "E without a target opens nothing");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "E never pauses");
        Assert.IsFalse(ship.IsOverlayOpen, "E never opens a device panel");
        Assert.AreEqual(0, AppQuit.RequestCount, "E never quits");
    }

    [UnityTest]
    public IEnumerator Gamepad_TerminalContract_Remains_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);

        yield return StandAtConsole(ship);

        yield return PressButton(GamepadButton.South);
        yield return WaitUntil(() => ship.IsConsoleFocusActive, 3f);
        Assert.IsTrue(ship.IsConsoleFocusActive, "A opens the console view");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "A never pauses");

        yield return PressButton(GamepadButton.East);
        yield return WaitUntil(() => !ship.IsConsoleFocusActive, 4f);
        Assert.IsFalse(ship.IsConsoleFocusActive, "B leaves the console view");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "B never pauses");
    }

    // ====================================================================
    // PASS 2 - one continuous raise timeline
    // ====================================================================

    [UnityTest]
    public IEnumerator WristRaise_IsOneContinuousTimeline_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);
        FirstPersonBlockArmsController arms = ship.Arms;

        yield return PressButton(GamepadButton.North);
        Assert.IsTrue(ship.IsWristOpen, "the wrist opens");

        var samples = new List<Vector3>();
        float started = Time.realtimeSinceStartup;
        while (arms.CurrentWristPhase == FirstPersonBlockArmsController.WristPhase.Raise
               && Time.realtimeSinceStartup - started < 4f)
        {
            samples.Add(new Vector3(arms.WristPresentationBlend,
                arms.PositionProgress, arms.RotationProgress));
            yield return null;
        }

        Assert.Greater(samples.Count, 5, "the raise was sampled over several frames");

        // Translation and rotation ride the same timeline: they start together and
        // never drift apart (this is what removes the two-stage feel).
        float worstGap = 0f;
        float previousBlend = -1f;
        float previousPosition = -1f;
        float previousRotation = -1f;
        foreach (Vector3 sample in samples)
        {
            worstGap = Mathf.Max(worstGap, Mathf.Abs(sample.y - sample.z));
            Assert.GreaterOrEqual(sample.x, previousBlend, "the blend never goes backwards");
            Assert.GreaterOrEqual(sample.y, previousPosition - 0.0001f,
                "position progress is monotonic");
            Assert.GreaterOrEqual(sample.z, previousRotation - 0.0001f,
                "rotation progress is monotonic");
            previousBlend = sample.x;
            previousPosition = sample.y;
            previousRotation = sample.z;
        }

        Assert.Less(worstGap, 0.02f,
            "position and rotation progress stay locked together (worst gap "
            + worstGap.ToString("0.000") + ")");

        // Both channels start moving on the very first sampled frame, not after a
        // first "lift" stage.
        Vector3 first = samples[0];
        Assert.Greater(first.y, 0f, "translation starts immediately");
        Assert.Greater(first.z, 0f, "rotation starts immediately");

        yield return WaitUntil(() => arms.CurrentWristPhase
            == FirstPersonBlockArmsController.WristPhase.Hold, 3f);
        Assert.AreEqual(1f, arms.PositionProgress, 0.001f, "translation finishes at hold");
        Assert.AreEqual(1f, arms.RotationProgress, 0.001f, "rotation finishes at hold");
    }

    [UnityTest]
    public IEnumerator WristRaise_TakesTheSpecifiedTime_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);
        FirstPersonBlockArmsController arms = ship.Arms;

        Assert.AreEqual(0.58f, FirstPersonBlockArmsController.RaiseDurationSeconds, 0.001f,
            "the raise is a 0.58 s move");

        yield return PressButton(GamepadButton.North);
        float start = Time.realtimeSinceStartup;
        yield return WaitUntil(() => arms.PositionProgress >= 0.999f, 4f);
        float elapsed = Time.realtimeSinceStartup - start;

        Assert.Greater(elapsed, 0.45f, "the raise is a real move, not a snap ("
            + elapsed.ToString("0.00") + "s)");
        Assert.Less(elapsed, 0.85f, "the raise is not sluggish ("
            + elapsed.ToString("0.00") + "s)");
    }

    // ====================================================================
    // PASS 3 - UI reveal starts before the raise ends
    // ====================================================================

    [UnityTest]
    public IEnumerator WristUiReveal_BeginsBeforeTheRaiseEnds_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);
        FirstPersonBlockArmsController arms = ship.Arms;

        Assert.AreEqual(0.82f, WristPresentationBridge.UiRevealNormalizedTime, 0.001f,
            "the hologram starts appearing at 82% of the raise");
        Assert.LessOrEqual(WristPresentationBridge.HoldSettleSeconds, 0.08f,
            "there is no visible wait between the arm arriving and the UI");

        yield return PressButton(GamepadButton.North);
        yield return WaitUntil(() => ship.IsWristTerminalRevealed, 4f);

        Assert.IsTrue(ship.IsWristTerminalRevealed, "the terminal is revealed");
        float progressAtReveal = ship.WristPresentation.LastRaiseProgress;
        Assert.Greater(progressAtReveal, 0.70f,
            "the reveal waited for the wrist to arrive ("
            + progressAtReveal.ToString("0.00") + ")");
        Assert.Less(progressAtReveal, 0.99f,
            "the reveal starts BEFORE the raise ends ("
            + progressAtReveal.ToString("0.00") + ")");
    }

    // ====================================================================
    // PASS 4 - frosted focus presentation
    // ====================================================================

    [UnityTest]
    public IEnumerator WristFocus_IsAFrostedCrossfade_NotABlackOut_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);
        FirstPersonBlockArmsController arms = ship.Arms;

        Assert.AreEqual(0f, arms.FocusBlend, 0.001f, "the arm starts crisp");
        Assert.AreEqual(1f, arms.PhysicalArmAlpha, 0.001f, "a crisp arm is fully opaque");

        yield return PressButton(GamepadButton.North);
        yield return WaitUntil(() => ship.IsWristTerminalRevealed, 4f);

        // The crossfade is a ramp, not a one-frame material step: capture the
        // blend while it moves.
        var ramp = new List<float>();
        float deadline = Time.realtimeSinceStartup + 2f;
        while (arms.FocusBlend < 0.999f && Time.realtimeSinceStartup < deadline)
        {
            ramp.Add(arms.FocusBlend);
            yield return null;
        }

        int intermediates = 0;
        foreach (float value in ramp)
        {
            if (value > 0.05f && value < 0.95f)
            {
                intermediates++;
            }
        }

        Assert.Greater(ramp.Count, 1, "the focus evolved over several frames");
        Assert.Greater(intermediates, 0,
            "the focus transition passes through intermediate states (crossfade)");
        Assert.AreEqual(1f, arms.FocusBlend, 0.01f, "the focus reaches full frost");

        // Never a black silhouette, and the cyan core survives.
        Assert.Greater(arms.PhysicalArmAlpha, 0.4f,
            "the frosted arm stays substantially visible (alpha "
            + arms.PhysicalArmAlpha.ToString("0.00") + ")");
        Assert.Greater(arms.PhysicalArmBrightness, 0.7f,
            "the frosted arm is not darkened into a silhouette (brightness "
            + arms.PhysicalArmBrightness.ToString("0.00") + ")");
        Assert.Less(arms.PhysicalArmDesaturation, 0.6f,
            "colour survives the frost (desaturation "
            + arms.PhysicalArmDesaturation.ToString("0.00") + ")");

        Color core = arms.WatchCoreEmission;
        Assert.Greater(core.g, 0.05f, "the cyan core keeps emitting (green "
            + core.g.ToString("0.00") + ")");
        Assert.Greater(core.b, core.r, "the core stays cyan/blue, not grey");

        // UI stays above the physical watch.
        Material displayMaterial = ship.Wrist.Display.SurfaceMaterial;
        Assert.IsNotNull(displayMaterial, "the hologram surface exists");
        Assert.Less(FirstPersonBlockArmsController.SoftenedArmRenderQueue,
            displayMaterial.renderQueue,
            "the frosted arm renders before the hologram");
    }

    // ====================================================================
    // PASS 4 - close: UI out, arm crisp again, then a single lower move
    // ====================================================================

    [UnityTest]
    public IEnumerator WristClose_FadesTheUiThenLowersInOneMove_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);
        FirstPersonBlockArmsController arms = ship.Arms;

        yield return PressButton(GamepadButton.North);
        yield return WaitUntil(() => ship.IsWristTerminalRevealed, 4f);
        yield return WaitUntil(() => arms.FocusBlend >= 0.999f, 3f);

        yield return PressButton(GamepadButton.North);
        Assert.IsFalse(ship.IsWristOpen, "the wrist closes");
        Assert.IsFalse(ship.IsWristTerminalRevealed, "the hologram is released first");

        // The arm fades back to crisp while it lowers (never a hard cut).
        yield return WaitUntil(() => arms.FocusBlend <= 0.001f, 3f);
        Assert.Less(arms.FocusBlend, 0.01f, "the arm is crisp again");
        Assert.AreEqual(1f, arms.PhysicalArmAlpha, 0.01f, "the opaque arm is restored");

        yield return WaitUntil(() => arms.CurrentWristPhase
            == FirstPersonBlockArmsController.WristPhase.Rest, 4f);
        Assert.AreEqual("Rest", arms.CurrentStateName, "the arm is back at rest");
        Assert.AreEqual(0f, arms.PositionProgress, 0.001f, "translation returned to rest");
        Assert.AreEqual(0f, arms.RotationProgress, 0.001f, "rotation returned to rest");
        Assert.IsTrue(ship.Player.InputEnabled, "exploration input is restored");
    }

    // ====================================================================
    // Helpers - real input path
    // ====================================================================

    private static IEnumerator StandAtConsole(ShipGameController ship)
    {
        ShipConsoleInteractable console = ship.Consoles[0];
        Vector3 dir = new Vector3(0f, 0f, -1f);
        ship.Player.TeleportTo(console.transform.position + dir * 1.7f);
        ship.Player.SetFacing(Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg, 0f);
        yield return null;
        yield return null;
        Physics.SyncTransforms();

        yield return WaitUntil(() =>
        {
            ship.Interactor.RefreshTarget();
            return ship.Interactor.HasValidTarget;
        }, 2f);
    }

    /// <summary>
    /// Interact press (keyboard E / Xbox A). The batch-mode harness cannot deliver
    /// a synthetic keyboard EDGE, so the physical press is injected while every
    /// layer below - the gaze-gated interactor, the console focus controller and
    /// the Back priority board - runs for real. Real queued gamepad events cover
    /// the device path in <see cref="Gamepad_TerminalContract_Remains_Green"/>.
    /// </summary>
    private static IEnumerator PressInteract()
    {
        UiInput.InjectInteractForAutomation();
        yield return null;
        yield return null;
        yield return null;
    }

    /// <summary>Escape press through the same documented seam.</summary>
    private static IEnumerator PressEscape()
    {
        UiInput.InjectEscapeForAutomation();
        yield return null;
        yield return null;
        yield return null;
    }

    private IEnumerator PressButton(GamepadButton button)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
    }

    private static IEnumerator EnterShip(System.Action<ShipGameController> onShip)
    {
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);
        yield return WaitUntilReal(
            () => SceneManager.GetActiveScene().name == GameSession.ShipInteriorSceneName, 15f);

        ShipGameController ship = null;
        yield return WaitUntilReal(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady && ship.Player != null && ship.Overlay != null;
        }, 25f);

        Assert.IsNotNull(ship, "the ship interior is ready");
        yield return WaitUntilReal(() => !ship.IsEntryInputLocked, 8f);
        Assert.IsFalse(ship.IsEntryInputLocked, "first-person control is available");
        Assert.IsTrue(ship.PauseMenu != null && ship.PauseMenu.IsReady,
            "the pause menu is ready");
        onShip(ship);
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition,
        float seconds = MaxWaitFrames / 60f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitUntilReal(System.Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }
}
