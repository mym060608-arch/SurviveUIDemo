using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 05 Play Mode validation for the Outpost module economy.
///
/// Covers: authoritative derived values, deterministic costs, Core
/// prerequisite, max-level protection, insufficient Alloy, Harvester
/// efficiency, Refinery duration integration into Processing, Reset, node
/// selection/detail binding and cross-screen Alloy consistency.
///
/// Single Boot -> Hub load per session (own -testFilter).
/// </summary>
public class SurvivePhase05Tests
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

    private static void AssertNearly(float expected, float actual, string label)
    {
        Assert.IsTrue(Mathf.Abs(expected - actual) < 0.001f,
            label + " expected " + expected + " but was " + actual);
    }

    [UnityTest]
    public IEnumerator SessionState_OutpostModule_UpgradeRules()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        // Derived defaults.
        Assert.AreEqual(100, session.HarvesterEfficiencyPercent, "default efficiency 100%");
        AssertNearly(2.0f, session.RefineryProcessSeconds, "default duration 2.0");
        Assert.AreEqual(2, session.SubsystemLevelCap, "default cap LV.2");
        yield return null;

        // Harvester LV.1 -> LV.2 costs 3, efficiency 125%.
        Assert.AreEqual(3, session.GetUpgradeCost(GameSession.OutpostModule.Harvester), "harvester cost");
        Assert.AreEqual(GameSession.UpgradeFailureReason.None, session.CanUpgrade(GameSession.OutpostModule.Harvester),
            "harvester startable");
        Assert.AreEqual(GameSession.UpgradeFailureReason.None, session.TryUpgrade(GameSession.OutpostModule.Harvester),
            "harvester upgrade");
        Assert.AreEqual(2, session.Alloy, "alloy 5 - 3");
        Assert.AreEqual(2, session.HarvesterLevel, "harvester LV.2");
        Assert.AreEqual(125, session.HarvesterEfficiencyPercent, "efficiency 125%");

        // LV.2 -> LV.3 blocked by Core LV.1, nothing deducted.
        Assert.AreEqual(GameSession.UpgradeFailureReason.CoreLevelRequired,
            session.CanUpgrade(GameSession.OutpostModule.Harvester), "harvester LV.3 core lock");
        Assert.AreEqual(GameSession.UpgradeFailureReason.CoreLevelRequired,
            session.TryUpgrade(GameSession.OutpostModule.Harvester), "locked upgrade reason");
        Assert.AreEqual(2, session.Alloy, "no deduction on lock");
        Assert.AreEqual(2, session.HarvesterLevel, "harvester unchanged");

        // Refinery upgrade speeds Processing, output ratio untouched.
        session.ResetSession();
        Assert.AreEqual(GameSession.UpgradeFailureReason.None,
            session.TryUpgrade(GameSession.OutpostModule.Refinery), "refinery upgrade");
        Assert.AreEqual(2, session.RefineryLevel, "refinery LV.2");
        AssertNearly(1.6f, session.RefineryProcessSeconds, "duration 1.6 at LV.2");
        Assert.AreEqual(GameSession.FuelGainFor(10), 4, "fuel ratio unchanged");
        Assert.AreEqual(GameSession.AlloyGainFor(10), 2, "alloy ratio unchanged");

        // Core upgrade raises subsystem cap.
        session.ResetSession();
        Assert.AreEqual(5, session.GetUpgradeCost(GameSession.OutpostModule.Core), "core cost");
        Assert.AreEqual(GameSession.UpgradeFailureReason.None, session.TryUpgrade(GameSession.OutpostModule.Core),
            "core upgrade");
        Assert.AreEqual(0, session.Alloy, "alloy after core upgrade");
        Assert.AreEqual(2, session.OutpostLevel, "core LV.2");
        Assert.AreEqual(3, session.SubsystemLevelCap, "cap LV.3 after core LV.2");

        // Insufficient Alloy: leave 2, attempt a 3-cost module.
        session.ResetSession();
        session.TryUpgrade(GameSession.OutpostModule.Harvester); // alloy -> 2
        Assert.AreEqual(GameSession.UpgradeFailureReason.InsufficientAlloy,
            session.CanUpgrade(GameSession.OutpostModule.Refinery), "refinery lacks alloy");
        Assert.AreEqual(GameSession.UpgradeFailureReason.InsufficientAlloy,
            session.TryUpgrade(GameSession.OutpostModule.Refinery), "refinery rejected");
        Assert.AreEqual(2, session.Alloy, "alloy unchanged");
        Assert.AreEqual(1, session.RefineryLevel, "refinery unchanged");

        // Prerequisite with enough Alloy still blocked.
        session.ResetSession();
        session.TryUpgrade(GameSession.OutpostModule.Refinery); // LV.2, alloy 2
        session.AddAlloy(5); // alloy 7
        Assert.AreEqual(GameSession.UpgradeFailureReason.CoreLevelRequired,
            session.CanUpgrade(GameSession.OutpostModule.Refinery), "refinery LV.3 core lock");
        Assert.AreEqual(7, session.Alloy, "alloy unchanged by lock");

        // Full progression: Core LV.2 unlocks subsystems to LV.3.
        session.ResetSession();
        session.AddAlloy(18); // 23
        Assert.AreEqual(GameSession.UpgradeFailureReason.None,
            session.TryUpgrade(GameSession.OutpostModule.Core), "core -> LV.2");          // 18
        Assert.AreEqual(GameSession.UpgradeFailureReason.None,
            session.TryUpgrade(GameSession.OutpostModule.Harvester), "harvester -> LV.2"); // 15
        Assert.AreEqual(GameSession.UpgradeFailureReason.None,
            session.TryUpgrade(GameSession.OutpostModule.Harvester), "harvester -> LV.3"); // 9
        Assert.AreEqual(GameSession.UpgradeFailureReason.None,
            session.TryUpgrade(GameSession.OutpostModule.Refinery), "refinery -> LV.2");   // 6
        Assert.AreEqual(GameSession.UpgradeFailureReason.None,
            session.TryUpgrade(GameSession.OutpostModule.Refinery), "refinery -> LV.3");   // 0
        Assert.AreEqual(3, session.HarvesterLevel, "harvester LV.3");
        Assert.AreEqual(3, session.RefineryLevel, "refinery LV.3");
        Assert.AreEqual(150, session.HarvesterEfficiencyPercent, "efficiency 150%");
        AssertNearly(1.2f, session.RefineryProcessSeconds, "duration 1.2 at LV.3");
        Assert.AreEqual(0, session.Alloy, "alloy spent");

        // Max level protection: no further upgrade, no negative alloy.
        Assert.AreEqual(GameSession.UpgradeFailureReason.MaxLevel,
            session.CanUpgrade(GameSession.OutpostModule.Refinery), "refinery max");
        Assert.AreEqual(GameSession.UpgradeFailureReason.MaxLevel,
            session.TryUpgrade(GameSession.OutpostModule.Harvester), "harvester max");
        Assert.AreEqual(0, session.Alloy, "no negative alloy");

        // Reset restores defaults and derived values.
        session.ResetSession();
        Assert.AreEqual(1, session.OutpostLevel, "reset core");
        Assert.AreEqual(1, session.HarvesterLevel, "reset harvester");
        Assert.AreEqual(1, session.RefineryLevel, "reset refinery");
        Assert.AreEqual(5, session.Alloy, "reset alloy");
        Assert.AreEqual(100, session.HarvesterEfficiencyPercent, "reset efficiency");
        AssertNearly(2.0f, session.RefineryProcessSeconds, "reset duration");
        Assert.AreEqual(2, session.SubsystemLevelCap, "reset cap");
    }

    [UnityTest]
    public IEnumerator BootToHub_OutpostChineseJourney_EconomyAndProcessingIntegration()
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

        // ---- Outpost Chinese baseline + default selection (Refinery) ----
        app.ShowScreen(AppScreen.Outpost);
        yield return null;
        yield return null;
        Assert.AreEqual(AppScreen.Outpost, app.CurrentScreen, "outpost active");
        Assert.AreEqual(1, app.VisibleScreenCount, "one visible screen");

        Button coreNode = root.Q<Button>(ElementNames.CoreModuleNode);
        Button harvesterNode = root.Q<Button>(ElementNames.HarvesterModuleNode);
        Button refineryNode = root.Q<Button>(ElementNames.RefineryModuleNode);
        Assert.IsNotNull(coreNode, "core node");
        Assert.IsNotNull(harvesterNode, "harvester node");
        Assert.IsNotNull(refineryNode, "refinery node");

        Assert.IsTrue(refineryNode.ClassListContains("node-selected"), "refinery selected by default");
        Assert.AreEqual("核心模块", coreNode.Q<Label>(null, "node-title").text, "core node title");
        Assert.AreEqual("采集模块", harvesterNode.Q<Label>(null, "node-title").text, "harvester node title");
        Assert.AreEqual("精炼模块", refineryNode.Q<Label>(null, "node-title").text, "refinery node title");
        Assert.AreEqual("LV.1", LabelText(root, ElementNames.OutpostLevelValue), "status core LV.1");
        Assert.AreEqual("LV.1", LabelText(root, ElementNames.RefineryLevelValue), "status refinery LV.1");
        Assert.AreEqual("LV.2", LabelText(root, ElementNames.OutpostCapValue), "module cap LV.2");
        Assert.AreEqual("5", LabelText(root, ElementNames.OutpostAlloyValue), "alloy stock live");

        // Detail panel mirrors the selected refinery module.
        Assert.AreEqual("精炼模块", LabelText(root, ElementNames.ModuleDetailName), "detail name refinery");
        Assert.AreEqual("LV.1 → LV.2", LabelText(root, ElementNames.ModuleDetailLevels), "detail levels");
        Assert.AreEqual("加工周期", LabelText(root, ElementNames.ModuleDetailEffectTitle), "detail effect title");
        Assert.AreEqual("2.0s", LabelText(root, ElementNames.ModuleCurrentEffectValue), "detail current duration");
        Assert.AreEqual("1.6s", LabelText(root, ElementNames.ModuleNextEffectValue), "detail next duration");
        Assert.AreEqual("3 合金", LabelText(root, ElementNames.ModuleUpgradeCostValue), "detail cost");
        Button upgrade = root.Q<Button>(ElementNames.OutpostUpgradeButton);
        Assert.AreEqual("升级模块", upgrade.text, "CTA default");
        Assert.IsTrue(upgrade.enabledSelf, "CTA enabled at default");

        // ---- Node selection updates the detail panel (presentation only) ----
        app.OutpostView.SelectModule(GameSession.OutpostModule.Harvester);
        Assert.IsTrue(harvesterNode.ClassListContains("node-selected"), "harvester selected");
        Assert.IsFalse(refineryNode.ClassListContains("node-selected"), "refinery deselected");
        Assert.AreEqual("采集模块", LabelText(root, ElementNames.ModuleDetailName), "detail name harvester");
        Assert.AreEqual("采集效率", LabelText(root, ElementNames.ModuleDetailEffectTitle), "harvester effect title");
        Assert.AreEqual("100%", LabelText(root, ElementNames.ModuleCurrentEffectValue), "harvester current effect");
        Assert.AreEqual("125%", LabelText(root, ElementNames.ModuleNextEffectValue), "harvester next effect");

        app.OutpostView.SelectModule(GameSession.OutpostModule.Core);
        Assert.AreEqual("核心模块", LabelText(root, ElementNames.ModuleDetailName), "detail name core");
        Assert.AreEqual("模块等级上限", LabelText(root, ElementNames.ModuleDetailEffectTitle), "core effect title");
        Assert.AreEqual("LV.2", LabelText(root, ElementNames.ModuleCurrentEffectValue), "core current cap");
        Assert.AreEqual("LV.3", LabelText(root, ElementNames.ModuleNextEffectValue), "core next cap");

        // ---- Refinery upgrade: real economy, live Alloy, faster Processing ----
        app.OutpostView.SelectModule(GameSession.OutpostModule.Refinery);
        Assert.AreEqual(GameSession.UpgradeFailureReason.None,
            session.TryUpgrade(GameSession.OutpostModule.Refinery), "refinery LV.2 upgrade");
        yield return null;
        Assert.AreEqual("2", LabelText(root, ElementNames.OutpostAlloyValue), "alloy stock after upgrade");
        Assert.AreEqual("LV.2", LabelText(root, ElementNames.RefineryLevelValue), "status refinery LV.2");
        Assert.AreEqual("精炼模块", LabelText(root, ElementNames.ModuleDetailName), "detail remains refinery");
        Assert.AreEqual("1.6s", LabelText(root, ElementNames.ModuleCurrentEffectValue), "detail duration after LV.2");
        Assert.AreEqual("需要核心升级", upgrade.text, "CTA shows core requirement at LV.2->LV.3");
        Assert.IsFalse(upgrade.enabledSelf, "CTA disabled by core lock");
        AssertNearly(1.6f, session.RefineryProcessSeconds, "authoritative duration 1.6");

        // Cross-screen: Processing reports LV.2 and the faster duration.
        app.ShowScreen(AppScreen.Processing);
        yield return null;
        yield return null;
        Assert.AreEqual("LV.2", LabelText(root, ElementNames.ProcessingRefineryLevel), "processing refinery LV.2");
        Assert.AreEqual("1.6s", LabelText(root, ElementNames.ProcessingDurationValue), "processing duration 1.6s");

        // Actual timer uses the derived duration.
        Assert.IsTrue(session.TryStartProcess(5), "start batch 5");
        AssertNearly(1.6f, session.ProcessRemainingSeconds, "remaining seconds uses derived duration");
        session.AdvanceProcessing(session.RefineryProcessSeconds);
        yield return null;
        Assert.AreEqual(GameSession.RefineryProcessState.Complete, session.ProcessState, "batch complete");

        // Alloy gained from processing shows on Outpost immediately.
        app.ShowScreen(AppScreen.Overview);
        yield return null;
        Assert.AreEqual("3", LabelText(root, ElementNames.AlloyValue), "overview alloy after processing");
        app.ShowScreen(AppScreen.Outpost);
        yield return null;
        Assert.AreEqual("3", LabelText(root, ElementNames.OutpostAlloyValue), "outpost alloy after processing");

        // ---- Reset restores the whole economy and UI ----
        session.ResetSession();
        yield return null;
        Assert.AreEqual("5", LabelText(root, ElementNames.OutpostAlloyValue), "outpost alloy reset");
        Assert.AreEqual("LV.1", LabelText(root, ElementNames.RefineryLevelValue), "outpost refinery reset");
        Assert.AreEqual("LV.2", LabelText(root, ElementNames.OutpostCapValue), "cap back to LV.2");
        Assert.AreEqual("2.0s", LabelText(root, ElementNames.ModuleCurrentEffectValue), "detail duration reset");
        Assert.AreEqual("升级模块", upgrade.text, "CTA back to default");
        Assert.IsTrue(upgrade.enabledSelf, "CTA enabled after reset");
        Assert.AreEqual(1, session.OutpostLevel, "core level reset");
        Assert.AreEqual(1, session.HarvesterLevel, "harvester level reset");
        AssertNearly(2.0f, session.RefineryProcessSeconds, "duration reset to 2.0");
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }
}
