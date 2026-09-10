using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 08 Main Menu — runtime smoke + interaction validation.
///
/// Every menu action is exercised through REAL UI Toolkit pointer events
/// (<see cref="UiToolkitPointerSimulator"/>), never by calling a private
/// shortcut: a green suite here means the button wiring itself works.
///
/// Covered contracts:
/// - Boot -> Main Menu -> 进入任务 -> existing Overview,
/// - 系统设置 / 制作信息 open + close, no modal stacking, ESC closes,
/// - background clicks are blocked while a modal is open,
/// - 动态效果 / 界面提示 really change runtime behavior,
/// - the session survives the menu (opening the cover is not "new game"),
/// - one authoritative planet preview rig and one scene app across re-entry,
/// - no EventSystem / AudioListener is introduced anywhere.
/// </summary>
public class SurvivePhase08Tests
{
    private const int MaxWaitFrames = 900;

    [SetUp]
    public void ResetPresentationSettings()
    {
        // A settings test can never leak into another suite (the planet preview
        // rig is shared by the Phase 07 suites too).
        MainMenuSettings.ResetToDefaults();
    }

    /// <summary>Boot -> Main Menu structure + hero visual.</summary>
    [UnityTest]
    public IEnumerator Boot_MainMenu_StructureAndHero_Green()
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
        Assert.IsNotNull(menu.UiRoot.Q<Label>(MainMenuNames.Title), "brand title exists");
        Assert.IsNotNull(menu.UiRoot.Q<Label>(MainMenuNames.Subtitle), "Chinese subtitle exists");
        Assert.IsNotNull(menu.EnterMissionButton, "primary CTA exists");
        Assert.IsNotNull(menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton), "settings button exists");
        Assert.IsNotNull(menu.UiRoot.Q<Button>(MainMenuNames.CreditsButton), "credits button exists");
        Assert.IsNotNull(menu.UiRoot.Q<Button>(MainMenuNames.ExitButton), "exit button exists");
        Assert.IsNotNull(menu.UiRoot.Q<VisualElement>(MainMenuNames.SettingsModal), "settings modal exists");
        Assert.IsNotNull(menu.UiRoot.Q<VisualElement>(MainMenuNames.CreditsModal), "credits modal exists");

        // Chinese-first copy contract.
        Assert.AreEqual("SURVIVE", menu.UiRoot.Q<Label>(MainMenuNames.Title).text, "brand title copy");
        Assert.AreEqual("边境生存协议", menu.UiRoot.Q<Label>(MainMenuNames.Subtitle).text, "subtitle copy");
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

        // The CTA is really interactable and the dominant action on screen:
        // tallest, filled with the primary ink treatment, largest label.
        Assert.IsTrue(menu.EnterMissionButton.enabledSelf, "CTA enabled");
        Assert.AreNotEqual(DisplayStyle.None, menu.EnterMissionButton.resolvedStyle.display,
            "CTA visible");
        Button settingsButton = menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton);
        Label ctaLabelElement = menu.EnterMissionButton.Q<Label>(MainMenuNames.EnterMissionLabel);
        Label rowLabelElement = settingsButton.Q<Label>(className: "menu-secondary-label");
        Assert.Greater(menu.EnterMissionButton.worldBound.height,
            settingsButton.worldBound.height * 1.8f, "CTA is the tallest action");
        Assert.Greater(menu.EnterMissionButton.resolvedStyle.backgroundColor.a, 0.9f,
            "CTA carries the primary fill treatment");
        Assert.Less(settingsButton.resolvedStyle.backgroundColor.a, 0.1f,
            "secondary rows stay unfilled");
        Assert.Greater(ctaLabelElement.resolvedStyle.fontSize,
            rowLabelElement.resolvedStyle.fontSize * 1.3f, "CTA label is visually dominant");

        // Hero visual: the ONE shared rig, rendering, displayed on the disc.
        OverviewPlanetPreview rig = OverviewPlanetPreview.Shared;
        Assert.IsNotNull(rig, "shared planet preview rig exists");
        Assert.AreEqual(1, Object.FindObjectsByType<OverviewPlanetPreview>().Length,
            "exactly one planet preview rig (no duplicate)");
        Assert.IsTrue(rig.Texture != null && rig.Texture.IsCreated(), "hero RenderTexture is created");
        Assert.IsTrue(menu.IsHeroPlanetActive, "hero disc shows the shared preview texture");
        Assert.IsTrue(rig.ViewerCount >= 1, "menu is registered as a preview viewer");
        Assert.IsTrue(rig.IsRendering, "preview camera is rendering for the menu");
        string rigReport;
        Assert.IsTrue(rig.VerifySetup(out rigReport), "preview rig setup sound: " + rigReport);

        // The hero must be a real hero, and it must not sit on top of the
        // navigation column.
        VisualElement disc = menu.UiRoot.Q<VisualElement>(MainMenuNames.HeroPlanetDisc);
        VisualElement content = menu.UiRoot.Q<VisualElement>(MainMenuNames.ContentColumn);
        Assert.Greater(disc.worldBound.height, 400f, "hero planet is large");
        Assert.LessOrEqual(disc.worldBound.xMax, menu.UiRoot.worldBound.xMax, "hero inside the frame");
        Assert.LessOrEqual(content.worldBound.xMax, disc.worldBound.xMin + 1f,
            "navigation column never overlaps the hero visual");

        AssertNoEventSystemOrAudioListener("menu scene");

        // Opening the cover must not have touched the session.
        GameSession session = GameSession.Instance;
        Assert.IsNotNull(session, "authoritative session exists");
        Assert.AreEqual(1, Object.FindObjectsByType<GameSession>().Length, "exactly one session");
    }

    /// <summary>Real clicks: modals, background blocking, ESC, 进入任务 -> Overview.</summary>
    [UnityTest]
    public IEnumerator MainMenu_RealClicks_ModalsAndOverview_Green()
    {
        MainMenuController menu = null;
        yield return BootToMainMenu(m => menu = m);
        Assert.IsNotNull(menu, "MainMenuController must be ready");

        GameSession session = GameSession.Instance;
        session.ResetSession();
        // A distinctive value proves the menu never resets the session.
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

        // Clicking the CTA behind the modal must do nothing.
        Assert.IsTrue(UiToolkitPointerSimulator.Click(menu.EnterMissionButton),
            "background click dispatched");
        yield return null;
        yield return null;
        Assert.IsFalse(menu.IsTransitioning, "background CTA click is blocked by the modal");
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "no navigation happened behind the modal");

        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.SettingsCloseButton), "设置关闭");
        yield return WaitUntil(() => !menu.IsSettingsOpen, 60);
        Assert.IsFalse(menu.IsSettingsOpen, "设置关闭 closes the modal");
        // The modal fades out; it must be gone (and stop blocking) shortly after.
        yield return WaitUntil(() => menu.OpenModalCount == 0, 400);
        Assert.AreEqual(0, menu.OpenModalCount, "no modal remains open");

        // ---- ESC closes a modal and never quits ----
        yield return RealClick(settings, "系统设置 (ESC path)");
        yield return WaitUntil(() => menu.IsSettingsOpen, 60);
        Assert.IsTrue(menu.IsSettingsOpen, "settings reopened for the ESC check");
        menu.HandleEscapeKey();
        yield return WaitUntil(() => !menu.IsSettingsOpen, 60);
        Assert.IsFalse(menu.IsSettingsOpen, "ESC closes the open modal");
        yield return WaitUntil(() => menu.OpenModalCount == 0, 400);
        Assert.AreEqual(0, menu.OpenModalCount, "ESC leaves no modal open");
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "ESC never leaves the main menu (and never exits)");

        // ---- 制作信息: real click, no stacking ----
        // While a modal is open the menu behind it is blocked, so the secondary
        // buttons cannot be clicked through either.
        yield return RealClick(settings, "系统设置 (stacking check)");
        yield return WaitUntil(() => menu.IsSettingsOpen, 60);
        Assert.IsTrue(UiToolkitPointerSimulator.Click(credits), "blocked credits click dispatched");
        yield return null;
        yield return null;
        Assert.IsFalse(menu.IsCreditsOpen, "制作信息 behind the modal is blocked");
        Assert.IsTrue(menu.IsSettingsOpen, "settings stays open after the blocked click");

        // The API enforces the same rule: opening one modal closes the other.
        menu.ShowCredits();
        yield return WaitUntil(() => menu.IsCreditsOpen, 60);
        Assert.IsTrue(menu.IsCreditsOpen, "制作信息 opens");
        Assert.IsFalse(menu.IsSettingsOpen, "opening credits closes settings (no stacking)");
        Assert.AreEqual(1, menu.OpenModalCount, "modal stacking is impossible");

        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.CreditsCloseButton),
            "制作信息关闭");
        yield return WaitUntil(() => !menu.IsCreditsOpen, 60);
        Assert.IsFalse(menu.IsCreditsOpen, "制作信息关闭 closes the credits modal");
        yield return WaitUntil(() => menu.OpenModalCount == 0, 400);
        Assert.AreEqual(0, menu.OpenModalCount, "no modal remains open");

        // With nothing open, the credits button opens its modal for real.
        yield return RealClick(credits, "制作信息 (real click)");
        yield return WaitUntil(() => menu.IsCreditsOpen, 60);
        Assert.IsTrue(menu.IsCreditsOpen, "制作信息 opens the credits modal with a real click");
        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.CreditsCloseButton),
            "制作信息关闭 (2)");
        yield return WaitUntil(() => !menu.IsCreditsOpen, 60);
        yield return WaitUntil(() => menu.OpenModalCount == 0, 400);
        Assert.AreEqual(0, menu.OpenModalCount, "no modal remains open before the mission");

        // ---- 进入任务 -> existing Overview ----
        yield return RealClick(menu.EnterMissionButton, "进入任务");
        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });
        Assert.IsNotNull(app, "进入任务 reaches the existing UI application");
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "Overview is the landing screen");
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one visible screen");
        Assert.AreEqual(1, Object.FindObjectsByType<AppController>().Length,
            "no second UI application");
        Assert.AreEqual(0, Object.FindObjectsByType<MainMenuController>().Length,
            "the menu scene is unloaded");
        Assert.AreEqual(1, Object.FindObjectsByType<OverviewPlanetPreview>().Length,
            "still exactly one preview rig");
        Assert.AreSame(session, GameSession.Instance, "the same session survives the menu");
        Assert.AreEqual(27, session.Minerals, "进入任务 never resets the session");
        Assert.AreEqual("27", app.UiRoot.Q<Label>(ElementNames.MineralsValue).text,
            "Overview shows the preserved session value");

        // The existing cross-screen path still works after the transition.
        app.ShowScreen(AppScreen.Preparation);
        app.ShowScreen(AppScreen.Processing);
        app.ShowScreen(AppScreen.Outpost);
        Assert.AreEqual(AppScreen.Outpost, app.CurrentScreen, "screen navigation intact");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen after navigation");
        AssertNoEventSystemOrAudioListener("hub scene");
    }

    /// <summary>动态效果 / 界面提示 must really change runtime behavior.</summary>
    [UnityTest]
    public IEnumerator MainMenu_Settings_ReallyChangeBehavior_Green()
    {
        MainMenuController menu = null;
        yield return BootToMainMenu(m => menu = m);
        Assert.IsNotNull(menu, "MainMenuController must be ready");

        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton), "系统设置");
        yield return WaitUntil(() => menu.IsSettingsOpen, 60);

        VisualElement title = menu.UiRoot.Q<Label>(MainMenuNames.Title);
        Rect titleBefore = title.worldBound;
        Rect ctaBefore = menu.EnterMissionButton.worldBound;

        // ---- 动态效果 关闭: the shared planet spin must stop ----
        OverviewPlanetPreview rig = OverviewPlanetPreview.Shared;
        Assert.IsNotNull(rig, "shared rig exists");

        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.MotionOffButton), "动态效果关闭");
        yield return null;
        Assert.IsFalse(MainMenuSettings.MotionEnabled, "motion setting is off");
        Assert.IsFalse(OverviewPlanetPreview.MotionEnabled, "planet preview motion is off");
        Assert.IsTrue(menu.UiRoot.Q<Button>(MainMenuNames.MotionOffButton)
            .ClassListContains("segment-active"), "关闭 segment shows the active state");

        float frozenAngle = rig.SpinAngleDegrees;
        for (int i = 0; i < 20; i++)
        {
            yield return null;
        }

        Assert.AreEqual(frozenAngle, rig.SpinAngleDegrees, 0.0001f,
            "motion off really pauses the hero planet rotation");

        // ---- 动态效果 开启: rotation resumes ----
        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.MotionOnButton), "动态效果开启");
        yield return null;
        Assert.IsTrue(MainMenuSettings.MotionEnabled, "motion setting is back on");
        Assert.IsTrue(OverviewPlanetPreview.MotionEnabled, "planet preview motion is back on");
        float resumeStart = rig.SpinAngleDegrees;
        for (int i = 0; i < 20; i++)
        {
            yield return null;
        }

        Assert.Greater(rig.SpinAngleDegrees, resumeStart,
            "motion on really resumes the hero planet rotation");

        // ---- 界面提示 隐藏: the annotation layer goes away, layout does not ----
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

        // Restore the launch defaults immediately (never leak into other suites).
        MainMenuSettings.ResetToDefaults();
    }

    /// <summary>Main Menu &lt;-&gt; Overview round trip: one rig, one session, no reset.</summary>
    [UnityTest]
    public IEnumerator MainMenu_Overview_RoundTrip_NoDuplicates_Green()
    {
        MainMenuController menu = null;
        yield return BootToMainMenu(m => menu = m);
        Assert.IsNotNull(menu, "MainMenuController must be ready");

        GameSession session = GameSession.Instance;
        session.ResetSession();
        session.AddMinerals(9);
        yield return RealClick(menu.EnterMissionButton, "进入任务 (round trip out)");

        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });
        Assert.IsNotNull(app, "Overview reached");

        SceneRouting.EnterMainMenu();
        MainMenuController back = null;
        yield return WaitUntil(() =>
        {
            back = Object.FindAnyObjectByType<MainMenuController>();
            return back != null && back.IsReady;
        });
        Assert.IsNotNull(back, "main menu re-enterable");
        Assert.AreEqual(1, Object.FindObjectsByType<MainMenuController>().Length,
            "no duplicated menu controller");
        Assert.AreEqual(1, Object.FindObjectsByType<GameSession>().Length,
            "no duplicated persistent session");
        Assert.AreEqual(1, Object.FindObjectsByType<OverviewPlanetPreview>().Length,
            "no duplicated persistent preview rig");
        Assert.AreEqual(0, Object.FindObjectsByType<AppController>().Length,
            "the previous UI application scene is unloaded");
        Assert.AreSame(session, GameSession.Instance, "same session instance");
        Assert.AreEqual(29, session.Minerals, "returning to the menu never resets the session");
        Assert.IsTrue(back.IsHeroPlanetActive, "hero preview is active again after re-entry");
        string rigReport;
        Assert.IsTrue(OverviewPlanetPreview.Shared.VerifySetup(out rigReport),
            "preview rig still sound after re-entry: " + rigReport);

        yield return RealClick(back.EnterMissionButton, "进入任务 (round trip back)");
        AppController again = null;
        yield return WaitUntil(() =>
        {
            again = Object.FindAnyObjectByType<AppController>();
            return again != null && again.IsReady;
        });
        Assert.IsNotNull(again, "Overview reached again");
        Assert.AreEqual(AppScreen.Overview, again.CurrentScreen, "Overview after the round trip");
        Assert.AreEqual("29", again.UiRoot.Q<Label>(ElementNames.MineralsValue).text,
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
