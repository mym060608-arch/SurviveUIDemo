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
///     pause menu open  >  active device panel / modal  >  console focus
///                      >  free gameplay  >  main menu
///
/// Every consumer must therefore check the state it belongs to (pause open,
/// device panel open, console focus active, player input enabled, ...) before
/// acting, and the states themselves are mutually exclusive by construction.
/// A consumer that handled the press must consume it: no lower layer may run
/// for the same frame.
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
    Interact,

    /// <summary>Wrist terminal toggle (Xbox Y / M / I) - Phase 09A_2 adds the
    /// gamepad binding the ship wrist never had; the semantic name stays
    /// platform neutral.</summary>
    Wrist
}

/// <summary>
/// The single physical→logical input mapping for UI actions.
/// </summary>
public static class UiInput
{
    /// <summary>
    /// Which UI layer is asking for an action that Escape shares between Pause
    /// and Back (Phase 08 device-panel hotfix).
    ///
    /// The gamepad never has this problem because its two controls are distinct
    /// (Menu/Start = Pause, B = Back). Escape is one key standing for two
    /// semantics, so the ASKING LAYER decides which one it is:
    ///
    ///   Gameplay- free first person / console view / wrist: Escape pauses
    ///              (there is no UI layer to leave).
    ///   Modal   - a device panel / deployment confirmation / loadout is open:
    ///              Escape is Back for that layer, so it closes the panel and
    ///              must NOT also open the pause over it.
    /// </summary>
    public enum UiLayer
    {
        /// <summary>No modal UI layer is open: Escape is the global Pause key.</summary>
        Gameplay,

        /// <summary>A modal UI layer owns Escape as Back.</summary>
        Modal
    }

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
        "Wrist=Gamepad.buttonNorth,Keyboard.mKey|iKey",
        "Navigate=Gamepad.dpad|leftStick,Keyboard.arrowKeys"
    };

    private static int _injectedPauseFrame = -1000;
    private static int _injectedEscapeFrame = -1000;
    private static int _injectedWristFrame = -1000;
    private static int _backConsumedFrame = -1000;

    /// <summary>
    /// Marks the Back action as CONSUMED for this frame (Phase 08 §14.3).
    ///
    /// Every layer that handled a Back press calls this, because two open layers
    /// can legitimately see the same physical press in one frame (the pause menu
    /// sits ABOVE an open device panel) and MonoBehaviour update order is not
    /// defined. Consuming makes the guarantee order-independent: the first layer
    /// to handle the press owns it, every other layer then sees
    /// <see cref="BackPressed"/> as false for that frame, and Escape cannot be
    /// re-read as the global Pause either (see
    /// <see cref="PausePressed(UiLayer)"/>).
    ///
    /// Layers that can never be open at the same time (console focus vs device
    /// panels) do not need it; the state gating already separates them.
    /// </summary>
    public static void ConsumeBackForFrame()
    {
        _backConsumedFrame = Time.frameCount;
    }

    /// <summary>True when a layer already handled this frame's Back press.</summary>
    public static bool BackConsumedThisFrame => Time.frameCount == _backConsumedFrame;

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

    /// <summary>
    /// Automation seam: reports an ESCAPE press for the next frame.
    ///
    /// Escape is the one key shared by two semantics, so it needs its own seam:
    /// injecting it (instead of a logical action) lets a test verify the LAYER
    /// resolution itself - Escape closes an open device panel and must not also
    /// open the pause over it, while the same Escape in gameplay pauses.
    /// </summary>
    public static void InjectEscapeForAutomation()
    {
        _injectedEscapeFrame = Time.frameCount + 1;
    }

    /// <summary>Escape (the keyboard's Back/Pause key) this frame.</summary>
    private static bool EscapePressedThisFrame()
    {
        if (Time.frameCount == _injectedEscapeFrame)
        {
            return true;
        }

        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
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

    /// <summary>
    /// True when the Back action was pressed this frame (menu / modal layers:
    /// Xbox B or Escape). A modal layer that is open consumes it - the ship's
    /// device panels close through this, and it must not fall through to the
    /// gameplay layers in the same frame.
    /// </summary>
    public static bool BackPressed()
    {
        if (BackConsumedThisFrame)
        {
            return false;
        }

        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonEast.wasPressedThisFrame)
        {
            return true;
        }

        return EscapePressedThisFrame();
    }

    /// <summary>
    /// True when the global Pause action was pressed this frame, resolved for
    /// the layer that is asking (see <see cref="UiLayer"/>).
    ///
    /// Menu/Start always pauses - it works in free first person, inside the
    /// console view and above an OPEN device panel (the panel stays as the
    /// frozen underlying state). Escape pauses only in the gameplay layer,
    /// because inside a modal layer Escape IS that layer's Back key and the
    /// press must close the panel instead of stacking a pause on top of it.
    /// </summary>
    public static bool PausePressed(UiLayer layer)
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

        if (layer == UiLayer.Modal)
        {
            return false;
        }

        // Escape may have been consumed as Back by a modal layer THIS frame (the
        // panel that just closed): then the same press is not a pause either.
        if (BackConsumedThisFrame)
        {
            return false;
        }

        return EscapePressedThisFrame();
    }

    /// <summary>
    /// True when the global Pause action was pressed this frame for a gameplay
    /// layer (Escape and Menu/Start both count).
    /// </summary>
    public static bool PausePressed()
    {
        return PausePressed(UiLayer.Gameplay);
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
    /// Automation seam: reports a Wrist press for the next frame, so a test can
    /// drive the REAL wrist layer (authority -> presentation bridge -> animator)
    /// on harnesses that cannot deliver device state to the batch-mode player.
    /// </summary>
    public static void InjectWristForAutomation()
    {
        _injectedWristFrame = Time.frameCount + 1;
    }

    /// <summary>
    /// True when the Wrist action was pressed this frame.
    ///
    /// The ship wrist had no gamepad binding before Phase 09A_2 (only the M / I
    /// keys), so the Xbox Y / PlayStation triangle control is bound here as the
    /// semantic Wrist action: it is the one face control that neither Confirm
    /// (buttonSouth), Back (buttonEast) nor Pause (Menu/Start) uses, so no
    /// existing contract is disturbed. Physical mapping stays in this file - no
    /// gameplay class names a control directly.
    /// </summary>
    public static bool WristPressed()
    {
        if (Time.frameCount == _injectedWristFrame)
        {
            return true;
        }

        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonNorth.wasPressedThisFrame)
        {
            return true;
        }

        Keyboard keyboard = Keyboard.current;
        return keyboard != null
               && (keyboard.mKey.wasPressedThisFrame || keyboard.iKey.wasPressedThisFrame);
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
