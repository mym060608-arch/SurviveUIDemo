using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B.2.2 acceptance - the two human blockers of the Release review:
///
///  1. the console UI was upside down in the Windows standalone Release player
///     although the previous diagnostic reported PASS,
///  2. the console screens behaved like a global HUD overlay (drawn through
///     walls and on top of the wrist).
///
/// Required runtime tests:
///  - ConsoleReleaseUvOrientation_Runtime
///  - ConsoleWorldOcclusion_Runtime
///  - WristOccludesConsoleScreen_Runtime
///  - ConsoleFocusProxyLifecycle_Runtime
///  - WorldConsolePointerOrientation_Runtime
///
/// The editor Test Runner does not render the game view backbuffer, so the
/// pixel-level Release evidence (TL/TR/BL/BR screenshot, magenta occlusion A/B)
/// is produced by ShipB22ShotProbe in the standalone player. These tests assert
/// the geometry, layer, camera, material and pointer invariants that make those
/// pixel results structurally impossible to violate.
/// </summary>
public class SurvivePhase07B22Tests
{
    private const int MaxWaitFrames = 2400;

    private static readonly Vector3[] ConsolePositions =
    {
        new Vector3(0f, 0f, 10.05f),
        new Vector3(-4.95f, 0f, 3.4f),
        new Vector3(4.95f, 0f, 3.4f),
        new Vector3(-2.2f, 0f, -8.85f)
    };

    private static readonly Vector3[] ScreenDirs =
    {
        new Vector3(0f, 0f, -1f),
        new Vector3(1f, 0f, 0f),
        new Vector3(-1f, 0f, 0f),
        new Vector3(0f, 0f, 1f)
    };

    // ---- helpers ----

    private static IEnumerator WaitUntil(System.Func<bool> condition,
        int maxFrames = MaxWaitFrames)
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
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() ? active.name : "<none>";
    }

    private static IEnumerator LoadShipScene()
    {
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.ShipInteriorSceneName);
        yield return null;
        yield return null;
    }

    private static IEnumerator WaitForShip(System.Action<ShipGameController> onReady)
    {
        ShipGameController controller = null;
        yield return WaitUntil(() =>
        {
            controller = Object.FindAnyObjectByType<ShipGameController>();
            return controller != null && controller.IsReady && controller.Player != null;
        });
        onReady(controller);
    }

    private static IEnumerator ApproachConsole(ShipGameController controller, int index,
        float distance = 1.7f)
    {
        SurfacePlayerController player = controller.Player;
        Vector3 approach = ConsolePositions[index] + ScreenDirs[index] * distance;
        player.TeleportTo(approach);
        float yaw = Mathf.Atan2(-ScreenDirs[index].x, -ScreenDirs[index].z) * Mathf.Rad2Deg;
        player.SetFacing(yaw, 0f);
        yield return null;
        yield return null;
        // Physics transforms must be synced before raycast/gaze assertions.
        Physics.SyncTransforms();
    }

    private static IEnumerator EnterConsole(ShipGameController controller, int index)
    {
        yield return ApproachConsole(controller, index);
        ShipConsoleInteractable console = controller.Consoles[index];
        FirstPersonInteractor interactor = controller.Interactor;
        for (int f = 0; f < 240; f++)
        {
            interactor.RefreshTarget();
            if (ReferenceEquals(interactor.CurrentTarget, console)
                && interactor.HasValidTarget)
            {
                break;
            }

            yield return null;
        }

        Assert.IsTrue(ReferenceEquals(interactor.CurrentTarget, console),
            "console " + console.ConsoleId + " is the gaze target");
        Assert.IsTrue(interactor.TryInteract(),
            "E enters console " + console.ConsoleId);
    }

    private static IEnumerator WaitFocused(ShipConsoleFocusController focus)
    {
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus);
        yield return null;
        yield return null;
    }

    private static IEnumerator WaitExplore(ShipConsoleFocusController focus)
    {
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore);
        yield return null;
    }

    private static WorldConsoleSurface SurfaceOf(ShipGameController controller, int index)
    {
        return controller.Consoles[index].GetComponent<WorldConsoleSurface>();
    }

    private static Camera SharpOf(Camera eye)
    {
        Transform t = eye.transform.Find("SharpScreenCamera");
        return t != null ? t.GetComponent<Camera>() : null;
    }

    private static WorldConsoleSurface[] ScreensOf(ShipGameController controller)
    {
        var screens = new WorldConsoleSurface[4];
        for (int i = 0; i < 4; i++)
        {
            screens[i] = SurfaceOf(controller, i);
        }

        return screens;
    }

    // ====================================================================
    // 1) ConsoleReleaseUvOrientation_Runtime
    //     The physical screen must show the panel's canonical corners at the
    //     canonical screen corners. This is asserted through the ONE authority
    //     (ConsoleUvAuthority) plus the real UI document, and it is the same
    //     transform the Release TL/TR/BL/BR screenshot measures.
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleReleaseUvOrientation_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        Camera eye = controller.Player.EyeCamera;

        for (int c = 0; c < 4; c++)
        {
            WorldConsoleSurface surface = SurfaceOf(controller, c);
            yield return WaitUntil(() => surface.IsReady);

            // Geometry: mesh v grows UP with the world, so no physical V flip
            // exists; mesh u runs along the quad's local +x while the viewer's
            // right hand points along local -x.
            Transform quad = surface.ScreenQuad;
            Assert.IsNotNull(quad, "console " + (c + 1) + " screen quad");
            Assert.IsTrue(Vector3.Dot(quad.up, Vector3.up) > 0.9f,
                "console " + (c + 1) + " quad V axis points up");
            Vector3 viewerForward = -quad.forward;
            Vector3 viewerRight = Vector3.Cross(Vector3.up, viewerForward).normalized;
            bool quadXPointsViewerLeft = Vector3.Dot(quad.right, viewerRight) < -0.9f;
            Assert.AreEqual(quadXPointsViewerLeft, ConsoleUvAuthority.FlipU > 0.5f,
                "console " + (c + 1) + " FlipU compensates the quad axis");

            // The ONE authority: a panel pixel must round-trip through the
            // canonical transform, and the canonical V axis is 0 at the UI
            // bottom / 1 at the UI top.
            Vector2 panelSize = new Vector2(WorldConsoleSurface.UiWidth,
                WorldConsoleSurface.UiHeight);
            Vector2 panelTopLeft = new Vector2(0f, 0f);
            Vector2 panelBottomLeft = new Vector2(0f, panelSize.y);
            Vector2 uvTopLeft = ConsoleUvAuthority.MeshUvFromPanelPixel(
                panelTopLeft, panelSize);
            Vector2 uvBottomLeft = ConsoleUvAuthority.MeshUvFromPanelPixel(
                panelBottomLeft, panelSize);
            Assert.IsTrue(uvTopLeft.y > uvBottomLeft.y,
                "console " + (c + 1) + ": UI top samples a HIGHER mesh v than "
                + "UI bottom (mesh v grows up with the world)");
            Assert.AreEqual(panelTopLeft, ConsoleUvAuthority.UvToPanelPixel(uvTopLeft,
                panelSize), "console " + (c + 1) + " panel top-left round-trips");
            Assert.AreEqual(panelBottomLeft, ConsoleUvAuthority.UvToPanelPixel(
                uvBottomLeft, panelSize),
                "console " + (c + 1) + " panel bottom-left round-trips");

            // The authoritative values really reach the physical material.
            Material material = surface.ScreenMaterial;
            Assert.IsNotNull(material, "console " + (c + 1) + " screen material");
            Assert.IsTrue(material.HasProperty(ConsoleUvAuthority.FlipUProperty),
                "console " + (c + 1) + " shader exposes _FlipU");
            Assert.IsTrue(material.HasProperty(ConsoleUvAuthority.FlipVProperty),
                "console " + (c + 1) + " shader exposes _FlipV");
            Assert.AreEqual(ConsoleUvAuthority.FlipU,
                material.GetFloat(ConsoleUvAuthority.FlipUProperty), 0.001f,
                "console " + (c + 1) + " uses the authoritative FlipU");
            Assert.AreEqual(ConsoleUvAuthority.FlipV,
                material.GetFloat(ConsoleUvAuthority.FlipVProperty), 0.001f,
                "console " + (c + 1) + " uses the authoritative FlipV");

            // The Release TL/TR/BL/BR diagnostic lives in the REAL UI document,
            // so the screenshot really exercises UI Toolkit -> PanelSettings ->
            // RenderTexture -> screen shader.
            Assert.IsTrue(surface.SetUvReferenceMarkersVisible(true),
                "console " + (c + 1) + " UV reference markers created");
            VisualElement root = surface.Document.rootVisualElement;
            string[] markerNames = { "UvRef_TL", "UvRef_TR", "UvRef_BL", "UvRef_BR" };
            foreach (string marker in markerNames)
            {
                Assert.IsNotNull(root.Q<VisualElement>(marker), marker + " present");
                Assert.IsNotNull(root.Q<Label>(marker + "_Label"),
                    marker + " carries its literal corner label");
            }

            surface.SetUvReferenceMarkersVisible(false);
        }

        // The canonical contract itself: U 0 = UI left, V 0 = UI bottom.
        Assert.AreEqual(1f, ConsoleUvAuthority.FlipU, 0.001f,
            "FlipU mirrors U exactly once");
        Assert.AreEqual(0f, ConsoleUvAuthority.FlipV, 0.001f,
            "FlipV must NOT flip: the UI Toolkit RenderTexture already carries "
            + "the panel bottom row at texture v = 0");

        // The focus proxy shares the same orientation authority.
        ConsoleFocusProxy proxy = Object.FindAnyObjectByType<ConsoleFocusProxy>(
            FindObjectsInactive.Include);
        Assert.IsNotNull(proxy, "focus proxy present");
        yield return EnterConsole(controller, 0);
        ShipConsoleFocusController focus = controller.Focus;
        yield return WaitFocused(focus);
        Assert.IsTrue(proxy.IsVisible, "proxy visible while focused");
        Material proxyMaterial = proxy.ProxyMaterial;
        Assert.IsNotNull(proxyMaterial, "proxy material instance");
        Assert.AreEqual(ConsoleUvAuthority.FlipU,
            proxyMaterial.GetFloat(ConsoleUvAuthority.FlipUProperty), 0.001f,
            "proxy uses the same authoritative FlipU");
        Assert.AreEqual(ConsoleUvAuthority.FlipV,
            proxyMaterial.GetFloat(ConsoleUvAuthority.FlipVProperty), 0.001f,
            "proxy uses the same authoritative FlipV");
        focus.RequestExit();
        yield return WaitExplore(focus);

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 2) ConsoleWorldOcclusion_Runtime
    //     Physical screens are ordinary world geometry: they use the base
    //     camera and its real depth, never an always-on-top overlay.
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleWorldOcclusion_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        Camera eye = controller.Player.EyeCamera;
        Camera sharp = SharpOf(eye);

        // Real depth chain: base camera renders the screens, the focus overlay
        // camera is disabled outside focus and never clears depth.
        var eyeData = eye.GetComponent<UniversalAdditionalCameraData>();
        var sharpData = sharp.GetComponent<UniversalAdditionalCameraData>();
        Assert.AreEqual(CameraRenderType.Base, eyeData.renderType,
            "environment camera is the stack base");
        Assert.AreEqual(CameraRenderType.Overlay, sharpData.renderType,
            "focus camera is an overlay");
        Assert.IsFalse(sharpData.clearDepth,
            "focus overlay camera must keep the base depth buffer");
        Assert.IsFalse(sharp.enabled,
            "focus overlay camera is DISABLED in ShipExplore");
        Assert.AreEqual(1 << WorldConsoleSurface.FocusOverlayLayer, sharp.cullingMask,
            "focus overlay camera renders only the proxy layer");

        foreach (WorldConsoleSurface surface in ScreensOf(controller))
        {
            int layer = surface.ScreenRenderer.gameObject.layer;
            Assert.AreEqual(WorldConsoleSurface.PhysicalScreenLayer, layer,
                "physical screen " + surface.ConsoleId + " is ordinary world geometry");
            Assert.AreNotEqual(WorldConsoleSurface.FocusOverlayLayer, layer,
                "physical screen " + surface.ConsoleId + " is not on the focus "
                + "overlay layer");
            Assert.IsTrue((eye.cullingMask & (1 << layer)) != 0,
                "physical screen " + surface.ConsoleId + " is rendered by the base "
                + "camera");
            Assert.IsTrue(surface.ScreenRenderer.enabled,
                "physical screen " + surface.ConsoleId + " renderer enabled");

            // The screen material is opaque world geometry: it writes depth in
            // the geometry queue (the proxy is the transparent one).
            Material material = surface.ScreenMaterial;
            Assert.IsNotNull(material, "screen material");
            Assert.Less(material.renderQueue, (int)RenderQueue.Transparent,
                "physical screen renders in the opaque queue with depth writes");
        }

        // The focus overlay layer holds exactly the one proxy quad.
        int overlayObjects = 0;
        foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(
                     FindObjectsInactive.Include))
        {
            if (renderer.gameObject.layer == WorldConsoleSurface.FocusOverlayLayer)
            {
                overlayObjects++;
                Assert.AreEqual("FocusScreenProxy", renderer.gameObject.name,
                    "only the focus proxy may live on the focus overlay layer");
                Assert.IsFalse(renderer.enabled,
                    "the proxy is disabled outside console focus");
            }
        }

        Assert.AreEqual(1, overlayObjects,
            "the focus overlay layer holds exactly one object");

        // Real physics precondition for the Release screenshot: a console seen
        // from the other side of the dividing wall is really occluded.
        SurfacePlayerController player = controller.Player;
        Vector3 viewpoint = new Vector3(2.6f, 0f, 4.5f);
        Vector3 origin = viewpoint + new Vector3(0f, 1.65f, 0f);
        Vector3 toScreen = SurfaceOf(controller, 0).ScreenQuad.position - origin;
        player.TeleportTo(viewpoint);
        player.SetFacing(Mathf.Atan2(-toScreen.x, -toScreen.z) * Mathf.Rad2Deg, 0f);
        yield return null;
        Physics.SyncTransforms();

        Vector3 screenCentre = SurfaceOf(controller, 0).ScreenQuad.position;
        Vector3 direction = (screenCentre - origin).normalized;
        Assert.IsTrue(Physics.Raycast(origin, direction, out RaycastHit hit, 40f),
            "the sight line to console 01 hits something");
        Assert.AreNotEqual("Screen", hit.collider.gameObject.name,
            "the first thing on the sight line is the WALL, not the console "
            + "screen (hit='" + hit.collider.gameObject.name + "')");
        Assert.Greater(hit.distance, 0.5f,
            "the blocker is real geometry, not the player's own capsule");

        // And the screen really is farther away than that blocker.
        Assert.Less(hit.distance, Vector3.Distance(origin, screenCentre),
            "the blocker sits between the eye and the console screen");

        PlayerBlockedGeometryCheck(controller, 0, origin, direction);

        GameSession.Ensure().ResetSession();
    }

    private static void PlayerBlockedGeometryCheck(ShipGameController controller,
        int index, Vector3 origin, Vector3 direction)
    {
        // The console must still be a normal, always-on world object even while
        // occluded: its renderer stays enabled and its RT stays lit.
        WorldConsoleSurface surface = SurfaceOf(controller, index);
        Assert.IsTrue(surface.ScreenRenderer.enabled,
            "the occluded console screen keeps rendering (world object, not a "
            + "camera-culled overlay)");
        Assert.IsTrue(surface.SampleScreenContent(out float lightRatio, out string summary),
            "the occluded console RT stays lit: " + summary);
        Assert.Greater(lightRatio, 0.35f, "occluded console RT light ratio");

        // The blocker is real world geometry on a layer the base camera renders.
        Camera eye = controller.Player.EyeCamera;
        if (Physics.Raycast(origin, direction, out RaycastHit hit, 40f))
        {
            int layer = hit.collider.gameObject.layer;
            Assert.IsTrue((eye.cullingMask & (1 << layer)) != 0,
                "the occluding wall is rendered by the base camera");
        }
    }

    // ====================================================================
    // 3) WristOccludesConsoleScreen_Runtime
    // ====================================================================

    [UnityTest]
    public IEnumerator WristOccludesConsoleScreen_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        Camera eye = controller.Player.EyeCamera;
        Camera sharp = SharpOf(eye);
        SurfaceWristController wrist = controller.Wrist;
        Assert.IsNotNull(wrist, "ship wrist bound");
        Assert.IsNotNull(wrist.Display, "wrist display anchor");

        // Stand in the spine looking at console 03 through its doorway, so a
        // distant console really is behind the raised wrist.
        yield return ApproachConsole(controller, 2, 4.6f);
        controller.SetWristOpen(true);
        yield return WaitUntil(() => controller.IsWristOpen);
        // Phase 09A_2: the existing terminal is revealed once the wrist raise
        // animation reaches its presentation point (immediately without arms).
        yield return WaitUntil(() => controller.IsWristTerminalRevealed);
        yield return null;
        yield return null;
        Assert.IsTrue(controller.IsWristOpen, "wrist opened in ShipExplore");

        // No console UI can be composited over the wrist: the screens are
        // opaque world geometry drawn by the base camera BEFORE the transparent
        // wrist, and no overlay camera is running at all.
        Assert.IsFalse(sharp.enabled,
            "no overlay camera may run while the wrist is open");
        ConsoleFocusProxy proxy = Object.FindAnyObjectByType<ConsoleFocusProxy>(
            FindObjectsInactive.Include);
        Assert.IsNotNull(proxy, "focus proxy present");
        Assert.IsFalse(proxy.IsVisible,
            "the focus proxy is invisible while the wrist is open");
        Assert.IsNull(proxy.ActiveSurface,
            "no console is bound to the proxy while the wrist is open");

        foreach (WorldConsoleSurface surface in ScreensOf(controller))
        {
            Assert.AreEqual(WorldConsoleSurface.PhysicalScreenLayer,
                surface.ScreenRenderer.gameObject.layer,
                "console " + surface.ConsoleId + " screen stays world geometry "
                + "with the wrist open");
            Material material = surface.ScreenMaterial;
            Assert.Less(material.renderQueue, (int)RenderQueue.Transparent,
                "console " + surface.ConsoleId
                + " screen stays opaque (the wrist composites over it)");
        }

        // The wrist display itself is a transparent world object in front of
        // the camera and must not be on the overlay layer.
        Renderer displayRenderer = wrist.Display.SurfaceRenderer;
        Assert.IsNotNull(displayRenderer, "wrist display renderer");
        Assert.IsTrue(displayRenderer.enabled, "wrist display renderer enabled");
        Assert.AreNotEqual(WorldConsoleSurface.FocusOverlayLayer,
            displayRenderer.gameObject.layer,
            "wrist display is not on the focus overlay layer");
        Assert.IsTrue((eye.cullingMask & (1 << displayRenderer.gameObject.layer)) != 0,
            "wrist display is rendered by the base camera");

        // The wrist is drawn in front of the camera: every console screen must
        // be strictly farther from the eye than the wrist disc.
        float wristDistance = Vector3.Distance(eye.transform.position,
            wrist.Display.transform.position);
        for (int i = 0; i < 4; i++)
        {
            float screenDistance = Vector3.Distance(eye.transform.position,
                SurfaceOf(controller, i).ScreenQuad.position);
            Assert.Greater(screenDistance, wristDistance,
                "console " + (i + 1) + " screen is behind the wrist disc");
        }

        // M / I gating: the wrist can never be open on a focused console, so
        // the sharp proxy and the wrist can never coexist.
        controller.SetWristOpen(false);
        yield return WaitUntil(() => !controller.IsWristOpen);
        yield return EnterConsole(controller, 2);
        ShipConsoleFocusController focus = controller.Focus;
        yield return WaitFocused(focus);
        controller.SetWristOpen(true);
        yield return null;
        yield return null;
        Assert.IsFalse(controller.IsWristOpen,
            "the wrist is refused while a console is focused");
        Assert.IsTrue(proxy.IsVisible, "the proxy is shown while focused");
        focus.RequestExit();
        yield return WaitExplore(focus);

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 4) ConsoleFocusProxyLifecycle_Runtime
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleFocusProxyLifecycle_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        Camera eye = controller.Player.EyeCamera;
        Camera sharp = SharpOf(eye);
        ShipConsoleFocusController focus = controller.Focus;
        ConsoleFocusProxy proxy = Object.FindAnyObjectByType<ConsoleFocusProxy>(
            FindObjectsInactive.Include);
        Assert.IsNotNull(proxy, "focus proxy present");
        Assert.IsTrue(proxy.VerifySetup(out string setupReport),
            "focus proxy setup: " + setupReport);
        Debug.Log("[07B22] proxy setup: " + setupReport);

        // ---- ShipExplore: hidden ----
        Assert.IsFalse(proxy.IsVisible, "proxy hidden in ShipExplore");
        Assert.IsNull(proxy.ActiveSurface, "no surface bound in ShipExplore");
        Assert.IsFalse(sharp.enabled, "overlay camera disabled in ShipExplore");
        Assert.IsNull(proxy.ProxyMaterial.GetTexture("_BaseMap"),
            "no RenderTexture is bound while the proxy is hidden");

        // ---- Wrist: still hidden ----
        controller.SetWristOpen(true);
        yield return WaitUntil(() => controller.IsWristOpen);
        yield return null;
        Assert.IsFalse(proxy.IsVisible, "proxy hidden in the wrist");
        Assert.IsFalse(sharp.enabled, "overlay camera disabled in the wrist");
        controller.SetWristOpen(false);
        yield return WaitUntil(() => !controller.IsWristOpen);

        // ---- TransitionIn: shown, bound to the active console ----
        yield return ApproachConsole(controller, 1);
        ShipConsoleInteractable console = controller.Consoles[1];
        FirstPersonInteractor interactor = controller.Interactor;
        for (int f = 0; f < 240; f++)
        {
            interactor.RefreshTarget();
            if (ReferenceEquals(interactor.CurrentTarget, console)
                && interactor.HasValidTarget)
            {
                break;
            }

            yield return null;
        }

        Assert.IsTrue(interactor.TryInteract(), "E enters console 02");
        Assert.AreEqual(ShipConsoleFocusController.ShipConsoleFocusState.TransitionIn,
            focus.State, "focus enters through TransitionIn");
        Assert.IsTrue(proxy.IsVisible, "proxy shown during TransitionIn");
        Assert.IsTrue(sharp.enabled, "overlay camera enabled during TransitionIn");

        WorldConsoleSurface active = SurfaceOf(controller, 1);
        Assert.IsTrue(ReferenceEquals(proxy.ActiveSurface, active),
            "proxy mirrors the active console surface");
        yield return WaitUntil(() => active.IsReady);
        Assert.AreEqual(active.CurrentRenderTexture,
            proxy.ProxyMaterial.GetTexture("_BaseMap"),
            "proxy samples the ACTIVE console RenderTexture");

        // Transform match: the proxy covers exactly the active screen.
        Transform quad = active.ScreenQuad;
        Assert.Less(Vector3.Distance(proxy.transform.position, quad.position),
            0.05f, "proxy is co-located with the active screen");
        Assert.Less(Quaternion.Angle(proxy.transform.rotation, quad.rotation), 0.5f,
            "proxy orientation matches the active screen");
        Vector3 proxyScale = proxy.transform.lossyScale;
        Assert.Less(Mathf.Abs(proxyScale.x - quad.lossyScale.x), 0.001f,
            "proxy width matches the active screen");
        Assert.Less(Mathf.Abs(proxyScale.y - quad.lossyScale.y), 0.001f,
            "proxy height matches the active screen");

        // ---- Focus: still shown, still exactly one proxy ----
        yield return WaitFocused(focus);
        Assert.IsTrue(proxy.IsVisible, "proxy visible while focused");
        Assert.IsTrue(sharp.enabled, "overlay camera enabled while focused");
        int visibleProxies = 0;
        foreach (ConsoleFocusProxy candidate in Object.FindObjectsByType<
                     ConsoleFocusProxy>(FindObjectsInactive.Include))
        {
            if (candidate.IsVisible)
            {
                visibleProxies++;
            }
        }

        Assert.AreEqual(1, visibleProxies, "at most ONE proxy is ever visible");

        // ---- Retarget: entering another console moves the same proxy ----
        focus.RequestExit();
        yield return WaitExplore(focus);
        Assert.IsFalse(proxy.IsVisible, "proxy hidden after the first exit");
        Assert.IsFalse(sharp.enabled, "overlay camera disabled after the exit");
        Assert.IsNull(proxy.ActiveSurface, "no surface bound after the exit");

        yield return EnterConsole(controller, 3);
        yield return WaitFocused(focus);
        WorldConsoleSurface second = SurfaceOf(controller, 3);
        Assert.IsTrue(ReferenceEquals(proxy.ActiveSurface, second),
            "the one proxy retargets to the newly focused console");
        Assert.AreEqual(second.CurrentRenderTexture,
            proxy.ProxyMaterial.GetTexture("_BaseMap"),
            "proxy samples the new console's RenderTexture");

        focus.RequestExit();
        yield return WaitExplore(focus);
        Assert.IsFalse(proxy.IsVisible, "proxy hidden after the second exit");
        Assert.IsFalse(sharp.enabled, "overlay camera disabled after the exit");
        Assert.IsTrue(SurfaceOf(controller, 3).ScreenRenderer.enabled,
            "the world screen keeps rendering after the exit");

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 5) WorldConsolePointerOrientation_Runtime
    //     Visual and pointer share ONE canonical transform.
    // ====================================================================

    [UnityTest]
    public IEnumerator WorldConsolePointerOrientation_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        WorldUiPointerRouter router = Object.FindAnyObjectByType<WorldUiPointerRouter>();
        Assert.IsNotNull(router, "world pointer router present");
        Camera eye = controller.Player.EyeCamera;

        yield return EnterConsole(controller, 2);
        yield return WaitFocused(focus);
        WorldConsoleSurface surface = SurfaceOf(controller, 2);
        yield return WaitUntil(() => surface.IsReady);
        yield return null;
        yield return null;

        // What the player SEES as the screen top-left must resolve to the panel
        // top-left, and bottom-right to bottom-right. A mirrored or 180-degree
        // rotated screen fails here.
        Transform quad = surface.ScreenQuad;
        Vector3 scale = quad.lossyScale;
        Vector3 viewerForward = -quad.forward;
        Vector3 viewerRight = Vector3.Cross(Vector3.up, viewerForward).normalized;
        Vector3 seenTopLeft = quad.position - viewerRight * (scale.x * 0.5f)
                              + Vector3.up * (scale.y * 0.5f);
        Vector3 seenBottomRight = quad.position + viewerRight * (scale.x * 0.5f)
                                  - Vector3.up * (scale.y * 0.5f);
        Vector2 vpTl = eye.WorldToViewportPoint(seenTopLeft);
        Vector2 vpBr = eye.WorldToViewportPoint(seenBottomRight);
        Assert.IsTrue(router.TryScreenToPanelPixel(
            new Vector2(vpTl.x * Screen.width, vpTl.y * Screen.height),
            out Vector2 panelTl), "seen top-left maps into the panel");
        Assert.IsTrue(router.TryScreenToPanelPixel(
            new Vector2(vpBr.x * Screen.width, vpBr.y * Screen.height),
            out Vector2 panelBr), "seen bottom-right maps into the panel");
        Assert.IsTrue(panelTl.x < panelBr.x && panelTl.y < panelBr.y,
            "seen top-left -> panel top-left, seen bottom-right -> panel "
            + "bottom-right (got " + panelTl + " vs " + panelBr + ")");

        // Four-corner agreement: each visible screen corner resolves to the
        // matching panel quadrant with a tight tolerance, so no flip, mirror or
        // 180-degree rotation can hide in the mapping.
        Vector2 panelSize = new Vector2(WorldConsoleSurface.UiWidth,
            WorldConsoleSurface.UiHeight);
        Vector2[] panelCorners =
        {
            new Vector2(0f, 0f), new Vector2(panelSize.x, 0f),
            new Vector2(0f, panelSize.y), new Vector2(panelSize.x, panelSize.y)
        };
        foreach (Vector2 corner in panelCorners)
        {
            Vector2 inset = Vector2.Lerp(corner,
                new Vector2(panelSize.x * 0.5f, panelSize.y * 0.5f), 0.12f);
            Assert.IsTrue(ConsoleScreenProjection.TryPanelPixelViewport(surface, eye,
                inset, out Vector2 viewport), "corner " + corner + " projects");
            Assert.IsTrue(router.TryScreenToPanelPixel(
                new Vector2(viewport.x * Screen.width, viewport.y * Screen.height),
                out Vector2 back), "corner " + corner + " maps back");
            Assert.Less(Mathf.Abs(back.x - inset.x), panelSize.x * 0.06f,
                "corner " + corner + " x round-trip");
            Assert.Less(Mathf.Abs(back.y - inset.y), panelSize.y * 0.06f,
                "corner " + corner + " y round-trip");
        }

        // A real UI button must be reachable exactly where it is displayed.
        Button batch5 = surface.Document.rootVisualElement
            .Q<Button>(ElementNames.Batch5Button);
        Assert.IsNotNull(batch5, "batch 05 button present");
        Assert.IsTrue(ConsoleScreenProjection.TryElementViewport(surface, eye,
            batch5, out Vector2 buttonViewport), "batch button projects on screen");
        Assert.IsTrue(router.TryScreenToPanelPixel(
            new Vector2(buttonViewport.x * Screen.width,
                buttonViewport.y * Screen.height),
            out Vector2 mappedBack), "button viewport maps back into the panel");
        Assert.IsTrue(batch5.worldBound.Contains(mappedBack),
            "pointer position matches the displayed button (mapped " + mappedBack
            + " inside " + batch5.worldBound + ")");
        Assert.Less(Vector2.Distance(mappedBack, batch5.worldBound.center),
            WorldConsoleSurface.UiWidth * 0.03f,
            "pointer round-trip stays within 3% of the panel width");

        // The proxy that the player is looking at carries the same orientation,
        // so the sharp copy can never disagree with the pointer either.
        ConsoleFocusProxy proxy = Object.FindAnyObjectByType<ConsoleFocusProxy>(
            FindObjectsInactive.Include);
        Assert.IsNotNull(proxy, "focus proxy present");
        Assert.IsTrue(proxy.IsVisible, "proxy visible while focused");
        Assert.AreEqual(ConsoleUvAuthority.FlipU,
            proxy.ProxyMaterial.GetFloat(ConsoleUvAuthority.FlipUProperty), 0.001f,
            "proxy FlipU matches the pointer authority");
        Assert.AreEqual(ConsoleUvAuthority.FlipV,
            proxy.ProxyMaterial.GetFloat(ConsoleUvAuthority.FlipVProperty), 0.001f,
            "proxy FlipV matches the pointer authority");

        focus.RequestExit();
        yield return WaitExplore(focus);
        GameSession.Ensure().ResetSession();
    }
}
