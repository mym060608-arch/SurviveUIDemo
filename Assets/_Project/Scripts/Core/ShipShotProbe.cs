using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Clean ship-interior graybox screenshot probe (Phase 07 Checkpoint A).
///
/// Inert unless launched with  -shipShot &lt;base png path&gt;. Lives on
/// 00_Boot (DontDestroyOnLoad) so a standalone player drives the real scene:
/// it loads 03_ShipInterior, waits for the coordinator, then walks the
/// deterministic first-person viewpoints (corridor, each compartment, the
/// airlock) through the public gameplay APIs and captures one frame each.
/// Frame files derive from the base path, e.g. base ".../frame_ship" writes
/// frame_ship_corridor.png / frame_ship_bridge.png / ... exactly the file set
/// the Phase 07 contract requires for the 07A evidence set.
///
/// Viewpoint coordinates mirror the ShipInteriorBuilder layout contract.
/// </summary>
public class ShipShotProbe : MonoBehaviour
{
    private const string Flag = "-shipShot";
    private const string Flag07B = "-ship07BShot";
    private const string Flag07B1 = "-ship07B1Shot";

    private string _basePath;
    private string _base07BPath;
    private bool _run07B1;
    private bool _active;

    private void Awake()
    {
        _basePath = ParseFlag(Flag);
        _base07BPath = ParseFlag(Flag07B);
        string b1Path = ParseFlag(Flag07B1);
        if (!string.IsNullOrEmpty(b1Path))
        {
            // Phase 07B.1 evidence run writes its frames into the folder and
            // its PASS/FAIL result into phase07b1_result.txt inside it.
            _run07B1 = true;
            _base07BPath = b1Path;
            Directory.CreateDirectory(b1Path);
        }

        _active = !string.IsNullOrEmpty(_basePath) || !string.IsNullOrEmpty(_base07BPath);
        if (!_active)
        {
            enabled = false;
            return;
        }

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (_active)
        {
            StartCoroutine(Run());
            StartCoroutine(Watchdog());
        }
    }

    private static IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(300f);
        Debug.LogError("[ShipShotProbe] watchdog timeout");
        Application.Quit(2);
    }

    private IEnumerator Run()
    {
        if (_run07B1)
        {
            yield return Run07B1();
            yield break;
        }

        if (!string.IsNullOrEmpty(_base07BPath))
        {
            yield return Run07B();
            yield break;
        }

        yield return RunShipViews();
    }

    // ====================================================================
    // Phase 07B evidence set (door open/closed, idle + focused consoles,
    // transition mid frame, ship wrist + recorded surface target)
    // ====================================================================

    private IEnumerator Run07B()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        ShipGameController controller = null;
        for (int i = 0; i < 1800; i++)
        {
            controller = Object.FindAnyObjectByType<ShipGameController>();
            if (controller != null && controller.IsReady
                && controller.Consoles.Count == 4 && controller.Player != null)
            {
                break;
            }

            yield return null;
        }

        if (controller == null || !controller.IsReady || controller.Player == null)
        {
            Debug.LogError("[Ship07BShot] ship interior never ready");
            Application.Quit(1);
            yield break;
        }

        Screen.SetResolution(1920, 1080, false);
        SurfacePlayerController player = controller.Player;
        yield return new WaitForSecondsRealtime(0.8f);

        // ---- Door frames (bridge door D1, front view) ----
        ShipDoorInteractable door = controller.Doors[0];
        if (!door.IsOpen)
        {
            door.RequestOpen();
        }

        yield return WaitReal(2.5f);
        player.TeleportTo(new Vector3(0f, 0f, 4.9f));
        player.SetFacing(0f, 0f);
        door.RequestClose();
        yield return WaitUntilDoor(door, ShipDoorInteractable.ShipDoorState.Closed, 8f);
        yield return WaitReal(0.4f);
        yield return Capture07B("frame_ship_door_closed");
        door.RequestOpen();
        yield return WaitUntilDoor(door, ShipDoorInteractable.ShipDoorState.Open, 8f);
        yield return WaitReal(0.4f);
        yield return Capture07B("frame_ship_door_open");

        // ---- Idle + focused frames per console (always-on UI) ----
        string[] idleSuffixes =
        {
            "frame_console_overview_idle",
            "frame_console_warehouse_idle",
            "frame_console_processing_idle",
            "frame_console_outpost_idle"
        };
        string[] focusSuffixes =
        {
            "frame_console_overview_focus",
            "frame_console_warehouse_focus",
            "frame_console_processing_focus",
            "frame_console_outpost_focus"
        };

        for (int c = 0; c < 4; c++)
        {
            yield return Approach(player, controller, c);
            yield return WaitReal(0.5f);
            yield return Capture07B(idleSuffixes[c]);

            // E enters focus through the real interactor.
            bool entered = false;
            for (int t = 0; t < 400 && !entered; t++)
            {
                controller.Interactor.RefreshTarget();
                if (ReferenceEquals(controller.Interactor.CurrentTarget, controller.Consoles[c])
                    && controller.Interactor.HasValidTarget)
                {
                    entered = controller.Interactor.TryInteract();
                }

                if (!entered)
                {
                    yield return null;
                }
            }

            if (c == 0)
            {
                // Mid-transition evidence on the first console.
                yield return WaitReal(0.13f);
                yield return Capture07B("frame_console_transition_mid");
            }

            ShipConsoleFocusController focus =
                Object.FindAnyObjectByType<ShipConsoleFocusController>();
            for (int t = 0; t < 600 && (focus == null || focus.State
                     != ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus); t++)
            {
                yield return null;
            }

            yield return WaitReal(0.5f);
            yield return Capture07B(focusSuffixes[c]);
            if (focus != null)
            {
                focus.RequestExit();
                yield return WaitReal(0.9f);
            }
        }

        // ---- Ship wrist with a recorded surface target memory ----
        player.TeleportTo(new Vector3(0f, 0f, 3.6f));
        player.SetFacing(180f, 0f);
        yield return null;
        session.RecordSurfaceTarget(1);
        controller.SetWristOpen(true);
        yield return WaitReal(1.2f);
        yield return Capture07B("frame_ship_wrist_target_memory");
        controller.SetWristOpen(false);
        yield return WaitReal(0.6f);

        Debug.Log("[Ship07BShot] wrote phase07b frames to: " + _base07BPath);
        Application.Quit(0);
    }

    // ====================================================================
    // Phase 07B.1 evidence run (-ship07B1Shot <dir>)
    // Re-shoots the real Release frames, proves the console RenderTextures
    // carry actual pixels (idle AND focus), reports the camera/DOF chain that
    // makes focus blur real, clicks a console button through the genuine
    // world-pointer pipeline and records the GameSession change, then verifies
    // the ship wrist schematic + map mode. Writes phase07b1_result.txt.
    // ====================================================================

    private readonly StringBuilder _b1Report = new StringBuilder();
    private int _b1Failures;

    private void B1Check(string label, bool ok)
    {
        _b1Report.AppendLine((ok ? "PASS" : "FAIL") + " :: " + label);
        if (!ok)
        {
            _b1Failures++;
        }
    }

    private void B1Log(string line)
    {
        _b1Report.AppendLine("INFO :: " + line);
    }

    private IEnumerator Run07B1()
    {
        _b1Report.AppendLine("=== PHASE 07B.1 EVIDENCE RUN ===");
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        ShipGameController controller = null;
        for (int i = 0; i < 1800; i++)
        {
            controller = Object.FindAnyObjectByType<ShipGameController>();
            if (controller != null && controller.IsReady
                && controller.Consoles.Count == 4 && controller.Player != null)
            {
                break;
            }

            yield return null;
        }

        if (controller == null || !controller.IsReady || controller.Player == null)
        {
            B1Check("ship interior ready", false);
            Finish07B1();
            yield break;
        }

        B1Check("ship interior ready", true);
        SurfacePlayerController player = controller.Player;
        Screen.SetResolution(1920, 1080, false);
        yield return WaitReal(0.9f);

        string[] idleSuffixes =
        {
            "frame_console_overview_idle",
            "frame_console_warehouse_idle",
            "frame_console_processing_idle",
            "frame_console_outpost_idle"
        };
        string[] focusSuffixes =
        {
            "frame_console_overview_focus",
            "frame_console_warehouse_focus",
            "frame_console_processing_focus",
            "frame_console_outpost_focus"
        };

        ShipConsoleFocusController focus =
            Object.FindAnyObjectByType<ShipConsoleFocusController>();
        for (int c = 0; c < 4; c++)
        {
            WorldConsoleSurface surface =
                controller.Consoles[c].GetComponent<WorldConsoleSurface>();
            yield return Approach(player, controller, c);
            yield return WaitReal(0.6f);
            yield return WaitConsoleUiReady(surface);
            yield return WaitReal(0.3f);
            B1SampleAndCheck(surface, "console " + (c + 1) + " IDLE screen content", true);

            if (!string.IsNullOrEmpty(_base07BPath))
            {
                yield return Capture07B(idleSuffixes[c]);
                if (c == 0)
                {
                    yield return Capture07B("frame_console_blur_compare_idle");
                }
            }

            // E enters focus through the real interactor.
            bool entered = false;
            for (int t = 0; t < 400 && !entered; t++)
            {
                controller.Interactor.RefreshTarget();
                if (ReferenceEquals(controller.Interactor.CurrentTarget,
                        controller.Consoles[c]) && controller.Interactor.HasValidTarget)
                {
                    entered = controller.Interactor.TryInteract();
                }

                if (!entered)
                {
                    yield return null;
                }
            }

            B1Check("console " + (c + 1) + " E entered focus", entered);
            if (c == 0 && !string.IsNullOrEmpty(_base07BPath))
            {
                yield return WaitReal(0.13f);
                yield return Capture07B("frame_console_transition_mid");
            }

            for (int t = 0; t < 600 && (focus == null || focus.State
                     != ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus); t++)
            {
                yield return null;
            }

            if (c == 0 && focus != null)
            {
                DofFocusDiag(focus, player);
            }

            yield return WaitReal(0.45f);
            B1SampleAndCheck(surface, "console " + (c + 1) + " FOCUS screen content", true);

            if (!string.IsNullOrEmpty(_base07BPath))
            {
                yield return Capture07B(focusSuffixes[c]);
                if (c == 0)
                {
                    yield return Capture07B("frame_console_blur_compare_focus");
                }
            }

            if (c == 0 && focus != null && focus.BlurVolume != null)
            {
                // Same-pose A/B evidence for the blur: hold the focused camera
                // still and only drop the DOF weight to 0, so the two frames
                // differ solely by the depth-of-field effect (identical
                // framing/content). frame_console_overview_focus above is the
                // weight=1 twin of this frame.
                focus.BlurVolume.weight = 0f;
                yield return WaitReal(0.2f);
                if (!string.IsNullOrEmpty(_base07BPath))
                {
                    yield return Capture07B("frame_console_blur_samepose_off");
                }

                focus.BlurVolume.weight = 1f;
                yield return WaitReal(0.2f);
                B1Check("DOF weight restored to 1 after same-pose capture",
                    Mathf.Abs(focus.BlurWeight - 1f) < 0.05f);
            }

            if (focus != null)
            {
                focus.RequestExit();
                yield return WaitReal(0.9f);
            }
        }

        // ---- Real world-pointer click proof on Processing console 03 ----
        // (Console02 is now the read-only 仓库总览 terminal; the interactive
        // loadout editing lives in the Airlock locker/deployment overlays,
        // which ShipB2ShotProbe covers. Here the real world-pointer path must
        // still mutate GameSession on a physical console.)
        session.ResetSession();
        int fuelBefore = session.Fuel;
        int alloyBefore = session.Alloy;
        B1Log("pointer-click before fuel=" + fuelBefore + " alloy=" + alloyBefore);

        yield return Approach(player, controller, 2);
        bool processingFocused = false;
        for (int t = 0; t < 400 && !processingFocused; t++)
        {
            controller.Interactor.RefreshTarget();
            if (ReferenceEquals(controller.Interactor.CurrentTarget, controller.Consoles[2])
                && controller.Interactor.HasValidTarget)
            {
                processingFocused = controller.Interactor.TryInteract();
            }

            if (!processingFocused)
            {
                yield return null;
            }
        }

        B1Check("processing console focused for clicks", processingFocused);
        WorldConsoleSurface processingSurface =
            controller.Consoles[2].GetComponent<WorldConsoleSurface>();
        yield return WaitConsoleUiReady(processingSurface);
        yield return WaitReal(0.4f);
        B1SampleAndCheck(processingSurface,
            "processing FOCUS screen content before click", true);

        Button batch5 = FindConsoleButton(processingSurface, ElementNames.Batch5Button);
        B1Check("batch 05 button resolved", batch5 != null);
        if (batch5 != null)
        {
            yield return ClickWorldButton(player, processingSurface, batch5);
        }

        Button start = FindConsoleButton(processingSurface, ElementNames.ProcessingStartButton);
        B1Check("processing start button resolved", start != null);
        if (start != null)
        {
            yield return ClickWorldButton(player, processingSurface, start);
        }

        yield return null;
        B1Check("pointer click started processing (GameSession mutated)",
            session.IsProcessing);

        if (focus != null)
        {
            focus.RequestExit();
            yield return WaitReal(0.9f);
        }

        yield return WaitReal(2.6f);
        B1Check("processing completed under the persistent session",
            session.ProcessState == GameSession.RefineryProcessState.Complete
            && session.Fuel == fuelBefore + GameSession.ProcessFuelReward
            && session.Alloy == alloyBefore + GameSession.ProcessAlloyReward);
        B1Log("pointer-click after fuel=" + session.Fuel
              + " alloy=" + session.Alloy
              + " (GameSession mutated by the real click)");

        // ---- Ship wrist schematic + map-mode evidence ----
        player.TeleportTo(new Vector3(0f, 0f, 3.6f));
        player.SetFacing(180f, 0f);
        yield return null;
        session.RecordSurfaceTarget(1);
        controller.SetWristOpen(true);
        yield return WaitReal(1.3f);

        SurfaceWristController wrist = controller.Wrist;
        B1Check("ship wrist bound", wrist != null);
        if (wrist != null)
        {
            B1Log("wrist returnMode=" + wrist.ReturnMode + " isShipMode="
                  + wrist.IsShipMode + " schematicAttached=" + wrist.HasShipSchematic);
            B1Check("ship wrist is ship mode", wrist.IsShipMode);
            B1Check("ship wrist floorplan visual attached", wrist.HasShipSchematic);
            if (wrist.RootElement != null)
            {
                VisualElement mapPlayer = wrist.RootElement.Q<VisualElement>("MapPlayer");
                B1Check("ship wrist hides surface player marker (no scatter map)",
                    mapPlayer == null || mapPlayer.style.display == DisplayStyle.None);

                // Floorplan data: 4 terminal markers + 1 airlock marker,
                // exactly 4 door segments, terminal element names present.
                B1Check("floorplan holds four terminal markers",
                    ShipSchematicLayout.Terminals.Length == 4);
                B1Check("floorplan holds one airlock marker",
                    ShipSchematicLayout.Airlock != null);
                B1Check("floorplan holds exactly four door segments",
                    ShipSchematicLayout.Doors.Length == 4);
                bool terminalLabelsOk = true;
                for (int i = 0; i < ShipSchematicLayout.Terminals.Length; i++)
                {
                    ShipTerminalMarker marker = ShipSchematicLayout.Terminals[i];
                    VisualElement container = wrist.RootElement
                        .Q<VisualElement>("ShipTerminal_" + marker.ConsoleId);
                    if (container == null)
                    {
                        terminalLabelsOk = false;
                    }
                }

                B1Check("floorplan terminal containers rendered",
                    terminalLabelsOk);
                VisualElement airlockMarker =
                    wrist.RootElement.Q<VisualElement>("ShipAirlockMarker");
                B1Check("floorplan airlock container rendered",
                    airlockMarker != null);
            }

            B1Check("schematic hull outline fits map disc",
                ShipSchematicLayout.HullFitsMapDisc(RadialWristElement.MapRadius, 2f));
            B1Check("whole floorplan fits map disc",
                ShipSchematicLayout.FloorplanFitsMapDisc(RadialWristElement.MapRadius, 2f));
            bool markerOk = false;
            if (wrist.ShipSchematic != null
                && wrist.ShipSchematic.TryGetPlayerCentre(out Vector2 playerPx))
            {
                markerOk = ShipSchematicLayout.IsPixelInsideMapDisc(playerPx,
                    RadialWristElement.MapRadius, 0f);
            }

            B1Check("schematic live player marker inside map disc", markerOk);
        }

        if (!string.IsNullOrEmpty(_base07BPath))
        {
            yield return Capture07B("frame_ship_wrist_schematic");
        }

        controller.SetWristOpen(false);
        yield return WaitReal(0.6f);
        Finish07B1();
    }

    private void B1SampleAndCheck(WorldConsoleSurface surface, string label,
        bool expectVisible)
    {
        bool visible = surface.SampleScreenContent(out float ratio, out string summary);
        Debug.Log("[07B1] " + summary);
        B1Log(summary);
        B1Check(label + " visible on RenderTexture", expectVisible == visible);
    }

    /// <summary>Camera post-processing + focus DOF chain report while a
    /// console is focused. Proves the blur is not just a volume weight but a
    /// chain URP actually executes (renderPostProcessing + authored Gaussian
    /// far ramp).</summary>
    private void DofFocusDiag(ShipConsoleFocusController focus,
        SurfacePlayerController player)
    {
        Camera eye = focus != null ? focus.EyeCamera : null;
        if (eye == null && player != null)
        {
            eye = player.EyeCamera;
        }

        if (eye == null)
        {
            eye = Camera.main;
        }

        UniversalAdditionalCameraData data = eye != null
            ? eye.GetComponent<UniversalAdditionalCameraData>()
            : null;
        B1Log("camera=" + (eye != null ? eye.name : "<null>")
              + " hasUniversalData=" + (data != null)
              + " renderPostProcessing=" + (data != null && data.renderPostProcessing)
              + " volumeLayerMask="
              + (data != null ? data.volumeLayerMask.value.ToString() : "-")
              + " antialiasing=" + (eye != null ? eye.allowMSAA.ToString() : "-"));
        B1Check("camera post-processing enabled (URP executes DOF)",
            data != null && data.renderPostProcessing);

        Volume volume = focus != null ? focus.BlurVolume : null;
        B1Check("focus DOF volume present", volume != null);
        if (volume == null)
        {
            B1Check("DOF component in focus profile", false);
            B1Check("DOF weight high while focused", false);
            return;
        }

        DepthOfField dof = null;
        bool dofOk = volume.profile != null
                     && volume.profile.TryGet<DepthOfField>(out dof);
        B1Check("DOF component in focus profile", dofOk);
        if (dofOk && dof != null)
        {
            B1Log("dof mode=" + dof.mode.value + " override=" + dof.mode.overrideState
                  + " focusDistance=" + dof.focusDistance.value
                  + " aperture=" + dof.aperture.value
                  + " focalLength=" + dof.focalLength.value
                  + " volumeWeight=" + volume.weight);
            B1Check("DOF mode Bokeh with override",
                dof.mode.overrideState && dof.mode.value == DepthOfFieldMode.Bokeh);
            B1Check("DOF focus on the console housing band (1.05 m)",
                Mathf.Abs(dof.focusDistance.value - 1.05f) < 0.01f);
            B1Check("DOF strong aperture/focal (1.2/150)",
                Mathf.Abs(dof.aperture.value - 1.2f) < 0.01f
                && Mathf.Abs(dof.focalLength.value - 150f) < 0.01f);
            B1Check("DOF volume weight high while focused", volume.weight > 0.85f);
        }
    }

    private static IEnumerator WaitConsoleUiReady(WorldConsoleSurface surface)
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

    private static Button FindConsoleButton(WorldConsoleSurface surface, string name)
    {
        return surface.Document != null && surface.Document.rootVisualElement != null
            ? surface.Document.rootVisualElement.Q<Button>(name)
            : null;
    }

    private IEnumerator ClickWorldButton(SurfacePlayerController player,
        WorldConsoleSurface surface, VisualElement element)
    {
        Camera eye = player != null ? player.EyeCamera : Camera.main;
        WorldUiPointerRouter router =
            Object.FindAnyObjectByType<WorldUiPointerRouter>();
        if (router == null)
        {
            B1Log("click failed: no world pointer router");
            yield break;
        }

        Vector2 viewport;
        if (!TryElementViewport(surface, eye, element, out viewport))
        {
            B1Log("click failed: element not mappable to viewport");
            yield break;
        }

        bool dispatched = router.TryClickViewportPoint(viewport);
        B1Log("pointer dispatch ok=" + dispatched + " viewport=" + viewport);
        yield return null;
        yield return null;
        yield return null;
    }

    /// <summary>UI element centre -> focused eye viewport fraction through the
    /// screen quad (shared with the PlayMode tests).</summary>
    private static bool TryElementViewport(WorldConsoleSurface surface,
        Camera camera, VisualElement element, out Vector2 viewport)
    {
        viewport = Vector2.zero;
        if (surface == null || camera == null || element == null
            || surface.ScreenQuad == null || surface.Document == null)
        {
            return false;
        }

        Rect bounds = element.worldBound;
        if (bounds.width < 1f || bounds.height < 1f)
        {
            bounds = new Rect(element.resolvedStyle.left, element.resolvedStyle.top,
                element.resolvedStyle.width, element.resolvedStyle.height);
        }

        if (bounds.width < 1f || bounds.height < 1f || float.IsNaN(bounds.center.x)
            || float.IsNaN(bounds.center.y))
        {
            return false;
        }

        float layoutW = Mathf.Max(1f, WorldConsoleSurface.UiWidth);
        float layoutH = Mathf.Max(1f, WorldConsoleSurface.UiHeight);
        float u = Mathf.Clamp01(bounds.center.x / layoutW);
        float vUp = 1f - Mathf.Clamp01(bounds.center.y / layoutH);

        Transform quad = surface.ScreenQuad;
        Vector3 scale = quad.lossyScale;
        Vector3 world = quad.position
                        + quad.right * ((u - 0.5f) * scale.x)
                        + quad.up * ((vUp - 0.5f) * scale.y);
        Vector3 vp = camera.WorldToViewportPoint(world);
        if (vp.z <= 0f)
        {
            return false;
        }

        viewport = new Vector2(vp.x, vp.y);
        return true;
    }

    private void Finish07B1()
    {
        _b1Report.AppendLine(_b1Failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        string path = Path.Combine(_base07BPath ?? ".", "phase07b1_result.txt");
        try
        {
            File.WriteAllText(path, _b1Report.ToString());
            Debug.Log("[07B1] wrote result: " + path);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[07B1] cannot write result: " + e);
        }

        Debug.Log("[07B1] report:\n" + _b1Report);
        Application.Quit(_b1Failures == 0 ? 0 : 1);
    }

    private IEnumerator Approach(SurfacePlayerController player, ShipGameController controller,
        int index)
    {
        ShipConsoleInteractable console = controller.Consoles[index];
        Vector3 dir = ConsoleScreenDir(index);
        Vector3 position = console.transform.position + dir * 1.7f;
        player.TeleportTo(position);
        float yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
        player.SetFacing(yaw, 0f);
        yield return null;
        yield return null;
    }

    private static Vector3 ConsoleScreenDir(int index)
    {
        switch (index)
        {
            case 0:
                return new Vector3(0f, 0f, -1f);
            case 1:
                return new Vector3(1f, 0f, 0f);
            case 2:
                return new Vector3(-1f, 0f, 0f);
            default:
                return new Vector3(0f, 0f, 1f);
        }
    }

    private IEnumerator WaitUntilDoor(ShipDoorInteractable door,
        ShipDoorInteractable.ShipDoorState target, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (door.State != target && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitReal(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private IEnumerator CaptureAt(SurfacePlayerController player, string suffix,
        Vector3 position, float yaw)
    {
        player.TeleportTo(position);
        player.SetFacing(yaw, 0f);
        yield return new WaitForSecondsRealtime(0.6f);
        yield return WaitForScreenshot(Base(suffix));
    }

    private IEnumerator Capture07B(string suffix)
    {
        yield return WaitForScreenshot(Base07B(suffix));
    }

    private string Base07B(string suffix)
    {
        // -ship07BShot takes the OUTPUT FOLDER; every frame is written with
        // the exact contract filename (e.g. frame_ship_door_closed.png).
        return Path.Combine(_base07BPath, suffix + ".png");
    }

    // ====================================================================
    // Phase 07A view set (kept for regression evidence)
    // ====================================================================

    private IEnumerator RunShipViews()
    {
        // Phase 07A.1: the probe does NOT load any scene - normal boot itself
        // enters 03_ShipInterior. This run therefore proves the real
        // standalone launch route (Boot -> ShipInterior -> ShipExplore).
        GameSession.Ensure();

        ShipGameController controller = null;
        for (int i = 0; i < 1800; i++)
        {
            controller = Object.FindAnyObjectByType<ShipGameController>();
            if (controller != null && controller.IsReady
                && controller.Consoles.Count == 4 && controller.Player != null)
            {
                break;
            }

            yield return null;
        }

        if (controller == null || !controller.IsReady || controller.Player == null)
        {
            Debug.LogError("[ShipShotProbe] ship interior never ready");
            Application.Quit(1);
            yield break;
        }

        Screen.SetResolution(1920, 1080, false);
        SurfacePlayerController player = controller.Player;
        yield return new WaitForSecondsRealtime(0.5f);

        // corridor (looking forward to the bridge door / side doors).
        yield return Capture(player, "corridor", new Vector3(0f, 0f, 0.8f), 0f);
        // bridge / ops deck toward console 01.
        yield return Capture(player, "bridge", new Vector3(0f, 0f, 8.0f), 0f);
        // preparation / gear bay toward console 02.
        yield return CaptureToward(player, "preparation_bay", new Vector3(-3.4f, 0f, 3.4f),
            new Vector3(-4.95f, 1.3f, 3.4f));
        // fabrication / processing bay toward console 03.
        yield return CaptureToward(player, "processing_bay", new Vector3(3.4f, 0f, 3.4f),
            new Vector3(4.95f, 1.3f, 3.4f));
        // outpost / comms station toward console 04.
        yield return CaptureToward(player, "outpost_station", new Vector3(-2.2f, 0f, -6.8f),
            new Vector3(-2.2f, 1.3f, -8.85f));
        // airlock / EVA bay looking aft at the deployment station.
        yield return Capture(player, "airlock", new Vector3(0f, 0f, -11.4f), 180f);

        Debug.Log("[ShipShotProbe] wrote ship graybox frames: " + _basePath + "*.png");
        Application.Quit(0);
    }

    private IEnumerator Capture(SurfacePlayerController player, string suffix,
        Vector3 position, float yaw)
    {
        player.TeleportTo(position);
        player.SetFacing(yaw, 0f);
        yield return new WaitForSecondsRealtime(0.6f);
        yield return WaitForScreenshot(Base(suffix));
    }

    private IEnumerator CaptureToward(SurfacePlayerController player, string suffix,
        Vector3 position, Vector3 lookAt)
    {
        player.TeleportTo(position);
        yield return null;
        FaceToward(player, lookAt);
        yield return new WaitForSecondsRealtime(0.6f);
        yield return WaitForScreenshot(Base(suffix));
    }

    private string Base(string suffix)
    {
        return Path.Combine(Path.GetDirectoryName(_basePath) ?? ".",
            Path.GetFileNameWithoutExtension(_basePath) + "_" + suffix + ".png");
    }

    private static IEnumerator WaitForScreenshot(string path)
    {
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 900; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 20000)
            {
                break;
            }

            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.15f);
    }

    private static void FaceToward(SurfacePlayerController player, Vector3 target)
    {
        Vector3 delta = target - player.transform.position;
        float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        player.SetFacing(yaw, 0f);
    }

    private static string ParseFlag(string flag)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
