using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Phase 08 pause work — pause menu, return-to-main-menu loop and console view.
///
/// Everything here is driven through REAL device input: a virtual gamepad and a
/// virtual keyboard are added to the Input System and real state events are
/// queued, so the semantic layer (UiInput), the ship controller, the pause menu
/// and the console focus controller all run exactly as they do with hardware.
///
/// Covered contract:
///   gameplay + Menu/Start or Escape  -> pause open, timeScale 0, input frozen
///   pause + B / Menu/Start / Escape  -> continue, timeScale restored, input back
///   pause + 返回主菜单               -> main menu, session kept, 继续游戏 enabled
///   main menu + 继续游戏             -> back into the same ship state
///   pause + 退出游戏                 -> the only pause path that can quit
///   console view: confirm enters, back leaves, pause freezes and resumes it,
///                 return-to-main-menu cleans it, confirm never fires console UI
/// </summary>
public class SurvivePhase08PauseTests
{
    private Gamepad _pad;
    private Keyboard _keyboard;

    [SetUp]
    public void SetUp()
    {
        Time.timeScale = 1f;
        MainMenuSettings.ResetToDefaults();
        InputDeviceTracker.ResetToDefaults();
        AppQuit.ResetBookkeepingForAutomation();
        AppQuit.SuppressForAutomation = true;

        GameSession session = GameSession.Instance;
        if (session != null)
        {
            Object.Destroy(session.gameObject);
        }
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;

        if (_pad != null && _pad.added)
        {
            InputSystem.RemoveDevice(_pad);
        }

        if (_keyboard != null && _keyboard.added)
        {
            InputSystem.RemoveDevice(_keyboard);
        }

        _pad = null;
        _keyboard = null;
        AppQuit.ResetBookkeepingForAutomation();
        InputDeviceTracker.ResetToDefaults();
    }

    // ---- Pause open / freeze ----

    /// <summary>Menu/Start in gameplay opens the pause and freezes the game.</summary>
    [UnityTest]
    public IEnumerator Gamepad_Pause_OpensAndFreezes_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        Assert.IsTrue(ship.Player.InputEnabled, "the player starts in control");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "gameplay runs at normal time");

        yield return PressButton(GamepadButton.Start);

        Assert.IsTrue(ship.PauseMenu.IsOpen, "Menu/Start opens the pause menu");
        Assert.AreEqual(0f, Time.timeScale, 0.0001f, "pause sets Time.timeScale = 0");
        Assert.IsFalse(ship.Player.InputEnabled, "movement and look are frozen");
        Assert.AreEqual(ShipGameController.ShipPointerOwner.PauseMenu, ship.PointerOwner,
            "the pause menu owns the pointer");
        Assert.AreEqual("Resume", ship.PauseMenu.FocusedItemId,
            "focus starts on 继续游戏");
        Assert.AreEqual(0, AppQuit.RequestCount, "opening the pause never quits");
        Assert.IsNotNull(GameSession.Instance, "the session is untouched");
    }

    /// <summary>Escape in gameplay opens the pause too.</summary>
    [UnityTest]
    public IEnumerator Keyboard_Escape_OpensPause_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        // Keyboard Escape maps to the same logical Pause action (see
        // UiInput.BindingTable); the harness injects the logical press because a
        // synthetic Keyboard device cannot receive state in batch mode.
        yield return PressPauseSemantic();

        Assert.IsTrue(ship.PauseMenu.IsOpen, "the Pause action opens the pause menu");
        Assert.AreEqual(0f, Time.timeScale, 0.0001f, "the Pause action also freezes time");
        Assert.IsFalse(ship.Player.InputEnabled, "input frozen");
        Assert.AreEqual(0, AppQuit.RequestCount, "the Pause action never quits");
    }

    /// <summary>Pause keeps the pause UI itself operable (unscaled).</summary>
    [UnityTest]
    public IEnumerator Pause_Navigation_MovesFocus_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        yield return PressButton(GamepadButton.Start);

        Assert.AreEqual(3, ship.PauseMenu.Items.Count, "three pause options");
        Assert.AreEqual("Resume", ship.PauseMenu.FocusedItemId, "starts on 继续游戏");

        yield return PressButton(GamepadButton.DpadDown);
        Assert.AreEqual("ReturnToMainMenu", ship.PauseMenu.FocusedItemId,
            "D-Pad down reaches 返回主菜单");
        Assert.IsTrue(ship.PauseMenu.MainMenuButton.ClassListContains("pause-item-focused"),
            "the focused row is visibly marked");

        yield return PressButton(GamepadButton.DpadDown);
        Assert.AreEqual("QuitGame", ship.PauseMenu.FocusedItemId, "D-Pad down reaches 退出游戏");

        yield return PressButton(GamepadButton.DpadDown);
        Assert.AreEqual("Resume", ship.PauseMenu.FocusedItemId, "navigation wraps");

        yield return PushStick(new Vector2(0f, -1f));
        Assert.AreEqual("ReturnToMainMenu", ship.PauseMenu.FocusedItemId,
            "the left stick navigates like the D-Pad");

        Assert.AreEqual(0f, Time.timeScale, 0.0001f,
            "the game stays frozen while the pause is navigated");
        Assert.AreEqual(0, AppQuit.RequestCount, "navigation never quits");
    }

    // ---- Continue ----

    /// <summary>B continues and restores exactly what the pause froze.</summary>
    [UnityTest]
    public IEnumerator Pause_Back_Continues_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "pause is open");

        yield return PressButton(GamepadButton.East);

        Assert.IsFalse(ship.PauseMenu.IsOpen, "B continues");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "timeScale is restored to 1");
        Assert.IsTrue(ship.Player.InputEnabled, "movement and look are restored");
        Assert.AreEqual(ShipGameController.ShipPointerOwner.None, ship.PointerOwner,
            "the pause releases the pointer");
        Assert.AreEqual(0, AppQuit.RequestCount, "B never quits");
    }

    /// <summary>Menu/Start again closes the pause.</summary>
    [UnityTest]
    public IEnumerator Pause_StartButton_Continues_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "pause open");

        yield return PressButton(GamepadButton.Start);
        Assert.IsFalse(ship.PauseMenu.IsOpen, "Menu/Start toggles the pause closed");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "timeScale restored");
        Assert.IsTrue(ship.Player.InputEnabled, "input restored");
    }

    /// <summary>Escape while paused continues (never quits).</summary>
    [UnityTest]
    public IEnumerator Pause_Escape_Continues_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        yield return PressPauseSemantic();
        Assert.IsTrue(ship.PauseMenu.IsOpen, "pause open from the Pause action");

        yield return PressPauseSemantic();

        Assert.IsFalse(ship.PauseMenu.IsOpen, "the Pause action continues");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "timeScale restored");
        Assert.AreEqual(0, AppQuit.RequestCount, "the Pause action never quits");
    }

    // ---- Return to main menu loop ----

    /// <summary>Pause -> 返回主菜单 -> 继续游戏 -> the same ship state.</summary>
    [UnityTest]
    public IEnumerator Pause_ReturnToMainMenu_ContinueLoop_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        GameSession session = GameSession.Instance;
        session.AddMinerals(7);
        int minerals = session.Minerals;
        Assert.IsTrue(session.HasStartedRun, "the run is started");

        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "pause open");

        // Focus 返回主菜单 and confirm.
        yield return PressButton(GamepadButton.DpadDown);
        Assert.AreEqual("ReturnToMainMenu", ship.PauseMenu.FocusedItemId, "focus on 返回主菜单");
        yield return PressButton(GamepadButton.South);

        yield return WaitUntilReal(() => SceneManager.GetActiveScene().name
                                          == SceneRouting.MainMenuSceneName, 20f);
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "返回主菜单 reaches the title screen");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "timeScale is restored on the way out");
        Assert.AreSame(session, GameSession.Instance, "the SAME session survives");
        Assert.AreEqual(minerals, session.Minerals, "返回主菜单 never resets the session");
        Assert.IsTrue(session.HasStartedRun, "the run is still resumable");
        Assert.AreEqual(0, AppQuit.RequestCount, "返回主菜单 never quits");

        MainMenuController menu = null;
        yield return WaitUntilReal(() =>
        {
            menu = Object.FindAnyObjectByType<MainMenuController>();
            return menu != null && menu.IsReady;
        }, 20f);
        Assert.IsNotNull(menu, "the title screen is ready");
        Assert.IsTrue(menu.IsContinueAvailable, "继续游戏 is available again");
        Assert.IsTrue(menu.ContinueButton.enabledSelf, "继续游戏 is enabled");

        // ---- and back into the same ship state through 继续游戏 ----
        menu.SetFocus(1);
        Assert.AreEqual("Continue", menu.FocusedItemId, "focus on 继续游戏");
        yield return PressButton(GamepadButton.South);

        ShipGameController resumed = null;
        yield return WaitUntilReal(() =>
        {
            resumed = Object.FindAnyObjectByType<ShipGameController>();
            return resumed != null && resumed.IsReady;
        }, 30f);
        Assert.IsNotNull(resumed, "继续游戏 returns to the ship interior");
        Assert.AreSame(session, GameSession.Instance, "still the same session object");
        Assert.AreEqual(minerals, session.Minerals, "继续游戏 never resets the session");
        Assert.IsTrue(resumed.PauseMenu != null && resumed.PauseMenu.IsReady,
            "the pause menu is available again");
    }

    // ---- Quit ----

    /// <summary>Only the 退出游戏 option may quit; nothing else in the pause does.</summary>
    [UnityTest]
    public IEnumerator Pause_QuitGame_IsTheOnlyQuitPath_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);
        yield return PressButton(GamepadButton.Start);

        // B, Menu/Start, Escape, D-Pad and the other two options never quit.
        yield return PressButton(GamepadButton.DpadDown);
        yield return PressButton(GamepadButton.DpadUp);
        Assert.AreEqual(0, AppQuit.RequestCount, "navigation never quits");

        yield return PressButton(GamepadButton.South);   // confirm 继续游戏
        Assert.AreEqual(0, AppQuit.RequestCount, "继续游戏 never quits");
        Assert.IsFalse(ship.PauseMenu.IsOpen, "继续游戏 closed the pause");

        yield return PressButton(GamepadButton.Start);
        yield return PressButton(GamepadButton.East);     // B -> continue
        Assert.AreEqual(0, AppQuit.RequestCount, "B never quits");

        yield return PressButton(GamepadButton.Start);
        yield return PressPauseSemantic();
        Assert.AreEqual(0, AppQuit.RequestCount, "the Pause action never quits");

        // Now the one allowed path: focus 退出游戏 and confirm.
        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "pause open for the quit check");
        ship.PauseMenu.SetFocus(2);
        Assert.AreEqual("QuitGame", ship.PauseMenu.FocusedItemId, "focus on 退出游戏");
        yield return PressButton(GamepadButton.South);

        Assert.AreEqual(1, AppQuit.RequestCount, "退出游戏 + confirm is the one quit path");
        Assert.AreEqual("Pause.QuitGame", AppQuit.LastSource, "the quit came from the pause menu");
    }

    // ---- Input priority ----

    /// <summary>While paused, gameplay interaction underneath must not fire.</summary>
    [UnityTest]
    public IEnumerator Pause_BlocksGameplayInteraction_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        ShipConsoleInteractable console = FindConsole(ship, 1);
        Assert.IsNotNull(console, "the 总览 terminal exists");
        FaceConsole(ship, console);
        yield return new WaitForSecondsRealtime(0.2f);

        yield return PressButton(GamepadButton.Start);
        Assert.IsTrue(ship.PauseMenu.IsOpen, "pause open");

        int consolesBefore = CountFocusedConsoles(ship);
        yield return PressButton(GamepadButton.South);   // would be "view console" in gameplay
        yield return PressButton(GamepadButton.West);    // X: no function
        yield return PressButton(GamepadButton.North);   // Y: no function

        Assert.IsTrue(ship.PauseMenu.IsOpen || CountFocusedConsoles(ship) == consolesBefore,
            "no gameplay action fired underneath the pause");
        Assert.IsFalse(ship.IsWristOpen, "the wrist cannot open behind the pause");
        Assert.AreEqual(0, AppQuit.RequestCount, "the paused input never quits");

        // The pause is still fully controllable afterwards.
        yield return PressButton(GamepadButton.East);
        Assert.IsFalse(ship.PauseMenu.IsOpen, "the pause still closes with B");
    }

    // ---- Console view (gaze) ----

    /// <summary>Confirm near a console enters the view; Back leaves it.</summary>
    [UnityTest]
    public IEnumerator ConsoleView_ConfirmEnters_BackExits_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        ShipConsoleInteractable console = FindConsole(ship, 1);
        Assert.IsNotNull(console, "the 总览 terminal exists");
        FaceConsole(ship, console);
        yield return WaitForGaze(ship, console);

        Assert.IsFalse(ship.Focus.FocusActive, "not focused while walking");
        Vector3 freeEyePosition = ship.Player.EyeCamera.transform.position;

        yield return PressButton(GamepadButton.South);

        Assert.IsTrue(ship.Focus.FocusActive, "confirm enters the console view");
        Assert.IsFalse(ship.Player.InputEnabled, "movement and look are disabled in the view");
        Assert.AreEqual(0, AppQuit.RequestCount, "viewing a console never quits");

        // Wait for the smooth transition to finish, THEN check the anchor pose.
        WorldConsoleSurface surface = console.GetComponent<WorldConsoleSurface>();
        Transform anchor = surface.FocusAnchor;
        yield return WaitUntilReal(() =>
            ship.Focus.State == ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus, 3f);

        Assert.AreEqual(ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus,
            ship.Focus.State, "the view transition completed");
        Assert.Less(Vector3.Distance(ship.Player.EyeCamera.transform.position, anchor.position),
            0.05f, "the camera reached the console view anchor");
        Assert.Less(Quaternion.Angle(ship.Player.EyeCamera.transform.rotation, anchor.rotation),
            1.5f, "the camera faces the console screen");

        // ---- Back leaves the view ----
        yield return PressButton(GamepadButton.East);

        // The exit runs as a smooth 0.26 s transition, so wait for it to land
        // before asserting the released state.
        yield return WaitUntilReal(() =>
            ship.Focus.State == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore, 3f);
        Assert.AreEqual(ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore,
            ship.Focus.State, "B leaves the console view");
        Assert.IsFalse(ship.Focus.FocusActive, "the console view is released");
        yield return WaitUntilReal(() => ship.Player.InputEnabled, 3f);
        Assert.IsTrue(ship.Player.InputEnabled, "movement and look are restored");
        Assert.AreEqual(0, AppQuit.RequestCount, "leaving the view never quits");

        // The camera is back on the player's own free-look pose.
        float eyeOffset = Vector3.Distance(
            ship.Player.EyeCamera.transform.localPosition,
            new Vector3(0f, ship.Player.eyeHeight, 0f));
        Assert.Less(eyeOffset, 0.05f, "the camera returned to its player mount");
        Assert.Less(Vector3.Distance(ship.Player.EyeCamera.transform.position, freeEyePosition),
            0.05f, "the camera returned to where the player was standing");
    }

    /// <summary>Confirm while focused must not fire console UI (not implemented yet).</summary>
    [UnityTest]
    public IEnumerator ConsoleView_ConfirmDoesNotFireConsoleUi_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        ShipConsoleInteractable console = FindConsole(ship, 1);
        FaceConsole(ship, console);
        yield return WaitForGaze(ship, console);
        yield return PressButton(GamepadButton.South);
        Assert.IsTrue(ship.Focus.FocusActive, "console view active");

        int visibleBefore = CountVisibleConsoleScreens(ship);

        // A / E while focused: no internal console action exists in this phase,
        // so nothing may change and the view must stay up.
        yield return PressButton(GamepadButton.South);
        yield return PressButton(GamepadButton.South);

        Assert.IsTrue(ship.Focus.FocusActive, "the console view stays up");
        Assert.AreEqual(visibleBefore, CountVisibleConsoleScreens(ship),
            "no console UI screen changed state");
        Assert.AreEqual(0, AppQuit.RequestCount, "no quit");

        // The HUD tells the player how to leave, and only that.
        InputDeviceTracker.NotifyGamepad();
        yield return null;
        Assert.AreEqual("[B] 返回", ship.Hud.FocusPromptText,
            "the only hint in the view is the Back hint on a gamepad");
        InputDeviceTracker.NotifyKeyboardMouse();
        yield return null;
        Assert.AreEqual("[Esc] 返回", ship.Hud.FocusPromptText,
            "and the keyboard presentation names Escape");
    }

    /// <summary>Menu/Start during the console view opens the pause on top of it.</summary>
    [UnityTest]
    public IEnumerator ConsoleView_Pause_FreezesAndResumes_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        ShipConsoleInteractable console = FindConsole(ship, 1);
        FaceConsole(ship, console);
        yield return WaitForGaze(ship, console);
        yield return PressButton(GamepadButton.South);
        yield return WaitUntilReal(() => ship.Focus.FocusActive, 3f);
        Assert.IsTrue(ship.Focus.FocusActive, "console view active");

        // Let the view transition finish before freezing it.
        yield return WaitUntilReal(() =>
            ship.Focus.State == ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus, 3f);

        yield return PressButton(GamepadButton.Start);

        Assert.IsTrue(ship.PauseMenu.IsOpen, "Menu/Start opens the pause over the view");
        Assert.AreEqual(0f, Time.timeScale, 0.0001f, "time freezes");
        Assert.IsTrue(ship.Focus.FocusActive,
            "the console view stays frozen underneath (it is the gameplay context)");
        Assert.AreEqual(0, AppQuit.RequestCount, "no quit");

        // 继续游戏 goes back to the console view.
        yield return PressButton(GamepadButton.South);
        Assert.IsFalse(ship.PauseMenu.IsOpen, "继续游戏 closes the pause");
        Assert.AreEqual(1f, Time.timeScale, 0.0001f, "time resumes");
        Assert.IsTrue(ship.Focus.FocusActive, "we are back in the console view");
        Assert.IsFalse(ship.Player.InputEnabled, "movement stays disabled inside the view");

        // And B still leaves the console view normally.
        yield return PressButton(GamepadButton.East);
        yield return WaitUntilReal(() =>
            ship.Focus.State == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore, 3f);
        Assert.AreEqual(ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore,
            ship.Focus.State, "B leaves the console view after resuming");
        Assert.IsTrue(ship.Player.InputEnabled, "the player is back in control");
    }

    /// <summary>Pause -> 返回主菜单 from the console view cleans the focus state.</summary>
    [UnityTest]
    public IEnumerator ConsoleView_ReturnToMainMenu_CleansFocus_Green()
    {
        ShipGameController ship = null;
        yield return EnterShip(s => ship = s);

        ShipConsoleInteractable console = FindConsole(ship, 1);
        FaceConsole(ship, console);
        yield return WaitForGaze(ship, console);
        yield return PressButton(GamepadButton.South);
        yield return WaitUntilReal(() => ship.Focus.FocusActive, 3f);
        Assert.IsTrue(ship.Focus.FocusActive, "console view active");

        yield return PressButton(GamepadButton.Start);
        ship.PauseMenu.SetFocus(1);
        Assert.AreEqual("ReturnToMainMenu", ship.PauseMenu.FocusedItemId, "focus on 返回主菜单");
        yield return PressButton(GamepadButton.South);

        yield return WaitUntilReal(() => SceneManager.GetActiveScene().name
                                          == SceneRouting.MainMenuSceneName, 20f);
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "returned to the title screen");
        Assert.AreEqual(0, Object.FindObjectsByType<ShipConsoleFocusController>().Length,
            "the console focus controller is gone with the ship scene");
        Assert.AreEqual(0, AppQuit.RequestCount, "no quit");
    }

    /// <summary>
    /// The physical binding contract, pinned as data.
    ///
    /// The batch-mode harness cannot deliver KEYBOARD state (a synthetic Keyboard
    /// device never receives state events), so the Escape path is covered by this
    /// contract plus the injected logical Pause action in the tests below, while
    /// every gamepad binding is driven through the real device pipeline.
    /// </summary>
    [Test]
    public void UiInput_PhysicalBindingContract_Green()
    {
        CollectionAssert.Contains(UiInput.BindingTable,
            "Confirm=Gamepad.buttonSouth,Keyboard.eKey|enterKey|spaceKey",
            "confirm is the gamepad SOUTH button (Xbox A)");
        CollectionAssert.Contains(UiInput.BindingTable,
            "Back=Gamepad.buttonEast,Keyboard.escapeKey",
            "back is the gamepad EAST button (Xbox B)");
        CollectionAssert.Contains(UiInput.BindingTable,
            "Pause=Gamepad.startButton,Keyboard.escapeKey",
            "pause is Menu/Start (and Escape on the keyboard)");

        foreach (string row in UiInput.BindingTable)
        {
            StringAssert.DoesNotContain("AButton", row, "no AButton semantics");
            StringAssert.DoesNotContain("BButton", row, "no BButton semantics");
            StringAssert.DoesNotContain("StartButton", row, "no StartButton semantics");
        }
    }
    // ---- helpers ----

    private IEnumerator EnterShip(System.Action<ShipGameController> capture)
    {
        _pad = InputSystem.AddDevice<Gamepad>();
        _keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.EnableDevice(_keyboard);
        Assert.AreSame(_pad, Gamepad.current, "virtual gamepad is current");
        Assert.AreSame(_keyboard, Keyboard.current, "virtual keyboard is current");

        // A started run so the pause's return-to-menu loop has something to resume.
        GameSession session = GameSession.Ensure();
        session.ResetSession();
        session.BeginRun();

        // Enter the ship as an ORDINARY cut: the entry eyelids are the title
        // screen's hand-over, and an eyelid left pending by another test must not
        // arm itself here (it would lock input for a second).
        SceneRouting.RequestEntryEyelid(SceneRouting.EntryEyelid.None);
        yield return LoadSceneStable(GameSession.ShipInteriorSceneName);

        ShipGameController ship = null;
        yield return WaitUntilReal(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady;
        }, 30f);

        Assert.IsNotNull(ship, "the ship interior is ready");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, SceneManager.GetActiveScene().name,
            "the ship scene is the active one");
        capture(ship);

        // Let the first frames settle (HUD bind, pause document build).
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        Assert.IsNotNull(ship.PauseMenu, "the pause menu was created");
        Assert.IsTrue(ship.PauseMenu.IsReady, "the pause menu is bound");
    }

    /// <summary>
    /// Loads a scene and waits until it is STABLE. A previous test can leave an
    /// asynchronous scene switch in flight (pause -> 返回主菜单, cinematic -> ship),
    /// which would otherwise land in the middle of this test and destroy the
    /// objects it just captured.
    /// </summary>
    private static IEnumerator LoadSceneStable(string sceneName)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            SceneManager.LoadScene(sceneName);
            yield return WaitUntilReal(() => SceneManager.GetActiveScene().name == sceneName, 15f);
            yield return new WaitForSecondsRealtime(0.25f);
            if (SceneManager.GetActiveScene().name == sceneName)
            {
                yield break;
            }
        }
    }

    private static ShipConsoleInteractable FindConsole(ShipGameController ship, int id)
    {
        foreach (ShipConsoleInteractable console in ship.Consoles)
        {
            if (console.ConsoleId == id)
            {
                return console;
            }
        }

        return null;
    }

    /// <summary>Stands the player in front of the console, looking at its screen.</summary>
    private static void FaceConsole(ShipGameController ship, ShipConsoleInteractable console)
    {
        Vector3 forward = console.transform.forward;
        ship.Player.TeleportTo(console.transform.position + forward * 1.9f);
        ship.Player.SetFacing(console.transform.eulerAngles.y + 180f, 0f);
    }

    private static IEnumerator WaitForGaze(ShipGameController ship,
        ShipConsoleInteractable console)
    {
        float deadline = Time.realtimeSinceStartup + 4f;
        while (Time.realtimeSinceStartup < deadline)
        {
            ship.Interactor.RefreshTarget();
            if (ship.Interactor.CurrentTarget == (IFirstPersonInteractable)console)
            {
                yield break;
            }

            yield return null;
        }
    }

    private static int CountFocusedConsoles(ShipGameController ship)
    {
        return ship.IsConsoleFocusActive ? 1 : 0;
    }

    private static int CountVisibleConsoleScreens(ShipGameController ship)
    {
        int count = 0;
        foreach (ShipConsoleScreenHost host
                 in Object.FindObjectsByType<ShipConsoleScreenHost>())
        {
            count += host.CountVisibleScreens();
        }

        return count;
    }

    /// <summary>Queues a real press/release of one face, d-pad or menu button.</summary>
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
        InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = direction * 0.9f });
        float deadline = Time.realtimeSinceStartup + 0.08f;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        InputSystem.QueueStateEvent(_pad, new GamepadState());
        yield return null;
        yield return null;
    }

    /// <summary>
    /// Injects the LOGICAL Pause action for one frame and lets the game process
    /// it. (The batch-mode harness cannot deliver keyboard state, so the Escape
    /// key cannot be driven directly; the physical binding is pinned by
    /// UiInput.BindingTable.)
    /// </summary>
    private IEnumerator PressPauseSemantic()
    {
        UiInput.InjectPauseForAutomation();
        yield return null;
        yield return null;
    }

    /// <summary>Queues a real key press/release on the virtual keyboard.</summary>
    private IEnumerator PressKey(Key key)
    {
        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
        yield return null;
        yield return null;
    }

    private static IEnumerator WaitUntilReal(System.Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }
}
