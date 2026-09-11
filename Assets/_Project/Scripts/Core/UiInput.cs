using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Logical UI actions (Phase 08 pause work).
///
/// The project separates SEMANTIC actions from PHYSICAL controls:
///
///     logical : Confirm / Back / Pause   (+ navigation)
///     physical: Gamepad.buttonSouth / buttonEast / startButton, keys
///
/// Nothing outside this file may name a physical control for a UI action
/// (no AButton/BButton semantics, no numeric joystick button indices). Games
/// logic asks for <see cref="UiAction.Confirm"/> and this file decides that an
/// Xbox A, an E key or Enter all mean it.
///
/// Dispatch rule (this is what keeps one press from firing two actions):
/// an action belongs to exactly ONE owner, chosen by STATE, never by update
/// order:
///
///     pause menu open  >  console focus  >  free gameplay  >  main menu
///
/// Every consumer must therefore check the state it belongs to (pause open,
/// console focus active, player input enabled, ...) before acting, and the
/// states themselves are mutually exclusive by construction.
/// </summary>
public enum UiAction
{
    /// <summary>Activate the focused element (Xbox A / E / Enter / Space).</summary>
    Confirm,

    /// <summary>Leave the current UI layer (Xbox B / Escape).</summary>
    Back,

    /// <summary>Global pause toggle (Gamepad Menu/Start / Escape).</summary>
    Pause,

    /// <summary>Context interaction (Xbox A / E) - gaze-driven in the world.</summary>
    Interact
}

/// <summary>
/// The single physical→logical input mapping for UI actions.
/// </summary>
public static class UiInput
{
    /// <summary>Stick magnitude that counts as a navigation press.</summary>
    public const float StickThreshold = 0.55f;

    /// <summary>
    /// The authoritative binding table (logical action -&gt; physical controls).
    /// It is data on purpose: reviewers can read it and tests can pin it, so the
    /// semantic split cannot silently drift back to "A button" naming.
    /// </summary>
    public static readonly string[] BindingTable =
    {
        "Confirm=Gamepad.buttonSouth,Keyboard.eKey|enterKey|spaceKey",
        "Interact=Gamepad.buttonSouth,Keyboard.eKey",
        "Back=Gamepad.buttonEast,Keyboard.escapeKey",
        "Pause=Gamepad.startButton,Keyboard.escapeKey",
        "Navigate=Gamepad.dpad|leftStick,Keyboard.arrowKeys"
    };

    private static int _injectedPauseFrame = -1000;

    /// <summary>
    /// Automation seam: reports a Pause press for the current frame.
    ///
    /// The batch-mode test harness cannot deliver keyboard state (a synthetic
    /// Keyboard device never receives it), so tests inject the LOGICAL action
    /// here while every layer below - the pause menu, the input gating, the
    /// timeScale handling and the routing - still runs for real. The physical
    /// mapping itself is pinned by <see cref="BindingTable"/>.
    /// </summary>
    public static void InjectPauseForAutomation()
    {
        // The caller (a test coroutine) runs AFTER Update in its frame, so the
        // press is aimed at the NEXT frame - the same phase a real device press
        // is processed in.
        _injectedPauseFrame = Time.frameCount + 1;
    }

    /// <summary>True when the Confirm action was pressed this frame.</summary>
    public static bool ConfirmPressed()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonSouth.wasPressedThisFrame)
        {
            return true;
        }

        Keyboard keyboard = Keyboard.current;
        return keyboard != null
               && (keyboard.eKey.wasPressedThisFrame
                   || keyboard.enterKey.wasPressedThisFrame
                   || keyboard.numpadEnterKey.wasPressedThisFrame
                   || keyboard.spaceKey.wasPressedThisFrame);
    }

    /// <summary>True when the Back action was pressed this frame (menu layers).</summary>
    public static bool BackPressed()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonEast.wasPressedThisFrame)
        {
            return true;
        }

        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
    }

    /// <summary>
    /// True when the global Pause action was pressed this frame.
    /// Escape is shared with Back on purpose: gameplay layers treat Escape as
    /// Pause, overlay layers treat it as Back, and the two never run at once
    /// because the pause menu owns the whole input context while it is open.
    /// </summary>
    public static bool PausePressed()
    {
        if (Time.frameCount == _injectedPauseFrame)
        {
            return true;
        }

        Gamepad pad = Gamepad.current;
        if (pad != null && pad.startButton.wasPressedThisFrame)
        {
            return true;
        }

        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
    }

    /// <summary>
    /// True when the console-view exit action was pressed this frame
    /// (Xbox B / E). Escape is deliberately NOT part of it: Escape stays the
    /// global Pause key so the two can never fight.
    /// </summary>
    public static bool ConsoleExitPressed()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonEast.wasPressedThisFrame)
        {
            return true;
        }

        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.eKey.wasPressedThisFrame;
    }

    /// <summary>True when the context interaction action was pressed (A / E).</summary>
    public static bool InteractPressed()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonSouth.wasPressedThisFrame)
        {
            return true;
        }

        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.eKey.wasPressedThisFrame;
    }

    /// <summary>
    /// Vertical navigation: -1 = up, 1 = down, 0 = nothing. Reads the D-Pad, the
    /// left stick and the arrow keys.
    /// </summary>
    public static int NavigationDirection()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            float stick = pad.leftStick.ReadValue().y;
            if (stick > StickThreshold)
            {
                return -1;
            }

            if (stick < -StickThreshold)
            {
                return 1;
            }

            Vector2 dpad = pad.dpad.ReadValue();
            if (dpad.y > 0.5f)
            {
                return -1;
            }

            if (dpad.y < -0.5f)
            {
                return 1;
            }
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.upArrowKey.wasPressedThisFrame)
            {
                return -1;
            }

            if (keyboard.downArrowKey.wasPressedThisFrame)
            {
                return 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// True on the frame the gamepad's confirm control was pressed.
    ///
    /// Used to recognise the pointer click Unity's runtime UI SYNTHESISES from a
    /// gamepad confirm press (its UI "Click" action shares the same control):
    /// that synthetic click must never activate a menu item, otherwise one press
    /// performs two actions.
    /// </summary>
    public static bool GamepadConfirmPressedThisFrame()
    {
        Gamepad pad = Gamepad.current;
        return pad != null && pad.buttonSouth.wasPressedThisFrame;
    }

    /// <summary>Action for a "confirm" style prompt on the current device.</summary>
    public static UiAction ConfirmAction => UiAction.Confirm;

    /// <summary>True when the current device is a gamepad (presentation helper).</summary>
    public static bool GamepadActive => InputDeviceTracker.GamepadActive;
}
