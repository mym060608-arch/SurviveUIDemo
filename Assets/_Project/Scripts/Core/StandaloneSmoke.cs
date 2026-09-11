using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
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

    private static bool _sessionClaimed;

    private string _resultPath;
    private bool _ownsRun;

    private void Awake()
    {
        _resultPath = ParseFlag(SmokeResultFlag);
        if (string.IsNullOrEmpty(_resultPath) || _sessionClaimed)
        {
            // Not a smoke launch, or another probe instance already owns it.
            enabled = false;
            return;
        }

        _sessionClaimed = true;
        _ownsRun = true;

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

        // ---- 8. Walk-in terminal flow: A / E on the 总览 terminal ----
        Debug.Log("[StandaloneSmoke] step 8: terminal flow");
        InputDeviceTracker.NotifyGamepad();
        yield return null;

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
            log.Check("the gamepad prompt names A, not E",
                overviewConsole.GetInteractionPrompt().StartsWith("(A)"));
            InputDeviceTracker.NotifyKeyboardMouse();
            yield return null;
            log.Check("the keyboard prompt names E",
                overviewConsole.GetInteractionPrompt().StartsWith("E"));
        }

        if (overviewConsole != null && ship.Focus != null)
        {
            bool entered = ship.Focus.RequestEnter(overviewConsole);
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

            // The other three production terminals stay available as well.
            // Console 02 (仓库总览) hosts the dedicated warehouse / 出发准备 tree
            // instead of a ShipConsoleScreenHost, so the mapping is read from the
            // console surfaces rather than from one component type.
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
            log.Check("leaving the terminal returns to the first-person ship",
                !ship.Focus.FocusActive && ship.Player.InputEnabled);
        }

        // ---- 9. existing Phase 01-05 state journey (scene independent) ----
        Debug.Log("[StandaloneSmoke] step 9: session + processing journey");
        RunSessionJourney(log, session);
        yield return RunProcessJourney(log, session, null);

        // ---- 10. Title screen round trip + 继续游戏 (no reset) ----
        Debug.Log("[StandaloneSmoke] step 10: continue journey");
        int mineralsBefore = session.Minerals;
        int fuelBefore = session.Fuel;
        int alloyBefore = session.Alloy;

        SceneRouting.EnterMainMenu();
        MainMenuController menuAgain = null;
        yield return WaitFor(() =>
        {
            menuAgain = FindFirstObjectByTypeHelper.Find<MainMenuController>();
            return menuAgain != null && menuAgain.IsReady;
        }, 25f);

        log.Check("the title screen is re-enterable after the mission entry",
            menuAgain != null
            && SceneManager.GetActiveScene().name == SceneRouting.MainMenuSceneName);
        log.Check("returning to the title screen never resets the session",
            GameSession.Instance == session
            && session.Minerals == mineralsBefore && session.Fuel == fuelBefore
            && session.Alloy == alloyBefore);
        log.Check("继续游戏 is available once a run has started",
            menuAgain != null && menuAgain.IsContinueAvailable
            && menuAgain.ContinueButton.enabledSelf);

        if (menuAgain != null)
        {
            menuAgain.SetFocus(1);
            log.Check("focus can rest on 继续游戏", menuAgain.FocusIndex == 1);
            log.Check("A confirms 继续游戏", menuAgain.ConfirmFocused());
            log.Check("继续游戏 plays the short shot",
                menuAgain.Cinematic != null && menuAgain.Cinematic.IsContinueShot);

            ShipGameController resumed = null;
            yield return WaitFor(() =>
            {
                resumed = FindFirstObjectByTypeHelper.Find<ShipGameController>();
                return resumed != null && resumed.IsReady;
            }, 30f);

            log.Check("继续游戏 returns to the existing ship state",
                resumed != null
                && SceneManager.GetActiveScene().name == GameSession.ShipInteriorSceneName);
            log.Check("继续游戏 never resets the session",
                GameSession.Instance == session
                && session.Minerals == mineralsBefore && session.Fuel == fuelBefore
                && session.Alloy == alloyBefore);
            log.Check("继续游戏 uses the short eyelid entry",
                resumed != null && resumed.EntryBlinkArmed);
        }

        // ---- 11. 退出 must terminate the application ----
        SceneRouting.EnterMainMenu();
        MainMenuController menuExit = null;
        yield return WaitFor(() =>
        {
            menuExit = FindFirstObjectByTypeHelper.Find<MainMenuController>();
            return menuExit != null && menuExit.IsReady;
        }, 25f);
        log.Check("title screen is ready for the exit smoke", menuExit != null);

        log.Line("EXIT: confirming 退出 (the process must terminate)");
        WriteResult(log, log.Failures == 0);
        yield return RealClick(menuExit?.UiRoot?.Q<Button>(MainMenuNames.ExitButton), "退出", log);

        // Still alive after ~1.5 s means the exit action is broken.
        for (int i = 0; i < 90; i++)
        {
            yield return null;
        }

        log.Fail("退出 did not terminate the application");
        WriteResult(log, false);
        Application.Quit(1);
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
