using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B.2.1 render-recovery acceptance (composition updated by Phase
/// 07B.2.2):
///  - the environment base camera renders the whole visible world INCLUDING the
///    four real physical console screens (ordinary world geometry with normal
///    depth) and excludes ONLY the focus-only ConsoleFocusOverlay layer,
///  - the focus camera is a single URP Overlay camera stacked once, enabled
///    only while a console is focused and never clearing depth,
///  - the ConsoleFocusOverlay layer holds exactly the one focus proxy quad,
///  - the ship WRIST is rendered by the base camera and is NOT on that layer,
///  - focus composition keeps the environment visible,
///  - the console UV orientation has ONE authority shared by the shader, the
///    pointer router and the diagnostics.
/// </summary>
public class SurvivePhase07B21Tests
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

    private static IEnumerator ApproachConsole(ShipGameController controller, int index)
    {
        SurfacePlayerController player = controller.Player;
        Vector3 approach = ConsolePositions[index] + ScreenDirs[index] * 1.7f;
        player.TeleportTo(approach);
        float yaw = Mathf.Atan2(-ScreenDirs[index].x, -ScreenDirs[index].z) * Mathf.Rad2Deg;
        player.SetFacing(yaw, 0f);
        yield return null;
        yield return null;
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

    private static Renderer FindRenderer(string name)
    {
        foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(
                     FindObjectsInactive.Include))
        {
            if (renderer.gameObject.name == name)
            {
                return renderer;
            }
        }

        return null;
    }

    // ====================================================================
    // 1) Base camera visibility mask (environment must never disappear)
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipBaseCameraVisibilityMask_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        yield return null;
        yield return null;

        int overlayLayer = WorldConsoleSurface.FocusOverlayLayer;
        int worldLayer = WorldConsoleSurface.PhysicalScreenLayer;
        Camera eye = controller.Player.EyeCamera;
        Assert.IsNotNull(eye, "eye camera");
        var eyeData = eye.GetComponent<UniversalAdditionalCameraData>();
        Assert.IsNotNull(eyeData, "eye camera URP data");
        Assert.AreEqual(CameraRenderType.Base, eyeData.renderType,
            "environment camera is a URP Base camera");
        Assert.AreEqual(~(1 << overlayLayer), eye.cullingMask,
            "base camera renders every layer except the focus-only overlay "
            + "(mask " + eye.cullingMask + ")");

        Camera sharp = SharpOf(eye);
        Assert.IsNotNull(sharp, "sharp screen camera present");
        var sharpData = sharp.GetComponent<UniversalAdditionalCameraData>();
        Assert.IsNotNull(sharpData, "sharp camera URP data");
        Assert.AreEqual(CameraRenderType.Overlay, sharpData.renderType,
            "sharp camera is an Overlay camera (never a second Base camera)");
        Assert.AreEqual(1 << overlayLayer, sharp.cullingMask,
            "sharp camera renders only the focus overlay layer");
        Assert.IsFalse(sharpData.renderPostProcessing,
            "sharp camera post-processing OFF");
        Assert.IsFalse(sharpData.clearDepth,
            "sharp camera keeps the base camera's depth buffer");
        Assert.AreEqual(CameraClearFlags.Nothing, sharp.clearFlags,
            "sharp camera never clears the frame");
        Assert.IsNotNull(eyeData.cameraStack, "base camera has a URP stack");
        Assert.AreEqual(1, eyeData.cameraStack.Count,
            "overlay camera stacked exactly once");
        Assert.AreEqual(sharp, eyeData.cameraStack[0], "stacked camera is the sharp one");

        // Full runtime visibility report (also logs the focus overlay objects).
        bool ok = controller.VerifyBaseCameraVisibility(out string report);
        Debug.Log("[07B21] base camera visibility: " + report);
        Assert.IsTrue(ok, "base camera visibility report: " + report);

        // Phase 07B.2.2: the four REAL physical screens are ordinary world
        // geometry on the base camera, and the focus-only overlay layer holds
        // exactly one object (the proxy quad).
        int physicalScreens = 0;
        int overlayObjects = 0;
        foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(
                     FindObjectsInactive.Include))
        {
            int objLayer = renderer.gameObject.layer;
            if (objLayer == overlayLayer)
            {
                overlayObjects++;
                continue;
            }

            if (renderer.gameObject.name == "Screen"
                && renderer.transform.parent != null
                && renderer.transform.parent.name.StartsWith("ShipConsole_"))
            {
                physicalScreens++;
                Assert.AreEqual(worldLayer, objLayer,
                    "physical screen " + renderer.transform.parent.name
                    + " is ordinary world geometry, not a HUD overlay");
                Assert.IsTrue((eye.cullingMask & (1 << objLayer)) != 0,
                    "the base camera renders the physical screen of "
                    + renderer.transform.parent.name);
            }
        }

        Assert.AreEqual(4, physicalScreens, "exactly four physical console screens");
        Assert.AreEqual(1, overlayObjects,
            "the focus-only overlay layer holds exactly one proxy quad");

        // Key visible environment objects (housing/floor/wall/door/locker/
        // airlock/wrist) must be on layers the base camera renders.
        string[] visibleNames =
        {
            "Deck_Floor", "Deck_Ceiling", "Bezel_Plate", "Locker_Body",
            "Hatch_Plate", "WristDisplay", "Pedestal"
        };
        foreach (string name in visibleNames)
        {
            Renderer renderer = FindRenderer(name);
            Assert.IsNotNull(renderer, name + " renderer exists");
            int objLayer = renderer.gameObject.layer;
            Assert.AreNotEqual(overlayLayer, objLayer,
                name + " is not on the focus overlay layer");
            Assert.IsTrue((eye.cullingMask & (1 << objLayer)) != 0,
                name + " (layer " + objLayer + ") is rendered by the base camera");
        }

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 2) Wrist visibility (base camera, never the ConsoleScreen layer)
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipWristCameraVisibility_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        SurfaceWristController wrist = controller.Wrist;
        Assert.IsNotNull(wrist, "ship wrist bound");
        Camera eye = controller.Player.EyeCamera;

        Renderer displayRenderer = wrist.Display != null
            ? wrist.Display.GetComponent<Renderer>()
            : null;
        Assert.IsNotNull(displayRenderer, "wrist display renderer");
        int displayLayer = displayRenderer.gameObject.layer;
        Assert.AreNotEqual(WorldConsoleSurface.FocusOverlayLayer, displayLayer,
            "wrist display is NOT on the focus-only overlay layer");
        Assert.IsTrue((eye.cullingMask & (1 << displayLayer)) != 0,
            "wrist display layer is rendered by the base camera");
        Assert.AreNotEqual(WorldConsoleSurface.FocusOverlayLayer,
            wrist.ProjectionPivot != null ? wrist.ProjectionPivot.gameObject.layer : -1,
            "wrist pivot is not on the focus-only overlay layer");

        // M / I (semantic):
        controller.SetWristOpen(true);
        yield return WaitUntil(() => controller.IsWristOpen);
        // Phase 09A_2: with the first-person arms present, the existing terminal is
        // revealed once the wrist raise animation reaches its presentation point
        // (without the arms it opens immediately), so wait for the presentation.
        yield return WaitUntil(() => controller.IsWristTerminalRevealed);
        yield return null;
        yield return null;
        yield return null;

        Assert.IsTrue(controller.IsWristOpen, "wrist opened from ShipExplore");
        Assert.IsTrue(displayRenderer.enabled, "wrist display renderer enabled");
        Assert.IsTrue(wrist.HasShipSchematic || wrist.IsShipMode,
            "ship wrist content active");
        Assert.IsFalse(controller.Player.InputEnabled, "movement frozen by wrist");

        controller.SetWristOpen(false);
        yield return WaitUntil(() => !controller.IsWristOpen);
        Assert.IsTrue(controller.Player.InputEnabled,
            "wrist closed returns to ShipExplore input");

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 3) Focus composition keeps the environment visible
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleCompositionWorldVisibility_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        Camera eye = controller.Player.EyeCamera;
        int overlayLayer = WorldConsoleSurface.FocusOverlayLayer;

        yield return EnterConsole(controller, 0);
        yield return WaitFocused(focus);

        Renderer bezel = FindRenderer("Bezel_Plate");
        Renderer floor = FindRenderer("Deck_Floor");
        Renderer wall = FindRenderer("Deck_Ceiling");
        Assert.IsNotNull(bezel, "console bezel renderer");
        Assert.IsNotNull(floor, "deck floor renderer");
        Assert.IsNotNull(wall, "deck ceiling renderer");

        foreach (Renderer renderer in new[] { bezel, floor, wall })
        {
            int objLayer = renderer.gameObject.layer;
            Assert.AreNotEqual(overlayLayer, objLayer,
                renderer.gameObject.name + " is not on the focus overlay layer");
            Assert.IsTrue((eye.cullingMask & (1 << objLayer)) != 0,
                renderer.gameObject.name + " stays visible while focused");
        }

        var eyeData = eye.GetComponent<UniversalAdditionalCameraData>();
        Assert.AreEqual(CameraRenderType.Base, eyeData.renderType,
            "composition unchanged by focus");
        Camera sharp = SharpOf(eye);
        Assert.AreEqual(1, eyeData.cameraStack.Count,
            "overlay camera still stacked exactly once");
        Assert.AreEqual(sharp, eyeData.cameraStack[0], "stack entry stable");
        Assert.AreEqual(1 << WorldConsoleSurface.FocusOverlayLayer, sharp.cullingMask,
            "overlay mask unchanged during focus");
        Assert.IsTrue(focus.BlurWeight > 0.8f, "environment blur engaged");

        focus.RequestExit();
        yield return WaitExplore(focus);
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 4) Console visual UV orientation (one authority)
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleVisualUvOrientation_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);

        for (int c = 0; c < 4; c++)
        {
            WorldConsoleSurface surface = SurfaceOf(controller, c);
            for (int f = 0; f < 600 && !surface.IsReady; f++)
            {
                yield return null;
            }

            Material material = surface.ScreenMaterial;
            Assert.IsNotNull(material, "console " + (c + 1) + " screen material");
            Assert.IsTrue(material.HasProperty(ConsoleUvAuthority.FlipUProperty),
                "console " + (c + 1) + " screen shader exposes _FlipU");
            Assert.IsTrue(material.HasProperty(ConsoleUvAuthority.FlipVProperty),
                "console " + (c + 1) + " screen shader exposes _FlipV");
            Assert.AreEqual(ConsoleUvAuthority.FlipU,
                material.GetFloat(ConsoleUvAuthority.FlipUProperty), 0.001f,
                "console " + (c + 1) + " uses the authoritative FlipU");
            Assert.AreEqual(ConsoleUvAuthority.FlipV,
                material.GetFloat(ConsoleUvAuthority.FlipVProperty), 0.001f,
                "console " + (c + 1) + " uses the authoritative FlipV");

            Transform quad = surface.ScreenQuad;
            Assert.IsNotNull(quad, "console " + (c + 1) + " screen quad");
            Assert.IsTrue(Vector3.Dot(quad.up, Vector3.up) > 0.9f,
                "console " + (c + 1) + " quad V axis points up (no physical V flip)");

            // Invariant of the single authority: the shader U flip must exactly
            // compensate the physical axis convention (viewer right = quad -x).
            Vector3 viewerForward = -quad.forward;
            Vector3 viewerRight = Vector3.Cross(Vector3.up, viewerForward).normalized;
            bool quadXPointsViewerLeft = Vector3.Dot(quad.right, viewerRight) < -0.9f;
            Assert.AreEqual(quadXPointsViewerLeft, ConsoleUvAuthority.FlipU > 0.5f,
                "console " + (c + 1) + " shader U flip compensates the quad axis "
                + "(quadXPointsViewerLeft=" + quadXPointsViewerLeft + ")");

            // The TL/TR/BL/BR diagnostic must be available in the real UI tree.
            Assert.IsTrue(surface.SetUvReferenceMarkersVisible(true),
                "console " + (c + 1) + " UV reference markers created");
            VisualElement root = surface.Document.rootVisualElement;
            Assert.IsNotNull(root.Q<VisualElement>("UvRef_TL"), "TL marker");
            Assert.IsNotNull(root.Q<VisualElement>("UvRef_TR"), "TR marker");
            Assert.IsNotNull(root.Q<VisualElement>("UvRef_BL"), "BL marker");
            Assert.IsNotNull(root.Q<VisualElement>("UvRef_BR"), "BR marker");
            surface.SetUvReferenceMarkersVisible(false);
        }

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 5) Pointer matches what the player sees
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
        for (int f = 0; f < 600 && !surface.IsReady; f++)
        {
            yield return null;
        }

        yield return null;
        yield return null;

        // What the player SEES as the screen top-left must map to the panel
        // top-left; what the player sees as bottom-right must map to the panel
        // bottom-right. A mirrored / 180-degree flipped screen fails here.
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

        // A UI button must be reachable exactly where it is displayed.
        Button batch5 = surface.Document.rootVisualElement
            .Q<Button>(ElementNames.Batch5Button);
        Assert.IsNotNull(batch5, "batch 05 button present");
        Assert.IsTrue(ConsoleScreenProjection.TryElementViewport(surface, eye,
            batch5, out Vector2 buttonViewport), "batch button projects on screen");
        Vector2 buttonPixel = buttonViewport;
        Assert.IsTrue(router.TryScreenToPanelPixel(
            new Vector2(buttonPixel.x * Screen.width, buttonPixel.y * Screen.height),
            out Vector2 mappedBack), "button viewport maps back into the panel");
        Vector2 expected = batch5.worldBound.center;
        // The router ray hits the screen collider's FRONT FACE (2 cm in front
        // of the quad plane), so a small parallax offset is expected; what must
        // hold is that clicking the displayed button position lands inside the
        // button itself.
        Assert.IsTrue(batch5.worldBound.Contains(mappedBack),
            "pointer position matches the displayed button (mapped " + mappedBack
            + " inside " + batch5.worldBound + ")");
        Assert.IsTrue(Vector2.Distance(mappedBack, expected)
                      < WorldConsoleSurface.UiWidth * 0.03f,
            "pointer round-trip stays within 3% of the panel width (mapped "
            + mappedBack + " vs expected " + expected + ")");

        focus.RequestExit();
        yield return WaitExplore(focus);
        GameSession.Ensure().ResetSession();
    }
}
