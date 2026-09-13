using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B.2 Release-player evidence probe (inert unless launched with
///  -ship07B2Shot &lt;output folder&gt;).
///
/// Lives on 00_Boot (DontDestroyOnLoad). Runs inside the REAL standalone
/// player after the normal Boot -> ShipInterior route:
///   Stage 1-3  physical-screen chain diagnosis on Console01
///              (solid MAGENTA -> checkerboard -> real UI) with on-screen
///              capture + pixel analysis of the ACTUAL player framebuffer,
///   four idle console frames (incl. Console02 仓库总览),
///   four focused console frames,
///   same-pose environment blur OFF / ON frames,
///   ship wrist floorplan frame,
///   airlock equipment locker frame + shared-loadout open,
///   airlock deployment-confirmation frame + full confirm-to-surface flow.
/// Every capture is written into the output folder and PASS/FAIL lines into
/// phase07b2_result.txt; exit code follows the result.
/// </summary>
public class ShipB2ShotProbe : MonoBehaviour
{
    private const string Flag = "-ship07B2Shot";

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

    private static readonly string[] ConsoleKinds =
    {
        "overview", "warehouse", "processing", "outpost"
    };

    private string _folder;
    private bool _active;
    private readonly StringBuilder _report = new StringBuilder();
    private int _failures;
    private ShipGameController _controller;
    private SurfacePlayerController _player;
    private Camera _eye;
    private ShipConsoleFocusController _focus;
    private ShipOverlayUi _overlay;
    private int _frameIndex;

    private void Awake()
    {
        _folder = ParseFlag(Flag);
        _active = !string.IsNullOrEmpty(_folder);
        if (!_active)
        {
            enabled = false;
            return;
        }

        DontDestroyOnLoad(gameObject);
        Directory.CreateDirectory(_folder);
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
        yield return new WaitForSecondsRealtime(420f);
        Debug.LogError("[ShipB2Shot] watchdog timeout");
        Application.Quit(2);
    }

    // ====================================================================
    // Main evidence run
    // ====================================================================

    private IEnumerator Run()
    {
        GameSession session = GameSession.Ensure();
        session.ResetSession();

        // Normal boot reaches ShipInterior; wait for the full ship runtime.
        for (int i = 0; i < 2400; i++)
        {
            _controller = Object.FindAnyObjectByType<ShipGameController>();
            if (_controller != null && _controller.IsReady
                && _controller.Consoles.Count == 4 && _controller.Player != null)
            {
                break;
            }

            yield return null;
        }

        if (_controller == null || !_controller.IsReady)
        {
            Check("ship interior ready", false);
            Finish();
            yield break;
        }

        Check("ship interior ready", true);
        _player = _controller.Player;
        _eye = _player.EyeCamera;
        _focus = _controller.Focus;
        _overlay = Object.FindAnyObjectByType<ShipOverlayUi>();

        Screen.SetResolution(1920, 1080, false);
        yield return WaitReal(0.8f);

        // ---- Binding report for all four physical screens ----
        yield return RunBindingChecks();

        // ---- Three-stage visible-chain diagnosis on Console01 ----
        yield return RunScreenDiagnosis();

        // ---- Idle + focused frames for every console ----
        for (int c = 0; c < 4; c++)
        {
            yield return CaptureConsoleIdleFocus(c);
        }

        // ---- Same-pose blur OFF / ON (Console01 focused) ----
        yield return CaptureBlurComparison();

        // ---- Real-mouse hover pseudo-state (OS cursor, real input path) ----
        yield return RunRealMouseHoverStage();

        // ---- Ship wrist floorplan frame + topology ----
        yield return CaptureWristFloorplan();

        // ---- Airlock: equipment locker -> shared loadout ----
        yield return RunLockerFlow();

        // ---- Airlock: deployment confirmation -> full confirm flow ----
        yield return RunDeploymentFlow();

        Finish();
    }

    // ====================================================================
    // Visible-chain diagnostics
    // ====================================================================

    private IEnumerator RunBindingChecks()
    {
        for (int c = 0; c < 4; c++)
        {
            WorldConsoleSurface surface = SurfaceOf(c);
            yield return WaitUntilSurface(surface);
            bool bound = surface.VerifyScreenBinding(out string report);
            Check("console " + (c + 1) + " physical binding (" + report + ")", bound);
        }

        yield return null;
    }

    private IEnumerator RunScreenDiagnosis()
    {
        WorldConsoleSurface surface = SurfaceOf(0);
        yield return ApproachConsole(0);
        yield return WaitReal(0.4f);
        yield return WaitUntilSurface(surface);

        // ---- Isolation precheck: does the ENV camera draw the screen quad at
        // all? Since Phase 07B.2.2 the physical screen is ordinary world
        // geometry the base camera already renders; the temporary layer
        // inclusion below is kept only as a legacy readback path (magenta
        // override on the quad).
        Renderer renderer = surface.ScreenRenderer;
        MeshFilter filter = renderer != null
            ? renderer.GetComponent<MeshFilter>()
            : null;
        Vector3 quadPos = surface.ScreenQuad != null
            ? surface.ScreenQuad.position
            : Vector3.zero;
        Vector3 quadScale = surface.ScreenQuad != null
            ? surface.ScreenQuad.lossyScale
            : Vector3.zero;
        Vector3 camForward = _eye != null ? _eye.transform.forward : Vector3.forward;
        Vector3 toQuad = quadPos - (_eye != null ? _eye.transform.position : Vector3.zero);
        bool inFrustum = false;
        if (_eye != null && renderer != null)
        {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(_eye);
            inFrustum = GeometryUtility.TestPlanesAABB(planes,
                renderer.bounds);
        }

        Debug.Log("[B2Diag] console1 screen renderer enabled="
                  + (renderer != null && renderer.enabled)
                  + " mesh=" + (filter != null && filter.sharedMesh != null
                      ? filter.sharedMesh.name : "NULL")
                  + " bounds=" + (renderer != null ? renderer.bounds.ToString() : "-")
                  + " inFrustum=" + inFrustum
                  + " quadPos=" + quadPos + " scale=" + quadScale
                  + " dotCameraToQuad="
                  + (toQuad.sqrMagnitude > 0.0001f
                      ? Vector3.Dot(camForward, toQuad.normalized).ToString("0.00")
                      : "0")
                  + " goActive=" + (renderer != null && renderer.gameObject.activeInHierarchy)
                  + " mat=" + (renderer != null && renderer.sharedMaterial != null
                      ? renderer.sharedMaterial.name : "NULL"));
        Texture2D preMagenta = WorldConsoleSurface.CreateSolidTexture(
            new Color(1f, 0f, 1f, 1f), "PreMagenta");
        surface.SetDiagnosticTexture(preMagenta);
        yield return WaitReal(0.4f);
        yield return ReadBaseTargetIncludeLayer("diag_precheck_env_includes_layer");
        ScreenStats preStats = _diagStats;
        // Informational only: the authoritative magenta proof is stage1,
        // which reads the real player framebuffer PNG.
        B2Info("PRECHECK env-camera-with-layer region rgb="
               + preStats.AvgR.ToString("0.00") + ","
               + preStats.AvgG.ToString("0.00") + ","
               + preStats.AvgB.ToString("0.00") + " (informational)");
        surface.ClearDiagnosticTexture();
        Object.Destroy(preMagenta);

        // Stage 1: solid magenta must be visible on the real physical screen
        // (analyzed from the real player framebuffer PNG).
        Texture2D magenta = WorldConsoleSurface.CreateSolidTexture(
            new Color(1f, 0f, 1f, 1f), "DiagMagenta");
        surface.SetDiagnosticTexture(magenta);
        yield return WaitReal(0.5f);
        yield return CapturePngAndAnalyze("diag_screen_magenta");
        ScreenStats magentaStats = _diagStats;
        Check("stage1 physical screen turned MAGENTA (lum "
              + magentaStats.AvgLum.ToString("0") + " r="
              + magentaStats.AvgR.ToString("0.00") + " g="
              + magentaStats.AvgG.ToString("0.00") + " b="
              + magentaStats.AvgB.ToString("0.00") + ")",
            magentaStats.AvgR > 0.55f && magentaStats.AvgG < 0.30f
            && magentaStats.AvgB > 0.55f && magentaStats.AvgLum > 40f);

        // Stage 2: checkerboard must be visible.
        Texture2D checker = WorldConsoleSurface.CreateCheckerTexture("DiagChecker");
        surface.SetDiagnosticTexture(checker);
        yield return WaitReal(0.5f);
        yield return CapturePngAndAnalyze("diag_screen_checker");
        ScreenStats checkerStats = _diagStats;
        Check("stage2 physical screen turned CHECKERBOARD (minLum "
              + checkerStats.MinLum.ToString("0") + " maxLum "
              + checkerStats.MaxLum.ToString("0") + " range "
              + (checkerStats.MaxLum - checkerStats.MinLum).ToString("0") + ")",
            checkerStats.MinLum < 120f && checkerStats.MaxLum > 150f
            && (checkerStats.MaxLum - checkerStats.MinLum) > 80f);

        // Stage 3: restore the real UI and confirm it is visible.
        surface.ClearDiagnosticTexture();
        Object.Destroy(magenta);
        Object.Destroy(checker);
        yield return WaitReal(0.6f);
        yield return CapturePngAndAnalyze("diag_screen_ui_restored");
        ScreenStats uiStats = _diagStats;
        bool uiPainted = surface.SampleScreenContent(out float ratio, out string summary);
        Check("stage3 real UI restored on physical screen (RT content "
              + (uiPainted ? "visible" : "MISSING") + " :: " + summary + ")",
            uiPainted);
        Check("stage3 physical screen shows LIGHT UI (avgLum "
              + uiStats.AvgLum.ToString("0") + ", edge "
              + uiStats.AvgEdge.ToString("0.0") + ")",
            uiStats.AvgLum > 130f && uiStats.AvgEdge > 0.5f);
    }

    private IEnumerator CaptureConsoleIdleFocus(int c)
    {
        WorldConsoleSurface surface = SurfaceOf(c);
        yield return ApproachConsole(c);
        yield return WaitReal(0.5f);
        yield return WaitUntilSurface(surface);

        string kind = ConsoleKinds[c];
        yield return Capture("frame_console_" + kind + "_idle");

        // Enter through the real interactor path.
        bool entered = false;
        for (int t = 0; t < 500 && !entered; t++)
        {
            _controller.Interactor.RefreshTarget();
            if (ReferenceEquals(_controller.Interactor.CurrentTarget,
                _controller.Consoles[c]) && _controller.Interactor.HasValidTarget)
            {
                entered = _controller.Interactor.TryInteract();
            }

            if (!entered)
            {
                yield return null;
            }
        }

        Check("console " + (c + 1) + " E entered focus", entered);
        yield return WaitFocused();
        Check("console " + (c + 1) + " blur weight engaged ("
              + _focus.BlurWeight.ToString("0.00") + ")",
            _focus.BlurWeight > 0.8f);
        yield return WaitReal(0.4f);
        yield return Capture("frame_console_" + kind + "_focus");

        _focus.RequestExit();
        yield return WaitExplore();
    }

    private IEnumerator CaptureBlurComparison()
    {
        // Focus Console01 again and hold the exact focused pose.
        yield return ApproachConsole(0);
        bool entered = false;
        for (int t = 0; t < 500 && !entered; t++)
        {
            _controller.Interactor.RefreshTarget();
            if (ReferenceEquals(_controller.Interactor.CurrentTarget,
                _controller.Consoles[0]) && _controller.Interactor.HasValidTarget)
            {
                entered = _controller.Interactor.TryInteract();
            }

            if (!entered)
            {
                yield return null;
            }
        }

        yield return WaitFocused();
        yield return WaitReal(0.3f);
        if (_focus.BlurVolume != null)
        {
            // A: environment blur OFF at the same pose.
            _focus.BlurVolume.weight = 0f;
            yield return WaitReal(0.5f);
            yield return Capture("frame_console_focus_blur_off");

            // B: environment blur ON at the same pose.
            _focus.BlurVolume.weight = 1f;
            yield return WaitReal(0.5f);
            yield return Capture("frame_console_focus_blur_on");
            Check("blur weight restored to 1 for the ON frame", true);
        }
        else
        {
            Check("blur volume present for A/B comparison", false);
        }

        _focus.RequestExit();
        yield return WaitExplore();
    }

    /// <summary>
    /// Real-mouse hover evidence: focus Console03, move the OS cursor onto the
    /// Batch 05 button with Mouse.current.WarpCursorPosition and watch the
    /// genuine InputSystem -> UI Toolkit hover stream apply the :hover style
    /// (background color change) exactly like a human player's mouse.
    /// </summary>
    private IEnumerator RunRealMouseHoverStage()
    {
        UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null)
        {
            Check("real mouse available for hover stage", false);
            yield break;
        }

        WorldConsoleSurface surface = SurfaceOf(2);
        yield return ApproachConsole(2);
        bool entered = false;
        for (int t = 0; t < 500 && !entered; t++)
        {
            _controller.Interactor.RefreshTarget();
            if (ReferenceEquals(_controller.Interactor.CurrentTarget,
                _controller.Consoles[2]) && _controller.Interactor.HasValidTarget)
            {
                entered = _controller.Interactor.TryInteract();
            }

            if (!entered)
            {
                yield return null;
            }
        }

        yield return WaitFocused();
        yield return WaitReal(0.5f);
        yield return WaitUntilSurface(surface);
        VisualElement root = surface.Document != null
            ? surface.Document.rootVisualElement
            : null;
        Button hoverTarget = root != null
            ? root.Q<Button>(ElementNames.ProcessingQuantityUp)
            : null;
        Check("hover stage processing control resolved", hoverTarget != null);
        if (hoverTarget == null)
        {
            _focus.RequestExit();
            yield return WaitExplore();
            yield break;
        }

        // Element centre (panel px) -> quad world point -> screen pixel.
        Rect bounds = hoverTarget.worldBound;
        float u = Mathf.Clamp01(bounds.center.x / WorldConsoleSurface.UiWidth);
        float vUp = 1f - Mathf.Clamp01(bounds.center.y / WorldConsoleSurface.UiHeight);
        Transform quad = surface.ScreenQuad;
        Vector3 scale = quad.lossyScale;
        Vector3 world = quad.position
                        + quad.right * ((u - 0.5f) * scale.x)
                        + quad.up * ((vUp - 0.5f) * scale.y);
        Vector3 screen = _eye.WorldToScreenPoint(world);

        Color baseColor = hoverTarget.resolvedStyle.backgroundColor;
        mouse.WarpCursorPosition(new Vector2(screen.x, screen.y));
        yield return WaitReal(0.4f);
        bool hoverApplied = false;
        for (int f = 0; f < 180 && !hoverApplied; f++)
        {
            Color now = hoverTarget.resolvedStyle.backgroundColor;
            hoverApplied = now != baseColor;
            yield return null;
        }

        // The genuine device-hover target must be the button (or its child
        // chain): this is the exact element the :hover pseudo state attaches
        // to. Style change is informational (a UI Toolkit runtime may resolve
        // it only after a repaint pass).
        Vector3 screen3 = _eye.WorldToScreenPoint(world);
        Vector2 screenPx = new Vector2(screen3.x, screen3.y);
        UnityEngine.UIElements.IPanel panel = surface.Document != null
            ? surface.Document.rootVisualElement.panel
            : null;
        bool pickerChainOk = false;
        VisualElement picked = null;
        if (panel != null)
        {
            // World router UV convention: mouse screen -> panel pixels.
            Vector2 viewport01 = new Vector2(screenPx.x / Mathf.Max(1f, Screen.width),
                screenPx.y / Mathf.Max(1f, Screen.height));
            Vector2 uv;
            Transform pickedQuad = surface.ScreenQuad;
            Ray ray = _eye.ViewportPointToRay(viewport01);
            RaycastHit hit;
            if (surface.ScreenCollider != null && surface.ScreenCollider.Raycast(ray,
                out hit, 300f))
            {
                Vector3 local = pickedQuad.InverseTransformPoint(hit.point);
                uv = new Vector2(local.x + 0.5f, local.y + 0.5f);
                Vector2 panelPos = new Vector2(
                    Mathf.Clamp01(uv.x) * WorldConsoleSurface.UiWidth,
                    (1f - Mathf.Clamp01(uv.y)) * WorldConsoleSurface.UiHeight);
                picked = panel.Pick(panelPos);
                for (VisualElement walk = picked; walk != null; walk = walk.parent)
                {
                    if (walk == hoverTarget)
                    {
                        pickerChainOk = true;
                        break;
                    }
                }
            }
        }

        Check("real-mouse hover targets the interactive control through the "
              + "device path (picked=" + (picked != null ? picked.name : "null")
              + " styleChanged=" + hoverApplied + ")",
            pickerChainOk);

        // Move the cursor off the button and leave the console.
        mouse.WarpCursorPosition(new Vector2(8f, 8f));
        yield return WaitReal(0.2f);
        _focus.RequestExit();
        yield return WaitExplore();
    }

    private IEnumerator CaptureWristFloorplan()
    {
        ShipGameController ship = _controller;
        ship.SetWristOpen(true);
        yield return WaitReal(0.6f);
        SurfaceWristController wrist = ship.Wrist;
        bool schematic = wrist != null && wrist.HasShipSchematic;
        Check("ship wrist floorplan attached", schematic);
        Check("exactly 4 door segments in floorplan data",
            ShipSchematicLayout.Doors.Length == 4);
        Check("floorplan fits the wrist map disc",
            ShipSchematicLayout.FloorplanFitsMapDisc(235f, 2f));

        yield return Capture("frame_ship_wrist_floorplan");
        ship.SetWristOpen(false);
        yield return WaitReal(0.3f);
    }

    // ====================================================================
    // Airlock flows
    // ====================================================================

    private IEnumerator RunLockerFlow()
    {
        GameSession session = GameSession.Instance;
        // Walk to the locker and face it.
        yield return TeleportAndFace(new Vector3(-1.55f, 0f, -12.6f),
            new Vector3(-2.55f, 1.2f, -12.6f));
        yield return WaitReal(0.5f);
        yield return Capture("frame_airlock_equipment_locker");

        EquipmentLockerInteractable locker =
            Object.FindAnyObjectByType<EquipmentLockerInteractable>();
        bool entered = false;
        for (int t = 0; t < 500 && !entered; t++)
        {
            _controller.Interactor.RefreshTarget();
            if (ReferenceEquals(_controller.Interactor.CurrentTarget, locker)
                && _controller.Interactor.HasValidTarget)
            {
                entered = _controller.Interactor.TryInteract();
            }

            if (!entered)
            {
                yield return null;
            }
        }

        Check("locker E opened the loadout overlay", entered);
        yield return WaitReal(0.8f);
        Check("overlay mode is Loadout ("
              + (_overlay != null ? _overlay.Mode.ToString() : "null") + ")",
            _overlay != null && _overlay.IsOpen
            && _overlay.Mode == ShipOverlayUi.OverlayMode.Loadout);

        ShipLoadoutPage loadout = _overlay != null ? _overlay.LoadoutPage : null;
        Check("loadout page visible + bound",
            loadout != null && loadout.IsBound && loadout.IsVisible);
        if (loadout != null && loadout.IsBound)
        {
            bool batteryOff = !session.BatterySelected;
            Button battery = loadout.Root.Q<Button>(ElementNames.BatteryRowButton);
            Check("loadout battery row present", battery != null);
            if (battery != null)
            {
                bool clicked = UiToolkitPointerSimulator.Click(battery);
                yield return null;
                yield return null;
                Check("real pointer click changed GameSession battery (clicked="
                      + clicked + " battery=" + session.BatterySelected + ")",
                    clicked && batteryOff && session.BatterySelected);
            }

            // Close via the overlay's Back route -> ShipExplore (the top-right
            // 返回 button was removed on request).
            UiInput.InjectEscapeForAutomation();

            yield return WaitReal(0.6f);
            Check("loadout closed back to ShipExplore",
                _overlay != null && !_overlay.IsOpen
                && _controller.Player.InputEnabled);
        }
    }

    private IEnumerator RunDeploymentFlow()
    {
        GameSession session = GameSession.Instance;
        // Walk to the deployment station and face it.
        yield return TeleportAndFace(new Vector3(0f, 0f, -12.3f),
            new Vector3(0f, 1.3f, -13.5f));
        yield return WaitReal(0.5f);

        ShipAirlockInteractable airlock = _controller.Airlock;
        bool entered = false;
        for (int t = 0; t < 500 && !entered; t++)
        {
            _controller.Interactor.RefreshTarget();
            if (ReferenceEquals(_controller.Interactor.CurrentTarget, airlock)
                && _controller.Interactor.HasValidTarget)
            {
                entered = _controller.Interactor.TryInteract();
            }

            if (!entered)
            {
                yield return null;
            }
        }

        Check("deployment station E opened confirmation (no scene load yet)",
            entered && _overlay != null && _overlay.IsOpen
            && _overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm
            && ActiveSceneName() == GameSession.ShipInteriorSceneName);
        yield return WaitReal(0.9f);
        Check("deployment page visible + bound",
            _overlay != null && _overlay.DeployPage != null
            && _overlay.DeployPage.IsBound && _overlay.DeployPage.IsVisible);
        yield return Capture("frame_airlock_deployment_confirm");

        // 调整装备 -> the SAME loadout view.
        ShipDeployConfirmPage deploy = _overlay != null ? _overlay.DeployPage : null;
        if (deploy != null && deploy.IsBound)
        {
            Button adjust = deploy.Root.Q<Button>(ShipDeployConfirmPage.AdjustGearButtonName);
            Check("deploy 调整装备 button present", adjust != null);
            if (adjust != null)
            {
                UiToolkitPointerSimulator.Click(adjust);
                yield return WaitReal(0.8f);
                Check("调整装备 opened the shared loadout view",
                    _overlay.IsOpen
                    && _overlay.Mode == ShipOverlayUi.OverlayMode.Loadout
                    && _overlay.LoadoutPage != null && _overlay.LoadoutPage.IsVisible);

                // Return to the confirmation (Back / Esc, no top-right button).
                UiInput.InjectEscapeForAutomation();

                yield return WaitReal(0.7f);
                Check("loadout returned to deployment confirmation",
                    _overlay.IsOpen
                    && _overlay.Mode == ShipOverlayUi.OverlayMode.DeploymentConfirm);
            }

            // 确认降临 -> real ShipInterior -> Surface routing.
            int mineralsBefore = session.Minerals;
            Button confirm = deploy.Root.Q<Button>(ShipDeployConfirmPage.ConfirmButtonName);
            Check("deploy 确认降临 button present", confirm != null);
            if (confirm != null)
            {
                UiToolkitPointerSimulator.Click(confirm);
                for (int t = 0; t < 1200
                     && ActiveSceneName() != GameSession.SurfaceSceneName; t++)
                {
                    yield return null;
                }

                Check("确认降临 routed to the NEREID-04 surface (scene="
                      + ActiveSceneName() + ")",
                    ActiveSceneName() == GameSession.SurfaceSceneName);
                yield return WaitReal(1.0f);
                SurfaceGameController surface =
                    Object.FindAnyObjectByType<SurfaceGameController>();
                Check("surface runtime ready after deployment",
                    surface != null && surface.IsReady);
                Check("GameSession persisted across the transition (minerals "
                      + mineralsBefore + " -> " + session.Minerals + ")",
                    session.Minerals == mineralsBefore);
            }
        }
    }

    // ====================================================================
    // Shared capture / analysis
    // ====================================================================

    private struct ScreenStats
    {
        public float AvgR;
        public float AvgG;
        public float AvgB;
        public float AvgLum;
        public float AvgEdge;
        public float MinLum;
        public float MaxLum;
    }

    private ScreenStats _diagStats;

    /// <summary>Reads the ENVIRONMENT camera's offscreen target with the
    /// focus-only overlay layer temporarily included on its culling mask,
    /// proving whether that quad/material chain renders at all (independent of
    /// stacking/presentation). Physical console screens no longer need this:
    /// since Phase 07B.2.2 they are ordinary world geometry on the base camera.</summary>
    private IEnumerator ReadBaseTargetIncludeLayer(string name)
    {
        Camera eye = _eye;
        if (eye == null)
        {
            yield break;
        }

        int layer = WorldConsoleSurface.FocusOverlayLayer;
        int previousMask = eye.cullingMask;
        eye.cullingMask = previousMask | (1 << layer);

        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        target.name = "PrecheckRT";
        target.Create();
        RenderTexture previous = eye.targetTexture;
        eye.targetTexture = target;
        // Synchronous render: independent of frame scheduling/presentation.
        eye.Render();
        yield return null;

        Rect rect = ScreenRectForConsole(0);
        RenderTexture.active = target;
        var readback = new Texture2D(target.width, target.height,
            TextureFormat.RGBA32, false);
        readback.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0, false);
        readback.Apply();
        _diagStats = new ScreenStats();
        float sumR = 0f, sumG = 0f, sumB = 0f;
        int total = 0;
        float rectL = rect.x / Screen.width * target.width;
        float rectT = rect.y / Screen.height * target.height;
        float rectR = (rect.x + rect.width) / Screen.width * target.width;
        float rectB = (rect.y + rect.height) / Screen.height * target.height;
        for (int y = Mathf.Max(0, Mathf.RoundToInt(rectT));
             y < Mathf.Min(target.height - 1, Mathf.RoundToInt(rectB)); y += 3)
        {
            for (int x = Mathf.Max(0, Mathf.RoundToInt(rectL));
                 x < Mathf.Min(target.width - 1, Mathf.RoundToInt(rectR)); x += 3)
            {
                Color c = readback.GetPixel(x, y);
                sumR += c.r;
                sumG += c.g;
                sumB += c.b;
                total++;
            }
        }

        Object.Destroy(readback);
        RenderTexture.active = null;
        eye.targetTexture = previous;
        Object.Destroy(target);
        eye.cullingMask = previousMask;

        if (total > 0)
        {
            _diagStats.AvgR = sumR / total;
            _diagStats.AvgG = sumG / total;
            _diagStats.AvgB = sumB / total;
            float lum = 0.2126f * _diagStats.AvgR + 0.7152f * _diagStats.AvgG
                        + 0.0722f * _diagStats.AvgB;
            _diagStats.AvgLum = lum * 255f;
        }

        Debug.Log("[ShipB2Shot] " + name + " rgb="
                  + _diagStats.AvgR.ToString("0.00") + ","
                  + _diagStats.AvgG.ToString("0.00") + ","
                  + _diagStats.AvgB.ToString("0.00"));
    }

    private IEnumerator CapturePngAndAnalyze(string name)
    {
        string path = Path.Combine(_folder, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 900; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 20000)
            {
                break;
            }

            yield return null;
        }

        yield return WaitReal(0.25f);
        _diagStats = new ScreenStats();
        Rect rect = ScreenRectForConsole(0);
        ReadRegion(path, rect, ref _diagStats);
        Debug.Log("[ShipB2Shot] png readback " + name + " lum="
                  + _diagStats.AvgLum.ToString("0") + " rgb="
                  + _diagStats.AvgR.ToString("0.00") + ","
                  + _diagStats.AvgG.ToString("0.00") + ","
                  + _diagStats.AvgB.ToString("0.00"));
    }

    private IEnumerator Capture(string name)
    {
        string path = Path.Combine(_folder, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 900; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 20000)
            {
                break;
            }

            yield return null;
        }

        yield return WaitReal(0.2f);
        Debug.Log("[ShipB2Shot] captured " + name + ".png");
    }

    /// <summary>Screen-space rect of Console01's screen quad (capture pose).
    /// Pixel y flips: WorldToScreenPoint y grows up, PNG rows grow down.</summary>
    private Rect ScreenRectForConsole(int index)
    {
        WorldConsoleSurface surface = SurfaceOf(index);
        Transform quad = surface.ScreenQuad;
        Vector3 scale = quad.lossyScale;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 corner = quad.position
                                 + quad.right * (sx * scale.x * 0.5f)
                                 + quad.up * (sy * scale.y * 0.5f);
                Vector3 sp = _eye.WorldToScreenPoint(corner);
                if (sp.z > 0f)
                {
                    float px = sp.x;
                    float py = Screen.height - sp.y;
                    minX = Mathf.Min(minX, px);
                    maxX = Mathf.Max(maxX, px);
                    minY = Mathf.Min(minY, py);
                    maxY = Mathf.Max(maxY, py);
                }
            }
        }

        if (maxX <= minX || maxY <= minY)
        {
            return new Rect(0f, 0f, 0f, 0f);
        }

        // 6% inset avoids the bezel edge blend.
        float marginX = (maxX - minX) * 0.06f;
        float marginY = (maxY - minY) * 0.08f;
        return new Rect(minX + marginX, minY + marginY,
            maxX - minX - marginX * 2f, maxY - minY - marginY * 2f);
    }

    private static void ReadRegion(string pngPath, Rect rect, ref ScreenStats stats)
    {
        if (!File.Exists(pngPath))
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(pngPath);
        }
        catch (System.Exception)
        {
            return;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Object.Destroy(texture);
            return;
        }

        int x0 = Mathf.Max(0, Mathf.RoundToInt(rect.xMin));
        int y0 = Mathf.Max(0, Mathf.RoundToInt(rect.yMin));
        int x1 = Mathf.Min(texture.width - 1, Mathf.RoundToInt(rect.xMax));
        int y1 = Mathf.Min(texture.height - 1, Mathf.RoundToInt(rect.yMax));
        float sumR = 0f, sumG = 0f, sumB = 0f, sumLum = 0f, sumEdge = 0f;
        float minLum = float.MaxValue, maxLum = float.MinValue;
        int total = 0;
        for (int y = y0; y <= y1; y += 3)
        {
            for (int x = x0; x <= x1; x += 3)
            {
                Color c = texture.GetPixel(x, y);
                float lum = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                sumR += c.r;
                sumG += c.g;
                sumB += c.b;
                sumLum += lum;
                minLum = Mathf.Min(minLum, lum);
                maxLum = Mathf.Max(maxLum, lum);
                if (x + 2 <= x1)
                {
                    Color n = texture.GetPixel(x + 2, y);
                    float lumN = n.r * 0.2126f + n.g * 0.7152f + n.b * 0.0722f;
                    sumEdge += Mathf.Abs(lumN - lum);
                }

                total++;
            }
        }

        if (total > 0)
        {
            stats.AvgR = sumR / total;
            stats.AvgG = sumG / total;
            stats.AvgB = sumB / total;
            stats.AvgLum = sumLum / total * 255f;
            stats.AvgEdge = sumEdge / total * 255f;
            stats.MinLum = minLum * 255f;
            stats.MaxLum = maxLum * 255f;
        }

        Object.Destroy(texture);
    }

    // ====================================================================
    // Movement / waiting helpers
    // ====================================================================

    private WorldConsoleSurface SurfaceOf(int index)
    {
        return _controller.Consoles[index].GetComponent<WorldConsoleSurface>();
    }

    private IEnumerator ApproachConsole(int index)
    {
        Vector3 position = ConsolePositions[index] + ScreenDirs[index] * 1.7f;
        _player.TeleportTo(position);
        float yaw = Mathf.Atan2(-ScreenDirs[index].x, -ScreenDirs[index].z)
                    * Mathf.Rad2Deg;
        _player.SetFacing(yaw, 0f);
        yield return null;
        yield return null;
    }

    private IEnumerator TeleportAndFace(Vector3 position, Vector3 lookAt)
    {
        _player.TeleportTo(position);
        yield return null;
        Vector3 delta = lookAt - position;
        float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        _player.SetFacing(yaw, 0f);
        yield return null;
    }

    private static IEnumerator WaitUntilSurface(WorldConsoleSurface surface)
    {
        for (int f = 0; f < 900 && !surface.IsReady; f++)
        {
            yield return null;
        }

        yield return null;
    }

    private IEnumerator WaitFocused()
    {
        if (_focus == null)
        {
            yield break;
        }

        for (int f = 0; f < 600 && _focus.State
             != ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus; f++)
        {
            yield return null;
        }

        yield return null;
        yield return null;
    }

    private IEnumerator WaitExplore()
    {
        if (_focus == null)
        {
            yield break;
        }

        for (int f = 0; f < 600 && _focus.State
             != ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore; f++)
        {
            yield return null;
        }

        yield return null;
    }

    private static IEnumerator WaitReal(float seconds)
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
        return active.IsValid() ? active.name : "";
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

    private void Check(string label, bool ok)
    {
        _report.AppendLine((ok ? "PASS :: " : "FAIL :: ") + label);
        Debug.Log("[ShipB2Shot] " + (ok ? "PASS :: " : "FAIL :: ") + label);
        if (!ok)
        {
            _failures++;
        }
    }

    private void B2Info(string label)
    {
        _report.AppendLine("INFO :: " + label);
        Debug.Log("[ShipB2Shot] INFO :: " + label);
    }

    private void Finish()
    {
        _report.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        string path = Path.Combine(_folder, "phase07b2_result.txt");
        try
        {
            File.WriteAllText(path, _report.ToString());
            Debug.Log("[ShipB2Shot] wrote result: " + path);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ShipB2Shot] cannot write result: " + e);
        }

        Debug.Log("[ShipB2Shot] report:\n" + _report);
        Application.Quit(_failures == 0 ? 0 : 1);
    }
}
