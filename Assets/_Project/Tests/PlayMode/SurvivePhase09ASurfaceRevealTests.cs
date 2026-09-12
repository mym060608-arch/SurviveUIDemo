using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Phase 09A_5 - the surface wrist / map REVEAL TIMING contract.
///
/// The surface used to reveal its terminal on the input that opened it and only
/// then start the arm raise, so the hologram popped in one frame and the motion
/// happened afterwards (the recorded acceptance bug). It now runs the same
/// presentation timing as the ship interior: the authority opens, the arm raises,
/// and WristPresentationBridge reveals the terminal once the raise has brought the
/// wrist into view.
///
/// These rows sample the per-frame sequence instead of looking at the end state,
/// because the defect was purely temporal: raise progress and UI visibility had to
/// be recorded together to see it. X (buttonWest / map) and Y (buttonNorth /
/// wrist) both go through the same route, so both are sampled.
/// </summary>
public class SurvivePhase09ASurfaceRevealTests
{
    /// <summary>One sampled frame of the open sequence.</summary>
    private struct Frame
    {
        public float RaiseProgress;
        public bool UiVisible;
        public float FocusBlend;
        public float Time;
    }

    private Gamepad _pad;

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
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _pad = null;
        UiInput.ResetLayersForAutomation();
        AppQuit.ResetBookkeepingForAutomation();
        InputDeviceTracker.ResetToDefaults();
    }

    // ====================================================================
    // The reported defect: no one-frame pop, on either entry
    // ====================================================================

    [UnityTest]
    public IEnumerator SurfaceY_Open_DoesNotRevealUiImmediately_Green()
    {
        yield return OpenWithButtonAndCheckTiming(GamepadButton.North, "Y / buttonNorth");
    }

    [UnityTest]
    public IEnumerator SurfaceX_Open_DoesNotRevealUiImmediately_Green()
    {
        yield return OpenWithButtonAndCheckTiming(GamepadButton.West, "X / buttonWest");
    }

    private IEnumerator OpenWithButtonAndCheckTiming(GamepadButton button, string label)
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        Assert.IsFalse(surface.IsWristOpen, "the surface wrist starts closed");
        Assert.IsFalse(surface.IsWristTerminalRevealed,
            "no terminal is presented before the input");

        // Queue the real press and start sampling immediately: the first frames
        // after the input are exactly where the defect lived.
        var frames = new List<Frame>();
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
        yield return null;
        InputSystem.QueueStateEvent(_pad, new GamepadState());

        float deadline = Time.realtimeSinceStartup + 4f;
        while (Time.realtimeSinceStartup < deadline)
        {
            frames.Add(new Frame
            {
                RaiseProgress = surface.Arms != null ? surface.Arms.WristPresentationBlend : 0f,
                UiVisible = surface.IsWristTerminalRevealed,
                FocusBlend = surface.Arms != null ? surface.Arms.FocusBlend : 0f,
                Time = Time.realtimeSinceStartup
            });

            if (surface.IsWristTerminalRevealed && surface.Arms != null
                && surface.Arms.FocusBlend >= 0.999f
                && surface.Arms.CurrentWristPhase
                   == FirstPersonBlockArmsController.WristPhase.Hold)
            {
                break;
            }

            yield return null;
        }

        Assert.Greater(frames.Count, 3, label + ": the sequence was sampled over frames");
        Assert.IsTrue(surface.IsWristOpen, label + " opened the wrist authority");
        Assert.IsTrue(surface.IsWristTerminalRevealed, label + " reached the presented state");

        // ---- 1. the arm moves FIRST ----
        int firstMoving = frames.FindIndex(f => f.RaiseProgress > 0.02f);
        int firstVisible = frames.FindIndex(f => f.UiVisible);
        Assert.GreaterOrEqual(firstMoving, 0,
            label + ": the arm started raising (no frame ever moved)");
        Assert.GreaterOrEqual(firstVisible, 0, label + ": the terminal was presented");
        Assert.Less(firstMoving, firstVisible,
            label + ": the raise starts BEFORE the terminal is presented (arm frame "
            + firstMoving + " < ui frame " + firstVisible + ")");

        // ---- 2. nothing is presented while the arm is still early ----
        for (int index = 0; index < frames.Count; index++)
        {
            if (frames[index].RaiseProgress < 0.70f)
            {
                Assert.IsFalse(frames[index].UiVisible,
                    label + ": the terminal stays hidden while the raise is at "
                    + frames[index].RaiseProgress.ToString("0.00") + " (frame " + index + ")");
            }
        }

        // ---- 3. the reveal happens around 0.82, never at 0 ----
        float revealProgress = frames[firstVisible].RaiseProgress;
        Assert.Greater(revealProgress, 0.70f,
            label + ": the reveal waited for the raise (progress "
            + revealProgress.ToString("0.00") + ")");
        Assert.Less(revealProgress, 1.01f,
            label + ": the reveal did not overshoot the raise");

        // ---- 4. no one-frame pop: the terminal is not shown straight away ----
        Assert.Greater(frames[firstVisible].Time - frames[0].Time, 0.15f,
            label + ": the terminal did not appear within a frame or two of the input");
    }

    // ====================================================================
    // Threshold, smoothness and the hold / close sequence
    // ====================================================================

    [UnityTest]
    public IEnumerator Surface_Open_UiAppearsOnlyAfterRevealThreshold_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        yield return PressButton(GamepadButton.North);
        Assert.IsTrue(surface.IsWristOpen, "the authority opened");

        // Sample until the bridge reports the reveal it actually used.
        yield return WaitUntil(() => surface.WristPresentation != null
                                     && surface.WristPresentation.LastRevealProgress > 0f, 4f);

        WristPresentationBridge bridge = surface.WristPresentation;
        Assert.IsNotNull(bridge, "the surface drives the same presentation bridge");
        Assert.GreaterOrEqual(bridge.LastRevealProgress,
            WristPresentationBridge.UiRevealNormalizedTime - 0.02f,
            "the reveal waited for the raise threshold (progress "
            + bridge.LastRevealProgress.ToString("0.00") + ")");
        Assert.Less(bridge.LastRevealProgress, 1.02f,
            "the reveal happened at the end of the raise, not after it");
        Assert.IsTrue(bridge.LastRevealReason.StartsWith("raise reached"),
            "the reveal came from the raise, not from the timeout fallback ("
            + bridge.LastRevealReason + ")");
    }

    [UnityTest]
    public IEnumerator Surface_Open_UiAlphaDoesNotJumpZeroToOneInOneFrame_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        yield return PressButton(GamepadButton.North);
        yield return WaitUntil(() => surface.IsWristTerminalRevealed, 4f);
        Assert.IsTrue(surface.IsWristTerminalRevealed, "the terminal is presented");

        // The frost crossfade is the alpha ramp that follows the reveal: it must
        // pass through intermediate states, never step 0 -> 1.
        var blend = new List<float>();
        float deadline = Time.realtimeSinceStartup + 2f;
        while (surface.Arms.FocusBlend < 0.999f && Time.realtimeSinceStartup < deadline)
        {
            blend.Add(surface.Arms.FocusBlend);
            yield return null;
        }

        Assert.AreEqual(1f, surface.Arms.FocusBlend, 0.01f, "the frost settles");
        int intermediates = 0;
        foreach (float value in blend)
        {
            if (value > 0.05f && value < 0.95f)
            {
                intermediates++;
            }
        }

        Assert.Greater(intermediates, 0,
            "the presented state crossfades instead of jumping in one frame "
            + "(sampled " + blend.Count + " frames)");
    }

    [UnityTest]
    public IEnumerator Surface_Open_ReachesHoldWithUiVisible_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        yield return PressButton(GamepadButton.West);

        yield return WaitUntil(() => surface.Arms != null
                                     && surface.Arms.CurrentWristPhase
                                        == FirstPersonBlockArmsController.WristPhase.Hold, 4f);

        Assert.AreEqual(FirstPersonBlockArmsController.WristPhase.Hold,
            surface.Arms.CurrentWristPhase, "the raise settled into the hold phase");
        Assert.IsTrue(surface.IsWristTerminalRevealed,
            "the terminal is presented at the hold pose");

        // The frost crossfade starts in the reveal frame and takes
        // FocusCrossfadeDuration to settle, so give it that beat before asserting.
        yield return WaitUntil(() => surface.Arms.FocusBlend >= 0.999f, 2f);
        Assert.AreEqual(1f, surface.Arms.FocusBlend, 0.01f, "the frost settled");
        Assert.AreEqual(1f, surface.Arms.PositionProgress, 0.02f, "the pose is the hold pose");
        Assert.AreEqual(1f, surface.Arms.RotationProgress, 0.02f,
            "translation and rotation finished together");
    }

    [UnityTest]
    public IEnumerator Surface_Close_UsesTheExistingPresentationSequence_Green()
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        yield return PressButton(GamepadButton.North);
        yield return WaitUntil(() => surface.IsWristTerminalRevealed
                                     && surface.Arms.CurrentWristPhase
                                        == FirstPersonBlockArmsController.WristPhase.Hold, 4f);
        Assert.IsTrue(surface.IsWristTerminalRevealed, "the terminal is up before closing");

        yield return PressButton(GamepadButton.East);

        // Close order: the terminal is released first, then the arm lowers.
        Assert.IsFalse(surface.IsWristOpen, "Back closed the authority");
        Assert.IsFalse(surface.IsWristTerminalRevealed,
            "the terminal is released on the close, not after the lower");

        yield return WaitUntil(() => surface.Arms.CurrentWristPhase
                                     == FirstPersonBlockArmsController.WristPhase.Rest, 4f);
        Assert.AreEqual(FirstPersonBlockArmsController.WristPhase.Rest,
            surface.Arms.CurrentWristPhase, "the arm returned to rest");
        Assert.Less(surface.Arms.FocusBlend, 0.01f, "the arm is crisp again");
        Assert.AreEqual(1f, surface.Arms.PhysicalArmAlpha, 0.01f,
            "the opaque arm is restored");
    }

    // ====================================================================
    // The ship keeps the timing it was accepted with
    // ====================================================================

    [UnityTest]
    public IEnumerator Ship_And_Surface_ShareTheSameRevealTiming_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(value => ship = value);

        yield return PressButton(GamepadButton.North);
        Assert.IsTrue(ship.IsWristOpen, "the ship wrist opened");
        Assert.IsFalse(ship.IsWristTerminalRevealed,
            "the ship does NOT present the terminal on the press");
        yield return WaitUntil(() => ship.IsWristTerminalRevealed, 4f);
        float shipReveal = ship.WristPresentation.LastRevealProgress;

        yield return PressButton(GamepadButton.North);
        yield return WaitUntil(() => !ship.IsWristOpen, 3f);

        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        yield return PressButton(GamepadButton.North);
        Assert.IsFalse(surface.IsWristTerminalRevealed,
            "the surface does NOT present the terminal on the press either");
        yield return WaitUntil(() => surface.IsWristTerminalRevealed, 4f);
        float surfaceReveal = surface.WristPresentation.LastRevealProgress;

        Assert.Greater(shipReveal, 0f, "the ship revealed through the raise");
        Assert.Greater(surfaceReveal, 0f, "the surface revealed through the raise");
        Assert.AreEqual(shipReveal, surfaceReveal, 0.06f,
            "ship and surface reveal at the same point of the raise (ship "
            + shipReveal.ToString("0.00") + " vs surface " + surfaceReveal.ToString("0.00") + ")");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

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

        ShipGameController ship = null;
        yield return WaitUntil(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady && ship.Player != null;
        }, 25f);
        Assert.IsNotNull(ship, "the ship interior is ready");
        yield return WaitUntil(() => !ship.IsEntryInputLocked, 8f);
        onShip(ship);
    }

    private static IEnumerator EnterSurface(System.Action<SurfaceGameController> onSurface)
    {
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return null;

        SurfaceGameController surface = null;
        yield return WaitUntil(() =>
        {
            surface = Object.FindAnyObjectByType<SurfaceGameController>();
            return surface != null && surface.IsReady && surface.Player != null
                   && surface.Arms != null;
        }, 25f);
        Assert.IsNotNull(surface, "the planet surface is ready with its arms");
        yield return WaitUntil(() => surface.Player.InputEnabled, 5f);
        onSurface(surface);
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }
}
