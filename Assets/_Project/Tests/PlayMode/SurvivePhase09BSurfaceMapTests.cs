using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 09B PASS 2 - the surface map driven by a controller.
///
/// The map does NOT get a second, pad-only implementation. Its existing cursor is
/// driven by the shared pointer authority (<see cref="UiPointerState"/>): the right
/// stick moves the very cursor the mouse uses, Confirm is the left button's equal,
/// the triggers zoom the map content and Back still leaves the map. These rows
/// queue REAL gamepad state, so the whole route a physical pad takes is covered:
///
///   right stick -> shared pointer -> display ray -> wrist pixel -> node hit test
///   Confirm     -> the same single selector the mouse click reaches
///   LT / RT     -> map content zoom (marker geometry and hit test together)
///   B           -> the wrist layer's Back (never the pause)
/// </summary>
public class SurvivePhase09BSurfaceMapTests
{
    private Gamepad _pad;
    private Keyboard _keyboard;
    private Mouse _mouse;
    private InputSettings.BackgroundBehavior _backgroundBehavior;
    private InputSettings.EditorInputBehaviorInPlayMode _editorInputBehavior;

    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;
        InputDeviceTracker.ResetToDefaults();
        UiInput.ResetLayersForAutomation();
        UiPointerState.ResetForAutomation();
        AppQuit.ResetBookkeepingForAutomation();
        AppQuit.SuppressForAutomation = true;

        // A batch-mode run never owns the OS foreground. By default the editor only
        // lets pointers and keyboards deliver input while the Game View is focused
        // and disables every other device that cannot run in the background, which
        // is why a queued mouse state was silently dropped while the gamepad always
        // arrived (measured here: mouse.delta read 0.0 and a queued mouse press
        // never appeared). These two settings make the flagged run deterministic and
        // are both restored in TearDown - this is the harness, never the product.
        _backgroundBehavior = InputSystem.settings.backgroundBehavior;
        _editorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

        GameSession session = GameSession.Instance;
        if (session != null)
        {
            Object.Destroy(session.gameObject);
        }

        _pad = InputSystem.AddDevice<Gamepad>();
        _keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.EnableDevice(_keyboard);

        // The batch-mode runner owns no OS mouse, so the mouse half of the shared
        // pointer is driven by a device added HERE (a device added once the scene is
        // running never receives queued state - measured in Phase 09A_3).
        _mouse = InputSystem.AddDevice<Mouse>();
        InputSystem.EnableDevice(_mouse);
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        if (_mouse != null && _mouse.added)
        {
            InputSystem.RemoveDevice(_mouse);
        }

        if (_keyboard != null && _keyboard.added)
        {
            InputSystem.RemoveDevice(_keyboard);
        }

        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _mouse = null;
        _keyboard = null;
        _pad = null;
        InputSystem.settings.backgroundBehavior = _backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBehavior;
        UiInput.ResetLayersForAutomation();
        UiPointerState.ResetForAutomation();
        InputDeviceTracker.ResetToDefaults();
        AppQuit.ResetBookkeepingForAutomation();
    }

    // ====================================================================
    // 1. The right stick moves the EXISTING cursor
    // ====================================================================

    [UnityTest]
    public IEnumerator MapCursor_RightStick_MovesTheExistingCursor_Steadily_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenMap(value => surface = value);
        Assert.IsTrue(MapVisible(surface), "the map is presented");

        // The cursor starts on the middle of the hologram - where a mouse user who
        // opened the map with the pointer over the disc would leave it.
        yield return PlacePointer(MapCentreScreen(surface));
        Vector2 start = UiPointerState.Position;

        // ---- a real right-stick push ----
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = new Vector2(0f, 1f) });
        yield return null;
        yield return null;

        Assert.IsTrue(UiPointerState.GamepadOwns,
            "the right stick takes the pointer over from the mouse");
        Assert.IsTrue(UiPointerState.GamepadMovedThisFrame,
            "the right stick is the movement source this frame");

        float windowStart = Time.realtimeSinceStartup;
        int frames = 0;
        while (Time.realtimeSinceStartup - windowStart < 0.30f)
        {
            frames++;
            yield return null;
        }

        Vector2 afterFirstWindow = UiPointerState.Position;
        float firstElapsed = Time.realtimeSinceStartup - windowStart;
        Assert.Greater(frames, 3, "the stick was held for several frames");
        Assert.Greater(afterFirstWindow.y, start.y + 1f,
            "pushing the right stick up moves the cursor up the screen");
        Assert.Less(Mathf.Abs(afterFirstWindow.x - start.x), 2f,
            "a purely vertical stick push does not drift the cursor sideways");

        // ---- steady speed: the distance matches speed x time ----
        float firstMoved = Vector2.Distance(afterFirstWindow, start);
        float expected = UiPointerState.SpeedPixelsPerSecond * firstElapsed;
        Assert.Greater(firstMoved, expected * 0.5f,
            "the cursor moves at the declared speed (moved " + firstMoved.ToString("0.0")
            + "px in " + firstElapsed.ToString("0.00") + "s, expected ~"
            + expected.ToString("0.0") + "px)");
        Assert.Less(firstMoved, expected * 1.5f,
            "and never faster than the declared speed: no acceleration curve");

        // ---- the next window covers the same distance: no ramp-up, no ramp-down ----
        Vector2 secondStart = UiPointerState.Position;
        float secondWindowStart = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - secondWindowStart < 0.20f)
        {
            yield return null;
        }

        float secondElapsed = Time.realtimeSinceStartup - secondWindowStart;
        float secondMoved = Vector2.Distance(UiPointerState.Position, secondStart);
        float secondExpected = UiPointerState.SpeedPixelsPerSecond * secondElapsed;
        Assert.Greater(secondMoved, secondExpected * 0.5f,
            "the speed is identical in the second window (no acceleration)");
        Assert.Less(secondMoved, secondExpected * 1.5f,
            "the speed is identical in the second window (no acceleration)");

        // ---- the cursor stays inside the hologram ----
        Assert.IsTrue(UiPointerState.HasBounds, "the map declared the cursor region");
        Assert.IsTrue(InsideBounds(UiPointerState.Position),
            "the pad cursor stays inside the projected map region");

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator MapCursor_CannotBePushedOffTheMap_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenMap(value => surface = value);

        // Hold the stick far longer than it takes to cross the hologram.
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = new Vector2(1f, 1f) });
        float deadline = Time.realtimeSinceStartup + 1.4f;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        Rect bounds = UiPointerState.Bounds;
        Vector2 position = UiPointerState.Position;
        Assert.IsTrue(InsideBounds(position),
            "the cursor is clamped inside the map region (pos " + position
            + " bounds " + bounds + ")");

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;

        // Moving the cursor is not selecting: nothing was chosen.
        Assert.AreEqual(-1, surface.NavigationTargetNodeId,
            "no target was selected just by moving the cursor");
        Assert.IsTrue(surface.IsWristOpen, "the map survived the clamp");
        Assert.IsFalse(surface.Player.InputEnabled, "the map still owns exploration input");
    }

    // ====================================================================
    // 2. Confirm is the mouse's left button on the map
    // ====================================================================

    [UnityTest]
    public IEnumerator MapCursor_Confirm_SelectsTheNodeUnderTheCursor_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenMap(value => surface = value);

        SurfaceWristController wrist = surface.Wrist;
        Assert.AreEqual(-1, surface.NavigationTargetNodeId, "no target is selected yet");

        // Park the shared pointer exactly on node 0. The pad could have driven it
        // there; the position comes from the same coordinate source the markers and
        // the hit test use, so this is the real cursor being moved, not a shortcut.
        yield return PlacePointerOnNode(surface, 0);

        Assert.AreEqual(0, wrist.HitTestFromPointer(),
            "the pointer over node 0 resolves to node 0");

        yield return PressButton(GamepadButton.South);

        Assert.AreEqual(0, surface.NavigationTargetNodeId,
            "A selected the node under the cursor (the mouse's left button equivalent)");
        Assert.IsTrue(surface.IsNavigationActive, "the navigation target is live");

        // A second press on the SAME node must not change anything: one selector
        // serves the pad and the mouse, and it is idempotent.
        yield return PressButton(GamepadButton.South);
        Assert.AreEqual(0, surface.NavigationTargetNodeId, "confirm is idempotent");
    }

    [UnityTest]
    public IEnumerator MapCursor_ConfirmAwayFromANode_SelectsNothing_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenMap(value => surface = value);

        // The map centre carries the landing marker, not a resource node.
        yield return PlacePointer(MapCentreScreen(surface));
        Assert.AreEqual(-1, surface.Wrist.HitTestFromPointer(),
            "the map centre is not a node");

        yield return PressButton(GamepadButton.South);

        Assert.AreEqual(-1, surface.NavigationTargetNodeId,
            "A over empty map space selects nothing");
        Assert.IsTrue(surface.IsWristOpen, "and the map stays open");
    }

    // ====================================================================
    // 3. LT / RT zoom the map content
    // ====================================================================

    [UnityTest]
    public IEnumerator MapZoom_Triggers_ZoomTheMapContent_AndClamp_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenMap(value => surface = value);

        SurfaceWristController wrist = surface.Wrist;
        Assert.AreEqual(1f, wrist.MapZoom, "the map opens at the authored 1:1 zoom");

        Vector2 markerBefore = NodeMarkerPixel(surface, 0);
        float distanceBefore = Vector2.Distance(markerBefore, RadialWristElement.Center);
        Assert.Greater(distanceBefore, 1f, "node 0 is drawn away from the map centre");

        // ---- RT = ZoomIn ----
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightTrigger = 1f });
        yield return Hold(0.45f);

        Assert.Greater(wrist.MapZoom, 1.05f, "RT zooms the map in");
        float distanceZoomedIn = Vector2.Distance(NodeMarkerPixel(surface, 0),
            RadialWristElement.Center);
        Assert.Greater(distanceZoomedIn, distanceBefore + 1f,
            "zooming in spreads the node markers apart");

        // ---- LT = ZoomOut, past the 1:1 mapping ----
        InputSystem.QueueStateEvent(_pad, new GamepadState { leftTrigger = 1f });
        yield return Hold(0.9f);
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;

        Assert.Less(wrist.MapZoom, 1f, "LT zooms the map out");
        float distanceZoomedOut = Vector2.Distance(NodeMarkerPixel(surface, 0),
            RadialWristElement.Center);
        Assert.Less(distanceZoomedOut, distanceBefore,
            "zooming out gathers the node markers toward the centre");

        // ---- the zoom is clamped and the marker never leaves the disc ----
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightTrigger = 1f });
        yield return Hold(2.2f);
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;

        Assert.AreEqual(WristMapCoordinates.MaxZoom, wrist.MapZoom, 0.001f,
            "the zoom is clamped at the authored maximum");
        float maxDistance = Vector2.Distance(NodeMarkerPixel(surface, 0),
            RadialWristElement.Center);
        Assert.LessOrEqual(maxDistance, WristMapCoordinates.UsableMapRadius + 1f,
            "a marker never leaves the usable map disc at maximum zoom");

        // The hit test follows the same zoom: the cursor still lands on the marker.
        yield return PlacePointerOnNode(surface, 0);
        Assert.AreEqual(0, wrist.HitTestFromPointer(),
            "hit testing follows the zoomed marker position");

        yield return PressButton(GamepadButton.South);
        Assert.AreEqual(0, surface.NavigationTargetNodeId,
            "the zoomed map is still selectable with Confirm");
    }

    // ====================================================================
    // 4. The prompt, and no input leak while the map is open
    // ====================================================================

    [UnityTest]
    public IEnumerator MapPrompt_OffersOnlyTheMapKeys_AndNoLeakThroughOtherLayers_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenMap(value => surface = value);

        // ---- the dynamic prompt: only what the map can do right now ----
        InputDeviceTracker.NotifyGamepad();
        yield return null;
        yield return null;

        ControllerPromptBar bar = surface.Hud.PromptBar;
        Assert.IsNotNull(bar, "the surface owns the shared prompt bar");
        Assert.IsTrue(bar.IsShown, "the map shows its controller prompt while presented");
        Assert.AreEqual("[RS] 光标   [A] 选择   [LT/RT] 缩放   [B] 返回", bar.PromptText,
            "the map prompt names cursor / select / zoom / back");
        Assert.IsFalse(bar.PromptText.Contains("LB") || bar.PromptText.Contains("RB"),
            "no tab keys are offered: this map has no tabs or layers");
        Assert.IsFalse(bar.PromptText.Contains("LS"),
            "no pan key is offered: this map has no pan");

        // ---- the wrist layer owns Back and the pause never stacks ----
        Assert.AreEqual(UiInput.UiLayer.Wrist, UiInput.ActiveBackLayer,
            "the open map owns Back");
        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(surface.IsWristOpen, "Menu/Start never closes the map");
        Assert.AreEqual(UiInput.UiLayer.Wrist, UiInput.ActiveBackLayer,
            "the map still owns Back after the pause key");

        // ---- Y still closes the map (the Phase 09A contract, untouched) ----
        yield return PressButton(GamepadButton.North);
        Assert.IsFalse(surface.IsWristOpen, "Y closes the map as it always did");
        yield return WaitUntil(() => !MapVisible(surface), 3f);
        Assert.IsFalse(MapVisible(surface), "the map presentation was released");

        // ---- X reopens it, and B closes it again ----
        yield return PressButton(GamepadButton.West);
        yield return WaitUntil(() => MapVisible(surface), 3f);
        Assert.IsTrue(surface.IsWristOpen, "X opened the map again");
        Assert.IsTrue(surface.Hud.PromptBar.IsShown,
            "the prompt comes back with the presented map");

        yield return PressButton(GamepadButton.East);
        Assert.IsFalse(surface.IsWristOpen, "B closes the map");
        Assert.IsTrue(surface.Player.InputEnabled, "exploration input is restored");
        Assert.IsFalse(UiPointerState.HasBounds,
            "the closed map released the cursor region");
        Assert.AreEqual(UiInput.UiLayer.Gameplay, UiInput.ActiveBackLayer,
            "Back belongs to free exploration again");
    }

    [UnityTest]
    public IEnumerator MapCursor_Escape_StillIsTheMapsBack_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenMap(value => surface = value);

        // Escape keeps its Phase 09A meaning while the map is open: the wrist
        // layer's local Back, never the global Pause.
        UiInput.InjectEscapeForAutomation();
        yield return WaitUntil(() => !surface.IsWristOpen, 2f);

        Assert.IsFalse(surface.IsWristOpen, "Escape closes the map");
        Assert.AreEqual(UiInput.UiLayer.Gameplay, UiInput.ActiveBackLayer,
            "Escape was consumed as the map's Back, so no pause layer opened");
    }

    // ====================================================================
    // 5. Mouse and pad share one cursor
    // ====================================================================

    [UnityTest]
    public IEnumerator MapCursor_MouseAndGamepad_HandOverWithoutJumping_Green()
    {
        SurfaceGameController surface = null;
        yield return OpenMap(value => surface = value);

        // The mouse route is exercised with the device the run created (the batch
        // runner has no OS mouse). What is under test is the handover RULE, and it
        // reads exactly the same device controls either way.
        Assert.AreSame(_mouse, Mouse.current,
            "the run's mouse device is the current mouse for the handover rules");
        Mouse mouse = _mouse;

        yield return PlacePointer(MapCentreScreen(surface));
        Assert.IsFalse(UiPointerState.GamepadOwns, "the mouse starts as the pointer source");

        // ---- the pad takes over, continuing FROM the mouse's position ----
        Vector2 handoverStart = UiPointerState.Position;
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = new Vector2(1f, 0f) });
        yield return null;
        yield return null;

        Assert.IsTrue(UiPointerState.GamepadOwns, "the stick took the pointer over");
        Vector2 firstStep = UiPointerState.Position;
        Assert.Less(Vector2.Distance(firstStep, handoverStart),
            UiPointerState.SpeedPixelsPerSecond * 0.2f,
            "the pad continues FROM the current position instead of jumping");
        Assert.Greater(firstStep.x, handoverStart.x, "and it moves where the stick points");

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
        yield return null;
        yield return null;
        yield return null;

        Assert.IsTrue(UiPointerState.GamepadOwns,
            "the cursor stays where the pad left it while the stick is centred");
        Vector2 padPosition = UiPointerState.Position;

        // ---- a real mouse MOVE takes it straight back ----
        //
        // The queued delta is the movement signal a real mouse sends; it is read
        // back here to prove the device route delivered it (a synthetic mouse that
        // the runner never enables would silently swallow it).
        InputSystem.QueueDeltaStateEvent(mouse.delta, new Vector2(60f, 0f));
        yield return null;
        Assert.Greater(mouse.delta.ReadValue().magnitude, 2f,
            "the queued mouse delta reached the mouse device");
        Assert.IsTrue(InputDeviceTracker.MouseProducedRealInput(),
            "a real mouse move is real mouse input (never our own cursor warp)");

        bool mouseTookOver = false;
        for (int i = 0; i < 8 && !mouseTookOver; i++)
        {
            yield return null;
            mouseTookOver = !UiPointerState.GamepadOwns;
        }

        Assert.IsTrue(mouseTookOver, "a real mouse move takes the cursor back immediately");
        Assert.AreEqual(mouse.position.ReadValue(), UiPointerState.Position,
            "the mouse position is the pointer again (one cursor, two devices)");
        Assert.AreNotEqual(padPosition, UiPointerState.Position,
            "the pointer really moved to where the mouse is");

        // ---- and the pad can take it back, again without jumping ----
        yield return PlacePointer(MapCentreScreen(surface));
        Vector2 secondHandoverStart = UiPointerState.Position;
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = new Vector2(0f, -1f) });
        yield return null;
        yield return null;

        Assert.IsTrue(UiPointerState.GamepadOwns, "the stick owns the pointer again");
        Vector2 secondStep = UiPointerState.Position;
        Assert.Less(Vector2.Distance(secondStep, secondHandoverStart),
            UiPointerState.SpeedPixelsPerSecond * 0.2f,
            "the second handover also continues from the mouse's position");
        Assert.Less(secondStep.y, secondHandoverStart.y,
            "and it moves where the stick points (down)");

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;

        // One cursor, two devices: the map never grew a pad-only cursor.
        Assert.AreEqual(1, Object.FindObjectsByType<SurfaceWristController>(
            FindObjectsInactive.Include).Length,
            "exactly one surface map exists");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static bool MapVisible(SurfaceGameController surface)
    {
        return surface != null && surface.Wrist != null
               && surface.Wrist.Display != null && surface.Wrist.Display.Visible;
    }

    /// <summary>
    /// True when the point is inside the declared cursor region. A 1 px epsilon is
    /// allowed because the clamp lands exactly ON the boundary (Rect.Contains
    /// excludes its max edge) and the boundaries are projections of a moving quad.
    /// </summary>
    private static bool InsideBounds(Vector2 position)
    {
        Rect bounds = UiPointerState.Bounds;
        return position.x >= bounds.xMin - 1f && position.x <= bounds.xMax + 1f
               && position.y >= bounds.yMin - 1f && position.y <= bounds.yMax + 1f;
    }

    /// <summary>Screen position (bottom-left origin) of the map centre.</summary>
    private static Vector2 MapCentreScreen(SurfaceGameController surface)
    {
        return ScreenOf(surface, RadialWristElement.Center);
    }

    /// <summary>Screen position of a node's marker at the CURRENT map zoom.</summary>
    private static Vector2 NodeScreen(SurfaceGameController surface, int nodeId)
    {
        SurfaceResourceNode node = surface.GetNodeById(nodeId);
        Vector2 pixel = WristMapCoordinates.WorldToWristMap(node.transform.position,
            surface.Wrist.MapZoom);
        return ScreenOf(surface, pixel);
    }

    private static Vector2 ScreenOf(SurfaceGameController surface, Vector2 wristPixel)
    {
        if (surface.Wrist.TryProjectWristPixelToViewport(wristPixel, out Vector3 viewport))
        {
            return new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
        }

        Assert.Fail("the wrist pixel " + wristPixel + " does not project onto the display");
        return Vector2.zero;
    }

    /// <summary>The drawn marker pixel of a node (the visual element's own style).</summary>
    private static Vector2 NodeMarkerPixel(SurfaceGameController surface, int nodeId)
    {
        VisualElement marker = surface.Wrist.RootElement.Q<VisualElement>("MapNode" + nodeId);
        Assert.IsNotNull(marker, "MapNode" + nodeId + " exists in the wrist document");
        return new Vector2(marker.style.left.value.value, marker.style.top.value.value);
    }

    /// <summary>
    /// Puts the shared pointer at a screen position the way a mouse user would:
    /// the device itself reports the position (warp + a real position state), and
    /// the shared pointer state is synchronised to it. Two frames pass so the wrist
    /// has ticked with the new pointer.
    /// </summary>
    private static IEnumerator PlacePointer(Vector2 screenPosition)
    {
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            mouse.WarpCursorPosition(screenPosition);
            InputDeviceTracker.NotifyPointerWarp(screenPosition);
            InputSystem.QueueStateEvent(mouse, MouseStateAt(screenPosition, false));
        }

        UiPointerState.PlaceForAutomation(screenPosition, UiPointerState.Source.Mouse);
        yield return null;
        yield return null;
    }

    /// <summary>A full mouse state the device would report (position / buttons).</summary>
    private static MouseState MouseStateAt(Vector2 position, bool leftButtonDown)
    {
        var state = new MouseState { position = position, delta = Vector2.zero };
        return leftButtonDown
            ? state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left)
            : state;
    }

    private IEnumerator Hold(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    /// <summary>Opens the map through the REAL gamepad route, then waits for it.</summary>
    private IEnumerator OpenMap(System.Action<SurfaceGameController> onSurface)
    {
        SurfaceGameController surface = null;
        yield return EnterSurface(value => surface = value);

        yield return PressButton(GamepadButton.West);
        Assert.IsTrue(surface.IsWristOpen, "Xbox X opens the surface map");
        yield return WaitUntil(() => MapVisible(surface), 3f);
        Assert.IsTrue(MapVisible(surface), "the map is presented");

        // The wrist presentation raises the arm and reveals the hologram over about
        // a second, and the display anchor travels with it while it does. Every
        // screen-space pointer calculation below would drift with it, so the tests
        // wait until the hologram has actually stopped moving.
        yield return WaitUntil(() => surface.Arms != null && surface.Arms.FocusBlend >= 0.999f, 4f);
        yield return WaitForDisplayToSettle(surface, 3f);
        onSurface(surface);
    }

    /// <summary>Waits until the wrist display transform stops moving (3 stable frames).</summary>
    private static IEnumerator WaitForDisplayToSettle(SurfaceGameController surface, float seconds)
    {
        Transform display = surface.Wrist.Display != null
            ? surface.Wrist.Display.transform
            : null;
        if (display == null)
        {
            yield break;
        }

        Vector3 lastPosition = display.position;
        Quaternion lastRotation = display.rotation;
        int stable = 0;
        float deadline = Time.realtimeSinceStartup + seconds;
        while (stable < 3 && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            bool still = Vector3.Distance(display.position, lastPosition) < 0.0005f
                         && Quaternion.Angle(display.rotation, lastRotation) < 0.05f;
            stable = still ? stable + 1 : 0;
            lastPosition = display.position;
            lastRotation = display.rotation;
        }
    }

    /// <summary>
    /// Parks the shared pointer on a node marker, then verifies the node really is
    /// under it. The hologram keeps a tiny residual motion (its parallax follows the
    /// pointer), so the placement is re-asserted instead of assumed; the assertion
    /// that follows is still the real contract.
    /// </summary>
    private static IEnumerator PlacePointerOnNode(SurfaceGameController surface, int nodeId,
        int attempts = 6)
    {
        for (int i = 0; i < attempts; i++)
        {
            yield return PlacePointer(NodeScreen(surface, nodeId));
            if (surface.Wrist.HitTestFromPointer() == nodeId)
            {
                yield break;
            }
        }
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

    private static IEnumerator EnterSurface(System.Action<SurfaceGameController> onSurface)
    {
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return null;

        SurfaceGameController surface = null;
        yield return WaitUntil(() =>
        {
            surface = Object.FindAnyObjectByType<SurfaceGameController>();
            return surface != null && surface.IsReady && surface.Player != null;
        }, 25f);
        Assert.IsNotNull(surface, "the planet surface is ready");
        yield return WaitUntil(() => surface.Player.InputEnabled, 5f);
        yield return null;
        onSurface?.Invoke(surface);
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
