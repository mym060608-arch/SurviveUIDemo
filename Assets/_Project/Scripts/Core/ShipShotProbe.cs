using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    private string _basePath;
    private string _base07BPath;
    private bool _active;

    private void Awake()
    {
        _basePath = ParseFlag(Flag);
        _base07BPath = ParseFlag(Flag07B);
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
            "frame_console_preparation_idle",
            "frame_console_processing_idle",
            "frame_console_outpost_idle"
        };
        string[] focusSuffixes =
        {
            "frame_console_overview_focus",
            "frame_console_preparation_focus",
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
