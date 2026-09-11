using UnityEngine;
using UnityEngine.InputSystem;

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

    /// <summary>Stick dead zone for "the player is using the gamepad".</summary>
    private const float StickDeadZone = 0.25f;

    /// <summary>Last device that produced input (keyboard/mouse by default).</summary>
    public static Device LastDevice { get; private set; } = Device.KeyboardMouse;

    /// <summary>True while the gamepad was the last device used.</summary>
    public static bool GamepadActive => LastDevice == Device.Gamepad;

    /// <summary>True while mouse+keyboard was the last device used.</summary>
    public static bool KeyboardMouseActive => LastDevice == Device.KeyboardMouse;

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
               || pad.rightShoulder.wasPressedThisFrame;
    }

    /// <summary>True when mouse or keyboard produced input this frame.</summary>
    public static bool KeyboardMouseProducedInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null
            && (mouse.delta.ReadValue().sqrMagnitude > 4f
                || mouse.leftButton.wasPressedThisFrame
                || mouse.rightButton.wasPressedThisFrame
                || mouse.scroll.ReadValue().sqrMagnitude > 0.01f))
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
    }
}
