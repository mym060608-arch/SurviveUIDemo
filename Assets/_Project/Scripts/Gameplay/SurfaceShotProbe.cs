using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Clean first-person + wrist screenshot probe (validation/asset pipeline).
///
/// Inert unless launched with  -surfaceShot &lt;base png path&gt;. Lives on
/// 00_Boot (DontDestroyOnLoad) so a Release player drives the real journey:
/// Hub loadout confirm -> 02_NereidSurface (first person) ->
///   A. first-person exploration frame,
///   B. wrist open - circular LOCAL MAP,
///   C. wrist open with navigation node selected,
///   D. wrist open during wireless transfer,
///   E. wrist open after settlement (传输完成 / node recycled).
/// All through public gameplay APIs (no fake physical input). Semantic checks
/// belong to AutoSurfaceFlowProbe.
/// </summary>
public class SurfaceShotProbe : MonoBehaviour
{
    private const string Flag = "-surfaceShot";

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
        yield return new WaitForSecondsRealtime(180f);
        Debug.LogError("[SurfaceShotProbe] watchdog timeout");
        Application.Quit(2);
    }

    private IEnumerator Run()
    {
        // Phase 07A.1: normal boot now enters the ship interior. This probe
        // needs the Hub-side loadout confirmation ceremony, so it enters the
        // legacy Hub explicitly before continuing its surface journey.
        GameSession.Ensure();
        SceneManager.LoadScene(BootController.HubSceneName);

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

        Screen.SetResolution(1920, 1080, false);
        SurfacePlayerController player = Object.FindAnyObjectByType<SurfacePlayerController>();
        SurfaceResourceNode node0 = controller.Nodes[0];
        if (player == null)
        {
            Debug.LogError("[SurfaceShotProbe] player missing");
            Application.Quit(1);
            yield break;
        }

        string explorePath = Base("_fp_explore");
        string compassNavPath = Base("_fp_compass_nav");
        string wristMapPath = Base("_wrist_map");
        string wristNavPath = Base("_wrist_nav");
        string wristTransferPath = Base("_wrist_transfer");
        string wristSettledPath = Base("_wrist_settled");

        // A: first-person exploration, standing beside node 0 looking at it.
        player.TeleportTo(node0.transform.position + new Vector3(-1.7f, 0f, 1.9f));
        yield return null;
        FaceToward(player, node0.transform.position);
        yield return WaitForScreenshot(explorePath);
        if (!File.Exists(explorePath))
        {
            Debug.LogError("[SurfaceShotProbe] explore capture failed");
            Application.Quit(1);
            yield break;
        }

        // A2 (Round 9): compass HUD engaged - select node 0 while exploring;
        // the player already faces it, so the fixed centre light dot lights up
        // and the restrained target summary appears under the compass frame.
        if (!controller.TrySelectNavigationTarget(0))
        {
            Debug.LogError("[SurfaceShotProbe] compass nav selection failed");
            Application.Quit(1);
            yield break;
        }

        yield return new WaitForSecondsRealtime(0.5f);
        yield return WaitForScreenshot(compassNavPath);

        // B: wrist open - circular local map.
        controller.SetWristOpen(true);
        yield return new WaitForSecondsRealtime(0.5f);
        yield return WaitForScreenshot(wristMapPath);

        // C: wrist open - node 0 selected as navigation target.
        controller.TrySelectNavigationTarget(0);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return WaitForScreenshot(wristNavPath);

        // D: close wrist, deploy node 0, reopen during wireless transfer.
        controller.SetWristOpen(false);
        player.TeleportTo(node0.transform.position + new Vector3(-1.7f, 0f, 1.9f));
        yield return null;
        if (!controller.TryDeployNearest())
        {
            Debug.LogError("[SurfaceShotProbe] deploy failed");
            Application.Quit(1);
            yield break;
        }

        float transferUntil = Time.realtimeSinceStartup + 14f;
        while (node0.State != SurfaceResourceNode.NodeState.Transmitting
               && Time.realtimeSinceStartup < transferUntil)
        {
            yield return null;
        }

        controller.SetWristOpen(true);
        yield return new WaitForSecondsRealtime(0.45f);
        yield return WaitForScreenshot(wristTransferPath);

        // E: settlement completes while the wrist stays open.
        float settleUntil = Time.realtimeSinceStartup + 8f;
        while (node0.State != SurfaceResourceNode.NodeState.Depleted
               && Time.realtimeSinceStartup < settleUntil)
        {
            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.5f);
        yield return WaitForScreenshot(wristSettledPath);

        if (session.Minerals != 25 || session.HarvestedNodeCount != 1)
        {
            Debug.LogError("[SurfaceShotProbe] settle did not pay +5");
            Application.Quit(1);
            yield break;
        }

        // Shut the wrist panel down before quitting: an active UI Toolkit
        // panel targeting a RenderTexture at Application.Quit can crash the
        // engine graphics teardown on some drivers.
        controller.SetWristOpen(false);
        yield return new WaitForSecondsRealtime(0.4f);

        Debug.Log("[SurfaceShotProbe] wrote screenshots: " + explorePath + " | "
                  + wristMapPath + " | " + wristNavPath + " | " + wristTransferPath
                  + " | " + wristSettledPath);
        Application.Quit(0);
    }

    private string Base(string suffix)
    {
        return Path.Combine(Path.GetDirectoryName(_basePath) ?? ".",
            Path.GetFileNameWithoutExtension(_basePath) + suffix + ".png");
    }

    private static IEnumerator WaitForScreenshot(string path)
    {
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 600; i++)
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
