using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B.1 Play Mode acceptance - real console screen pixels (idle +
/// focus), an actually-executed focus blur chain, the ship wrist schematic and
/// the world-pointer click still mutating GameSession. Every console test
/// reads the real RenderTexture pixels (SampleScreenContent), so "the UI is
/// on the terminal" can no longer pass on an empty/black RT.
/// </summary>
public class SurvivePhase07B1Tests
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

    // ====================================================================
    // Helpers (mirror SurvivePhase07BTests so B1 stays self-contained)
    // ====================================================================

    private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = MaxWaitFrames)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }

    private static IEnumerator WaitSeconds(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static string ActiveSceneName()
    {
        return SceneManager.GetActiveScene().name;
    }

    private static IEnumerator LoadShipScene()
    {
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.ShipInteriorSceneName);
        yield return null;
        yield return null;
    }

    private static IEnumerator LoadSurfaceScene()
    {
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return WaitUntil(() => ActiveSceneName() == GameSession.SurfaceSceneName);
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

    private static IEnumerator WaitForSurface(System.Action<SurfaceGameController> onReady)
    {
        SurfaceGameController controller = null;
        yield return WaitUntil(() =>
        {
            controller = Object.FindAnyObjectByType<SurfaceGameController>();
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
        interactor.RefreshTarget();
        Assert.IsTrue(ReferenceEquals(interactor.CurrentTarget, console),
            "console " + console.ConsoleId + " is the gaze target");
        Assert.IsTrue(interactor.TryInteract(),
            "E enters console " + console.ConsoleId + " focus");
    }

    private static IEnumerator WaitFocused(ShipConsoleFocusController focus)
    {
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus);
        yield return null;
        yield return null;
    }

    private static IEnumerator WaitUiContent(WorldConsoleSurface surface)
    {
        for (int f = 0; f < 600 && (surface.Document == null
             || surface.Document.rootVisualElement == null
             || surface.Document.rootVisualElement.childCount == 0); f++)
        {
            yield return null;
        }

        yield return null;
        yield return null;
    }

    private static WorldConsoleSurface SurfaceOf(ShipGameController controller, int index)
    {
        return controller.Consoles[index].GetComponent<WorldConsoleSurface>();
    }

    // ====================================================================
    // 1) ConsoleScreenVisible_Runtime: all four idle terminals carry light UI
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleScreenVisible_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        yield return WaitSeconds(0.6f);

        for (int c = 0; c < 4; c++)
        {
            WorldConsoleSurface surface = SurfaceOf(controller, c);
            yield return ApproachConsole(controller, c);
            yield return WaitUiContent(surface);
            yield return WaitSeconds(0.4f);
            bool visible = surface.SampleScreenContent(out float ratio, out string summary);
            Assert.IsTrue(surface.IsReady, "console " + (c + 1) + " surface ready");
            Assert.IsTrue(visible,
                "console " + (c + 1) + " idle screen has real UI pixels: " + summary);
        }
    }

    // ====================================================================
    // 2) ConsoleFocusVisible_Runtime: focused terminals stay visible
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleFocusVisible_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        Assert.IsNotNull(focus, "focus controller present");

        yield return EnterConsole(controller, 0);
        yield return WaitFocused(focus);
        WorldConsoleSurface surface = SurfaceOf(controller, 0);
        yield return WaitUiContent(surface);
        yield return WaitSeconds(0.4f);

        bool visible = surface.SampleScreenContent(out float ratio, out string summary);
        Assert.IsTrue(visible, "focused overview screen still has UI pixels: " + summary);
        Assert.IsTrue(focus.BlurWeight > 0.8f, "focus blur weight high");
        focus.RequestExit();
    }

    // ====================================================================
    // 3) ConsoleBlurVisible_Runtime: the blur CHAIN really executes
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleBlurVisible_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        SurfacePlayerController player = controller.Player;

        // The camera must actually run URP post-processing, otherwise the DOF
        // volume weight animates but no blur is ever rendered.
        Camera eye = player != null ? player.EyeCamera : Camera.main;
        UniversalAdditionalCameraData data = eye != null
            ? eye.GetComponent<UniversalAdditionalCameraData>()
            : null;
        Assert.IsNotNull(data, "eye camera carries UniversalAdditionalCameraData");
        Assert.IsTrue(data.renderPostProcessing,
            "eye camera renderPostProcessing enabled (URP executes the volume)");

        Volume volume = focus != null ? focus.BlurVolume : null;
        Assert.IsNotNull(volume, "focus DOF volume present");
        DepthOfField dof = null;
        bool dofResolved = volume.profile != null
            && volume.profile.TryGet<DepthOfField>(out dof);
        Assert.IsTrue(dofResolved && dof != null,
            "DOF component present in the focus profile");
        Assert.IsTrue(dof.mode.overrideState
            && dof.mode.value == DepthOfFieldMode.Bokeh,
            "DOF Bokeh mode is overridden");
        Assert.IsTrue(Mathf.Abs(dof.focusDistance.value - 1.05f) < 0.01f,
            "DOF focus sits on the console housing band (1.05 m)");
        Assert.IsTrue(Mathf.Abs(dof.aperture.value - 1.2f) < 0.01f
            && Mathf.Abs(dof.focalLength.value - 150f) < 0.01f,
            "DOF shallow aperture/long focal for a clearly visible falloff");

        yield return EnterConsole(controller, 0);
        Assert.IsTrue(focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.TransitionIn,
            "transition starts after E");
        bool sawRamp = false;
        for (int f = 0; f < 240; f++)
        {
            if (focus.BlurWeight > 0.2f && focus.BlurWeight < 0.95f)
            {
                sawRamp = true;
                break;
            }

            yield return null;
        }

        Assert.IsTrue(sawRamp, "DOF weight visibly ramps during the transition");
        yield return WaitFocused(focus);
        Assert.IsTrue(focus.BlurWeight > 0.9f, "DOF fully engaged while focused");
        focus.RequestExit();
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore);
        Assert.IsTrue(Mathf.Abs(focus.BlurWeight) < 0.02f, "DOF weight back to 0 on exit");
    }

    // ====================================================================
    // 4) ShipWristSchematic_Runtime: ship wrist central plan exists with the
    // five contract nodes, all inside the map disc, with a live player marker
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipWristSchematic_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        SurfaceWristController wrist = controller.Wrist;
        Assert.IsNotNull(wrist, "ship wrist bound");

        controller.SetWristOpen(true);
        yield return WaitSeconds(0.5f);
        Assert.IsTrue(wrist.IsShipMode, "ship wrist is ship mode");
        Assert.IsTrue(wrist.HasShipSchematic,
            "floorplan visual attached to the wrist geometry host");

        VisualElement root = wrist.RootElement;
        Assert.IsNotNull(root, "ship wrist document root");
        Assert.IsNotNull(root.Q<VisualElement>("ShipSchematicVisual"),
            "ShipSchematicVisual element present in the document");

        // Floorplan data model: 4 doors with exact topology, 4 terminals,
        // 1 airlock marker (Phase 07B.2 authoritative drawing).
        Assert.AreEqual(4, ShipSchematicLayout.Doors.Length,
            "floorplan has exactly four door segments");
        ShipDoorSegment bridgeDoor = ShipSchematicLayout.DoorById(1);
        ShipDoorSegment warehouseDoor = ShipSchematicLayout.DoorById(2);
        ShipDoorSegment processingDoor = ShipSchematicLayout.DoorById(3);
        ShipDoorSegment airlockDoor = ShipSchematicLayout.DoorById(4);
        Assert.IsNotNull(bridgeDoor, "bridge door present");
        Assert.IsTrue(ShipSchematicLayout.IsHorizontal(bridgeDoor)
            && bridgeDoor.A.x < 0f && bridgeDoor.B.x > 0f
            && Mathf.Abs(bridgeDoor.A.y - 6.3f) < 0.001f,
            "door 01 is the Bridge BOTTOM horizontal door");
        Assert.IsNotNull(warehouseDoor, "warehouse door present");
        Assert.IsTrue(!ShipSchematicLayout.IsHorizontal(warehouseDoor)
            && Mathf.Abs(warehouseDoor.A.x + 1.1f) < 0.001f
            && warehouseDoor.A.y > 0.3f,
            "door 02 is the Warehouse RIGHT/EAST vertical door");
        Assert.IsNotNull(processingDoor, "processing door present");
        Assert.IsTrue(!ShipSchematicLayout.IsHorizontal(processingDoor)
            && Mathf.Abs(processingDoor.A.x - 1.1f) < 0.001f
            && processingDoor.A.y > 0.3f,
            "door 03 is the Processing LEFT/WEST vertical door");
        Assert.IsNotNull(airlockDoor, "airlock door present");
        Assert.IsTrue(ShipSchematicLayout.IsHorizontal(airlockDoor)
            && airlockDoor.A.x < 0f && airlockDoor.B.x > 0f
            && Mathf.Abs(airlockDoor.A.y + 9.4f) < 0.001f,
            "door 04 is the Airlock TOP horizontal door");

        Assert.AreEqual(4, ShipSchematicLayout.Terminals.Length,
            "floorplan holds four terminal markers");
        for (int i = 0; i < ShipSchematicLayout.Terminals.Length; i++)
        {
            ShipTerminalMarker marker = ShipSchematicLayout.Terminals[i];
            Assert.IsTrue(marker.ConsoleId >= 1 && marker.ConsoleId <= 4,
                "terminal " + i + " maps console 1..4");
            Assert.AreNotEqual("出发准备", marker.Zh,
                "no legacy preparation terminal marker exists");
        }

        Assert.IsNotNull(ShipSchematicLayout.Airlock, "airlock marker exists");
        Assert.IsTrue(ShipSchematicLayout.Walls.Length > 0,
            "solid wall runs exist");

        Assert.IsTrue(
            ShipSchematicLayout.HullFitsMapDisc(RadialWristElement.MapRadius, 2f),
            "deck hull outline fits the central map disc");
        Assert.IsTrue(
            ShipSchematicLayout.FloorplanFitsMapDisc(RadialWristElement.MapRadius, 2f),
            "whole floorplan fits the central map disc");
        bool markerOk = false;
        Vector2 markerPx = Vector2.zero;
        if (wrist.ShipSchematic != null
            && wrist.ShipSchematic.TryGetPlayerCentre(out markerPx))
        {
            markerOk = ShipSchematicLayout.IsPixelInsideMapDisc(markerPx,
                RadialWristElement.MapRadius, 0f);
        }

        Assert.IsTrue(markerOk,
            "live player marker is drawn inside the map disc");
        Assert.IsNotNull(root.Q<VisualElement>("ShipAirlockMarker"),
            "airlock marker container present in the floorplan");

        controller.SetWristOpen(false);
        Assert.IsFalse(controller.IsWristOpen, "ship wrist closed");
    }

    // ====================================================================
    // 5) ShipWristMapMode_Runtime: ship mode draws NO surface node scatter
    // map; surface mode still uses its own local map
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipWristMapMode_Runtime()
    {
        GameSession.Ensure().ResetSession();

        // Surface first: normal surface wrist, no schematic.
        yield return LoadSurfaceScene();
        SurfaceGameController surface = null;
        yield return WaitForSurface(value => surface = value);
        SurfaceWristController surfaceWrist =
            Object.FindAnyObjectByType<SurfaceWristController>();
        Assert.IsNotNull(surfaceWrist, "surface wrist present");
        surface.SetWristOpen(true);
        yield return WaitSeconds(0.3f);
        Assert.IsFalse(surfaceWrist.IsShipMode, "surface wrist is NOT ship mode");
        Assert.IsFalse(surfaceWrist.HasShipSchematic,
            "surface wrist has no deck schematic");
        Assert.IsNotNull(surfaceWrist.RootElement.Q<VisualElement>("MapPlayer"),
            "surface wrist keeps its surface map player marker");
        surface.SetWristOpen(false);
        yield return WaitSeconds(0.2f);

        // Ship: no surface scatter markers may be drawn at all.
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        SurfaceWristController shipWrist = controller.Wrist;
        controller.SetWristOpen(true);
        yield return WaitSeconds(0.4f);
        Assert.IsTrue(shipWrist.IsShipMode, "ship wrist is ship mode");
        Assert.IsTrue(shipWrist.HasShipSchematic, "ship wrist shows the deck plan");
        VisualElement mapPlayer = shipWrist.RootElement.Q<VisualElement>("MapPlayer");
        VisualElement mapNode = shipWrist.RootElement.Q<VisualElement>("MapNode0");
        Assert.IsTrue(mapPlayer == null || mapPlayer.style.display == DisplayStyle.None,
            "ship wrist hides the surface player marker");
        Assert.IsTrue(mapNode == null || mapNode.style.display == DisplayStyle.None,
            "ship wrist hides surface node markers (no scatter map)");
        controller.SetWristOpen(false);
    }

    // ====================================================================
    // 6) PointerClickStillWorks_Runtime: a real world-pointer click still
    // mutates GameSession (loadout confirm on Preparation)
    // ====================================================================

    [UnityTest]
    public IEnumerator PointerClickStillWorks_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        WorldUiPointerRouter router = Object.FindAnyObjectByType<WorldUiPointerRouter>();
        Assert.IsNotNull(router, "shared world pointer router present");

        // Console03 资源加工 carries interactive UI on a physical console
        // (Console02 is the read-only 仓库总览 now). Real world-pointer clicks
        // must still mutate GameSession.
        yield return EnterConsole(controller, 2);
        yield return WaitFocused(focus);
        WorldConsoleSurface surface = SurfaceOf(controller, 2);
        yield return WaitUiContent(surface);
        yield return WaitSeconds(0.4f);

        Assert.IsFalse(GameSession.Instance.IsProcessing, "processing starts off");
        Button batch = FindButton(surface, ElementNames.Batch5Button);
        Assert.IsNotNull(batch, "batch 05 row button present");
        yield return ClickElement(router, surface, batch);
        Button start = FindButton(surface, ElementNames.ProcessingStartButton);
        Assert.IsNotNull(start, "start button present");
        yield return ClickElement(router, surface, start);
        Assert.IsTrue(GameSession.Instance.IsProcessing,
            "world-pointer click started the authoritative process");

        focus.RequestExit();
        yield return WaitUntil(() => focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore);
        yield return WaitSeconds(2.6f);
        Assert.AreEqual(GameSession.RefineryProcessState.Complete,
            GameSession.Instance.ProcessState,
            "processing completed while unfocused");
        GameSession.Ensure().ResetSession();
    }

    // ---- pointer helpers ----

    private static Button FindButton(WorldConsoleSurface surface, string name)
    {
        return surface.Document != null && surface.Document.rootVisualElement != null
            ? surface.Document.rootVisualElement.Q<Button>(name)
            : null;
    }

    private static IEnumerator ClickElement(WorldUiPointerRouter router,
        WorldConsoleSurface surface, VisualElement element)
    {
        SurfacePlayerController player =
            Object.FindAnyObjectByType<SurfacePlayerController>();
        Camera eye = player != null ? player.EyeCamera : Camera.main;
        Vector2 viewport;
        Assert.IsTrue(TryElementViewport(surface, eye, element, out viewport),
            "click target not mappable to the viewport");
        Assert.IsTrue(router.TryClickViewportPoint(viewport),
            "world-pointer click dispatched to the focused screen");
        yield return null;
        yield return null;
        yield return null;
    }

    private static bool TryElementViewport(WorldConsoleSurface surface, Camera camera,
        VisualElement element, out Vector2 viewport)
    {
        // Single projection authority (Phase 07B.2.1): the click position and
        // the visual orientation share ConsoleUvAuthority.
        return ConsoleScreenProjection.TryElementViewport(surface, camera, element,
            out viewport);
    }
}
