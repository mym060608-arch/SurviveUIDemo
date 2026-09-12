using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

/// <summary>
/// Phase 09B controller-interaction evidence probe.
///
/// Inert unless the player is launched with
/// <c>-ship09BShot &lt;output folder&gt;</c>. It drives the REAL product route with a
/// REAL virtual gamepad (queued device state, exactly what a physical pad sends)
/// and records what the player would see and feel on the surface map:
///
///   * X opens the map and the shared prompt bar names the map's own keys,
///   * the RIGHT STICK moves the existing map cursor at a steady speed and the
///     cursor can never be pushed off the map,
///   * the cursor is steered onto a resource node with nothing but the stick, and
///     Confirm (A) is the mouse's left button on that node,
///   * LT / RT zoom the map content and the marker never leaves the disc,
///   * the mouse takes the cursor over the instant it moves and the pad resumes
///     from wherever the mouse left the dot,
///   * the prompt bar re-glyphs for the device the player is holding,
///   * B closes the map and releases the cursor region.
///
/// The dot/ring cursor the player sees IS the OS cursor (Cursor.SetCursor through
/// WristCursorService), and an OS cursor is composited by the desktop, never into
/// the captured backbuffer. The cursor POSITION is therefore evidenced by the
/// logged pointer / wrist-pixel / hit-test numbers, while the screenshots evidence
/// the hover and selection state inside the hologram.
/// </summary>
public class Ship09BControllerProbe : MonoBehaviour
{
    private const string Flag = "-ship09BShot";

    private string _folder;
    private bool _active;
    private readonly StringBuilder _log = new StringBuilder();
    private bool _allPassed = true;

    private Gamepad _pad;
    private bool _addedPad;
    private SurfaceGameController _surface;
    private SurfacePlayerController _player;

    private void Awake()
    {
        _folder = ParseFlag(Flag);
        _active = !string.IsNullOrEmpty(_folder);
        if (!_active)
        {
            enabled = false;
            return;
        }

        Directory.CreateDirectory(_folder);
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        Screen.SetResolution(1920, 1080, false);

        // The evidence player is launched from a shell and never owns the OS
        // foreground for the whole run. The Input System's default background
        // behaviour disables every device that cannot run in the background (a
        // device ADDED while unfocused included), which silently drops queued state
        // for the mouse and the keyboard (measured in Phase 09A_3 and again in the
        // Phase 09B suites). Ignoring focus keeps the device path deterministic; a
        // normal game launch never reaches this line.
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (_active)
        {
            StartCoroutine(Run());
            StartCoroutine(Watchdog());
        }
    }

    private IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(420f);
        Fail("watchdog timeout");
        Finish(2);
    }

    private IEnumerator Run()
    {
        Info("probe start: screen " + Screen.width + "x" + Screen.height
             + " pointerSpeed=" + UiPointerState.SpeedPixelsPerSecond.ToString("0")
             + "px/s (fraction " + UiPointerState.SpeedFractionPerSecond + ")");

        yield return EnsureGamepadDevice();
        if (_pad == null || !_pad.added)
        {
            Finish(1);
            yield break;
        }

        // ---- the planet surface, standing beside node 0 (the real slice) ----
        GameSession.Ensure();
        SceneRouting.EnterSurface();
        yield return WaitUntil(() =>
        {
            _surface = UnityEngine.Object.FindAnyObjectByType<SurfaceGameController>();
            return _surface != null && _surface.IsReady && _surface.Player != null
                   && _surface.Nodes.Count > 0;
        }, 40f);

        Check("the planet surface is ready", _surface != null && _surface.Player != null);
        if (_surface == null || _surface.Player == null)
        {
            Finish(1);
            yield break;
        }

        _player = _surface.Player;
        yield return WaitUntil(() => _player.InputEnabled, 5f);

        SurfaceResourceNode node0 = _surface.GetNodeById(0);
        Check("node 0 exists for the cursor test", node0 != null);
        _player.TeleportTo(node0.transform.position + new Vector3(-1.7f, 0f, 1.9f));
        yield return null;
        FaceToward(_player, node0.transform.position);
        yield return new WaitForSecondsRealtime(0.5f);

        // ---- X opens the map (the Phase 09A binding, still the ONLY map) ----
        Check("the map is closed before the press", !_surface.IsWristOpen);
        yield return PressButton(GamepadButton.West);
        Check("Xbox X / buttonWest opened the surface map", _surface.IsWristOpen);

        SurfaceWristController wrist = _surface.Wrist;
        yield return WaitUntil(() => wrist != null && wrist.Display != null
                                     && wrist.Display.Visible, 4f);
        Check("the map hologram is presented", MapVisible());
        yield return WaitUntil(() => _surface.Arms != null && _surface.Arms.FocusBlend >= 0.999f, 5f);
        yield return WaitForDisplayToSettle(3f);

        Check("the deck has exactly one surface map (no pad-only second map)",
            UnityEngine.Object.FindObjectsByType<SurfaceWristController>(
                FindObjectsInactive.Include).Length == 1);
        Check("the map opens at the authored 1:1 zoom", Mathf.Approximately(wrist.MapZoom, 1f));

        // ---- the dynamic prompt ----
        yield return new WaitForSecondsRealtime(0.25f);
        ControllerPromptBar bar = _surface.Hud != null ? _surface.Hud.PromptBar : null;
        Check("the surface owns the shared prompt bar", bar != null);
        if (bar != null)
        {
            Info("map prompt (gamepad): " + bar.PromptText);
            Check("the map prompt names cursor / select / zoom / back",
                bar.PromptText == "[RS] 光标   [A] 选择   [LT/RT] 缩放   [B] 返回");
            Check("the map prompt never offers a key the map cannot use (no LB/RB, no LS)",
                !bar.PromptText.Contains("LB") && !bar.PromptText.Contains("RB")
                && !bar.PromptText.Contains("LS"));
        }

        Info("pointer before the stick: source=" + UiPointerState.Current
             + " pos=" + Format(UiPointerState.Position)
             + " bounds=" + (UiPointerState.HasBounds ? UiPointerState.Bounds.ToString() : "<none>"));

        yield return Capture("01_map_open_prompt");

        // ---- the OS cursor may be anywhere when the map opens ----
        //
        // The mouse is never clamped, so the pointer can legitimately sit outside
        // the hologram while the mouse owns it. The FIRST stick input has to bring
        // it onto the map (the cursor can never be left permanently outside the
        // valid region). That snap is measured as its own step so it can never be
        // mistaken for cursor speed in the windows below.
        Vector2 beforeFirstStick = UiPointerState.Position;
        bool startedOutside = !Inside(beforeFirstStick);
        Info("first stick input: cursor " + Format(beforeFirstStick)
             + " inside=" + !startedOutside);
        yield return PushStickWindow(new Vector2(0f, 1f), 0.35f);
        Info("after the first stick input: cursor " + Format(UiPointerState.Position)
             + " inside=" + Inside(UiPointerState.Position));
        Check("the pad owns the cursor after the first stick input", UiPointerState.GamepadOwns);
        Check("the first stick input puts the cursor on the map (it started at "
              + Format(beforeFirstStick) + ")", Inside(UiPointerState.Position));
        yield return Capture("02_cursor_on_map");

        // ---- the RIGHT STICK moves the existing cursor at a steady speed ----
        yield return CursorWindow("03_rs_cursor_up", new Vector2(0f, 1f), 0.35f);
        yield return CursorWindow("04_rs_cursor_left", new Vector2(-1f, 0f), 0.35f);
        yield return CursorWindow("05_rs_cursor_down_right", new Vector2(0.7f, -0.7f), 0.35f);

        // ---- the cursor cannot be pushed off the map ----
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = new Vector2(1f, 1f) });
        yield return new WaitForSecondsRealtime(1.4f);
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
        Rect bounds = UiPointerState.Bounds;
        Vector2 clamped = UiPointerState.Position;
        Info("clamp: pos=" + Format(clamped) + " bounds=" + bounds);
        Check("the pad cursor stays inside the projected map region after 1.4 s of "
              + "full deflection", Inside(clamped));
        yield return Capture("06_cursor_clamped");

        // ---- steer the cursor onto node 0 with the stick alone ----
        yield return SteerOntoNode(node0, "07_cursor_on_node");

        // ---- Confirm is the mouse's left button ----
        int hitBefore = wrist.HitTestFromPointer();
        bool hoverBefore = wrist.CursorService != null && wrist.CursorService.IsHovering;
        Info("hover over the node: hit=" + hitBefore + " cursorHover=" + hoverBefore);
        Check("the cursor hovers node 0 (the dot gains its ring)", hitBefore == 0 && hoverBefore);

        yield return PressButton(GamepadButton.South);
        yield return null;
        Info("after Confirm: target=" + _surface.NavigationTargetNodeId
             + " targetName=" + (_surface.NavigationTargetNodeId >= 0
                 ? SurfaceGameController.NodeDisplayName(_surface.NavigationTargetNodeId)
                 : "<none>"));
        Check("A selected the node under the cursor (left-button equivalent)",
            _surface.NavigationTargetNodeId == 0);
        Check("selecting never closed the map", _surface.IsWristOpen);
        yield return Capture("08_confirm_selected_node");

        // ---- LT / RT zoom the map content ----
        float zoomAtOpen = wrist.MapZoom;
        Vector2 markerAtOpen = NodeMarkerPixel(0);
        float distanceAtOpen = Vector2.Distance(markerAtOpen, RadialWristElement.Center);

        yield return HoldTrigger(right: true, 0.7f);
        Vector2 markerZoomIn = NodeMarkerPixel(0);
        float distanceZoomIn = Vector2.Distance(markerZoomIn, RadialWristElement.Center);
        Info("zoom in: " + zoomAtOpen.ToString("0.00") + " -> " + wrist.MapZoom.ToString("0.00")
             + " node0 marker distance " + distanceAtOpen.ToString("0") + " -> "
             + distanceZoomIn.ToString("0") + "px");
        Check("RT zooms the map in", wrist.MapZoom > zoomAtOpen + 0.05f);
        Check("zooming in spreads the node markers apart", distanceZoomIn > distanceAtOpen + 1f);
        yield return Capture("09_zoom_in");

        yield return HoldTrigger(right: false, 1.6f);
        Vector2 markerZoomOut = NodeMarkerPixel(0);
        float distanceZoomOut = Vector2.Distance(markerZoomOut, RadialWristElement.Center);
        Info("zoom out: -> " + wrist.MapZoom.ToString("0.00")
             + " node0 marker distance " + distanceZoomOut.ToString("0") + "px");
        Check("LT zooms the map out", wrist.MapZoom < 1f);
        Check("zooming out gathers the markers toward the map centre",
            distanceZoomOut < distanceAtOpen);
        yield return Capture("10_zoom_out");

        yield return HoldTrigger(right: true, 2.4f);
        Vector2 markerAtMax = NodeMarkerPixel(0);
        float distanceAtMax = Vector2.Distance(markerAtMax, RadialWristElement.Center);
        Info("zoom clamp: " + wrist.MapZoom.ToString("0.00") + " (max "
             + WristMapCoordinates.MaxZoom.ToString("0.00") + ") marker distance "
             + distanceAtMax.ToString("0") + "px (usable radius "
             + WristMapCoordinates.UsableMapRadius.ToString("0") + ")");
        Check("the zoom is clamped at the authored maximum",
            Mathf.Abs(wrist.MapZoom - WristMapCoordinates.MaxZoom) < 0.001f);
        Check("a marker never leaves the usable map disc at maximum zoom",
            distanceAtMax <= WristMapCoordinates.UsableMapRadius + 1f);
        Check("the zoomed map is still selectable (hit test follows the zoom)",
            wrist.HitTestNode(WristMapCoordinates.WorldToWristMap(node0.transform.position,
                wrist.MapZoom)) == 0);
        yield return Capture("11_zoom_clamped");

        // ---- the mouse takes the cursor over ----
        yield return MouseTakeover(bar);

        // ---- the pad resumes from wherever the mouse left the dot ----
        yield return PadResume();

        // ---- the prompt follows the device that was used last ----
        yield return KeyboardGlyphCheck(bar);

        // ---- B closes the map and releases the cursor region ----
        yield return PressButton(GamepadButton.East);
        yield return null;
        Check("B (Back) closes the map", !_surface.IsWristOpen);
        Check("exploration input is restored", _player.InputEnabled);
        Check("the closed map released the cursor region", !UiPointerState.HasBounds);
        Check("Back belongs to free exploration again",
            UiInput.ActiveBackLayer == UiInput.UiLayer.Gameplay);
        if (bar != null)
        {
            Check("the map prompt is withdrawn with the map", !bar.IsShown);
        }

        yield return WaitForDisplayToSettle(2f);
        yield return Capture("15_map_closed");

        if (_addedPad && _pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        Finish(_allPassed ? 0 : 1);
    }

    // ====================================================================
    // Stages
    // ====================================================================

    /// <summary>Holds the right stick for a window without judging the movement.</summary>
    private IEnumerator PushStickWindow(Vector2 stick, float seconds)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = stick });
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
    }

    /// <summary>Holds the right stick for a window and reports the cursor movement.</summary>
    private IEnumerator CursorWindow(string shot, Vector2 stick, float seconds)
    {
        Vector2 start = UiPointerState.Position;
        float begin = Time.realtimeSinceStartup;
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = stick });
        while (Time.realtimeSinceStartup - begin < seconds)
        {
            yield return null;
        }

        Vector2 end = UiPointerState.Position;
        float elapsed = Time.realtimeSinceStartup - begin;
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;

        float moved = Vector2.Distance(end, start);
        float expected = UiPointerState.SpeedPixelsPerSecond * elapsed;
        Info("stick " + Format(stick) + " for " + elapsed.ToString("0.00") + "s: "
             + Format(start) + " -> " + Format(end) + " moved " + moved.ToString("0.0")
             + "px (expected ~" + expected.ToString("0.0") + "px)");
        Check("the right stick moved the cursor (" + Format(stick) + ")", moved > 4f);
        Check("the cursor speed matches the declared speed, with no acceleration ("
              + Format(stick) + ")",
            moved > expected * 0.5f && moved < expected * 1.5f);
        Check("the stick direction moved the cursor the right way (" + Format(stick) + ")",
            (stick.x > 0.1f && end.x > start.x + 2f)
            || (stick.x < -0.1f && end.x < start.x - 2f)
            || (Mathf.Abs(stick.x) <= 0.1f && Mathf.Abs(end.x - start.x) < 6f));
        Check("the pad owns the cursor after the stick moved it", UiPointerState.GamepadOwns);
        yield return Capture(shot);
    }

    /// <summary>
    /// Drives the cursor onto a node with the stick alone: every frame the stick is
    /// pushed toward the node's projected screen position, exactly the correction a
    /// player makes. Uses only the public product surfaces.
    /// </summary>
    private IEnumerator SteerOntoNode(SurfaceResourceNode node, string shot)
    {
        SurfaceWristController wrist = _surface.Wrist;
        int frames = 0;
        int hit = -1;
        while (frames < 600)
        {
            hit = wrist.HitTestFromPointer();
            if (hit == 0)
            {
                break;
            }

            // The target is re-projected every frame: the hologram keeps a small
            // parallax tilt that follows the pointer.
            Vector2 nodePixel = WristMapCoordinates.WorldToWristMap(node.transform.position,
                wrist.MapZoom);
            Vector2 targetScreen = ScreenOf(nodePixel);
            Vector2 error = targetScreen - UiPointerState.Position;
            Vector2 stick = error.sqrMagnitude < 1f
                ? Vector2.zero
                : error.normalized * Mathf.Min(1f, 0.25f + error.magnitude / 400f);
            InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = stick });
            frames++;
            yield return null;
        }

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;

        Info("stick steering: " + frames + " frames, hit=" + wrist.HitTestFromPointer()
             + " pointer=" + Format(UiPointerState.Position)
             + " nodeWristPixel=" + Format(WristMapCoordinates.WorldToWristMap(
                 node.transform.position, wrist.MapZoom)));
        Check("the stick alone can drive the cursor onto a node marker (" + frames + " frames)",
            wrist.HitTestFromPointer() == 0);
        yield return Capture(shot);
    }

    /// <summary>
    /// The mouse takes the cursor over. Measured platform fact: a programmatic
    /// WarpCursorPosition does NOT make Windows report mouse motion, so the probe
    /// drives the mouse's own device state - the same route a physical mouse takes -
    /// with a real position and a real right-button press. (The MOVEMENT half of the
    /// rule is covered by the Phase 09B PlayMode suite, which queues a real mouse
    /// delta, and by the human walkthrough with a physical mouse.)
    /// </summary>
    private IEnumerator MouseTakeover(ControllerPromptBar bar)
    {
        Mouse mouse = Mouse.current;
        Check("a mouse device exists for the handover", mouse != null);
        if (mouse == null)
        {
            yield break;
        }

        // Move the pointer off the node first, so nothing under the cursor can react.
        yield return PushStickWindow(new Vector2(-0.6f, 0.8f), 0.28f);
        Vector2 padBefore = UiPointerState.Position;
        Vector2 target = ScreenOf(RadialWristElement.Center) + new Vector2(120f, 60f);
        float zoomBefore = _surface.Wrist.MapZoom;

        InputSystem.QueueStateEvent(mouse,
            new MouseState { position = target, delta = Vector2.zero }
                .WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right));
        bool tookOver = false;
        for (int i = 0; i < 15 && !tookOver; i++)
        {
            yield return null;
            tookOver = !UiPointerState.GamepadOwns;
        }

        Info("mouse handover: pad pos " + Format(padBefore) + " -> pointer "
             + Format(UiPointerState.Position) + " source=" + UiPointerState.Current
             + " lastDevice=" + InputDeviceTracker.LastDevice);
        Check("real mouse input takes the cursor over from the pad", tookOver);
        Check("the pointer is at the mouse position the device reported",
            Vector2.Distance(target, UiPointerState.Position) < 8f);
        Check("the handover moved the cursor", Vector2.Distance(padBefore, target) > 2f);
        Check("the last device is the mouse again",
            InputDeviceTracker.LastDevice == InputDeviceTracker.Device.KeyboardMouse);
        yield return null;
        yield return null;
        if (bar != null)
        {
            Info("prompt after the mouse took over: " + bar.PromptText);
            Check("the map prompt follows the mouse", bar.PromptText.Contains("鼠标"));
        }

        Check("the handover never changed the map view (zoom "
              + zoomBefore.ToString("0.00") + ")", Mathf.Approximately(zoomBefore,
            _surface.Wrist.MapZoom));
        Check("the map is still open after the handover", _surface.IsWristOpen);
        yield return Capture("12_mouse_takeover");

        InputSystem.QueueStateEvent(mouse, new MouseState { position = target });
        yield return null;
        yield return null;
    }

    /// <summary>The pad takes the cursor back, continuing from the mouse position.</summary>
    private IEnumerator PadResume()
    {
        Vector2 start = UiPointerState.Position;
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightStick = new Vector2(0f, 1f) });
        yield return null;
        yield return null;

        Vector2 after = UiPointerState.Position;
        float step = Vector2.Distance(after, start);
        Info("pad resume: " + Format(start) + " -> " + Format(after) + " (step "
             + step.ToString("0.0") + "px)");
        Check("the pad took the cursor back", UiPointerState.GamepadOwns);
        Check("the pad continued from the mouse's position instead of jumping",
            step < UiPointerState.SpeedPixelsPerSecond * 0.2f);
        Check("and it moved where the stick points", after.y > start.y);

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
        yield return Capture("13_pad_resumes");
    }

    /// <summary>Real keyboard input re-glyphs the prompt bar.</summary>
    private IEnumerator KeyboardGlyphCheck(ControllerPromptBar bar)
    {
        if (bar == null)
        {
            yield break;
        }

        string gamepadPrompt = bar.PromptText;
        yield return PressKey(Key.W);

        Info("prompt after a real keyboard press: " + bar.PromptText
             + " (lastDevice=" + InputDeviceTracker.LastDevice + ")");
        Check("the last input device decides the prompt glyphs",
            InputDeviceTracker.LastDevice == InputDeviceTracker.Device.KeyboardMouse);
        Check("the map prompt re-glyphs for mouse+keyboard without changing its rows",
            bar.PromptText != gamepadPrompt && bar.PromptText.Contains("鼠标")
            && bar.PromptText.Contains("Esc"));
        yield return Capture("14_prompt_keyboard");

        // Back to the pad for the closing rows. Select has no product meaning, so the
        // device switch cannot touch the map state.
        yield return PressButton(GamepadButton.Select);
        Info("prompt back on the pad: " + bar.PromptText
             + " (lastDevice=" + InputDeviceTracker.LastDevice
             + " mapOpen=" + _surface.IsWristOpen + ")");
        Check("the prompt follows the pad again, with the map still open",
            _surface.IsWristOpen
            && InputDeviceTracker.LastDevice == InputDeviceTracker.Device.Gamepad);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private bool MapVisible()
    {
        return _surface != null && _surface.Wrist != null && _surface.Wrist.Display != null
               && _surface.Wrist.Display.Visible;
    }

    /// <summary>True when a screen position is inside the declared cursor region.</summary>
    private static bool Inside(Vector2 position)
    {
        if (!UiPointerState.HasBounds)
        {
            return false;
        }

        Rect bounds = UiPointerState.Bounds;
        return position.x >= bounds.xMin - 1f && position.x <= bounds.xMax + 1f
               && position.y >= bounds.yMin - 1f && position.y <= bounds.yMax + 1f;
    }

    private Vector2 ScreenOf(Vector2 wristPixel)
    {
        if (_surface.Wrist.TryProjectWristPixelToViewport(wristPixel, out Vector3 viewport))
        {
            return new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
        }

        Fail("wrist pixel " + Format(wristPixel) + " does not project onto the display");
        return Vector2.zero;
    }

    private Vector2 NodeMarkerPixel(int nodeId)
    {
        VisualElement marker = _surface.Wrist.RootElement.Q<VisualElement>("MapNode" + nodeId);
        if (marker == null)
        {
            return Vector2.zero;
        }

        return new Vector2(marker.style.left.value.value, marker.style.top.value.value);
    }

    private IEnumerator HoldTrigger(bool right, float seconds)
    {
        GamepadState state = right
            ? new GamepadState { rightTrigger = 1f }
            : new GamepadState { leftTrigger = 1f };
        InputSystem.QueueStateEvent(_pad, state);
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        InputSystem.QueueStateEvent(_pad, new GamepadState());
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

    private IEnumerator PressKey(Key key)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            yield break;
        }

        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        yield return null;
    }

    private IEnumerator WaitForDisplayToSettle(float seconds)
    {
        Transform display = _surface.Wrist.Display != null
            ? _surface.Wrist.Display.transform
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
    /// Creates the run's virtual gamepad once and proves that a queued state event
    /// really reaches it. The proof uses the Select control: it has no meaning
    /// anywhere in the product, so the delivery check cannot disturb the state under
    /// test.
    /// </summary>
    private IEnumerator EnsureGamepadDevice()
    {
        _pad = Gamepad.current;
        _addedPad = false;
        if (_pad == null)
        {
            _pad = InputSystem.AddDevice<Gamepad>();
            _addedPad = true;
        }

        yield return WaitUntil(() => Gamepad.current == _pad, 5f);
        Check("a gamepad device is available for the evidence run", Gamepad.current == _pad);
        if (Gamepad.current != _pad)
        {
            yield break;
        }

        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.Select));
        yield return null;
        float value = _pad.selectButton.ReadValue();
        Check("a queued gamepad state event reaches the real device route (Select read "
              + value.ToString("0.00", CultureInfo.InvariantCulture) + ")", value > 0.5f);
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
    }

    private static void FaceToward(SurfacePlayerController player, Vector3 target)
    {
        Vector3 delta = target - player.transform.position;
        float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        player.SetFacing(yaw, 0f);
    }

    private static string Format(Vector2 value)
    {
        return "(" + value.x.ToString("0.0", CultureInfo.InvariantCulture) + ", "
               + value.y.ToString("0.0", CultureInfo.InvariantCulture) + ")";
    }

    private IEnumerator Capture(string suffix)
    {
        string path = Path.Combine(_folder, suffix + ".png");
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 600; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 20000)
            {
                break;
            }

            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.12f);
        Check("frame captured: " + suffix, File.Exists(path));
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private void Check(string label, bool passed)
    {
        _log.AppendLine((passed ? "PASS" : "FAIL") + " :: " + label);
        if (!passed)
        {
            _allPassed = false;
        }
    }

    private void Info(string message)
    {
        _log.AppendLine("INFO :: " + message);
    }

    private void Fail(string message)
    {
        Check(message, false);
    }

    private void Finish(int exitCode)
    {
        string resultPath = Path.Combine(_folder, "phase09b_result.txt");
        try
        {
            File.WriteAllText(resultPath,
                "=== PHASE 09B CONTROLLER INTERACTION EVIDENCE ===\n" + _log);
        }
        catch (Exception e)
        {
            Debug.LogError("[Ship09BProbe] cannot write result: " + e.Message);
        }

        Debug.Log("[Ship09BProbe] result " + (_allPassed ? "ALL PASS" : "FAILURES")
                  + " -> " + resultPath);
        Application.Quit(_allPassed ? 0 : exitCode);
    }

    private static string ParseFlag(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
