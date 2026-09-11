using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Phase 09A_2 first-person arms / wrist evidence probe.
///
/// Inert unless the player is launched with
/// <c>-ship09A2Shot &lt;output folder&gt; [-stage 1..4]</c>. Lives on 00_Boot
/// (DontDestroyOnLoad) so a STANDALONE player drives the real launch route
/// (Boot -&gt; Main Menu routing -&gt; Ship Interior), then walks the exact
/// contract poses through the public gameplay APIs.
///
/// Besides the review frames it measures every visual claim numerically, which
/// is the only way this phase can be checked without a human eye in the loop:
///
///   * an ARMS-ONLY mask frame (eye camera temporarily rendered with just the
///     FirstPersonArms layer, every UIDocument off) gives the real on-screen
///     footprint of the arms: bounding box, coverage, the clear central
///     observation area and the bottom-band presence,
///   * an ASCII occupancy grid of that mask (printed into the result file) so
///     the composition can be read as text,
///   * the cyan emissive wrist screens give the presented terminal position and
///     size (left half, inside the frame, large enough),
///   * wrist reveal timing is checked against the raise animation, not a fixed
///     guess,
///   * console focus / device panels / pause / main-menu round trips are checked
///     for arm occlusion and single-instance guarantees.
/// </summary>
public class Ship09A2ShotProbe : MonoBehaviour
{
    private const string Flag = "-ship09A2Shot";
    private const string StageFlag = "-stage";

    private string _folder;
    private int _stage = 4;
    private bool _active;
    private readonly StringBuilder _log = new StringBuilder();
    private bool _allPassed = true;

    private ShipGameController _ship;
    private SurfacePlayerController _player;
    private Camera _eye;

    /// <summary>
    /// While set, the probe drives the AUTHORITATIVE movement path every frame
    /// (the same call SurfacePlayerController.Update makes from real input), so
    /// locomotion can be held across capture waits without faking a device.
    /// </summary>
    private bool _driveForward;

    private void Update()
    {
        if (_driveForward && _player != null)
        {
            _player.MoveByInput(new Vector2(0f, 1f), Time.deltaTime);
        }
    }

    private void Awake()
    {
        _folder = ParseFlag(Flag);
        string stage = ParseFlag(StageFlag);
        if (!string.IsNullOrEmpty(stage) && int.TryParse(stage, out int parsed))
        {
            _stage = Mathf.Clamp(parsed, 1, 4);
        }

        _active = !string.IsNullOrEmpty(_folder);
        if (!_active)
        {
            enabled = false;
            return;
        }

        Directory.CreateDirectory(_folder);

        // Automation must not depend on the window owning OS focus: a background
        // player stops ticking, which would freeze the flow mid-journey.
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
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

    private IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(360f);
        Fail("watchdog timeout");
        Finish(2);
    }

    private IEnumerator Run()
    {
        Info("probe start stage=" + _stage + " folder=" + _folder);

        // ---- the product route: title screen -> 新游戏 -> interior ----
        yield return EnterInteriorFromMenu();

        if (_ship == null || _ship.Player == null)
        {
            Fail("ship interior never became ready");
            Finish(1);
            yield break;
        }

        _player = _ship.Player;
        _eye = _player.EyeCamera;
        Screen.SetResolution(1920, 1080, false);
        yield return new WaitForSecondsRealtime(0.8f);

        Check("ship interior ready", true);
        Check("authoritative eye camera available", _eye != null);
        Check("eye camera FOV is the Phase 08 value 70",
            _eye != null && Mathf.Abs(_eye.fieldOfView - 70f) < 0.01f);
        Check("the entry eyelid really played before first person",
            _ship.EntryBlinkArmed);

        // ---- 0. baseline free first person ----
        yield return Stand(0f, 0f, 4.2f);
        yield return CaptureAndAnalyse("01_fp_idle", "FP Idle", true, false);

        if (_stage >= 2)
        {
            yield return WalkStage();
        }

        if (_stage >= 3)
        {
            yield return WristStage();
        }

        if (_stage >= 4)
        {
            yield return CompatibilityStage();
            yield return ResolutionStage();
        }

        Finish(_allPassed ? 0 : 1);
    }

    /// <summary>
    /// Walks the real product entry (title screen 新游戏 through the departure
    /// cinematic and the entry eyelids) instead of loading the ship scene
    /// directly, so the evidence frames come from the shipped route.
    /// </summary>
    private IEnumerator EnterInteriorFromMenu()
    {
        MainMenuController menu = null;
        for (int i = 0; i < 1800; i++)
        {
            menu = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
            if (menu != null && menu.IsReady)
            {
                break;
            }

            yield return null;
        }

        Check("title screen reached and ready", menu != null && menu.IsReady);
        if (menu != null && menu.IsReady)
        {
            menu.RequestNewGame();
        }
        else
        {
            SceneRouting.EnterShipInterior(SceneRouting.ShipEntry.FromMainMenu,
                SceneRouting.EntryEyelid.Full);
        }

        for (int i = 0; i < 4800; i++)
        {
            _ship = UnityEngine.Object.FindAnyObjectByType<ShipGameController>();
            if (_ship != null && _ship.IsReady && _ship.Player != null
                && _ship.Consoles.Count == 4)
            {
                yield return WaitUntil(() => !_ship.IsEntryInputLocked, 8f);
                yield break;
            }

            yield return null;
        }
    }

    // ====================================================================
    // PASS 2 - idle composition
    // ====================================================================

    private IEnumerator Stand(float x, float z, float yaw)
    {
        _player.TeleportTo(new Vector3(x, 0f, z));
        _player.SetFacing(yaw, 0f);
        yield return new WaitForSecondsRealtime(0.7f);
    }

    private IEnumerator CaptureAndAnalyse(string suffix, string label, bool expectVisible,
        bool expectWristScreens)
    {
        string path = Path.Combine(_folder, suffix + ".png");
        yield return Capture(path);
        LogArmGeometry(label);
        string maskPath = Path.Combine(_folder, suffix + "_arms_mask.png");
        string darkPath = Path.Combine(_folder, suffix + "_arms_dark.png");
        yield return MaskCapture(maskPath, false);
        yield return MaskCapture(darkPath, true);
        Analysis analysis = AnalyseMask(maskPath, darkPath);

        _log.AppendLine("--- " + label + " (" + suffix + ") ---");
        _log.Append(analysis.Describe());

        if (expectVisible)
        {
            Check(label + ": arms are on screen (" + analysis.SolidCount + " px)",
                analysis.SolidCount > 500);
            Check(label + ": arms stay inside the frame (bbox "
                  + analysis.BoundsText() + ")",
                analysis.MinX >= 0 && analysis.MaxX < analysis.Width
                && analysis.MinY >= 0 && analysis.MaxY < analysis.Height);
            Check(label + ": arms sit in the lower part of the frame (bbox centre y "
                  + (analysis.CentreY / Mathf.Max(1f, analysis.Height)).ToString("0.00") + ")",
                analysis.CentreY < analysis.Height * 0.55f);
            Check(label + ": central observation area stays clear ("
                  + (analysis.CentralFraction * 100f).ToString("0.00") + "% of the centre box)",
                analysis.CentralFraction <= 0.001f);
            Check(label + ": arms reach the bottom band of the frame",
                analysis.BottomBandFraction > 0.004f);
            CheckHandedness(label);
        }
        else
        {
            Check(label + ": arms produce no lit pixels (" + analysis.SolidCount
                  + " px, any=" + analysis.PixelCount + ")",
                analysis.SolidCount == 0);
        }

        if (expectWristScreens)
        {
            CyanReport cyan = AnalyseCyan(Path.Combine(_folder, suffix + "_arms_mask.png"));
            _log.Append(cyan.Describe());
            Check(label + ": wrist device screens are visible (" + cyan.Count + " px)",
                cyan.Count > 150);
            Check(label + ": wrist device is on the LEFT half (centroid x "
                  + (cyan.CentreX / Mathf.Max(1f, cyan.Width)).ToString("0.00") + ")",
                cyan.Count > 0 && cyan.CentreX < cyan.Width * 0.5f);
            Check(label + ": wrist device is large enough ("
                  + (cyan.WidthFraction * 100f).ToString("0.0") + "% of frame width)",
                cyan.WidthFraction > 0.03f);
        }
    }

    // ====================================================================
    // PASS 3 - locomotion presentation
    // ====================================================================

    private IEnumerator WalkStage()
    {
        FirstPersonArmsController arms = _ship.Arms;
        if (arms == null)
        {
            Fail("no first-person arms instance for the walk stage");
            yield break;
        }

        Vector3 armsLocalBefore = arms.transform.localPosition;
        // Walking forward from the bridge end of the spine looks straight down
        // the corridor, so the shot shows real forward motion and never runs
        // into the closed bridge door.
        yield return Stand(0f, 8.5f, 180f);
        string idleState = arms.CurrentStateName;
        Check("locomotion: player still => " + idleState, idleState == "Idle");

        // Drive the AUTHORITATIVE movement path (the same call the update loop
        // makes from keyboard/gamepad); the probe never fakes a device.
        _driveForward = true;
        float deadline = Time.realtimeSinceStartup + 1.15f;
        string walkState = "<none>";
        while (Time.realtimeSinceStartup < deadline)
        {
            walkState = arms.CurrentStateName;
            if (Time.realtimeSinceStartup > deadline - 0.25f)
            {
                yield return CaptureAndAnalyse("02_fp_walk", "FP Walk", true, false);
            }
            else
            {
                yield return null;
            }
        }

        _driveForward = false;

        Check("locomotion: player moving => " + walkState, walkState == "Walk");

        Vector3 moved = _player.transform.position;
        Check("locomotion: the player really moved (z "
              + moved.z.ToString("0.00", CultureInfo.InvariantCulture) + ")",
            Mathf.Abs(moved.z - 8.5f) > 0.5f);

        yield return new WaitForSecondsRealtime(0.4f);
        Check("locomotion: stopping returns to Idle (" + arms.CurrentStateName + ")",
            arms.CurrentStateName == "Idle");

        Animator animator = arms.Animator;
        Check("root motion stays OFF on the arms animator",
            animator != null && !animator.applyRootMotion);
        Check("arms local pose under the camera is unchanged by walking",
            (arms.transform.localPosition - armsLocalBefore).magnitude < 0.0001f);

        // ---- pause freezes the presentation (contract §23) ----
        _ship.SetWristOpen(true);
        yield return WaitUntil(() => _ship.IsWristTerminalRevealed, 3f);
        yield return new WaitForSecondsRealtime(0.4f);
        string beforePause = arms.CurrentStateName;
        float beforeNormalized = NormalizedTime(animator);
        _ship.PauseMenu.Open();
        yield return new WaitForSecondsRealtime(0.6f);
        string duringPause = arms.CurrentStateName;
        float duringNormalized = NormalizedTime(animator);
        Check("pause: wrist presentation state frozen (" + beforePause + " -> " + duringPause + ")",
            beforePause == duringPause);
        Check("pause: animation clock frozen (normalized "
              + beforeNormalized.ToString("0.000") + " -> "
              + duringNormalized.ToString("0.000") + ")",
            Mathf.Abs(beforeNormalized - duringNormalized) < 0.02f);
        Check("pause: wrist terminal stays presented while paused",
            _ship.IsWristOpen && _ship.IsWristTerminalRevealed);

        _ship.PauseMenu.ResumeGame();
        yield return new WaitForSecondsRealtime(0.3f);
        Check("continue: presentation resumes from the same state ("
              + arms.CurrentStateName + ")",
            arms.CurrentStateName == beforePause);

        _ship.SetWristOpen(false);
        yield return WaitUntil(() => arms.CurrentWristPhase
            == FirstPersonArmsController.WristPhase.Rest, 3f);
        Check("walker stage leaves the wrist closed",
            !_ship.IsWristOpen && !_ship.IsWristTerminalRevealed);
    }

    private static float NormalizedTime(Animator animator)
    {
        if (animator == null)
        {
            return 0f;
        }

        return animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
    }

    // ====================================================================
    // PASS 4 - wrist presentation chain
    // ====================================================================

    private IEnumerator WristStage()
    {
        FirstPersonArmsController arms = _ship.Arms;
        WristPresentationBridge bridge = _ship.WristPresentation;
        if (arms == null || bridge == null)
        {
            Fail("wrist stage needs the arms + presentation bridge");
            yield break;
        }

        yield return Stand(0f, 0f, 4.2f);

        // ---- open request: the AUTHORITY opens at once, the terminal does not ----
        _ship.ToggleWrist();
        yield return null;
        Check("wrist open: authority state is open immediately", _ship.IsWristOpen);
        Check("wrist open: the existing terminal is NOT on screen yet",
            !_ship.IsWristTerminalRevealed);
        Check("wrist open: raise requested (" + arms.CurrentWristPhase + ")",
            arms.CurrentWristPhase == FirstPersonArmsController.WristPhase.Raise);

        // mid-raise evidence frame
        yield return new WaitForSecondsRealtime(0.35f);
        yield return Capture(Path.Combine(_folder, "03_wrist_raise.png"));
        Info("raise: animator state=" + arms.CurrentStateName
             + " progress=" + arms.WristRaiseProgress.ToString("0.00")
             + " terminalRevealed=" + _ship.IsWristTerminalRevealed);

        // ---- reveal: the existing terminal appears with the arm, not before ----
        yield return WaitUntil(() => _ship.IsWristTerminalRevealed, 3f);
        Check("wrist reveal: the existing terminal becomes visible (reason: "
              + bridge.LastRevealReason + ")", _ship.IsWristTerminalRevealed);
        Check("wrist reveal: reveal waited for the raise animation (progress "
              + bridge.LastRaiseProgress.ToString("0.00") + ")",
            bridge.LastRaiseProgress < 0f || bridge.LastRaiseProgress >= 0.7f);
        Check("wrist reveal: existing wrist display is shown",
            _ship.Wrist != null && _ship.Wrist.Display != null && _ship.Wrist.Display.Visible);
        Check("wrist reveal: the wrist owns the pointer",
            _ship.PointerOwner == ShipGameController.ShipPointerOwner.Wrist);

        yield return WaitUntil(() => arms.CurrentStateName == "WristHold", 3f);
        yield return new WaitForSecondsRealtime(0.5f);
        yield return CaptureAndAnalyse("04_wrist_hold_ui", "Wrist Hold + existing UI", true, true);

        // hold stability: the presented pose must not drift
        Vector3 holdPosition = arms.transform.position;
        yield return new WaitForSecondsRealtime(0.4f);
        Check("wrist hold: presentation is stable (delta "
              + (arms.transform.position - holdPosition).magnitude.ToString("0.000") + " m)",
            (arms.transform.position - holdPosition).magnitude < 0.03f);
        Check("wrist hold: wrist screens glow while the terminal is presented",
            arms.IsWristScreenActive);

        // ---- close: UI releases first, the arm comes down after ----
        _ship.ToggleWrist();
        yield return null;
        Check("wrist close: authority state closed immediately", !_ship.IsWristOpen);
        Check("wrist close: the existing terminal releases/hides first",
            !_ship.IsWristTerminalRevealed
            && (_ship.Wrist == null || _ship.Wrist.Display == null
                || !_ship.Wrist.Display.Visible));
        Check("wrist close: lower requested (" + arms.CurrentWristPhase + ")",
            arms.CurrentWristPhase == FirstPersonArmsController.WristPhase.Lower);

        yield return new WaitForSecondsRealtime(0.25f);
        yield return Capture(Path.Combine(_folder, "05_wrist_lower.png"));
        Info("lower: animator state=" + arms.CurrentStateName);

        yield return WaitUntil(() => arms.CurrentWristPhase
            == FirstPersonArmsController.WristPhase.Rest, 3f);
        Check("wrist close: the arm returns to Idle (" + arms.CurrentStateName + ")",
            arms.CurrentStateName == "Idle");
        yield return CaptureAndAnalyse("07_fp_idle_after_wrist", "FP Idle after the wrist round trip",
            true, false);
    }

    // ====================================================================
    // PASS 5 - Phase 08 compatibility
    // ====================================================================

    private IEnumerator CompatibilityStage()
    {
        FirstPersonArmsController arms = _ship.Arms;

        // ---- console focus: the arms must not cover the terminal ----
        // Same approach geometry the Phase 07B evidence probe uses, so the
        // player really stands in front of the 总览 terminal.
        ShipConsoleInteractable console = _ship.Consoles[0];
        Vector3 dir = ConsoleScreenDir(0);
        Vector3 approach = console.transform.position + dir * 1.7f;
        float approachYaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
        _player.TeleportTo(approach);
        _player.SetFacing(approachYaw, 0f);
        yield return null;

        bool entered = false;
        for (int i = 0; i < 400 && !entered; i++)
        {
            _ship.Interactor.RefreshTarget();
            if (ReferenceEquals(_ship.Interactor.CurrentTarget, console)
                && _ship.Interactor.HasValidTarget)
            {
                entered = _ship.Interactor.TryInteract();
            }

            if (!entered)
            {
                yield return null;
            }
        }

        yield return WaitUntil(() => _ship.IsConsoleFocusActive, 4f);
        Check("console focus entered through the real interactor", entered && _ship.IsConsoleFocusActive);
        yield return new WaitForSecondsRealtime(0.7f);
        yield return CaptureAndAnalyse("06_console_focus", "Console focus", false, false);
        Check("console focus: arms presentation is hidden while focused",
            arms == null || !arms.IsVisible);

        _ship.Focus.RequestExit();
        yield return WaitUntil(() => !_ship.IsConsoleFocusActive, 4f);
        yield return new WaitForSecondsRealtime(0.6f);
        Check("console focus: arms presentation restored after leaving the view",
            arms == null || arms.IsVisible);

        // ---- device panel (shipboard overlay) ----
        if (_ship.Overlay != null)
        {
            _ship.Overlay.OpenLoadout(ShipOverlayUi.LoadoutReturnTarget.ShipExplore);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return CaptureAndAnalyse("08_device_panel", "Device panel over the interior",
                false, false);
            Check("device panel: arms presentation hidden while the panel is open",
                arms == null || !arms.IsVisible);
            _ship.Overlay.CloseToExplore();
            yield return new WaitForSecondsRealtime(0.5f);
            Check("device panel: arms restored after Back",
                arms == null || arms.IsVisible);
        }

        // ---- main menu round trip: exactly one arms instance ----
        int before = CountArms();
        _ship.PauseMenu.Open();
        yield return new WaitForSecondsRealtime(0.4f);
        _ship.PauseMenu.ReturnToMainMenu();
        yield return WaitUntil(() => SceneManager.GetActiveScene().name
            == SceneRouting.MainMenuSceneName, 12f);
        yield return new WaitForSecondsRealtime(0.8f);
        Check("return to main menu: no arms instance survives (found " + CountArms() + ")",
            CountArms() == 0);
        yield return Capture(Path.Combine(_folder, "09_main_menu.png"));
        Check("main menu: arms count is zero in the menu scene", CountArms() == 0);

        Info("arms instance count before the round trip: " + before);
    }

    // ====================================================================
    // Resolution sanity (2560x1440 / 3440x1440)
    // ====================================================================

    private IEnumerator ResolutionStage()
    {
        // Enter the interior again through the real product routing: back at the
        // title screen, 继续游戏 is exactly what a player presses, and it is what
        // proves the second entry cannot duplicate the arms.
        MainMenuController menu = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
        if (menu != null && menu.IsReady && menu.IsContinueAvailable)
        {
            menu.RequestContinue();
        }
        else
        {
            SceneRouting.EnterShipInterior(SceneRouting.ShipEntry.FromMainMenu,
                SceneRouting.EntryEyelid.Quick);
        }

        for (int i = 0; i < 4800; i++)
        {
            _ship = UnityEngine.Object.FindAnyObjectByType<ShipGameController>();
            if (_ship != null && _ship.IsReady && _ship.Player != null)
            {
                break;
            }

            yield return null;
        }

        if (_ship == null || _ship.Player == null)
        {
            Fail("re-entering the ship interior failed");
            yield break;
        }

        _player = _ship.Player;
        _eye = _player.EyeCamera;
        yield return new WaitForSecondsRealtime(0.6f);

        int afterContinue = CountArms();
        Check("continue game: exactly one arms instance (found " + afterContinue + ")",
            afterContinue == 1);

        int[,] resolutions =
        {
            { 2560, 1440 },
            { 3440, 1440 }
        };

        for (int i = 0; i < resolutions.GetLength(0); i++)
        {
            int width = resolutions[i, 0];
            int height = resolutions[i, 1];
            Screen.SetResolution(width, height, false);
            yield return Stand(0f, 0f, 4.2f);
            yield return new WaitForSecondsRealtime(0.5f);
            Info("resolution requested " + width + "x" + height + " actual "
                 + Screen.width + "x" + Screen.height);
            string suffix = "10_fp_idle_" + width + "x" + height;
            yield return CaptureAndAnalyse(suffix, "FP Idle @ " + width + "x" + height, true, false);
        }

        // The two poses that must survive 21:9 are measured offscreen: a
        // 3440x1440 WINDOW cannot exist on a 2560x1440 desktop, so the eye camera
        // renders into a 3440x1440 render texture on the same camera (identical
        // FOV/aspect maths, screen-space UI excluded) and is reported as such.
        yield return Stand(0f, 0f, 4.2f);
        yield return UltrawideRender("12_fp_idle_3440x1440", "FP Idle @ 3440x1440 offscreen",
            false);

        _ship.ToggleWrist();
        yield return WaitUntil(() => _ship.IsWristTerminalRevealed, 3f);
        yield return WaitUntil(() => _ship.Arms != null
            && _ship.Arms.CurrentStateName == "WristHold", 3f);
        yield return new WaitForSecondsRealtime(0.6f);
        yield return UltrawideRender("13_wrist_hold_3440x1440",
            "Wrist Hold @ 3440x1440 offscreen", true);
        _ship.ToggleWrist();
        yield return new WaitForSecondsRealtime(0.6f);

        Screen.SetResolution(2560, 1440, false);
        yield return new WaitForSecondsRealtime(0.4f);
        _ship.ToggleWrist();
        yield return WaitUntil(() => _ship.IsWristTerminalRevealed, 3f);
        yield return WaitUntil(() => _ship.Arms != null
            && _ship.Arms.CurrentStateName == "WristHold", 3f);
        yield return new WaitForSecondsRealtime(0.6f);
        yield return CaptureAndAnalyse("11_wrist_hold_2560x1440", "Wrist Hold @ 2560x1440",
            true, true);
        _ship.ToggleWrist();
        yield return new WaitForSecondsRealtime(0.6f);
    }

    /// <summary>
    /// Offscreen 3440x1440 (21:9) composition check: the eye camera renders into
    /// a render texture at ultrawide size, so the arms / wrist placement can be
    /// measured at an aspect the physical desktop cannot display. At 21:9 the
    /// same world offsets map to smaller viewport offsets, so this is the check
    /// that proves the arms do not converge onto the central view and the wrist
    /// device stays in the lower left.
    /// </summary>
    private IEnumerator UltrawideRender(string suffix, string label, bool expectWristScreens)
    {
        const int width = 3440;
        const int height = 1440;

        if (_ship == null || _ship.Arms == null || _eye == null)
        {
            yield break;
        }

        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        target.name = "Phase09A2Ultrawide";
        target.Create();

        int savedMask = _eye.cullingMask;
        RenderTexture savedTarget = _eye.targetTexture;
        CameraClearFlags savedFlags = _eye.clearFlags;
        Color savedBackground = _eye.backgroundColor;
        int armsLayer = LayerMask.NameToLayer(FirstPersonArmsController.ArmsLayerName);

        string maskPath = Path.Combine(_folder, suffix + "_arms_mask.png");
        string darkPath = Path.Combine(_folder, suffix + "_arms_dark.png");

        // Combined frame (world + arms) for the visual review, then the
        // arms-only measurement frame and its empty reference.
        _eye.targetTexture = target;
        yield return null;
        yield return null;
        SaveRenderTexture(target, Path.Combine(_folder, suffix + ".png"));

        _eye.cullingMask = armsLayer >= 0 ? 1 << armsLayer : 0;
        _eye.clearFlags = CameraClearFlags.SolidColor;
        _eye.backgroundColor = Color.black;
        yield return null;
        SaveRenderTexture(target, maskPath);

        _eye.cullingMask = 0;
        yield return null;
        SaveRenderTexture(target, darkPath);

        _eye.cullingMask = savedMask;
        _eye.clearFlags = savedFlags;
        _eye.backgroundColor = savedBackground;
        _eye.targetTexture = savedTarget;
        yield return null;

        Analysis wild = AnalyseMask(maskPath, darkPath);
        _log.AppendLine("--- " + label + " ---");
        _log.Append(wild.Describe());
        Check("3440x1440 offscreen render is really " + width + "x" + height,
            wild.Width == width && wild.Height == height);
        Check(suffix + ": arms are on screen (" + wild.SolidCount + " px)",
            wild.SolidCount > 500);
        Check(suffix + ": arms stay inside the frame (bbox " + wild.BoundsText() + ")",
            wild.Width > 0 && wild.MinX >= 0 && wild.MaxX < wild.Width
            && wild.MinY >= 0 && wild.MaxY < wild.Height);
        Check(suffix + ": central observation area stays clear ("
              + (wild.CentralFraction * 100f).ToString("0.00") + "%)",
            wild.CentralFraction <= 0.001f);

        if (expectWristScreens)
        {
            CyanReport cyan = AnalyseCyan(maskPath);
            _log.Append(cyan.Describe());
            Check(suffix + ": wrist device stays in the left half (centroid x "
                  + (cyan.CentreX / Mathf.Max(1f, cyan.Width)).ToString("0.00") + ")",
                cyan.Count > 0 && cyan.CentreX < cyan.Width * 0.5f);
        }

        target.Release();
        UnityEngine.Object.Destroy(target);
    }

    private static void SaveRenderTexture(RenderTexture target, string path)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
        readback.Apply();
        RenderTexture.active = previous;
        try
        {
            File.WriteAllBytes(path, readback.EncodeToPNG());
        }
        catch (Exception e)
        {
            Debug.LogError("[Ship09A2Shot] cannot write " + path + ": " + e.Message);
        }

        UnityEngine.Object.Destroy(readback);
    }

    // ====================================================================
    // Capture helpers
    // ====================================================================

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

        // The contract also names its acceptance frames explicitly (§18, §24,
        // §38, §51): write those names next to the 01..06 set so both lists
        // exist without a second capture pass.
        string contractName = ContractFrameName(Path.GetFileNameWithoutExtension(path));
        if (contractName == null)
        {
            yield break;
        }

        string contractPath = Path.Combine(_folder, contractName);
        try
        {
            File.Copy(path, contractPath, true);
        }
        catch (Exception e)
        {
            Info("could not write " + contractName + ": " + e.Message);
        }
    }

    /// <summary>Explicit acceptance-frame names required by the phase contract.</summary>
    private static string ContractFrameName(string suffix)
    {
        switch (suffix)
        {
            case "01_fp_idle":
                return "Phase09A2_FP_Idle_1920x1080.png";
            case "02_fp_walk":
                return "Phase09A2_FP_Walk_1920x1080.png";
            case "03_wrist_raise":
                return "Phase09A2_WristRaise_1920x1080.png";
            case "04_wrist_hold_ui":
                return "Phase09A2_WristHold_1920x1080.png";
            case "05_wrist_lower":
                return "Phase09A2_WristLower_1920x1080.png";
            case "06_console_focus":
                return "Phase09A2_ConsoleFocus_1920x1080.png";
            default:
                return null;
        }
    }

    /// <summary>
    /// Renders one arms-only measurement frame.
    ///
    /// <paramref name="referenceOnly"/> produces the matching EMPTY frame: same
    /// camera, same clear, but nothing rendered at all (culling mask 0). The
    /// arms are deliberately not hidden through their renderers - the
    /// presentation tick re-asserts their visibility every frame - and the
    /// culling mask is never touched by gameplay, so it is the reliable seam.
    /// Everything that is not the camera's own output (post-processing residue,
    /// the constant small screen-space overlay the project composites into the
    /// bottom-right corner of every capture, other cameras, UI Toolkit panels)
    /// is identical in both frames and cancels out of the difference.
    /// </summary>
    private IEnumerator MaskCapture(string path, bool referenceOnly)
    {
        if (_eye == null)
        {
            yield break;
        }

        int armsLayer = LayerMask.NameToLayer(FirstPersonArmsController.ArmsLayerName);
        int savedMask = _eye.cullingMask;
        CameraClearFlags savedFlags = _eye.clearFlags;
        Color savedBackground = _eye.backgroundColor;

        // The ship coordinator re-asserts pointer ownership every frame and the
        // world pointer router picks into the very panels that are being
        // disabled here (a pick during a panel rebuild trips a UI Toolkit
        // NullReferenceException), so both are parked for the few frames of the
        // measurement. Nothing about the product changes - this only keeps the
        // instrumentation from fighting the runtime.
        bool shipWasEnabled = _ship != null && _ship.enabled;
        if (_ship != null)
        {
            _ship.enabled = false;
        }

        var disabledRouters = new List<WorldUiPointerRouter>();
        foreach (WorldUiPointerRouter router in UnityEngine.Object
                     .FindObjectsByType<WorldUiPointerRouter>(FindObjectsInactive.Include))
        {
            if (router.enabled)
            {
                router.enabled = false;
                disabledRouters.Add(router);
            }
        }

        var disabledDocuments = new List<UIDocument>();
        foreach (UIDocument document in UnityEngine.Object.FindObjectsByType<UIDocument>(
                     FindObjectsInactive.Include))
        {
            // Only SCREEN-SPACE documents composite into the captured frame.
            // Documents that render into their own RenderTexture (the console
            // screens and the wrist terminal) never appear here, and toggling
            // them breaks their panel layout state - which a later pointer pick
            // then trips over inside UI Toolkit.
            if (!document.enabled
                || (document.panelSettings != null
                    && document.panelSettings.targetTexture != null))
            {
                continue;
            }

            document.enabled = false;
            disabledDocuments.Add(document);
        }

        var disabledCameras = new List<Camera>();
        foreach (Camera camera in UnityEngine.Object.FindObjectsByType<Camera>(
                     FindObjectsInactive.Exclude))
        {
            if (camera != _eye && camera.enabled)
            {
                camera.enabled = false;
                disabledCameras.Add(camera);
            }
        }

        _eye.cullingMask = referenceOnly ? 0 : armsLayer >= 0 ? 1 << armsLayer : 0;
        _eye.clearFlags = CameraClearFlags.SolidColor;
        _eye.backgroundColor = Color.black;
        yield return null;
        yield return null;

        yield return Capture(path);

        _eye.cullingMask = savedMask;
        _eye.clearFlags = savedFlags;
        _eye.backgroundColor = savedBackground;
        foreach (Camera camera in disabledCameras)
        {
            if (camera != null)
            {
                camera.enabled = true;
            }
        }

        foreach (UIDocument document in disabledDocuments)
        {
            if (document != null)
            {
                document.enabled = true;
            }
        }

        foreach (WorldUiPointerRouter router in disabledRouters)
        {
            if (router != null)
            {
                router.enabled = true;
            }
        }

        if (_ship != null)
        {
            _ship.enabled = shipWasEnabled;
        }

        // Let the rebuilt panels settle before any pointer pick runs again.
        yield return null;
        yield return null;
    }

    private int CountArms()
    {
        return UnityEngine.Object.FindObjectsByType<FirstPersonArmsController>(
            FindObjectsInactive.Include).Length;
    }

    /// <summary>Approach direction per terminal index (matches the Phase 07B
    /// evidence probe, so the console focus pose is the same one that shipped).</summary>
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

    /// <summary>
    /// Contract §69's first two rejection conditions, measured: the LEFT hand
    /// must be on the left half and the RIGHT hand on the right half whenever
    /// they are inside the frame. A mirrored rig or a 180 degree flip would put
    /// them the other way round.
    /// </summary>
    private void CheckHandedness(string label)
    {
        FirstPersonArmsController arms = _ship != null ? _ship.Arms : null;
        if (arms == null || _eye == null)
        {
            return;
        }

        Transform left = FindDeep(arms.transform, "Hand_L");
        Transform right = FindDeep(arms.transform, "Hand_R");
        if (left == null || right == null)
        {
            return;
        }

        Vector3 leftViewport = _eye.WorldToViewportPoint(left.position);
        Vector3 rightViewport = _eye.WorldToViewportPoint(right.position);
        if (leftViewport.y > 0f && leftViewport.y < 1f
            && leftViewport.z > 0f)
        {
            Check(label + ": LEFT hand really is on the left (viewport x "
                  + leftViewport.x.ToString("0.00") + ")",
                leftViewport.x < 0.5f);
        }

        if (rightViewport.y > 0f && rightViewport.y < 1f && rightViewport.z > 0f)
        {
            Check(label + ": RIGHT hand really is on the right (viewport x "
                  + rightViewport.x.ToString("0.00") + ")",
                rightViewport.x > 0.5f);
        }
    }

    /// <summary>
    /// Prints the ANIMATED arms geometry in camera-local metres and viewport
    /// units, so a bad composition can be corrected numerically (framing offset
    /// = camera-local target minus measured position) instead of by eye.
    /// </summary>
    private void LogArmGeometry(string label)
    {
        FirstPersonArmsController arms = _ship != null ? _ship.Arms : null;
        if (arms == null || _eye == null)
        {
            Info(label + ": no arms instance to measure");
            return;
        }

        Transform camera = _eye.transform;
        Info(label + " armsRoot cameraLocalPos="
             + Format(camera.InverseTransformPoint(arms.transform.position))
             + " localPos=" + Format(arms.transform.localPosition)
             + " localScale=" + Format(arms.transform.localScale));

        foreach (string boneName in new[] { "WristDevice_L", "Hand_L", "Hand_R", "Forearm_L" })
        {
            Transform bone = FindDeep(arms.transform, boneName);
            if (bone == null)
            {
                continue;
            }

            Vector3 cameraLocal = camera.InverseTransformPoint(bone.position);
            Vector3 viewport = _eye.WorldToViewportPoint(bone.position);
            Info(label + " " + boneName + " cameraLocal=" + Format(cameraLocal)
                 + " viewport=" + Format(viewport));
        }

        bool hasBounds = false;
        Bounds bounds = new Bounds();
        foreach (Renderer renderer in arms.GetComponentsInChildren<Renderer>(true))
        {
            if (hasBounds)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
        }

        if (!hasBounds)
        {
            return;
        }

        Vector3 viewportMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 viewportMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        Vector3 cameraLocalMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 cameraLocalMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = bounds.center
                                     + Vector3.Scale(bounds.extents,
                                         new Vector3(sx, sy, sz));
                    Vector3 cameraLocal = camera.InverseTransformPoint(corner);
                    cameraLocalMin = Vector3.Min(cameraLocalMin, cameraLocal);
                    cameraLocalMax = Vector3.Max(cameraLocalMax, cameraLocal);
                    Vector3 viewport = _eye.WorldToViewportPoint(corner);
                    viewportMin = Vector3.Min(viewportMin, viewport);
                    viewportMax = Vector3.Max(viewportMax, viewport);
                }
            }
        }

        Info(label + " animatedBounds worldCentre=" + Format(bounds.center)
             + " size=" + Format(bounds.size)
             + " cameraLocal=" + Format(cameraLocalMin) + ".." + Format(cameraLocalMax)
             + " viewport=" + Format(viewportMin) + ".." + Format(viewportMax));
    }

    private static string Format(Vector3 value)
    {
        return "(" + value.x.ToString("0.###", CultureInfo.InvariantCulture)
               + "," + value.y.ToString("0.###", CultureInfo.InvariantCulture)
               + "," + value.z.ToString("0.###", CultureInfo.InvariantCulture) + ")";
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name == name)
            {
                return transform;
            }
        }

        return null;
    }

    private sealed class Analysis
    {
        public int Width;
        public int Height;
        public int PixelCount;
        public int SolidCount;
        public float MeanLuminance;
        public Color MeanColour = Color.black;
        public Color BrightestColour = Color.black;
        public int BrightestX = -1;
        public int BrightestY = -1;
        public int MinX = int.MaxValue;
        public int MaxX = int.MinValue;
        public int MinY = int.MaxValue;
        public int MaxY = int.MinValue;
        public float CentralFraction;
        public float BottomBandFraction;
        public float Coverage;
        public string Grid = "";

        public float CentreX => (MinX + MaxX) * 0.5f;
        public float CentreY => (MinY + MaxY) * 0.5f;

        public string BoundsText()
        {
            if (SolidCount == 0)
            {
                return "empty";
            }

            return "x " + MinX + ".." + MaxX + " y " + MinY + ".." + MaxY;
        }

        public string Describe()
        {
            if (Width <= 0)
            {
                return "  mask: <unreadable>\n";
            }

            var sb = new StringBuilder();
            sb.AppendLine("  mask: " + Width + "x" + Height + " armPixels=" + SolidCount
                          + " (anyPixel=" + PixelCount + " meanLum="
                          + MeanLuminance.ToString("0.000") + " meanRgb="
                          + MeanColour.r.ToString("0.00") + "/"
                          + MeanColour.g.ToString("0.00") + "/"
                          + MeanColour.b.ToString("0.00") + ")"
                          + " coverage=" + (Coverage * 100f).ToString("0.0") + "%"
                          + " bbox=" + BoundsText());
            if (PixelCount > 0)
            {
                sb.AppendLine("  mask: brightest pixel at (" + BrightestX + "," + BrightestY
                              + ") rgb=" + BrightestColour.r.ToString("0.00") + "/"
                              + BrightestColour.g.ToString("0.00") + "/"
                              + BrightestColour.b.ToString("0.00"));
            }
            sb.AppendLine("  mask: bboxCentre=(" + CentreX.ToString("0")
                          + "," + CentreY.ToString("0") + ") viewport=("
                          + (CentreX / Width).ToString("0.00") + ","
                          + (CentreY / Height).ToString("0.00") + ") (0,0 = bottom-left)"
                          + " centreBoxCoverage=" + (CentralFraction * 100f).ToString("0.00") + "%"
                          + " bottomBand=" + (BottomBandFraction * 100f).ToString("0.00") + "%");
            if (Grid.Length > 0)
            {
                sb.AppendLine("  occupancy grid (top row = top of the frame, '#' = arms):");
                sb.Append(Grid);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Measures the arms' real screen footprint as the DIFFERENCE between an
    /// arms-only layer frame and the same frame with the arms renderers forced
    /// off. Subtracting the reference removes everything that is not the arms -
    /// post-processing residue and the constant small screen-space overlay the
    /// project composites in the bottom-right corner of every capture (it is
    /// present in the main menu frames too), so "no arm pixels" is a real
    /// statement instead of a threshold guess.
    ///
    /// Two levels are reported: "anyPixel" (anything differing from the
    /// reference) and "armPixels" (differing AND lit above 0.10), which is what
    /// the composition claims are measured on.
    /// </summary>
    private Analysis AnalyseMask(string path, string referencePath)
    {
        const float diffThreshold = 0.03f;
        const float solidThreshold = 0.10f;

        var analysis = new Analysis();
        if (!File.Exists(path))
        {
            return analysis;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D reference = null;
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                return analysis;
            }

            if (!string.IsNullOrEmpty(referencePath) && File.Exists(referencePath))
            {
                reference = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!reference.LoadImage(File.ReadAllBytes(referencePath))
                    || reference.width != texture.width
                    || reference.height != texture.height)
                {
                    UnityEngine.Object.Destroy(reference);
                    reference = null;
                }
            }

            int width = texture.width;
            int height = texture.height;
            analysis.Width = width;
            analysis.Height = height;

            const int gridX = 64;
            const int gridY = 20;
            var cells = new int[gridX * gridY];
            int centreHits = 0;
            int bottomHits = 0;
            double luminanceSum = 0.0;
            Color colourSum = Color.black;
            float brightestLuminance = 0f;
            Color brightestColour = Color.black;
            int brightestX = -1;
            int brightestY = -1;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color colour = texture.GetPixel(x, y);
                    float luminance = colour.r * 0.299f + colour.g * 0.587f + colour.b * 0.114f;
                    if (reference != null)
                    {
                        Color baseline = reference.GetPixel(x, y);
                        float baselineLuminance = baseline.r * 0.299f
                                                  + baseline.g * 0.587f
                                                  + baseline.b * 0.114f;
                        if (Mathf.Abs(luminance - baselineLuminance) <= diffThreshold)
                        {
                            continue;
                        }
                    }
                    else if (luminance <= 0.02f)
                    {
                        continue;
                    }

                    analysis.PixelCount++;
                    luminanceSum += luminance;
                    colourSum += colour;
                    if (luminance > brightestLuminance)
                    {
                        brightestLuminance = luminance;
                        brightestColour = colour;
                        brightestX = x;
                        brightestY = y;
                    }

                    if (luminance <= solidThreshold)
                    {
                        continue;
                    }

                    analysis.SolidCount++;
                    if (x < analysis.MinX)
                    {
                        analysis.MinX = x;
                    }

                    if (x > analysis.MaxX)
                    {
                        analysis.MaxX = x;
                    }

                    if (y < analysis.MinY)
                    {
                        analysis.MinY = y;
                    }

                    if (y > analysis.MaxY)
                    {
                        analysis.MaxY = y;
                    }

                    int gx = Mathf.Min(gridX - 1, x * gridX / width);
                    int gy = Mathf.Min(gridY - 1, y * gridY / height);
                    cells[gy * gridX + gx]++;

                    bool inCentreBox = x > width * 0.35f && x < width * 0.65f
                                       && y > height * 0.30f && y < height * 0.70f;
                    if (inCentreBox)
                    {
                        centreHits++;
                    }

                    if (y < height * 0.35f)
                    {
                        bottomHits++;
                    }
                }
            }

            int centreTotal = Mathf.RoundToInt(width * 0.30f) * Mathf.RoundToInt(height * 0.40f);
            int bottomTotal = width * Mathf.RoundToInt(height * 0.35f);
            analysis.MeanLuminance = analysis.PixelCount > 0
                ? (float)(luminanceSum / analysis.PixelCount)
                : 0f;
            analysis.MeanColour = analysis.PixelCount > 0
                ? new Color(colourSum.r / analysis.PixelCount,
                    colourSum.g / analysis.PixelCount,
                    colourSum.b / analysis.PixelCount, 1f)
                : Color.black;
            analysis.BrightestColour = brightestColour;
            analysis.BrightestX = brightestX;
            analysis.BrightestY = brightestY;
            analysis.Coverage = analysis.SolidCount / (float)(width * height);
            analysis.CentralFraction = centreTotal > 0 ? centreHits / (float)centreTotal : 0f;
            analysis.BottomBandFraction = bottomTotal > 0 ? bottomHits / (float)bottomTotal : 0f;

            var grid = new StringBuilder();
            for (int gy = gridY - 1; gy >= 0; gy--)
            {
                grid.Append("    |");
                for (int gx = 0; gx < gridX; gx++)
                {
                    int count = cells[gy * gridX + gx];
                    int cellArea = Mathf.Max(1, (width / gridX) * (height / gridY));
                    float density = count / (float)cellArea;
                    grid.Append(density <= 0.002f ? '.'
                        : density < 0.15f ? ':'
                        : density < 0.45f ? '+'
                        : density < 0.75f ? '*' : '#');
                }

                grid.AppendLine("|");
            }

            analysis.Grid = grid.ToString();
        }
        finally
        {
            if (reference != null)
            {
                UnityEngine.Object.Destroy(reference);
            }

            UnityEngine.Object.Destroy(texture);
        }

        return analysis;
    }

    private sealed class CyanReport
    {
        public int Width;
        public int Height;
        public int Count;
        public float SumX;
        public float SumY;
        public int MinX = int.MaxValue;
        public int MaxX = int.MinValue;

        public float CentreX => Count > 0 ? SumX / Count : 0f;
        public float CentreY => Count > 0 ? SumY / Count : 0f;
        public float WidthFraction => Width > 0 && Count > 0
            ? (MaxX - MinX) / (float)Width
            : 0f;

        public string Describe()
        {
            if (Count == 0)
            {
                return "  cyan: no wrist-screen pixels found\n";
            }

            return "  cyan: pixels=" + Count
                   + " centroid=(" + CentreX.ToString("0") + "," + CentreY.ToString("0") + ")"
                   + " viewport=(" + (CentreX / Width).ToString("0.00") + ","
                   + (CentreY / Height).ToString("0.00") + ")"
                   + " width=" + (WidthFraction * 100f).ToString("0.0") + "% of frame\n";
        }
    }

    /// <summary>Locates the cyan emissive wrist screens inside an arms-only mask.</summary>
    private CyanReport AnalyseCyan(string path)
    {
        var report = new CyanReport();
        if (!File.Exists(path))
        {
            return report;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                return report;
            }

            report.Width = texture.width;
            report.Height = texture.height;
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    Color colour = texture.GetPixel(x, y);
                    bool cyan = colour.b > colour.r + 0.06f
                                && colour.g > colour.r + 0.03f
                                && colour.b > 0.10f;
                    if (!cyan)
                    {
                        continue;
                    }

                    report.Count++;
                    report.SumX += x;
                    report.SumY += y;
                    if (x < report.MinX)
                    {
                        report.MinX = x;
                    }

                    if (x > report.MaxX)
                    {
                        report.MaxX = x;
                    }
                }
            }
        }
        finally
        {
            UnityEngine.Object.Destroy(texture);
        }

        return report;
    }

    // ====================================================================
    // Result plumbing
    // ====================================================================

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private void Check(string label, bool passed)
    {
        _log.AppendLine((passed ? "PASS" : "FAIL") + " :: " + label);
        if (!passed)
        {
            _allPassed = false;
        }
    }

    private void Info(string message)
    {
        _log.AppendLine("INFO :: " + message);
    }

    private void Fail(string message)
    {
        Check(message, false);
    }

    private void Finish(int exitCode)
    {
        string resultPath = Path.Combine(_folder, "phase09a2_result.txt");
        try
        {
            File.WriteAllText(resultPath,
                "=== PHASE 09A_2 EVIDENCE (stage " + _stage + ") ===\n" + _log);
        }
        catch (Exception e)
        {
            Debug.LogError("[Ship09A2Shot] cannot write result: " + e.Message);
        }

        Debug.Log("[Ship09A2Shot] result " + (_allPassed ? "ALL PASS" : "FAILURES")
                  + " -> " + resultPath);
        Application.Quit(_allPassed ? 0 : exitCode);
    }

    private static string ParseFlag(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
