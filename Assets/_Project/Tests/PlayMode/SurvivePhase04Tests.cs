using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 04 Play Mode validation.
///
/// Covers (a) the Chinese-first language baseline on representative stable
/// elements of Overview / Preparation / Processing / navigation, and (b) the
/// authoritative refinery processing state: batches 5/10/15, expected and
/// settled outputs, insufficient minerals, double-start protection, reset
/// during processing (no stale settlement), and navigation persistence while
/// a process runs.
///
/// Single Boot -> Hub scene load per session (own -testFilter).
/// </summary>
public class SurvivePhase04Tests
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

    [UnityTest]
    public IEnumerator SessionState_Processing_Batches_Insufficient_DoubleStart_Reset()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        // Defaults: Idle, no pending outputs.
        Assert.AreEqual(20, session.Minerals, "default minerals");
        Assert.AreEqual(8, session.Fuel, "default fuel");
        Assert.AreEqual(5, session.Alloy, "default alloy");
        Assert.AreEqual(GameSession.RefineryProcessState.Idle, session.ProcessState, "default Idle");
        Assert.AreEqual(0, session.PendingFuel, "no pending fuel by default");
        Assert.AreEqual(0, session.PendingAlloy, "no pending alloy by default");
        yield return null;

        // Batch 5 (contract): start -> 15, running; complete -> fuel 10, alloy 6.
        Assert.IsTrue(session.CanStartBatch(5), "batch 5 startable at 20");
        Assert.IsTrue(session.TryStartProcess(5), "start batch 5");
        Assert.AreEqual(15, session.Minerals, "minerals after start batch 5");
        Assert.AreEqual(GameSession.RefineryProcessState.Running, session.ProcessState, "running");
        Assert.AreEqual(2, session.PendingFuel, "pending fuel batch 5");
        Assert.AreEqual(1, session.PendingAlloy, "pending alloy batch 5");
        Assert.IsTrue(session.ProcessProgress01 > 0f || session.ProcessRemainingSeconds > 0f,
            "timer armed");

        // Double start protection: no duplicate cost.
        Assert.IsFalse(session.CanStartBatch(10), "no batch startable while running");
        Assert.IsFalse(session.TryStartProcess(10), "double start rejected");
        Assert.AreEqual(15, session.Minerals, "no duplicate cost");
        Assert.AreEqual(GameSession.RefineryProcessState.Running, session.ProcessState, "still running");

        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "complete");
        Assert.AreEqual(10, session.Fuel, "fuel after batch 5");
        Assert.AreEqual(6, session.Alloy, "alloy after batch 5");
        Assert.AreEqual(15, session.Minerals, "minerals unchanged by settle");

        // Reset during processing: cancel, no stale settlement.
        session.ResetSession();
        Assert.IsTrue(session.TryStartProcess(10), "batch 10 from reset");
        Assert.AreEqual(10, session.Minerals, "minerals after start batch 10");
        session.ResetSession();
        Assert.AreEqual(20, session.Minerals, "reset restores minerals");
        Assert.AreEqual(8, session.Fuel, "reset restores fuel");
        Assert.AreEqual(5, session.Alloy, "reset restores alloy");
        Assert.AreEqual(GameSession.RefineryProcessState.Idle, session.ProcessState, "reset cancels processing");
        Assert.AreEqual(0, session.PendingFuel, "reset clears pending fuel");
        Assert.AreEqual(0, session.PendingAlloy, "reset clears pending alloy");
        session.AdvanceProcessing(30f);
        Assert.AreEqual(8, session.Fuel, "no stale fuel settlement after reset");
        Assert.AreEqual(5, session.Alloy, "no stale alloy settlement after reset");

        // Batch 10 from reset completes to fuel 12, alloy 7.
        Assert.IsTrue(session.TryStartProcess(10), "batch 10 (reset) start");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "batch10 complete");
        Assert.AreEqual(10, session.Minerals, "minerals after batch 10");
        Assert.AreEqual(12, session.Fuel, "fuel after batch 10 (+4)");
        Assert.AreEqual(7, session.Alloy, "alloy after batch 10 (+2)");

        // Batch 15 from reset completes to fuel 14, alloy 8.
        session.ResetSession();
        Assert.IsTrue(session.TryStartProcess(15), "batch 15 start");
        Assert.AreEqual(5, session.Minerals, "minerals after start batch 15");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.AreEqual(14, session.Fuel, "fuel after batch 15 (+6)");
        Assert.AreEqual(8, session.Alloy, "alloy after batch 15 (+3)");

        // Insufficient minerals: 10 and 15 unavailable, 5 still works.
        Assert.IsFalse(session.CanStartBatch(10), "batch 10 unavailable at minerals 5");
        Assert.IsFalse(session.TryStartProcess(10), "batch 10 start rejected");
        Assert.IsFalse(session.CanStartBatch(15), "batch 15 unavailable at minerals 5");
        Assert.IsTrue(session.CanStartBatch(5), "batch 5 startable at minerals 5");
        Assert.IsTrue(session.TryStartProcess(5), "batch 5 start accepted");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.AreEqual(0, session.Minerals, "minerals never negative");
        Assert.IsFalse(session.TryStartProcess(5), "no start at minerals 0");

        session.ResetSession();
        Assert.AreEqual(20, session.Minerals, "final reset minerals");
    }

    [UnityTest]
    public IEnumerator BootToHub_LanguageBaseline_AndProcessingJourney()
    {
        GameSession.Ensure().ResetSession();
        // Phase 07A.1: legacy Hub suites load the Hub explicitly.
        SceneManager.LoadScene(BootController.HubSceneName);
        yield return WaitUntil(() => ActiveSceneName() == BootController.HubSceneName);

        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });

        Assert.IsNotNull(app, "AppController not ready");
        VisualElement root = app.UiRoot;
        GameSession session = GameSession.Instance;
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        // ---- Chinese-first navigation labels ----
        Assert.AreEqual("总览", NavLabel(root, ElementNames.NavOverview), "nav overview 总览");
        Assert.AreEqual("出发准备", NavLabel(root, ElementNames.NavPreparation), "nav preparation 出发准备");
        Assert.AreEqual("资源加工", NavLabel(root, ElementNames.NavProcessing), "nav processing 资源加工");
        Assert.AreEqual("据点", NavLabel(root, ElementNames.NavOutpost), "nav outpost 据点");

        // ---- Overview representative Chinese labels ----
        Assert.AreEqual("舰船状态", PanelHeader(root, ElementNames.ShipStatusPanel), "overview ship status header");
        VisualElement destinationPanel = root.Q<VisualElement>(ElementNames.DestinationPanel);
        Label destinationEyebrow = destinationPanel.Q<Label>(null, "eyebrow");
        Assert.IsNotNull(destinationEyebrow, "overview destination eyebrow exists");
        Assert.AreEqual("当前目的地", destinationEyebrow.text, "overview destination eyebrow");
        Assert.IsTrue(LabelText(root, ElementNames.ObjectiveCopy).Contains("寻找可利用资源"),
            "overview objective copy chinese");
        Label ctaLabel = root.Q<Button>(ElementNames.OpenPreparationButton).Q<Label>(null, "cta-label");
        Assert.IsNotNull(ctaLabel, "overview CTA label exists");
        Assert.AreEqual("准备远征 »", ctaLabel.text, "overview CTA chinese");

        // ---- Preparation representative Chinese labels ----
        app.ShowScreen(AppScreen.Preparation);
        yield return null;
        yield return null;
        Assert.AreEqual("任务准备度", PanelHeader(root, ElementNames.ReadinessPanel), "preparation readiness header");
        Assert.AreEqual("就绪", LabelText(root, ElementNames.LoadoutStateLabel), "preparation default state 就绪");
        Button harvesterButton = root.Q<Button>(ElementNames.HarvesterRowButton);
        Label harvesterName = harvesterButton.Q<Label>(null, "equipment-name");
        Label harvesterChip = harvesterButton.Q<Label>(null, "equipment-chip");
        Assert.IsNotNull(harvesterName, "harvester name exists");
        Assert.IsNotNull(harvesterChip, "harvester chip exists");
        Assert.AreEqual("采集器 MK-I", harvesterName.text, "harvester chinese name");
        Assert.AreEqual("必需", harvesterChip.text, "harvester required chip");

        // ---- Processing: Chinese production UI + live flow ----
        app.ShowScreen(AppScreen.Processing);
        yield return null;
        yield return null;
        Label title = root.Q<Label>(null, "process-title");
        Assert.IsNotNull(title, "processing title exists");
        Assert.AreEqual("资源加工", title.text, "processing title chinese");
        Assert.AreEqual("20", LabelText(root, ElementNames.ProcessingMineralsValue), "storage minerals default");
        Assert.AreEqual("8", LabelText(root, ElementNames.ProcessingFuelValue), "inventory fuel default");
        Assert.AreEqual("5", LabelText(root, ElementNames.ProcessingAlloyValue), "inventory alloy default");

        // Default UI selection is batch 5 (visible in the selector buttons).
        Assert.AreEqual("05", root.Q<Button>(ElementNames.Batch5Button).text, "batch5 label");
        Assert.AreEqual("10", root.Q<Button>(ElementNames.Batch10Button).text, "batch10 label");
        Assert.AreEqual("15", root.Q<Button>(ElementNames.Batch15Button).text, "batch15 label");
        Assert.IsTrue(root.Q<Button>(ElementNames.Batch5Button).ClassListContains("batch-selected"),
            "batch 5 selected by default");

        Button start = root.Q<Button>(ElementNames.ProcessingStartButton);
        Assert.AreEqual("开始加工", start.text, "start label ready");
        Assert.AreEqual("预计产出", LabelText(root, ElementNames.ProcessingOutputCaption), "output caption ready");

        Assert.IsTrue(session.TryStartProcess(5), "start batch 5");
        yield return null;
        Assert.AreEqual("15", LabelText(root, ElementNames.ProcessingMineralsValue), "storage after start batch 5");
        Assert.AreEqual("加工中", LabelText(root, ElementNames.ProcessingStatusLabel), "status 加工中");
        Assert.AreEqual("加工中", start.text, "start label 加工中");
        Assert.IsFalse(start.enabledSelf, "start disabled while processing");
        Assert.AreEqual("+2", LabelText(root, ElementNames.ProcessingFuelGainValue), "expected fuel +2 preview");
        Assert.AreEqual("+1", LabelText(root, ElementNames.ProcessingAlloyGainValue), "expected alloy +1 preview");

        // Progress tick path (deterministic advance).
        session.AdvanceProcessing(session.RefineryProcessSeconds * 0.5f);
        Assert.IsTrue(session.ProcessProgress01 > 0f, "progress advanced after half duration");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        yield return null;
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "complete");
        Assert.AreEqual("加工完成", LabelText(root, ElementNames.ProcessingStatusLabel), "status 加工完成");
        Assert.AreEqual("10", LabelText(root, ElementNames.ProcessingFuelValue), "inventory fuel after batch 5");
        Assert.AreEqual("6", LabelText(root, ElementNames.ProcessingAlloyValue), "inventory alloy after batch 5");
        Assert.AreEqual("本次产出", LabelText(root, ElementNames.ProcessingOutputCaption), "settled caption 本次产出");
        Assert.AreEqual("再次加工", start.text, "start label 再次加工");
        Assert.IsTrue(start.enabledSelf, "restart enabled after completion");

        // ---- Navigation persistence during a running process ----
        Assert.IsTrue(session.TryStartProcess(5), "start batch 5 again");
        Assert.AreEqual(GameSession.RefineryProcessState.Running, session.ProcessState, "running batch 5");
        Assert.AreEqual(10, session.Minerals, "minerals after second batch start");
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual("10", LabelText(root, ElementNames.MineralsValue), "overview minerals reflect running batch");
        app.ShowScreen(AppScreen.Preparation);
        yield return null;
        app.ShowScreen(AppScreen.Processing);
        yield return null;
        Assert.IsTrue(session.IsProcessing, "processing still running after navigation");
        Assert.AreEqual(10, session.Minerals, "no state duplication across navigation");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        yield return null;
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "complete after return");
        Assert.AreEqual("12", LabelText(root, ElementNames.ProcessingFuelValue), "fuel after second batch 5");

        // ---- Reset integration through the UI ----
        session.ResetSession();
        yield return null;
        Assert.AreEqual("20", LabelText(root, ElementNames.ProcessingMineralsValue), "reset restores storage UI");
        Assert.AreEqual("准备就绪", LabelText(root, ElementNames.ProcessingStatusLabel), "reset restores status UI");
        Assert.AreEqual("开始加工", start.text, "start label back to 开始加工");
        Assert.IsTrue(start.enabledSelf, "start enabled after reset");
    }

    private static string NavLabel(VisualElement root, string navName)
    {
        Button nav = root.Q<Button>(navName);
        Assert.IsNotNull(nav, "nav button " + navName);
        Label label = nav.Q<Label>(null, "nav-label");
        Assert.IsNotNull(label, "nav label " + navName);
        return label.text;
    }

    private static string PanelHeader(VisualElement root, string containerName)
    {
        VisualElement container = root.Q<VisualElement>(containerName);
        Assert.IsNotNull(container, "panel " + containerName);
        return FirstLabelText(container, null, "panel-header-text");
    }

    private static string FirstLabelText(VisualElement container, string elementName, string className)
    {
        Label label = container.Q<Label>(elementName, className);
        Assert.IsNotNull(label, "label " + elementName + "/" + className);
        return label.text;
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }
}
