using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Normal human-runtime routing probe (Phase 07A.1).
///
/// Inert unless launched with  -shipFlow &lt;result file&gt;  (optional
/// -shipFlowShots &lt;base png path&gt;). Starts from the real 00_Boot exactly
/// like a normal player launch and NEVER loads a scene itself: it observes
/// the Boot -&gt; ShipInterior entry, then drives the semantic E paths
/// (airlock -&gt; surface, landing -&gt; ship interior), verifying spawn
/// markers, cursor/input mode, GameSession persistence and absence of
/// duplicates, and writes the PASS evidence lines required by the Phase07A.1
/// contract before quitting. Lives on 00_Boot (DontDestroyOnLoad).
/// </summary>
public class ShipFlowProbe : MonoBehaviour
{
    private const string ResultFlag = "-shipFlow";
    private const string ShotsFlag = "-shipFlowShots";

    private string _resultPath;
    private string _shotsBase;
    private readonly StringBuilder _results = new StringBuilder();
    private int _failures;

    private void Awake()
    {
        _resultPath = ParseFlag(ResultFlag);
        _shotsBase = ParseFlag(ShotsFlag);
        if (string.IsNullOrEmpty(_resultPath) && string.IsNullOrEmpty(_shotsBase))
        {
            enabled = false;
            return;
        }

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartCoroutine(Run());
        StartCoroutine(Watchdog());
    }

    private static IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(300f);
        Debug.LogError("[ShipFlowProbe] watchdog timeout");
        Application.Quit(2);
    }

    private IEnumerator Run()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();
        int mineralsAtStart = session.Minerals;
        int fuelAtStart = session.Fuel;
        int alloyAtStart = session.Alloy;

        // ---- Stage 1: normal boot -> ShipInterior (Default spawn) ----
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

        SurfacePlayerController player = controller != null ? controller.Player : null;
        Check("normal boot entered ShipInterior",
            ActiveName() == GameSession.ShipInteriorSceneName && controller != null && player != null);

        ShipSpawnMarker defaultMarker = FindMarker(ShipSpawnMarker.DefaultId);
        bool defaultSpawnOk = player != null && defaultMarker != null
                              && Near(player.transform.position, defaultMarker.transform.position);
        Check("default ship spawn valid", defaultSpawnOk);

        Check("ShipExplore input restored",
            player != null && player.InputEnabled && controller.Interactor != null
            && UnityEngine.Cursor.lockState == CursorLockMode.Locked
            && !UnityEngine.Cursor.visible);

        if (_failures > 0)
        {
            Finish();
            yield break;
        }

        yield return new WaitForSecondsRealtime(0.6f);
        if (!string.IsNullOrEmpty(_shotsBase) && defaultMarker != null)
        {
            player.TeleportTo(defaultMarker.transform.position);
            player.SetFacing(defaultMarker.transform.eulerAngles.y, 0f);
            yield return null;
            yield return Capture(Base("_boot_spawn"));
        }

        // ---- Stage 2: airlock E -> NEREID surface ----
        player.TeleportTo(new Vector3(0f, 0f, -11.4f));
        player.SetFacing(180f, 0f);
        yield return null;
        yield return null;

        ShipAirlockInteractable airlock = controller.Airlock;
        bool gazeOnAirlock = false;
        for (int i = 0; i < 600 && !gazeOnAirlock; i++)
        {
            controller.Interactor.RefreshTarget();
            gazeOnAirlock = ReferenceEquals(controller.Interactor.CurrentTarget, airlock)
                            && controller.Interactor.HasValidTarget;
            if (!gazeOnAirlock)
            {
                yield return null;
            }
        }

        Check("airlock gaze resolves", gazeOnAirlock);
        bool airlockInteracted = false;
        if (gazeOnAirlock)
        {
            airlockInteracted = controller.Interactor.TryInteract();
        }

        // Phase 07B.2: E opens the deployment confirmation; 确认降临 then
        // performs the ShipInterior -> Surface routing through the real UI.
        ShipOverlayUi overlay = Object.FindAnyObjectByType<ShipOverlayUi>();
        for (int i = 0; i < 900
             && (overlay == null || !overlay.IsOpen
                 || overlay.DeployPage == null || !overlay.DeployPage.IsBound); i++)
        {
            overlay = Object.FindAnyObjectByType<ShipOverlayUi>();
            yield return null;
        }

        bool confirmOpen = overlay != null && overlay.IsOpen
                           && overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm
                           && overlay.DeployPage != null && overlay.DeployPage.IsBound;
        Check("deployment confirmation open after airlock E (no scene load yet)",
            confirmOpen && ActiveIs(GameSession.ShipInteriorSceneName));
        if (confirmOpen && overlay.DeployPage.Root != null)
        {
            var confirm = overlay.DeployPage.Root
                .Q<UnityEngine.UIElements.Button>(
                    ShipDeployConfirmPage.ConfirmButtonName);
            if (confirm != null)
            {
                UiToolkitPointerSimulator.Click(confirm);
            }
        }

        SurfaceGameController surface = null;
        for (int i = 0; i < 1800 && !ActiveIs(GameSession.SurfaceSceneName); i++)
        {
            yield return null;
        }

        for (int i = 0; i < 1800; i++)
        {
            surface = Object.FindAnyObjectByType<SurfaceGameController>();
            if (surface != null && surface.IsReady && surface.Player != null)
            {
                break;
            }

            yield return null;
        }

        bool airlockOk = airlockInteracted && ActiveIs(GameSession.SurfaceSceneName)
                         && surface != null && surface.IsReady;
        Check("airlock entered Surface", airlockOk);
        Check("surface FP input restored", surface != null
              && UnityEngine.Cursor.lockState == CursorLockMode.Locked
              && !UnityEngine.Cursor.visible);

        if (_failures > 0 || surface == null)
        {
            Finish();
            yield break;
        }

        // ---- Stage 3: landing E -> ShipInterior (FromSurface spawn) ----
        SurfacePlayerController surfacePlayer = surface.Player;
        surfacePlayer.TeleportTo(new Vector3(0.8f, 0f, 0.8f));
        yield return null;
        Vector3 toPad = Vector3.zero - surfacePlayer.transform.position;
        surfacePlayer.SetFacing(Mathf.Atan2(toPad.x, toPad.z) * Mathf.Rad2Deg, 0f);
        yield return null;
        yield return null;

        bool gazeOnPad = false;
        for (int i = 0; i < 600 && !gazeOnPad; i++)
        {
            surface.Interactor.RefreshTarget();
            gazeOnPad = surface.Interactor.HasValidTarget;
            if (!gazeOnPad)
            {
                yield return null;
            }
        }

        Check("landing return gaze resolves", gazeOnPad);
        bool returnInteracted = false;
        if (gazeOnPad)
        {
            returnInteracted = surface.Interactor.TryInteract();
        }

        controller = null;
        for (int i = 0; i < 1800 && !ActiveIs(GameSession.ShipInteriorSceneName); i++)
        {
            yield return null;
        }

        for (int i = 0; i < 1800; i++)
        {
            controller = Object.FindAnyObjectByType<ShipGameController>();
            if (controller != null && controller.IsReady && controller.Player != null)
            {
                break;
            }

            yield return null;
        }

        player = controller != null ? controller.Player : null;
        bool returnOk = returnInteracted && ActiveIs(GameSession.ShipInteriorSceneName)
                        && controller != null && player != null;
        Check("surface return entered ShipInterior", returnOk);

        ShipSpawnMarker surfaceMarker = FindMarker(ShipSpawnMarker.FromSurfaceId);
        bool fromSurfaceSpawnOk = player != null && surfaceMarker != null
                                  && Near(player.transform.position, surfaceMarker.transform.position)
                                  && Mathf.Abs(player.YawDegrees) < 0.5f;
        Check("from-surface spawn valid", fromSurfaceSpawnOk);
        Check("returned player inside airlock zone",
            player != null
            && ShipGameController.ResolveZoneZh(player.transform.position) == "气闸舱");
        Check("ShipExplore restored after return",
            player != null && player.InputEnabled && controller != null
            && controller.Interactor != null
            && UnityEngine.Cursor.lockState == CursorLockMode.Locked
            && !UnityEngine.Cursor.visible);

        if (_failures > 0)
        {
            Finish();
            yield break;
        }

        yield return new WaitForSecondsRealtime(0.6f);
        if (!string.IsNullOrEmpty(_shotsBase) && surfaceMarker != null)
        {
            player.TeleportTo(surfaceMarker.transform.position);
            player.SetFacing(surfaceMarker.transform.eulerAngles.y, 0f);
            yield return null;
            yield return Capture(Base("_return_from_surface"));
        }

        // ---- Stage 4: session persistence + no duplicates ----
        int gameSessions = Object.FindObjectsByType<GameSession>().Length;
        Check("session persisted",
            GameSession.Instance == session && session.Minerals == mineralsAtStart
            && session.Fuel == fuelAtStart && session.Alloy == alloyAtStart);
        Check("no duplicate GameSession", gameSessions == 1);

        Finish();
    }

    private static ShipSpawnMarker FindMarker(string markerId)
    {
        foreach (ShipSpawnMarker marker in Object.FindObjectsByType<ShipSpawnMarker>())
        {
            if (marker.MarkerId == markerId)
            {
                return marker;
            }
        }

        return null;
    }

    private static bool Near(Vector3 a, Vector3 b)
    {
        return Mathf.Abs(a.x - b.x) < 0.05f && Mathf.Abs(a.z - b.z) < 0.05f;
    }

    private static bool ActiveIs(string name)
    {
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() && active.name == name;
    }

    private static string ActiveName()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() ? active.name : "<none>";
    }

    private IEnumerator Capture(string path)
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

    private string Base(string suffix)
    {
        return Path.Combine(Path.GetDirectoryName(_shotsBase) ?? ".",
            Path.GetFileNameWithoutExtension(_shotsBase) + suffix + ".png");
    }

    private void Check(string label, bool condition)
    {
        _results.AppendLine((condition ? "PASS" : "FAIL") + " :: " + label);
        if (!condition)
        {
            _failures++;
        }
    }

    private void Finish()
    {
        _results.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        try
        {
            if (!string.IsNullOrEmpty(_resultPath))
            {
                File.WriteAllText(_resultPath, _results.ToString());
                Debug.Log("[ShipFlowProbe] wrote result: " + _resultPath);
            }
            else
            {
                Debug.Log("[ShipFlowProbe] result:\n" + _results);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ShipFlowProbe] cannot write result: " + e);
            _failures++;
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
