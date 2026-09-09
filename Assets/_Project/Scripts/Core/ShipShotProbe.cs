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

    private string _basePath;
    private bool _active;

    private void Awake()
    {
        _basePath = ParseFlag(Flag);
        _active = !string.IsNullOrEmpty(_basePath);
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
        yield return new WaitForSecondsRealtime(240f);
        Debug.LogError("[ShipShotProbe] watchdog timeout");
        Application.Quit(2);
    }

    private IEnumerator Run()
    {
        GameSession.Ensure();
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);

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
