using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Phase 08 controller hotfix — real gamepad binding tests.
///
/// These tests do NOT call ConfirmFocused() and pretend. They add a virtual
/// gamepad to the Input System and queue REAL device state for the face buttons
/// and sticks, so the whole pipeline runs exactly as it does with a physical
/// Xbox pad: the Input System device state, Unity's runtime UI event system and
/// the title screen controller all see the same press.
///
/// Contract under test (Xbox layout per Unity's Gamepad mapping):
///   buttonSouth = A = Confirm      buttonEast = B = Back
///   buttonWest  = X (no function)  buttonNorth = Y (no function)
///   D-Pad / left stick = focus navigation
///   Exit + A (or a mouse click on 退出) is the ONLY way to quit.
/// </summary>
public class SurvivePhase08ControllerTests
{
    private Gamepad _pad;

    [SetUp]
    public void SetUp()
    {
        MainMenuSettings.ResetToDefaults();
        InputDeviceTracker.ResetToDefaults();

        // Never let a test terminate the editor: the quit action records itself.
        MainMenuController.ResetQuitBookkeepingForAutomation();
        MainMenuController.SuppressQuitForAutomation = true;

        GameSession session = GameSession.Instance;
        if (session != null)
        {
            Object.Destroy(session.gameObject);
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        _pad = null;
        MainMenuController.ResetQuitBookkeepingForAutomation();
        InputDeviceTracker.ResetToDefaults();
        MainMenuSettings.ResetToDefaults();
    }

    /// <summary>A on 新游戏 must start the new game, never quit.</summary>
    [UnityTest]
    public IEnumerator Gamepad_South_OnNewGame_StartsNewGame_NotQuit()
    {
        MainMenuController menu = null;
        yield return BootWithGamepad(m => menu = m);

        menu.SetFocus(0);
        Assert.AreEqual("NewGame", menu.FocusedItemId, "focus is on 新游戏");

        yield return PressButton(GamepadButton.South);

        Assert.AreEqual(0, MainMenuController.QuitRequestCount,
            "A on 新游戏 must never reach the quit action");
        Assert.IsTrue(menu.Cinematic.IsPlaying, "A on 新游戏 starts the new game cinematic");
        Assert.IsTrue(GameSession.Instance.HasStartedRun, "新游戏 started a run");
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "the cinematic plays inside the title screen scene");
    }

    /// <summary>A on 继续游戏 must resume, never quit.</summary>
    [UnityTest]
    public IEnumerator Gamepad_South_OnContinue_Continues_NotQuit()
    {
        MainMenuController menu = null;
        yield return BootWithGamepad(m => menu = m);

        // A run already exists in this process, so 继续游戏 is available.
        GameSession session = GameSession.Ensure();
        session.BeginRun();
        menu.RefreshContinueAvailability();
        yield return null;
        Assert.IsTrue(menu.ContinueButton.enabledSelf, "继续游戏 is available");

        menu.SetFocus(1);
        Assert.AreEqual("Continue", menu.FocusedItemId, "focus is on 继续游戏");

        yield return PressButton(GamepadButton.South);

        Assert.AreEqual(0, MainMenuController.QuitRequestCount,
            "A on 继续游戏 must never reach the quit action");
        Assert.IsTrue(menu.Cinematic.IsPlaying, "A on 继续游戏 starts the continue shot");
        Assert.IsTrue(menu.Cinematic.IsContinueShot, "it is the continue shot");
    }

    /// <summary>A on 选项 must open the options modal, never quit.</summary>
    [UnityTest]
    public IEnumerator Gamepad_South_OnOptions_OpensOptions_NotQuit()
    {
        MainMenuController menu = null;
        yield return BootWithGamepad(m => menu = m);

        menu.SetFocus(2);
        Assert.AreEqual("Options", menu.FocusedItemId, "focus is on 选项");

        yield return PressButton(GamepadButton.South);

        Assert.AreEqual(0, MainMenuController.QuitRequestCount,
            "A on 选项 must never reach the quit action");
        Assert.IsTrue(menu.IsOptionsOpen, "A on 选项 opens the options modal");
        Assert.IsFalse(menu.Cinematic.IsPlaying, "no cinematic is started from 选项");
    }

    /// <summary>B closes the options modal and never quits.</summary>
    [UnityTest]
    public IEnumerator Gamepad_East_ClosesOptions_NotQuit()
    {
        MainMenuController menu = null;
        yield return BootWithGamepad(m => menu = m);

        menu.ShowOptions();
        yield return null;
        Assert.IsTrue(menu.IsOptionsOpen, "选项 is open");

        yield return PressButton(GamepadButton.East);

        Assert.AreEqual(0, MainMenuController.QuitRequestCount,
            "B must never reach the quit action");
        Assert.IsFalse(menu.IsOptionsOpen, "B closes 选项");
    }

    /// <summary>Only 退出 + A reaches the quit action.</summary>
    [UnityTest]
    public IEnumerator Gamepad_South_OnExit_Quits()
    {
        MainMenuController menu = null;
        yield return BootWithGamepad(m => menu = m);

        menu.SetFocus(3);
        Assert.AreEqual("Exit", menu.FocusedItemId, "focus is on 退出");

        yield return PressButton(GamepadButton.South);

        Assert.AreEqual(1, MainMenuController.QuitRequestCount,
            "A on 退出 is the one path that reaches the quit action");
    }

    /// <summary>B / X / Y on the title screen must never quit.</summary>
    [UnityTest]
    public IEnumerator Gamepad_EastWestNorth_OnTitleScreen_DoNotQuit()
    {
        MainMenuController menu = null;
        yield return BootWithGamepad(m => menu = m);

        menu.SetFocus(0);

        yield return PressButton(GamepadButton.East);
        Assert.AreEqual(0, MainMenuController.QuitRequestCount, "B on 新游戏 does not quit");
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "B does not leave the title screen");

        yield return PressButton(GamepadButton.West);
        Assert.AreEqual(0, MainMenuController.QuitRequestCount, "X has no function");

        yield return PressButton(GamepadButton.North);
        Assert.AreEqual(0, MainMenuController.QuitRequestCount,
            "Y has no title-screen function (Phase 09A_2 binds it to Wrist inside the ship)");

        Assert.IsFalse(menu.Cinematic.IsPlaying,
            "B / X / Y never start the new game either");
        Assert.AreEqual("NewGame", menu.FocusedItemId, "the focus stayed on 新游戏");
    }

    /// <summary>D-Pad and left stick move the focus; the disabled 继续游戏 is skipped.</summary>
    [UnityTest]
    public IEnumerator Gamepad_Navigation_MovesFocus()
    {
        MainMenuController menu = null;
        yield return BootWithGamepad(m => menu = m);

        Assert.AreEqual("NewGame", menu.FocusedItemId, "focus starts on 新游戏");
        Assert.IsFalse(menu.ContinueButton.enabledSelf, "继续游戏 is disabled");

        // D-Pad down: 新游戏 -> 选项 (the disabled 继续游戏 is skipped).
        yield return PressButton(GamepadButton.DpadDown);
        Assert.AreEqual("Options", menu.FocusedItemId, "D-Pad down skips the disabled entry");

        yield return PressButton(GamepadButton.DpadDown);
        Assert.AreEqual("Exit", menu.FocusedItemId, "D-Pad down reaches 退出");

        yield return PressButton(GamepadButton.DpadUp);
        Assert.AreEqual("Options", menu.FocusedItemId, "D-Pad up moves back to 选项");

        // Left stick works the same way.
        yield return PushStick(new Vector2(0f, 1f));
        Assert.AreEqual("NewGame", menu.FocusedItemId, "left stick up skips back to 新游戏");

        yield return PushStick(new Vector2(0f, -1f));
        Assert.AreEqual("Options", menu.FocusedItemId, "left stick down skips the disabled entry");

        Assert.AreEqual(0, MainMenuController.QuitRequestCount, "navigation never quits");
    }

    /// <summary>
    /// The exact bug mechanism: Unity's runtime UI turns a gamepad A press into a
    /// pointer click at the VIRTUAL pointer position (driven by the left stick).
    /// If that synthetic click lands on 退出 while the player is confirming 新游戏,
    /// one press performs two actions and the game quits. This test presses A and
    /// dispatches that synthetic click in the same frame.
    /// </summary>
    [UnityTest]
    public IEnumerator Gamepad_South_WithSynthesisedClickOnExit_DoesNotQuit()
    {
        MainMenuController menu = null;
        yield return BootWithGamepad(m => menu = m);

        menu.SetFocus(0);
        Assert.AreEqual("NewGame", menu.FocusedItemId, "focus is on 新游戏");

        // Frame 1: queue the real press. Frame 2: the Input System has processed
        // it, A confirms 新游戏, and the synthetic click for that same press is
        // dispatched at the Exit row.
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.South));
        yield return null;

        bool dispatched = UiToolkitPointerSimulator.Click(menu.ExitButton);
        Assert.IsTrue(dispatched, "the synthetic click was dispatched");

        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;

        Assert.AreEqual(0, MainMenuController.QuitRequestCount,
            "the gamepad's synthesised click on 退出 must not quit");
        Assert.IsTrue(menu.Cinematic.IsPlaying, "A started the new game instead");
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "the title screen is still alive");
    }

    /// <summary>
    /// Control case for the test above: a real pointer click on 退出 (with no
    /// gamepad press in the same frame) still reaches the quit action.
    /// </summary>
    [UnityTest]
    public IEnumerator PointerClick_OnExit_Quits()
    {
        MainMenuController menu = null;
        yield return BootWithGamepad(m => menu = m);

        Assert.IsTrue(UiToolkitPointerSimulator.Click(menu.ExitButton),
            "the click was dispatched");
        yield return null;
        yield return null;

        Assert.AreEqual(1, MainMenuController.QuitRequestCount,
            "a real click on 退出 reaches the quit action");
    }

    /// <summary>
    /// The other half of the same rule: no menu row may be reachable by Unity's
    /// built-in focus/submit path, so the controller's own focus state stays the
    /// only authority.
    /// </summary>
    [UnityTest]
    public IEnumerator Gamepad_MenuRows_AreNotFocusable()
    {
        MainMenuController menu = null;
        yield return BootWithGamepad(m => menu = m);

        foreach (UnityEngine.UIElements.Button item in menu.MenuItems)
        {
            Assert.IsFalse(item.focusable,
                item.name + " must not be reachable by Unity's built-in UI submit");
        }

        Assert.AreEqual("NewGame", menu.FocusedItemId,
            "the controller's own focus state is the only focus authority");
    }

    // ---- helpers ----

    private IEnumerator BootWithGamepad(System.Action<MainMenuController> capture)
    {
        _pad = InputSystem.AddDevice<Gamepad>();
        Assert.IsNotNull(_pad, "virtual gamepad added");
        Assert.AreSame(_pad, Gamepad.current, "the virtual gamepad is the current gamepad");

        SceneManager.LoadScene("00_Boot");
        yield return WaitUntil(() => SceneManager.GetActiveScene().name
                                    == SceneRouting.MainMenuSceneName);

        MainMenuController menu = null;
        yield return WaitUntil(() =>
        {
            menu = Object.FindAnyObjectByType<MainMenuController>();
            return menu != null && menu.IsReady;
        });

        Assert.IsNotNull(menu, "title screen ready");
        capture(menu);

        yield return null;
        yield return null;
    }

    /// <summary>
    /// Queues a real press/release of one face or d-pad button. Buttons live in
    /// the gamepad's button bitfield, so the whole state is sent the way a
    /// physical pad reports it (GamepadState.WithButton sets that one bit).
    /// </summary>
    private IEnumerator PressButton(GamepadButton button)
    {
        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
    }

    /// <summary>Queues a real left-stick push, then returns it to centre.</summary>
    private IEnumerator PushStick(Vector2 direction)
    {
        var state = new GamepadState { leftStick = direction * 0.9f };
        InputSystem.QueueStateEvent(_pad, state);

        // Navigation repeat needs the stick held for a moment.
        float deadline = Time.realtimeSinceStartup + 0.08f;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = 900)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }
}
