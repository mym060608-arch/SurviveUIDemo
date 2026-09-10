using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 08 Main Menu (REVISED composition) — runtime smoke + interaction
/// validation.
///
/// Every menu action is exercised through REAL UI Toolkit pointer events
/// (<see cref="UiToolkitPointerSimulator"/>), never by calling a private
/// shortcut: a green suite here means the button wiring itself works.
///
/// Covered contracts (revision):
/// - Boot -> Main Menu, LEFT 3D hero ship / RIGHT menu, deep-space backdrop,
/// - 系统设置 / 制作信息 open + close, no modal stacking, ESC closes,
/// - background clicks are blocked while a modal is open,
/// - 动态效果 / 界面提示 really change runtime behavior (ship + plume + stars),
/// - 进入任务 plays the cinematic (menu fade -> engine boost -> departure ->
///   blackout) and lands in the FIRST-PERSON ship interior, never in Overview,
/// - the ship's terminals still reach Overview / Preparation / Processing /
///   Outpost,
/// - the session survives the cover (opening the menu is not "new game"),
/// - no EventSystem / AudioListener is introduced anywhere.
///
/// Timing note: every animated wait is bounded in REAL TIME. A batch-mode
/// PlayMode editor runs at thousands of frames per second, so a frame budget
/// cannot express "wait for the 2.95 s start cinematic".
/// </summary>
public class SurvivePhase08Tests
{
    private const int MaxWaitFrames = 900;

    [SetUp]
    public void ResetPresentationSettings()
    {
        MainMenuSettings.ResetToDefaults();
    }

    /// <summary>Boot -> Main Menu composition (ship left / menu right / space).</summary>
    [UnityTest]
    public IEnumerator Boot_MainMenu_LeftShipRightMenu_Green()
    {
        MainMenuController menu = null;
        yield return BootToMainMenu(m => menu = m);
        Assert.IsNotNull(menu, "MainMenuController must be ready after Boot");

        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "boot must land on the Main Menu scene");
        Assert.AreEqual(1, Object.FindObjectsByType<MainMenuController>().Length,
            "exactly one menu controller");
        Assert.AreEqual(0, Object.FindObjectsByType<AppController>().Length,
            "the menu scene must not author a second UI application");

        // Structural contract.
        Assert.IsNotNull(menu.UiRoot.Q<VisualElement>(MainMenuNames.Root), "menu root exists");
        Assert.IsNotNull(menu.UiRoot.Q<VisualElement>(MainMenuNames.Layer), "menu layer exists");
        Assert.IsNotNull(menu.UiRoot.Q<Label>(MainMenuNames.Title), "brand title exists");
        Assert.IsNotNull(menu.UiRoot.Q<Label>(MainMenuNames.Subtitle), "Chinese subtitle exists");
        Assert.IsNotNull(menu.EnterMissionButton, "primary CTA exists");
        Assert.IsNotNull(menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton), "settings button exists");
        Assert.IsNotNull(menu.UiRoot.Q<Button>(MainMenuNames.CreditsButton), "credits button exists");
        Assert.IsNotNull(menu.UiRoot.Q<Button>(MainMenuNames.ExitButton), "exit button exists");
        Assert.IsNotNull(menu.UiRoot.Q<VisualElement>(MainMenuNames.SettingsModal), "settings modal exists");
        Assert.IsNotNull(menu.UiRoot.Q<VisualElement>(MainMenuNames.CreditsModal), "credits modal exists");

        // Chinese-first copy contract.
        Assert.AreEqual("SURVIVE // DRIFT", menu.UiRoot.Q<Label>(MainMenuNames.Title).text,
            "brand wordmark copy");
        Assert.AreEqual("边境生存协议", menu.UiRoot.Q<Label>(MainMenuNames.Subtitle).text,
            "subtitle copy");
        Assert.AreEqual("进入任务",
            menu.EnterMissionButton.Q<Label>(MainMenuNames.EnterMissionLabel).text, "CTA copy");
        Assert.AreEqual("系统设置",
            menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton)
                .Q<Label>(className: "menu-secondary-label").text, "settings copy");
        Assert.AreEqual("制作信息",
            menu.UiRoot.Q<Button>(MainMenuNames.CreditsButton)
                .Q<Label>(className: "menu-secondary-label").text, "credits copy");
        Assert.AreEqual("退出",
            menu.UiRoot.Q<Button>(MainMenuNames.ExitButton)
                .Q<Label>(className: "menu-secondary-label").text, "exit copy");

        // The CTA is really interactable and the dominant action on screen.
        Assert.IsTrue(menu.EnterMissionButton.enabledSelf, "CTA enabled");
        Assert.AreNotEqual(DisplayStyle.None, menu.EnterMissionButton.resolvedStyle.display,
            "CTA visible");
        Button settingsButton = menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton);
        Label ctaLabelElement = menu.EnterMissionButton.Q<Label>(MainMenuNames.EnterMissionLabel);
        Label rowLabelElement = settingsButton.Q<Label>(className: "menu-secondary-label");
        Assert.Greater(menu.EnterMissionButton.worldBound.height,
            settingsButton.worldBound.height * 1.5f, "CTA is the tallest action");
        Assert.Greater(menu.EnterMissionButton.resolvedStyle.backgroundColor.a, 0.9f,
            "CTA carries the primary fill treatment");
        Assert.Less(settingsButton.resolvedStyle.backgroundColor.a, 0.1f,
            "secondary rows stay unfilled");
        Assert.Greater(ctaLabelElement.resolvedStyle.fontSize,
            rowLabelElement.resolvedStyle.fontSize * 1.3f, "CTA label is visually dominant");

        // ---- The revision's core composition contract ----
        VisualElement heroZone = menu.UiRoot.Q<VisualElement>(MainMenuNames.HeroColumn);
        VisualElement menuColumn = menu.UiRoot.Q<VisualElement>(MainMenuNames.ContentColumn);
        Rect rootRect = menu.UiRoot.worldBound;
        Assert.IsNotNull(heroZone, "hero column exists");
        Assert.IsNotNull(menuColumn, "menu column exists");
        Assert.LessOrEqual(heroZone.worldBound.xMax, menuColumn.worldBound.xMin + 1f,
            "the hero zone is strictly LEFT of the menu column");
        Assert.Greater(menuColumn.worldBound.xMin, rootRect.width * 0.55f,
            "the menu column lives in the right part of the frame");
        Assert.Greater(menuColumn.worldBound.width, rootRect.width * 0.25f,
            "the menu column is wide enough to read");
        Assert.LessOrEqual(menuColumn.worldBound.xMax, rootRect.xMax + 1f,
            "the menu column stays inside the frame");

        // The cover really paints almost nothing on the left: the 3D stage shows
        // through instead of a UI panel covering it.
        Assert.Less(menu.UiRoot.Q<VisualElement>(MainMenuNames.Root)
                .resolvedStyle.backgroundColor.a, 0.05f,
            "the menu root stays transparent so the 3D stage shows");
        Assert.Less(heroZone.resolvedStyle.backgroundColor.a, 0.05f,
            "the hero zone stays transparent (window onto the 3D ship)");

        // ---- The hero is the real asset, not a picture ----
        MainMenuShipHero hero = Object.FindAnyObjectByType<MainMenuShipHero>();
        Assert.IsNotNull(hero, "hero ship component exists");
        Assert.IsTrue(hero.IsShipPresent, "the ship mesh hierarchy is present");
        Assert.GreaterOrEqual(hero.ShipRendererCount, 40,
            "the whole authored ship is instantiated (" + hero.ShipRendererCount + " renderers)");
        Assert.AreEqual(2, hero.NozzleCount, "both authored exhaust anchors drive a plume");
        Assert.IsTrue(menu.IsHeroShipActive, "the menu reports the 3D hero as active");

        // The ship must sit in the LEFT half of the frame, left of the menu
        // column. The composition is authored for the 16:9 panel reference, so
        // the projection is pinned to that aspect: a batch-mode Game View can be
        // square, which would squeeze the projection and make this meaningless.
        Camera stageCamera = Object.FindAnyObjectByType<Camera>();
        Assert.IsNotNull(stageCamera, "stage camera exists");
        stageCamera.aspect = 16f / 9f;
        float shipX = stageCamera.WorldToViewportPoint(hero.WorldBounds.center).x;
        float menuStart = menuColumn.worldBound.xMin / Mathf.Max(1f, rootRect.xMax);
        Assert.Less(shipX, menuStart,
            "the hero ship is LEFT of the menu column (ship x=" + shipX.ToString("F3")
            + ", menu starts at " + menuStart.ToString("F3")
            + ", authored aspect=" + stageCamera.aspect.ToString("F3")
            + ", pixel=" + stageCamera.pixelWidth + "x" + stageCamera.pixelHeight
            + ", camPos=" + stageCamera.transform.position.ToString("F2")
            + ", shipCenter=" + hero.WorldBounds.center.ToString("F2") + ")");
        Assert.Less(shipX, 0.62f,
            "the ship centre is in the left half at the authored 16:9 aspect (x="
            + shipX.ToString("F3") + ")");
        Assert.Greater(shipX, 0.02f,
            "the ship is on screen (x=" + shipX.ToString("F3") + ")");
        stageCamera.ResetAspect();

        // Deep-space backdrop behind it.
        MainMenuStarfield stars = Object.FindAnyObjectByType<MainMenuStarfield>();
        Assert.IsNotNull(stars, "starfield component exists");
        Assert.IsTrue(stars.IsActive, "the starfield is built");
        Assert.AreEqual(3, stars.LayerCount, "three parallax layers");
        Assert.Greater(stars.StarCount, 1000, "the sky carries a real star count");

        // The cinematic is authored and idle before 进入任务.
        Assert.IsNotNull(menu.Cinematic, "start cinematic exists");
        Assert.IsFalse(menu.Cinematic.IsPlaying, "the cinematic is idle on the cover");
        Assert.AreEqual(MainMenuCinematic.Phase.Idle, menu.Cinematic.CurrentPhase,
            "cinematic starts in Idle");

        AssertNoEventSystemOrAudioListener("menu scene");

        // Opening the cover must not have touched the session.
        GameSession session = GameSession.Instance;
        Assert.IsNotNull(session, "authoritative session exists");
        Assert.AreEqual(1, Object.FindObjectsByType<GameSession>().Length, "exactly one session");
    }

    /// <summary>Real clicks: modals, blocking, ESC, then 进入任务 -> ship interior.</summary>
    [UnityTest]
    public IEnumerator MainMenu_RealClicks_ModalsAndShipEntry_Green()
    {
        MainMenuController menu = null;
        yield return BootToMainMenu(m => menu = m);
        Assert.IsNotNull(menu, "MainMenuController must be ready");

        GameSession session = GameSession.Instance;
        session.ResetSession();
        session.AddMinerals(7);
        Assert.AreEqual(27, session.Minerals, "session prepared with 27 minerals");

        VisualElement settings = menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton);
        VisualElement credits = menu.UiRoot.Q<Button>(MainMenuNames.CreditsButton);

        // ---- 系统设置: open, block the background, close ----
        yield return RealClick(settings, "系统设置");
        yield return WaitUntil(() => menu.IsSettingsOpen, 60);
        Assert.IsTrue(menu.IsSettingsOpen, "系统设置 opens the settings modal");
        Assert.AreEqual(1, menu.OpenModalCount, "exactly one modal is shown");
        Assert.AreEqual(DisplayStyle.Flex,
            menu.UiRoot.Q<VisualElement>(MainMenuNames.SettingsModal).resolvedStyle.display,
            "settings modal is displayed");

        Assert.IsTrue(UiToolkitPointerSimulator.Click(menu.EnterMissionButton),
            "background click dispatched");
        yield return null;
        yield return null;
        Assert.IsFalse(menu.IsTransitioning, "background CTA click is blocked by the modal");
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "no navigation happened behind the modal");
        Assert.IsFalse(menu.Cinematic.IsPlaying, "the cinematic never starts behind a modal");

        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.SettingsCloseButton), "设置关闭");
        yield return WaitUntil(() => !menu.IsSettingsOpen, 60);
        Assert.IsFalse(menu.IsSettingsOpen, "设置关闭 closes the modal");
        yield return WaitUntilReal(() => menu.OpenModalCount == 0, 6f);
        Assert.AreEqual(0, menu.OpenModalCount, "no modal remains open");

        // ---- ESC closes a modal and never quits ----
        yield return RealClick(settings, "系统设置 (ESC path)");
        yield return WaitUntil(() => menu.IsSettingsOpen, 60);
        menu.HandleEscapeKey();
        yield return WaitUntil(() => !menu.IsSettingsOpen, 60);
        Assert.IsFalse(menu.IsSettingsOpen, "ESC closes the open modal");
        yield return WaitUntilReal(() => menu.OpenModalCount == 0, 6f);
        Assert.AreEqual(0, menu.OpenModalCount, "ESC leaves no modal open");
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "ESC never leaves the main menu (and never exits)");

        // ---- 制作信息: real click, no stacking ----
        yield return RealClick(settings, "系统设置 (stacking check)");
        yield return WaitUntil(() => menu.IsSettingsOpen, 60);
        Assert.IsTrue(UiToolkitPointerSimulator.Click(credits), "blocked credits click dispatched");
        yield return null;
        yield return null;
        Assert.IsFalse(menu.IsCreditsOpen, "制作信息 behind the modal is blocked");
        Assert.IsTrue(menu.IsSettingsOpen, "settings stays open after the blocked click");

        menu.ShowCredits();
        yield return WaitUntil(() => menu.IsCreditsOpen, 60);
        Assert.IsTrue(menu.IsCreditsOpen, "制作信息 opens");
        Assert.IsFalse(menu.IsSettingsOpen, "opening credits closes settings (no stacking)");
        // The previous modal keeps fading for a few frames; the "exactly one modal
        // shown" invariant is reached once that fade is over.
        yield return WaitUntilReal(() => menu.OpenModalCount == 1, 6f);
        Assert.AreEqual(1, menu.OpenModalCount, "modal stacking is impossible");

        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.CreditsCloseButton),
            "制作信息关闭");
        yield return WaitUntil(() => !menu.IsCreditsOpen, 60);
        yield return WaitUntilReal(() => menu.OpenModalCount == 0, 6f);
        Assert.AreEqual(0, menu.OpenModalCount, "no modal remains open before the mission");

        // ---- 进入任务 -> cinematic -> first-person ship interior ----
        MainMenuCinematic cinematic = menu.Cinematic;
        MainMenuShipHero hero = Object.FindAnyObjectByType<MainMenuShipHero>();
        Assert.IsNotNull(cinematic, "cinematic available");
        Assert.IsNotNull(hero, "hero available");

        yield return RealClick(menu.EnterMissionButton, "进入任务");
        Assert.IsTrue(cinematic.IsPlaying, "进入任务 starts the cinematic");
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "the cinematic plays inside the menu scene (no hard cut)");

        // Menu fade first: the ship and the starfield stay.
        yield return WaitUntilReal(() => cinematic.MenuAlpha < 0.5f, 6f);
        Assert.Less(cinematic.MenuAlpha, 0.5f, "the menu layer fades out first");
        Assert.IsTrue(hero.IsShipPresent, "the ship stays visible while the menu fades");

        // Engine boost before the ship leaves.
        yield return WaitUntilReal(
            () => cinematic.CurrentPhase == MainMenuCinematic.Phase.EngineBoost
                  || cinematic.CurrentPhase == MainMenuCinematic.Phase.ShipDeparture
                  || cinematic.CurrentPhase == MainMenuCinematic.Phase.FadeToBlack, 6f);
        Assert.Greater(hero.EngineTarget, 0.3f, "the thrusters are boosted before departure");

        // Departure: the ship really moves out of its authored pose.
        yield return WaitUntilReal(
            () => cinematic.CurrentPhase == MainMenuCinematic.Phase.ShipDeparture
                  || cinematic.CurrentPhase == MainMenuCinematic.Phase.FadeToBlack, 6f);
        Assert.IsTrue(hero.IsDeparting, "the ship is departing");
        yield return WaitUntilReal(() => hero.DepartureProgress > 0.35f, 6f);
        Assert.Greater(Vector3.Distance(hero.transform.position, hero.SpawnPosition), 1f,
            "the ship actually left its idle pose");
        Assert.Greater(hero.EngineLevel, 0.5f, "the plumes are at boost while it accelerates");

        // Blackout, then the ship interior.
        yield return WaitUntilReal(
            () => cinematic.BlackAlpha > 0.5f
                  || SceneManager.GetActiveScene().name != SceneRouting.MainMenuSceneName, 8f);

        ShipGameController ship = null;
        yield return WaitUntilReal(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady;
        }, 30f);
        Assert.IsNotNull(ship, "进入任务 lands in the walkable ship interior");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, SceneManager.GetActiveScene().name,
            "the landing scene is 03_ShipInterior");
        Assert.AreEqual(0, Object.FindObjectsByType<MainMenuController>().Length,
            "the menu scene is unloaded");
        Assert.AreEqual(0, Object.FindObjectsByType<AppController>().Length,
            "进入任务 does NOT land in the Overview/Hub UI application");

        // First person + blink fade-in (PASS 4).
        Assert.IsNotNull(ship.Player, "the player exists");
        Assert.IsNotNull(ship.Player.EyeCamera, "the first-person eye camera exists");
        Assert.IsTrue(ship.Player.EyeCamera.enabled, "the first-person camera renders");
        Assert.IsTrue(ship.Player.InputEnabled, "the player is controllable");
        Assert.IsTrue(ship.EntryBlinkArmed,
            "the menu hand-over armed the first-person blink fade");
        Assert.IsTrue(ship.EntryBlink == null || ship.EntryBlink.IsPlaying,
            "the entry blink is playing (or already finished cleanly)");
        Assert.IsFalse(ship.IsConsoleFocusActive, "no terminal UI covers the frame on entry");
        Assert.IsFalse(ship.IsOverlayOpen, "no shipboard modal covers the frame on entry");

        // The eyelid curve is a blink, not a linear fade.
        Assert.AreEqual(1f, ShipEntryBlinkFade.AlphaAt(0f), 0.001f, "eyes start shut");
        Assert.Greater(ShipEntryBlinkFade.AlphaAt(0.1f), 0.9f, "still shut at 0.1 s");
        Assert.Less(ShipEntryBlinkFade.AlphaAt(0.3f), 0.6f, "first crack open");
        Assert.Greater(ShipEntryBlinkFade.AlphaAt(0.4f), ShipEntryBlinkFade.AlphaAt(0.3f),
            "reflexive re-close");
        Assert.AreEqual(0f, ShipEntryBlinkFade.AlphaAt(ShipEntryBlinkFade.BlinkSeconds + 0.1f),
            0.001f, "the blink ends fully open");

        yield return WaitUntilReal(() => ship.EntryBlink == null, 8f);
        // NUnit's IsNull does a reference comparison, which cannot see Unity's
        // destroyed-object null: the Unity operator is the correct check here.
        Assert.IsTrue(ship.EntryBlink == null,
            "the blink removes its overlay when it finishes");

        // Spawn is the ship's default marker, in front of the bridge terminals.
        Assert.Less(Vector3.Distance(ship.Player.transform.position,
            new Vector3(0f, 0f, 3.6f)), 1.5f, "the player spawns on the ship's default marker");

        // ---- Terminal flow: the ship still reaches Overview et al. ----
        ShipConsoleInteractable overviewConsole = null;
        foreach (ShipConsoleInteractable console in ship.Consoles)
        {
            if (console.ConsoleId == 1)
            {
                overviewConsole = console;
                break;
            }
        }

        Assert.IsNotNull(overviewConsole, "the 总览 terminal exists on the bridge");
        Assert.IsTrue(ship.Focus.RequestEnter(overviewConsole),
            "the player can enter the 总览 terminal");
        yield return WaitUntilReal(() => ship.Focus.FocusActive, 8f);
        Assert.IsTrue(ship.Focus.FocusActive, "the terminal focus is active");

        // Console 02 (仓库总览) hosts the dedicated warehouse / 出发准备 tree
        // instead of a ShipConsoleScreenHost, so the mapping is read from the
        // console surfaces rather than from one component type.
        ShipConsoleScreenHost overviewHost = null;
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
                if (surface.ScreenHost.MappedScreen == AppScreen.Overview)
                {
                    overviewHost = surface.ScreenHost;
                }
            }

            if (surface.WarehouseHost != null)
            {
                available.Add(AppScreen.Preparation);
            }
        }

        Assert.AreEqual(4, surfaces, "all four ship terminals are wired");
        Assert.IsNotNull(overviewHost, "a terminal hosts the Overview screen");
        Assert.IsTrue(overviewHost.IsBound, "the Overview terminal bound its UI");
        Assert.AreEqual(1, overviewHost.CountVisibleScreens(),
            "exactly one production screen is visible in the terminal");
        Assert.IsTrue(available.Contains(AppScreen.Overview), "Overview reachable");
        Assert.IsTrue(available.Contains(AppScreen.Preparation), "Preparation reachable");
        Assert.IsTrue(available.Contains(AppScreen.Processing), "Processing reachable");
        Assert.IsTrue(available.Contains(AppScreen.Outpost), "Outpost reachable");

        ship.Focus.RequestExit();
        yield return WaitUntilReal(() => !ship.Focus.FocusActive, 8f);
        Assert.IsFalse(ship.Focus.FocusActive, "leaving the terminal returns to first person");
        Assert.IsTrue(ship.Player.InputEnabled, "the player can move again");

        // The session survived the whole hand-over.
        Assert.AreSame(session, GameSession.Instance, "the same session survives the cover");
        Assert.AreEqual(27, session.Minerals, "进入任务 never resets the session");
    }

    /// <summary>动态效果 / 界面提示 must really change runtime behavior.</summary>
    [UnityTest]
    public IEnumerator MainMenu_Settings_ReallyChangeBehavior_Green()
    {
        MainMenuController menu = null;
        yield return BootToMainMenu(m => menu = m);
        Assert.IsNotNull(menu, "MainMenuController must be ready");

        MainMenuShipHero hero = Object.FindAnyObjectByType<MainMenuShipHero>();
        Assert.IsNotNull(hero, "hero ship exists");

        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton), "系统设置");
        yield return WaitUntil(() => menu.IsSettingsOpen, 60);

        VisualElement title = menu.UiRoot.Q<Label>(MainMenuNames.Title);
        Rect titleBefore = title.worldBound;
        Rect ctaBefore = menu.EnterMissionButton.worldBound;

        // ---- 动态效果 off: the hero and its plumes freeze ----
        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.MotionOffButton), "动态效果关闭");
        yield return null;
        Assert.IsFalse(MainMenuSettings.MotionEnabled, "motion setting is off");
        Assert.IsTrue(menu.UiRoot.Q<Button>(MainMenuNames.MotionOffButton)
            .ClassListContains("segment-active"), "关闭 segment shows the active state");

        Vector3 frozenPosition = hero.transform.position;
        float frozenPlume = hero.Flames[0].PlumeLength;
        // Real time, not frames: a batch-mode editor runs at thousands of fps.
        yield return new WaitForSecondsRealtime(0.6f);

        Assert.AreEqual(frozenPosition.x, hero.transform.position.x, 0.0001f,
            "motion off stops the hero drift");
        Assert.AreEqual(frozenPosition.y, hero.transform.position.y, 0.0001f,
            "motion off stops the hero bob");
        Assert.AreEqual(frozenPlume, hero.Flames[0].PlumeLength, 0.0001f,
            "motion off stops the plume flicker");
        Assert.AreEqual(2, hero.NozzleCount, "the plumes stay on screen when motion is off");

        // ---- 动态效果 on: motion resumes ----
        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.MotionOnButton), "动态效果开启");
        yield return null;
        Assert.IsTrue(MainMenuSettings.MotionEnabled, "motion setting is back on");

        float resumePlume = hero.Flames[0].PlumeLength;
        float moved = 0f;
        float deadline = Time.realtimeSinceStartup + 0.8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            moved = Mathf.Max(moved, Mathf.Abs(hero.Flames[0].PlumeLength - resumePlume));
            yield return null;
        }

        Assert.Greater(moved, 0.0005f, "motion on really resumes the plume flicker");

        // ---- 界面提示 hidden: the annotation layer goes away, layout does not ----
        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.HintsHideButton), "界面提示隐藏");
        yield return null;
        Assert.IsFalse(MainMenuSettings.HintsVisible, "hints setting is off");
        Assert.AreEqual(DisplayStyle.None,
            menu.UiRoot.Q<VisualElement>(MainMenuNames.HintLayer).resolvedStyle.display,
            "annotation layer hidden");
        AssertRectUnchanged(titleBefore, title.worldBound, "title");
        AssertRectUnchanged(ctaBefore, menu.EnterMissionButton.worldBound, "CTA");

        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.HintsShowButton), "界面提示显示");
        yield return null;
        Assert.IsTrue(MainMenuSettings.HintsVisible, "hints setting is back on");
        Assert.AreEqual(DisplayStyle.Flex,
            menu.UiRoot.Q<VisualElement>(MainMenuNames.HintLayer).resolvedStyle.display,
            "annotation layer visible again");

        MainMenuSettings.ResetToDefaults();
    }

    /// <summary>Main Menu &lt;-&gt; Ship round trip: one session, no duplicates.</summary>
    [UnityTest]
    public IEnumerator MainMenu_ShipInterior_RoundTrip_NoDuplicates_Green()
    {
        MainMenuController menu = null;
        yield return BootToMainMenu(m => menu = m);
        Assert.IsNotNull(menu, "MainMenuController must be ready");

        GameSession session = GameSession.Instance;
        session.ResetSession();
        session.AddMinerals(9);

        Assert.AreEqual(1, Object.FindObjectsByType<MainMenuShipHero>().Length,
            "exactly one hero ship in the cover");
        Assert.AreEqual(1, Object.FindObjectsByType<MainMenuStarfield>().Length,
            "exactly one starfield in the cover");

        yield return RealClick(menu.EnterMissionButton, "进入任务 (round trip out)");
        ShipGameController ship = null;
        yield return WaitUntilReal(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady;
        }, 30f);
        Assert.IsNotNull(ship, "ship interior reached");

        SceneRouting.EnterMainMenu();
        MainMenuController back = null;
        yield return WaitUntilReal(() =>
        {
            back = Object.FindAnyObjectByType<MainMenuController>();
            return back != null && back.IsReady;
        }, 30f);
        Assert.IsNotNull(back, "main menu re-enterable");
        Assert.AreEqual(1, Object.FindObjectsByType<MainMenuController>().Length,
            "no duplicated menu controller");
        Assert.AreEqual(1, Object.FindObjectsByType<GameSession>().Length,
            "no duplicated persistent session");
        Assert.AreEqual(1, Object.FindObjectsByType<MainMenuShipHero>().Length,
            "no duplicated hero ship after re-entry");
        Assert.AreEqual(1, Object.FindObjectsByType<MainMenuStarfield>().Length,
            "no duplicated starfield after re-entry");
        Assert.AreEqual(0, Object.FindObjectsByType<ShipGameController>().Length,
            "the ship scene is unloaded");
        Assert.AreSame(session, GameSession.Instance, "same session instance");
        Assert.AreEqual(29, session.Minerals, "returning to the menu never resets the session");
        Assert.IsTrue(back.IsHeroShipActive, "the 3D hero is active again after re-entry");
        Assert.IsTrue(back.IsStarfieldActive, "the backdrop is active again after re-entry");

        yield return RealClick(back.EnterMissionButton, "进入任务 (round trip back)");
        ShipGameController again = null;
        yield return WaitUntilReal(() =>
        {
            again = Object.FindAnyObjectByType<ShipGameController>();
            return again != null && again.IsReady;
        }, 30f);
        Assert.IsNotNull(again, "ship interior reached again");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, SceneManager.GetActiveScene().name,
            "the second entry lands in the ship as well");
        Assert.AreEqual(29, GameSession.Instance.Minerals,
            "session value preserved through the whole round trip");
    }

    // ---- helpers ----

    private static IEnumerator BootToMainMenu(System.Action<MainMenuController> capture)
    {
        GameSession.Ensure().ResetSession();
        SceneManager.LoadScene("00_Boot");
        yield return WaitUntil(() => SceneManager.GetActiveScene().name
                                    == SceneRouting.MainMenuSceneName);

        MainMenuController menu = null;
        yield return WaitUntil(() =>
        {
            menu = Object.FindAnyObjectByType<MainMenuController>();
            return menu != null && menu.IsReady;
        });
        capture(menu);

        // Two extra frames so the first layout pass is fully resolved.
        yield return null;
        yield return null;
    }

    private static IEnumerator RealClick(VisualElement element, string label)
    {
        Assert.IsNotNull(element, "clickable element exists: " + label);
        // Let the panel lay the element out: a click uses the element centre.
        yield return null;
        Assert.IsTrue(element.worldBound.width > 1f && element.worldBound.height > 1f,
            "element is laid out before the click: " + label);
        Assert.IsTrue(UiToolkitPointerSimulator.Click(element), "click dispatched: " + label);
        yield return null;
        yield return null;
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = MaxWaitFrames)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }

    /// <summary>
    /// Real-time bounded wait. A frame budget cannot express "wait for the 2.95 s
    /// start cinematic": a batch-mode editor runs at thousands of frames per
    /// second, so 900 frames can be well under a second of wall time.
    /// </summary>
    private static IEnumerator WaitUntilReal(System.Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    /// <summary>Asserts a layout rect did not move (sub-pixel tolerant).</summary>
    private static void AssertRectUnchanged(Rect before, Rect after, string label)
    {
        const float tolerance = 0.5f;
        Assert.AreEqual(before.xMin, after.xMin, tolerance, label + " x unchanged");
        Assert.AreEqual(before.yMin, after.yMin, tolerance, label + " y unchanged");
        Assert.AreEqual(before.width, after.width, tolerance, label + " width unchanged");
        Assert.AreEqual(before.height, after.height, tolerance, label + " height unchanged");
    }

    /// <summary>No EventSystem / AudioListener anywhere in the active scene.</summary>
    private static void AssertNoEventSystemOrAudioListener(string context)
    {
        int eventSystems = 0;
        int audioListeners = 0;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }

                string typeName = component.GetType().Name;
                if (typeName == "EventSystem")
                {
                    eventSystems++;
                }
                else if (typeName == "AudioListener")
                {
                    audioListeners++;
                }
            }
        }

        Assert.AreEqual(0, eventSystems, "no EventSystem in the " + context);
        Assert.AreEqual(0, audioListeners, "no AudioListener in the " + context);
    }
}
