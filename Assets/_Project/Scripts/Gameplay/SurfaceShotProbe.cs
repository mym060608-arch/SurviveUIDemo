using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Clean combined gameplay screenshot probe (validation/asset pipeline only).
///
/// Inert unless launched with  -surfaceShot &lt;absolute png path&gt;. Lives on
/// 00_Boot (DontDestroyOnLoad) so a Release player can drive the real journey:
/// Hub loadout confirm -> 02_NereidSurface -> walk player next to node 0 ->
/// deploy -> wait for the wireless transfer stage -> capture the combined
/// 3D+HUD frame -> quit. Used with a Release player so the screenshot carries
/// no engine watermark. Semantic checks belong to AutoSurfaceFlowProbe.
/// </summary>
public class SurfaceShotProbe : MonoBehaviour
{
    private const string Flag = "-surfaceShot";

    private string _outputPath;
    private bool _active;

    private void Awake()
    {
        _outputPath = ParseFlag(Flag);
        _active = !string.IsNullOrEmpty(_outputPath);
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
        yield return new WaitForSecondsRealtime(150f);
        Debug.LogError("[SurfaceShotProbe] watchdog timeout");
        Application.Quit(2);
    }

    private IEnumerator Run()
    {
        // Stage A: reach the Hub (00_Boot auto-loads it) and confirm the
        // default loadout so the expedition gate allows the surface launch.
        GameSession session = GameSession.Instance;
        AppController app = null;
        for (int i = 0; i < 1200; i++)
        {
            app = Object.FindAnyObjectByType<AppController>();
            if (app != null && app.IsReady && session != null)
            {
                break;
            }

            yield return null;
        }

        if (app == null || session == null)
        {
            Debug.LogError("[SurfaceShotProbe] hub never ready");
            Application.Quit(1);
            yield break;
        }

        session.ResetSession();
        if (!session.TryConfirmLoadout() || !session.CanStartExpedition)
        {
            Debug.LogError("[SurfaceShotProbe] cannot confirm loadout");
            Application.Quit(1);
            yield break;
        }

        // Stage B: enter the surface scene.
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        SurfaceGameController controller = null;
        for (int i = 0; i < 1200; i++)
        {
            controller = Object.FindAnyObjectByType<SurfaceGameController>();
            if (controller != null && controller.IsReady && controller.Nodes.Count > 0)
            {
                break;
            }

            yield return null;
        }

        if (controller == null || controller.Nodes.Count == 0)
        {
            Debug.LogError("[SurfaceShotProbe] surface never ready");
            Application.Quit(1);
            yield break;
        }

        // Deterministic framing: windowed 1920x1080.
        Screen.SetResolution(1920, 1080, false);

        SurfaceResourceNode node0 = controller.Nodes[0];
        SurfacePlayerController player = Object.FindAnyObjectByType<SurfacePlayerController>();
        if (player == null)
        {
            Debug.LogError("[SurfaceShotProbe] player missing");
            Application.Quit(1);
            yield break;
        }

        player.TeleportTo(node0.transform.position + new Vector3(-1.6f, 0f, 1.6f));
        yield return null;
        bool deployed = controller.TryDeployNearest();

        // Capture while the wireless transfer beam is visible (after harvest).
        // Bounded by real seconds: frame counts do not bound wall time when
        // the player loop is uncapped.
        float waitUntil = Time.realtimeSinceStartup + 14f;
        while ((!deployed || node0.State != SurfaceResourceNode.NodeState.Transmitting)
               && Time.realtimeSinceStartup < waitUntil)
        {
            yield return null;
        }

        if (node0.State != SurfaceResourceNode.NodeState.Transmitting)
        {
            Debug.LogError("[SurfaceShotProbe] node0 never reached wireless transfer");
            Application.Quit(1);
            yield break;
        }

        yield return new WaitForSecondsRealtime(0.25f);
        Capture();
        yield return new WaitForSecondsRealtime(0.2f);

        // Second frame right after the settle: shows 传输完成 矿物 +5.
        float settleUntil = Time.realtimeSinceStartup + 8f;
        while (node0.State != SurfaceResourceNode.NodeState.Depleted
               && Time.realtimeSinceStartup < settleUntil)
        {
            yield return null;
        }

        if (node0.State != SurfaceResourceNode.NodeState.Depleted)
        {
            Debug.LogError("[SurfaceShotProbe] node0 never settled");
            Application.Quit(1);
            yield break;
        }

        yield return new WaitForSecondsRealtime(0.35f);
        string settledPath = Path.Combine(
            Path.GetDirectoryName(_outputPath) ?? ".",
            Path.GetFileNameWithoutExtension(_outputPath) + "_settled.png");
        CaptureTo(settledPath);

        if (session.HarvestedNodeCount != 1 || session.Minerals != 25)
        {
            Debug.LogError("[SurfaceShotProbe] settle did not pay +5");
            Application.Quit(1);
            yield break;
        }

        Debug.Log("[SurfaceShotProbe] wrote screenshots: " + _outputPath + " | " + settledPath);
        Application.Quit(0);
    }

    private void Capture()
    {
        CaptureTo(_outputPath);
    }

    private static void CaptureTo(string path)
    {
        ScreenCapture.CaptureScreenshot(path);
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
