using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The ONE pointer authority for screen-space UI that is driven by both the mouse
/// and the pad (Phase 09B PASS 2).
///
/// The surface map keeps its existing cursor: the dot/ring the player sees is the
/// REAL OS cursor, assigned through Cursor.SetCursor by
/// <see cref="WristCursorService"/>. There is deliberately no second, gamepad-only
/// map and no UI Toolkit cursor overlay, so "the right stick moves the map cursor"
/// has exactly one honest implementation:
///
///   * the right stick integrates a virtual pointer position at a steady speed
///     (no acceleration curve), clamped to the region the page declares;
///   * the OS cursor is WARPED to that position, so the cursor the player already
///     knows simply moves - the same dot, the same hover ring;
///   * while the mouse is the source the position is just the real mouse position,
///     unchanged;
///   * whoever produced input last owns the pointer, and the handover keeps the
///     CURRENT position, so the pad continues from wherever the mouse left the dot
///     and the mouse takes over the instant it moves.
///
/// Consumers read <see cref="Position"/> instead of Mouse.current.position: the
/// wrist map's hover, its Confirm hit-test and its parallax all go through here,
/// so a mouse click and a pad Confirm can never disagree about where the cursor is.
/// </summary>
public static class UiPointerState
{
    public enum Source
    {
        Mouse,
        Gamepad
    }

    /// <summary>
    /// Full-deflection cursor speed as a fraction of the smaller screen dimension
    /// per second. 0.62 crosses a 1080p frame in about 1.6 s: fast enough to reach
    /// a node, slow enough to place the dot on it.
    /// </summary>
    public const float SpeedFractionPerSecond = 0.62f;

    /// <summary>Automation/probe switch for the OS cursor warp.</summary>
    public static bool WarpCursorWithGamepad = true;

    /// <summary>Largest per-frame step the pad cursor may integrate (50 ms).</summary>
    private const float MaxStepSeconds = 0.05f;

    private static Vector2 _position;
    private static Source _source = Source.Mouse;
    private static bool _initialized;
    private static bool _movedByGamepadThisFrame;

    private static Rect _bounds;
    private static bool _hasBounds;

    /// <summary>The authoritative pointer position in screen pixels (bottom-left origin).</summary>
    public static Vector2 Position => _position;

    /// <summary>Which device owns the pointer right now.</summary>
    public static Source Current => _source;

    /// <summary>True while the pad is driving the pointer.</summary>
    public static bool GamepadOwns => _source == Source.Gamepad;

    /// <summary>True when the right stick actually moved the pointer this frame.</summary>
    public static bool GamepadMovedThisFrame => _movedByGamepadThisFrame;

    /// <summary>
    /// True when a real device is behind the pointer: the pad is driving it, or a
    /// mouse exists at all. Consumers that CONTINUOUSLY overwrite a value from the
    /// pointer (the hologram parallax does) must ask this first, so an automation
    /// run with no mouse device never erases a value a test set on purpose.
    /// </summary>
    public static bool IsDrivenByDevice =>
        _source == Source.Gamepad || Mouse.current != null;

    /// <summary>Cursor speed in screen pixels per second at full deflection.</summary>
    public static float SpeedPixelsPerSecond =>
        SpeedFractionPerSecond * Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));

    /// <summary>True when a page declared a clamp region for the pad cursor.</summary>
    public static bool HasBounds => _hasBounds;

    /// <summary>The declared clamp region as a rect (screen pixels, bottom-left origin).</summary>
    public static Rect Bounds => _bounds;

    /// <summary>
    /// Declares the region the pad may move the pointer inside (screen pixels,
    /// bottom-left origin). The map passes the projected display quad, which is what
    /// keeps the cursor from being pushed off the hologram and lost
    /// ("光标不能永久跑出有效地图区域"). The region is intersected with the visible
    /// screen, so the cursor can never be parked somewhere the player cannot see
    /// (the hologram's transparent edges project past the frame). The mouse is never
    /// clamped.
    /// </summary>
    public static void SetBounds(Rect screenRect)
    {
        float screenWidth = Mathf.Max(1f, Screen.width);
        float screenHeight = Mathf.Max(1f, Screen.height);
        Rect visible = Rect.MinMaxRect(
            Mathf.Max(screenRect.xMin, 0f),
            Mathf.Max(screenRect.yMin, 0f),
            Mathf.Min(screenRect.xMax, screenWidth),
            Mathf.Min(screenRect.yMax, screenHeight));

        if (visible.width >= 2f && visible.height >= 2f)
        {
            screenRect = visible;
        }

        _bounds = screenRect;
        _hasBounds = screenRect.width >= 2f && screenRect.height >= 2f;
    }

    /// <summary>Drops the declared region (the page closed).</summary>
    public static void ClearBounds()
    {
        _hasBounds = false;
    }

    /// <summary>
    /// Advances the pointer one frame and returns the position every consumer must
    /// use. Safe to call once per frame; extra calls are harmless (the mouse branch
    /// is idempotent and the pad branch integrates by the given delta).
    /// </summary>
    public static Vector2 Tick(float deltaTime)
    {
        Mouse mouse = Mouse.current;
        if (!_initialized)
        {
            _position = mouse != null
                ? mouse.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _source = Source.Mouse;
            _initialized = true;
        }

        // The pad owns the pointer for as long as the stick is actually deflected:
        // a real stick push must never be overruled by a warp echo.
        Vector2 stick = UiInput.MapCursorVector();
        if (stick != Vector2.zero)
        {
            _source = Source.Gamepad;
            _movedByGamepadThisFrame = true;

            // A frame hitch must never teleport the cursor: the step is capped at
            // one 50 ms frame, so the speed the player feels stays constant.
            float step = Mathf.Min(Mathf.Max(0f, deltaTime), MaxStepSeconds);
            _position = Clamp(_position + stick * SpeedPixelsPerSecond * step, mouse);
            Warp(mouse);
            return _position;
        }

        _movedByGamepadThisFrame = false;

        if (_source == Source.Gamepad)
        {
            // Stay where the pad left the cursor. Only a REAL mouse move (not the
            // echo of our own warp) hands the pointer back, and it takes over at
            // the mouse's own position immediately.
            if (mouse != null && InputDeviceTracker.MouseProducedRealInput())
            {
                _source = Source.Mouse;
                _position = mouse.position.ReadValue();
            }

            return _position;
        }

        if (mouse != null)
        {
            _position = mouse.position.ReadValue();
        }

        return _position;
    }

    private static Vector2 Clamp(Vector2 position, Mouse mouse)
    {
        if (_hasBounds)
        {
            position.x = Mathf.Clamp(position.x, _bounds.xMin, _bounds.xMax);
            position.y = Mathf.Clamp(position.y, _bounds.yMin, _bounds.yMax);
        }
        else
        {
            position.x = Mathf.Clamp(position.x, 0f, Mathf.Max(1f, Screen.width - 1f));
            position.y = Mathf.Clamp(position.y, 0f, Mathf.Max(1f, Screen.height - 1f));
        }

        return position;
    }

    private static void Warp(Mouse mouse)
    {
        if (!WarpCursorWithGamepad || mouse == null)
        {
            return;
        }

        mouse.WarpCursorPosition(_position);

        // The warp makes the mouse device report a position change; announcing it
        // is what keeps that echo from being read as "the player moved the mouse".
        InputDeviceTracker.NotifyPointerWarp(_position);
    }

    /// <summary>
    /// Automation seam: places the pointer without a device, so a test or probe can
    /// start from a known cursor position (the pad then continues from there).
    /// </summary>
    public static void PlaceForAutomation(Vector2 position, Source source)
    {
        _position = position;
        _source = source;
        _initialized = true;
        _movedByGamepadThisFrame = false;
    }

    /// <summary>Forgets the pointer state (a scene change must not leak a cursor).</summary>
    public static void ResetForAutomation()
    {
        _position = Vector2.zero;
        _source = Source.Mouse;
        _initialized = false;
        _movedByGamepadThisFrame = false;
        _hasBounds = false;
        WarpCursorWithGamepad = true;
    }
}
