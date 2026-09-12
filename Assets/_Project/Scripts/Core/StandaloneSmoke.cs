using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Standalone smoke probe (validation instrumentation only).
///
/// Inert during normal play: it only activates when the player is launched
/// with  -smokeResult &lt;absolute path&gt;. Exactly ONE probe instance owns the run
/// (the first one to wake claims it), so the menu probe and the legacy Hub
/// probe can both be authored without ever driving the app twice. The owner
/// survives scene loads (DontDestroyOnLoad), because the journey crosses them.
///
/// Phase 08 Revision order (the real human flow, end to end):
///   Boot -&gt; Main Menu cover (left hero ship / right menu / deep space)
///        -&gt; 系统设置 open/close -&gt; background click blocked -&gt; 制作信息
///        -&gt; 进入任务 -&gt; start cinematic (menu fade / engine boost / ship
///        departure / blackout) -&gt; first-person ship interior with the blink
///        fade -&gt; walk-in terminal flow (总览 + the other three terminals)
///        -&gt; existing Phase 01-05 state journey -&gt; main menu round trip
///        (session preserved) -&gt; 进入任务 again -&gt; 退出 (the application must
///        terminate).
/// </summary>
public class StandaloneSmoke : MonoBehaviour
{
    private const string SmokeResultFlag = "-smokeResult";
    private const string SmokeShotsFlag = "-smokeShots";

    private static bool _sessionClaimed;

    private string _resultPath;
    private string _shotFolder;
    private bool _ownsRun;

    private void Awake()
    {
        _resultPath = ParseFlag(SmokeResultFlag);
        _shotFolder = ParseFlag(SmokeShotsFlag);
        if (string.IsNullOrEmpty(_resultPath) || _sessionClaimed)
        {
            // Not a smoke launch, or another probe instance already owns it.
            enabled = false;
            return;
        }

        _sessionClaimed = true;
        _ownsRun = true;

        if (!string.IsNullOrEmpty(_shotFolder))
        {
            // The recorded frames are compared against the 1920x1080 phase
            // baselines, so the visual evidence is captured at that size.
            Screen.SetResolution(1920, 1080, false);
        }

        // The journey crosses scene loads (Main Menu -> Ship Interior -> Main
        // Menu). Without this the probe would be destroyed with the cover scene
        // and the run would stall right after 进入任务.
        DontDestroyOnLoad(gameObject);

        // Automation must not depend on the window having OS focus: a background
        // player stops ticking, which would freeze the flow mid-journey.
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
    }

    private void Start()
    {
        if (!_ownsRun)
        {
            return;
        }

        StartCoroutine(RunProbe());
        // Watchdog guarantees termination if an unexpected exception stops the
        // probe coroutine.
        StartCoroutine(QuitWatchdog());
    }

    private static IEnumerator QuitWatchdog()
    {
        yield return new WaitForSecondsRealtime(300f);
        Debug.LogError("[StandaloneSmoke] watchdog timeout - forcing quit");
        Application.Quit(2);
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

    private IEnumerator RunProbe()
    {
        var log = new SmokeLog(_resultPath);
        Debug.Log("[StandaloneSmoke] probe start -> " + _resultPath);

        // ---- 1. Boot -> minimal title screen ----
        MainMenuController menu = null;
        yield return WaitFor(() =>
        {
            menu = FindFirstObjectByTypeHelper.Find<MainMenuController>();
            return menu != null && menu.IsReady;
        }, 25f);

        if (menu == null)
        {
            Debug.LogError("[StandaloneSmoke] title screen never became ready");
            log.Fail("title screen never became ready");
            Finish(log, 1);
            yield break;
        }

        log.Check("boot enters the title screen scene",
            SceneManager.GetActiveScene().name == SceneRouting.MainMenuSceneName);
        log.Check("exactly one MainMenuController",
            UnityEngine.Object.FindObjectsByType<MainMenuController>().Length == 1);

        GameSession session = GameSession.Instance;
        log.Check("exactly one authoritative GameSession",
            session != null
            && UnityEngine.Object.FindObjectsByType<GameSession>().Length == 1);
        log.Check("a fresh launch has no started run",
            session != null && !session.HasStartedRun);

        // ---- 2. Minimal copy: 活下去 + exactly four options ----
        Label title = menu.UiRoot?.Q<Label>(MainMenuNames.Title);
        log.Check("the only headline is 活下去", title != null && title.text == "活下去");
        log.Check("exactly four options", menu.MenuItems.Count == 4);

        string[] expectedOptions = { "新游戏", "继续游戏", "选项", "退出" };
        for (int i = 0; i < expectedOptions.Length && i < menu.MenuItems.Count; i++)
        {
            Label optionLabel = menu.MenuItems[i].Q<Label>(className: "menu-item-label");
            log.Check("option " + (i + 1) + " reads " + expectedOptions[i],
                optionLabel != null && optionLabel.text == expectedOptions[i]);
        }

        var copy = new System.Text.StringBuilder();
        menu.UiRoot?.Query<Label>().ForEach(label => copy.Append(label.text).Append('\n'));
        string screenCopy = copy.ToString();
        bool copyClean = true;
        foreach (string removed in MainMenuNames.RemovedCopy)
        {
            if (screenCopy.Contains(removed))
            {
                copyClean = false;
                log.Line("  still visible: " + removed);
            }
        }

        log.Check("no removed HUD / telemetry copy is visible", copyClean);
        log.Check("no 制作信息 button and no top bar / footer / telemetry",
            menu.UiRoot?.Q<Button>("CreditsButton") == null
            && menu.UiRoot?.Q<VisualElement>("MenuTopBar") == null
            && menu.UiRoot?.Q<VisualElement>("MenuFooter") == null
            && menu.UiRoot?.Q<VisualElement>("HeroTelemetry") == null);

        // ---- 3. Hero ship + deep space ----
        MainMenuShipHero hero = UnityEngine.Object.FindAnyObjectByType<MainMenuShipHero>();
        MainMenuStarfield stars = UnityEngine.Object.FindAnyObjectByType<MainMenuStarfield>();
        log.Check("hero is the real 3D ship asset (" + (hero != null ? hero.ShipRendererCount : 0)
                  + " renderers, " + (hero != null ? hero.NozzleCount : 0) + " nozzles)",
            hero != null && hero.IsShipPresent && hero.ShipRendererCount >= 40
            && hero.NozzleCount == 2);
        log.Check("deep-space starfield is built (" + (stars != null ? stars.StarCount : 0) + " stars)",
            stars != null && stars.IsActive && stars.LayerCount == 3 && stars.StarCount > 1000);
        log.Check("star layers drift with parallax and twinkle",
            stars != null && stars.ScrollSpeedOf(2) > stars.ScrollSpeedOf(0)
            && stars.TwinkleAmountOf(1) > 0.2f);

        Camera stageCamera = menu.StageCamera;
        if (stageCamera != null && hero != null)
        {
            stageCamera.aspect = 16f / 9f;
            float shipX = stageCamera.WorldToViewportPoint(hero.WorldBounds.center).x;
            log.Check("the ship sits in the left half (x=" + shipX.ToString("F3") + ")", shipX < 0.5f);

            float hereX = stageCamera.WorldToViewportPoint(hero.WorldBounds.center).x;
            float aheadX = stageCamera.WorldToViewportPoint(
                hero.WorldBounds.center + hero.ForwardWorld * 8f).x;
            log.Check("the ship nose points to the RIGHT (" + hereX.ToString("F3") + " -> "
                      + aheadX.ToString("F3") + ")", aheadX > hereX);
            stageCamera.ResetAspect();
        }

        // ---- 4. Focus navigation, disabled continue, controller hint ----
        log.Check("continuing is unavailable before the first run",
            !menu.IsContinueAvailable && !menu.ContinueButton.enabledSelf);
        log.Check("focus starts on 新游戏",
            menu.FocusIndex == 0 && menu.NewGameButton.ClassListContains("menu-item-focused"));

        menu.FocusNext();
        log.Check("focus skips the disabled 继续游戏 and lands on 选项",
            menu.FocusIndex == 2 && menu.OptionsButton.ClassListContains("menu-item-focused"));
        menu.FocusNext();
        log.Check("focus reaches 退出", menu.FocusIndex == 3);
        menu.FocusNext();
        log.Check("focus wraps back to 新游戏", menu.FocusIndex == 0);
        menu.FocusPrevious();
        log.Check("focus wraps backwards to 退出", menu.FocusIndex == 3);
        menu.SetFocus(0);

        InputDeviceTracker.NotifyGamepad();
        yield return null;
        log.Check("the controller hint appears for a gamepad",
            menu.IsGamepadHintVisible && menu.InputHintText == "(A) 确认");
        InputDeviceTracker.NotifyKeyboardMouse();
        yield return null;
        log.Check("the controller hint disappears for mouse/keyboard",
            !menu.IsGamepadHintVisible);
        InputDeviceTracker.NotifyGamepad();
        yield return null;

        // ---- 5. 选项 open -> back closes ----
        VisualElement optionsButton = menu.UiRoot?.Q<Button>(MainMenuNames.OptionsButton);
        yield return RealClick(optionsButton, "选项", log);
        yield return WaitFor(() => menu.IsOptionsOpen, 5f);
        log.Check("选项 opens", menu.IsOptionsOpen && menu.OpenModalCount == 1);
        log.Check("the modal is titled 选项",
            menu.UiRoot?.Q<Label>(MainMenuNames.OptionsTitle)?.text == "选项");
        log.Check("the options hint offers confirm and back",
            menu.InputHintText == "(A) 确认    (B) 返回");

        menu.HandleBackInput();
        yield return WaitFor(() => !menu.IsOptionsOpen, 5f);
        log.Check("back closes 选项", !menu.IsOptionsOpen);

        // ---- 6. A confirms 新游戏 -> cinematic -> ship interior ----
        // The mouse click above moved the focus onto 选项 (hover feeds focus on
        // purpose), so the player's next step is to select 新游戏 again.
        menu.SetFocus(0);
        log.Check("focus is back on 新游戏",
            menu.FocusIndex == 0 && menu.NewGameButton.ClassListContains("menu-item-focused"));
        log.Check("A confirms the focused 新游戏", menu.ConfirmFocused());
        MainMenuCinematic cinematic = menu.Cinematic;
        yield return null;
        log.Check("新游戏 starts the cinematic", cinematic != null && cinematic.IsPlaying);
        log.Check("新游戏 starts a run and enables 继续游戏",
            session.HasStartedRun && menu.IsContinueAvailable);
        log.Check("新游戏 resets the authoritative session",
            session.Minerals == GameSession.DefaultMinerals && session.Fuel == GameSession.DefaultFuel);

        float departureStartX = -1f;
        float departureEndX = -1f;
        bool sawFade = false;
        bool sawBoost = false;
        bool sawBlack = false;
        int guard = 0;
        while (guard < 1200
               && SceneManager.GetActiveScene().name == SceneRouting.MainMenuSceneName)
        {
            guard++;
            if (cinematic != null)
            {
                if (!sawFade && cinematic.MenuAlpha < 0.4f)
                {
                    sawFade = true;
                }

                if (!sawBoost && hero != null && hero.EngineTarget > 0.6f)
                {
                    sawBoost = true;
                }

                if (!sawBlack && cinematic.BlackAlpha > 0.85f)
                {
                    sawBlack = true;
                }

                if (hero != null && hero.IsDeparting && stageCamera != null)
                {
                    stageCamera.aspect = 16f / 9f;
                    float x = stageCamera.WorldToViewportPoint(hero.WorldBounds.center).x;
                    stageCamera.ResetAspect();
                    if (departureStartX < 0f)
                    {
                        departureStartX = x;
                    }

                    departureEndX = x;
                }
            }

            yield return null;
        }

        log.Check("the menu faded out", sawFade);
        log.Check("the thrusters boosted", sawBoost);
        log.Check("the shot reached the blackout", sawBlack);
        log.Check("the ship departed LEFT to RIGHT (" + departureStartX.ToString("F3") + " -> "
                  + departureEndX.ToString("F3") + ")",
            departureStartX >= 0f && departureEndX > departureStartX);
        log.Check("the title screen scene is gone",
            UnityEngine.Object.FindObjectsByType<MainMenuController>().Length == 0);

        // ---- 7. Ship interior: eyelids, then first person ----
        ShipGameController ship = null;
        yield return WaitFor(() =>
        {
            ship = FindFirstObjectByTypeHelper.Find<ShipGameController>();
            return ship != null && ship.IsReady;
        }, 30f);

        if (ship == null)
        {
            log.Fail("新游戏 never reached the ship interior");
            Finish(log, 1);
            yield break;
        }

        log.Check("新游戏 lands in the walkable ship interior (never Overview)",
            SceneManager.GetActiveScene().name == GameSession.ShipInteriorSceneName
            && UnityEngine.Object.FindObjectsByType<AppController>().Length == 0);
        log.Check("the eyelids were armed and control is locked",
            ship.EntryBlinkArmed && ship.IsEntryInputLocked);

        var stages = new System.Collections.Generic.HashSet<string>();
        guard = 0;
        while (ship.EntryBlink != null && guard < 1200)
        {
            guard++;
            stages.Add(ship.EntryBlink.StageLabel);
            yield return null;
        }

        log.Check("the eyelid beats played (stages: " + string.Join(",", stages) + ")",
            stages.Contains("beat1") && stages.Contains("beat2") && stages.Contains("beat3"));
        log.Check("the eyelid overlay was removed", ship.EntryBlink == null);
        log.Check("control is released after the eyes open",
            !ship.IsEntryInputLocked && ship.Player != null && ship.Player.InputEnabled);
        log.Check("the first-person camera renders",
            ship.Player.EyeCamera != null && ship.Player.EyeCamera.enabled);
        log.Check("no terminal UI covers the frame on entry",
            !ship.IsConsoleFocusActive && !ship.IsOverlayOpen);

        // The UI contract of the whole run (see UiIntegrityAudit): exactly one
        // hub screen, every screen-space document styled, and no overlay that
        // survived the scene change on its own.
        System.Collections.Generic.List<string> uiProblems = UiIntegrityAudit.Problems();
        log.Check("the ship interior UI is intact (" + UiIntegrityAudit.Describe(uiProblems) + ")",
            uiProblems.Count == 0);
        log.Info("ship interior UI state: " + UiIntegrityAudit.Snapshot());
        yield return CaptureFrame("smoke_ship_interior");

        // ---- 8. Console view + the full pause loop ----
        // A virtual gamepad is added so this standalone run drives the REAL
        // device path (buttons through the Input System) with no hardware present.
        Gamepad pad = InputSystem.AddDevice<Gamepad>();
        yield return WaitFor(() => Gamepad.current == pad, 5f);
        log.Check("a gamepad device is available for the standalone run",
            Gamepad.current == pad);

        Debug.Log("[StandaloneSmoke] step 8: console view + pause loop");
        InputDeviceTracker.NotifyGamepad();
        yield return null;

        // ---- 8a. Phase 09A_3: block arms + the wrist presentation ----
        // Runs on the real device path: the locomotion push and the Wrist press
        // are Input System gamepad events, never direct API calls, so this is the
        // standalone proof of  "Wrist binding -> existing wrist system ->
        // presentation bridge -> block-arm transform blend".
        Debug.Log("[StandaloneSmoke] step 8a: first-person arms + wrist");
        log.Check("the first-person arms exist exactly once after the eyes open",
            CountArms() == 1);
        log.Check("the block arms follow the player rig in body space",
            ship.Arms != null && ship.Player != null
            && ship.Arms.transform.parent == ship.Player.transform
            && Mathf.Abs(ship.Arms.transform.localPosition.y
                         - ship.Player.eyeHeight) < 0.001f);
        log.Check("the arms are presented in free first person",
            ship.Arms != null && ship.Arms.IsVisible);
        log.Check("the arms present Rest while standing still",
            ship.Arms != null && ship.Arms.CurrentStateName == "Rest");

        InputSystem.QueueStateEvent(pad, new GamepadState
        {
            leftStick = new Vector2(0f, 0.9f)
        });
        bool sawWalk = false;
        float walkDeadline = Time.realtimeSinceStartup + 0.9f;
        while (Time.realtimeSinceStartup < walkDeadline)
        {
            sawWalk |= ship.Arms != null && ship.Arms.CurrentStateName == "Walk";
            yield return null;
        }

        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return new WaitForSecondsRealtime(0.6f);
        log.Check("the arms present Walk while the player really moves", sawWalk);
        log.Check("the arms return to the rest presentation when the player stops",
            ship.Arms != null && ship.Arms.CurrentStateName == "Rest");
        log.Check("the block arms are pure presentation geometry (no collider, no rig)",
            ship.Arms != null
            && ship.Arms.GetComponentsInChildren<Collider>(true).Length == 0);

        // The Wrist binding opens the AUTHORITY at once; the existing terminal
        // only appears once the left wrist has risen into view.
        yield return PressGamepadButton(pad, GamepadButton.North);
        log.Check("the Wrist binding opens the wrist authority", ship.IsWristOpen);
        log.Check("the wrist terminal is NOT presented yet (the arm raises first)",
            !ship.IsWristTerminalRevealed);
        log.Check("the arm starts raising",
            ship.Arms != null
            && ship.Arms.CurrentWristPhase == FirstPersonBlockArmsController.WristPhase.Raise);

        yield return WaitFor(() => ship.IsWristTerminalRevealed, 4f);
        log.Check("the existing wrist terminal is revealed after the raise",
            ship.IsWristTerminalRevealed && ship.Wrist.Display != null
            && ship.Wrist.Display.Visible);
        log.Check("the reveal waited for the raise animation (progress "
                  + ship.WristPresentation.LastRaiseProgress.ToString("0.00") + ")",
            ship.WristPresentation.LastRaiseProgress > 0.7f);
        yield return WaitFor(() => ship.Arms != null
            && ship.Arms.CurrentStateName == "WristHold", 4f);
        log.Check("the wrist presentation settles into the hold loop",
            ship.Arms != null && ship.Arms.CurrentStateName == "WristHold");
        log.Check("the wrist screens glow while the terminal is presented",
            ship.Arms != null && ship.Arms.IsWristScreenActive);

        yield return PressGamepadButton(pad, GamepadButton.North);
        log.Check("the Wrist binding closes the wrist authority", !ship.IsWristOpen);
        log.Check("the terminal releases before the arm lowers",
            !ship.IsWristTerminalRevealed && !ship.Wrist.Display.Visible);
        log.Check("the arm starts lowering",
            ship.Arms != null
            && ship.Arms.CurrentWristPhase == FirstPersonBlockArmsController.WristPhase.Lower);
        yield return WaitFor(() => ship.Arms != null
            && ship.Arms.CurrentWristPhase == FirstPersonBlockArmsController.WristPhase.Rest, 4f);
        log.Check("the arm returns to the rest presentation",
            ship.Arms != null && ship.Arms.CurrentStateName == "Rest"
            && ship.Player.InputEnabled);

        ShipConsoleInteractable overviewConsole = null;
        foreach (ShipConsoleInteractable console in ship.Consoles)
        {
            if (console.ConsoleId == 1)
            {
                overviewConsole = console;
                break;
            }
        }

        log.Check("the 总览 terminal exists on the bridge", overviewConsole != null);

        if (overviewConsole != null)
        {
            log.Check("the gamepad prompt offers viewing the console",
                overviewConsole.GetInteractionPrompt() == "[A] 查看 总览终端");
            InputDeviceTracker.NotifyKeyboardMouse();
            yield return null;
            log.Check("the keyboard prompt offers viewing the console",
                overviewConsole.GetInteractionPrompt() == "[E] 查看 总览终端");
            InputDeviceTracker.NotifyGamepad();
            yield return null;
        }

        if (overviewConsole != null && ship.Focus != null && ship.PauseMenu != null)
        {
            // Stand in front of the console and look at its screen.
            Vector3 consoleForward = overviewConsole.transform.forward;
            ship.Player.TeleportTo(overviewConsole.transform.position + consoleForward * 1.9f);
            ship.Player.SetFacing(overviewConsole.transform.eulerAngles.y + 180f, 0f);
            yield return WaitFor(() =>
                ship.Interactor.CurrentTarget == (IFirstPersonInteractable)overviewConsole, 4f);

            // A -> console view.
            yield return PressGamepadButton(pad, GamepadButton.South);
            yield return WaitFor(() => ship.Focus.FocusActive, 3f);
            log.Check("A enters the console view", ship.Focus.FocusActive);
            log.Check("movement and look are disabled in the view", !ship.Player.InputEnabled);
            log.Check("the view hint names the Back button ("
                      + ship.Hud.FocusPromptText + ")", ship.Hud.FocusPromptText == "[B] 返回");
            log.Check("viewing a console never quits", AppQuit.RequestCount == 0);

            Transform anchor = overviewConsole.GetComponent<WorldConsoleSurface>().FocusAnchor;
            yield return WaitFor(() =>
                ship.Focus.State == ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus, 4f);
            log.Check("the camera reached the console view anchor",
                Vector3.Distance(ship.Player.EyeCamera.transform.position, anchor.position) < 0.06f);
            log.Check("the first-person arms are hidden in the console view",
                ship.Arms != null && !ship.Arms.IsVisible);

            // Menu/Start -> pause over the frozen console view.
            yield return PressGamepadButton(pad, GamepadButton.Start);
            yield return WaitFor(() => ship.PauseMenu.IsOpen, 3f);
            log.Check("Menu/Start opens the pause over the console view", ship.PauseMenu.IsOpen);
            log.Check("the pause freezes time", Mathf.Approximately(Time.timeScale, 0f));
            log.Check("the console view stays as the underlying state", ship.Focus.FocusActive);
            log.Check("the pause focus lands on 继续游戏", ship.PauseMenu.FocusedItemId == "Resume");

            // Continue -> back to the console view.
            yield return PressGamepadButton(pad, GamepadButton.South);
            yield return WaitFor(() => !ship.PauseMenu.IsOpen, 3f);
            log.Check("继续游戏 closes the pause", !ship.PauseMenu.IsOpen);
            log.Check("time resumes", Mathf.Approximately(Time.timeScale, 1f));
            log.Check("we are back in the console view", ship.Focus.FocusActive);

            // B -> leave the console view.
            yield return PressGamepadButton(pad, GamepadButton.East);
            yield return WaitFor(() =>
                ship.Focus.State == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore, 4f);
            log.Check("B leaves the console view",
                ship.Focus.State == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore);
            yield return WaitFor(() => ship.Player.InputEnabled, 3f);
            log.Check("movement and look are restored", ship.Player.InputEnabled);
            log.Check("the first-person arms come back after leaving the console view",
                ship.Arms != null && ship.Arms.IsVisible);
            log.Check("no quit from the console view", AppQuit.RequestCount == 0);

            // ---- 8b. Pause: open / continue / back / return to main menu ----
            yield return PressGamepadButton(pad, GamepadButton.Start);
            yield return WaitFor(() => ship.PauseMenu.IsOpen, 3f);
            log.Check("Menu/Start opens the pause in gameplay", ship.PauseMenu.IsOpen);
            log.Check("the pause freezes gameplay",
                Mathf.Approximately(Time.timeScale, 0f) && !ship.Player.InputEnabled);
            log.Check("the pause freezes the arms presentation too",
                ship.Arms != null && ship.Arms.IsFrozen);

            // Pause must overlay exactly the running context: no extra screen may
            // become visible just because the pause opened.
            System.Collections.Generic.List<string> pauseProblems = UiIntegrityAudit.Problems();
            log.Check("the pause overlays the current context only ("
                      + UiIntegrityAudit.Describe(pauseProblems) + ")",
                pauseProblems.Count == 0);
            log.Info("pause UI state: " + UiIntegrityAudit.Snapshot());
            yield return CaptureFrame("smoke_pause_gameplay");

            yield return PressGamepadButton(pad, GamepadButton.Start);
            yield return WaitFor(() => !ship.PauseMenu.IsOpen, 3f);
            log.Check("Menu/Start again continues the game",
                !ship.PauseMenu.IsOpen && Mathf.Approximately(Time.timeScale, 1f)
                && ship.Player.InputEnabled);
            yield return WaitFor(() => ship.Arms != null && !ship.Arms.IsFrozen, 2f);
            log.Check("continue releases the frozen arms presentation",
                ship.Arms != null && !ship.Arms.IsFrozen);

            yield return PressGamepadButton(pad, GamepadButton.Start);
            yield return WaitFor(() => ship.PauseMenu.IsOpen, 3f);
            yield return PressGamepadButton(pad, GamepadButton.East);
            yield return WaitFor(() => !ship.PauseMenu.IsOpen, 3f);
            log.Check("B continues the game from the pause",
                !ship.PauseMenu.IsOpen && ship.Player.InputEnabled);
            log.Check("B never quits", AppQuit.RequestCount == 0);

            int mineralsBefore = session.Minerals;
            yield return PressGamepadButton(pad, GamepadButton.Start);
            yield return WaitFor(() => ship.PauseMenu.IsOpen, 3f);
            yield return PressGamepadButton(pad, GamepadButton.DpadDown);
            log.Check("D-Pad moves the pause focus to 返回主菜单",
                ship.PauseMenu.FocusedItemId == "ReturnToMainMenu");
            yield return PressGamepadButton(pad, GamepadButton.South);

            MainMenuController menuBack = null;
            yield return WaitFor(() =>
            {
                menuBack = FindFirstObjectByTypeHelper.Find<MainMenuController>();
                return menuBack != null && menuBack.IsReady;
            }, 25f);

            log.Check("返回主菜单 reaches the title screen",
                menuBack != null
                && SceneManager.GetActiveScene().name == SceneRouting.MainMenuSceneName);
            log.Check("返回主菜单 restores normal time",
                Mathf.Approximately(Time.timeScale, 1f));
            log.Check("返回主菜单 never resets the session",
                GameSession.Instance == session && session.Minerals == mineralsBefore);
            log.Check("the run is still resumable and 继续游戏 is enabled",
                session.HasStartedRun && menuBack != null && menuBack.ContinueButton.enabledSelf);
            log.Check("no arms instance survives the return to the title screen",
                CountArms() == 0);
            log.Check("no quit has happened yet", AppQuit.RequestCount == 0);

            // ---- 8c. 继续游戏 -> the same ship state ----
            menuBack.SetFocus(1);
            yield return PressGamepadButton(pad, GamepadButton.South);

            ShipGameController resumed = null;
            yield return WaitFor(() =>
            {
                resumed = FindFirstObjectByTypeHelper.Find<ShipGameController>();
                return resumed != null && resumed.IsReady;
            }, 30f);

            log.Check("继续游戏 returns to the ship interior",
                resumed != null
                && SceneManager.GetActiveScene().name == GameSession.ShipInteriorSceneName);
            log.Check("继续游戏 keeps the same session",
                GameSession.Instance == session && session.Minerals == mineralsBefore);
            log.Check("继续游戏 creates the arms exactly once (no duplicates)",
                resumed != null && CountArms() == 1);

            ship = resumed;

            // 继续游戏 opens the eyes with the SHORT eyelid and only then hands
            // control back; wait for that before driving gameplay again.
            yield return WaitFor(() => !ship.IsEntryInputLocked, 6f);
            log.Check("继续游戏 releases first-person control after the short eyelid",
                !ship.IsEntryInputLocked && ship.Player.InputEnabled);

            // ---- 8d. Terminal focus still reaches the Overview screen ----
            if (ship != null && ship.Focus != null)
            {
                ShipConsoleInteractable console01 = null;
                foreach (ShipConsoleInteractable console in ship.Consoles)
                {
                    if (console.ConsoleId == 1)
                    {
                        console01 = console;
                        break;
                    }
                }

                bool entered = console01 != null && ship.Focus.RequestEnter(console01);
                yield return WaitFor(() => ship.Focus.FocusActive, 6f);
                log.Check("the player can enter the Overview terminal from inside the ship",
                    entered && ship.Focus.FocusActive);

                ShipConsoleScreenHost overviewHost = null;
                foreach (ShipConsoleScreenHost candidate
                         in UnityEngine.Object.FindObjectsByType<ShipConsoleScreenHost>())
                {
                    if (candidate.MappedScreen == AppScreen.Overview)
                    {
                        overviewHost = candidate;
                        break;
                    }
                }

                log.Check("the Overview terminal is bound to the shared Overview screen",
                    overviewHost != null && overviewHost.IsBound
                    && overviewHost.CountVisibleScreens() == 1);

                // Console 02 (仓库总览) hosts the dedicated warehouse / 出发准备 tree
                // instead of a ShipConsoleScreenHost, so the mapping is read from
                // the console surfaces rather than from one component type.
                var available = new System.Collections.Generic.HashSet<AppScreen>();
                int surfaces = 0;
                foreach (ShipConsoleInteractable console in ship.Consoles)
                {
                    var surface = console.GetComponent<WorldConsoleSurface>();
                    if (surface == null)
                    {
                        continue;
                    }

                    surfaces++;
                    if (surface.ScreenHost != null)
                    {
                        available.Add(surface.ScreenHost.MappedScreen);
                    }

                    if (surface.WarehouseHost != null)
                    {
                        // 仓库总览 == the 出发准备 / Preparation production screen.
                        available.Add(AppScreen.Preparation);
                    }
                }

                log.Check("all four terminals are wired (" + surfaces + " surfaces)", surfaces == 4);
                log.Check("Preparation / Processing / Outpost terminals remain available",
                    available.Contains(AppScreen.Overview)
                    && available.Contains(AppScreen.Preparation)
                    && available.Contains(AppScreen.Processing)
                    && available.Contains(AppScreen.Outpost));

                ship.Focus.RequestExit();
                yield return WaitFor(() => !ship.Focus.FocusActive, 6f);
                log.Check("leaving the terminal returns to the first-person ship"
                          + " (focusActive=" + ship.Focus.FocusActive
                          + " state=" + ship.Focus.State
                          + " paused=" + ship.IsPaused
                          + " owner=" + ship.PointerOwner
                          + " wrist=" + ship.IsWristOpen
                          + " overlay=" + ship.IsOverlayOpen
                          + " entryLock=" + ship.IsEntryInputLocked
                          + " input=" + ship.Player.InputEnabled
                          + " timeScale=" + Time.timeScale + ")",
                    !ship.Focus.FocusActive && ship.Player.InputEnabled);
            }

            // ---- 8e. Airlock device panels: A opens, B closes (Phase 08 §14) ----
            yield return RunAirlockDevicePanels(log, ship, pad, session);

            // ---- 9. existing Phase 01-05 state journey (scene independent) ----
            Debug.Log("[StandaloneSmoke] step 9: session + processing journey");
            RunSessionJourney(log, session);
            yield return RunProcessJourney(log, session, null);

            // ---- 10. Pause -> 退出游戏 must terminate the process ----
            yield return PressGamepadButton(pad, GamepadButton.Start);
            yield return WaitFor(() => ship.PauseMenu.IsOpen, 3f);
            log.Check("the pause reopens after the journey", ship.PauseMenu.IsOpen);

            ship.PauseMenu.SetFocus(2);
            log.Check("focus is on 退出游戏", ship.PauseMenu.FocusedItemId == "QuitGame");
            log.Check("nothing has quit before the explicit confirm",
                AppQuit.RequestCount == 0);

            log.Line("EXIT: confirming 退出游戏 in the pause menu (the process must terminate)");
            WriteResult(log, log.Failures == 0);
            yield return PressGamepadButton(pad, GamepadButton.South);

            // Still alive after ~1.5 s means the quit action is broken.
            for (int i = 0; i < 90; i++)
            {
                yield return null;
            }

            log.Fail("退出游戏 did not terminate the application");
            WriteResult(log, false);
            Application.Quit(1);
            yield break;
        }

        // The console view could not be set up: fail loudly instead of passing.
        log.Fail("the console view / pause loop could not run (console or pause menu missing)");
        Finish(log, 1);
    }

    /// <summary>Queues a real press/release of one gamepad button.</summary>
    private static IEnumerator PressGamepadButton(Gamepad pad, GamepadButton button)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null;
        yield return null;
    }

    /// <summary>
    /// Optional visual evidence for the real route: writes one 1920x1080 frame
    /// into the folder given with <c>-smokeShots &lt;folder&gt;</c>. Inert (and
    /// free) when the flag is absent, so a normal smoke run is unchanged.
    /// </summary>
    private IEnumerator CaptureFrame(string name)
    {
        if (string.IsNullOrEmpty(_shotFolder))
        {
            yield break;
        }

        try
        {
            Directory.CreateDirectory(_shotFolder);
        }
        catch (Exception)
        {
            yield break;
        }

        string path = Path.Combine(_shotFolder, name + ".png");
        if (Screen.width != 1920 || Screen.height != 1080)
        {
            Screen.SetResolution(1920, 1080, false);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 600; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 20000)
            {
                break;
            }

            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.15f);
    }

    /// <summary>Live block-arms instances (Phase 09A_3 duplicate guard).</summary>
    private static int CountArms()
    {
        return UnityEngine.Object.FindObjectsByType<FirstPersonBlockArmsController>(
            FindObjectsInactive.Include).Length;
    }
    /// <summary>
    /// Phase 01-05 authoritative STATE journey. Scene independent, so it runs in
    /// every host (the four-screen UI app or the walkable ship).
    /// </summary>
    private static void RunSessionJourney(SmokeLog log, GameSession session)
    {
        session.ResetSession();

        log.Check("defaults minerals=20 fuel=8 alloy=5 modules LV1",
            session.Minerals == 20 && session.Fuel == 8 && session.Alloy == 5
            && session.RefineryLevel == 1 && session.HarvesterLevel == 1
            && session.OutpostLevel == 1);

        log.Check("derived defaults eff=100 duration=2.0 cap=LV.2",
            session.HarvesterEfficiencyPercent == 100
            && Math.Abs(session.RefineryProcessSeconds - 2.0f) < 0.001f
            && session.SubsystemLevelCap == 2);

        session.AddMinerals(10);
        log.Check("after +10 minerals minerals=30", session.Minerals == 30);

        bool started5 = session.TryStartProcess(5);
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        log.Check("batch5 complete -> minerals=25 fuel=10 alloy=6",
            started5 && session.Minerals == 25 && session.Fuel == 10 && session.Alloy == 6);

        bool upgradeOk = session.TryUpgrade(GameSession.OutpostModule.Refinery)
                         == GameSession.UpgradeFailureReason.None;
        log.Check("refinery upgrade -> alloy=3 level=2 duration=1.6",
            upgradeOk && session.Alloy == 3 && session.RefineryLevel == 2
            && Math.Abs(session.RefineryProcessSeconds - 1.6f) < 0.001f);
    }

    /// <summary>Phase 01-03 UI journey (needs the four-screen UI app host).</summary>
    private static void RunLegacyUiJourney(SmokeLog log, AppController app, GameSession session)
    {
        app.ShowScreen(AppScreen.Overview);
        log.Check("screen overview active/unique",
            app.CurrentScreen == AppScreen.Overview && app.VisibleScreenCount == 1);

        app.ShowScreen(AppScreen.Preparation);
        log.Check("screen preparation active/unique",
            app.CurrentScreen == AppScreen.Preparation && app.VisibleScreenCount == 1);

        app.ShowScreen(AppScreen.Processing);
        log.Check("screen processing active/unique",
            app.CurrentScreen == AppScreen.Processing && app.VisibleScreenCount == 1);

        app.ShowScreen(AppScreen.Outpost);
        log.Check("screen outpost active/unique",
            app.CurrentScreen == AppScreen.Outpost && app.VisibleScreenCount == 1);

        // Phase 03: the Preparation loadout journey (Chinese-first copy).
        app.ShowScreen(AppScreen.Preparation);
        Label cargoLabel = app.UiRoot?.Q<Label>(ElementNames.CargoValue);
        Label stateLabel = app.UiRoot?.Q<Label>(ElementNames.LoadoutStateLabel);
        Button confirmButton = app.UiRoot?.Q<Button>(ElementNames.ConfirmLoadoutButton);
        log.Check("preparation defaults cargo 45/100 state 就绪 confirm enabled",
            app.CurrentScreen == AppScreen.Preparation && app.VisibleScreenCount == 1
            && cargoLabel != null && cargoLabel.text == "45 / 100"
            && stateLabel != null && stateLabel.text == "就绪"
            && confirmButton != null && confirmButton.enabledSelf);

        bool selectBattery = session.TrySetBatterySelected(true);
        log.Check("select battery -> cargo 70/100",
            selectBattery && session.CargoWeight == 70
            && cargoLabel != null && cargoLabel.text == "70 / 100");

        bool selectDrone = session.TrySetDroneSelected(true);
        log.Check("select drone -> cargo 90/100",
            selectDrone && session.CargoWeight == 90
            && cargoLabel != null && cargoLabel.text == "90 / 100");

        bool confirmLoadout = session.TryConfirmLoadout();
        log.Check("confirm loadout -> 已确认",
            confirmLoadout && session.LoadoutConfirmed
            && stateLabel != null && stateLabel.text == "已确认");

        app.ShowScreen(AppScreen.Overview);
        app.ShowScreen(AppScreen.Preparation);
        log.Check("loadout preserved across navigation",
            session.BatterySelected && session.DroneSelected && session.LoadoutConfirmed
            && cargoLabel != null && cargoLabel.text == "90 / 100");

        bool editAfterConfirm = session.TrySetBatterySelected(false);
        log.Check("edit after confirm clears confirmation (cargo 65)",
            editAfterConfirm && !session.LoadoutConfirmed
            && cargoLabel != null && cargoLabel.text == "65 / 100");

        app.ShowScreen(AppScreen.Overview);
        Label overviewMinerals = app.UiRoot?.Q<Label>(ElementNames.MineralsValue);
        Label overviewAlloy = app.UiRoot?.Q<Label>(ElementNames.AlloyValue);
        log.Check("overview reflects shared state (minerals=25 alloy=3)",
            overviewMinerals != null && overviewMinerals.text == "25"
            && overviewAlloy != null && overviewAlloy.text == "3");

        bool blockedByCore = session.TryUpgrade(GameSession.OutpostModule.Refinery)
                             == GameSession.UpgradeFailureReason.CoreLevelRequired;
        log.Check("refinery LV3 blocked by core (alloy unchanged)",
            blockedByCore && session.RefineryLevel == 2 && session.Alloy == 3);
        bool coreLack = session.TryUpgrade(GameSession.OutpostModule.Core)
                        == GameSession.UpgradeFailureReason.InsufficientAlloy;
        log.Check("core upgrade insufficient alloy",
            coreLack && session.OutpostLevel == 1 && session.Alloy == 3);
    }

    /// <summary>
    /// Phase 04/05 journey with real timers, then the state regression. The
    /// processing/handling assertions run in every host; the label assertions
    /// only when the four-screen UI app is the active host.
    /// </summary>
    private static IEnumerator RunProcessJourney(SmokeLog log, GameSession session,
        AppController app)
    {
        string host = app != null ? "hub" : "ship";
        if (app != null)
        {
            app.ShowScreen(AppScreen.Outpost);
            yield return null;
            yield return null;
            Label outpostAlloy = app.UiRoot?.Q<Label>(ElementNames.OutpostAlloyValue);
            Label refineryStatus = app.UiRoot?.Q<Label>(ElementNames.RefineryLevelValue);
            Label detailDuration = app.UiRoot?.Q<Label>(ElementNames.ModuleCurrentEffectValue);
            Label detailName = app.UiRoot?.Q<Label>(ElementNames.ModuleDetailName);
            Button outpostUpgrade = app.UiRoot?.Q<Button>(ElementNames.OutpostUpgradeButton);
            log.Check("outpost UI shows refinery LV.2 duration 1.6 CTA core-locked",
                outpostAlloy != null && outpostAlloy.text == "3"
                && refineryStatus != null && refineryStatus.text == "LV.2"
                && detailName != null && detailName.text == "精炼模块"
                && detailDuration != null && detailDuration.text == "1.6s"
                && outpostUpgrade != null && outpostUpgrade.text == "需要核心升级"
                && !outpostUpgrade.enabledSelf);
        }
        else
        {
            log.Check("outpost state derived values in the " + host + " host",
                session.RefineryLevel == 2 && session.Alloy == 3
                && session.SubsystemLevelCap == 2);
        }

        Label mineralsLabel = null;
        Label fuelLabel = null;
        Label statusLabel = null;
        Label durationMeta = null;
        Label refineryMeta = null;
        Button processStart = null;
        if (app != null)
        {
            app.ShowScreen(AppScreen.Processing);
            mineralsLabel = app.UiRoot?.Q<Label>(ElementNames.ProcessingMineralsValue);
            fuelLabel = app.UiRoot?.Q<Label>(ElementNames.ProcessingFuelValue);
            statusLabel = app.UiRoot?.Q<Label>(ElementNames.ProcessingStatusLabel);
            durationMeta = app.UiRoot?.Q<Label>(ElementNames.ProcessingDurationValue);
            refineryMeta = app.UiRoot?.Q<Label>(ElementNames.ProcessingRefineryLevel);
            processStart = app.UiRoot?.Q<Button>(ElementNames.ProcessingStartButton);
            log.Check("processing shows refinery LV.2 duration 1.6s",
                refineryMeta != null && refineryMeta.text == "LV.2"
                && durationMeta != null && durationMeta.text == "1.6s");
        }
        else
        {
            log.Check("processing host has no UI app: duration derived from LV.2",
                Math.Abs(session.RefineryProcessSeconds - 1.6f) < 0.001f);
        }

        bool batch10 = session.TryStartProcess(10);
        log.Check("start batch 10 -> minerals=15 state Running",
            batch10 && session.Minerals == 15 && session.IsProcessing
            && (app == null || (mineralsLabel != null && mineralsLabel.text == "15"
                                && processStart != null && processStart.text == "加工中"
                                && !processStart.enabledSelf)));

        if (app != null)
        {
            app.ShowScreen(AppScreen.Overview);
            app.ShowScreen(AppScreen.Preparation);
            app.ShowScreen(AppScreen.Processing);
        }

        log.Check("processing persists across navigation",
            session.IsProcessing && session.Minerals == 15);

        yield return WaitForProcessDone(session, 30f);
        log.Check("batch10 complete -> fuel=14 alloy=5",
            session.ProcessState == GameSession.RefineryProcessState.Complete
            && session.Fuel == 14 && session.Alloy == 5
            && (app == null || (fuelLabel != null && fuelLabel.text == "14")));

        bool again = session.TryStartProcess(5);
        log.Check("re-start batch 5 after complete -> minerals=10",
            again && session.Minerals == 10 && session.IsProcessing);
        yield return WaitForProcessDone(session, 30f);
        log.Check("batch5 complete -> fuel=16 alloy=6 status 加工完成",
            session.ProcessState == GameSession.RefineryProcessState.Complete
            && session.Fuel == 16 && session.Alloy == 6
            && (app == null || (statusLabel != null && statusLabel.text == "加工完成")));

        // §73: a short cross-screen state regression after the menu transition.
        if (app != null)
        {
            app.ShowScreen(AppScreen.Overview);
            Label overviewMinerals = app.UiRoot?.Q<Label>(ElementNames.MineralsValue);
            log.Check("state regression starts on Overview with the settled state",
                app.CurrentScreen == AppScreen.Overview && overviewMinerals != null
                && overviewMinerals.text == "10");
            app.ShowScreen(AppScreen.Preparation);
            app.ShowScreen(AppScreen.Processing);
            app.ShowScreen(AppScreen.Outpost);
            log.Check("state regression ends on Outpost with one visible screen",
                app.CurrentScreen == AppScreen.Outpost && app.VisibleScreenCount == 1);
        }
        else
        {
            log.Check("state regression: settled state is shared in the " + host + " host",
                session.Minerals == 10 && session.Fuel == 16 && session.Alloy == 6
                && session.ProcessState == GameSession.RefineryProcessState.Complete);
        }
    }

    /// <summary>Waits (bounded) for a running process to settle via the real
    /// session timer; completes immediately if already finished.</summary>
    private static IEnumerator WaitForProcessDone(GameSession session, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (session.IsProcessing && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    /// <summary>Real UI Toolkit click through the shared pointer simulator.</summary>
    private static IEnumerator RealClick(VisualElement element, string label, SmokeLog log)
    {
        if (element == null)
        {
            log.Fail("cannot click missing element: " + label);
            yield break;
        }

        // Let the panel lay the element out first: a click targets its centre,
        // and a synthetic click only lands when the panel resolves the element
        // itself under that point.
        yield return null;
        if (element.worldBound.width < 1f || element.worldBound.height < 1f)
        {
            log.Fail("element not laid out before the click: " + label);
            yield break;
        }

        if (!UiToolkitPointerSimulator.Click(element))
        {
            log.Fail("pointer event could not be dispatched to: " + label);
            yield break;
        }

        yield return null;
        yield return null;
    }

    /// <summary>
    /// Waits (bounded) for a condition. The bound is REAL TIME, not frames: the
    /// player runs at thousands of frames per second without vsync, so a frame
    /// budget cannot express "wait for a 0.18 s fade and a scene load".
    /// </summary>
    private static IEnumerator WaitFor(Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    /// <summary>
    /// Phase 08 §14: the two Airlock device panels, driven with a REAL gamepad
    /// (queued Input System state events, the real interactor, the real pages).
    ///
    ///   装备整理   (equipment locker)     A opens, B closes, control restored
    ///   出征前准备 (deployment station)   A opens, Menu/Start pauses above it,
    ///                                     B resumes the pause WITHOUT closing
    ///                                     the panel, the next B closes it
    ///
    /// Every step is asserted against the LIVE panel state (ShipOverlayUi) and
    /// against the budget: no quit, no scene change, no session reset.
    /// </summary>
    private static IEnumerator RunAirlockDevicePanels(SmokeLog log,
        ShipGameController ship, Gamepad pad, GameSession session)
    {
        if (ship == null || ship.Overlay == null)
        {
            log.Fail("the ship has no device-panel authority (ShipOverlayUi)");
            yield break;
        }

        ShipOverlayUi overlay = ship.Overlay;
        Debug.Log("[StandaloneSmoke] step 8e: airlock device panels");

        // The whole section is driven with the virtual gamepad: the panel hints
        // must therefore read the controller binding.
        InputDeviceTracker.NotifyGamepad();

        // ---- 装备整理: equipment locker -> shared loadout ----
        var locker = UnityEngine.Object.FindAnyObjectByType<EquipmentLockerInteractable>();
        log.Check("the Airlock equipment locker exists", locker != null);

        if (locker != null)
        {
            int lockerBackBaseline = overlay.BackActionCount;

            ship.Player.TeleportTo(new Vector3(-1.55f, 0f, -12.6f));
            ship.Player.SetFacing(270f, 0f);
            yield return WaitFor(() =>
                ReferenceEquals(ship.Interactor.CurrentTarget,
                    (IFirstPersonInteractable)locker), 4f);
            log.Check("gazing at 装备整理 offers the interaction",
                ReferenceEquals(ship.Interactor.CurrentTarget,
                    (IFirstPersonInteractable)locker));

            yield return PressGamepadButton(pad, GamepadButton.South);
            yield return WaitFor(() => overlay.IsOpen
                && overlay.Mode == ShipOverlayUi.OverlayMode.Loadout, 5f);

            log.Check("A opens 装备整理 (the shared loadout page)",
                overlay.IsOpen && overlay.Mode == ShipOverlayUi.OverlayMode.Loadout);
            log.Check("the panel shows the controller Back binding ("
                      + overlay.BackHintText + ")",
                overlay.BackHintText == "[B] 返回");
            log.Check("movement and look are frozen inside the panel",
                !ship.Player.InputEnabled);
            log.Check("the panel owns the pointer",
                ship.PointerOwner == ShipGameController.ShipPointerOwner.ScreenModal);
            log.Check("opening 装备整理 never quits", AppQuit.RequestCount == 0);
            log.Check("opening 装备整理 never leaves the ship",
                SceneManager.GetActiveScene().name == GameSession.ShipInteriorSceneName);
            log.Check("opening 装备整理 keeps the same session",
                GameSession.Instance == session);

            yield return PressGamepadButton(pad, GamepadButton.East);
            yield return WaitFor(() => !overlay.IsOpen && ship.Player.InputEnabled, 5f);

            log.Check("B closes 装备整理", !overlay.IsOpen);
            log.Check("the panel restored first-person control",
                ship.Player.InputEnabled
                && ship.PointerOwner == ShipGameController.ShipPointerOwner.None);
            log.Check("closing is exactly ONE action ("
                      + (overlay.BackActionCount - lockerBackBaseline) + ")",
                overlay.BackActionCount == lockerBackBaseline + 1);
            log.Check("B does not open the pause", !ship.PauseMenu.IsOpen);
            log.Check("B closes nothing else (still inside the ship)",
                SceneManager.GetActiveScene().name == GameSession.ShipInteriorSceneName);
            log.Check("B never quits", AppQuit.RequestCount == 0);

            yield return WaitFor(() =>
                ReferenceEquals(ship.Interactor.CurrentTarget,
                    (IFirstPersonInteractable)locker), 4f);
            log.Check("the world interaction prompt is back after the panel closes",
                ReferenceEquals(ship.Interactor.CurrentTarget,
                    (IFirstPersonInteractable)locker)
                && ship.Interactor.ResolvePrompt().Contains("调整远征装备"));
        }

        // ---- 出征前准备: deployment station -> pause above the panel ----
        log.Check("the Airlock deployment station exists", ship.Airlock != null);

        if (ship.Airlock != null)
        {
            // BackActionCount is CUMULATIVE on the one overlay authority, so this
            // section asserts DELTAS from the baseline it starts with.
            int backBaseline = overlay.BackActionCount;

            ship.Player.TeleportTo(new Vector3(0f, 0f, -12.3f));
            ship.Player.SetFacing(180f, 0f);
            yield return WaitFor(() =>
                ReferenceEquals(ship.Interactor.CurrentTarget,
                    (IFirstPersonInteractable)ship.Airlock), 4f);

            yield return PressGamepadButton(pad, GamepadButton.South);
            yield return WaitFor(() => overlay.IsOpen
                && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm, 5f);

            log.Check("A opens 出征前准备 (the deployment confirmation)",
                overlay.IsOpen
                && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm);
            log.Check("the panel shows the controller Back binding ("
                      + overlay.BackHintText + ")",
                overlay.BackHintText == "[B] 返回");
            log.Check("opening 出征前准备 does NOT route to the surface",
                SceneManager.GetActiveScene().name == GameSession.ShipInteriorSceneName);

            // Menu/Start pauses ABOVE the panel and must not close it.
            yield return PressGamepadButton(pad, GamepadButton.Start);
            yield return WaitFor(() => ship.PauseMenu.IsOpen, 3f);

            log.Check("Menu/Start opens the pause over 出征前准备", ship.PauseMenu.IsOpen);
            log.Check("the pause freezes time", Mathf.Approximately(Time.timeScale, 0f));
            log.Check("the panel stays as the frozen underlying state",
                overlay.IsOpen
                && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm);
            log.Check("Menu/Start is not a Back action ("
                      + (overlay.BackActionCount - backBaseline) + " since the panel opened)",
                overlay.BackActionCount == backBaseline);

            // Back belongs to the pause: it resumes and leaves the panel open.
            yield return PressGamepadButton(pad, GamepadButton.East);
            yield return WaitFor(() => !ship.PauseMenu.IsOpen, 3f);

            log.Check("B resumes the pause", !ship.PauseMenu.IsOpen);
            log.Check("time resumes", Mathf.Approximately(Time.timeScale, 1f));
            log.Check("the panel is STILL open after the pause",
                overlay.IsOpen
                && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm);
            log.Check("resuming the pause did not close the panel ("
                      + (overlay.BackActionCount - backBaseline) + " Back actions)",
                overlay.BackActionCount == backBaseline);
            log.Check("the panel still owns the input",
                !ship.Player.InputEnabled);

            // The next B closes the panel for real.
            yield return PressGamepadButton(pad, GamepadButton.East);
            yield return WaitFor(() => !overlay.IsOpen && ship.Player.InputEnabled, 5f);

            log.Check("B closes 出征前准备", !overlay.IsOpen);
            log.Check("closing 出征前准备 restored first-person control",
                ship.Player.InputEnabled
                && ship.PointerOwner == ShipGameController.ShipPointerOwner.None);
            log.Check("the whole loop performed exactly one Back action ("
                      + (overlay.BackActionCount - backBaseline) + ")",
                overlay.BackActionCount == backBaseline + 1);
            log.Check("closing 出征前准备 never quits", AppQuit.RequestCount == 0);
            log.Check("closing 出征前准备 stays inside the ship",
                SceneManager.GetActiveScene().name == GameSession.ShipInteriorSceneName);
            log.Check("closing 出征前准备 keeps the same session",
                GameSession.Instance == session);

            yield return WaitFor(() =>
                ReferenceEquals(ship.Interactor.CurrentTarget,
                    (IFirstPersonInteractable)ship.Airlock), 4f);
            log.Check("the deployment prompt is back after the panel closes",
                ReferenceEquals(ship.Interactor.CurrentTarget,
                    (IFirstPersonInteractable)ship.Airlock)
                && ship.Interactor.ResolvePrompt().Contains("前往 NEREID-04 地表"));
        }
    }

    private void Finish(SmokeLog log, int exitCode)
    {
        WriteResult(log, log.Failures == 0 && exitCode == 0);
        Application.Quit(log.Failures == 0 && exitCode == 0 ? 0 : 1);
    }

    private void WriteResult(SmokeLog log, bool success)
    {
        log.Line(success ? "RESULT: SUCCESS" : "RESULT: FAILED");
        try
        {
            File.WriteAllText(_resultPath, log.ToString());
            Debug.Log("[StandaloneSmoke] wrote result: " + _resultPath
                      + (success ? " SUCCESS" : " FAILED"));
        }
        catch (Exception e)
        {
            Debug.LogError("[StandaloneSmoke] failed to write result file: " + e);
        }
    }

    /// <summary>
    /// Accumulates PASS/FAIL lines. A plain collector (instead of ref
    /// parameters) keeps the checks usable from iterator coroutines.
    ///
    /// Every line is flushed to disk immediately: if a player build ever hangs
    /// mid-journey, the result file still shows exactly which check was the last
    /// one reached, instead of losing the whole run.
    /// </summary>
    private sealed class SmokeLog
    {
        private readonly StringBuilder _lines = new StringBuilder();
        private readonly string _path;

        public SmokeLog(string path)
        {
            _path = path;
        }

        public int Failures { get; private set; }

        public void Line(string text)
        {
            _lines.AppendLine(text);
            Flush();
        }

        public void Check(string label, bool condition)
        {
            if (condition)
            {
                _lines.AppendLine("PASS :: " + label);
            }
            else
            {
                Failures++;
                _lines.AppendLine("FAIL :: " + label);
            }

            Flush();
        }

        public void Fail(string label)
        {
            Failures++;
            _lines.AppendLine("FAIL :: " + label);
            Flush();
        }

        public void Info(string text)
        {
            _lines.AppendLine("INFO :: " + text);
            Flush();
        }

        private void Flush()
        {
            if (string.IsNullOrEmpty(_path))
            {
                return;
            }

            try
            {
                File.WriteAllText(_path, _lines.ToString());
            }
            catch (Exception)
            {
                // A probe must never fail because of its own log file.
            }
        }

        public override string ToString()
        {
            return _lines.ToString();
        }
    }
}

/// <summary>Small indirection so the probe code stays clean on newer Unity APIs.</summary>
internal static class FindFirstObjectByTypeHelper
{
    public static T Find<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindAnyObjectByType<T>();
    }
}
