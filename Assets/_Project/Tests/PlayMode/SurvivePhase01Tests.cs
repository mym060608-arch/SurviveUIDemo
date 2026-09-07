using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 01 Play Mode validation (kept green under Phase 04/05 semantics).
///
/// Intentional semantic migrations documented:
/// - Phase 04: instant "PROCESS 5" replaced by the batch refinery flow
///   (conversion ratio preserved: 5 Minerals -> +2 Fuel / +1 Alloy).
/// - Phase 05: the flat "3 Alloy per refinery level" upgrade was replaced by
///   the module economy (Refinery cost = level * 3, LV.3 cap, Core
///   prerequisite). Refinery processing duration is now derived from level.
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
        Assert.AreEqual(1, session.OutpostLevel, "default OutpostLevel");
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

        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "state Complete");
        Assert.AreEqual(25, session.Minerals, "Minerals unchanged by completion");
        Assert.AreEqual(10, session.Fuel, "Fuel after batch 5 (+2)");
        Assert.AreEqual(6, session.Alloy, "Alloy after batch 5 (+1)");

        // Refinery LV.1 -> LV.2 costs 3 Alloy.
        Assert.AreEqual(GameSession.UpgradeFailureReason.None, session.TryUpgrade(GameSession.OutpostModule.Refinery),
            "refinery upgrade succeeds");
        Assert.AreEqual(3, session.Alloy, "Alloy after refinery upgrade");
        Assert.AreEqual(2, session.RefineryLevel, "RefineryLevel after upgrade");

        // LV.2 -> LV.3 is blocked by Core LV.1 (cap LV.2); state untouched.
        Assert.AreEqual(GameSession.UpgradeFailureReason.CoreLevelRequired,
            session.CanUpgrade(GameSession.OutpostModule.Refinery), "LV.3 needs Core LV.2");
        Assert.AreEqual(GameSession.UpgradeFailureReason.CoreLevelRequired,
            session.TryUpgrade(GameSession.OutpostModule.Refinery), "blocked upgrade returns reason");
        Assert.AreEqual(3, session.Alloy, "no Alloy deducted on blocked upgrade");
        Assert.AreEqual(2, session.RefineryLevel, "level unchanged on blocked upgrade");

        // Core upgrade needs 5 Alloy -> insufficient here, still 3.
        Assert.AreEqual(GameSession.UpgradeFailureReason.InsufficientAlloy,
            session.CanUpgrade(GameSession.OutpostModule.Core), "core cost 5 > alloy 3");
        Assert.AreEqual(3, session.Alloy, "Alloy unchanged by rejected core upgrade");

        // Drain Minerals 25 -> 0 with supported batches; failing start keeps 0.
        Assert.IsTrue(session.TryStartProcess(15), "drain batch 15");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.AreEqual(10, session.Minerals, "Minerals after 15 batch");
        Assert.IsTrue(session.TryStartProcess(10), "drain batch 10");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
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
        Assert.AreEqual(1, session.OutpostLevel, "reset OutpostLevel");
        Assert.AreEqual(GameSession.RefineryProcessState.Idle, session.ProcessState, "reset process Idle");
        Assert.AreEqual(0, session.PendingFuel, "reset clears pending fuel");
        Assert.AreEqual(0, session.PendingAlloy, "reset clears pending alloy");
    }

    /// <summary>
    /// The single scene-loading test: Boot -> Hub -> exactly one shared
    /// GameSession -> full UI journey (navigation, processing, module
    /// upgrade, reset) with cross-screen consistency and disabled-button
    /// states, expressed through the Phase 04 batch + Phase 05 economy.
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

        session.AdvanceProcessing(session.RefineryProcessSeconds);
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

        // ---- Outpost: module topology + refinery upgrade economy ----
        app.ShowScreen(AppScreen.Outpost);
        yield return null;
        yield return null;
        Assert.AreEqual(AppScreen.Outpost, app.CurrentScreen, "Outpost reachable");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen on Outpost");
        Assert.AreEqual("6", LabelText(root, ElementNames.OutpostAlloyValue), "Outpost Alloy before upgrade");

        // Default selection is Refinery, detail visible and ready.
        Button refineryNode = root.Q<Button>(ElementNames.RefineryModuleNode);
        Button coreNode = root.Q<Button>(ElementNames.CoreModuleNode);
        Button harvesterNode = root.Q<Button>(ElementNames.HarvesterModuleNode);
        Assert.IsNotNull(refineryNode, "refinery node exists");
        Assert.IsNotNull(coreNode, "core node exists");
        Assert.IsNotNull(harvesterNode, "harvester node exists");
        Assert.IsTrue(refineryNode.ClassListContains("node-selected"), "refinery selected by default");
        Assert.AreEqual("精炼模块", LabelText(root, ElementNames.ModuleDetailName), "detail shows refinery");

        Button upgradeButton = root.Q<Button>(ElementNames.OutpostUpgradeButton);
        Assert.IsNotNull(upgradeButton, "upgrade button exists");
        Assert.AreEqual("LV.1", LabelText(root, ElementNames.RefineryLevelValue), "Outpost refinery level before");
        Assert.IsTrue(upgradeButton.enabledSelf, "upgrade enabled (alloy 6 >= cost 3)");

        Assert.AreEqual(GameSession.UpgradeFailureReason.None, session.TryUpgrade(GameSession.OutpostModule.Refinery),
            "refinery upgrade succeeds");
        yield return null;
        Assert.AreEqual(3, session.Alloy, "session Alloy after refinery upgrade");
        Assert.AreEqual(2, session.RefineryLevel, "session RefineryLevel after upgrade");
        Assert.AreEqual("3", LabelText(root, ElementNames.OutpostAlloyValue), "Outpost Alloy after upgrade");
        Assert.AreEqual("LV.2", LabelText(root, ElementNames.RefineryLevelValue), "Outpost refinery level after");
        Assert.AreEqual("1.6s", LabelText(root, ElementNames.ModuleCurrentEffectValue), "detail duration updated");

        // LV.2 -> LV.3 blocked by Core LV.1: CTA disabled with clear copy.
        Assert.AreEqual(GameSession.UpgradeFailureReason.CoreLevelRequired,
            session.CanUpgrade(GameSession.OutpostModule.Refinery), "LV.3 blocked by core");
        Assert.AreEqual("需要核心升级", upgradeButton.text, "CTA shows core requirement");
        Assert.IsFalse(upgradeButton.enabledSelf, "CTA disabled when core-locked");

        // ---- Overview still consistent (alloy 3, refinery LV.2) ----
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual("3", LabelText(root, ElementNames.AlloyValue), "Overview Alloy after upgrades");

        // ---- Insufficient Minerals: drain to 0, start disables ----
        // Minerals 15: batch 10 -> 5, then batch 5 -> 0; a further start fails.
        Assert.IsTrue(session.TryStartProcess(10), "drain batch 10");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.AreEqual(5, session.Minerals, "Minerals after drain to 5");
        Assert.IsTrue(session.CanStartBatch(5), "batch 5 startable at Minerals == 5");

        app.ShowScreen(AppScreen.Processing);
        yield return null;
        Assert.IsTrue(startButton.enabledSelf, "start enabled at Minerals == 5 (batch 5)");

        Assert.IsTrue(session.TryStartProcess(5), "batch 5 at Minerals == 5 succeeds");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
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
        Assert.AreEqual(1, session.OutpostLevel, "session OutpostLevel after reset");
        Assert.AreEqual(1, session.HarvesterLevel, "session HarvesterLevel after reset");
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
