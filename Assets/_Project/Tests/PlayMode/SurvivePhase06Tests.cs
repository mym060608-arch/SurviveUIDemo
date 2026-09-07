using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 06 Play Mode validation for the NEREID-04 surface gameplay slice.
///
/// Separation of concerns: GameSession owns the authoritative per-node mask
/// and settlement; the scene state machine (SurfaceResourceNode) only renders
/// and reports through it. This suite drives real public APIs:
/// - pure session rules (mask, yields, reset),
/// - the deterministic node state machine (fixed-time AdvanceTimers),
/// - the real cross-scene expedition loop Boot -> Hub -> surface (real-time
///   settle) -> Hub preservation, then a second re-entry proving persistence.
///
/// Semantic automation uses the public gameplay APIs (TryDeployNearest /
/// TeleportTo / ReturnToHub) exactly like AutoSurfaceFlowProbe; it never
/// fakes physical key presses (see the Manual Input Review in the report).
/// Single Boot -> Hub load per test, suite runs via its own -testFilter.
/// </summary>
public class SurvivePhase06Tests
{
    private const int MaxWaitFrames = 1500;

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
    public IEnumerator SessionState_SurfaceNodes_AuthoritativeRules()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        // Phase 06 constants + Level-1 derived harvest duration.
        Assert.AreEqual(3, GameSession.SurfaceNodeCount, "node count");
        Assert.AreEqual(5, GameSession.SurfaceNodeYield, "per-node yield");
        Assert.AreEqual("02_NereidSurface", GameSession.SurfaceSceneName, "surface scene name");
        AssertNearly(4.0f, session.SurfaceHarvestSeconds, "LV.1 harvest 4.0s");
        Assert.AreEqual(0, session.HarvestedNodeCount, "fresh session no nodes");
        Assert.AreEqual(20, session.Minerals, "fresh minerals 20");
        yield return null;

        // Out-of-range ids and non-positive yields are rejected, state intact.
        Assert.IsFalse(session.TryCompleteSurfaceNode(-1, 5), "negative id rejected");
        Assert.IsFalse(session.TryCompleteSurfaceNode(3, 5), "id 3 rejected");
        Assert.IsFalse(session.TryCompleteSurfaceNode(0, 0), "zero yield rejected");
        Assert.IsFalse(session.TryCompleteSurfaceNode(0, -5), "negative yield rejected");
        Assert.AreEqual(0, session.HarvestedNodeCount, "rejections change nothing");
        Assert.AreEqual(20, session.Minerals, "no payout on rejections");

        // First settlement pays exactly +5 once.
        Assert.IsTrue(session.TryCompleteSurfaceNode(0, 5), "node0 completes");
        Assert.AreEqual(25, session.Minerals, "minerals +5");
        Assert.AreEqual(1, session.HarvestedNodeCount, "count 1");
        Assert.IsTrue(session.IsSurfaceNodeCompleted(0), "node0 marked");
        Assert.IsFalse(session.IsSurfaceNodeCompleted(1), "node1 untouched");

        // Duplicate settle is rejected and pays nothing.
        Assert.IsFalse(session.TryCompleteSurfaceNode(0, 5), "node0 duplicate rejected");
        Assert.AreEqual(25, session.Minerals, "no double payout");
        Assert.AreEqual(1, session.HarvestedNodeCount, "count stays 1");

        // All three nodes settle once each.
        Assert.IsTrue(session.TryCompleteSurfaceNode(1, 5), "node1 completes");
        Assert.IsTrue(session.TryCompleteSurfaceNode(2, 5), "node2 completes");
        Assert.AreEqual(3, session.HarvestedNodeCount, "count 3");
        Assert.AreEqual(35, session.Minerals, "all nodes +15");
        Assert.IsFalse(session.TryCompleteSurfaceNode(2, 5), "all duplicates rejected");

        // Reset clears the mask and restores the session economy.
        session.ResetSession();
        Assert.AreEqual(0, session.HarvestedNodeCount, "mask cleared on reset");
        Assert.AreEqual(20, session.Minerals, "minerals restored");
        Assert.IsFalse(session.IsSurfaceNodeCompleted(0), "node0 available after reset");

        // Phase 05 integration: the real harvester multiplier shortens harvest.
        session.TryUpgrade(GameSession.OutpostModule.Harvester); // LV.2, alloy 2
        AssertNearly(3.2f, session.SurfaceHarvestSeconds, "LV.2 harvest 3.2s");
        session.ResetSession();
        session.AddAlloy(18); // 23
        session.TryUpgrade(GameSession.OutpostModule.Core);       // LV.2, 18
        session.TryUpgrade(GameSession.OutpostModule.Harvester);  // LV.2, 15
        session.TryUpgrade(GameSession.OutpostModule.Harvester);  // LV.3, 12
        Assert.AreEqual(3, session.HarvesterLevel, "harvester LV.3");
        AssertNearly(4.0f / 1.5f, session.SurfaceHarvestSeconds, "LV.3 harvest ~2.667s");
        session.ResetSession();
    }

    [UnityTest]
    public IEnumerator SurfaceNode_StateMachine_Deterministic()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        var go = new GameObject("TestNode");
        SurfaceResourceNode node = go.AddComponent<SurfaceResourceNode>();
        node.nodeId = 0;
        node.mineralYield = GameSession.SurfaceNodeYield;
        node.enabled = false; // deterministic: no real Update racing AdvanceTimers

        int settledEvents = 0;
        int settledYield = 0;
        node.OnSettled += (n, y) =>
        {
            settledEvents++;
            settledYield = y;
        };

        Assert.AreEqual(SurfaceResourceNode.NodeState.Available, node.State, "starts available");
        AssertNearly(session.SurfaceHarvestSeconds, 4.0f, "session harvest base");

        // Node never starts completed from a fresh session.
        Assert.IsFalse(session.IsSurfaceNodeCompleted(0), "node not completed");

        // Deploy: Available -> Harvesting with the authoritative duration.
        Assert.IsTrue(node.TryStartHarvest(), "deploy accepted");
        Assert.AreEqual(SurfaceResourceNode.NodeState.Harvesting, node.State, "harvesting");
        AssertNearly(session.SurfaceHarvestSeconds, node.StageSeconds, "stage = session harvest seconds");
        Assert.IsTrue(node.IsBusy, "busy while harvesting");
        Assert.IsFalse(node.TryStartHarvest(), "second deploy rejected while busy");

        // Fixed-time advancement: 1s in, still harvesting; remaining dropped.
        node.AdvanceTimers(1f);
        Assert.AreEqual(SurfaceResourceNode.NodeState.Harvesting, node.State, "still harvesting after 1s");
        AssertNearly(node.StageSeconds - 1f, node.RemainingSeconds, "remaining 3.0");

        // Reaching zero flips into wireless transfer with the fixed stage.
        node.AdvanceTimers(node.RemainingSeconds);
        Assert.AreEqual(SurfaceResourceNode.NodeState.Transmitting, node.State, "transmitting");
        AssertNearly(GameSession.SurfaceWirelessTransferSeconds, node.StageSeconds, "transfer stage 0.75s");
        Assert.AreEqual(20, session.Minerals, "minerals unchanged during transfer");
        Assert.AreEqual(0, settledEvents, "no settle during transfer");

        // Transfer completion settles exactly once through GameSession.
        node.AdvanceTimers(node.RemainingSeconds);
        Assert.AreEqual(SurfaceResourceNode.NodeState.Depleted, node.State, "depleted");
        Assert.AreEqual(1, settledEvents, "settle event fired once");
        Assert.AreEqual(5, settledYield, "settled yield 5");
        Assert.AreEqual(1, session.HarvestedNodeCount, "session count 1");
        Assert.AreEqual(25, session.Minerals, "minerals +5 after settle");

        // Depleted node can never start again; session rejects duplicate bits.
        Assert.IsFalse(node.TryStartHarvest(), "depleted cannot deploy");
        Assert.IsFalse(session.TryCompleteSurfaceNode(0, 5), "session duplicate rejected");
        Assert.AreEqual(25, session.Minerals, "no double payout after state machine");

        // Second independent node settles independently. Create it inactive so
        // Awake runs only after nodeId is assigned (Awake reads the session mask).
        var go2 = new GameObject("TestNode2");
        go2.SetActive(false);
        SurfaceResourceNode node2 = go2.AddComponent<SurfaceResourceNode>();
        node2.nodeId = 1;
        node2.mineralYield = GameSession.SurfaceNodeYield;
        node2.enabled = false;
        go2.SetActive(true); // Awake now sees id 1 against the fresh session
        Assert.IsTrue(node2.TryStartHarvest(), "node1 deploys");
        node2.AdvanceTimers(node2.RemainingSeconds); // -> transmitting
        node2.AdvanceTimers(node2.RemainingSeconds); // -> depleted
        Assert.AreEqual(2, session.HarvestedNodeCount, "count 2");
        Assert.AreEqual(30, session.Minerals, "minerals +10");

        Object.Destroy(go);
        Object.Destroy(go2);
        session.ResetSession();
        yield return null;
    }

    [UnityTest]
    public IEnumerator BootToHubSurface_ExpeditionLoop_RealTimeSettle()
    {
        GameSession.Ensure().ResetSession();
        SceneManager.LoadScene("00_Boot");
        yield return WaitUntil(() => ActiveSceneName() == BootController.HubSceneName);

        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });

        Assert.IsNotNull(app, "AppController ready");
        GameSession session = GameSession.Instance;
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        // Hub launch gate: unconfirmed rejected, confirmed accepted.
        Assert.IsFalse(session.CanStartExpedition, "gate closed before confirm");
        Assert.IsTrue(session.TryConfirmLoadout(), "confirm accepted");
        Assert.IsTrue(session.CanStartExpedition, "gate open after confirm");

        // CTA turns into the expedition launch after confirmation.
        Button confirm = app.UiRoot.Q<Button>(ElementNames.ConfirmLoadoutButton);
        Assert.IsNotNull(confirm, "confirm CTA exists");
        Assert.AreEqual("开始远征 »", confirm.text, "CTA is launch after locking");
        Assert.IsTrue(confirm.enabledSelf, "launch CTA enabled");

        // Real expedition: Preparation CTA semantic equivalent (LoadScene).
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.SurfaceSceneName);

        SurfaceGameController controller = null;
        yield return WaitUntil(() =>
        {
            controller = Object.FindAnyObjectByType<SurfaceGameController>();
            return controller != null && controller.IsReady;
        });

        Assert.IsNotNull(controller, "surface controller ready");
        Assert.AreEqual(3, controller.Nodes.Count, "three nodes");
        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        // Chinese-first HUD bound to the real session values. The surface
        // scene owns two documents (screen HUD + wrist RenderTexture); tests
        // always pick the screen-space SurfaceHUD one by asset name.
        UIDocument hudDocument = FindHudDocument();
        Assert.IsNotNull(hudDocument, "HUD UIDocument exists");
        Assert.AreEqual("SurfaceHUD", hudDocument.visualTreeAsset.name, "HUD bound to SurfaceHUD");
        VisualElement hudRoot = hudDocument.rootVisualElement;
        Assert.IsNotNull(hudRoot.Q<Label>("HudMineralsValue"), "HudMineralsValue exists");
        Assert.IsNotNull(hudRoot.Q<Label>("HudEfficiencyValue"), "HudEfficiencyValue exists");
        Assert.IsNotNull(hudRoot.Q<Label>("HudNodesValue"), "HudNodesValue exists");
        Assert.IsNotNull(hudRoot.Q<Button>("HudReturnButton"), "HudReturnButton exists");

        SurfaceResourceNode node0 = controller.Nodes[0];
        SurfacePlayerController player = Object.FindAnyObjectByType<SurfacePlayerController>();
        Assert.IsNotNull(player, "player exists");
        Assert.AreEqual(20, session.Minerals, "surface entry minerals unchanged");
        Assert.AreEqual("20", hudRoot.Q<Label>("HudMineralsValue").text, "HUD shows 20");
        Assert.AreEqual("0 / 3", hudRoot.Q<Label>("HudNodesValue").text, "HUD nodes 0 / 3");

        // Out-of-range deploy rejected.
        Assert.IsFalse(controller.TryDeployNearest(), "deploy rejected when far");

        // Semantic WASD-equivalent: teleport the unit next to node 0.
        player.TeleportTo(node0.transform.position + Vector3.forward * 1.2f);
        yield return null;
        yield return null;

        // Deploy via the same semantic API the E key binds to.
        Assert.IsTrue(controller.TryDeployNearest(), "deploy node0 accepted");
        Assert.AreEqual(SurfaceResourceNode.NodeState.Harvesting, node0.State, "harvesting");
        Assert.IsTrue(controller.NearestNode == node0, "nearest is node0");
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        // HUD reflects the live harvest status card.
        VisualElement statusCard = hudRoot.Q<VisualElement>("HudStatusCard");
        Assert.IsTrue(statusCard != null && statusCard.resolvedStyle.display == DisplayStyle.Flex,
            "status card visible during harvest");
        Assert.AreEqual("采集中", hudRoot.Q<Label>("HudStatusText").text, "status text harvesting");

        // Real-time settle: node Update drives the timers; bounded by real
        // seconds (frame caps do not bound wall time when the batch loop is
        // uncapped).
        yield return WaitUntilState(node0, SurfaceResourceNode.NodeState.Depleted, 12f);
        Assert.AreEqual(SurfaceResourceNode.NodeState.Depleted, node0.State, "node0 depleted in real time");
        Assert.AreEqual(25, session.Minerals, "minerals +5 after real settle");
        Assert.AreEqual(1, session.HarvestedNodeCount, "count 1");
        Assert.IsFalse(controller.TryDeployNearest(), "depleted node cannot redeploy");

        // Transfer result surfaced (auto-clears after ~2.5s).
        Label result = hudRoot.Q<Label>("HudResult");
        Assert.AreEqual("传输完成  矿物 +5", result.text, "transfer result line shown");

        // Persistence: leave for the Hub and verify the shared value survives.
        controller.ReturnToHub();
        yield return WaitUntil(() => ActiveSceneName() == BootController.HubSceneName);
        app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        Assert.IsNotNull(app, "hub after return");
        Assert.AreEqual("25", LabelText(app.UiRoot, ElementNames.MineralsValue), "overview shows 25");
        app.ShowScreen(AppScreen.Processing);
        yield return null;
        yield return null;
        Assert.AreEqual("25", LabelText(app.UiRoot, ElementNames.ProcessingMineralsValue),
            "processing shows 25");

        // Re-entry: the completed node restores Depleted, others stay available.
        // Registry order is not guaranteed to follow node ids, so all lookups
        // below resolve by NodeId.
        app.ShowScreen(AppScreen.Preparation);
        yield return null;
        Assert.IsTrue(session.CanStartExpedition, "gate still open (loadout persisted)");
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.SurfaceSceneName);
        controller = null;
        yield return WaitUntil(() =>
        {
            controller = Object.FindAnyObjectByType<SurfaceGameController>();
            return controller != null && controller.IsReady;
        });
        Assert.IsNotNull(controller, "surface controller on re-entry");
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        SurfaceResourceNode byId0 = null;
        SurfaceResourceNode byId1 = null;
        SurfaceResourceNode byId2 = null;
        foreach (SurfaceResourceNode n in controller.Nodes)
        {
            if (n.NodeId == 0)
            {
                byId0 = n;
            }
            else if (n.NodeId == 1)
            {
                byId1 = n;
            }
            else if (n.NodeId == 2)
            {
                byId2 = n;
            }
        }

        Assert.IsNotNull(byId0, "node id0 present on re-entry");
        Assert.IsNotNull(byId1, "node id1 present on re-entry");
        Assert.IsNotNull(byId2, "node id2 present on re-entry");
        Assert.AreEqual(SurfaceResourceNode.NodeState.Depleted, byId0.State,
            "node0 persists depleted across re-entry");
        Assert.AreEqual(SurfaceResourceNode.NodeState.Available, byId1.State, "node1 still available");
        Assert.AreEqual(SurfaceResourceNode.NodeState.Available, byId2.State, "node2 still available");
        Assert.AreEqual(25, session.Minerals, "minerals preserved on re-entry");

        // Fresh HUD document on the re-entered scene reports the live values.
        hudDocument = FindHudDocument();
        Assert.IsNotNull(hudDocument, "HUD UIDocument on re-entry");
        hudRoot = hudDocument.rootVisualElement;
        Assert.AreEqual("25", hudRoot.Q<Label>("HudMineralsValue").text, "HUD minerals 25 on re-entry");
        Assert.AreEqual("1 / 3", hudRoot.Q<Label>("HudNodesValue").text, "HUD nodes 1 / 3");

        // Harvest node1 now: every node pays exactly once per Reset.
        player = Object.FindAnyObjectByType<SurfacePlayerController>();
        player.TeleportTo(byId1.transform.position + Vector3.forward * 1.2f);
        yield return null;
        Assert.IsTrue(controller.TryDeployNearest(), "node1 deploys on re-entry");
        yield return WaitUntilState(byId1, SurfaceResourceNode.NodeState.Depleted, 12f);
        Assert.AreEqual(2, session.HarvestedNodeCount, "count 2 after node1");
        Assert.AreEqual(30, session.Minerals, "minerals +10 total");

        // Reset clears surface state again (game over -> new expedition).
        session.ResetSession();
        Assert.AreEqual(0, session.HarvestedNodeCount, "reset clears node mask");
        Assert.AreEqual(20, session.Minerals, "reset restores minerals");
    }

    private static IEnumerator WaitUntilState(SurfaceResourceNode node,
        SurfaceResourceNode.NodeState target, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (node.State != target && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static UIDocument FindHudDocument()
    {
        foreach (UIDocument doc in Object.FindObjectsByType<UIDocument>())
        {
            if (doc.visualTreeAsset != null && doc.visualTreeAsset.name == "SurfaceHUD")
            {
                return doc;
            }
        }

        return null;
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }
}
