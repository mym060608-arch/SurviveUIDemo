using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 01 Play Mode validation (kept green under Phase 04 semantics).
///
/// Phase 04 intentionally migrated the instant "PROCESS 5 MINERALS" action to
/// the batch refinery flow (TryStartProcess / AdvanceProcessing), preserving
/// the conversion ratio (5 Minerals -> +2 Fuel / +1 Alloy). This suite now
/// drives the same journey through the batch API and asserts the same state
/// contract; the underlying Phase 01 resource semantics stay green.
///
/// Only one test loads scenes (00_Boot -> 01_Hub) per play-mode session.
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
        Assert.AreEqual(GameSession.RefineryProcessState.Idle, session.ProcessState, "default process Idle");
        yield return null;

        // +10 Minerals.
        session.AddMinerals(10);
        Assert.AreEqual(30, session.Minerals, "Minerals after +10");

        // Batch 5: minerals deducted on start, outputs settle on completion.
        Assert.IsTrue(session.TryStartProcess(5), "start batch 5");
        Assert.AreEqual(25, session.Minerals, "Minerals after starting batch 5");
        Assert.AreEqual(GameSession.RefineryProcessState.Running, session.ProcessState, "state Running");
        Assert.IsFalse(session.TryStartProcess(5), "double start rejected");
        Assert.AreEqual(25, session.Minerals, "no double cost on rejected start");

        session.AdvanceProcessing(GameSession.ProcessDurationSeconds);
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "state Complete");
        Assert.AreEqual(25, session.Minerals, "Minerals unchanged by completion");
        Assert.AreEqual(10, session.Fuel, "Fuel after batch 5 (+2)");
        Assert.AreEqual(6, session.Alloy, "Alloy after batch 5 (+1)");

        // Upgrade Refinery: -3 Alloy, level+1.
        Assert.IsTrue(session.TryUpgradeRefinery(), "first upgrade should succeed");
        Assert.AreEqual(3, session.Alloy, "Alloy after upgrade");
        Assert.AreEqual(2, session.RefineryLevel, "RefineryLevel after upgrade");

        // Second upgrade drains Alloy to 0 -> level 3; third must fail safely.
        Assert.IsTrue(session.TryUpgradeRefinery(), "second upgrade should succeed (Alloy 3)");
        Assert.AreEqual(0, session.Alloy, "Alloy after second upgrade");
        Assert.AreEqual(3, session.RefineryLevel, "RefineryLevel after second upgrade");
        Assert.IsFalse(session.TryUpgradeRefinery(), "third upgrade must fail (Alloy < 3)");
        Assert.AreEqual(3, session.RefineryLevel, "level unchanged on failed upgrade");
        Assert.AreEqual(0, session.Alloy, "Alloy unchanged on failed upgrade");

        // Drain Minerals 25 -> 0 with supported batches; failing start keeps 0.
        Assert.IsTrue(session.TryStartProcess(15), "drain batch 15");
        session.AdvanceProcessing(GameSession.ProcessDurationSeconds);
        Assert.AreEqual(10, session.Minerals, "Minerals after 15 batch");
        Assert.IsTrue(session.TryStartProcess(10), "drain batch 10");
        session.AdvanceProcessing(GameSession.ProcessDurationSeconds);
        Assert.AreEqual(0, session.Minerals, "Minerals after drain to 0");
        Assert.IsFalse(session.TryStartProcess(5), "start must fail with Minerals < 5");
        Assert.AreEqual(0, session.Minerals, "Minerals unchanged on failed start");

        // Reset restores defaults and clears the process state.
        session.ResetSession();
        Assert.AreEqual(74, session.Energy, "reset Energy");
        Assert.AreEqual(51, session.Food, "reset Food");
        Assert.AreEqual(20, session.Minerals, "reset Minerals");
        Assert.AreEqual(8, session.Fuel, "reset Fuel");
        Assert.AreEqual(5, session.Alloy, "reset Alloy");
        Assert.AreEqual(1, session.RefineryLevel, "reset RefineryLevel");
        Assert.AreEqual(GameSession.RefineryProcessState.Idle, session.ProcessState, "reset process Idle");
        Assert.AreEqual(0, session.PendingFuel, "reset clears pending fuel");
        Assert.AreEqual(0, session.PendingAlloy, "reset clears pending alloy");
    }

    /// <summary>
    /// The single scene-loading test: Boot -> Hub -> exactly one shared
    /// GameSession -> full UI journey (navigation, processing, refinery
    /// upgrade, reset) with cross-screen consistency and disabled-button
    /// states, now expressed through the batch refinery semantics.
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
        // Chinese-first Phase 04 copy: 就绪 = valid, unconfirmed package.
        app.ShowScreen(AppScreen.Preparation);
        yield return null;
        Assert.AreEqual(AppScreen.Preparation, app.CurrentScreen, "Preparation reachable");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen on Preparation");
        Assert.AreEqual("45 / 100", LabelText(root, ElementNames.CargoValue), "Preparation default cargo");
        Assert.AreEqual("就绪", LabelText(root, ElementNames.LoadoutStateLabel), "Preparation default status");

        // ---- Processing: batch flow + button states ----
        app.ShowScreen(AppScreen.Processing);
        yield return null;
        Assert.AreEqual(AppScreen.Processing, app.CurrentScreen, "Processing reachable");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen on Processing");
        Assert.AreEqual("20", LabelText(root, ElementNames.ProcessingMineralsValue), "Processing minerals before");

        Button startButton = root.Q<Button>(ElementNames.ProcessingStartButton);
        Assert.IsNotNull(startButton, "start button exists");
        Assert.IsTrue(startButton.enabledSelf, "start enabled while Minerals >= selected batch (5)");
        Assert.AreEqual("开始加工", startButton.text, "start label before running");

        Assert.IsTrue(session.TryStartProcess(5), "start batch 5 with 20 Minerals");
        Assert.AreEqual(15, session.Minerals, "session Minerals after starting batch 5");
        Assert.AreEqual(GameSession.RefineryProcessState.Running, session.ProcessState, "state Running");
        Assert.AreEqual("加工中", startButton.text, "start button label while processing");
        Assert.IsFalse(startButton.enabledSelf, "start disabled while processing");

        session.AdvanceProcessing(GameSession.ProcessDurationSeconds);
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "state Complete");
        Assert.AreEqual("15", LabelText(root, ElementNames.ProcessingMineralsValue), "Processing minerals after");
        Assert.AreEqual("10", LabelText(root, ElementNames.ProcessingFuelValue), "Processing fuel after");
        Assert.AreEqual("6", LabelText(root, ElementNames.ProcessingAlloyValue), "Processing alloy after");
        Assert.AreEqual("加工完成", LabelText(root, ElementNames.ProcessingStatusLabel), "status Complete");

        // ---- Overview reflects the processing result ----
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
        Assert.AreEqual("精炼模块 LV.1", LabelText(root, ElementNames.RefineryLevelValue), "Outpost refinery level before");

        Button upgradeButton = root.Q<Button>(ElementNames.UpgradeRefineryButton);
        Assert.IsNotNull(upgradeButton, "UpgradeRefineryButton must exist");
        Assert.IsTrue(upgradeButton.enabledSelf, "Upgrade button enabled while Alloy >= 3");

        Assert.IsTrue(session.TryUpgradeRefinery(), "upgrade should succeed with 6 Alloy");
        Assert.AreEqual(3, session.Alloy, "session Alloy after upgrade");
        Assert.AreEqual(2, session.RefineryLevel, "session RefineryLevel after upgrade");
        Assert.AreEqual("3", LabelText(root, ElementNames.OutpostAlloyValue), "Outpost Alloy after upgrade");
        Assert.AreEqual("精炼模块 LV.2", LabelText(root, ElementNames.RefineryLevelValue), "Outpost refinery level after");

        // Second upgrade (3 Alloy) drains Alloy to 0 -> level 3, button disabled.
        Assert.IsTrue(session.TryUpgradeRefinery(), "second upgrade succeeds (3 Alloy)");
        Assert.AreEqual("0", LabelText(root, ElementNames.OutpostAlloyValue), "Outpost Alloy drained");
        Assert.AreEqual("精炼模块 LV.3", LabelText(root, ElementNames.RefineryLevelValue), "Outpost refinery level after drain");
        Assert.IsFalse(upgradeButton.enabledSelf, "Upgrade button disabled while Alloy < 3");
        Assert.IsFalse(session.TryUpgradeRefinery(), "upgrade fails safely when Alloy < 3");
        Assert.AreEqual(3, session.RefineryLevel, "level unchanged after failed upgrade");

        // ---- Overview still consistent ----
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual("0", LabelText(root, ElementNames.AlloyValue), "Overview Alloy after upgrades");

        // ---- Insufficient Minerals: drain to 0, start disables ----
        // Minerals 15: batch 10 -> 5, then batch 5 -> 0; a further start fails.
        Assert.IsTrue(session.TryStartProcess(10), "drain batch 10");
        session.AdvanceProcessing(GameSession.ProcessDurationSeconds);
        Assert.AreEqual(5, session.Minerals, "Minerals after drain to 5");
        Assert.IsTrue(session.CanStartBatch(5), "batch 5 startable at Minerals == 5");

        app.ShowScreen(AppScreen.Processing);
        yield return null;
        Assert.IsTrue(startButton.enabledSelf, "start enabled at Minerals == 5 (batch 5)");

        Assert.IsTrue(session.TryStartProcess(5), "batch 5 at Minerals == 5 succeeds");
        session.AdvanceProcessing(GameSession.ProcessDurationSeconds);
        Assert.AreEqual(0, session.Minerals, "Minerals after drain to 0");
        Assert.AreEqual("原料不足", startButton.text, "start label when insufficient");
        Assert.IsFalse(startButton.enabledSelf, "start disabled at Minerals < 5");
        Assert.IsFalse(session.TryStartProcess(5), "start fails safely when Minerals < 5");
        Assert.AreEqual(0, session.Minerals, "Minerals unchanged on failed start");

        // ---- Reset restores defaults and refreshes every screen ----
        session.ResetSession();
        Assert.AreEqual(74, session.Energy, "session Energy after reset");
        Assert.AreEqual(51, session.Food, "session Food after reset");
        Assert.AreEqual(20, session.Minerals, "session Minerals after reset");
        Assert.AreEqual(8, session.Fuel, "session Fuel after reset");
        Assert.AreEqual(5, session.Alloy, "session Alloy after reset");
        Assert.AreEqual(1, session.RefineryLevel, "session RefineryLevel after reset");
        Assert.AreEqual(GameSession.RefineryProcessState.Idle, session.ProcessState, "session process Idle after reset");

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
