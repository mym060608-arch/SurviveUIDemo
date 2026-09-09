using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B.2 Play Mode acceptance:
///  - physical screens always-on with REAL content signatures and a verified
///    material chain (authored shader + per-console RT through property
///    blocks; the visible chain that Release shader stripping used to break),
///  - ConsoleScreen layer composition (base env camera w/ PP, sharp overlay
///    camera without PP, transforms frame-locked),
///  - ship wrist floorplan topology (exactly 4 doors, 4 terminals, airlock,
///    no legacy preparation terminal),
///  - Airlock equipment locker + deployment confirmation flows driving the
///    SAME GameSession loadout through real UI Toolkit pointer events.
/// </summary>
public class SurvivePhase07B2Tests
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
        for (int f = 0; f < 120; f++)
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
            "E enters console " + console.ConsoleId + " focus");
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

    private static IEnumerator WaitUiReady(WorldConsoleSurface surface)
    {
        for (int f = 0; f < 900 && !surface.IsReady; f++)
        {
            yield return null;
        }

        yield return null;
        yield return null;
    }

    private static void AssertConsoleScreenLayer(Camera eye, Camera sharp)
    {
        Assert.IsNotNull(eye, "eye camera");
        Assert.IsNotNull(sharp, "sharp screen camera");
        UniversalAdditionalCameraData eyeData =
            eye.GetComponent<UniversalAdditionalCameraData>();
        UniversalAdditionalCameraData sharpData =
            sharp.GetComponent<UniversalAdditionalCameraData>();
        Assert.IsNotNull(eyeData, "eye camera carries URP data");
        Assert.IsNotNull(sharpData, "sharp camera carries URP data");
        Assert.IsTrue(eyeData.renderPostProcessing,
            "base/environment camera post-processing ON");
        Assert.IsFalse(sharpData.renderPostProcessing,
            "sharp screen camera post-processing OFF");
        // Phase 07B.2 composition: a second independent base-type camera with
        // a depth-only clear renders the ConsoleScreen layer after the
        // post-processed environment frame (sharp screens, blurred env).
        Assert.AreEqual(CameraRenderType.Base, sharpData.renderType,
            "sharp camera is a base-type camera ordered after the environment");
        Assert.IsTrue(sharp.depth > eye.depth,
            "sharp camera renders after the environment camera (depth)");
        Assert.AreEqual(CameraClearFlags.Depth, sharp.clearFlags,
            "sharp camera keeps the environment frame (depth-only clear)");
    }

    // ====================================================================
    // 1) ConsoleScreenVisible_Runtime - idle content signatures + binding
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
            yield return WaitUiReady(surface);
            yield return WaitSeconds(0.3f);

            // RT really carries light UI pixels.
            bool content = surface.SampleScreenContent(out float ratio, out string summary);
            Assert.IsTrue(content,
                "console " + (c + 1) + " idle screen has real UI pixels: " + summary);

            // Distinctive per-console content signature (never just a bright
            // empty background).
            VisualElement root = surface.Document != null
                ? surface.Document.rootVisualElement
                : null;
            if (c == 0)
            {
                Assert.IsNotNull(root.Q<Label>(ElementNames.EnergyValue),
                    "console 1 shows Overview data");
                Assert.IsNotNull(root.Q<Label>(ElementNames.MineralsValue),
                    "console 1 Overview minerals");
            }
            else if (c == 1)
            {
                VisualElement warehouseRoot = surface.WarehouseHost != null
                    ? surface.WarehouseHost.Root
                    : null;
                Assert.IsNotNull(warehouseRoot, "console 2 warehouse root");
                Assert.AreEqual("仓库总览",
                    warehouseRoot.Q<Label>("WarehouseTitleLabel").text,
                    "console 2 title 仓库总览");
                Assert.IsNotNull(
                    warehouseRoot.Q<Label>(ShipWarehouseScreenHost.MineralsValueName),
                    "console 2 minerals row");
            }
            else if (c == 2)
            {
                Assert.IsNotNull(root.Q<Button>(ElementNames.Batch5Button),
                    "console 3 shows Processing batch controls");
            }
            else
            {
                Assert.IsNotNull(root.Q<Button>(ElementNames.OutpostUpgradeButton),
                    "console 4 shows Outpost upgrade controls");
            }
        }

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 2) ConsoleRendererBinding_Runtime - per-console RT through real material
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleRendererBinding_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        yield return WaitSeconds(0.5f);

        for (int c = 0; c < 4; c++)
        {
            WorldConsoleSurface surface = SurfaceOf(controller, c);
            yield return WaitUiReady(surface);
            bool ok = surface.VerifyScreenBinding(out string report);
            Assert.IsTrue(ok, "console " + (c + 1) + " physical binding: " + report);
            Material material = surface.ScreenRenderer != null
                ? surface.ScreenRenderer.sharedMaterial
                : null;
            Assert.IsNotNull(material, "console " + (c + 1) + " screen material");
            Assert.IsNotNull(material.shader, "console " + (c + 1) + " shader present");
            Assert.IsTrue(material.shader.name.Contains("Unlit")
                          || material.shader.name.Contains("unlit"),
                "console " + (c + 1) + " unlit monitor shader: " + material.shader.name);
            Assert.AreEqual(surface.CurrentRenderTexture,
                surface.ReadBoundScreenTexture(),
                "console " + (c + 1) + " screen samples ITS OWN RT");
            Assert.AreEqual(WorldConsoleSurface.ConsoleScreenLayer,
                surface.ScreenRenderer.gameObject.layer,
                "console " + (c + 1) + " screen on ConsoleScreen layer");
        }

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 3) ConsoleScreenFrontSurface_Runtime - screen in front of the dark
    //    backing plate (>= 0.005 m separation, no coplanar blocker)
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleScreenFrontSurface_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);

        for (int c = 0; c < 4; c++)
        {
            WorldConsoleSurface surface = SurfaceOf(controller, c);
            yield return WaitUiReady(surface);
            Transform quad = surface.ScreenQuad;
            Assert.IsNotNull(quad, "console " + (c + 1) + " screen quad");
            Transform console = quad.parent;
            Assert.IsNotNull(console, "console " + (c + 1) + " console root");

            // Screen plane offset along the console front normal.
            Vector3 forward = console.forward;
            float screenZ = Vector3.Dot(quad.position - console.position, forward);
            // The bezel backing plate box: find its child by name.
            Transform plate = console.Find("Bezel_Plate");
            Assert.IsNotNull(plate, "console " + (c + 1) + " bezel plate");
            Vector3 plateHalf = plate.localScale * 0.5f;
            float plateFront = Vector3.Dot(plate.position - console.position, forward)
                               + plateHalf.z;
            float separation = screenZ - plateFront;
            Assert.IsTrue(separation >= 0.005f,
                "console " + (c + 1) + " screen/plate separation " + separation + " m");
            Assert.IsTrue(surface.ScreenRenderer.enabled,
                "console " + (c + 1) + " renderer enabled");
        }

        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 4) ConsoleFocusComposition_Runtime + ConsoleFocusSharpScreen_Runtime -
    //    base(PP env) + overlay(no PP ConsoleScreen-only) composition
    // ====================================================================

    [UnityTest]
    public IEnumerator ConsoleFocusComposition_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        SurfacePlayerController player = controller.Player;
        Camera eye = player.EyeCamera;
        Assert.IsNotNull(eye, "eye camera");

        // The sharp screen camera is a child of the eye camera at identity:
        // position/rotation/FOV/projection stay frame-locked by construction.
        Transform sharpTransform = null;
        foreach (Transform child in eye.transform)
        {
            if (child.name == "SharpScreenCamera")
            {
                sharpTransform = child;
                break;
            }
        }

        Assert.IsNotNull(sharpTransform, "sharp overlay camera child exists");
        Camera sharp = sharpTransform.GetComponent<Camera>();
        AssertConsoleScreenLayer(eye, sharp);

        // Culling split: environment camera never sees ConsoleScreen; the
        // overlay sees ONLY ConsoleScreen.
        int consoleLayer = WorldConsoleSurface.ConsoleScreenLayer;
        Assert.IsTrue((eye.cullingMask & (1 << consoleLayer)) == 0,
            "base camera excludes ConsoleScreen layer");
        Assert.AreEqual(1 << consoleLayer, sharp.cullingMask,
            "overlay camera renders only ConsoleScreen layer");

        // Same projection parameters as the base camera (no parallax/drift).
        Assert.IsTrue(Mathf.Abs(sharp.fieldOfView - eye.fieldOfView) < 0.01f,
            "FOV matches");
        Assert.IsTrue(Mathf.Abs(sharp.nearClipPlane - eye.nearClipPlane) < 0.001f,
            "near clip matches");
        Assert.IsTrue(Mathf.Abs(sharp.farClipPlane - eye.farClipPlane) < 0.01f,
            "far clip matches");
        Assert.IsTrue(sharpTransform.localPosition.sqrMagnitude < 0.0001f,
            "overlay camera at eye local origin");
        Assert.IsTrue(Mathf.Abs(Quaternion.Angle(sharpTransform.localRotation,
            Quaternion.identity)) < 0.01f, "overlay camera orientation locked");

        // Focus volume stays a real environment effect on the base camera.
        ShipConsoleFocusController focus = controller.Focus;
        Volume volume = focus != null ? focus.BlurVolume : null;
        Assert.IsNotNull(volume, "focus volume present");
        Assert.IsTrue(volume.profile != null
            && volume.profile.TryGet<DepthOfField>(out _),
            "DOF component in focus profile");

        GameSession.Ensure().ResetSession();
    }

    /// <summary>
    /// Sharp screen / blurred environment: with the composition verified
    /// above, the console surface is RENDERED BY THE OVERLAY CAMERA ONLY, so
    /// the base camera's DOF mathematically cannot touch screen pixels. This
    /// test asserts the structural guarantee plus the DOF chain parameters;
    /// the same-pose A/B pixel evidence (screen ROI vs environment ROI) is
    /// produced by the Release-player probe (ShipB2ShotProbe blur_off/on
    /// frames) because the editor Test Runner does not render the backbuffer.
    /// </summary>
    [UnityTest]
    public IEnumerator ConsoleFocusSharpScreen_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipConsoleFocusController focus = controller.Focus;
        Camera eye = controller.Player.EyeCamera;
        AssertConsoleScreenLayer(eye,
            eye.transform.Find("SharpScreenCamera").GetComponent<Camera>());

        int consoleLayer = WorldConsoleSurface.ConsoleScreenLayer;
        Assert.IsTrue((eye.cullingMask & (1 << consoleLayer)) == 0,
            "screen not in the DOF base camera");
        Assert.IsTrue(eye.GetComponent<UniversalAdditionalCameraData>()
            .renderPostProcessing, "DOF executes on the base camera");

        Volume volume = focus.BlurVolume;
        Assert.IsNotNull(volume, "blur volume present");
        DepthOfField dof = null;
        Assert.IsTrue(volume.profile != null
            && volume.profile.TryGet<DepthOfField>(out dof),
            "DOF in focus profile");
        Assert.IsTrue(dof.mode.overrideState
            && dof.mode.value == DepthOfFieldMode.Bokeh,
            "Bokeh mode engaged");
        Assert.IsTrue(Mathf.Abs(dof.aperture.value - 1.2f) < 0.01f
            && dof.focusDistance.value > 0.8f
            && dof.focalLength.value >= 100f,
            "strong visibly-soft environment parameters");

        // While focused, the environment blur weight is really engaged and
        // the overlay chain stays active.
        yield return EnterConsole(controller, 0);
        yield return WaitFocused(focus);
        Assert.IsTrue(focus.BlurWeight > 0.8f,
            "environment blur engaged while focused (" + focus.BlurWeight + ")");
        Camera sharp = eye.transform.Find("SharpScreenCamera").GetComponent<Camera>();
        Assert.IsTrue(sharp.enabled, "overlay camera enabled during focus");
        Assert.AreEqual(1 << consoleLayer, sharp.cullingMask,
            "overlay mask unchanged during focus");
        focus.RequestExit();
        yield return WaitExplore(focus);
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 5) ShipSchematicTopology_Runtime - exactly 4 doors, positions verified
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipSchematicTopology_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        controller.SetWristOpen(true);
        yield return WaitSeconds(0.5f);
        Assert.IsTrue(controller.Wrist.HasShipSchematic,
            "ship wrist floorplan attached");

        Assert.IsTrue(ShipSchematicLayout.Walls.Length > 0,
            "solid wall segments exist");
        Assert.AreEqual(4, ShipSchematicLayout.Doors.Length,
            "exactly 4 door segments (no five-door model)");

        ShipDoorSegment bridgeDoor = ShipSchematicLayout.DoorById(1);
        Assert.IsNotNull(bridgeDoor, "bridge door");
        Assert.IsTrue(ShipSchematicLayout.IsHorizontal(bridgeDoor),
            "bridge door horizontal");
        Assert.IsTrue(Mathf.Abs(bridgeDoor.A.y - 6.3f) < 0.001f
            && Mathf.Abs(bridgeDoor.B.y - 6.3f) < 0.001f
            && Mathf.Abs(bridgeDoor.A.x + 0.9f) < 0.001f
            && Mathf.Abs(bridgeDoor.B.x - 0.9f) < 0.001f,
            "bridge door sits centred on the Bridge BOTTOM wall (z 6.3)");

        ShipDoorSegment warehouseDoor = ShipSchematicLayout.DoorById(2);
        Assert.IsNotNull(warehouseDoor, "warehouse door");
        Assert.IsTrue(!ShipSchematicLayout.IsHorizontal(warehouseDoor),
            "warehouse door vertical");
        Assert.IsTrue(Mathf.Abs(warehouseDoor.A.x + 1.1f) < 0.001f
            && Mathf.Abs(warehouseDoor.B.x + 1.1f) < 0.001f
            && warehouseDoor.A.y > 0.3f && warehouseDoor.B.y < 6.3f,
            "warehouse door on its RIGHT/EAST wall (x -1.1)");

        ShipDoorSegment processingDoor = ShipSchematicLayout.DoorById(3);
        Assert.IsNotNull(processingDoor, "processing door");
        Assert.IsTrue(!ShipSchematicLayout.IsHorizontal(processingDoor),
            "processing door vertical");
        Assert.IsTrue(Mathf.Abs(processingDoor.A.x - 1.1f) < 0.001f
            && Mathf.Abs(processingDoor.B.x - 1.1f) < 0.001f
            && processingDoor.A.y > 0.3f && processingDoor.B.y < 6.3f,
            "processing door on its LEFT/WEST wall (x 1.1)");

        ShipDoorSegment airlockDoor = ShipSchematicLayout.DoorById(4);
        Assert.IsNotNull(airlockDoor, "airlock door");
        Assert.IsTrue(ShipSchematicLayout.IsHorizontal(airlockDoor),
            "airlock door horizontal");
        Assert.IsTrue(Mathf.Abs(airlockDoor.A.y + 9.4f) < 0.001f
            && Mathf.Abs(airlockDoor.B.y + 9.4f) < 0.001f
            && Mathf.Abs(airlockDoor.A.x + 0.8f) < 0.001f
            && Mathf.Abs(airlockDoor.B.x - 0.8f) < 0.001f,
            "airlock door on its TOP wall (z -9.4)");

        // Outpost has no door and is not a closed room: no door touches the
        // Outpost zone.
        for (int i = 0; i < ShipSchematicLayout.Doors.Length; i++)
        {
            Assert.AreNotEqual(ShipSchematicZone.Outpost,
                ShipSchematicLayout.Doors[i].From,
                "door " + ShipSchematicLayout.Doors[i].Id + " never starts at Outpost");
            Assert.AreNotEqual(ShipSchematicZone.Outpost,
                ShipSchematicLayout.Doors[i].To,
                "door " + ShipSchematicLayout.Doors[i].Id + " never ends at Outpost");
        }

        // The 4 warehouse/processing doors are NOT on the room bottom walls.
        Assert.IsTrue(warehouseDoor.A.y > 1.0f && warehouseDoor.B.y > 1.0f,
            "warehouse door never sits on the bottom wall");
        Assert.IsTrue(processingDoor.A.y > 1.0f && processingDoor.B.y > 1.0f,
            "processing door never sits on the bottom wall");

        controller.SetWristOpen(false);
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 6) ShipSchematicMarker_Runtime - 4 terminals + airlock + player marker
    // ====================================================================

    [UnityTest]
    public IEnumerator ShipSchematicMarker_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);

        ShipTerminalMarker[] terminals = ShipSchematicLayout.Terminals;
        Assert.AreEqual(4, terminals.Length, "four terminal markers");
        Assert.IsNotNull(terminals[0], "terminal 01");
        Assert.IsNotNull(terminals[1], "terminal 02");
        Assert.IsNotNull(terminals[2], "terminal 03");
        Assert.IsNotNull(terminals[3], "terminal 04");

        // Exact console mapping: 01 总览 / 02 仓库 / 03 加工 / 04 据点.
        Assert.AreEqual(1, terminals[0].ConsoleId);
        Assert.AreEqual("总览", terminals[0].Zh);
        Assert.AreEqual(2, terminals[1].ConsoleId);
        Assert.AreEqual("仓库", terminals[1].Zh);
        Assert.AreEqual(3, terminals[2].ConsoleId);
        Assert.AreEqual("加工", terminals[2].Zh);
        Assert.AreEqual(4, terminals[3].ConsoleId);
        Assert.AreEqual("据点", terminals[3].Zh);

        for (int i = 0; i < terminals.Length; i++)
        {
            Assert.AreNotEqual("出发准备", terminals[i].Zh,
                "no legacy preparation terminal");
            Assert.AreNotEqual("PREP", terminals[i].Code,
                "no legacy PREP marker code");
        }

        Assert.IsNotNull(ShipSchematicLayout.Airlock, "one airlock marker");

        // Live wrist: floorplan element + player marker present.
        controller.SetWristOpen(true);
        yield return WaitSeconds(0.5f);
        SurfaceWristController wrist = controller.Wrist;
        VisualElement root = wrist.RootElement;
        Assert.IsNotNull(root.Q<VisualElement>("ShipSchematicVisual"),
            "floorplan element in document");
        Assert.IsNotNull(root.Q<VisualElement>("ShipAirlockMarker"),
            "airlock marker element in document");
        for (int i = 0; i < terminals.Length; i++)
        {
            Assert.IsNotNull(root.Q<VisualElement>(
                "ShipTerminal_" + terminals[i].ConsoleId),
                "terminal " + terminals[i].ConsoleId + " element in document");
        }

        Vector2 playerPx = Vector2.zero;
        bool playerMarkerOk = wrist.ShipSchematic != null
            && wrist.ShipSchematic.TryGetPlayerCentre(out playerPx);
        Assert.IsTrue(playerMarkerOk,
            "floorplan player marker visible");
        Assert.IsTrue(playerMarkerOk && ShipSchematicLayout.IsPixelInsideMapDisc(
                playerPx, RadialWristElement.MapRadius, 0f),
            "player marker inside the map disc");
        controller.SetWristOpen(false);
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 7) EquipmentLocker_Runtime - locker -> shared loadout -> GameSession
    // ====================================================================

    [UnityTest]
    public IEnumerator EquipmentLocker_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipOverlayUi overlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        Assert.IsNotNull(overlay, "overlay coordinator present");
        GameSession session = GameSession.Instance;

        // Walk to the locker and interact (real gaze path). The cabinet sits
        // on the WEST airlock wall, so face west (yaw 270).
        SurfacePlayerController player = controller.Player;
        player.TeleportTo(new Vector3(-1.55f, 0f, -12.6f));
        player.SetFacing(270f, 0f);
        yield return null;
        yield return null;
        EquipmentLockerInteractable locker =
            Object.FindAnyObjectByType<EquipmentLockerInteractable>();
        Assert.IsNotNull(locker, "equipment locker present");
        Assert.AreEqual("E  调整远征装备", locker.PromptText, "locker prompt");

        bool entered = false;
        for (int f = 0; f < 240 && !entered; f++)
        {
            controller.Interactor.RefreshTarget();
            if (ReferenceEquals(controller.Interactor.CurrentTarget, locker)
                && controller.Interactor.HasValidTarget)
            {
                entered = controller.Interactor.TryInteract();
            }

            yield return null;
        }

        Assert.IsTrue(entered, "E opened the loadout overlay");
        yield return WaitUntil(() => overlay.IsOpen
            && overlay.Mode == ShipOverlayUi.OverlayMode.Loadout
            && overlay.LoadoutPage != null && overlay.LoadoutPage.IsBound);
        Assert.IsTrue(overlay.LoadoutPage.IsVisible, "loadout page visible");
        Assert.IsFalse(controller.Player.InputEnabled,
            "movement/look OFF in LoadoutFocus");

        // Change optional gear through the REAL UI Toolkit pointer pipeline.
        Assert.IsFalse(session.BatterySelected, "battery starts off");
        Button battery = overlay.LoadoutPage.Root
            .Q<Button>(ElementNames.BatteryRowButton);
        Assert.IsNotNull(battery, "shared loadout battery row present");
        Assert.IsTrue(UiToolkitPointerSimulator.Click(battery),
            "battery click dispatched");
        yield return null;
        yield return null;
        Assert.IsTrue(session.BatterySelected,
            "GameSession battery changed by the real click");

        // 返回 closes back to ShipExplore (locker return context).
        Button back = overlay.LoadoutPage.Root
            .Q<Button>(ShipLoadoutPage.CloseButtonName);
        Assert.IsNotNull(back, "loadout 返回 button present");
        Assert.IsTrue(UiToolkitPointerSimulator.Click(back), "返回 clicked");
        yield return WaitUntil(() => !overlay.IsOpen && controller.Player.InputEnabled);
        GameSession.Ensure().ResetSession();
    }

    // ====================================================================
    // 8) AirlockDeploymentConfirmation_Runtime - full confirm flow
    // ====================================================================

    [UnityTest]
    public IEnumerator AirlockDeploymentConfirmation_Runtime()
    {
        GameSession.Ensure().ResetSession();
        yield return LoadShipScene();
        ShipGameController controller = null;
        yield return WaitForShip(value => controller = value);
        ShipOverlayUi overlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        Assert.IsNotNull(overlay, "overlay coordinator present");
        GameSession session = GameSession.Instance;

        // Interact with the deployment station.
        SurfacePlayerController player = controller.Player;
        player.TeleportTo(new Vector3(0f, 0f, -12.3f));
        player.SetFacing(180f, 0f);
        yield return null;
        yield return null;
        ShipAirlockInteractable airlock = controller.Airlock;
        Assert.IsNotNull(airlock, "airlock interactable present");
        bool entered = false;
        for (int f = 0; f < 240 && !entered; f++)
        {
            controller.Interactor.RefreshTarget();
            if (ReferenceEquals(controller.Interactor.CurrentTarget, airlock)
                && controller.Interactor.HasValidTarget)
            {
                entered = controller.Interactor.TryInteract();
            }

            yield return null;
        }

        Assert.IsTrue(entered, "deployment station E accepted");
        yield return WaitUntil(() => overlay.IsOpen
            && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm
            && overlay.DeployPage != null && overlay.DeployPage.IsBound);
        Assert.IsTrue(overlay.DeployPage.IsVisible, "deploy page visible");

        // NO immediate scene load before confirmation.
        yield return WaitSeconds(0.5f);
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "still inside the ship before 确认降临");

        // 调整装备 -> the SAME shared loadout view.
        Button adjust = overlay.DeployPage.Root
            .Q<Button>(ShipDeployConfirmPage.AdjustGearButtonName);
        Assert.IsNotNull(adjust, "调整装备 button present");
        Assert.IsTrue(UiToolkitPointerSimulator.Click(adjust), "调整装备 clicked");
        yield return WaitUntil(() => overlay.IsOpen
            && overlay.Mode == ShipOverlayUi.OverlayMode.Loadout
            && overlay.LoadoutPage != null && overlay.LoadoutPage.IsVisible);

        // Editing from the deploy route changes the same GameSession loadout.
        Assert.IsFalse(session.DroneSelected, "drone starts off");
        Button drone = overlay.LoadoutPage.Root.Q<Button>(ElementNames.DroneRowButton);
        Assert.IsNotNull(drone, "drone row present in the shared loadout view");
        Assert.IsTrue(UiToolkitPointerSimulator.Click(drone), "drone click dispatched");
        yield return null;
        yield return null;
        Assert.IsTrue(session.DroneSelected,
            "drone changed through GameSession (same authority as the locker)");

        // 返回 -> back to the deployment confirmation.
        Button back = overlay.LoadoutPage.Root
            .Q<Button>(ShipLoadoutPage.CloseButtonName);
        Assert.IsNotNull(back, "loadout 返回 button present");
        Assert.IsTrue(UiToolkitPointerSimulator.Click(back), "返回 clicked");
        yield return WaitUntil(() => overlay.IsOpen
            && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm);

        // 取消 -> ShipExplore (no routing).
        Button cancel = overlay.DeployPage.Root
            .Q<Button>(ShipDeployConfirmPage.CancelButtonName);
        Assert.IsNotNull(cancel, "取消 button present");
        Assert.IsTrue(UiToolkitPointerSimulator.Click(cancel), "取消 clicked");
        yield return WaitUntil(() => !overlay.IsOpen && controller.Player.InputEnabled);
        Assert.AreEqual(GameSession.ShipInteriorSceneName, ActiveSceneName(),
            "cancel stays inside the ship");

        // Re-open and 确认降临 -> Surface routing, session persisted.
        entered = false;
        for (int f = 0; f < 240 && !entered; f++)
        {
            controller.Interactor.RefreshTarget();
            if (ReferenceEquals(controller.Interactor.CurrentTarget, airlock)
                && controller.Interactor.HasValidTarget)
            {
                entered = controller.Interactor.TryInteract();
            }

            yield return null;
        }

        Assert.IsTrue(entered, "deployment station re-opened");

        yield return WaitUntil(() => overlay.IsOpen
            && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm
            && overlay.DeployPage != null && overlay.DeployPage.IsBound);
        int mineralsBefore = session.Minerals;
        Button confirm = overlay.DeployPage.Root
            .Q<Button>(ShipDeployConfirmPage.ConfirmButtonName);
        Assert.IsNotNull(confirm, "确认降临 button present");
        Assert.IsTrue(UiToolkitPointerSimulator.Click(confirm), "确认降临 clicked");
        yield return WaitUntil(() => ActiveSceneName() == GameSession.SurfaceSceneName);
        yield return WaitForSurface(_ => { });
        Assert.AreEqual(session.Minerals, mineralsBefore,
            "GameSession persisted across the deployment");
        Assert.IsTrue(session.DroneSelected,
            "equipment edited earlier persisted to the surface run");
        Assert.IsTrue(GameSessionCount() == 1, "no duplicate GameSession");
        GameSession.Ensure().ResetSession();
    }

    private static int GameSessionCount()
    {
        return Object.FindObjectsByType<GameSession>().Length;
    }
}
