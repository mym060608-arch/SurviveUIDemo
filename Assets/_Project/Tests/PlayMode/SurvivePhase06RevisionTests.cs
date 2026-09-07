using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 06 Revision round 3 Play Mode validation.
///
/// Covers: satiety (0=extremely hungry .. 100=fully satiated, default 82),
/// facing-relative WASD movement, very-near / mid / far / behind gaze
/// interaction, floating wrist presentation (RenderTexture + world display),
/// deterministic LOCAL MAP hit-test coordinates, navigation selection and
/// distance, depleted-node clearing, live wrist harvest states, landing-zone
/// E return and the Hub UI input-mode path.
///
/// Automation drives public semantic APIs and pure coordinate mapping only.
/// Physical WASD / mouse / E / M / I / Esc / left-click remain manual review
/// items; the real left-click path (Mouse.current + display ray -> map
/// hit-test -> TrySelectNavigationTarget) is wired but cursor-lock/visibility
/// assertions are validated on the Release player where OS cursor state is
/// authoritative.
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
        for (int i = 0; i < 12; i++)
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

    /// <summary>HUD document (screen-space SurfaceHUD.uxml).</summary>
    private static UIDocument HudDocument()
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

    /// <summary>Wrist controller whose document renders into the RenderTexture.</summary>
    private static SurfaceWristController Wrist()
    {
        return Object.FindAnyObjectByType<SurfaceWristController>();
    }

    // ------------------------------------------------------------------

    [UnityTest]
    public IEnumerator SessionState_SatietyFoundation()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        Assert.AreEqual(100, GameSession.DefaultPlayerHealth, "health default const");
        Assert.AreEqual(82, GameSession.DefaultPlayerSatiety, "satiety default const (normal high)");
        Assert.AreEqual(100, session.PlayerHealth, "health after reset");
        Assert.AreEqual(82, session.PlayerSatiety,
            "satiety after reset (0=extremely hungry, 100=fully satiated)");

        session.SetPlayerHealth(150);
        Assert.AreEqual(100, session.PlayerHealth, "health clamps at 100");
        session.SetPlayerHealth(-5);
        Assert.AreEqual(0, session.PlayerHealth, "health clamps at 0");
        session.SetPlayerSatiety(120);
        Assert.AreEqual(100, session.PlayerSatiety, "satiety clamps at 100");
        session.SetPlayerSatiety(7);
        Assert.AreEqual(7, session.PlayerSatiety, "satiety accepts low (hungry) value");
        session.SetPlayerSatiety(-3);
        Assert.AreEqual(0, session.PlayerSatiety, "satiety clamps at 0");

        session.ResetSession();
        Assert.AreEqual(100, session.PlayerHealth, "health restored by reset");
        Assert.AreEqual(82, session.PlayerSatiety, "satiety restored by reset");
        Assert.AreEqual(20, session.Minerals, "resources still default");
        Assert.AreEqual(0, session.HarvestedNodeCount, "surface mask still cleared");
        yield return null;
    }

    [UnityTest]
    public IEnumerator FirstPerson_FacingRelativeMovementAndLook()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        Assert.IsNotNull(controller, "surface controller ready");
        SurfacePlayerController player = controller.Player;
        Assert.IsNotNull(player, "player present");
        Assert.IsNotNull(player.EyeCamera, "eye camera present");
        AssertNearly(1.65f, player.EyeCamera.transform.localPosition.y, "eye height");

        player.SetFacing(30f, 20f);
        AssertNearly(30f, player.YawDegrees, "yaw set");
        AssertNearly(20f, player.PitchDegrees, "pitch set");
        AssertNearly(30f, player.transform.rotation.eulerAngles.y, "body yaw applied");
        AssertNearly(20f, player.EyeCamera.transform.localRotation.eulerAngles.x, "camera pitch applied");

        player.SetFacing(0f, 0f);
        player.ApplyLook(-45f, -150f);
        AssertNearly(315f, player.YawDegrees, "yaw wrapped -45 -> 315");
        AssertNearly(-80f, player.PitchDegrees, "pitch clamped at -80");
        player.ApplyLook(0f, 300f);
        AssertNearly(80f, player.PitchDegrees, "pitch clamped at +80");

        Vector3 step;
        player.TeleportTo(new Vector3(0f, 0f, 4f));
        yield return null;
        player.SetFacing(0f, 0f);
        player.MoveByInput(new Vector2(0f, 1f), 0.6f);
        step = player.transform.position - new Vector3(0f, 0f, 4f);
        Assert.Greater(step.z, 2f, "yaw 0: W moves +Z");
        Assert.Less(Mathf.Abs(step.x), 0.5f, "yaw 0: W has no X drift");

        player.TeleportTo(new Vector3(0f, 0f, 4f));
        yield return null;
        player.SetFacing(90f, 0f);
        player.MoveByInput(new Vector2(0f, 1f), 0.6f);
        step = player.transform.position - new Vector3(0f, 0f, 4f);
        Assert.Greater(step.x, 2f, "yaw 90: W moves +X");

        player.TeleportTo(new Vector3(0f, 0f, 4f));
        yield return null;
        player.SetFacing(180f, 0f);
        player.MoveByInput(new Vector2(0f, 1f), 0.6f);
        step = player.transform.position - new Vector3(0f, 0f, 4f);
        Assert.Less(step.z, -2f, "yaw 180: W moves -Z");

        player.TeleportTo(new Vector3(0f, 0f, 4f));
        yield return null;
        player.SetFacing(0f, 0f);
        player.MoveByInput(new Vector2(1f, 0f), 0.6f);
        step = player.transform.position - new Vector3(0f, 0f, 4f);
        Assert.Greater(step.x, 2f, "yaw 0: D moves +X");
        player.MoveByInput(new Vector2(-1f, 0f), 0.6f);
        Assert.Less(player.transform.position.x - 4f, -1.5f, "yaw 0: A moves -X");

        player.TeleportTo(new Vector3(16f, 0f, 16f));
        yield return null;
        player.MoveByInput(new Vector2(1f, 1f), 3f);
        Assert.LessOrEqual(Mathf.Abs(player.transform.position.x), 16.5f, "x clamp");
        Assert.LessOrEqual(Mathf.Abs(player.transform.position.z), 16.5f, "z clamp");

        SurfaceResourceNode node0 = controller.Nodes[0];
        player.TeleportTo(node0.transform.position + new Vector3(-2.0f, 0f, 2.2f));
        yield return null;
        Vector3 toNode = node0.transform.position - player.transform.position;
        player.SetFacing(Mathf.Atan2(toNode.x, toNode.z) * Mathf.Rad2Deg, 0f);
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        Assert.IsNotNull(controller.Interactor, "interactor present");
        Assert.IsTrue(controller.Interactor.HasValidTarget, "gaze sees the node at mid range");
        Assert.AreEqual("E  部署采集器", controller.Interactor.ResolvePrompt(), "node prompt");

        player.SetFacing(0f, -80f);
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
        UIDocument hudDoc = HudDocument();
        Assert.IsNotNull(hudDoc, "hud document exists");
        SurfaceWristController wrist = Wrist();
        Assert.IsNotNull(wrist, "wrist controller exists");
        Assert.IsNotNull(wrist.RootElement, "wrist root bound");

        VisualElement hudLayer = hudDoc.rootVisualElement.Q<VisualElement>("HudLayer");
        Assert.IsNotNull(hudLayer, "HudLayer exists");

        Assert.IsFalse(controller.IsWristOpen, "wrist closed initially");
        Assert.IsTrue(player.InputEnabled, "movement enabled while closed");
        Assert.IsTrue(hudLayer.resolvedStyle.display == DisplayStyle.Flex, "HUD visible");

        controller.ToggleWrist();
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Assert.IsTrue(controller.IsWristOpen, "wrist open after toggle");
        Assert.IsFalse(player.InputEnabled, "WASD/look frozen while open");
        Assert.IsTrue(hudLayer.resolvedStyle.display == DisplayStyle.None, "normal HUD hidden");

        // The wrist terminal is a transparent RenderTexture document; its
        // geometry (map/rings/cards) is what defines the floating layout.
        UIDocument wristDoc = null;
        foreach (UIDocument doc in Object.FindObjectsByType<UIDocument>())
        {
            if (doc.visualTreeAsset != null && doc.visualTreeAsset.name == "SurfaceWrist")
            {
                wristDoc = doc;
            }
        }

        Assert.IsNotNull(wristDoc, "wrist document found");
        Assert.IsTrue(wristDoc.enabled, "wrist document active while open");

        Assert.IsFalse(controller.TryDeployNearest(), "world interaction disabled while open");
        Assert.IsFalse(controller.Interactor.TryInteract(), "gaze interaction disabled");

        controller.SetWristOpen(false);
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Assert.IsFalse(controller.IsWristOpen, "wrist closed again");
        Assert.IsTrue(player.InputEnabled, "exploration restored");
        Assert.IsTrue(hudLayer.resolvedStyle.display == DisplayStyle.Flex, "HUD restored");
        Assert.IsTrue(wristDoc.enabled == false || !wristDoc.enabled, "wrist document disabled");
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

        Assert.IsTrue(session.TrySetBatterySelected(true), "battery selected");
        Assert.IsFalse(session.DroneSelected, "drone not selected");
        Assert.AreEqual(70, session.CargoWeight, "cargo 70 with battery");
        session.SetPlayerHealth(37);
        session.SetPlayerSatiety(64);
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
        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        VisualElement root = Wrist().RootElement;
        Assert.IsNotNull(root, "wrist root");
        Assert.AreEqual("37", LabelText(root, "WristHealthValue"), "wrist health live");
        Assert.AreEqual("64", LabelText(root, "WristSatietyValue"), "wrist satiety live");
        Assert.AreEqual("已装备", LabelText(root, "WristEquipHarvesterState"), "harvester equipped");
        Assert.AreEqual("已选择", LabelText(root, "WristEquipBatteryState"), "battery selected shown");
        Assert.AreEqual("未选择", LabelText(root, "WristEquipDroneState"), "drone deselected shown");
        Assert.IsTrue(LabelText(root, "WristCargoValue").Contains("70 / 100 KG"), "cargo weight real");

        VisualElement healthRing = root.Q<VisualElement>("WristHealthRing");
        VisualElement satietyRing = root.Q<VisualElement>("WristSatietyRing");
        Assert.IsNotNull(healthRing, "health ring exists");
        Assert.IsNotNull(satietyRing, "satiety ring exists");
        Assert.Greater(healthRing.childCount, 0, "health ring has segments");
        Assert.AreEqual(healthRing.childCount, satietyRing.childCount, "both rings sized equal");
        controller.SetWristOpen(false);
    }

    [UnityTest]
    public IEnumerator VeryNearAndBackInteraction()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        SurfacePlayerController player = controller.Player;
        SurfaceResourceNode node0 = controller.Nodes[0];
        Vector3 nodePos = node0.transform.position;

        player.TeleportTo(nodePos + new Vector3(0f, 0f, 0.35f));
        yield return null;
        player.SetFacing(Mathf.Atan2(0f, -0.35f) * Mathf.Rad2Deg, 0f);
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Assert.IsTrue(controller.Interactor.HasValidTarget, "very-near node still interactable");
        Assert.AreEqual("E  部署采集器", controller.Interactor.ResolvePrompt(), "near prompt");
        Assert.IsTrue(controller.Interactor.TryInteract(), "very-near E deploy works");
        Assert.AreEqual(SurfaceResourceNode.NodeState.Harvesting, node0.State, "deployed from very near");

        player.TeleportTo(nodePos + new Vector3(-5f, 0f, 5f));
        yield return null;
        player.SetFacing(Mathf.Atan2(5f, -5f) * Mathf.Rad2Deg, 0f);
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Assert.IsFalse(controller.Interactor.HasValidTarget, "far node not interactable");
    }

    [UnityTest]
    public IEnumerator BacksideNode_NotSelectedByProximity()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        SurfacePlayerController player = controller.Player;
        SurfaceResourceNode node1 = controller.Nodes[1];

        Vector3 nodePos = node1.transform.position;
        player.TeleportTo(nodePos + new Vector3(0f, 0f, -1.1f));
        yield return null;
        player.SetFacing(180f, 0f);
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Assert.IsFalse(controller.Interactor.HasValidTarget,
            "node behind the player is never the gaze target");

        Vector3 toNode = nodePos - player.transform.position;
        player.SetFacing(Mathf.Atan2(toNode.x, toNode.z) * Mathf.Rad2Deg, 0f);
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Assert.IsTrue(controller.Interactor.HasValidTarget, "facing the near node selects it");
    }

    [UnityTest]
    public IEnumerator LocalMap_HitTestCoordinates()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        SurfaceWristController wrist = Wrist();
        Assert.IsNotNull(wrist, "wrist controller");

        // Marker logical centre for a known world node on the fixed canvas.
        SurfaceResourceNode node1 = controller.Nodes[1];
        Vector2 fraction1 = SurfaceGameController.MapFraction(node1.transform.position);
        Vector2 node1Pixel = MapPixel(fraction1);

        // Direct hit at the marker centre resolves to that node.
        int hit = wrist.HitTestNode(node1Pixel);
        Assert.AreEqual(1, hit, "centre hit resolves to node 1");

        // Small offsets inside the generous hit radius still hit.
        int hitOffset = wrist.HitTestNode(node1Pixel + new Vector2(10f, 0f));
        Assert.AreEqual(1, hitOffset, "offset within hit radius resolves");

        // A corner of the wrist canvas far from any marker resolves to none.
        Assert.AreEqual(-1, wrist.HitTestNode(new Vector2(30f, 30f)), "corner has no node");

        // The player/landing centre also resolves to none (no node there).
        Assert.AreEqual(-1, wrist.HitTestNode(MapPixel(Vector2.zero)), "map centre has no node");

        // Orientation sanity: node 0 is left/up of node 1 on the map.
        SurfaceResourceNode node0 = controller.Nodes[0];
        Vector2 f0 = SurfaceGameController.MapFraction(node0.transform.position);
        Assert.Less(f0.x, fraction1.x, "node0 west of node1");
    }

    private static Vector2 MapPixel(Vector2 fraction)
    {
        return new Vector2(
            SurfaceWristController.MapLeft + SurfaceWristController.MapSize * 0.5f
                + fraction.x * SurfaceWristController.MapRadiusPx,
            SurfaceWristController.MapTop + SurfaceWristController.MapSize * 0.5f
                - fraction.y * SurfaceWristController.MapRadiusPx);
    }

    [UnityTest]
    public IEnumerator WristDisplay_RuntimeVisibilityChain()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        SurfaceWristController wrist = Wrist();
        Assert.IsNotNull(wrist, "wrist controller");
        SurfacePlayerController player = controller.Player;

        // Closed: nothing of the chain is active yet.
        Assert.IsNotNull(wrist.CurrentRenderTexture, "RenderTexture exists");
        Assert.Greater(wrist.CurrentRenderTexture.width, 0, "RT width");
        Assert.Greater(wrist.CurrentRenderTexture.height, 0, "RT height");
        Assert.AreEqual(RenderTextureFormat.ARGB32, wrist.CurrentRenderTexture.format,
            "RT carries alpha");

        controller.SetWristOpen(true);
        for (int i = 0; i < 16; i++)
        {
            yield return null;
        }

        bool ok = wrist.VerifyDisplayVisible(out string report);
        Assert.IsTrue(ok, "wrist display chain visible:\n" + report);

        // Individual links (independent of the report helper).
        UIDocument wristDoc = null;
        foreach (UIDocument doc in Object.FindObjectsByType<UIDocument>())
        {
            if (doc.visualTreeAsset != null && doc.visualTreeAsset.name == "SurfaceWrist")
            {
                wristDoc = doc;
            }
        }

        Assert.IsNotNull(wristDoc, "wrist doc");
        Assert.IsTrue(wristDoc.enabled, "wrist doc enabled while open");
        Assert.IsTrue(wristDoc.panelSettings != null
                      && wristDoc.panelSettings.targetTexture == wrist.CurrentRenderTexture,
            "panel targets wrist RT");
        WristDisplayAnchor anchor = wrist.Display;
        Assert.IsNotNull(anchor, "display anchor");
        Assert.IsTrue(anchor.SurfaceRenderer != null && anchor.SurfaceRenderer.enabled,
            "renderer enabled");
        Assert.IsTrue(anchor.HasTexture(wrist.CurrentRenderTexture),
            "material references wrist RT");

        // Player-camera geometry: display centre projected inside the viewport,
        // in clip range, front normal toward the camera, and a sane size.
        Camera cam = player.EyeCamera;
        Vector3 centre = anchor.transform.position;
        Vector3 viewport = cam.WorldToViewportPoint(centre);
        Assert.IsTrue(viewport.z > cam.nearClipPlane, "centre beyond near clip");
        Assert.IsTrue(viewport.z < cam.farClipPlane, "centre within far clip");
        Assert.IsTrue(viewport.x >= -0.05f && viewport.x <= 1.05f
                      && viewport.y >= -0.05f && viewport.y <= 1.05f,
            "centre inside viewport (v=" + viewport.ToString("0.000") + ")");

        // Unity Quad readable front is -transform.forward; it must face back
        // toward the camera (this is what prevents the mirrored text).
        float facing = Vector3.Dot(anchor.FrontNormal,
            (cam.transform.position - centre).normalized);
        Assert.Greater(facing, 0.5f, "display front normal faces the camera");

        // Projected size sanity: the wrist must not dominate the whole screen.
        Vector3 scale = anchor.transform.localScale;
        float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 corner = centre + anchor.transform.right * (sx * scale.x * 0.5f)
                                 + anchor.transform.up * (sy * scale.y * 0.5f);
                Vector3 c = cam.WorldToViewportPoint(corner);
                if (c.z > 0f)
                {
                    minX = Mathf.Min(minX, c.x);
                    maxX = Mathf.Max(maxX, c.x);
                    minY = Mathf.Min(minY, c.y);
                    maxY = Mathf.Max(maxY, c.y);
                }
            }
        }

        float widthFraction = Mathf.Clamp01(maxX - minX);
        float heightFraction = Mathf.Clamp01(maxY - minY);
        Assert.IsTrue(widthFraction <= 0.65f && widthFraction > 0.03f,
            "projected width sane (was " + (widthFraction * 100f).ToString("0") + "%)");
        Assert.IsTrue(heightFraction <= 0.70f && heightFraction > 0.03f,
            "projected height sane (was " + (heightFraction * 100f).ToString("0") + "%)");

        // UV orientation: display-right world direction maps to larger u
        // (RT right); display-top world direction maps to larger v (RT top).
        Vector3 p = anchor.transform.position;
        float uvLeft = anchor.WorldPointToUV(p - anchor.transform.right * 0.2f).x;
        float uvRight = anchor.WorldPointToUV(p + anchor.transform.right * 0.2f).x;
        float uvBottom = anchor.WorldPointToUV(p - anchor.transform.up * 0.2f).y;
        float uvTop = anchor.WorldPointToUV(p + anchor.transform.up * 0.2f).y;
        Assert.Less(uvLeft, uvRight, "display right -> RT right (no horizontal flip)");
        Assert.Less(uvBottom, uvTop, "display top -> RT top (no vertical flip)");

        controller.SetWristOpen(false);
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        Assert.IsFalse(wristDoc.enabled, "wrist doc disabled after close");
        if (anchor.SurfaceRenderer != null)
        {
            Assert.IsFalse(anchor.SurfaceRenderer.enabled, "renderer disabled after close");
        }
    }

    [UnityTest]
    public IEnumerator MapMathAndNavigationSelection()
    {
        Vector2 center = SurfaceGameController.MapFraction(Vector3.zero);
        AssertNearly(0f, center.x, "map center x");
        AssertNearly(0f, center.y, "map center y");

        Vector2 nodeA = SurfaceGameController.MapFraction(new Vector3(-7f, 0f, 2.5f));
        AssertNearly(-7f / SurfaceGameController.MapHalfExtent, nodeA.x, "node fraction x");
        AssertNearly(2.5f / SurfaceGameController.MapHalfExtent, nodeA.y, "node fraction z");

        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        SurfacePlayerController player = controller.Player;

        player.TeleportTo(new Vector3(0f, 0f, 3f));
        yield return null;
        controller.SetWristOpen(true);
        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        VisualElement root = Wrist().RootElement;
        Assert.IsNotNull(root, "wrist root");

        // The wrist document renders into its own RenderTexture; batch mode
        // provides no layout pass for it, so containment is verified from the
        // deterministic style coordinates (the same math as LocalMap hit-test).
        Vector2 mapLocalCenter = new Vector2(SurfaceWristController.MapSize * 0.5f,
            SurfaceWristController.MapSize * 0.5f);
        for (int i = 0; i < 3; i++)
        {
            VisualElement marker = root.Q<VisualElement>("MapNode" + i);
            Assert.IsNotNull(marker, "map marker " + i);
            Vector2 markerCenter = new Vector2(
                marker.style.left.value.value + 17f, marker.style.top.value.value + 17f);
            float distance = Vector2.Distance(markerCenter, mapLocalCenter);
            Assert.IsTrue(distance <= SurfaceWristController.MapRadiusPx + 1f,
                "marker " + i + " stays inside circle (d=" + distance.ToString("0.0") + ")");
        }

        controller.SetWristOpen(false);
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        UIDocument hudDoc = HudDocument();
        Assert.IsTrue(controller.TrySelectNavigationTarget(1), "select node 1");
        Assert.AreEqual(1, controller.NavigationTargetNodeId, "nav target id");
        Assert.IsTrue(controller.IsNavigationActive, "nav active");
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        VisualElement navBlock = hudDoc.rootVisualElement.Q<VisualElement>("HudNavBlock");
        Assert.IsTrue(navBlock.resolvedStyle.display == DisplayStyle.Flex, "HUD nav visible");
        Assert.AreEqual("◆ 资源节点 02", LabelText(hudDoc.rootVisualElement, "HudNavName"),
            "nav name text");
        string distanceText = LabelText(hudDoc.rootVisualElement, "HudNavDistance");
        Assert.IsTrue(distanceText.EndsWith(" m"), "distance shown in meters");
        float shown = float.Parse(distanceText.Replace(" m", ""));
        float real = Vector3.Distance(player.transform.position,
            controller.Nodes[1].transform.position);
        Assert.IsTrue(Mathf.Abs(shown - real) < 0.6f, "distance matches real world distance");

        controller.SetWristOpen(true);
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        root = Wrist().RootElement;
        Assert.AreEqual("资源节点 02", LabelText(root, "WristNavName"), "wrist nav name");
        VisualElement ring = root.Q<VisualElement>("MapNavRing");
        Assert.IsTrue(ring.resolvedStyle.display == DisplayStyle.Flex, "nav ring visible");
        VisualElement marker1 = root.Q<VisualElement>("MapNode1");
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

        Assert.IsTrue(controller.TrySelectNavigationTarget(0), "select node 0");
        player.TeleportTo(controller.Nodes[0].transform.position + Vector3.forward * 1.2f);
        yield return null;
        Assert.IsTrue(controller.TryDeployNearest(), "deploy node 0");
        SurfaceResourceNode node0 = controller.Nodes[0];
        node0.AdvanceTimers(node0.RemainingSeconds);
        node0.AdvanceTimers(node0.RemainingSeconds);
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

        player.TeleportTo(controller.Nodes[0].transform.position + Vector3.forward * 1.2f);
        yield return null;
        Assert.IsTrue(controller.TryDeployNearest(), "deploy node 0");
        SurfaceResourceNode node0 = controller.Nodes[0];

        controller.SetWristOpen(true);
        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        VisualElement root = Wrist().RootElement;
        Assert.AreEqual("资源节点 01", LabelText(root, "WristActiveNode"), "active node name");
        Assert.AreEqual("采集中", LabelText(root, "WristActiveState"), "wrist shows harvesting");
        Assert.IsTrue(LabelText(root, "WristActivePercent").EndsWith("%"), "progress percent shown");
        Assert.IsTrue(root.Q<VisualElement>("MapNode0").ClassListContains("marker-harvesting"),
            "map marker harvesting");

        node0.AdvanceTimers(0.75f);
        yield return null;
        yield return null;
        string percentText = LabelText(root, "WristActivePercent").Replace("%", "");
        int percent = int.Parse(percentText);
        Assert.Greater(percent, 0, "progress advanced while wrist open");

        node0.AdvanceTimers(node0.RemainingSeconds);
        yield return null;
        yield return null;
        Assert.AreEqual("无线传输中", LabelText(root, "WristActiveState"), "wrist shows transfer");
        Assert.IsTrue(root.Q<VisualElement>("MapNode0").ClassListContains("marker-transmitting"),
            "map marker transmitting");

        node0.AdvanceTimers(node0.RemainingSeconds);
        yield return null;
        yield return null;
        yield return null;
        Assert.AreEqual(SurfaceResourceNode.NodeState.Depleted, node0.State, "depleted");
        Assert.AreEqual(25, session.Minerals, "settled +5 while wrist open");
        Assert.IsTrue(root.Q<VisualElement>("MapNode0").ClassListContains("marker-depleted"),
            "map marker depleted");
        controller.SetWristOpen(false);
    }

    [UnityTest]
    public IEnumerator Landing_E_ReturnToShip()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        SurfacePlayerController player = controller.Player;

        player.TeleportTo(new Vector3(0.8f, 0f, 0.8f));
        yield return null;
        Vector3 toPad = Vector3.zero - player.transform.position;
        player.SetFacing(Mathf.Atan2(toPad.x, toPad.z) * Mathf.Rad2Deg, 0f);
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Assert.IsTrue(controller.Interactor.HasValidTarget, "landing area is interactable");
        Assert.AreEqual("E  返回舰船", controller.Interactor.ResolvePrompt(), "landing prompt text");

        Assert.IsTrue(controller.Interactor.TryInteract(), "E return accepted");
        yield return WaitUntil(() => ActiveSceneName() == BootController.HubSceneName);
        Assert.AreEqual(BootController.HubSceneName, ActiveSceneName(), "back on the Hub");

        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });
        Assert.IsNotNull(app, "hub UI ready after landing return");

        // HubUiMode semantics: the hub explicitly restores the cursor and the
        // 2D screen remains interactive (navigation reaches another screen).
        app.ApplyHubUiMode();
        app.ShowScreen(AppScreen.Preparation);
        yield return null;
        yield return null;
        Assert.AreEqual(AppScreen.Preparation, app.CurrentScreen, "hub navigation still interactive");
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }
}
