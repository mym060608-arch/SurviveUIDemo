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

        // ---- 1. Boot -> Main Menu (Phase 08 product cover) ----
        MainMenuController menu = null;
        yield return WaitFor(() =>
        {
            menu = FindFirstObjectByTypeHelper.Find<MainMenuController>();
            return menu != null && menu.IsReady;
        }, 25f);

        if (menu == null)
        {
            Debug.LogError("[StandaloneSmoke] main menu never became ready");
            log.Fail("main menu never became ready");
            Finish(log, 1);
            yield break;
        }

        Debug.Log("[StandaloneSmoke] main menu ready: "
                  + SceneManager.GetActiveScene().name);

        log.Check("boot enters the main menu scene",
            SceneManager.GetActiveScene().name == SceneRouting.MainMenuSceneName);
        log.Check("exactly one MainMenuController",
            UnityEngine.Object.FindObjectsByType<MainMenuController>().Length == 1);

        GameSession session = GameSession.Instance;
        log.Check("exactly one authoritative GameSession",
            session != null
            && UnityEngine.Object.FindObjectsByType<GameSession>().Length == 1);

        // The menu is presentation only: showing it never resets the state.
        log.Check("main menu shows launch defaults without resetting",
            session != null && session.Energy == 74 && session.Food == 51
            && session.Minerals == 20 && session.Fuel == 8 && session.Alloy == 5
            && session.OutpostLevel == 1 && session.RefineryLevel == 1);

        Button cta = menu.EnterMissionButton;
        Label ctaLabel = cta != null ? cta.Q<Label>(MainMenuNames.EnterMissionLabel) : null;
        log.Check("进入任务 CTA visible, enabled and labelled",
            cta != null && cta.enabledSelf
            && cta.resolvedStyle.display != DisplayStyle.None
            && ctaLabel != null && ctaLabel.text == "进入任务");

        VisualElement settingsButton = menu.UiRoot?.Q<Button>(MainMenuNames.SettingsButton);
        VisualElement creditsButton = menu.UiRoot?.Q<Button>(MainMenuNames.CreditsButton);
        Button exitButton = menu.UiRoot?.Q<Button>(MainMenuNames.ExitButton);
        log.Check("系统设置 / 制作信息 / 退出 all present",
            settingsButton != null && creditsButton != null && exitButton != null);

        // Hero visual: the REAL 3D ship asset on the left + the procedural
        // deep-space backdrop behind it (Phase 08 Revision composition).
        MainMenuShipHero hero = UnityEngine.Object.FindAnyObjectByType<MainMenuShipHero>();
        MainMenuStarfield stars = UnityEngine.Object.FindAnyObjectByType<MainMenuStarfield>();
        log.Check("hero is the real 3D main-menu ship asset (46 meshes, 2 nozzles)",
            hero != null && hero.IsShipPresent && hero.ShipRendererCount >= 40
            && hero.NozzleCount == 2 && menu.IsHeroShipActive);
        log.Check("hero ship sits in the LEFT half of the frame",
            hero != null && HeroScreenX(hero) < 0.62f);
        log.Check("deep-space starfield is built behind the ship",
            stars != null && menu.IsStarfieldActive && stars.LayerCount == 3
            && stars.StarCount > 1000);
        log.Check("start cinematic is authored and idle before 进入任务",
            menu.Cinematic != null && !menu.Cinematic.IsPlaying);

        // ---- 2. 系统设置 real click ----
        yield return RealClick(settingsButton, "系统设置", log);
        yield return WaitFor(() => menu.IsSettingsOpen, 5f);
        log.Check("系统设置 opens the settings modal",
            menu.IsSettingsOpen && menu.OpenModalCount == 1);

        // ---- 3. Background is blocked while the modal is open (§20) ----
        bool clickedBehind = UiToolkitPointerSimulator.Click(cta);
        yield return null;
        yield return null;
        log.Check("modal blocks the background CTA (no navigation)",
            clickedBehind && !menu.IsTransitioning
            && SceneManager.GetActiveScene().name == SceneRouting.MainMenuSceneName
            && menu.IsSettingsOpen);

        yield return RealClick(menu.UiRoot?.Q<Button>(MainMenuNames.SettingsCloseButton),
            "设置关闭", log);
        yield return WaitFor(() => !menu.IsSettingsOpen, 5f);
        yield return WaitFor(() => menu.OpenModalCount == 0, 5f);
        log.Check("设置关闭 hides the settings modal",
            !menu.IsSettingsOpen && menu.OpenModalCount == 0);

        // ---- 4. 制作信息 real click (and no modal stacking) ----
        // Opening settings first proves the background is blocked; the stacking
        // rule itself is then checked through the menu's own API.
        yield return RealClick(settingsButton, "系统设置(2)", log);
        yield return WaitFor(() => menu.IsSettingsOpen, 5f);
        bool blockedCredits = UiToolkitPointerSimulator.Click(creditsButton);
        yield return null;
        yield return null;
        log.Check("制作信息 behind the modal is blocked",
            blockedCredits && !menu.IsCreditsOpen && menu.IsSettingsOpen);

        menu.ShowCredits();
        yield return WaitFor(() => menu.IsCreditsOpen, 5f);
        // The outgoing modal is only LOGICALLY closed at this point: it keeps
        // fading (and stays displayed) for a few frames, so the "exactly one
        // modal" invariant is asserted once that fade has finished.
        yield return WaitFor(() => menu.OpenModalCount == 1, 5f);
        log.Check("制作信息 opens the credits modal without stacking",
            menu.IsCreditsOpen && !menu.IsSettingsOpen && menu.OpenModalCount == 1);

        yield return RealClick(menu.UiRoot?.Q<Button>(MainMenuNames.CreditsCloseButton),
            "制作信息关闭", log);
        yield return WaitFor(() => !menu.IsCreditsOpen, 5f);
        yield return WaitFor(() => menu.OpenModalCount == 0, 5f);
        log.Check("制作信息关闭 hides the credits modal",
            !menu.IsCreditsOpen && menu.OpenModalCount == 0 && !menu.IsSettingsOpen);

        // ---- 5. 进入任务 real click -> start cinematic -> first-person ship ----
        yield return RealClick(cta, "进入任务", log);

        MainMenuCinematic cinematic = menu.Cinematic;
        log.Check("进入任务 starts the cinematic instead of cutting scenes",
            cinematic != null && cinematic.IsPlaying
            && SceneManager.GetActiveScene().name == SceneRouting.MainMenuSceneName);

        // The shot must pass through menu fade -> engine boost -> departure ->
        // blackout (PASS 3), and only then load the ship.
        yield return WaitFor(() => cinematic == null
                                   || cinematic.CurrentPhase == MainMenuCinematic.Phase.ShipDeparture
                                   || cinematic.CurrentPhase == MainMenuCinematic.Phase.FadeToBlack, 4f);
        log.Check("cinematic fades the menu out (title/CTA/navigation)",
            cinematic == null || cinematic.MenuAlpha < 0.6f);
        log.Check("cinematic boosts the thrusters before departure",
            hero == null || hero.EngineTarget > 0.3f);
        log.Check("cinematic starts the ship departure",
            cinematic == null
            || cinematic.CurrentPhase == MainMenuCinematic.Phase.ShipDeparture
            || cinematic.CurrentPhase == MainMenuCinematic.Phase.FadeToBlack
            || hero.IsDeparting);

        // The phase only just started when the check above ran, so the travel
        // distance is asserted once the ship has really accelerated.
        yield return WaitFor(() => hero == null || !hero.IsDeparting
                                   || hero.DepartureProgress > 0.35f, 4f);
        log.Check("departing ship actually leaves its authored pose",
            hero == null || !hero.IsDeparting
            || Vector3.Distance(hero.transform.position, hero.SpawnPosition) > 1f);

        // 进入任务 now lands in the WALKABLE SHIP in every host (Editor and
        // player): the cover no longer routes into the 2D Hub.
        ShipGameController ship = null;
        yield return WaitFor(() =>
        {
            ship = FindFirstObjectByTypeHelper.Find<ShipGameController>();
            return ship != null && ship.IsReady;
        }, 30f);

        if (ship == null)
        {
            log.Fail("进入任务 never reached the walkable ship interior");
            Finish(log, 1);
            yield break;
        }

        log.Check("进入任务 lands in the ship interior (not the Hub/Overview)",
            SceneManager.GetActiveScene().name == GameSession.ShipInteriorSceneName
            && ship.Player != null && ship.Interactor != null
            && ship.Player.EyeCamera != null);
        log.Check("the landing spawn is the ship's default marker",
            Vector3.Distance(ship.Player.transform.position,
                new Vector3(0f, 0f, 3.6f)) < 1.5f);
        log.Check("the first-person entry plays the blink fade (PASS 4)",
            ship.EntryBlink != null && ship.EntryBlink.IsPlaying);
        log.Check("no terminal UI covers the frame on entry",
            ship.IsConsoleFocusActive == false && ship.IsOverlayOpen == false);
        log.Check("the player is in first person and controllable",
            ship.Player.EyeCamera.enabled && ship.Player.InputEnabled);

        // The blink must actually open the view again. A destroyed component
        // compares equal to null only after Unity retires it, so the wait keys on
        // the reference itself rather than on IsPlaying.
        yield return WaitFor(() => ship.EntryBlink == null, 6f);
        log.Check("the blink fade finishes and removes its overlay",
            ship.EntryBlink == null);
        Debug.Log("[StandaloneSmoke] step 5b: blink done, entering terminal checks");

        log.Check("the menu scene is gone after entering the mission",
            UnityEngine.Object.FindObjectsByType<MainMenuController>().Length == 0);

        // ---- 5b. Walk-in terminal flow: the ship's 总览 terminal hosts it ----
        Debug.Log("[StandaloneSmoke] step 5b: terminal flow");
        ShipConsoleInteractable overviewConsole = null;
        foreach (ShipConsoleInteractable candidate in ship.Consoles)
        {
            if (candidate.ConsoleId == 1)
            {
                overviewConsole = candidate;
                break;
            }
        }

        log.Check("the ship bridge carries the 总览 (Overview) terminal",
            overviewConsole != null
            && UnityEngine.Object.FindObjectsByType<ShipConsoleScreenHost>().Length >= 3);

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

            log.Check("all four terminals are wired (" + surfaces + " surfaces)",
                surfaces == 4);
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

        // ---- 6. existing Phase 01-05 state journey (scene independent) ----
        // The four-screen 2D Hub is no longer part of the product path, so the
        // journey runs in its state form here and the full UI journey stays with
        // the Hub's own probe (-bootHub automation).
        Debug.Log("[StandaloneSmoke] step 6: session + processing journey");
        RunSessionJourney(log, session);
        yield return RunProcessJourney(log, session, null);

        // ---- 7. Main Menu round trip: the session must be preserved ----
        Debug.Log("[StandaloneSmoke] step 7: main menu round trip");
        int mineralsBefore = session.Minerals;
        int alloyBefore = session.Alloy;
        int refineryBefore = session.RefineryLevel;
        SceneRouting.EnterMainMenu();
        MainMenuController menuAgain = null;
        yield return WaitFor(() =>
        {
            menuAgain = FindFirstObjectByTypeHelper.Find<MainMenuController>();
            return menuAgain != null && menuAgain.IsReady;
        }, 25f);

        log.Check("main menu is re-enterable after the mission entry",
            menuAgain != null
            && SceneManager.GetActiveScene().name == SceneRouting.MainMenuSceneName);
        log.Check("returning to the main menu never resets the session",
            GameSession.Instance == session
            && session.Minerals == mineralsBefore && session.Alloy == alloyBefore
            && session.RefineryLevel == refineryBefore);
        log.Check("still exactly one shared planet rig after re-entry",
            UnityEngine.Object.FindObjectsByType<OverviewPlanetPreview>().Length <= 1);
        log.Check("no duplicated persistent GameSession",
            UnityEngine.Object.FindObjectsByType<GameSession>().Length == 1);

        // ---- 8. 进入任务 again -> the same demo flow ----
        yield return RealClick(menuAgain?.EnterMissionButton, "进入任务(2)", log);
        ShipGameController secondShip = null;
        yield return WaitFor(() =>
        {
            secondShip = FindFirstObjectByTypeHelper.Find<ShipGameController>();
            return secondShip != null && secondShip.IsReady;
        }, 30f);
        log.Check("进入任务 works a second time", secondShip != null && secondShip.IsReady);
        log.Check("the cover is stable on a second entry (ship + backdrop rebuilt once)",
            UnityEngine.Object.FindObjectsByType<MainMenuShipHero>().Length == 0
            && UnityEngine.Object.FindObjectsByType<MainMenuStarfield>().Length == 0);

        // ---- 9. 退出 must terminate the application ----
        SceneRouting.EnterMainMenu();
        MainMenuController menuExit = null;
        yield return WaitFor(() =>
        {
            menuExit = FindFirstObjectByTypeHelper.Find<MainMenuController>();
            return menuExit != null && menuExit.IsReady;
        }, 25f);
        log.Check("main menu is ready for the exit smoke", menuExit != null);

        log.Line("EXIT: clicking 退出 (the process must terminate)");
        WriteResult(log, log.Failures == 0);
        yield return RealClick(menuExit?.UiRoot?.Q<Button>(MainMenuNames.ExitButton), "退出", log);

        // Still alive after ~1.5 s means the exit action is broken; the
        // watchdog must not be allowed to hide that.
        for (int i = 0; i < 90; i++)
        {
            yield return null;
        }

        log.Fail("退出 did not terminate the application");
        WriteResult(log, false);
        Application.Quit(1);
    }

    /// <summary>
    /// Horizontal viewport position (0 = left edge, 1 = right edge) of the hero
    /// ship's centre, measured through the stage camera. This is what proves the
    /// composition is "ship on the LEFT" instead of trusting a screenshot.
    /// </summary>
    private static float HeroScreenX(MainMenuShipHero hero)
    {
        Camera stageCamera = UnityEngine.Object.FindAnyObjectByType<Camera>();
        if (stageCamera == null || hero == null)
        {
            return 1f;
        }

        return stageCamera.WorldToViewportPoint(hero.WorldBounds.center).x;
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
