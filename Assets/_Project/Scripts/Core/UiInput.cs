using System;
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
    Wrist,

    /// <summary>
    /// Surface map toggle (Xbox X / PlayStation Square). Phase 09A_4: the planet
    /// surface map had NO gamepad binding at all - the surface only polled the M
    /// and I keys. The map is the wrist's central map, so this action and
    /// <see cref="Wrist"/> drive the same surface presentation; the semantic name
    /// stays platform neutral (no XButton / PlayStationSquare anywhere in logic).
    /// </summary>
    Map
}

/// <summary>
/// The single physical→logical input mapping for UI actions.
/// </summary>
public static class UiInput
{
    /// <summary>
    /// Which UI layer owns Escape, and what Escape means there.
    ///
    /// The gamepad never has this problem because its two controls are distinct
    /// (Menu/Start = Pause, B = Back). Escape is one key standing for two
    /// semantics, so the ASKING LAYER decides which one it is:
    ///
    ///   Escape = Back-or-Pause-fallback
    ///   --------------------------------
    ///   Escape is handed to the highest-priority LOCAL layer that can go back.
    ///   Only when no local layer can, Escape is the global Pause key.
    ///
    /// Priority (lowest value wins = declared by enum order):
    ///
    ///   PauseMenu    the pause is open              -> Escape resumes
    ///   Modal        device panel / deployment      -> Escape closes it
    ///   Wrist        the wrist terminal is open     -> Escape closes it
    ///   ConsoleFocus a console view is focused      -> Escape leaves the view
    ///   Gameplay     free first person: NO local back -> Escape is Pause
    ///
    /// The resolution is STATE based, never update-order based: every layer
    /// publishes its own state through <see cref="SetLayerActive"/>, and the
    /// consumers (the pause menu, the panels, the wrist, the console view) ask
    /// <see cref="ActiveBackLayer"/> before acting. Menu/Start stays a global
    /// pause in every layer, and the pause can always be opened ABOVE the
    /// current local layer, which then stays as the frozen underlying state.
    /// </summary>
    public enum UiLayer
    {
        /// <summary>The pause menu is open: Escape (and B) resumes the game.</summary>
        PauseMenu = 0,

        /// <summary>A modal UI layer (device panel / deployment confirmation) owns Escape as Back.</summary>
        Modal = 1,

        /// <summary>The wrist terminal is open: Escape closes it.</summary>
        Wrist = 2,

        /// <summary>A console view is focused: Escape leaves it and restores free first person.</summary>
        ConsoleFocus = 3,

        /// <summary>
        /// No local layer can go back: Escape is the global Pause key. This is
        /// also the value <see cref="ActiveBackLayer"/> reports when nothing is
        /// open.
        /// </summary>
        Gameplay = 4
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
        "Map=Gamepad.buttonWest,Keyboard.mKey|iKey",
        "Navigate=Gamepad.dpad|leftStick,Keyboard.arrowKeys"
    };

    private static int _injectedPauseFrame = -1000;
    private static int _injectedEscapeFrame = -1000;
    private static int _injectedWristFrame = -1000;
    private static int _injectedInteractFrame = -1000;
    private static int _backConsumedFrame = -1000;

    private static readonly bool[] LayerActive =
        new bool[Enum.GetValues(typeof(UiLayer)).Length];

    /// <summary>
    /// Publishes whether a local layer can go back right now.
    ///
    /// Every layer that owns a Back step reports its OWN authoritative state here
    /// (the pause reports "I am open", the overlay "I am showing", the wrist "I
    /// am open", the console view "I am focused") instead of being polled in some
    /// script order. That is what makes Escape resolution independent of
    /// MonoBehaviour update order: the value only changes with the state it
    /// describes, so every consumer in the same frame sees the same answer no
    /// matter who runs first.
    ///
    /// A layer may call this from its state setter and/or republish it in its own
    /// tick (the same value), which removes any chance of a stale board.
    /// </summary>
    public static void SetLayerActive(UiLayer layer, bool active)
    {
        int index = (int)layer;
        if (index >= 0 && index < LayerActive.Length)
        {
            LayerActive[index] = active;
        }
    }

    /// <summary>
    /// The highest-priority local layer that can consume Back this frame, or
    /// <see cref="UiLayer.Gameplay"/> when nothing can (free first person).
    /// </summary>
    public static UiLayer ActiveBackLayer
    {
        get
        {
            for (int index = 0; index < (int)UiLayer.Gameplay; index++)
            {
                if (LayerActive[index])
                {
                    return (UiLayer)index;
                }
            }

            return UiLayer.Gameplay;
        }
    }

    /// <summary>True when this layer is the one Escape belongs to right now.</summary>
    public static bool IsTopBackLayer(UiLayer layer)
    {
        return layer != UiLayer.Gameplay && ActiveBackLayer == layer;
    }

    /// <summary>
    /// True when THIS local layer owns a Back press this frame. Local layers use
    /// this instead of <see cref="BackPressed"/> so a lower layer can never act
    /// on a press that belongs to a higher one (the pause above an open panel,
    /// the pause above the wrist, the pause above the console view).
    /// </summary>
    public static bool BackPressedForLayer(UiLayer layer)
    {
        return IsTopBackLayer(layer) && BackPressed();
    }

    /// <summary>
    /// Automation seam: forgets every published layer. Tests use it so a scene
    /// from a previous test can never leave a layer flagged as open; the game
    /// code only ever moves these flags together with the state they describe.
    /// </summary>
    public static void ResetLayersForAutomation()
    {
        for (int index = 0; index < LayerActive.Length; index++)
        {
            LayerActive[index] = false;
        }

        _backConsumedFrame = -1000;
        _injectedPauseFrame = -1000;
        _injectedEscapeFrame = -1000;
        _injectedWristFrame = -1000;
        _injectedInteractFrame = -1000;
    }

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
    /// console view, with the wrist open and above an open device panel (all of
    /// them stay as the frozen underlying state).
    ///
    /// Escape pauses only when NOTHING local can go back: if any local layer is
    /// active (pause &gt; modal panel &gt; wrist &gt; console view), the press is
    /// that layer's Back and must never also stack a pause on top of it. The
    /// answer comes from the published layer state, so it does not depend on
    /// which MonoBehaviour happens to update first.
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

        if (layer != UiLayer.Gameplay)
        {
            // The caller already knows it sits in a local layer.
            return false;
        }

        // Escape may have been consumed as Back by a local layer THIS frame (the
        // panel that just closed, the wrist that just closed): then the same
        // press is not a pause either.
        if (BackConsumedThisFrame)
        {
            return false;
        }

        // A local layer owns Escape while it can still go back.
        if (ActiveBackLayer != UiLayer.Gameplay)
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
    /// (Xbox B / E key).
    ///
    /// This is the console view's historical exit binding and NOT a Back
    /// semantic: Back (Xbox B / Escape) is resolved through
    /// <see cref="BackPressedForLayer"/> so the console view obeys the same
    /// priority as every other layer. Callers must only act on it while they own
    /// the layer (see <see cref="IsTopBackLayer"/>), otherwise B would leave a
    /// console that is sitting frozen underneath the pause.
    /// </summary>
    public static bool ConsoleExitPressed()
    {
        // The historical exit key is literally the interact key (E), so the
        // automation seam that reports an interact press has to appear here too:
        // that is what makes the "the E that opened the view is still readable as
        // its exit in the same frame" regression reproducible in the harness.
        if (InteractInjectedThisFrame)
        {
            return true;
        }

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
        if (InteractInjectedThisFrame)
        {
            return true;
        }

        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonSouth.wasPressedThisFrame)
        {
            return true;
        }

        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.eKey.wasPressedThisFrame;
    }

    /// <summary>
    /// Automation seam: reports an INTERACT press (Keyboard E / Xbox A) for the
    /// next frame.
    ///
    /// The batch-mode harness cannot deliver keyboard state to a synthetic
    /// Keyboard device: a queued KeyboardState reaches the control's VALUE but
    /// never produces the wasPressedThisFrame edge (measured - the control reads
    /// 1 while the edge stays false), which is the same limitation the Escape and
    /// Wrist seams already work around. Injecting the physical press here keeps
    /// every layer below real - the gaze-gated interactor, the console focus
    /// controller, the Back priority board - and the gamepad rows of the suites
    /// stay on real queued device events.
    /// </summary>
    public static void InjectInteractForAutomation()
    {
        _injectedInteractFrame = Time.frameCount + 1;
    }

    private static bool InteractInjectedThisFrame =>
        Time.frameCount == _injectedInteractFrame;

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
    /// gameplay class names a control directly. (The remaining face control,
    /// buttonWest, is the <see cref="Map"/> action - never the wrist.)
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
    /// True when the Map action was pressed this frame (Xbox X / PlayStation
    /// Square, and the M / I keys - the keyboard route the surface map always had).
    ///
    /// Phase 09A_4: the planet surface map had no gamepad binding because
    /// SurfaceGameController polled the M / I keys directly instead of consuming a
    /// logical action, so no face control ever reached it. The surface map IS the
    /// wrist's central map, so this action and <see cref="WristPressed"/> toggle the
    /// same surface presentation; the physical mapping stays in this file and no
    /// gameplay class names a control directly.
    /// </summary>
    public static bool MapPressed()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonWest.wasPressedThisFrame)
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
