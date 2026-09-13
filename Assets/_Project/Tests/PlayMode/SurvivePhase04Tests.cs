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
/// authoritative refinery processing state: the legal run-size range, expected
/// and settled outputs, insufficient minerals, double-start protection, reset
/// during processing (no stale settlement), and navigation persistence while
/// a run is in progress.
///
/// Phase 09B PASS 5 revised the yield contract (1 mineral -> 1 Alloy + 1 Fuel)
/// and replaced the fixed 5/10/15 batch buttons with the page's quantity
/// control; this suite asserts the new contract, and the page's controller
/// behaviour is covered by SurvivePhase09BProcessingTests.
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
    public IEnumerator SessionState_Processing_Quantity_Insufficient_DoubleStart_Reset()
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

        // ---- Yield contract (Phase 09B PASS 5) ----
        // One mineral refines into one Alloy (primary) and one Fuel (byproduct).
        Assert.AreEqual(1, GameSession.AlloyGainFor(1), "alloy per mineral");
        Assert.AreEqual(1, GameSession.FuelGainFor(1), "fuel per mineral");
        Assert.AreEqual(7, GameSession.AlloyGainFor(7), "alloy scales with the run size");
        Assert.AreEqual(7, GameSession.FuelGainFor(7), "fuel scales with the run size");

        // ---- Legal run sizes: 1..15, always capped by the real stock ----
        Assert.IsTrue(GameSession.IsValidBatch(1), "1 mineral is a legal run");
        Assert.IsTrue(GameSession.IsValidBatch(15), "15 minerals is a legal run");
        Assert.IsFalse(GameSession.IsValidBatch(0), "0 is not a run");
        Assert.IsFalse(GameSession.IsValidBatch(16), "16 exceeds the module range");
        Assert.AreEqual(15, session.MaxStartableBatch, "stock 20 caps at the module maximum");
        Assert.IsTrue(session.CanStartBatch(1), "any quantity is startable at full stock");
        Assert.IsTrue(session.CanStartBatch(15), "the module maximum is startable at full stock");

        // ---- A 3 mineral run: start -> 17, running; complete -> fuel 11, alloy 8 ----
        Assert.IsTrue(session.TryStartProcess(3), "start a 3 mineral run");
        Assert.AreEqual(17, session.Minerals, "minerals after start");
        Assert.AreEqual(GameSession.RefineryProcessState.Running, session.ProcessState, "running");
        Assert.AreEqual(3, session.PendingFuel, "pending fuel for 3 minerals");
        Assert.AreEqual(3, session.PendingAlloy, "pending alloy for 3 minerals");
        Assert.IsTrue(session.ProcessProgress01 > 0f || session.ProcessRemainingSeconds > 0f,
            "timer armed");

        // Double start protection: no duplicate cost.
        Assert.IsFalse(session.CanStartBatch(1), "no run startable while running");
        Assert.IsFalse(session.TryStartProcess(1), "double start rejected");
        Assert.AreEqual(17, session.Minerals, "no duplicate cost");
        Assert.AreEqual(GameSession.RefineryProcessState.Running, session.ProcessState, "still running");

        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "complete");
        Assert.AreEqual(11, session.Fuel, "fuel after a 3 mineral run (8 + 3)");
        Assert.AreEqual(8, session.Alloy, "alloy after a 3 mineral run (5 + 3)");
        Assert.AreEqual(17, session.Minerals, "minerals unchanged by settle");

        // Reset during processing: cancel, no stale settlement.
        session.ResetSession();
        Assert.IsTrue(session.TryStartProcess(10), "a 10 mineral run from reset");
        Assert.AreEqual(10, session.Minerals, "minerals after starting 10");
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

        // ---- The module maximum (15) from reset ----
        Assert.IsTrue(session.TryStartProcess(15), "a full 15 mineral run");
        Assert.AreEqual(5, session.Minerals, "minerals after starting 15");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "15 run complete");
        Assert.AreEqual(23, session.Fuel, "fuel after 15 (8 + 15)");
        Assert.AreEqual(20, session.Alloy, "alloy after 15 (5 + 15)");

        // ---- Insufficient minerals: the quantity can never exceed the stock ----
        Assert.AreEqual(5, session.MaxStartableBatch, "stock 5 caps the run size");
        Assert.IsFalse(session.CanStartBatch(6), "6 minerals are not startable at stock 5");
        Assert.IsFalse(session.TryStartProcess(6), "an over-stock run is rejected");
        Assert.IsTrue(session.CanStartBatch(5), "all of the remaining stock is startable");
        Assert.IsTrue(session.TryStartProcess(5), "run the remaining 5");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        Assert.AreEqual(0, session.Minerals, "minerals never negative");
        Assert.AreEqual(0, session.MaxStartableBatch, "nothing is startable at 0 stock");
        Assert.IsFalse(session.CanStartBatch(1), "no run at 0 minerals");
        Assert.IsFalse(session.TryStartProcess(1), "a run at 0 minerals is rejected");
        Assert.AreEqual(0, session.Minerals, "minerals unchanged by the rejected run");

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

        // ---- Processing: recipe + quantity composition with live flow ----
        app.ShowScreen(AppScreen.Processing);
        yield return null;
        yield return null;
        Label title = root.Q<Label>(null, "process-title");
        Assert.IsNotNull(title, "processing title exists");
        Assert.AreEqual("资源加工", title.text, "processing title chinese");
        Assert.AreEqual("20", LabelText(root, ElementNames.ProcessingMineralsValue), "storage minerals default");
        Assert.AreEqual("8", LabelText(root, ElementNames.ProcessingFuelValue), "inventory fuel default");
        Assert.AreEqual("5", LabelText(root, ElementNames.ProcessingAlloyValue), "inventory alloy default");

        // The recipe list shows the one real recipe and the quantity control
        // opens on the legal minimum (the old 5/10/15 buttons are gone).
        Assert.AreEqual("合金", LabelText(root, ElementNames.ProcessingRecipeName), "recipe name");
        Assert.AreEqual("矿物 → 合金", LabelText(root, ElementNames.ProcessingRecipeFlow), "recipe flow");
        Assert.AreEqual("01", LabelText(root, ElementNames.ProcessingQuantityValue), "quantity opens at the minimum");
        Assert.IsNotNull(root.Q<Button>(ElementNames.ProcessingQuantityDown), "quantity - exists");
        Assert.IsNotNull(root.Q<Button>(ElementNames.ProcessingQuantityUp), "quantity + exists");

        Button start = root.Q<Button>(ElementNames.ProcessingStartButton);
        Assert.AreEqual("确认加工", start.text, "confirm label ready");
        Assert.IsTrue(start.enabledSelf, "confirm enabled with stock");
        Assert.AreEqual("预计产出", LabelText(root, ElementNames.ProcessingOutputCaption), "output caption ready");
        Assert.AreEqual("+1", LabelText(root, ElementNames.ProcessingFuelGainValue), "expected fuel +1 preview");
        Assert.AreEqual("+1", LabelText(root, ElementNames.ProcessingAlloyGainValue), "expected alloy +1 preview");

        // A run started anywhere (page, probe or smoke) becomes the page's own
        // selection, so the control and the reported result can never disagree.
        Assert.IsTrue(session.TryStartProcess(5), "start a 5 mineral run");
        yield return null;
        Assert.AreEqual("15", LabelText(root, ElementNames.ProcessingMineralsValue), "storage after start");
        Assert.AreEqual("05", LabelText(root, ElementNames.ProcessingQuantityValue), "control follows the live run");
        Assert.AreEqual("加工中", LabelText(root, ElementNames.ProcessingStatusLabel), "status 加工中");
        Assert.AreEqual("加工中", start.text, "confirm label 加工中");
        Assert.IsFalse(start.enabledSelf, "confirm disabled while processing");
        Assert.AreEqual("+5", LabelText(root, ElementNames.ProcessingFuelGainValue), "expected fuel follows the run");
        Assert.AreEqual("+5", LabelText(root, ElementNames.ProcessingAlloyGainValue), "expected alloy follows the run");

        // Progress tick path (deterministic advance).
        session.AdvanceProcessing(session.RefineryProcessSeconds * 0.5f);
        Assert.IsTrue(session.ProcessProgress01 > 0f, "progress advanced after half duration");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        yield return null;
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "complete");
        Assert.AreEqual("加工完成", LabelText(root, ElementNames.ProcessingStatusLabel), "status 加工完成");
        Assert.AreEqual("13", LabelText(root, ElementNames.ProcessingFuelValue), "inventory fuel after the run");
        Assert.AreEqual("10", LabelText(root, ElementNames.ProcessingAlloyValue), "inventory alloy after the run");
        Assert.AreEqual("本次产出", LabelText(root, ElementNames.ProcessingOutputCaption), "settled caption 本次产出");
        Assert.AreEqual("+5", LabelText(root, ElementNames.ProcessingFuelGainValue), "settled fuel output");
        Assert.AreEqual("+5", LabelText(root, ElementNames.ProcessingAlloyGainValue), "settled alloy output");
        Assert.AreEqual("确认加工", start.text, "confirm label back to 确认加工");
        Assert.IsTrue(start.enabledSelf, "confirm enabled after completion");

        // ---- Navigation persistence during a running process ----
        Assert.IsTrue(session.TryStartProcess(5), "start a second 5 mineral run");
        Assert.AreEqual(GameSession.RefineryProcessState.Running, session.ProcessState, "running again");
        Assert.AreEqual(10, session.Minerals, "minerals after the second start");
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual("10", LabelText(root, ElementNames.MineralsValue), "overview minerals reflect the run");
        app.ShowScreen(AppScreen.Preparation);
        yield return null;
        app.ShowScreen(AppScreen.Processing);
        yield return null;
        Assert.IsTrue(session.IsProcessing, "processing still running after navigation");
        Assert.AreEqual(10, session.Minerals, "no state duplication across navigation");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        yield return null;
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "complete after return");
        Assert.AreEqual("18", LabelText(root, ElementNames.ProcessingFuelValue), "fuel after the second run");

        // ---- Reset integration through the UI ----
        session.ResetSession();
        yield return null;
        Assert.AreEqual("20", LabelText(root, ElementNames.ProcessingMineralsValue), "reset restores storage UI");
        // The control keeps the player's last choice, but it is always a legal
        // one: inside [1, min(module maximum, stock)].
        int resetQuantity = int.Parse(LabelText(root, ElementNames.ProcessingQuantityValue));
        Assert.GreaterOrEqual(resetQuantity, GameSession.ProcessBatchMin, "quantity stays legal after reset");
        Assert.LessOrEqual(resetQuantity, session.MaxStartableBatch, "quantity stays inside the stock");
        Assert.AreEqual("准备就绪", LabelText(root, ElementNames.ProcessingStatusLabel), "reset restores status UI");
        Assert.AreEqual("确认加工", start.text, "confirm label after reset");
        Assert.IsTrue(start.enabledSelf, "confirm enabled after reset");
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
