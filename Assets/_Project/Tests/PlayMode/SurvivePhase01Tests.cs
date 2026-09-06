using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 01 Play Mode validation.
///
/// The suite is intentionally split so that only ONE test loads scenes
/// (00_Boot -> 01_Hub) per play-mode session: repeated scene loads of the
/// Hub within a single session can re-create the UIDocument panel, which is
/// not representative of the runtime flow (a fresh process loads Boot -> Hub
/// exactly once).
/// </summary>
public class SurvivePhase01Tests
{
    private const int MaxWaitFrames = 600;

    private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = MaxWaitFrames)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }

    private static string ActiveSceneName()
    {
        return SceneManager.GetActiveScene().name;
    }

    private static int GameSessionCount()
    {
        return Object.FindObjectsByType<GameSession>().Length;
    }

    /// <summary>Pure authoritative-state cycle (no scenes, fully deterministic).</summary>
    [UnityTest]
    public IEnumerator SessionState_FlowCycle_AddsProcessesUpgradesResets()
    {
        // Establish preconditions deterministically.
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        // Default state.
        Assert.AreEqual(74, session.Energy, "default Energy");
        Assert.AreEqual(51, session.Food, "default Food");
        Assert.AreEqual(20, session.Minerals, "default Minerals");
        Assert.AreEqual(8, session.Fuel, "default Fuel");
        Assert.AreEqual(5, session.Alloy, "default Alloy");
        Assert.AreEqual(1, session.RefineryLevel, "default RefineryLevel");
        yield return null;

        // +10 Minerals.
        session.AddMinerals(10);
        Assert.AreEqual(30, session.Minerals, "Minerals after +10");

        // Process 5 Minerals -> +2 Fuel +1 Alloy.
        Assert.IsTrue(session.TryProcessMinerals(), "first process should succeed");
        Assert.AreEqual(25, session.Minerals, "Minerals after one process");
        Assert.AreEqual(10, session.Fuel, "Fuel after one process");
        Assert.AreEqual(6, session.Alloy, "Alloy after one process");

        // Upgrade Refinery: -3 Alloy, level+1.
        Assert.IsTrue(session.TryUpgradeRefinery(), "first upgrade should succeed");
        Assert.AreEqual(3, session.Alloy, "Alloy after upgrade");
        Assert.AreEqual(2, session.RefineryLevel, "RefineryLevel after upgrade");

        // Insufficient Alloy: two upgrades fit into the default 5 Alloy
        // (5 -> 2 -> 0), the third must fail safely.
        Assert.IsTrue(session.TryUpgradeRefinery(), "second upgrade should succeed (Alloy 3)");
        Assert.AreEqual(0, session.Alloy, "Alloy after second upgrade");
        Assert.AreEqual(3, session.RefineryLevel, "RefineryLevel after second upgrade");
        Assert.IsFalse(session.TryUpgradeRefinery(), "third upgrade must fail (Alloy < 3)");
        Assert.AreEqual(3, session.RefineryLevel, "level unchanged on failed upgrade");
        Assert.AreEqual(0, session.Alloy, "Alloy unchanged on failed upgrade");

        // Insufficient Minerals: drain 25 -> 0 over five processes; the sixth fails.
        for (int i = 0; i < 5; i++)
        {
            Assert.IsTrue(session.TryProcessMinerals(), "process drain step " + i);
        }

        Assert.AreEqual(0, session.Minerals, "Minerals after drain to 0");
        Assert.IsFalse(session.TryProcessMinerals(), "process must fail with Minerals < 5");
        Assert.AreEqual(0, session.Minerals, "Minerals unchanged on failed process");

        // Reset restores defaults.
        session.ResetSession();
        Assert.AreEqual(74, session.Energy, "reset Energy");
        Assert.AreEqual(51, session.Food, "reset Food");
        Assert.AreEqual(20, session.Minerals, "reset Minerals");
        Assert.AreEqual(8, session.Fuel, "reset Fuel");
        Assert.AreEqual(5, session.Alloy, "reset Alloy");
        Assert.AreEqual(1, session.RefineryLevel, "reset RefineryLevel");
    }

    /// <summary>
    /// The single scene-loading test: Boot -> Hub -> exactly one shared
    /// GameSession -> full UI journey (navigation, processing, refinery
    /// upgrade, reset) with cross-screen consistency and disabled-button
    /// states.
    /// </summary>
    [UnityTest]
    public IEnumerator BootToHub_SharedSession_FullUiJourney()
    {
        // Deterministic precondition.
        GameSession.Ensure().ResetSession();

        SceneManager.LoadScene("00_Boot");
        yield return WaitUntil(() => ActiveSceneName() == BootController.HubSceneName);
        Assert.AreEqual(BootController.HubSceneName, ActiveSceneName(), "Boot must reach Hub");

        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });

        Assert.IsNotNull(app, "AppController did not become ready in Hub");
        Assert.AreEqual(1, Object.FindObjectsByType<AppController>().Length, "exactly one AppController/UI app");
        Assert.AreEqual(1, GameSessionCount(), "exactly one authoritative GameSession");

        GameSession session = GameSession.Instance;
        Assert.IsNotNull(session, "GameSession.Instance must exist");
        VisualElement root = app.UiRoot;
        Assert.IsNotNull(root, "UI root must exist");

        // ---- Fresh-launch defaults (Boot applied defaults) ----
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "initial screen is Overview");
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one visible screen initially");
        Assert.AreEqual("74", LabelText(root, ElementNames.EnergyValue), "Overview Energy text");
        Assert.AreEqual("51", LabelText(root, ElementNames.FoodValue), "Overview Food text");
        Assert.AreEqual("20", LabelText(root, ElementNames.MineralsValue), "Overview Minerals text");
        Assert.AreEqual("8", LabelText(root, ElementNames.FuelValue), "Overview Fuel text");
        Assert.AreEqual("5", LabelText(root, ElementNames.AlloyValue), "Overview Alloy text");
        Assert.AreEqual("NEREID-04", LabelText(root, ElementNames.DestinationValue), "Overview destination text");

        // ---- Preparation screen: reachable, default cargo/status ----
        app.ShowScreen(AppScreen.Preparation);
        yield return null;
        Assert.AreEqual(AppScreen.Preparation, app.CurrentScreen, "Preparation reachable");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen on Preparation");
        Assert.AreEqual("0 / 100", LabelText(root, ElementNames.CargoValue), "Preparation default cargo");
        Assert.AreEqual("STATUS: DRAFT", LabelText(root, ElementNames.LoadoutStateLabel), "Preparation default status");

        // ---- Processing: process mutation + button enablement ----
        app.ShowScreen(AppScreen.Processing);
        yield return null;
        Assert.AreEqual(AppScreen.Processing, app.CurrentScreen, "Processing reachable");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen on Processing");
        Assert.AreEqual("20", LabelText(root, ElementNames.ProcessingMineralsValue), "Processing minerals before");

        Button processButton = root.Q<Button>(ElementNames.ProcessButton);
        Assert.IsNotNull(processButton, "ProcessButton must exist");
        Assert.IsTrue(processButton.enabledSelf, "ProcessButton enabled while Minerals >= 5");

        Assert.IsTrue(session.TryProcessMinerals(), "process should succeed with 20 Minerals");
        Assert.AreEqual(15, session.Minerals, "session Minerals after process");
        Assert.AreEqual("15", LabelText(root, ElementNames.ProcessingMineralsValue), "Processing minerals after");
        Assert.AreEqual("10", LabelText(root, ElementNames.ProcessingFuelValue), "Processing fuel after");
        Assert.AreEqual("6", LabelText(root, ElementNames.ProcessingAlloyValue), "Processing alloy after");

        // ---- Overview reflects the processing mutation ----
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual(AppScreen.Overview, app.CurrentScreen, "Overview reachable again");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen back on Overview");
        Assert.AreEqual("15", LabelText(root, ElementNames.MineralsValue), "Overview Minerals cross-screen");
        Assert.AreEqual("10", LabelText(root, ElementNames.FuelValue), "Overview Fuel cross-screen");
        Assert.AreEqual("6", LabelText(root, ElementNames.AlloyValue), "Overview Alloy cross-screen");

        // ---- Outpost: refinery upgrade + button enablement ----
        app.ShowScreen(AppScreen.Outpost);
        yield return null;
        Assert.AreEqual(AppScreen.Outpost, app.CurrentScreen, "Outpost reachable");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen on Outpost");
        Assert.AreEqual("6", LabelText(root, ElementNames.OutpostAlloyValue), "Outpost Alloy before upgrade");
        Assert.AreEqual("REFINERY LV.1", LabelText(root, ElementNames.RefineryLevelValue), "Outpost refinery level before");

        Button upgradeButton = root.Q<Button>(ElementNames.UpgradeRefineryButton);
        Assert.IsNotNull(upgradeButton, "UpgradeRefineryButton must exist");
        Assert.IsTrue(upgradeButton.enabledSelf, "Upgrade button enabled while Alloy >= 3");

        Assert.IsTrue(session.TryUpgradeRefinery(), "upgrade should succeed with 6 Alloy");
        Assert.AreEqual(3, session.Alloy, "session Alloy after upgrade");
        Assert.AreEqual(2, session.RefineryLevel, "session RefineryLevel after upgrade");
        Assert.AreEqual("3", LabelText(root, ElementNames.OutpostAlloyValue), "Outpost Alloy after upgrade");
        Assert.AreEqual("REFINERY LV.2", LabelText(root, ElementNames.RefineryLevelValue), "Outpost refinery level after");

        // Second upgrade (3 Alloy) drains Alloy to 0 -> level 3, button disabled.
        Assert.IsTrue(session.TryUpgradeRefinery(), "second upgrade succeeds (3 Alloy)");
        Assert.AreEqual("0", LabelText(root, ElementNames.OutpostAlloyValue), "Outpost Alloy drained");
        Assert.AreEqual("REFINERY LV.3", LabelText(root, ElementNames.RefineryLevelValue), "Outpost refinery level after drain");
        Assert.IsFalse(upgradeButton.enabledSelf, "Upgrade button disabled while Alloy < 3");
        Assert.IsFalse(session.TryUpgradeRefinery(), "upgrade fails safely when Alloy < 3");
        Assert.AreEqual(3, session.RefineryLevel, "level unchanged after failed upgrade");

        // ---- Overview still consistent ----
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual("0", LabelText(root, ElementNames.AlloyValue), "Overview Alloy after upgrades");

        // ---- Insufficient Minerals: drain to 0, button disables ----
        // Current Minerals 15: 15 -> 10 -> 5 (two processes), then the third
        // drains to 0 and the fourth must fail safely.
        Assert.IsTrue(session.TryProcessMinerals(), "minerals drain step 1");
        Assert.IsTrue(session.TryProcessMinerals(), "minerals drain step 2");
        Assert.AreEqual(5, session.Minerals, "Minerals after drain to 5");

        app.ShowScreen(AppScreen.Processing);
        yield return null;
        Assert.IsTrue(processButton.enabledSelf, "ProcessButton enabled at Minerals == 5");

        Assert.IsTrue(session.TryProcessMinerals(), "process at Minerals == 5 succeeds");
        Assert.AreEqual(0, session.Minerals, "Minerals after drain to 0");
        Assert.IsFalse(processButton.enabledSelf, "ProcessButton disabled at Minerals < 5");
        Assert.IsFalse(session.TryProcessMinerals(), "process fails safely when Minerals < 5");
        Assert.AreEqual(0, session.Minerals, "Minerals unchanged on failed process");

        // ---- Reset restores defaults and refreshes every screen ----
        session.ResetSession();
        Assert.AreEqual(74, session.Energy, "session Energy after reset");
        Assert.AreEqual(51, session.Food, "session Food after reset");
        Assert.AreEqual(20, session.Minerals, "session Minerals after reset");
        Assert.AreEqual(8, session.Fuel, "session Fuel after reset");
        Assert.AreEqual(5, session.Alloy, "session Alloy after reset");
        Assert.AreEqual(1, session.RefineryLevel, "session RefineryLevel after reset");

        Assert.AreEqual("74", LabelText(root, ElementNames.EnergyValue), "Overview Energy after reset");
        Assert.AreEqual("20", LabelText(root, ElementNames.MineralsValue), "Overview Minerals after reset");
        Assert.AreEqual("8", LabelText(root, ElementNames.FuelValue), "Overview Fuel after reset");
        Assert.AreEqual("5", LabelText(root, ElementNames.AlloyValue), "Overview Alloy after reset");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen after reset");
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }
}
