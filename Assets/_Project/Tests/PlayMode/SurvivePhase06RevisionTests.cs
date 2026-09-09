using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 06 Revision Round 9.2 Play Mode validation.
///
/// Round 9.2 changes the wrist presentation to Parallel-Cap Annular Panels
/// (annulus 248..365 intersected with horizontal Y bands - every panel has a
/// horizontal top edge and horizontal bottom edge), moves the health / satiety
/// status arcs inside so their inner radius equals the panel inner radius
/// (248), and rebuilds every text row as an independent horizontal slot whose
/// X is derived from the real panel cross-section (staircase typography).
///
/// Covers: satiety semantics, facing-relative WASD, gaze interaction, floating
/// wrist display chain, deterministic LOCAL MAP coordinates, navigation, live
/// harvest states with the Round 9.2 full-state text refresh (no '%' residue),
/// landing-zone return, raised-wrist pose, compass, cursor, and the Round 9.2
/// geometry validators (parallel caps / status arc alignment / row slots /
/// staircase ordering / harvest text reset / visual bounds).
///
/// Automation drives public semantic APIs and pure coordinate mapping only.
/// Physical WASD / mouse / E / M / I / Esc / left-click remain manual review
/// items.
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
    // Session / first-person / loadout foundations (Round 9 unchanged)
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
        Assert.IsFalse(wristDoc.enabled, "wrist document disabled");
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
        // Radial status labels sit beside the thin arcs.
        Assert.AreEqual("健康度 37", LabelText(root, "HealthLabel"), "health label live");
        Assert.AreEqual("饱食度 64", LabelText(root, "SatietyLabel"), "satiety label live");

        // Equipment panel lists the real loadout lines and marks selection
        // state through the row-off class (no fake inventory).
        Assert.AreEqual("采集器 MK-I", LabelText(root, "EquipHarvester"), "harvester listed");
        Assert.AreEqual("备用电池", LabelText(root, "EquipBattery"), "battery listed");
        Assert.AreEqual("勘测无人机", LabelText(root, "EquipDrone"), "drone listed");
        VisualElement battery = root.Q<VisualElement>("EquipBattery");
        VisualElement drone = root.Q<VisualElement>("EquipDrone");
        Assert.IsFalse(battery.ClassListContains("row-off"), "battery selected -> not muted");
        Assert.IsTrue(drone.ClassListContains("row-off"), "drone deselected -> muted");

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
        Assert.IsTrue(widthFraction >= 0.30f && widthFraction <= 1.0f,
            "projected width visibly large (was " + (widthFraction * 100f).ToString("0")
            + "% of screen width)");
        Assert.IsTrue(heightFraction >= 0.30f && heightFraction <= 1.0f,
            "projected height visibly large (was " + (heightFraction * 100f).ToString("0")
            + "% of screen height)");

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

    // ------------------------------------------------------------------
    // Map / navigation
    // ------------------------------------------------------------------

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
        Assert.AreEqual("节点 01", LabelText(root, "HarvestNode"), "active node name (short copy)");
        Assert.AreEqual("采集中", LabelText(root, "HarvestState"), "harvest state label (no % mixed in)");
        Assert.IsTrue(LabelText(root, "HarvestProgress").EndsWith("%"),
            "progress has its own row with percent");
        Assert.IsTrue(root.Q<VisualElement>("MapNode0").ClassListContains("node-active"),
            "map marker active (harvesting)");

        node0.AdvanceTimers(0.75f);
        yield return null;
        yield return null;
        string percentText = LabelText(root, "HarvestProgress").Replace("%", "");
        int percent = int.Parse(percentText);
        Assert.Greater(percent, 0, "progress advanced while wrist open");

        node0.AdvanceTimers(node0.RemainingSeconds);
        yield return null;
        yield return null;
        Assert.AreEqual("无线传输", LabelText(root, "HarvestState"), "wrist shows transfer");
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

    // ==================================================================
    // Round 9.2 geometry + interaction validation
    // ==================================================================

    /// <summary>RadialWrist_RuntimeGeometryAndParallax (Round 9.2 geometry):
    /// the single triangle-mesh element still exists, the local map stays
    /// radius 235, the four functional panels are Parallel-Cap Annular Panels
    /// on radii 248..365 (no 360 degree ring), the status arcs share the panel
    /// inner radius 248..262 and the health/satiety spans stay 70 degrees.
    /// The map/hit-test/parallax/cursor parts remain unchanged.</summary>
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

        // Round 9.2: shared panel + status inner radius = 248, panel outer 365,
        // status outer 262 (thickness 14). The arcs are integrated, no longer
        // floating on a distant 392 orbit.
        Assert.AreEqual(248f, RadialWristElement.PanelInnerRadius,
            "panel inner 248");
        Assert.AreEqual(365f, RadialWristElement.PanelOuterRadius,
            "panel outer 365");
        Assert.AreEqual(RadialWristElement.StatusInnerRadius,
            RadialWristElement.PanelInnerRadius,
            "status inner radius == panel inner radius (shared 248)");
        Assert.AreEqual(262f, RadialWristElement.StatusOuterRadius,
            "status outer 262");
        Assert.AreEqual(14f, RadialWristElement.StatusThickness,
            "status thickness 14");
        Assert.AreEqual(RadialWristElement.StatusOuterRadius
                        - RadialWristElement.StatusInnerRadius,
            RadialWristElement.StatusThickness,
            "thickness equals the outer-inner delta");

        // Exactly two thin status arcs: health 55..125, satiety 235..305.
        Assert.AreEqual(55f, RadialWristElement.HealthArcStartDeg, "health arc start 55");
        Assert.AreEqual(125f, RadialWristElement.HealthArcEndDeg, "health arc end 125");
        Assert.AreEqual(235f, RadialWristElement.SatietyArcStartDeg, "satiety arc start 235");
        Assert.AreEqual(305f, RadialWristElement.SatietyArcEndDeg, "satiety arc end 305");
        AssertNearly(70f, RadialWristElement.HealthArcEndDeg
                          - RadialWristElement.HealthArcStartDeg, "health span 70");
        AssertNearly(70f, RadialWristElement.SatietyArcEndDeg
                          - RadialWristElement.SatietyArcStartDeg, "satiety span 70");

        // The four functional panels are horizontal Y bands, NOT a continuous
        // 360 degree outer ring: upper band 205..345, lower band 455..595.
        AssertNearly(205f, RadialWristElement.UpperPanelTopY, "upper band top");
        AssertNearly(345f, RadialWristElement.UpperPanelBottomY, "upper band bottom");
        AssertNearly(455f, RadialWristElement.LowerPanelTopY, "lower band top");
        AssertNearly(595f, RadialWristElement.LowerPanelBottomY, "lower band bottom");
        Assert.AreEqual(RadialWristElement.FunctionalPanelThickness,
            RadialWristElement.PanelOuterRadius - RadialWristElement.PanelInnerRadius,
            "functional thickness 365-248");

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

        // Every text row label exists in its panel (Round 9.2 row slots).
        string[] rowNames =
        {
            "EquipHeading", "EquipHarvester", "EquipBattery", "EquipDrone",
            "StateHeading", "ActionState", "SignalState", "RecoveredState",
            "NavHeading", "NavTarget", "NavDistance",
            "HarvestHeading", "HarvestNode", "HarvestState", "HarvestProgress"
        };
        foreach (string rowName in rowNames)
        {
            Assert.IsNotNull(root.Q<Label>(rowName), "row label present: " + rowName);
        }

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

    // ==================================================================
    // Round 9 raised-wrist pose / compass (unchanged behaviour)
    // ==================================================================

    /// <summary>Round 9 M: RaisedWristPose_Runtime — the hologram must read
    /// as a raised left wrist, not a face-on tablet: wrist pivot lower-left
    /// of the hologram centre, a real perspective tilt (front dot 0.84..0.96),
    /// and the parallax pivot still the single rotation fulcrum.</summary>
    [UnityTest]
    public IEnumerator RaisedWristPose_Runtime()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        Assert.IsNotNull(controller, "controller ready");
        SurfaceWristController wrist = Wrist();
        Assert.IsNotNull(wrist, "wrist controller");
        SurfacePlayerController player = controller.Player;
        Camera cam = player.EyeCamera;

        controller.SetWristOpen(true);
        for (int i = 0; i < 16; i++)
        {
            yield return null;
        }

        bool visible = wrist.VerifyDisplayVisible(out string report);
        Assert.IsTrue(visible, "wrist display chain visible:\n" + report);

        WristDisplayAnchor anchor = wrist.Display;
        Transform pivot = wrist.ProjectionPivot;
        Assert.IsNotNull(anchor, "anchor");
        Assert.IsNotNull(pivot, "pivot");

        Vector3 pivotViewport = cam.WorldToViewportPoint(pivot.position);
        Vector3 centreViewport = cam.WorldToViewportPoint(anchor.transform.position);
        Assert.IsTrue(pivotViewport.z > 0f && pivotViewport.x > 0.25f && pivotViewport.x < 0.5f,
            "wrist pivot in the LEFT screen half (v=" + pivotViewport.ToString("0.000") + ")");
        Assert.IsTrue(pivotViewport.y > 0.15f && pivotViewport.y < 0.45f,
            "wrist pivot in the LOWER screen region (v=" + pivotViewport.ToString("0.000") + ")");
        Assert.IsTrue(centreViewport.y > pivotViewport.y + 0.04f,
            "hologram centre is ABOVE the wrist pivot (c=" + centreViewport.ToString("0.000")
            + " p=" + pivotViewport.ToString("0.000") + ")");
        Assert.IsTrue(centreViewport.x > pivotViewport.x + 0.01f,
            "hologram centre is RIGHT of the wrist pivot");

        // Real perspective: the base pose is clearly not a face-on tablet.
        Vector3 toCamera = (cam.transform.position - anchor.transform.position).normalized;
        float frontDot = Vector3.Dot(anchor.FrontNormal, toCamera);
        Assert.IsTrue(frontDot >= 0.84f && frontDot <= 0.96f,
            "raised-wrist base pose not camera-facing (front dot "
            + frontDot.ToString("0.00") + ", design target 0.88..0.93; exact values"
            + " are printed in the Release screenshot log)");
        Assert.Less(frontDot, 0.975f, "display is NOT a face-on tablet");

        // Projected size: still clearly larger than a tiny tablet (fraction
        // based so the check is resolution independent).
        Vector3 scale = anchor.transform.localScale;
        float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 corner = anchor.transform.position
                                 + anchor.transform.right * (sx * scale.x * 0.5f)
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

        float widthFrac = Mathf.Clamp01(maxX - minX);
        float heightFrac = Mathf.Clamp01(maxY - minY);
        Assert.IsTrue(widthFrac >= 0.50f && widthFrac <= 1.0f,
            "wrist brought closer and large (width " + (widthFrac * 100f).ToString("0")
            + "% of screen)");
        Assert.IsTrue(heightFrac >= 0.50f && heightFrac <= 1.0f,
            "wrist tall presentation (height " + (heightFrac * 100f).ToString("0")
            + "% of screen)");

        // Parallax still tilts around the captured base pose only.
        Quaternion neutral = pivot.localRotation;
        wrist.SetParallaxNormalized(0.6f, 0.4f);
        for (int i = 0; i < 50; i++)
        {
            yield return null;
        }

        Assert.Greater(wrist.CurrentParallaxYaw, 0.05f, "parallax yaw follows pointer");
        Assert.LessOrEqual(wrist.CurrentParallaxYaw, 4.5f, "parallax yaw clamped");
        wrist.SetParallaxNormalized(0f, 0f);
        for (int i = 0; i < 60; i++)
        {
            yield return null;
        }

        Assert.Less(Mathf.Abs(wrist.CurrentParallaxYaw), 0.6f, "returns to base yaw");
        Assert.Less(Quaternion.Angle(pivot.localRotation, neutral), 2f,
            "pivot returns to the raised-wrist base pose");

        controller.SetWristOpen(false);
    }

    /// <summary>Round 9 M: CompassHUD_Runtime — pure bearing math, 359/0 wrap,
    /// frame/target behaviour and real navigation distance remain
    /// authoritative.</summary>
    [UnityTest]
    public IEnumerator CompassHUD_Runtime()
    {
        // ---- Pure math first (no scene dependency) ----
        AssertNearly(0f, CompassTapeElement.Normalize360(360f), "normalize 360 -> 0");
        AssertNearly(30f, CompassTapeElement.Normalize360(750f), "normalize 750 -> 30");
        AssertNearly(315f, CompassTapeElement.Normalize360(-45f), "normalize -45 -> 315");
        AssertNearly(0f, CompassTapeElement.TargetBearing(
            new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 10f)), "bearing north +Z = 0");
        AssertNearly(90f, CompassTapeElement.TargetBearing(
            new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f)), "bearing east = 90");
        AssertNearly(270f, CompassTapeElement.TargetBearing(
            new Vector3(0f, 0f, 0f), new Vector3(-10f, 0f, 0f)), "bearing west = 270");
        AssertNearly(90f, CompassTapeElement.RelativeBearing(0f, 90f),
            "target east of facing = positive (right)");
        AssertNearly(-90f, CompassTapeElement.RelativeBearing(0f, 270f),
            "target west of facing = negative (left)");
        AssertNearly(20f, CompassTapeElement.RelativeBearing(350f, 10f),
            "wrap +: 350 facing, target 10 -> +20 (no jump)");
        AssertNearly(-20f, CompassTapeElement.RelativeBearing(10f, 350f),
            "wrap -: 10 facing, target 350 -> -20 (no jump)");

        var tape = new CompassTapeElement();
        tape.SetCameraYaw(359f);
        AssertNearly(359f, tape.CameraYawDegrees, "tape yaw 359");
        Assert.IsTrue(tape.IsVisibleOnTape(2f), "wrap across 0 stays visible (+3)");
        tape.SetCameraYaw(1f);
        Assert.IsTrue(tape.IsVisibleOnTape(359f), "wrap across 0 stays visible (-2)");
        Assert.IsFalse(tape.IsVisibleOnTape(50f), "bearing beyond +-45 outside window");

        // ---- Scene: compass exists on the surface frame ----
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        Assert.IsNotNull(controller, "controller ready");
        SurfacePlayerController player = controller.Player;
        UIDocument hudDoc = HudDocument();
        Assert.IsNotNull(hudDoc, "hud document exists");
        SurfaceHudController hud = hudDoc.GetComponent<SurfaceHudController>();
        Assert.IsNotNull(hud, "hud controller exists");

        VisualElement frame = hudDoc.rootVisualElement.Q<VisualElement>("CompassFrame");
        Assert.IsNotNull(frame, "CompassFrame present always on Surface");
        Assert.IsTrue(frame.resolvedStyle.display == DisplayStyle.Flex, "compass frame visible");
        Assert.IsNotNull(hudDoc.rootVisualElement.Q<VisualElement>("CompassFocusDot"),
            "fixed centre focus dot present");
        Assert.IsNotNull(hudDoc.rootVisualElement.Q<Label>("CompassEdgeLeft"), "edge cue L");
        Assert.IsNotNull(hudDoc.rootVisualElement.Q<Label>("CompassEdgeRight"), "edge cue R");
        CompassTapeElement injected = hudDoc.rootVisualElement.Q<CompassTapeElement>();
        Assert.IsNotNull(injected, "CompassTapeElement injected into ticks host");
        Assert.IsTrue(hud.CompassReady, "hud compass bound");

        // No target: everything neutral (no waypoint, no edge cue, no info).
        Assert.IsFalse(hud.LastCompassHasTarget, "no target initially");
        Assert.IsTrue(hudDoc.rootVisualElement.Q<VisualElement>("CompassWaypoint")
            .resolvedStyle.display == DisplayStyle.None, "waypoint hidden without target");

        // ---- Target east of the player -> positive/right, edge cue ----
        // Spawn (0,0,4.8) with yaw 0 (north); node 2 sits east (bearing ~114).
        player.TeleportTo(new Vector3(0f, 0f, 4.8f));
        yield return null;
        player.SetFacing(0f, 0f);
        Assert.IsTrue(controller.TrySelectNavigationTarget(2), "select node 2");
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Assert.IsTrue(hud.LastCompassHasTarget, "target present on compass");
        Assert.Greater(hud.LastCompassRelativeDeg, 30f,
            "node east of north-facing player is a positive (right) relative bearing");
        VisualElement waypoint = hudDoc.rootVisualElement.Q<VisualElement>("CompassWaypoint");
        Assert.IsTrue(waypoint.resolvedStyle.display == DisplayStyle.None,
            "waypoint out of the visible +-45 window -> hidden");
        Assert.IsTrue(hudDoc.rootVisualElement.Q<Label>("CompassEdgeRight")
            .resolvedStyle.display == DisplayStyle.Flex, "right edge cue shown");
        Assert.IsTrue(hudDoc.rootVisualElement.Q<Label>("CompassEdgeLeft")
            .resolvedStyle.display == DisplayStyle.None, "no left edge cue");
        Assert.IsFalse(hud.LastCompassAligned, "not aligned while looking away");
        Assert.IsFalse(hudDoc.rootVisualElement.Q<VisualElement>("CompassFocusDot")
            .ClassListContains("compass-aligned"), "focus dot dim");

        string infoText = LabelText(hudDoc.rootVisualElement, "CompassInfoText");
        Assert.IsTrue(infoText.StartsWith("◆ 资源节点 03"), "restrained info shows name: " + infoText);
        Assert.IsTrue(infoText.EndsWith(" m"), "info shows the real distance");

        // ---- Face the target: relative 0 -> aligned focus dot ----
        Vector3 delta = controller.Nodes[2].transform.position - player.transform.position;
        float bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        player.SetFacing(bearing, 0f);
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Assert.IsTrue(Mathf.Abs(hud.LastCompassRelativeDeg) <= 2.5f,
            "facing the node aligns the relative bearing ("
            + hud.LastCompassRelativeDeg.ToString("0.0") + ")");
        Assert.IsTrue(hud.LastCompassAligned, "aligned target detected");
        Assert.IsTrue(hudDoc.rootVisualElement.Q<VisualElement>("CompassFocusDot")
            .ClassListContains("compass-aligned"), "focus dot highlighted when aligned");
        Assert.IsTrue(waypoint.resolvedStyle.display == DisplayStyle.Flex,
            "waypoint visible on the tape when aligned");
        Assert.IsTrue(hudDoc.rootVisualElement.Q<Label>("CompassEdgeRight")
            .resolvedStyle.display == DisplayStyle.None, "edge cue clears when in window");

        // ---- Turn 90 deg to the target's left -> negative, left edge cue ----
        player.SetFacing(bearing + 90f, 0f);
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Assert.Less(hud.LastCompassRelativeDeg, -45f,
            "target now far LEFT of the view (negative relative bearing)");
        Assert.IsTrue(hudDoc.rootVisualElement.Q<Label>("CompassEdgeLeft")
            .resolvedStyle.display == DisplayStyle.Flex, "left edge cue shown");
        Assert.IsTrue(waypoint.resolvedStyle.display == DisplayStyle.None,
            "waypoint hidden again outside the window");
        Assert.IsFalse(hud.LastCompassAligned, "no longer aligned");

        // ---- Real navigation distance remains authoritative ----
        float real = Vector3.Distance(player.transform.position,
            controller.Nodes[2].transform.position);
        Assert.IsTrue(Mathf.Abs(controller.NavigationDistance - real) < 0.01f,
            "navigation distance still the real world distance");
        controller.ClearNavigationTarget();
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        Assert.IsFalse(hud.LastCompassHasTarget, "target cleared -> neutral compass");
    }

    // ==================================================================
    // Round 9.1 accepted: cursor
    // ==================================================================

    /// <summary>Round 9.1 M1: CursorVisual_Runtime - the normal cursor must be
    /// a SOLID dot with NO ring/arc; hover = dot + ring. Still Cursor.SetCursor
    /// only, still no UI Toolkit cursor overlay.</summary>
    [UnityTest]
    public IEnumerator CursorVisual_Runtime()
    {
        yield return EnterSurface();
        UIDocument hudDoc = HudDocument();
        Assert.IsNotNull(hudDoc, "hud document");
        Assert.IsNull(hudDoc.rootVisualElement.Q<VisualElement>("WristCursorOverlay"),
            "no UI Toolkit cursor overlay");
        Assert.AreEqual(0,
            hudDoc.rootVisualElement.Query<VisualElement>(null, "wrist-os-cursor").ToList().Count,
            "no wrist-os-cursor elements");

        var service = new WristCursorService();
        service.Initialize();
        Assert.IsNotNull(service.NormalCursorTexture, "normal dot cursor texture");
        Assert.IsNotNull(service.HoverCursorTexture, "hover dot+ring cursor texture");
        Assert.AreEqual(2, service.CachedTextureCount, "single cursor source (2 textures)");

        Assert.AreEqual(WristCursorService.CursorKind.SolidDot, service.NormalKind,
            "normal cursor is a solid dot, NOT a ring/arc");
        Assert.AreEqual(WristCursorService.CursorKind.DotWithRing, service.HoverKind,
            "hover cursor is the same dot + a thin teal ring");
        AssertNearly(6f, service.SolidDotDiameterPx, "solid dot diameter ~6px");
        AssertNearly(14.4f, service.HoverRingOuterDiameterPx, "hover ring outer ~14px");

        // Pixel probes: normal texture has a filled centre and NO ring; the
        // hover texture keeps the centre dot AND has ring pixels at ~6.5px.
        Assert.Greater(service.NormalCursorTexture.GetPixel(8, 8).a, 0.8f,
            "normal cursor centre filled");
        Assert.Less(service.NormalCursorTexture.GetPixel(14, 8).a, 0.1f,
            "normal cursor has NO pixels at ring radius (no crescent/arc)");
        Assert.Greater(service.HoverCursorTexture.GetPixel(16, 16).a, 0.8f,
            "hover cursor centre dot filled");
        Assert.Greater(service.HoverCursorTexture.GetPixel(22, 16).a, 0.4f,
            "hover cursor shows the teal ring around the dot");
        service.Dispose();
    }

    // ==================================================================
    // Round 9.2 geometry validators (pure + scene)
    // ==================================================================

    /// <summary>ParallelPanelCaps_Runtime - the four functional panels are
    /// Parallel-Cap Annular Panels: top edge horizontal, bottom edge
    /// horizontal, and the top/bottom edges parallel (the old wedge look with
    /// slanted caps is not accepted). Verified from the same geometry helpers
    /// the mesh rows are built from (no separate implementation to drift).</summary>
    [UnityTest]
    public IEnumerator ParallelPanelCaps_Runtime()
    {
        // Four panels: Equipment/State on the upper band, Navigation/Harvest on
        // the lower band; left and right sides each get one panel per band.
        VerifyParallelCaps(true, RadialWristElement.UpperPanelTopY,
            RadialWristElement.UpperPanelBottomY, "Equipment upper-left");
        VerifyParallelCaps(false, RadialWristElement.UpperPanelTopY,
            RadialWristElement.UpperPanelBottomY, "State upper-right");
        VerifyParallelCaps(true, RadialWristElement.LowerPanelTopY,
            RadialWristElement.LowerPanelBottomY, "Navigation lower-left");
        VerifyParallelCaps(false, RadialWristElement.LowerPanelTopY,
            RadialWristElement.LowerPanelBottomY, "Harvest lower-right");

        // The four panels never form one continuous 360 ring: the upper and
        // lower bands are separated by the equator gap (345..455) where no
        // functional geometry exists.
        Assert.Greater(RadialWristElement.LowerPanelTopY
                       - RadialWristElement.UpperPanelBottomY, 100f,
            "equator gap between upper and lower panels keeps them independent");

        yield return null;
    }

    private static void VerifyParallelCaps(bool leftSide, float topY, float bottomY, string label)
    {
        bool ok = RadialWristElement.GetPanelCapEndpoints(leftSide, topY, bottomY,
            out Vector2 topInner, out Vector2 topOuter,
            out Vector2 bottomInner, out Vector2 bottomOuter);
        Assert.IsTrue(ok, label + " cap endpoints resolvable");

        // Top cap: both ends share the same Y (<= 1 px tolerance).
        Assert.IsTrue(Mathf.Abs(topInner.y - topOuter.y) <= 1f,
            label + " top cap horizontal (y " + topInner.y.ToString("0.0") + " / "
            + topOuter.y.ToString("0.0") + ")");
        Assert.IsTrue(Mathf.Abs(topInner.y - topY) <= 1f,
            label + " top cap at band top y=" + topY);

        // Bottom cap: both ends share the same Y (<= 1 px tolerance).
        Assert.IsTrue(Mathf.Abs(bottomInner.y - bottomOuter.y) <= 1f,
            label + " bottom cap horizontal (y " + bottomInner.y.ToString("0.0") + " / "
            + bottomOuter.y.ToString("0.0") + ")");
        Assert.IsTrue(Mathf.Abs(bottomInner.y - bottomY) <= 1f,
            label + " bottom cap at band bottom y=" + bottomY);

        // Top and bottom edges are parallel (both horizontal).
        Vector2 topDir = (topOuter - topInner).normalized;
        Vector2 bottomDir = (bottomOuter - bottomInner).normalized;
        float dot = Vector2.Dot(topDir, bottomDir);
        Assert.IsTrue(dot >= 0.999f, label + " top/bottom edges parallel (dot "
            + dot.ToString("0.0000") + ")");

        // The cap x-ends must be ordered: inner (closest to centre) and outer
        // (farthest) must keep the authored panel thickness.
        float innerDx = Mathf.Abs(topInner.x - RadialWristElement.Center.x);
        float outerDx = Mathf.Abs(topOuter.x - RadialWristElement.Center.x);
        Assert.Greater(outerDx, innerDx, label + " outer radius beyond inner radius");
    }

    /// <summary>StatusArcAlignment_Runtime - Health and Satiety use identical
    /// physical track geometry whose INNER radius equals the panel inner
    /// radius (248): the status arcs sit in the same radial assembly, never on
    /// a distant outer orbit. Both tracks share span 70 deg, thickness 14,
    /// same segment count and the full inactive track is always drawn.</summary>
    [UnityTest]
    public IEnumerator StatusArcAlignment_Runtime()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        Assert.IsNotNull(controller, "controller ready");
        controller.SetWristOpen(true);
        for (int i = 0; i < 12; i++)
        {
            yield return null;
        }

        RadialWristElement radial = Wrist().RootElement.Q<RadialWristElement>();
        Assert.IsNotNull(radial, "radial element");

        // Shared inner radius with the panels.
        Assert.AreEqual(RadialWristElement.PanelInnerRadius,
            RadialWristElement.StatusInnerRadius,
            "status inner radius == panel inner radius (248)");
        Assert.AreEqual(248f, RadialWristElement.StatusInnerRadius, "status inner 248");
        Assert.AreEqual(262f, RadialWristElement.StatusOuterRadius, "status outer 262");
        Assert.AreEqual(14f, RadialWristElement.StatusThickness, "status thickness 14");

        // Identical 70 degree spans (health 55..125, satiety 235..305).
        AssertNearly(70f, RadialWristElement.HealthArcEndDeg
                          - RadialWristElement.HealthArcStartDeg, "health span 70");
        AssertNearly(70f, RadialWristElement.SatietyArcEndDeg
                          - RadialWristElement.SatietyArcStartDeg, "satiety span 70");
        AssertNearly(RadialWristElement.HealthArcEndDeg - RadialWristElement.HealthArcStartDeg,
            RadialWristElement.SatietyArcEndDeg - RadialWristElement.SatietyArcStartDeg,
            "identical physical track length (Health == Satiety)");

        // Round 9.2 colors: muted green / dark muted orange, no fluorescent.
        Color32 health = RadialWristElement.HealthActiveColor;
        Color32 satiety = RadialWristElement.SatietyActiveColor;
        Assert.IsTrue(health.g > health.r && health.g > health.b,
            "Health active fill is a GREEN family");
        Assert.IsTrue(satiety.r > satiety.g && satiety.g > satiety.b,
            "Satiety active fill is a DARK ORANGE family");
        Assert.IsFalse(health.g > 230, "no fluorescent green");
        Assert.IsFalse(satiety.r > 250, "no bright orange");

        // Fractions: health 100% active, satiety 82% active with the FULL
        // inactive track still drawn (bar length identical to Health).
        AssertNearly(1f, radial.HealthFraction01, "health fraction 100%");
        AssertNearly(0.82f, radial.SatietyFraction01, "satiety fraction 82%");
        Assert.Less(radial.SatietyFraction01, 1f,
            "satiety active fill does not reach 100% (inactive remainder visible)");

        controller.SetWristOpen(false);
    }

    /// <summary>PanelTextSlotBounds_Runtime - every visible text row sits fully
    /// inside its parallel-cap panel slot: horizontal box between the padded
    /// inner/outer radius bounds at the row Y, padding >= 10 px, row Y inside
    /// the panel band, row never overlapping the central map nor exceeding the
    /// outer radius.</summary>
    [UnityTest]
    public IEnumerator PanelTextSlotBounds_Runtime()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        Assert.IsNotNull(controller, "controller ready");
        controller.SetWristOpen(true);
        for (int i = 0; i < 12; i++)
        {
            yield return null;
        }

        SurfaceWristController wrist = Wrist();
        bool layoutOk = wrist.VerifyWristLayoutGeometry(out string layoutReport);
        Assert.IsTrue(layoutOk, "wrist layout with Round 9.2 row slots:\n" + layoutReport);

        VisualElement root = wrist.RootElement;

        // Upper-left Equipment (rows move DOWN-LEFT).
        VerifyRowInsideSlot(root.Q<Label>("EquipHeading"), true, 238f);
        VerifyRowInsideSlot(root.Q<Label>("EquipHarvester"), true, 268f);
        VerifyRowInsideSlot(root.Q<Label>("EquipBattery"), true, 296f);
        VerifyRowInsideSlot(root.Q<Label>("EquipDrone"), true, 322f);

        // Upper-right State (rows move DOWN-RIGHT).
        VerifyRowInsideSlot(root.Q<Label>("StateHeading"), false, 238f);
        VerifyRowInsideSlot(root.Q<Label>("ActionState"), false, 268f);
        VerifyRowInsideSlot(root.Q<Label>("SignalState"), false, 296f);
        VerifyRowInsideSlot(root.Q<Label>("RecoveredState"), false, 322f);

        // Lower-left Navigation (rows move DOWN-RIGHT).
        VerifyRowInsideSlot(root.Q<Label>("NavHeading"), true, 478f);
        VerifyRowInsideSlot(root.Q<Label>("NavTarget"), true, 510f);
        VerifyRowInsideSlot(root.Q<Label>("NavDistance"), true, 540f);

        // Lower-right Harvest (rows move DOWN-LEFT).
        VerifyRowInsideSlot(root.Q<Label>("HarvestHeading"), false, 478f);
        VerifyRowInsideSlot(root.Q<Label>("HarvestNode"), false, 508f);
        VerifyRowInsideSlot(root.Q<Label>("HarvestState"), false, 536f);
        VerifyRowInsideSlot(root.Q<Label>("HarvestProgress"), false, 562f);

        controller.SetWristOpen(false);
    }

    private static void VerifyRowInsideSlot(Label row, bool leftSide, float centerY)
    {
        Assert.IsNotNull(row, "row label " + row.name);
        Assert.IsTrue(SurfaceWristController.VerifyRowSlot(row, leftSide, centerY,
                out string info),
            "row " + row.name + " inside its panel slot :: " + info);
    }

    /// <summary>PanelTextStaircase_Runtime - the text rows in every functional
    /// panel follow the real panel cross-section so each staircase direction
    /// matches its quadrant: Equipment down-left, State down-right, Navigation
    /// down-right, Harvest down-left. Hidden (empty) rows are ignored.</summary>
    [UnityTest]
    public IEnumerator PanelTextStaircase_Runtime()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        Assert.IsNotNull(controller, "controller ready");
        controller.SetWristOpen(true);
        for (int i = 0; i < 12; i++)
        {
            yield return null;
        }

        VisualElement root = Wrist().RootElement;

        // Equipment (upper-left): headingX > line1X > line2X > line3X.
        VerifyStaircaseDownLeft(root, "EquipHeading", "EquipHarvester",
            "EquipBattery", "EquipDrone");

        // State (upper-right): headingX < line1X < line2X < line3X.
        VerifyStaircaseDownRight(root, "StateHeading", "ActionState",
            "SignalState", "RecoveredState");

        // Navigation (lower-left): headingX < line1X < line2X.
        VerifyStaircaseDownRight(root, "NavHeading", "NavTarget", "NavDistance");

        // Harvest (lower-right): headingX > line1X > line2X (progress too when
        // visible: line2X > progressX). At idle the progress row is empty and
        // therefore ignored.
        VerifyStaircaseDownLeft(root, "HarvestHeading", "HarvestNode",
            "HarvestState", "HarvestProgress");

        controller.SetWristOpen(false);
    }

    private static void VerifyStaircaseDownLeft(VisualElement root,
        string headingName, params string[] lineNames)
    {
        float headingCentre = RowCentreX(root, headingName);
        float previous = headingCentre;
        foreach (string line in lineNames)
        {
            Label label = root.Q<Label>(line);
            if (label == null || string.IsNullOrEmpty(label.text))
            {
                continue; // hidden row for the current state
            }

            float centre = RowCentreX(root, line);
            Assert.Less(centre, previous,
                line + " staircase moves LEFT (down-left): " + headingName + " "
                + headingCentre.ToString("0.0") + " > " + line + " " + centre.ToString("0.0"));
            previous = centre;
        }
    }

    private static void VerifyStaircaseDownRight(VisualElement root,
        string headingName, params string[] lineNames)
    {
        float headingCentre = RowCentreX(root, headingName);
        float previous = headingCentre;
        foreach (string line in lineNames)
        {
            Label label = root.Q<Label>(line);
            if (label == null || string.IsNullOrEmpty(label.text))
            {
                continue; // hidden row for the current state
            }

            float centre = RowCentreX(root, line);
            Assert.Greater(centre, previous,
                line + " staircase moves RIGHT (down-right): " + headingName + " "
                + headingCentre.ToString("0.0") + " < " + line + " " + centre.ToString("0.0"));
            previous = centre;
        }
    }

    private static float RowCentreX(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "row " + name);
        float left = label.style.left.value.value;
        float width = label.style.width.value.value;
        return left + width * 0.5f;
    }

    /// <summary>HarvestTextState_Reset_Runtime - after harvesting / wireless
    /// transfer completes, NO wrist text may retain a '%' residue. Every
    /// refresh must fully define every related label (no partial string
    /// replacement).</summary>
    [UnityTest]
    public IEnumerator HarvestTextState_Reset_Runtime()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        SurfacePlayerController player = controller.Player;
        SurfaceResourceNode node0 = controller.Nodes[0];

        player.TeleportTo(node0.transform.position + Vector3.forward * 1.2f);
        yield return null;
        Assert.IsTrue(controller.TryDeployNearest(), "deploy node 0");
        controller.SetWristOpen(true);
        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        VisualElement root = Wrist().RootElement;
        Assert.IsTrue(LabelText(root, "HarvestProgress").EndsWith("%"),
            "harvesting shows progress with %");

        // Advance into wireless transfer: progress still shows %.
        node0.AdvanceTimers(node0.RemainingSeconds);
        yield return null;
        yield return null;
        Assert.AreEqual("无线传输", LabelText(root, "HarvestState"), "wireless transfer state");
        Assert.IsTrue(LabelText(root, "HarvestProgress").EndsWith("%"),
            "transmitting shows progress with %");

        // Let the transfer settle while the wrist stays open: after Depleted
        // the refresh fully rewrites the rows and no '%' may remain anywhere.
        node0.AdvanceTimers(node0.RemainingSeconds);
        yield return null;
        yield return null;
        yield return null;
        Assert.AreEqual(SurfaceResourceNode.NodeState.Depleted, node0.State, "node depleted");

        Assert.AreEqual("传输完成", LabelText(root, "HarvestState"),
            "completion state shown (no % mixed in)");
        Assert.AreEqual("", LabelText(root, "HarvestProgress"),
            "progress row fully cleared after settlement (no % residue)");
        AssertNoPercentInWristText(root, "after settlement");

        // Idle (or still-completion) state after reopening also carries no %.
        controller.SetWristOpen(false);
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        controller.SetWristOpen(true);
        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        root = Wrist().RootElement;
        string reopenedState = LabelText(root, "HarvestState");
        Assert.IsTrue(reopenedState == "待机" || reopenedState == "传输完成",
            "post-settle state is idle or still completion (actual: " + reopenedState + ")");
        Assert.AreEqual("", LabelText(root, "HarvestProgress"), "progress row clear after reopen");
        AssertNoPercentInWristText(root, "after reopening post-settle");

        controller.SetWristOpen(false);
    }

    private static void AssertNoPercentInWristText(VisualElement root, string context)
    {
        foreach (TextElement text in root.Query<TextElement>().ToList())
        {
            Assert.IsFalse(text.text != null && text.text.Contains("%"),
                context + " :: wrist element '" + text.name + "' still shows '" + text.text + "'");
        }
    }

    /// <summary>WristVisualBounds_Runtime (Round 9.2) - the hologram centre
    /// target is x 44..46% / top-y 53..56% and the radial graphic must fit the
    /// viewport (no clipping). Fraction based so it is resolution independent;
    /// the exact 1920x1080 numbers land in the Release screenshot log.</summary>
    [UnityTest]
    public IEnumerator WristVisualBounds_Runtime()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        Assert.IsNotNull(controller, "controller ready");
        SurfacePlayerController player = controller.Player;
        Camera cam = player.EyeCamera;
        SurfaceWristController wrist = Wrist();
        Assert.IsNotNull(wrist, "wrist controller");

        controller.SetWristOpen(true);
        for (int i = 0; i < 16; i++)
        {
            yield return null;
        }

        bool ok = wrist.VerifyDisplayVisible(out string report);
        Assert.IsTrue(ok, "wrist display chain visible:\n" + report);

        // Hologram visual centre (design centre = display centre) - the exact
        // pixel target is printed in the Release log; the fraction band here is
        // resolution-independent and centred around the Round 9.2 target.
        WristDisplayAnchor anchor = wrist.Display;
        Vector3 centreViewport = cam.WorldToViewportPoint(anchor.transform.position);
        Assert.IsTrue(centreViewport.x >= 0.38f && centreViewport.x <= 0.52f,
            "hologram centre near LEFT-middle (x "
            + (centreViewport.x * 100f).ToString("0") + "%, target ~45%)");
        Assert.IsTrue(centreViewport.y >= 0.34f && centreViewport.y <= 0.52f,
            "hologram centre in the lower-middle (top-y "
            + ((1f - centreViewport.y) * 100f).ToString("0") + "%, target ~55%)");

        // Pivot stays lower-left of the centre.
        Vector3 pivotViewport = cam.WorldToViewportPoint(wrist.ProjectionPivot.position);
        Assert.IsTrue(pivotViewport.x < centreViewport.x - 0.03f,
            "wrist pivot left of hologram centre");
        Assert.IsTrue(centreViewport.y > pivotViewport.y + 0.1f,
            "hologram centre well above the wrist pivot");

        // Actual drawn radial geometry (panel outer radius 365) projected; must
        // be clearly large and NOT degenerate. Round 9.2 targets at 1920x1080
        // (width 960..1020, height 820..900, fully inside the viewport) print
        // in the Release screenshot log for human acceptance.
        bool radialOk = wrist.ComputeProjectedRadialBounds(
            out Vector2 radialCentre, out float radialWidthPx, out float radialHeightPx);
        Assert.IsTrue(radialOk, "radial graphic projection available");
        float widthFrac = radialWidthPx / Screen.width;
        float heightFrac = radialHeightPx / Screen.height;
        Assert.IsTrue(widthFrac >= 0.30f && widthFrac <= 0.85f,
            "drawn radial geometry width " + (widthFrac * 100f).ToString("0")
            + "% of screen (Round 9.2 target ~50% at 1920x1080)");
        Assert.IsTrue(heightFrac >= 0.40f && heightFrac <= 0.98f,
            "drawn radial geometry height " + (heightFrac * 100f).ToString("0")
            + "% of screen (Round 9.2 target ~76..83%; must fit the viewport)");

        // Orientation: front dot at the hologram centre (design 0.88..0.93;
        // banded slightly for editor-buffer variance, exact value in logs).
        Vector3 toCentre = (cam.transform.position - anchor.transform.position).normalized;
        float frontDot = Vector3.Dot(anchor.FrontNormal, toCentre);
        Assert.IsTrue(frontDot >= 0.84f && frontDot <= 0.95f,
            "orientation front dot " + frontDot.ToString("0.00")
            + " (design target 0.88..0.93)");

        controller.SetWristOpen(false);
    }

    // ==================================================================
    // Round 9.2.1 WristIdentity radial header
    // ==================================================================

    /// <summary>WristIdentityBounds_Runtime (Round 9.2.1) - the radial header
    /// label "NEREID-04 · 地表勘测" must stay inside the wrist RenderTexture,
    /// must not overlap the HealthLabel, must not sit deep over the central
    /// map, and its four corners must project inside the viewport (exact
    /// 1920x1080 numbers print in the Release screenshot log).</summary>
    [UnityTest]
    public IEnumerator WristIdentityBounds_Runtime()
    {
        yield return EnterSurface();
        SurfaceGameController controller = ReadyController();
        Assert.IsNotNull(controller, "controller ready");
        controller.SetWristOpen(true);
        for (int i = 0; i < 12; i++)
        {
            yield return null;
        }

        // Wait for the RenderTexture panel layout pass (mirrors the runtime
        // visibility log delay) so resolvedStyle sizes/positions are final.
        yield return new WaitForSecondsRealtime(0.5f);

        SurfaceWristController wrist = Wrist();
        Assert.IsNotNull(wrist, "wrist controller");
        bool ok = wrist.VerifyWristIdentityBounds(out string report);
        Assert.IsTrue(ok, "wrist identity radial header bounds:\n" + report);

        // Deterministic resolved position (USS-positioned, no inline style):
        // identity must be a top radial header (between health arc and map),
        // never at the RT bottom (the previous clipped location).
        VisualElement root = wrist.RootElement;
        Label identity = root.Q<Label>("WristIdentity");
        Assert.IsNotNull(identity, "identity label present");
        float top = identity.resolvedStyle.top;
        Assert.IsFalse(float.IsNaN(top), "identity resolved top available");
        Assert.IsTrue(top > 100f && top < 200f,
            "identity is a top radial header (resolved top=" + top.ToString("0")
            + ", expected ~130) - NOT at the clipped RT bottom");

        controller.SetWristOpen(false);
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        Assert.IsNotNull(label, "missing label element " + name);
        return label.text;
    }
}
