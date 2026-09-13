using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

/// <summary>
/// Which input device the player is CURRENTLY using (Phase 08 Revision 2).
///
/// The cover and the ship interior both have to answer one question - "is the
/// player on a gamepad or on mouse+keyboard right now?" - so that interaction
/// prompts read "(A) 使用终端" for a controller and "E 使用终端" for a keyboard
/// instead of stacking both. This is deliberately tiny static state plus a poll;
/// it is not a controller UX framework (global focus navigation, LB/RB tabs and
/// the full prompt bar belong to Phase 09).
///
/// The default is keyboard/mouse, and the device only changes when the other
/// device actually produces input, so a connected-but-idle gamepad never
/// rewrites the player-facing copy.
/// </summary>
public static class InputDeviceTracker
{
    public enum Device
    {
        KeyboardMouse,
        Gamepad
    }

    /// <summary>
    /// Which gamepad FAMILY the connected pad belongs to (Phase 09B). Only the
    /// GLYPH the player reads changes; the logical actions and their physical
    /// controls never do. Xbox and a generic HID pad share the A/B/X/Y wording
    /// because that is what those pads print on the shell.
    /// </summary>
    public enum GamepadFamily
    {
        Xbox,
        PlayStation,
        Generic
    }

    /// <summary>Stick dead zone for "the player is using the gamepad".</summary>
    private const float StickDeadZone = 0.25f;

    /// <summary>Last device that produced input (keyboard/mouse by default).</summary>
    public static Device LastDevice { get; private set; } = Device.KeyboardMouse;

    /// <summary>True while the gamepad was the last device used.</summary>
    public static bool GamepadActive => LastDevice == Device.Gamepad;

    /// <summary>True while mouse+keyboard was the last device used.</summary>
    public static bool KeyboardMouseActive => LastDevice == Device.KeyboardMouse;

    // ---- Pointer self-warp bookkeeping (Phase 09B) ----
    //
    // The map cursor is moved with the right stick by WARPING the real OS cursor
    // to the virtual pointer position (the cursor the player sees IS the OS
    // cursor with the runtime dot texture - there is no UI Toolkit cursor). That
    // warp makes the mouse device report a position change, which would look like
    // "the player moved the mouse" and flip the prompt bar back to the mouse on
    // the very next frame. The page that warps announces it here, so the same
    // change can be recognised as our own and ignored.
    private static int _selfWarpFrame = -1000;
    private static Vector2 _selfWarpPosition;

    /// <summary>Announces that the engine moved the OS cursor itself.</summary>
    public static void NotifyPointerWarp(Vector2 position)
    {
        _selfWarpFrame = Time.frameCount;
        _selfWarpPosition = position;
    }

    /// <summary>
    /// True when the mouse really moved this frame: a delta that is NOT the echo of
    /// our own cursor warp, or a button / wheel press. This is the single answer
    /// both the device poll and the shared pointer authority use, so "did the
    /// player touch the mouse?" can never be answered two different ways.
    /// </summary>
    public static bool MouseProducedRealInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return false;
        }

        bool button = mouse.leftButton.wasPressedThisFrame
                      || mouse.rightButton.wasPressedThisFrame
                      || Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f;
        if (button)
        {
            return true;
        }

        // A warp echo is only ever the frame we warped or the one right after it,
        // and the device then reports the position we asked for.
        if (Time.frameCount <= _selfWarpFrame + 1
            && Vector2.Distance(mouse.position.ReadValue(), _selfWarpPosition) < 6f)
        {
            return false;
        }

        return mouse.delta.ReadValue().sqrMagnitude > 4f;
    }

    /// <summary>
    /// Interaction key shown in prompts: the gamepad face button or E.
    /// </summary>
    public static string ActionPrefix => GamepadActive ? "(A)" : "E";

    /// <summary>Formats one interaction prompt for the current device.</summary>
    public static string FormatPrompt(string action)
    {
        return ActionPrefix + "  " + action;
    }

    /// <summary>
    /// Key glyph for a logical action on the current device, as a BARE label
    /// ("A", "B", "Esc", "Menu"). This is the presentation side of the semantic
    /// split - the binding itself always stays in <see cref="UiInput"/>.
    /// </summary>
    public static string KeyLabel(UiAction action)
    {
        switch (action)
        {
            case UiAction.Confirm:
            case UiAction.Interact:
                return GamepadActive ? "A" : "E";
            case UiAction.Back:
                return GamepadActive ? "B" : "Esc";
            case UiAction.Pause:
                return GamepadActive ? "Menu" : "Esc";
            case UiAction.Wrist:
                return GamepadActive ? "Y" : "M";
            case UiAction.Map:
            case UiAction.ContextAction:
                return GamepadActive ? "X" : "M";
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// The gamepad family of the CURRENT pad, used for glyph selection only.
    /// </summary>
    public static GamepadFamily CurrentFamily
    {
        get
        {
            Gamepad pad = Gamepad.current;
            return pad == null ? GamepadFamily.Generic : FamilyOf(pad);
        }
    }

    /// <summary>
    /// Classifies a live device from everything it reports: its name, the product
    /// string, the manufacturer and the interface it arrived on ("XInput", "HID",
    /// ...). A native Xbox pad reports its family through the interface, a
    /// DualSense through the product string, so both are read.
    /// </summary>
    public static GamepadFamily FamilyOf(InputDevice device)
    {
        if (device == null)
        {
            return GamepadFamily.Generic;
        }

        InputDeviceDescription description = device.description;
        return FamilyOf(string.Join(" ",
            device.name ?? string.Empty,
            description.product ?? string.Empty,
            description.manufacturer ?? string.Empty,
            description.interfaceName ?? string.Empty));
    }

    /// <summary>
    /// Classifies a device name into a pad family. Pure so it can be pinned
    /// without a device, and deliberately name-driven: the Input System reports
    /// "XInputControllerWindows", "DualSense Wireless Controller", a bare
    /// "Gamepad" for a synthetic device, and vendor strings for everything else.
    /// </summary>
    public static GamepadFamily FamilyOf(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            return GamepadFamily.Generic;
        }

        string name = deviceName.ToLowerInvariant();

        // Xbox first: "Xbox Wireless Controller" also contains the generic
        // "wireless controller" string a DualShock 4 reports on Windows.
        if (name.Contains("xbox") || name.Contains("xinput"))
        {
            return GamepadFamily.Xbox;
        }

        if (name.Contains("dualsense") || name.Contains("dualshock")
            || name.Contains("playstation") || name.Contains("ps4")
            || name.Contains("ps5") || name.Contains("wireless controller"))
        {
            return GamepadFamily.PlayStation;
        }

        return GamepadFamily.Generic;
    }

    /// <summary>
    /// The glyph a PROMPT shows for a logical action on the current device. This is
    /// the Phase 09B prompt contract: logic always names the action, presentation
    /// asks here, and only the printed glyph changes between Xbox, a
    /// PlayStation-compatible pad and mouse+keyboard.
    /// </summary>
    public static string PromptGlyph(UiAction action)
    {
        if (!GamepadActive)
        {
            switch (action)
            {
                case UiAction.Confirm:
                case UiAction.Interact:
                    return "E";
                case UiAction.Back:
                case UiAction.Pause:
                    return "Esc";
                case UiAction.Wrist:
                case UiAction.Map:
                case UiAction.ContextAction:
                    return "M";
                case UiAction.PreviousTab:
                    return "Q";
                case UiAction.NextTab:
                    return "Tab";
                case UiAction.MapCursor:
                    return "鼠标";
                case UiAction.ZoomIn:
                case UiAction.ZoomOut:
                    return "滚轮";
                case UiAction.Navigate:
                    // Phase 09B PASS 3: the wrist's ordinary UI moves its focus
                    // with the arrows on a keyboard.
                    return "方向键";
                default:
                    return string.Empty;
            }
        }

        switch (CurrentFamily)
        {
            case GamepadFamily.PlayStation:
                switch (action)
                {
                    case UiAction.Confirm:
                    case UiAction.Interact:
                        return "✕";
                    case UiAction.Back:
                        return "○";
                    case UiAction.Wrist:
                        return "△";
                    case UiAction.Map:
                    case UiAction.ContextAction:
                        return "□";
                    case UiAction.Pause:
                        return "Options";
                    case UiAction.PreviousTab:
                        return "L1";
                    case UiAction.NextTab:
                        return "R1";
                    case UiAction.MapCursor:
                        return "RS";
                    case UiAction.ZoomIn:
                        return "R2";
                    case UiAction.ZoomOut:
                        return "L2";
                    case UiAction.Navigate:
                        return "D-Pad";
                    default:
                        return string.Empty;
                }

            default:
                switch (action)
                {
                    case UiAction.Confirm:
                    case UiAction.Interact:
                        return "A";
                    case UiAction.Back:
                        return "B";
                    case UiAction.Wrist:
                        return "Y";
                    case UiAction.Map:
                    case UiAction.ContextAction:
                        return "X";
                    case UiAction.Pause:
                        return "Menu";
                    case UiAction.PreviousTab:
                        return "LB";
                    case UiAction.NextTab:
                        return "RB";
                    case UiAction.MapCursor:
                        return "RS";
                    case UiAction.ZoomIn:
                        return "RT";
                    case UiAction.ZoomOut:
                        return "LT";
                    case UiAction.Navigate:
                        return "D-Pad";
                    default:
                        return string.Empty;
                }
        }
    }

    /// <summary>Formats a bracketed prompt for a logical action: "[B] 返回".</summary>
    public static string FormatPrompt(UiAction action, string text)
    {
        return "[" + KeyLabel(action) + "] " + text;
    }

    /// <summary>
    /// The pause menu's navigation hint: the device's own wording, never a
    /// physical control name leaking into another device's presentation.
    /// </summary>
    public static string NavigationLabel => GamepadActive ? "[LS / D-Pad]" : "[↑/↓]";

    /// <summary>Confirm glyph for the pause hint line.</summary>
    public static string ConfirmLabel => GamepadActive ? "[A]" : "[Enter]";

    /// <summary>Back glyph for the pause hint line.</summary>
    public static string BackLabel => GamepadActive ? "[B]" : "[Esc]";

    /// <summary>The full pause menu hint line for the current device.</summary>
    public static string PauseHint =>
        NavigationLabel + " 选择    " + ConfirmLabel + " 确认    " + BackLabel + " 返回";

    /// <summary>
    /// Samples the devices once and updates <see cref="LastDevice"/> when the
    /// other device produced input this frame. Returns true when the gamepad was
    /// the device that produced input.
    /// </summary>
    public static bool Poll()
    {
        if (GamepadProducedInput())
        {
            LastDevice = Device.Gamepad;
            return true;
        }

        if (KeyboardMouseProducedInput())
        {
            LastDevice = Device.KeyboardMouse;
        }

        return false;
    }

    /// <summary>True when the gamepad produced meaningful input this frame.</summary>
    public static bool GamepadProducedInput()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null)
        {
            return false;
        }

        if (pad.leftStick.ReadValue().sqrMagnitude > StickDeadZone * StickDeadZone
            || pad.rightStick.ReadValue().sqrMagnitude > StickDeadZone * StickDeadZone
            || pad.dpad.ReadValue().sqrMagnitude > 0.5f * 0.5f)
        {
            return true;
        }

        return pad.buttonSouth.wasPressedThisFrame
               || pad.buttonEast.wasPressedThisFrame
               || pad.buttonNorth.wasPressedThisFrame
               || pad.buttonWest.wasPressedThisFrame
               || pad.startButton.wasPressedThisFrame
               || pad.selectButton.wasPressedThisFrame
               || pad.leftShoulder.wasPressedThisFrame
               || pad.rightShoulder.wasPressedThisFrame
               || pad.leftTrigger.ReadValue() > 0.4f
               || pad.rightTrigger.ReadValue() > 0.4f;
    }

    /// <summary>True when mouse or keyboard produced input this frame.</summary>
    public static bool KeyboardMouseProducedInput()
    {
        if (MouseProducedRealInput())
        {
            return true;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        return keyboard.anyKey.wasPressedThisFrame;
    }

    /// <summary>Forces the gamepad state (used by the entry prompt switch).</summary>
    public static void NotifyGamepad()
    {
        LastDevice = Device.Gamepad;
    }

    /// <summary>Forces the keyboard/mouse state.</summary>
    public static void NotifyKeyboardMouse()
    {
        LastDevice = Device.KeyboardMouse;
    }

    /// <summary>Restores the launch default (automation never leaks a device).</summary>
    public static void ResetToDefaults()
    {
        LastDevice = Device.KeyboardMouse;
        _selfWarpFrame = -1000;
        _selfWarpPosition = Vector2.zero;
    }
}
