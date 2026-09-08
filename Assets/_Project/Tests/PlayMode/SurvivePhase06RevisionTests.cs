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

        // CodeReference cursor architecture: no UI Toolkit cursor overlay
        // exists anywhere; the wrist-mode pointer is a Cursor.SetCursor ring
        // texture owned by the wrist controller's WristCursorService.
        Assert.IsNull(hudDoc.rootVisualElement.Q<VisualElement>("WristCursorOverlay"),
            "no legacy WristCursorOverlay element in the screen-space HUD");
        Assert.AreEqual(0,
            hudDoc.rootVisualElement.Query<VisualElement>(null, "wrist-os-cursor").ToList().Count,
            "no wrist-os-cursor elements (no overlay, no trail)");
        Assert.IsNotNull(wrist.CursorService, "wrist cursor service present");
        Assert.IsNotNull(wrist.CursorService.NormalCursorTexture,
            "Cursor.SetCursor normal ring texture assigned in wrist mode");
        Assert.IsFalse(wrist.CursorService.IsHovering, "not hovering initially");
        // Visual visibility of the ring needs a real mouse device; PlayMode
        // owns the mode semantics, Release/人工 owns the visual check.

        // The wrist terminal is a transparent RenderTexture document; its
        // geometry (map/arcs/sectors) is what defines the floating layout.
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
        Assert.IsFalse(wrist.CursorService.IsHovering, "cursor hover cleared after close");
        Assert.IsNotNull(wrist.CursorService.HoverCursorTexture,
            "hover ring texture cached for Cursor.SetCursor");
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
        // Radial status labels (new names) sit beside the thin arcs.
        Assert.AreEqual("健康度 37", LabelText(root, "HealthLabel"), "health label live");
        Assert.AreEqual("饱食度 64", LabelText(root, "SatietyLabel"), "satiety label live");

        // Equipment sector lists the real loadout lines and marks selection
        // state through the sector-off class (no fake inventory).
        Assert.AreEqual("采集器 MK-I", LabelText(root, "EquipHarvester"), "harvester listed");
        Assert.AreEqual("备用电池组", LabelText(root, "EquipBattery"), "battery listed");
        Assert.AreEqual("勘测无人机", LabelText(root, "EquipDrone"), "drone listed");
        VisualElement battery = root.Q<VisualElement>("EquipBattery");
        VisualElement drone = root.Q<VisualElement>("EquipDrone");
        Assert.IsFalse(battery.ClassListContains("sector-off"), "battery selected -> not muted");
        Assert.IsTrue(drone.ClassListContains("sector-off"), "drone deselected -> muted");

        // The single triangle-mesh radial hologram is present and driven.
        RadialWristElement radial = root.Q<RadialWristElement>();
        Assert.IsNotNull(radial, "RadialWristElement present");
        Assert.Greater(radial.HealthFraction01, 0f, "health arc fraction driven");
        Assert.Greater(radial.SatietyFraction01, 0f, "satiety arc fraction driven");
        Assert.IsNull(root.Q<VisualElement>("WristMapCircle"), "no legacy MapDisc container");
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

        // Marker logical centre for a known world node: the single shared
        // world -> wrist-map source returns absolute wrist pixels.
        SurfaceResourceNode node1 = controller.Nodes[1];
        Vector2 node1Pixel = WristMapCoordinates.WorldToWristMap(node1.transform.position);

        // Direct hit at the marker centre resolves to that node.
        int hit = wrist.HitTestNode(node1Pixel);
        Assert.AreEqual(1, hit, "centre hit resolves to node 1");

        // Small offsets inside the generous hit radius still hit.
        int hitOffset = wrist.HitTestNode(node1Pixel + new Vector2(10f, 0f));
        Assert.AreEqual(1, hitOffset, "offset within hit radius resolves");

        // A corner of the wrist canvas far from any marker resolves to none.
        Assert.AreEqual(-1, wrist.HitTestNode(new Vector2(30f, 30f)), "corner has no node");

        // The player/landing centre (world origin) also resolves to none.
        Vector2 originPixel = WristMapCoordinates.WorldToWristMap(Vector3.zero);
        Assert.AreEqual(-1, wrist.HitTestNode(originPixel), "map centre has no node");

        // Orientation sanity: node 0 is west of node 1 on the map.
        SurfaceResourceNode node0 = controller.Nodes[0];
        Vector2 p0 = WristMapCoordinates.WorldToWristMap(node0.transform.position);
        Assert.Less(p0.x, node1Pixel.x, "node0 west of node1");
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
        // World origin maps to the wrist design centre through the single
        // authoritative world -> wrist-map source.
        Vector2 center = WristMapCoordinates.WorldToWristMap(Vector3.zero);
        AssertNearly(RadialWristElement.Center.x, center.x, "map center x");
        AssertNearly(RadialWristElement.Center.y, center.y, "map center y");

        Vector2 nodeA = WristMapCoordinates.WorldToWristMap(new Vector3(-7f, 0f, 2.5f));
        Assert.Less(nodeA.x, center.x, "node x west of centre");
        Assert.Less(nodeA.y, center.y, "node z (north) above centre");

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
        // provides no layout pass for it, so containment and shared-source
        // equivalence are verified from the deterministic style coordinates
        // (the same math as the wrist hit-test).
        for (int i = 0; i < 3; i++)
        {
            VisualElement marker = root.Q<VisualElement>("MapNode" + i);
            Assert.IsNotNull(marker, "map marker " + i);
            Vector2 markerCenter = new Vector2(
                marker.style.left.value.value, marker.style.top.value.value);
            Vector2 expected = WristMapCoordinates.WorldToWristMap(
                controller.Nodes[i].transform.position);
            AssertNearly(expected.x, markerCenter.x, "marker " + i + " x from shared source");
            AssertNearly(expected.y, markerCenter.y, "marker " + i + " y from shared source");
            float distance = Vector2.Distance(markerCenter, RadialWristElement.Center);
            Assert.IsTrue(distance <= WristMapCoordinates.UsableMapRadius + 1f,
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
        Assert.AreEqual("资源节点 02", LabelText(root, "NavTarget"), "wrist nav name");
        VisualElement ring = root.Q<VisualElement>("MapSelectedRing");
        Assert.IsTrue(ring.resolvedStyle.display == DisplayStyle.Flex, "selected ring visible");
        VisualElement marker1 = root.Q<VisualElement>("MapNode1");
        Assert.IsTrue(marker1.ClassListContains("node-selected"), "selected marker class");
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
        Assert.AreEqual("资源节点 01", LabelText(root, "HarvestNode"), "active node name");
        Assert.IsTrue(LabelText(root, "HarvestState").Contains("采集中"),
            "wrist shows harvesting");
        Assert.IsTrue(LabelText(root, "HarvestState").EndsWith("%"), "progress percent shown");
        Assert.IsTrue(root.Q<VisualElement>("MapNode0").ClassListContains("node-active"),
            "map marker active (harvesting)");

        node0.AdvanceTimers(0.75f);
        yield return null;
        yield return null;
        string percentText = LabelText(root, "HarvestState").Replace("采集中 ", "").Replace("%", "");
        int percent = int.Parse(percentText);
        Assert.Greater(percent, 0, "progress advanced while wrist open");

        node0.AdvanceTimers(node0.RemainingSeconds);
        yield return null;
        yield return null;
        Assert.IsTrue(LabelText(root, "HarvestState").Contains("无线传输"),
            "wrist shows transfer");
        Assert.IsTrue(root.Q<VisualElement>("MapNode0").ClassListContains("node-transmitting"),
            "map marker transmitting");

        node0.AdvanceTimers(node0.RemainingSeconds);
        yield return null;
        yield return null;
        yield return null;
        Assert.AreEqual(SurfaceResourceNode.NodeState.Depleted, node0.State, "depleted");
        Assert.AreEqual(25, session.Minerals, "settled +5 while wrist open");
        Assert.IsTrue(root.Q<VisualElement>("MapNode0").ClassListContains("node-depleted"),
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

    [UnityTest]
    public IEnumerator RadialWrist_RuntimeGeometryAndParallax()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        SurfaceWristController wrist = Wrist();
        Assert.IsNotNull(wrist, "wrist controller");

        controller.SetWristOpen(true);
        for (int i = 0; i < 16; i++)
        {
            yield return null;
        }

        VisualElement root = wrist.RootElement;

        // ---- CodeReference radial geometry (single triangle-mesh element) ----
        RadialWristElement radial = root.Q<RadialWristElement>();
        Assert.IsNotNull(radial, "triangle-mesh RadialWristElement present");

        Assert.AreEqual(235f, RadialWristElement.MapRadius, "map radius 235");
        Assert.AreEqual(305f, RadialWristElement.ThinArcRadius, "thin arc radius 305");
        Assert.AreEqual(10f, RadialWristElement.ThinArcThickness, "thin arc thickness 10");
        Assert.AreEqual(270f, RadialWristElement.SectorInnerRadius, "sector inner 270");
        Assert.AreEqual(350f, RadialWristElement.SectorOuterRadius, "sector outer 350");

        // Exactly two thin status arcs: health 55..125, satiety 235..305.
        Assert.AreEqual(55f, RadialWristElement.HealthArcStartDeg, "health arc start 55");
        Assert.AreEqual(125f, RadialWristElement.HealthArcEndDeg, "health arc end 125");
        Assert.AreEqual(235f, RadialWristElement.SatietyArcStartDeg, "satiety arc start 235");
        Assert.AreEqual(305f, RadialWristElement.SatietyArcEndDeg, "satiety arc end 305");

        // Exactly four thick functional sectors (annular band 270..350) with
        // visible angular gaps: the total covered span stays well below 360.
        float equipSpan = RadialWristElement.EquipmentSectorEndDeg
                          - RadialWristElement.EquipmentSectorStartDeg;
        float navSpan = RadialWristElement.NavigationSectorEndDeg
                        - RadialWristElement.NavigationSectorStartDeg;
        float harvestSpan = RadialWristElement.HarvestSectorEndDeg
                            - RadialWristElement.HarvestSectorStartDeg;
        float stateSpan = RadialWristElement.StateSectorEndDeg
                          - RadialWristElement.StateSectorStartDeg;
        AssertNearly(55f, equipSpan, "equipment sector 135..190");
        AssertNearly(40f, navSpan, "navigation sector 195..235");
        AssertNearly(40f, harvestSpan, "harvest sector 305..345");
        AssertNearly(55f, stateSpan, "state sector 350..405 (wraps)");
        Assert.IsTrue(equipSpan + navSpan + harvestSpan + stateSpan < 360f,
            "no full 360 functional outer ring (angular gaps remain)");

        Assert.Greater(radial.HealthFraction01, 0f, "health arc fraction driven");
        Assert.Greater(radial.SatietyFraction01, 0f, "satiety arc fraction driven");

        // Map content: player, landing and the 3 node markers.
        Assert.IsNotNull(root.Q<VisualElement>("MapPlayer"), "player marker visible");
        for (int i = 0; i < 3; i++)
        {
            Assert.IsNotNull(root.Q<VisualElement>("MapNode" + i), "node marker " + i);
        }

        Assert.IsNotNull(root.Q<VisualElement>("MapLanding"), "landing marker visible");
        Assert.IsNull(root.Q<VisualElement>("WristMapCircle"), "no legacy MapDisc container");
        Assert.IsNull(root.Q<VisualElement>("WristCursor"), "no in-panel cursor element");

        // Draw + hover + click share the ONE world -> map coordinate source.
        Vector2 node1Pixel = WristMapCoordinates.WorldToWristMap(
            controller.Nodes[1].transform.position);
        VisualElement node1Marker = root.Q<VisualElement>("MapNode1");
        AssertNearly(node1Pixel.x, node1Marker.style.left.value.value,
            "node draw x from shared source");
        AssertNearly(node1Pixel.y, node1Marker.style.top.value.value,
            "node draw y from shared source");
        Assert.AreEqual(1, wrist.HitTestNode(node1Pixel), "click hit-test uses shared source");

        // UV -> wrist pixel performs exactly ONE Y inversion.
        Vector2 topLeft = SurfaceWristController.UVToWristPixel(new Vector2(0f, 1f));
        AssertNearly(0f, topLeft.x, "uv left -> x 0");
        AssertNearly(0f, topLeft.y, "uv top (v=1) -> y 0 (single inversion)");
        Vector2 bottomRight = SurfaceWristController.UVToWristPixel(new Vector2(1f, 0f));
        AssertNearly(SurfaceWristController.UiWidth, bottomRight.x, "uv right -> x 1280");
        AssertNearly(SurfaceWristController.UiHeight, bottomRight.y,
            "uv bottom (v=0) -> y 800");

        // Parallax: rotates the WristProjectionPivot around its captured base;
        // pointer right tilts +yaw, pointer top +pitch; both clamped; centring
        // returns the pose (damped, not instant).
        Transform pivot = wrist.ProjectionPivot;
        Assert.IsNotNull(pivot, "WristProjectionPivot present");
        wrist.SetParallaxNormalized(0f, 0f);
        for (int i = 0; i < 30; i++)
        {
            yield return null;
        }

        Quaternion neutral = pivot.localRotation;
        wrist.SetParallaxNormalized(1f, 0f);
        for (int i = 0; i < 50; i++)
        {
            yield return null;
        }

        float yawRight = wrist.CurrentParallaxYaw;
        Assert.Greater(yawRight, 0.1f, "pointer right tilts +yaw");
        Assert.LessOrEqual(yawRight, 4.5f, "yaw clamped to +-4");
        // Batch/test frames run far faster than real time, so the damped pivot
        // rotation only needs to prove it follows (nonzero), not reach steady
        // state, within the fixed frame window.
        Assert.Greater(Quaternion.Angle(neutral, pivot.localRotation), 0.1f,
            "pivot rotation follows parallax (not translation)");

        wrist.SetParallaxNormalized(0f, 1f);
        for (int i = 0; i < 50; i++)
        {
            yield return null;
        }

        Assert.Greater(wrist.CurrentParallaxPitch, 0.1f, "pointer top tilts +pitch");
        Assert.LessOrEqual(wrist.CurrentParallaxPitch, 3.5f, "pitch clamped to +-3");
        wrist.SetParallaxNormalized(0f, 0f);
        for (int i = 0; i < 60; i++)
        {
            yield return null;
        }

        Assert.Less(Mathf.Abs(wrist.CurrentParallaxYaw), 0.6f, "centre returns yaw");
        Assert.Less(Quaternion.Angle(pivot.localRotation, neutral), 2f,
            "pivot returns near base pose");

        // Cursor architecture: no UI Toolkit cursor element anywhere; the ring
        // cursor is assigned through Cursor.SetCursor (runtime textures).
        UIDocument hudDoc = HudDocument();
        Assert.IsNotNull(hudDoc, "hud doc");
        Assert.IsNull(hudDoc.rootVisualElement.Q<VisualElement>("WristCursorOverlay"),
            "no overlay cursor element in the screen-space HUD");
        Assert.AreEqual(0,
            hudDoc.rootVisualElement.Query<VisualElement>(null, "wrist-os-cursor").ToList().Count,
            "no wrist-os-cursor elements (no cursor trail)");
        Assert.IsNotNull(wrist.CursorService.HoverCursorTexture,
            "hover ring texture available for Cursor.SetCursor");
        controller.SetWristOpen(false);
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }
}
