using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Full cross-scene surface automation probe (validation instrumentation).
///
/// Inert unless launched with  -autoSurface &lt;absolute path&gt;. Drives the
/// authoritative public gameplay APIs (never fake key presses):
/// Hub(confirmed, loaded explicitly) -> 02_NereidSurface -> deploy Node 0 ->
/// harvest + wireless transfer settle -> 返回舰船 -> Hub preserved Minerals.
/// Writes a result file and quits. Lives in 00_Boot so it survives scene
/// transitions. Normal human boot now enters the ship interior; this probe
/// explicitly enters the legacy Hub because it validates Hub-side values.
/// </summary>
public class AutoSurfaceFlowProbe : MonoBehaviour
{
    private const string Flag = "-autoSurface";

    private string _resultPath;
    private readonly StringBuilder _results = new StringBuilder();
    private int _failures;

    private void Awake()
    {
        _resultPath = ParseFlag(Flag);
        if (string.IsNullOrEmpty(_resultPath))
        {
            enabled = false;
            return;
        }

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (!string.IsNullOrEmpty(_resultPath))
        {
            StartCoroutine(RunFlow());
            StartCoroutine(Watchdog());
        }
    }

    private static IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(180f);
        Debug.LogError("[AutoSurfaceFlowProbe] watchdog timeout");
        Application.Quit(2);
    }

    private IEnumerator RunFlow()
    {
        // Stage A: reach the legacy Hub explicitly (normal boot now enters
        // the ship interior; this probe needs Hub-side values).
        GameSession.Ensure();
        SceneManager.LoadScene(BootController.HubSceneName);
        AppController app = null;
        for (int i = 0; i < 1200; i++)
        {
            app = Object.FindAnyObjectByType<AppController>();
            if (app != null && app.IsReady && GameSession.Instance != null)
            {
                break;
            }

            yield return null;
        }

        if (app == null || GameSession.Instance == null)
        {
            Fail("hub never ready");
            Finish();
            yield break;
        }

        GameSession session = GameSession.Instance;
        session.ResetSession();

        Check("defaults minerals=20 nodes=0", session.Minerals == 20 && session.HarvestedNodeCount == 0);

        // Gate: Reset leaves the loadout unconfirmed -> launch rejected.
        Check("launch gate blocks unconfirmed", !session.CanStartExpedition);
        Check("confirm accepted", session.TryConfirmLoadout());
        Check("launch gate allows confirmed", session.CanStartExpedition);

        // Stage B: enter the surface scene.
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        SurfaceGameController controller = null;
        for (int i = 0; i < 1200; i++)
        {
            controller = Object.FindAnyObjectByType<SurfaceGameController>();
            if (controller != null && controller.IsReady)
            {
                break;
            }

            yield return null;
        }

        if (controller == null || controller.Nodes.Count != GameSession.SurfaceNodeCount)
        {
            Fail("surface controller or node count invalid");
            Finish();
            yield break;
        }

        SurfaceResourceNode node0 = controller.Nodes[0];
        Check("surface entry minerals unchanged", session.Minerals == 20);
        Check("node0 available at entry", node0.State == SurfaceResourceNode.NodeState.Available);
        Check("surface FP cursor locked+hidden",
            UnityEngine.Cursor.lockState == CursorLockMode.Locked && !UnityEngine.Cursor.visible);

        // Stage C: deploy Node 0 through the semantic E-equivalent path.
        SurfacePlayerController player = Object.FindAnyObjectByType<SurfacePlayerController>();
        if (player == null)
        {
            Fail("player missing");
            Finish();
            yield break;
        }

        player.TeleportTo(node0.transform.position + Vector3.forward * 1.2f);
        yield return null;
        bool deployed = controller.TryDeployNearest();
        Check("deploy node0 accepted", deployed
              && node0.State == SurfaceResourceNode.NodeState.Harvesting);
        Check("minerals unchanged during harvest", session.Minerals == 20);

        if (deployed)
        {
            Check("double deploy rejected", !controller.TryDeployNearest());
        }

        // Stage D: wait for the real LV.1 harvest and wireless transfer and
        // measure the actual durations (bounded by real seconds, not frames).
        float harvestStart = Time.realtimeSinceStartup;
        float transferStart = harvestStart;
        float settleUntil = Time.realtimeSinceStartup + 14f;
        while (node0.State != SurfaceResourceNode.NodeState.Depleted
               && Time.realtimeSinceStartup < settleUntil)
        {
            if (transferStart == harvestStart
                && node0.State == SurfaceResourceNode.NodeState.Transmitting)
            {
                transferStart = Time.realtimeSinceStartup;
            }

            yield return null;
        }

        if (node0.State == SurfaceResourceNode.NodeState.Depleted)
        {
            float harvestSeconds = transferStart - harvestStart;
            float transferSeconds = Time.realtimeSinceStartup - transferStart;
            Check("LV1 real harvest ~4.0s (actual "
                  + harvestSeconds.ToString("0.00") + "s)", harvestSeconds >= 3.4f
                  && harvestSeconds <= 4.9f);
            Check("LV1 real transfer ~0.75s (actual "
                  + transferSeconds.ToString("0.00") + "s)", transferSeconds >= 0.45f
                  && transferSeconds <= 1.3f);
        }
        else
        {
            Check("LV1 real timing observed (node reached depleted)", false);
        }

        Check("node0 depleted after transfer", node0.State == SurfaceResourceNode.NodeState.Depleted);
        Check("minerals +5 after settle", session.Minerals == 25 && session.HarvestedNodeCount == 1);
        Check("second settle rejected", !session.TryCompleteSurfaceNode(0, 5));
        Check("minerals unchanged on rejected settle", session.Minerals == 25);

        // Stage E: return to the legacy Hub (explicit automation route) and
        // verify preserved Minerals. The human return path now leads to the
        // ship interior and is covered by ShipFlowProbe / Phase07 routing tests.
        SceneRouting.EnterHub();
        for (int i = 0; i < 1200; i++)
        {
            app = Object.FindAnyObjectByType<AppController>();
            if (app != null && app.IsReady)
            {
                break;
            }

            yield return null;
        }

        if (app == null)
        {
            Fail("hub return failed");
            Finish();
            yield break;
        }

        Check("overview shows 25", LabelText(app, ElementNames.MineralsValue) == "25");
        app.ShowScreen(AppScreen.Processing);
        yield return null;
        Check("processing shows 25", LabelText(app, ElementNames.ProcessingMineralsValue) == "25");
        Check("hub cursor restored unlocked+visible",
            UnityEngine.Cursor.lockState == CursorLockMode.None && UnityEngine.Cursor.visible);

        // Stage F: Reset clears the node mask, minerals and launch gate.
        session.ResetSession();
        Check("reset clears surface nodes", session.HarvestedNodeCount == 0);
        Check("reset restores minerals + gate", session.Minerals == 20
              && !session.CanStartExpedition);

        Finish();
    }

    private static string LabelText(AppController app, string name)
    {
        Label label = app.UiRoot?.Q<Label>(name);
        return label != null ? label.text : "<missing>";
    }

    private void Check(string label, bool condition)
    {
        _results.AppendLine((condition ? "PASS" : "FAIL") + " :: " + label);
        if (!condition)
        {
            _failures++;
        }
    }

    private void Fail(string label)
    {
        _results.AppendLine("FAIL :: " + label);
        _failures++;
    }

    private void Finish()
    {
        _results.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        try
        {
            File.WriteAllText(_resultPath, _results.ToString());
            Debug.Log("[AutoSurfaceFlowProbe] wrote result: " + _resultPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[AutoSurfaceFlowProbe] cannot write result: " + e);
        }

        Application.Quit(_failures == 0 ? 0 : 1);
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
