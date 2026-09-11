using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 08 Revision 2 — title screen runtime validation.
///
/// Covered contracts:
/// - Boot lands on a MINIMAL title screen: the headline 活下去 and exactly four
///   options (新游戏 / 继续游戏 / 选项 / 退出), with the previous cover's HUD,
///   telemetry and 制作信息 gone,
/// - the hero ship stays on the LEFT and the menu on the RIGHT, and the ship's
///   NOSE points screen right (idle cruise and departure are left to right),
/// - focus is the primary state: navigation, skipping the disabled 继续游戏 and
///   confirmation all go through the same methods the gamepad handler calls,
/// - 新游戏 resets the ONE authoritative session, plays the cinematic and lands
///   in the first-person ship interior behind the three-beat eyelids,
/// - 继续游戏 resumes without resetting the session and uses the short eyelid,
/// - 选项 opens and closes (ESC / B / 返回) and still drives the presentation
///   switches,
/// - no EventSystem / AudioListener is introduced anywhere.
///
/// Timing note: every animated wait is bounded in REAL TIME. A batch-mode
/// PlayMode editor runs at thousands of frames per second, so a frame budget
/// cannot express "wait for the eyelid beats".
/// </summary>
public class SurvivePhase08Tests
{
    private const int MaxWaitFrames = 900;

    [SetUp]
    public void ResetState()
    {
        MainMenuSettings.ResetToDefaults();
        InputDeviceTracker.ResetToDefaults();

        // A fresh session per test: HasStartedRun is runtime-only state, so the
        // test must start from "nothing has been started yet".
        GameSession session = GameSession.Instance;
        if (session != null)
        {
            Object.Destroy(session.gameObject);
        }
    }

    /// <summary>The title screen is minimal, and the hero ship is on the left.</summary>
    [UnityTest]
    public IEnumerator TitleScreen_MinimalComposition_Green()
    {
        MainMenuController menu = null;
        yield return BootToTitleScreen(m => menu = m);
        Assert.IsNotNull(menu, "MainMenuController must be ready after Boot");

        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "boot must land on the title screen");
        Assert.AreEqual(0, Object.FindObjectsByType<AppController>().Length,
            "the title screen must not author a second UI application");

        // ---- The only headline ----
        Label title = menu.UiRoot.Q<Label>(MainMenuNames.Title);
        Assert.IsNotNull(title, "the title exists");
        Assert.AreEqual("活下去", title.text, "the only headline is 活下去");
        float childCount = menu.UiRoot.Query<Label>().ToList().Count;
        Assert.LessOrEqual(childCount, 15,
            "the title screen carries a handful of labels, not a HUD (" + childCount + ")");

        // ---- Exactly four options, in order ----
        Assert.AreEqual(4, menu.MenuItems.Count, "exactly four options");
        string[] expected = { "新游戏", "继续游戏", "选项", "退出" };
        for (int i = 0; i < expected.Length; i++)
        {
            Button item = menu.MenuItems[i];
            Assert.IsNotNull(item, "option " + i + " exists");
            Label label = item.Q<Label>(className: "menu-item-label");
            Assert.IsNotNull(label, "option " + i + " carries a label");
            Assert.AreEqual(expected[i], label.text, "option " + i + " copy");
        }

        // ---- The previous cover's copy is really gone ----
        var copy = new System.Text.StringBuilder();
        menu.UiRoot.Query<Label>().ForEach(label => copy.Append(label.text).Append('\n'));
        string screen = copy.ToString();
        foreach (string removed in MainMenuNames.RemovedCopy)
        {
            Assert.IsFalse(screen.Contains(removed),
                "the title screen must not show: " + removed);
        }

        Assert.IsNull(menu.UiRoot.Q<Button>("CreditsButton"), "no 制作信息 button");
        Assert.IsNull(menu.UiRoot.Q<VisualElement>("MenuTopBar"), "no top system bar");
        Assert.IsNull(menu.UiRoot.Q<VisualElement>("MenuFooter"), "no footer bar");
        Assert.IsNull(menu.UiRoot.Q<VisualElement>("HeroTelemetry"), "no telemetry strip");
        Assert.IsNull(menu.UiRoot.Q<VisualElement>("MenuHintLayer"), "no annotation layer");

        // ---- Composition: ship left, menu right ----
        VisualElement heroZone = menu.UiRoot.Q<VisualElement>(MainMenuNames.HeroColumn);
        VisualElement menuColumn = menu.UiRoot.Q<VisualElement>(MainMenuNames.ContentColumn);
        Rect rootRect = menu.UiRoot.worldBound;
        Assert.IsNotNull(heroZone, "hero window exists");
        Assert.IsNotNull(menuColumn, "menu column exists");
        Assert.LessOrEqual(heroZone.worldBound.xMax, menuColumn.worldBound.xMin + 1f,
            "the hero window is strictly LEFT of the menu column");
        Assert.Greater(menuColumn.worldBound.xMin, rootRect.width * 0.55f,
            "the menu column lives in the right part of the frame");
        Assert.Greater(menuColumn.worldBound.width, rootRect.width * 0.25f,
            "the menu column is wide enough to read");

        // Nothing is painted over the hero window or the root: the 3D stage IS
        // the background.
        Assert.Less(menu.UiRoot.Q<VisualElement>(MainMenuNames.Root)
            .resolvedStyle.backgroundColor.a, 0.05f, "the root stays transparent");
        Assert.Less(heroZone.resolvedStyle.backgroundColor.a, 0.05f,
            "the hero window stays transparent");

        // ---- The hero is the real ship, in the left half ----
        MainMenuShipHero hero = Object.FindAnyObjectByType<MainMenuShipHero>();
        Assert.IsNotNull(hero, "hero ship exists");
        Assert.IsTrue(hero.IsShipPresent, "the ship hierarchy is present");
        Assert.GreaterOrEqual(hero.ShipRendererCount, 40,
            "the whole authored ship is instantiated (" + hero.ShipRendererCount + ")");
        Assert.AreEqual(2, hero.NozzleCount, "both exhaust anchors drive a plume");

        Camera stageCamera = Object.FindAnyObjectByType<Camera>();
        Assert.IsNotNull(stageCamera, "stage camera exists");
        stageCamera.aspect = 16f / 9f;
        float shipX = stageCamera.WorldToViewportPoint(hero.WorldBounds.center).x;
        Assert.Less(shipX, 0.5f, "the ship centre is in the left half (x="
                                 + shipX.ToString("F3") + ")");
        Assert.Greater(shipX, 0.02f, "the ship is on screen");
        stageCamera.ResetAspect();

        // ---- Direction: nose to the RIGHT, plumes to the left ----
        AssertShipFliesRight(hero, stageCamera);

        // ---- Deep space ----
        MainMenuStarfield stars = Object.FindAnyObjectByType<MainMenuStarfield>();
        Assert.IsNotNull(stars, "starfield exists");
        Assert.IsTrue(stars.IsActive, "starfield is built");
        Assert.AreEqual(3, stars.LayerCount, "three parallax layers");
        Assert.Greater(stars.StarCount, 1000, "the sky carries a real star count");
        Assert.Greater(stars.ScrollSpeedOf(2), stars.ScrollSpeedOf(0),
            "the near layer drifts faster than the far one (parallax)");
        Assert.Greater(stars.TwinkleAmountOf(2), 0.2f,
            "the near layer twinkles visibly");

        // ---- Continue starts unavailable, and is not focusable ----
        Assert.IsFalse(menu.IsContinueAvailable,
            "nothing has been started in this process yet");
        Assert.IsFalse(menu.ContinueButton.enabledSelf, "继续游戏 is disabled");
        Assert.IsTrue(menu.ContinueButton.ClassListContains("menu-item-disabled"),
            "继续游戏 carries the weaker treatment");
        Assert.AreEqual(0, menu.FocusIndex, "focus starts on 新游戏");

        AssertNoEventSystemOrAudioListener("title screen");
        Assert.IsNotNull(GameSession.Instance, "the authoritative session exists");
        Assert.AreEqual(1, Object.FindObjectsByType<GameSession>().Length, "exactly one session");
    }

    /// <summary>Focus navigation, disabled skipping and confirmation.</summary>
    [UnityTest]
    public IEnumerator TitleScreen_FocusNavigation_Green()
    {
        MainMenuController menu = null;
        yield return BootToTitleScreen(m => menu = m);

        // Focus is the primary state and it is visible on the tree.
        Assert.AreEqual(0, menu.FocusIndex, "focus starts on 新游戏");
        Assert.IsTrue(menu.NewGameButton.ClassListContains("menu-item-focused"),
            "the focused option carries the focused class");
        Assert.IsFalse(menu.OptionsButton.ClassListContains("menu-item-focused"),
            "only one option is focused");

        // Down / Up move the focus and WRAP. 继续游戏 is disabled here, so the
        // focus must skip it.
        menu.FocusNext();
        Assert.AreEqual(2, menu.FocusIndex, "focus skips the disabled 继续游戏");
        Assert.IsTrue(menu.OptionsButton.ClassListContains("menu-item-focused"),
            "选项 is now focused");
        menu.FocusNext();
        Assert.AreEqual(3, menu.FocusIndex, "focus reaches 退出");
        menu.FocusNext();
        Assert.AreEqual(0, menu.FocusIndex, "focus wraps to 新游戏");
        menu.FocusPrevious();
        Assert.AreEqual(3, menu.FocusIndex, "focus wraps backwards to 退出");
        menu.FocusPrevious();
        Assert.AreEqual(2, menu.FocusIndex, "focus moves back to 选项");

        // Navigating upward again skips the disabled entry as well.
        menu.FocusPrevious();
        Assert.AreEqual(0, menu.FocusIndex, "focus skips 继续游戏 going up");

        // The device-aware hint only appears for a gamepad, and it names A/B.
        InputDeviceTracker.NotifyKeyboardMouse();
        yield return null;
        Assert.IsFalse(menu.IsGamepadHintVisible, "no controller hint for mouse/keyboard");

        InputDeviceTracker.NotifyGamepad();
        yield return null;
        Assert.IsTrue(menu.IsGamepadHintVisible, "the controller hint appears on gamepad");
        Assert.AreEqual("(A) 确认", menu.InputHintText, "the hint names the A button");

        // Confirmation is the same call the A button makes.
        menu.SetFocus(2);
        Assert.IsTrue(menu.ConfirmFocused(), "A confirms the focused option");
        yield return WaitUntilReal(() => menu.IsOptionsOpen, 4f);
        Assert.IsTrue(menu.IsOptionsOpen, "选项 opened from the focused option");

        InputDeviceTracker.NotifyGamepad();
        yield return null;
        Assert.AreEqual("(A) 确认    (B) 返回", menu.InputHintText,
            "the options hint adds B");

        // Back (B / ESC) closes it.
        menu.HandleBackInput();
        yield return WaitUntilReal(() => !menu.IsOptionsOpen, 4f);
        Assert.IsFalse(menu.IsOptionsOpen, "back closes 选项");
        Assert.AreEqual(2, menu.FocusIndex, "closing the modal keeps the focus");

        InputDeviceTracker.ResetToDefaults();
    }

    /// <summary>新游戏: session reset, cinematic, left-to-right departure, eyelids.</summary>
    [UnityTest]
    public IEnumerator NewGame_Cinematic_Eyelids_ShipInterior_Green()
    {
        MainMenuController menu = null;
        yield return BootToTitleScreen(m => menu = m);

        GameSession session = GameSession.Instance;
        session.ResetSession();
        session.AddMinerals(13);
        Assert.AreEqual(33, session.Minerals, "session prepared");
        Assert.IsFalse(session.HasStartedRun, "no run has started yet");

        MainMenuCinematic cinematic = menu.Cinematic;
        MainMenuShipHero hero = Object.FindAnyObjectByType<MainMenuShipHero>();
        Camera stageCamera = Object.FindAnyObjectByType<Camera>();
        stageCamera.aspect = 16f / 9f;

        menu.SetFocus(0);
        Assert.IsTrue(menu.ConfirmFocused(), "A starts 新游戏");

        // ---- New game semantics ----
        Assert.IsTrue(session.HasStartedRun, "新游戏 starts a run");
        Assert.AreEqual(GameSession.DefaultMinerals, session.Minerals,
            "新游戏 resets the authoritative session");
        Assert.IsTrue(menu.IsContinueAvailable, "继续游戏 becomes available after 新游戏");
        Assert.IsTrue(menu.ContinueButton.enabledSelf, "继续游戏 is now enabled");
        Assert.IsTrue(cinematic.IsPlaying, "the cinematic started");
        Assert.IsFalse(cinematic.IsContinueShot, "新游戏 plays the full shot");
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "the shot plays inside the title screen scene");

        // Menu fade first, then boost, then a LEFT -> RIGHT departure.
        yield return WaitUntilReal(() => cinematic.MenuAlpha < 0.5f, 6f);
        Assert.Less(cinematic.MenuAlpha, 0.5f, "the menu fades out first");

        yield return WaitUntilReal(() => hero.IsDeparting, 8f);
        Assert.IsTrue(hero.IsDeparting, "the ship departs");
        yield return WaitUntilReal(() => hero.DepartureProgress > 0.3f, 8f);

        Vector3 moved = hero.transform.position - hero.SpawnPosition;
        Assert.Greater(moved.magnitude, 1f, "the ship actually moved");
        Vector3 screenRight = stageCamera.transform.right;
        Assert.Greater(Vector3.Dot(moved, screenRight), 0.5f,
            "the departure moves the ship to the RIGHT of the screen (dot="
            + Vector3.Dot(moved, screenRight).ToString("F2") + ")");
        Assert.Greater(hero.EngineLevel, 0.4f, "the thrusters are boosted");

        // ---- Ship interior ----
        ShipGameController ship = null;
        yield return WaitUntilReal(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady;
        }, 30f);
        Assert.IsNotNull(ship, "新游戏 lands in the ship interior");
        Assert.AreEqual(GameSession.ShipInteriorSceneName, SceneManager.GetActiveScene().name,
            "landing scene");
        Assert.AreEqual(0, Object.FindObjectsByType<AppController>().Length,
            "新游戏 does NOT land in the Overview/Hub UI");

        // ---- Three-beat eyelids ----
        Assert.IsTrue(ship.EntryBlinkArmed, "the eyelids were armed by the hand-over");
        Assert.IsTrue(ship.IsEntryInputLocked, "control is locked while the eyes are shut");

        var stages = new HashSet<string>();
        ShipEntryBlinkFade blink = ship.EntryBlink;
        Assert.IsNotNull(blink, "the eyelid component is running");
        Assert.AreEqual(SceneRouting.EntryEyelid.Full, blink.Mode, "新游戏 plays the full eyelid");
        Assert.IsFalse(blink.TopEyelid == null, "the TOP eyelid exists");
        Assert.IsFalse(blink.BottomEyelid == null, "the BOTTOM eyelid exists");
        Assert.IsTrue(blink.TopEyelid.name == "TopEyelid", "top eyelid is named");
        Assert.IsTrue(blink.BottomEyelid.name == "BottomEyelid", "bottom eyelid is named");
        Assert.GreaterOrEqual(blink.CoveredFraction, 0.49f,
            "the eyes start fully shut (both curtains meet on the centre line)");
        Assert.AreEqual(PickingMode.Ignore, blink.TopEyelid.pickingMode,
            "the eyelids never block input");

        float deadline = Time.realtimeSinceStartup + 8f;
        while (ship.EntryBlink != null && Time.realtimeSinceStartup < deadline)
        {
            stages.Add(ship.EntryBlink.StageLabel);
            if (ship.EntryBlink.CoveredFraction > 0.5f)
            {
                Assert.Fail("an eyelid curtain covered more than half the screen");
            }

            yield return null;
        }

        Assert.IsTrue(stages.Contains("beat1"), "beat 1 happened (stages: "
                                                + string.Join(",", stages) + ")");
        Assert.IsTrue(stages.Contains("beat2"), "beat 2 happened (stages: "
                                                + string.Join(",", stages) + ")");
        Assert.IsTrue(stages.Contains("beat3"), "beat 3 happened (stages: "
                                                + string.Join(",", stages) + ")");

        // The controller releases the lock in its own Update, one frame after the
        // eyelid component destroys itself.
        yield return WaitUntilReal(() => !ship.IsEntryInputLocked, 4f);
        Assert.IsFalse(ship.IsEntryInputLocked,
            "control is released when the eyes are open");
        Assert.IsTrue(ship.Player.InputEnabled, "the player can move");
        Assert.IsFalse(ship.IsConsoleFocusActive, "no terminal UI covers the frame");

        // The overlay is gone: nothing is left to block input or picking.
        ShipHudController hud = Object.FindAnyObjectByType<ShipHudController>();
        Assert.IsNotNull(hud, "the ship HUD exists");
        var hudDocument = hud.GetComponent<UIDocument>();
        Assert.IsNotNull(hudDocument, "the ship HUD document exists");
        Assert.IsNull(hudDocument.rootVisualElement.Q<VisualElement>("TopEyelid"),
            "no leftover top eyelid element");
        Assert.IsNull(hudDocument.rootVisualElement.Q<VisualElement>("BottomEyelid"),
            "no leftover bottom eyelid element");
        Assert.AreEqual(0, Object.FindObjectsByType<MainMenuController>().Length,
            "the title screen scene is unloaded");
    }

    /// <summary>The eyelid curve itself: small crack, bigger crack, full open.</summary>
    [UnityTest]
    public IEnumerator EyelidCurve_ThreeBeats_Green()
    {
        yield return null;

        const SceneRouting.EntryEyelid full = SceneRouting.EntryEyelid.Full;

        // Closed at the start, and both curtains meet exactly on the centre line.
        Assert.AreEqual(0f, ShipEntryBlinkFade.OpennessAt(0f, full), 0.0001f,
            "the eyes start shut");
        Assert.AreEqual(0.5f, ShipEntryBlinkFade.CoveredFractionFor(0f), 0.0001f,
            "a shut eye covers half the screen with each curtain");

        // Beat 1: a small crack only.
        float beat1 = ShipEntryBlinkFade.OpennessAt(ShipEntryBlinkFade.Beat1OpenEnd, full);
        Assert.Greater(beat1, 0.15f, "beat 1 opens");
        Assert.Less(beat1, 0.3f, "beat 1 stays small (openness " + beat1.ToString("F2") + ")");

        // Back to shut.
        Assert.AreEqual(0f,
            ShipEntryBlinkFade.OpennessAt(ShipEntryBlinkFade.Beat1CloseEnd, full), 0.0001f,
            "the eyes close again after beat 1");

        // Beat 2: clearly bigger, still not open.
        float beat2 = ShipEntryBlinkFade.OpennessAt(ShipEntryBlinkFade.Beat2OpenEnd, full);
        Assert.Greater(beat2, beat1 + 0.1f,
            "beat 2 opens further than beat 1 (" + beat2.ToString("F2") + ")");
        Assert.Less(beat2, 0.6f, "beat 2 is still not fully open");

        Assert.AreEqual(0f,
            ShipEntryBlinkFade.OpennessAt(ShipEntryBlinkFade.Beat2CloseEnd, full), 0.0001f,
            "the eyes close again after beat 2");

        // Beat 3: fully open and it stays open.
        Assert.AreEqual(1f,
            ShipEntryBlinkFade.OpennessAt(ShipEntryBlinkFade.Beat3OpenEnd, full), 0.0001f,
            "beat 3 opens completely");
        Assert.AreEqual(1f, ShipEntryBlinkFade.OpennessAt(10f, full), 0.0001f,
            "the eyes stay open");
        Assert.AreEqual(0f,
            ShipEntryBlinkFade.CoveredFractionFor(ShipEntryBlinkFade.OpennessAt(10f, full)),
            0.0001f, "nothing is covered once open");

        // The whole animation is inside the 0.8-1.3 s budget.
        Assert.GreaterOrEqual(ShipEntryBlinkFade.BlinkSeconds, 0.8f, "not too fast");
        Assert.LessOrEqual(ShipEntryBlinkFade.BlinkSeconds, 1.3f, "not too slow");

        // The short (继续游戏) variant opens once and stays open.
        const SceneRouting.EntryEyelid quick = SceneRouting.EntryEyelid.Quick;
        Assert.AreEqual(0f, ShipEntryBlinkFade.OpennessAt(0f, quick), 0.0001f, "quick starts shut");
        Assert.AreEqual(1f, ShipEntryBlinkFade.OpennessAt(1f, quick), 0.0001f,
            "quick ends fully open");
        Assert.Less(ShipEntryBlinkFade.DurationSeconds(quick),
            ShipEntryBlinkFade.DurationSeconds(full), "quick is shorter than full");

        // Stage names describe the beats (used by the probes).
        Assert.AreEqual("beat1", ShipEntryBlinkFade.StageAt(0.10f, full), "beat 1 stage");
        Assert.AreEqual("beat2", ShipEntryBlinkFade.StageAt(0.35f, full), "beat 2 stage");
        Assert.AreEqual("beat3", ShipEntryBlinkFade.StageAt(0.80f, full), "beat 3 stage");
        Assert.AreEqual("open", ShipEntryBlinkFade.StageAt(1.20f, full), "open stage");
    }

    /// <summary>继续游戏 resumes the run without resetting it.</summary>
    [UnityTest]
    public IEnumerator Continue_ResumesWithoutReset_Green()
    {
        MainMenuController menu = null;
        yield return BootToTitleScreen(m => menu = m);

        // Start a run through the real 新游戏 path, then come back to the title.
        menu.SetFocus(0);
        Assert.IsTrue(menu.ConfirmFocused(), "新游戏 starts a run");
        ShipGameController ship = null;
        yield return WaitUntilReal(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady;
        }, 30f);
        Assert.IsNotNull(ship, "first entry reaches the ship");

        GameSession session = GameSession.Instance;
        session.AddMinerals(11);
        int minerals = session.Minerals;
        int alloy = session.Alloy;

        SceneRouting.EnterMainMenu();
        MainMenuController back = null;
        yield return WaitUntilReal(() =>
        {
            back = Object.FindAnyObjectByType<MainMenuController>();
            return back != null && back.IsReady;
        }, 30f);
        Assert.IsNotNull(back, "the title screen is re-enterable");

        // ---- Continue is available and resumes ----
        Assert.IsTrue(back.IsContinueAvailable, "继续游戏 is available after a run started");
        Assert.IsTrue(back.ContinueButton.enabledSelf, "继续游戏 is enabled");
        Assert.IsFalse(back.ContinueButton.ClassListContains("menu-item-disabled"),
            "继续游戏 no longer carries the disabled treatment");

        back.SetFocus(1);
        Assert.AreEqual(1, back.FocusIndex, "focus can sit on 继续游戏 when it is available");
        MainMenuCinematic cinematic = back.Cinematic;
        Assert.IsTrue(back.ConfirmFocused(), "A confirms 继续游戏");
        Assert.IsTrue(cinematic.IsPlaying, "the continue shot started");
        Assert.IsTrue(cinematic.IsContinueShot, "继续游戏 plays the short shot");
        Assert.IsTrue(session.HasStartedRun, "the run stays started");

        ShipGameController resumed = null;
        yield return WaitUntilReal(() =>
        {
            resumed = Object.FindAnyObjectByType<ShipGameController>();
            return resumed != null && resumed.IsReady;
        }, 30f);
        Assert.IsNotNull(resumed, "继续游戏 returns to the ship");
        Assert.AreSame(session, GameSession.Instance, "the same session object is reused");
        Assert.AreEqual(minerals, session.Minerals, "继续游戏 never resets the session");
        Assert.AreEqual(alloy, session.Alloy, "the whole session is preserved");
        Assert.IsTrue(resumed.EntryBlinkArmed, "the short eyelid was armed");
    }

    /// <summary>选项 opens/closes and still drives the presentation switches.</summary>
    [UnityTest]
    public IEnumerator Options_Modal_Green()
    {
        MainMenuController menu = null;
        yield return BootToTitleScreen(m => menu = m);

        VisualElement options = menu.UiRoot.Q<Button>(MainMenuNames.OptionsButton);
        Assert.IsNotNull(options, "选项 exists");

        yield return RealClick(options, "选项");
        yield return WaitUntilReal(() => menu.IsOptionsOpen, 4f);
        Assert.IsTrue(menu.IsOptionsOpen, "选项 opens");
        Assert.AreEqual(1, menu.OpenModalCount, "exactly one modal is shown");
        Assert.AreEqual("选项",
            menu.UiRoot.Q<Label>(MainMenuNames.OptionsTitle).text, "the modal is titled 选项");

        // The menu behind it is blocked: confirming does nothing.
        menu.SetFocus(0);
        Assert.IsFalse(menu.ConfirmFocused(), "the blocked title screen cannot start a game");
        Assert.IsFalse(menu.Cinematic.IsPlaying, "no cinematic behind the modal");

        // ---- The switches still work ----
        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.MotionOffButton), "动态效果关闭");
        yield return null;
        Assert.IsFalse(MainMenuSettings.MotionEnabled, "动态效果 off");
        Assert.IsTrue(menu.UiRoot.Q<Button>(MainMenuNames.MotionOffButton)
            .ClassListContains("segment-active"), "the active segment is shown");

        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.HintsHideButton), "界面提示隐藏");
        yield return null;
        Assert.IsFalse(MainMenuSettings.HintsVisible, "界面提示 off");

        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.MotionOnButton), "动态效果开启");
        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.HintsShowButton), "界面提示显示");
        yield return null;
        Assert.IsTrue(MainMenuSettings.MotionEnabled, "动态效果 back on");
        Assert.IsTrue(MainMenuSettings.HintsVisible, "界面提示 back on");

        // ---- 返回 closes it ----
        yield return RealClick(menu.UiRoot.Q<Button>(MainMenuNames.OptionsCloseButton), "返回");
        yield return WaitUntilReal(() => !menu.IsOptionsOpen, 4f);
        Assert.IsFalse(menu.IsOptionsOpen, "返回 closes 选项");
        yield return WaitUntilReal(() => menu.OpenModalCount == 0, 4f);
        Assert.AreEqual(0, menu.OpenModalCount, "the modal is fully gone");

        // ESC path.
        yield return RealClick(options, "选项 (ESC path)");
        yield return WaitUntilReal(() => menu.IsOptionsOpen, 4f);
        menu.HandleEscapeKey();
        yield return WaitUntilReal(() => !menu.IsOptionsOpen, 4f);
        Assert.IsFalse(menu.IsOptionsOpen, "ESC closes 选项");
        Assert.AreEqual(SceneRouting.MainMenuSceneName, SceneManager.GetActiveScene().name,
            "ESC never leaves the title screen");

        MainMenuSettings.ResetToDefaults();
    }

    /// <summary>
    /// The ship's screen-space direction is LEFT to RIGHT: the nose points right,
    /// the plumes point left, and the idle cruise drifts forward only.
    /// </summary>
    [UnityTest]
    public IEnumerator ShipDirection_LeftToRight_Green()
    {
        MainMenuController menu = null;
        yield return BootToTitleScreen(m => menu = m);

        MainMenuShipHero hero = Object.FindAnyObjectByType<MainMenuShipHero>();
        Camera stageCamera = Object.FindAnyObjectByType<Camera>();
        Assert.IsNotNull(hero, "hero exists");
        Assert.IsNotNull(stageCamera, "camera exists");
        AssertShipFliesRight(hero, stageCamera);

        // The plumes stream backwards (screen left), opposite the flight.
        Assert.AreEqual(2, hero.Flames.Count, "two plumes");
        Vector3 plumeDirection = hero.AftDirection;
        Vector3 plumeWorld = hero.transform.TransformDirection(plumeDirection);
        stageCamera.aspect = 16f / 9f;
        float tailX = stageCamera.WorldToViewportPoint(
            hero.WorldBounds.center + plumeWorld * 6f).x;
        float noseX = stageCamera.WorldToViewportPoint(
            hero.WorldBounds.center - plumeWorld * 6f).x;
        Assert.Greater(noseX, tailX, "the nose is to the RIGHT of the tail on screen");
        stageCamera.ResetAspect();

        // The idle cruise only ever pushes forward; the settle back is so slow
        // that it never reads as reverse flight.
        Assert.AreEqual(0f, MainMenuShipHero.CruiseOffsetAt(0f), 0.0001f, "cruise starts at rest");
        Assert.IsTrue(MainMenuShipHero.CruiseIsRightwardAt(1f),
            "the cruise is moving forward shortly after entry");
        Assert.Greater(MainMenuShipHero.CruiseOffsetAt(6f),
            MainMenuShipHero.CruiseOffsetAt(1f), "the cruise keeps moving forward");
        Assert.LessOrEqual(MainMenuShipHero.CruiseOffsetAt(64f),
            MainMenuShipHero.CruiseDistance + 0.001f, "the cruise stays inside its box");
        Assert.GreaterOrEqual(MainMenuShipHero.CruiseOffsetAt(64f), 0f,
            "the cruise never goes behind its start");
    }

    // ---- helpers ----

    /// <summary>
    /// Asserts the ship flies LEFT to RIGHT on screen: the nose points along the
    /// camera's right axis and the plumes point the other way. Comparing world
    /// directions against the camera's right axis is aspect independent and needs
    /// no projection, so it cannot be fooled by a Game View that is square.
    /// </summary>
    private static void AssertShipFliesRight(MainMenuShipHero hero, Camera camera)
    {
        Vector3 screenRight = camera.transform.right;
        float noseDot = Vector3.Dot(hero.ForwardWorld, screenRight);
        Assert.Greater(noseDot, 0.2f,
            "the nose points toward the screen RIGHT (dot=" + noseDot.ToString("F3") + ")");

        float plumeDot = Vector3.Dot(
            hero.transform.TransformDirection(hero.AftDirection), screenRight);
        Assert.Less(plumeDot, -0.2f,
            "the plumes stream toward the screen LEFT (dot=" + plumeDot.ToString("F3") + ")");
    }

    private static IEnumerator BootToTitleScreen(System.Action<MainMenuController> capture)
    {
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

        yield return null;
        yield return null;
    }

    private static IEnumerator RealClick(VisualElement element, string label)
    {
        Assert.IsNotNull(element, "clickable element exists: " + label);
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

    private static IEnumerator WaitUntilReal(System.Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
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
