using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 06 Revision Play Mode validation: first-person controller semantics,
/// wrist terminal toggle/input mode, personal status foundation, wrist
/// equipment/cargo display, circular-map coordinate math, navigation
/// selection/distance/depleted-clearing and the live wrist harvest states.
///
/// Automation drives only public semantic APIs (SetFacing / ApplyLook /
/// MoveByInput / ToggleWrist / TrySelectNavigationTarget / TryDeployNearest /
/// AdvanceTimers). Physical WASD / mouse / M / I / Esc / pointer clicks are
/// manual review items, never faked here.
/// </summary>
public class SurvivePhase06RevisionTests
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

    private static IEnumerator EnterSurface()
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

        Assert.IsNotNull(app, "hub ready");
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        Assert.IsTrue(GameSession.Instance.TryConfirmLoadout(), "loadout confirmed");
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.SurfaceSceneName);
        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }
    }

    private static SurfaceGameController ReadyController()
    {
        SurfaceGameController controller = null;
        foreach (SurfaceGameController candidate in Object.FindObjectsByType<SurfaceGameController>())
        {
            if (candidate != null && candidate.IsReady)
            {
                controller = candidate;
                break;
            }
        }

        return controller;
    }

    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator SessionState_PersonalStatusFoundation()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        Assert.AreEqual(100, GameSession.DefaultPlayerHealth, "health default const");
        Assert.AreEqual(18, GameSession.DefaultPlayerHunger, "hunger default const (low)");
        Assert.AreEqual(100, session.PlayerHealth, "health after reset");
        Assert.AreEqual(18, session.PlayerHunger, "hunger after reset (0=not hungry, 100=starving)");

        // Clamped mutations (no simulation exists yet).
        session.SetPlayerHealth(150);
        Assert.AreEqual(100, session.PlayerHealth, "health clamps at 100");
        session.SetPlayerHealth(-5);
        Assert.AreEqual(0, session.PlayerHealth, "health clamps at 0");
        session.SetPlayerHunger(120);
        Assert.AreEqual(100, session.PlayerHunger, "hunger clamps at 100");
        session.SetPlayerHunger(-3);
        Assert.AreEqual(0, session.PlayerHunger, "hunger clamps at 0");

        // Reset restores personal status without touching other defaults.
        session.ResetSession();
        Assert.AreEqual(100, session.PlayerHealth, "health restored by reset");
        Assert.AreEqual(18, session.PlayerHunger, "hunger restored by reset");
        Assert.AreEqual(20, session.Minerals, "resources still default");
        Assert.AreEqual(0, session.HarvestedNodeCount, "surface mask still cleared");
        yield return null;
    }

    [UnityTest]
    public IEnumerator FirstPerson_ControllerSemantics()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        Assert.IsNotNull(controller, "surface controller ready");
        SurfacePlayerController player = controller.Player;
        Assert.IsNotNull(player, "player present");
        Assert.IsNotNull(player.EyeCamera, "eye camera present");
        AssertNearly(1.65f, player.EyeCamera.transform.localPosition.y, "eye height");

        // Facing and pitch semantics.
        player.SetFacing(30f, 20f);
        AssertNearly(30f, player.YawDegrees, "yaw set");
        AssertNearly(20f, player.PitchDegrees, "pitch set");
        AssertNearly(30f, player.transform.rotation.eulerAngles.y, "body yaw applied");
        AssertNearly(20f, player.EyeCamera.transform.localRotation.eulerAngles.x, "camera pitch applied");

        // Look deltas: yaw wraps, pitch clamps at +/-80.
        player.SetFacing(0f, 0f);
        player.ApplyLook(-45f, -150f);
        AssertNearly(315f, player.YawDegrees, "yaw wrapped -45 -> 315");
        AssertNearly(-80f, player.PitchDegrees, "pitch clamped at -80");
        player.ApplyLook(0f, 300f);
        AssertNearly(80f, player.PitchDegrees, "pitch clamped at +80");

        // Movement moves the CharacterController body (horizontal plane).
        player.SetFacing(0f, 0f);
        player.TeleportTo(new Vector3(0f, 0f, 4f));
        yield return null;
        Vector3 before = player.transform.position;
        player.MoveByInput(new Vector2(0f, 1f), 1f);
        Vector3 delta = player.transform.position - before;
        Assert.Greater(new Vector2(delta.x, delta.z).magnitude, 3f,
            "MoveByInput moved the player forward ~moveSpeed");

        // World bounds clamp regardless of input direction.
        player.TeleportTo(new Vector3(16f, 0f, 16f));
        yield return null;
        player.MoveByInput(new Vector2(1f, 1f), 3f);
        Assert.LessOrEqual(Mathf.Abs(player.transform.position.x), 16.5f, "x clamp");
        Assert.LessOrEqual(Mathf.Abs(player.transform.position.z), 16.5f, "z clamp");

        // Gaze interaction semantics (first person): face an available node.
        SurfaceResourceNode node0 = controller.Nodes[0];
        player.TeleportTo(node0.transform.position + new Vector3(-1.2f, 0f, 1.4f));
        yield return null;
        Vector3 toNode = node0.transform.position - player.transform.position;
        player.SetFacing(Mathf.Atan2(toNode.x, toNode.z) * Mathf.Rad2Deg, 0f);
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        Assert.IsNotNull(controller.Interactor, "interactor present");
        Assert.IsTrue(controller.Interactor.HasValidTarget, "gaze sees the node");
        Assert.AreEqual("E  部署采集器", controller.Interactor.ResolvePrompt(), "node prompt");

        // Look away: no valid gaze target, crosshair returns to neutral.
        player.SetFacing(0f, -80f); // ground
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        Assert.IsFalse(controller.Interactor.HasValidTarget, "gaze misses when looking away");
    }

    [UnityTest]
    public IEnumerator WristMode_ToggleAndInputGating()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        Assert.IsNotNull(controller, "controller ready");
        SurfacePlayerController player = controller.Player;
        UIDocument document = Object.FindAnyObjectByType<UIDocument>();
        Assert.IsNotNull(document, "surface hud document");
        VisualElement hudLayer = document.rootVisualElement.Q<VisualElement>("HudLayer");
        VisualElement wristLayer = document.rootVisualElement.Q<VisualElement>("WristLayer");
        VisualElement cursor = document.rootVisualElement.Q<VisualElement>("WristCursor");
        Assert.IsNotNull(hudLayer, "HudLayer exists");
        Assert.IsNotNull(wristLayer, "WristLayer exists");

        // Default: wrist closed, exploration mode active.
        Assert.IsFalse(controller.IsWristOpen, "wrist closed initially");
        Assert.IsTrue(player.InputEnabled, "movement enabled while closed");
        Assert.IsTrue(hudLayer.resolvedStyle.display == DisplayStyle.Flex, "HUD visible");
        Assert.IsTrue(wristLayer.resolvedStyle.display == DisplayStyle.None, "wrist hidden");

        // The single shared toggle semantic (M and I route here) opens it.
        controller.ToggleWrist();
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        Assert.IsTrue(controller.IsWristOpen, "wrist open after toggle");
        Assert.IsFalse(player.InputEnabled, "WASD/look frozen while open");
        Assert.IsTrue(hudLayer.resolvedStyle.display == DisplayStyle.None, "normal HUD hidden");
        Assert.IsTrue(wristLayer.resolvedStyle.display == DisplayStyle.Flex, "wrist layer visible");
        if (cursor != null)
        {
            Assert.IsTrue(cursor.resolvedStyle.display == DisplayStyle.Flex, "dot cursor visible");
        }

        // Cursor lock/visibility are OS runtime behaviors (editor ignores
        // locking); semantic gating below is what the tests own.
        Assert.IsFalse(controller.TryDeployNearest(), "world interaction disabled while open");
        Assert.IsFalse(controller.Interactor.TryInteract(), "gaze interaction disabled");

        // Esc semantic shares the close path with the second toggle.
        controller.SetWristOpen(false);
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        Assert.IsFalse(controller.IsWristOpen, "wrist closed again");
        Assert.IsTrue(player.InputEnabled, "exploration restored");
        Assert.IsTrue(hudLayer.resolvedStyle.display == DisplayStyle.Flex, "HUD restored");
        Assert.IsTrue(wristLayer.resolvedStyle.display == DisplayStyle.None, "wrist hidden again");
    }

    [UnityTest]
    public IEnumerator Wrist_PersonalStatus_Equipment_Cargo()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();
        SceneManager.LoadScene("00_Boot");
        yield return WaitUntil(() => ActiveSceneName() == BootController.HubSceneName);

        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });

        // Real loadout: Harvester (always) + Battery selected, Drone deselected.
        Assert.IsTrue(session.TrySetBatterySelected(true), "battery selected");
        Assert.IsFalse(session.DroneSelected, "drone not selected");
        Assert.AreEqual(70, session.CargoWeight, "cargo 70 with battery");
        session.SetPlayerHealth(37);
        session.SetPlayerHunger(22);
        Assert.IsTrue(session.TryConfirmLoadout(), "confirm");

        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.SurfaceSceneName);
        for (int i = 0; i < 12; i++)
        {
            yield return null;
        }

        SurfaceGameController controller = ReadyController();
        Assert.IsNotNull(controller, "controller ready");
        controller.SetWristOpen(true);
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        UIDocument document = Object.FindAnyObjectByType<UIDocument>();
        VisualElement root = document.rootVisualElement;
        Assert.AreEqual("37", LabelText(root, "WristHealthValue"), "wrist health live");
        Assert.AreEqual("22", LabelText(root, "WristHungerValue"), "wrist hunger live");
        Assert.AreEqual("已装备", LabelText(root, "WristEquipHarvesterState"), "harvester equipped");
        Assert.AreEqual("已选择", LabelText(root, "WristEquipBatteryState"), "battery selected shown");
        Assert.AreEqual("未选择", LabelText(root, "WristEquipDroneState"), "drone deselected shown");
        Assert.IsTrue(LabelText(root, "WristCargoValue").Contains("设备 2"),
            "cargo equipment count real");
        Assert.IsTrue(LabelText(root, "WristCargoValue").Contains("70 / 100 KG"),
            "cargo weight real");
        controller.SetWristOpen(false);
    }

    [UnityTest]
    public IEnumerator MapMathAndNavigationSelection()
    {
        // Pure deterministic coordinate math.
        Vector2 center = SurfaceGameController.MapFraction(Vector3.zero);
        AssertNearly(0f, center.x, "map center x");
        AssertNearly(0f, center.y, "map center y");

        Vector2 nodeA = SurfaceGameController.MapFraction(new Vector3(-7f, 0f, 2.5f));
        AssertNearly(-7f / SurfaceGameController.MapHalfExtent, nodeA.x, "node fraction x");
        AssertNearly(2.5f / SurfaceGameController.MapHalfExtent, nodeA.y, "node fraction z");

        Vector2 clamped = SurfaceGameController.MapFraction(new Vector3(-99f, 0f, 99f));
        AssertNearly(-1f, clamped.x, "clamp x -1");
        AssertNearly(1f, clamped.y, "clamp z +1");

        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        SurfacePlayerController player = controller.Player;

        // Teleport somewhere definite and open the wrist to check marker bounds.
        player.TeleportTo(new Vector3(0f, 0f, 3f));
        yield return null;
        controller.SetWristOpen(true);
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        UIDocument document = Object.FindAnyObjectByType<UIDocument>();
        VisualElement circle = document.rootVisualElement.Q<VisualElement>("WristMapCircle");
        Rect circleBounds = circle.worldBound;
        Vector2 circleCenter = circleBounds.center;
        float radius = circleBounds.width * 0.5f;

        for (int i = 0; i < 3; i++)
        {
            VisualElement marker = document.rootVisualElement.Q<VisualElement>("MapNode" + i);
            Assert.IsNotNull(marker, "map marker " + i);
            Vector2 markerCenter = marker.worldBound.center;
            float distance = Vector2.Distance(markerCenter, circleCenter);
            Assert.IsTrue(distance <= radius + 1f, "marker " + i + " stays inside circle");
        }

        controller.SetWristOpen(false);
        for (int i = 0; i < 3; i++)
        {
            yield return null;
        }

        // Select node 1 (id 1 = 资源节点 02) and check the exploration HUD.
        Assert.IsTrue(controller.TrySelectNavigationTarget(1), "select node 1");
        Assert.AreEqual(1, controller.NavigationTargetNodeId, "nav target id");
        Assert.IsTrue(controller.IsNavigationActive, "nav active");
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        VisualElement navBlock = document.rootVisualElement.Q<VisualElement>("HudNavBlock");
        Assert.IsTrue(navBlock.resolvedStyle.display == DisplayStyle.Flex, "HUD nav visible");
        Assert.AreEqual("◆ 资源节点 02", LabelText(document.rootVisualElement, "HudNavName"),
            "nav name text");
        string distanceText = LabelText(document.rootVisualElement, "HudNavDistance");
        Assert.IsTrue(distanceText.EndsWith(" m"), "distance shown in meters");
        float shown = float.Parse(distanceText.Replace(" m", ""));
        SurfaceResourceNode node1 = controller.Nodes[1];
        float real = Vector3.Distance(player.transform.position, node1.transform.position);
        Assert.IsTrue(Mathf.Abs(shown - real) < 0.6f, "distance matches real world distance");

        // Wrist shows the same target and ring.
        controller.SetWristOpen(true);
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        Assert.AreEqual("资源节点 02", LabelText(document.rootVisualElement, "WristNavName"),
            "wrist nav name");
        VisualElement ring = document.rootVisualElement.Q<VisualElement>("MapNavRing");
        Assert.IsTrue(ring.resolvedStyle.display == DisplayStyle.Flex, "nav ring visible");
        VisualElement marker1 = document.rootVisualElement.Q<VisualElement>("MapNode1");
        Assert.IsTrue(marker1.ClassListContains("marker-selected"), "selected marker class");
        controller.SetWristOpen(false);
        controller.ClearNavigationTarget();
        Assert.IsFalse(controller.IsNavigationActive, "nav cleared");
    }

    [UnityTest]
    public IEnumerator DepletedNode_NavigationClearsAndCannotReselect()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        SurfacePlayerController player = controller.Player;

        // Select node 0 while it is Available, then settle it deterministically.
        Assert.IsTrue(controller.TrySelectNavigationTarget(0), "select node 0");
        player.TeleportTo(controller.Nodes[0].transform.position + Vector3.forward * 1.2f);
        yield return null;
        Assert.IsTrue(controller.TryDeployNearest(), "deploy node 0");
        SurfaceResourceNode node0 = controller.Nodes[0];
        node0.AdvanceTimers(node0.RemainingSeconds); // -> transmitting
        node0.AdvanceTimers(node0.RemainingSeconds); // -> depleted +5
        yield return null;
        yield return null;

        Assert.AreEqual(SurfaceResourceNode.NodeState.Depleted, node0.State, "node depleted");
        Assert.AreEqual(25, GameSession.Instance.Minerals, "minerals +5");
        Assert.IsFalse(controller.IsNavigationActive,
            "navigation clears when its target node is depleted");
        Assert.IsFalse(controller.TrySelectNavigationTarget(0),
            "depleted node cannot become a nav target");
    }

    [UnityTest]
    public IEnumerator Wrist_LiveHarvestStates()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        SurfacePlayerController player = controller.Player;
        GameSession session = GameSession.Instance;

        // Deploy node 0 while the wrist is closed.
        player.TeleportTo(controller.Nodes[0].transform.position + Vector3.forward * 1.2f);
        yield return null;
        Assert.IsTrue(controller.TryDeployNearest(), "deploy node 0");
        SurfaceResourceNode node0 = controller.Nodes[0];

        // Open the wrist during harvesting: world timer keeps running.
        controller.SetWristOpen(true);
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        UIDocument document = Object.FindAnyObjectByType<UIDocument>();
        VisualElement root = document.rootVisualElement;
        Assert.AreEqual("资源节点 01", LabelText(root, "WristActiveNode"), "active node name");
        Assert.AreEqual("采集中", LabelText(root, "WristActiveState"), "wrist shows harvesting");
        Assert.IsTrue(LabelText(root, "WristActivePercent").EndsWith("%"), "progress percent shown");
        Assert.IsTrue(root.Q<VisualElement>("MapNode0").ClassListContains("marker-harvesting"),
            "map marker harvesting");

        // Deterministic step -> still open wrist, progress advanced, timer runs.
        node0.AdvanceTimers(0.75f);
        yield return null;
        yield return null;
        string percentText = LabelText(root, "WristActivePercent").Replace("%", "");
        int percent = int.Parse(percentText);
        Assert.Greater(percent, 0, "progress advanced while wrist open");

        // Push through wireless transfer; the wrist reflects it live.
        node0.AdvanceTimers(node0.RemainingSeconds); // -> transmitting
        yield return null;
        yield return null;
        Assert.AreEqual("无线传输中", LabelText(root, "WristActiveState"), "wrist shows transfer");
        Assert.IsTrue(root.Q<VisualElement>("MapNode0").ClassListContains("marker-transmitting"),
            "map marker transmitting");

        // Completion while the wrist stays open.
        node0.AdvanceTimers(node0.RemainingSeconds); // -> depleted +5
        yield return null;
        yield return null;
        yield return null;
        Assert.AreEqual(SurfaceResourceNode.NodeState.Depleted, node0.State, "depleted");
        Assert.AreEqual(25, session.Minerals, "settled +5 while wrist open");
        Assert.IsTrue(root.Q<VisualElement>("MapNode0").ClassListContains("marker-depleted"),
            "map marker depleted");
        controller.SetWristOpen(false);
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }
}
