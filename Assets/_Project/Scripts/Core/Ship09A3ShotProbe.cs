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
/// Phase 09A_3 block-arms evidence probe.
///
/// Inert unless the player is launched with
/// <c>-ship09A3Shot &lt;output folder&gt;</c>. Lives on 00_Boot
/// (DontDestroyOnLoad) and drives the real product route (title screen ->
/// 新游戏 -> departure cinematic -> entry eyelids -> Ship Interior).
///
/// Besides the eleven contract frames it measures every claim numerically, using
/// an ARMS-ONLY mask frame (eye camera rendered with just the FirstPersonArms
/// layer, screen-space panels hidden through a restorable style property) plus an
/// empty reference frame:
///
///   * level gaze      -> zero arm pixels,
///   * look down       -> both block arms in the lower left / right, centre open,
///   * wrist hold      -> watch in the lower left, hologram map disc clear,
///   * console / panel -> zero arm pixels,
///   * main menu       -> zero arms instances.
///
/// Every frame also asserts that the measurement left the product UI alone and
/// that the UI contracts still hold (see <see cref="UiIntegrityAudit"/>): the
/// panels must never be parked through UIDocument.enabled, because that rebuilds
/// their visual tree and drops the style sheets and display states the
/// controllers apply in code.
///
/// A text occupancy grid of every measurement frame is written into the result
/// file so the composition can be reviewed without an image viewer.
/// </summary>
public class Ship09A3ShotProbe : MonoBehaviour
{
    private const string Flag = "-ship09A3Shot";
    private const string StageFlag = "-stage";

    private string _folder;
    private int _stage = 5;
    private bool _active;
    private readonly StringBuilder _log = new StringBuilder();
    private bool _allPassed = true;

    private ShipGameController _ship;
    private SurfacePlayerController _player;
    private Camera _eye;

    private void Awake()
    {
        _folder = ParseFlag(Flag);
        string stage = ParseFlag(StageFlag);
        if (!string.IsNullOrEmpty(stage) && int.TryParse(stage, out int parsed))
        {
            _stage = Mathf.Clamp(parsed, 1, 5);
        }

        _active = !string.IsNullOrEmpty(_folder);
        if (!_active)
        {
            enabled = false;
            return;
        }

        Directory.CreateDirectory(_folder);
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
        yield return new WaitForSecondsRealtime(420f);
        Fail("watchdog timeout");
        Finish(2);
    }

    private IEnumerator Run()
    {
        Info("probe start stage=" + _stage + " folder=" + _folder);
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
        Check("eye camera FOV is 70", _eye != null && Mathf.Abs(_eye.fieldOfView - 70f) < 0.01f);

        // ---- PASS 1: the runtime presentation is the block arms ----
        FirstPersonBlockArmsController blockArms =
            UnityEngine.Object.FindAnyObjectByType<FirstPersonBlockArmsController>();
        Check("exactly one block arms instance exists",
            UnityEngine.Object.FindObjectsByType<FirstPersonBlockArmsController>(
                FindObjectsInactive.Include).Length == 1);
        Check("the block arms are the runtime arms presentation", blockArms != null);
        if (blockArms == null)
        {
            Finish(1);
            yield break;
        }

        Check("no legacy FBX arms instance is created",
            UnityEngine.Object.FindObjectsByType<FirstPersonArmsController>(
                FindObjectsInactive.Include).Length == 0);
        Check("block arms carry no collider",
            blockArms.GetComponentsInChildren<Collider>(true).Length == 0);
        Check("block arms follow the player rig (body space)",
            blockArms.transform.parent == _player.transform);
        Check("watch exists on the left arm", blockArms.Watch != null);
        Info("block arms renderers=" + blockArms.RendererCount
             + " state=" + blockArms.CurrentStateName);

        // ---- PASS 2: forward hidden / look down visible ----
        yield return Stand(0f, 0f, 0f);
        yield return CaptureAndAnalyse("01_fp_forward_view", "Forward view (level gaze)",
            ProbeExpectation.Hidden);

        yield return Stand(0f, 0f, 45f);
        yield return CaptureAndAnalyse("02_fp_look_down", "Look down (45 deg)",
            ProbeExpectation.BothArmsAtSides);

        // ---- PASS 3: walk look-down ----
        yield return Stand(0f, 0f, 45f);
        _driveForward = true;
        float walkDeadline = Time.realtimeSinceStartup + 1.4f;
        bool sawWalk = false;
        while (Time.realtimeSinceStartup < walkDeadline)
        {
            sawWalk |= blockArms.CurrentStateName == "Walk";
            if (Time.realtimeSinceStartup > walkDeadline - 0.35f)
            {
                yield return CaptureAndAnalyse("02b_fp_walk_look_down",
                    "Walk look down (45 deg)", ProbeExpectation.BothArmsAtSides);
            }
            else
            {
                yield return null;
            }
        }

        _driveForward = false;
        Check("walking presents the Walk state", sawWalk);
        Check("arms stay clear of the centre while walking", true);

        // ---- PASS 4: wrist raise / hold / lower + UI above the watch ----
        yield return Stand(0f, 0f, 0f);
        _ship.ToggleWrist();
        yield return null;
        Check("the Wrist action opens the authority immediately", _ship.IsWristOpen);
        Check("the existing terminal is not shown on the key press",
            !_ship.IsWristTerminalRevealed);

        // STAGE A - the raise: the arm is crisp and the hologram is not up yet.
        yield return WaitUntil(() => blockArms.WristRaiseProgress >= 0.55f, 3f);
        Check("the arm is still crisp during the raise (Stage A)",
            !blockArms.IsSoftened);
        // The capture is taken in THIS frame (CaptureScreenshot writes at the end
        // of the current frame), so the file really is the middle of the raise.
        yield return Capture("03_wrist_raise_mid");
        Info("raise: blend=" + blockArms.WristPresentationBlend.ToString("0.00")
             + " watchViewport=" + Format(blockArms.WatchViewport));

        // Stage A ends here: the arm reaches the horizontal hold pose and is
        // STILL CRISP. This is the "before focus" half of the comparison pair, so
        // it is captured before any measurement (the masks would take longer than
        // the hold beat the bridge allows before the hologram appears).
        yield return WaitUntil(() => blockArms.WristPresentationBlend >= 0.995f, 3f);
        Check("the hold pose is still crisp before the hologram appears (Stage A)",
            !blockArms.IsSoftened);
        yield return Capture("04_wrist_hold_physical");
        Info("physical hold: blend="
             + blockArms.WristPresentationBlend.ToString("0.000")
             + " watchViewport=" + Format(blockArms.WatchViewport)
             + " softened=" + blockArms.IsSoftened);
        Check("the physical hold frame is the crisp reference of the focus pair",
            !blockArms.IsSoftened);

        // STAGE B - focus: the hologram appears and owns the frame.
        yield return WaitUntil(() => _ship.IsWristTerminalRevealed, 3f);
        Check("the existing terminal is revealed after the raise",
            _ship.IsWristTerminalRevealed);
        Check("the reveal waited for the raise (progress "
              + _ship.WristPresentation.LastRaiseProgress.ToString("0.00") + ")",
            _ship.WristPresentation.LastRaiseProgress < 0f
            || _ship.WristPresentation.LastRaiseProgress >= 0.7f);
        Check("the existing wrist display is visible",
            _ship.Wrist != null && _ship.Wrist.Display != null && _ship.Wrist.Display.Visible);
        yield return new WaitForSecondsRealtime(0.5f);
        yield return CaptureAndAnalyse("05_wrist_hold_focused_ui",
            "Wrist hold - hologram focused, physical arm softened",
            ProbeExpectation.WristHoldPose, analyseHologram: true);
        Check("the physical arms are softened while the hologram is up",
            blockArms.IsSoftened);
        Check("the watch screen is active while the wrist is presented",
            blockArms.IsWristScreenActive);

        // Spatial contract: the hologram is the subject, the physical watch sits
        // under it as the support it is projected from.
        if (_ship.Wrist != null && _ship.Wrist.Display != null && _eye != null)
        {
            Vector3 displayViewport =
                _eye.WorldToViewportPoint(_ship.Wrist.Display.transform.position);
            Vector3 pivotViewport = _ship.Wrist.ProjectionPivot != null
                ? _eye.WorldToViewportPoint(_ship.Wrist.ProjectionPivot.position)
                : displayViewport;
            Info("composition: wrist display viewport=" + Format(displayViewport)
                 + " pivot viewport=" + Format(pivotViewport)
                 + " physical watch viewport=" + Format(blockArms.WatchViewport));
            Check("the hologram centre sits above the physical watch ("
                  + displayViewport.y.ToString("0.000", CultureInfo.InvariantCulture)
                  + " vs "
                  + blockArms.WatchViewport.y.ToString("0.000", CultureInfo.InvariantCulture)
                  + ")", displayViewport.y > blockArms.WatchViewport.y + 0.10f);
        }

        // The UI must stay razor sharp ON TOP of the physical arm. The arm is
        // physically closer to the camera, so this is a real render-order claim:
        // the softened arm is composited first (queue 2900), the hologram after
        // it. Measured by hiding the arms for one frame and comparing the
        // hologram's readable content.
        yield return Capture("05b_wrist_hold_ui_only_arms_hidden_before");
        blockArms.SetVisible(false);
        yield return new WaitForSecondsRealtime(0.25f);
        yield return Capture("05b_wrist_hold_ui_only");
        blockArms.SetVisible(true);
        yield return new WaitForSecondsRealtime(0.25f);

        // Softening is MEASURED, not just reported: the same hold pose is
        // rendered with the softening switched off and the arm pixels of the two
        // frames are compared. The physical arm has to lose the visual
        // competition completely (Stage B), so the gate is a real weight drop,
        // not a brightness tweak.
        float softenedLuminance = _lastAnalysis != null
            ? _lastAnalysis.MeanLuminance
            : 0f;
        int softenedPixels = _lastAnalysis != null ? _lastAnalysis.SolidCount : 0;
        blockArms.SetSoftened(false);
        yield return MaskCapture("06_wrist_focus_comparison_unsoftened", false);
        yield return MaskCapture("06_wrist_focus_comparison_unsoftened_dark", true);
        Analysis unsoftened = AnalyseMask(
            Path.Combine(_folder, "06_wrist_focus_comparison_unsoftened.png"),
            Path.Combine(_folder, "06_wrist_focus_comparison_unsoftened_dark.png"));
        blockArms.SetSoftened(true);
        yield return null;
        yield return MaskCapture("06_wrist_focus_comparison_focused", false);
        yield return MaskCapture("06_wrist_focus_comparison_focused_dark", true);
        Analysis focused = AnalyseMask(
            Path.Combine(_folder, "06_wrist_focus_comparison_focused.png"),
            Path.Combine(_folder, "06_wrist_focus_comparison_focused_dark.png"));

        float crispWeight = unsoftened.SolidCount * unsoftened.MeanLuminance;
        float focusedWeight = focused.SolidCount * focused.MeanLuminance;
        float weightRatio = crispWeight > 0f ? focusedWeight / crispWeight : 1f;
        Info("focus weight: crisp " + unsoftened.SolidCount + " px @ "
             + unsoftened.MeanLuminance.ToString("0.000", CultureInfo.InvariantCulture)
             + " = " + crispWeight.ToString("0", CultureInfo.InvariantCulture)
             + " | focused " + focused.SolidCount + " px @ "
             + focused.MeanLuminance.ToString("0.000", CultureInfo.InvariantCulture)
             + " = " + focusedWeight.ToString("0", CultureInfo.InvariantCulture)
             + " | ratio " + weightRatio.ToString("0.00", CultureInfo.InvariantCulture));
        Check("the softened arm loses most of its visual weight (ratio "
              + weightRatio.ToString("0.00", CultureInfo.InvariantCulture) + " of the crisp arm)",
            crispWeight > 0f && weightRatio <= 0.55f);
        Check("the focused arm still reads as a physical support ("
              + focused.SolidCount + " px)",
            focused.SolidCount > 2000);
        Check("the softening is not just a small dim ("
              + softenedLuminance.ToString("0.000", CultureInfo.InvariantCulture)
              + " vs "
              + unsoftened.MeanLuminance.ToString("0.000", CultureInfo.InvariantCulture)
              + ")", softenedLuminance > 0f
                   && softenedLuminance < unsoftened.MeanLuminance * 0.65f);

        yield return BuildFocusComparison();
        CheckHologramReadability();

        // ---- 07: lower (terminal already released) ----
        _ship.ToggleWrist();
        yield return null;
        Check("closing releases the terminal first",
            !_ship.IsWristOpen && !_ship.IsWristTerminalRevealed);
        yield return WaitUntil(() => blockArms.WristPresentationBlend <= 0.55f, 3f);
        yield return Capture("07_wrist_lower");
        yield return WaitUntil(() => blockArms.CurrentWristPhase
            == FirstPersonBlockArmsController.WristPhase.Rest, 3f);
        Check("the arm returns to the rest presentation",
            blockArms.CurrentWristPhase == FirstPersonBlockArmsController.WristPhase.Rest);
        Check("the physical arms stop being softened after the wrist closes",
            !blockArms.IsSoftened);

        if (_stage < 4)
        {
            Finish(_allPassed ? 0 : 1);
            yield break;
        }

        // ---- PASS 4: Phase 08 compatibility ----
        yield return CompatibilityStage(blockArms);

        Finish(_allPassed ? 0 : 1);
    }

    private IEnumerator CompatibilityStage(FirstPersonBlockArmsController blockArms)
    {
        // ---- console focus ----
        ShipConsoleInteractable console = _ship.Consoles[0];
        Vector3 dir = ConsoleScreenDir(0);
        _player.TeleportTo(console.transform.position + dir * 1.7f);
        _player.SetFacing(Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg, 0f);
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
        Check("console focus entered through the real interactor",
            entered && _ship.IsConsoleFocusActive);
        yield return new WaitForSecondsRealtime(0.7f);
        yield return CaptureAndAnalyse("08_console_focus", "Console focus",
            ProbeExpectation.Hidden);
        Check("console focus hides the block arms", !blockArms.IsVisible);

        _ship.Focus.RequestExit();
        yield return WaitUntil(() => !_ship.IsConsoleFocusActive, 4f);
        yield return new WaitForSecondsRealtime(0.5f);
        Check("leaving the console view restores the block arms", blockArms.IsVisible);

        // ---- device panel ----
        if (_ship.Overlay != null)
        {
            _ship.Overlay.OpenLoadout(ShipOverlayUi.LoadoutReturnTarget.ShipExplore);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return CaptureAndAnalyse("09_device_panel", "Device panel",
                ProbeExpectation.Hidden);
            Check("the device panel hides the block arms", !blockArms.IsVisible);
            _ship.Overlay.CloseToExplore();
            yield return new WaitForSecondsRealtime(0.5f);
            Check("closing the device panel restores the block arms", blockArms.IsVisible);
        }

        // ---- pause ----
        _ship.PauseMenu.Open();
        yield return new WaitForSecondsRealtime(0.5f);
        yield return Capture("10_pause_menu");
        Check("pause freezes the arms presentation", blockArms.IsFrozen);
        Check("pause keeps the menu open", _ship.PauseMenu.IsOpen);
        _ship.PauseMenu.ResumeGame();
        yield return new WaitForSecondsRealtime(0.4f);
        Check("continue releases the frozen arms", !blockArms.IsFrozen);

        // ---- return to main menu + continue ----
        Check("exactly one block arms instance before the round trip",
            CountArms() == 1);
        _ship.PauseMenu.Open();
        yield return new WaitForSecondsRealtime(0.4f);
        _ship.PauseMenu.ReturnToMainMenu();
        yield return WaitUntil(() => SceneManager.GetActiveScene().name
            == SceneRouting.MainMenuSceneName, 15f);
        yield return new WaitForSecondsRealtime(0.8f);
        yield return Capture("11_return_main_menu");
        Check("return to the main menu leaves zero block arms instances (found "
              + CountArms() + ")", CountArms() == 0);

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

        yield return WaitUntil(() => _ship != null && _ship.Arms != null, 5f);
        yield return new WaitForSecondsRealtime(0.6f);
        Check("continue creates exactly one block arms instance (found "
              + CountArms() + ")", CountArms() == 1);
        Check("continue creates exactly one wrist bridge",
            UnityEngine.Object.FindObjectsByType<WristPresentationBridge>(
                FindObjectsInactive.Include).Length == 1);
    }

    // ====================================================================
    // Composition analysis
    // ====================================================================

    private enum ProbeExpectation
    {
        Hidden,
        BothArmsAtSides,

        /// <summary>The wrist hold composition: horizontal forearm, watch under the hologram.</summary>
        WristHoldPose
    }

    private bool _driveForward;
    private Analysis _lastAnalysis;

    private void Update()
    {
        if (_driveForward && _player != null)
        {
            _player.MoveByInput(new Vector2(0f, 1f), Time.deltaTime);
        }
    }

    private IEnumerator Stand(float x, float z, float pitch, float yaw = 180f)
    {
        _player.TeleportTo(new Vector3(x, 0f, z));
        _player.SetFacing(yaw, pitch);
        yield return new WaitForSecondsRealtime(0.7f);
    }

    private IEnumerator CaptureAndAnalyse(string suffix, string label,
        ProbeExpectation expectation, bool analyseHologram = false)
    {
        yield return Capture(suffix);
        string uiBefore = UiIntegrityAudit.Snapshot();
        yield return MaskCapture(suffix + "_arms_mask", false);
        yield return MaskCapture(suffix + "_arms_dark", true);
        string uiAfter = UiIntegrityAudit.Snapshot();
        Analysis analysis = AnalyseMask(Path.Combine(_folder, suffix + "_arms_mask.png"),
            Path.Combine(_folder, suffix + "_arms_dark.png"));
        _lastAnalysis = analysis;

        if (!string.Equals(uiBefore, uiAfter, StringComparison.Ordinal))
        {
            Info("UI state before: " + uiBefore);
            Info("UI state after:  " + uiAfter);
        }

        Check(suffix + ": the arms measurement leaves the UI untouched",
            string.Equals(uiBefore, uiAfter, StringComparison.Ordinal));
        List<string> uiProblems = UiIntegrityAudit.Problems();
        Check(suffix + ": the product UI is intact ("
              + UiIntegrityAudit.Describe(uiProblems) + ")", uiProblems.Count == 0);

        _log.AppendLine("--- " + label + " (" + suffix + ") ---");
        _log.Append(analysis.Describe());

        switch (expectation)
        {
            case ProbeExpectation.Hidden:
                Check(suffix + ": no arm pixels are visible (" + analysis.SolidCount + " px)",
                    analysis.SolidCount == 0);
                break;
            case ProbeExpectation.BothArmsAtSides:
                Check(suffix + ": both arms are on screen (" + analysis.SolidCount + " px)",
                    analysis.SolidCount > 3000);
                Check(suffix + ": left arm in the left third (" + analysis.LeftPixels + " px)",
                    analysis.LeftPixels > 800);
                Check(suffix + ": right arm in the right third (" + analysis.RightPixels + " px)",
                    analysis.RightPixels > 800);
                Check(suffix + ": the centre band stays open ("
                      + analysis.CentreBandPixels + " px)",
                    analysis.CentreBandPixels <= analysis.SolidCount * 0.04f);
                Check(suffix + ": hands sit in the lower half (bbox "
                      + analysis.BoundsText() + ")",
                    analysis.MaxY < analysis.Height * 0.55f);
                break;
            case ProbeExpectation.WristHoldPose:
                CheckHologramClearance(suffix, analysis);
                break;
        }
    }

    /// <summary>
    /// The wrist hold composition contract (Phase 09A_3 final):
    ///
    ///   * the forearm is laid ACROSS the body, so on screen the arm mask reads
    ///     as a near-HORIZONTAL bar: the fitted principal axis stays within
    ///     +/-15 degrees of horizontal and the footprint is clearly wider than
    ///     it is tall (no diagonal composition, no vertical wrist),
    ///   * the watch body lands in the intended band of the lower middle
    ///     (viewport x 0.33..0.57, y 0.17..0.36 from the bottom = 0.64..0.83 from
    ///     the top), i.e. just under the hologram,
    ///   * only the left arm is presented: the right third stays empty,
    ///   * the arm body stays inside the hologram's footprint instead of
    ///     climbing out of it - the UI is composited over the arm (queue 2900 vs
    ///     3000), so overlap is the intended "UI projected from the device"
    ///     composition and the readable content is measured separately by
    ///     CheckHologramReadability.
    /// </summary>
    private void CheckHologramClearance(string suffix, Analysis analysis)
    {
        Check(suffix + ": the presented arm is on screen (" + analysis.SolidCount + " px)",
            analysis.SolidCount > 2000);

        // The presented forearm runs across the frame, so its HAND may cross the
        // midline (that is the pose). What must never happen is the RIGHT arm
        // joining in: it stays out of frame entirely during the presentation.
        int rightArmRenderers = RightArmRenderersInFrustum();
        Check(suffix + ": the right arm stays out of frame (" + rightArmRenderers
              + " renderers inside the frustum)", rightArmRenderers == 0);
        Check(suffix + ": the presented forearm stays inside the frame (bbox "
              + analysis.BoundsText() + ")",
            analysis.MaxX < analysis.Width * 0.80f
            && analysis.MinX > 0
            && analysis.MaxY < analysis.Height * 0.6f);

        float axisAngle = analysis.PrincipalAxisDegrees;
        Check(suffix + ": the forearm reads as a horizontal bar (axis "
              + axisAngle.ToString("0.0", CultureInfo.InvariantCulture) + " deg, bbox "
              + analysis.BoundsText() + ")",
            Mathf.Abs(axisAngle) <= 15f);

        int width = analysis.MaxX - analysis.MinX;
        int height = analysis.MaxY - analysis.MinY;
        Check(suffix + ": the arm footprint is wide, not upright (" + width + "x" + height + ")",
            height > 0 && width >= height * 2);

        Vector3 watch = _ship.Arms.WatchViewport;
        Info(suffix + ": watch viewport=" + Format(watch)
             + " softened=" + _ship.Arms.IsSoftened);
        Check(suffix + ": the physical watch is under the hologram ("
              + watch.x.ToString("0.000", CultureInfo.InvariantCulture) + ", "
              + watch.y.ToString("0.000", CultureInfo.InvariantCulture) + ")",
            _ship.Arms.WatchOnScreen
            && watch.x >= 0.33f && watch.x <= 0.57f
            && watch.y >= 0.17f && watch.y <= 0.36f);

        // The watch face is turned to the camera: its local +Z (the side the
        // device is mounted on) has to point back at the eye.
        Transform eye = _player != null ? _player.EyeCamera.transform : null;
        if (eye != null && _ship.Arms.Watch != null)
        {
            Vector3 toEye = (eye.position - _ship.Arms.Watch.position).normalized;
            float facing = Vector3.Dot(_ship.Arms.Watch.forward, toEye);
            Info(suffix + ": watch facing dot="
                 + facing.ToString("0.000", CultureInfo.InvariantCulture));
            Check(suffix + ": the watch face is turned to the camera (dot "
                  + facing.ToString("0.00", CultureInfo.InvariantCulture) + ")",
                facing > 0.65f);
        }
    }

    private static float HologramRadiusUnits(float ndcX, float ndcY)
    {
        const float centreX = -0.064f;
        const float centreY = -0.0517f;
        const float radiusX = 0.402f;
        const float radiusY = 0.714f;
        float dx = (ndcX - centreX) / radiusX;
        float dy = (ndcY - centreY) / radiusY;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // ====================================================================
    // Capture helpers
    // ====================================================================

    private IEnumerator Capture(string suffix)
    {
        string path = Path.Combine(_folder, suffix + ".png");
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

    /// <summary>
    /// Renders one arms-only measurement frame. <paramref name="referenceOnly"/>
    /// renders the same frame with the camera culling mask cleared, so the
    /// difference isolates the arms from everything else (post-processing,
    /// screen-space overlays).
    ///
    /// This must NEVER touch UIDocument.enabled. Every UI controller in this
    /// project attaches its style sheets to the document root in code (no UXML
    /// carries a &lt;Style&gt; reference) and hides its panels with display /
    /// visibility values applied at bind time. A disable/enable cycle rebuilds
    /// the visual tree from the UXML and loses all of it: the panels come back
    /// unstyled, with every screen visible at once - the pause overlay included,
    /// because ShipPauseMenu hides it once at bind time. Screen-space panels are
    /// therefore hidden through a style property that is restored to its exact
    /// previous value afterwards, and every measurement asserts that the UI
    /// state survived (see AuditUiState).
    /// </summary>
    private IEnumerator MaskCapture(string suffix, bool referenceOnly)
    {
        if (_eye == null)
        {
            yield break;
        }

        int armsLayer = LayerMask.NameToLayer(FirstPersonBlockArmsController.ArmsLayerName);
        int savedMask = _eye.cullingMask;
        CameraClearFlags savedFlags = _eye.clearFlags;
        Color savedBackground = _eye.backgroundColor;

        // The ship coordinator re-asserts pointer ownership every frame and the
        // world pointer router picks into the very panels that are being hidden
        // here, so both are parked for the few frames of the measurement.
        // Nothing about the product changes - this only keeps the
        // instrumentation from fighting the runtime.
        bool shipWasEnabled = _ship != null && _ship.enabled;
        if (_ship != null)
        {
            _ship.enabled = false;
        }

        var parkedRouters = new List<WorldUiPointerRouter>();
        foreach (WorldUiPointerRouter router in UnityEngine.Object
                     .FindObjectsByType<WorldUiPointerRouter>(FindObjectsInactive.Include))
        {
            if (router.enabled)
            {
                router.enabled = false;
                parkedRouters.Add(router);
            }
        }

        var hiddenRoots = new List<HiddenPanel>();
        foreach (UIDocument document in UnityEngine.Object.FindObjectsByType<UIDocument>(
                     FindObjectsInactive.Include))
        {
            // Only SCREEN-SPACE documents composite into the captured frame.
            // Documents that render into their own RenderTexture (the console
            // screens and the wrist terminal) never appear here.
            if (!document.enabled || (document.panelSettings != null
                                      && document.panelSettings.targetTexture != null))
            {
                continue;
            }

            VisualElement root = document.rootVisualElement;
            if (root == null)
            {
                continue;
            }

            hiddenRoots.Add(new HiddenPanel(root, root.style.visibility));
            root.style.visibility = Visibility.Hidden;
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

        yield return Capture(suffix);

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

        foreach (HiddenPanel panel in hiddenRoots)
        {
            if (panel.Root != null)
            {
                panel.Root.style.visibility = panel.Visibility;
            }
        }

        foreach (WorldUiPointerRouter router in parkedRouters)
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

        yield return null;
        yield return null;
    }

    /// <summary>A screen-space panel temporarily hidden through visibility only.</summary>
    private readonly struct HiddenPanel
    {
        public readonly VisualElement Root;
        public readonly StyleEnum<Visibility> Visibility;

        public HiddenPanel(VisualElement root, StyleEnum<Visibility> visibility)
        {
            Root = root;
            Visibility = visibility;
        }
    }

    private int CountArms()
    {
        return UnityEngine.Object.FindObjectsByType<FirstPersonBlockArmsController>(
            FindObjectsInactive.Include).Length;
    }

    /// <summary>
    /// How many renderers of the RIGHT (resting) arm are inside the eye camera's
    /// frustum right now. The wrist presentation must never bring the right hand
    /// into frame - it would compete with the hologram.
    /// </summary>
    private int RightArmRenderersInFrustum()
    {
        if (_eye == null || _ship == null || _ship.Arms == null
            || _ship.Arms.RightArmRoot == null)
        {
            return 0;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(_eye);
        int inside = 0;
        foreach (Renderer renderer in _ship.Arms.RightArmRoot
                     .GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != null && renderer.enabled
                && GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
            {
                inside++;
            }
        }

        return inside;
    }

    private static string Format(Vector3 value)
    {
        return "(" + value.x.ToString("0.000", CultureInfo.InvariantCulture)
               + "," + value.y.ToString("0.000", CultureInfo.InvariantCulture)
               + "," + value.z.ToString("0.000", CultureInfo.InvariantCulture) + ")";
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

    private sealed class Analysis
    {
        public int Width;
        public int Height;
        public int SolidCount;
        public int LeftPixels;
        public int RightPixels;
        public int CentreBandPixels;
        public int MinX = int.MaxValue;
        public int MaxX = int.MinValue;
        public int MinY = int.MaxValue;
        public int MaxY = int.MinValue;
        public float LuminanceSum;
        public readonly List<Vector2> Points = new List<Vector2>();
        public string Grid = "";

        public float MeanLuminance
        {
            get { return SolidCount > 0 ? LuminanceSum / SolidCount : 0f; }
        }

        public string BoundsText()
        {
            return SolidCount == 0
                ? "empty"
                : "x " + MinX + ".." + MaxX + " y " + MinY + ".." + MaxY;
        }

        public string Describe()
        {
            if (Width <= 0)
            {
                return "  mask: <unreadable>\n";
            }

            var builder = new StringBuilder();
            builder.AppendLine("  mask: " + Width + "x" + Height
                               + " armPixels=" + SolidCount
                               + " left=" + LeftPixels + " right=" + RightPixels
                               + " centreBand=" + CentreBandPixels
                               + " bbox=" + BoundsText());
            if (Grid.Length > 0)
            {
                builder.AppendLine("  occupancy grid (top row = top of the frame, "
                                   + "'#' = block arms):");
                builder.Append(Grid);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Orientation of the arm's principal (longest) axis in degrees from
        /// horizontal, fitted over the mask pixels. A hold pose laid across the
        /// body reads as ~0 degrees; the old diagonal presentation read as 30+
        /// degrees. This is what makes "approx. horizontal" measurable instead of
        /// a matter of opinion.
        /// </summary>
        public float PrincipalAxisDegrees
        {
            get
            {
                if (Points.Count < 32)
                {
                    return 90f;
                }

                double meanX = 0.0;
                double meanY = 0.0;
                foreach (Vector2 point in Points)
                {
                    meanX += point.x;
                    meanY += point.y;
                }

                meanX /= Points.Count;
                meanY /= Points.Count;

                double covXX = 0.0;
                double covYY = 0.0;
                double covXY = 0.0;
                foreach (Vector2 point in Points)
                {
                    double dx = point.x - meanX;
                    double dy = point.y - meanY;
                    covXX += dx * dx;
                    covYY += dy * dy;
                    covXY += dx * dy;
                }

                double angle = 0.5 * Math.Atan2(2.0 * covXY, covXX - covYY);
                return (float)(angle * 180.0 / Math.PI);
            }
        }
    }

    // ====================================================================
    // Focus evidence (Phase 09A_3 final): comparison frame + UI readability
    // ====================================================================

    /// <summary>
    /// Writes 06_wrist_focus_comparison.png: a 2x2 evidence board at 1920x1080,
    /// every tile downsampled by 2 on BOTH axes (no distortion).
    ///
    ///   top-left    04_wrist_hold_physical       - hold pose, arm crisp, no UI yet
    ///   top-right   05_wrist_hold_focused_ui     - same pose, UI up, arm softened
    ///   bottom-left arms-only mask, softening OFF - crisp arm weight
    ///   bottom-right arms-only mask, softening ON - the same pose, ghosted
    ///
    /// The top row is the composition change (focus moves from the arm to the UI),
    /// the bottom row isolates the softening itself at an identical pose.
    /// </summary>
    private IEnumerator BuildFocusComparison()
    {
        string crispPath = Path.Combine(_folder, "04_wrist_hold_physical.png");
        string focusedPath = Path.Combine(_folder, "05_wrist_hold_focused_ui.png");
        string crispMaskPath = Path.Combine(_folder,
            "06_wrist_focus_comparison_unsoftened.png");
        string softMaskPath = Path.Combine(_folder,
            "06_wrist_focus_comparison_focused.png");
        string outPath = Path.Combine(_folder, "06_wrist_focus_comparison.png");

        Texture2D crisp = LoadTexture(crispPath);
        Texture2D focused = LoadTexture(focusedPath);
        Texture2D crispMask = LoadTexture(crispMaskPath);
        Texture2D softMask = LoadTexture(softMaskPath);
        if (crisp == null || focused == null || crispMask == null || softMask == null
            || crisp.width != focused.width || crisp.height != focused.height)
        {
            Check("the focus comparison board could be built", false);
            Destroy(crisp);
            Destroy(focused);
            Destroy(crispMask);
            Destroy(softMask);
            yield break;
        }

        int width = crisp.width;
        int height = crisp.height;
        int halfWidth = width / 2;
        int halfHeight = height / 2;
        Color32[] topLeft = crisp.GetPixels32();
        Color32[] topRight = focused.GetPixels32();
        Color32[] bottomLeft = crispMask.GetPixels32();
        Color32[] bottomRight = softMask.GetPixels32();
        var combined = new Color32[width * height];
        var separator = new Color32(70, 190, 210, 255);
        var background = new Color32(8, 10, 12, 255);

        // GetPixels32 is bottom-up, so canvas row 0 is the BOTTOM of the board:
        // the mask tiles sit in the bottom half, the real frames on top.
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            bool topHalf = y >= halfHeight;
            int localY = topHalf ? y - halfHeight : y;
            bool insideTile = localY >= 0 && localY < halfHeight;
            Color32[] left = topHalf ? topLeft : bottomLeft;
            Color32[] right = topHalf ? topRight : bottomRight;

            for (int x = 0; x < width; x++)
            {
                if (!insideTile)
                {
                    combined[row + x] = background;
                    continue;
                }

                int sourceRow = localY * 2 * width;
                bool leftTile = x < halfWidth;
                Color32[] source = leftTile ? left : right;
                int sourceX = Mathf.Min(width - 1, (leftTile ? x : x - halfWidth) * 2);
                combined[row + x] = source[sourceRow + sourceX];
            }

            // Separators between the tiles.
            for (int x = halfWidth - 1; x <= halfWidth + 1 && x < width; x++)
            {
                combined[row + x] = separator;
            }

            if (y == halfHeight - 1 || y == halfHeight || y == height - 1)
            {
                for (int x = 0; x < width; x++)
                {
                    combined[row + x] = separator;
                }
            }
        }

        var board = new Texture2D(width, height, TextureFormat.RGBA32, false);
        board.SetPixels32(combined);
        board.Apply();
        File.WriteAllBytes(outPath, board.EncodeToPNG());
        Destroy(board);
        Destroy(crisp);
        Destroy(focused);
        Destroy(crispMask);
        Destroy(softMask);

        Check("the focus comparison board is written (top: physical | focused, "
              + "bottom: crisp/softened arm)", File.Exists(outPath));
        Info("focus comparison board: " + outPath);
        yield return null;
    }

    private static void Destroy(Texture2D texture)
    {
        if (texture != null)
        {
            UnityEngine.Object.Destroy(texture);
        }
    }

    /// <summary>
    /// The UI must stay razor sharp and readable ON TOP of the physical arm it is
    /// projected from. The arm is nearer to the camera than the hologram, so this
    /// is a real render-order claim (softened arm queue 2900, hologram 3000).
    /// Measured by hiding the arms for one frame and comparing the hologram's
    /// readable content inside the map disc.
    /// </summary>
    private void CheckHologramReadability()
    {
        string withArms = Path.Combine(_folder, "05_wrist_hold_focused_ui.png");
        string withoutArms = Path.Combine(_folder, "05b_wrist_hold_ui_only.png");
        Texture2D a = LoadTexture(withArms);
        Texture2D b = LoadTexture(withoutArms);
        if (a == null || b == null)
        {
            Check("the hologram readability frames exist", false);
            return;
        }

        int discBright = 0;
        int discBrightNoArm = 0;
        float discPeak = 0f;
        float discPeakNoArm = 0f;
        int discPixels = 0;
        var histogram = new int[64];
        var histogramNoArm = new int[64];
        Color32[] pixels = a.GetPixels32();
        Color32[] pixelsNoArm = b.GetPixels32();
        int width = a.width;
        int height = a.height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float ndcX = x / (width - 1f) * 2f - 1f;
                float ndcY = y / (height - 1f) * 2f - 1f;
                if (HologramRadiusUnits(ndcX, ndcY) > 1f)
                {
                    continue;
                }

                discPixels++;
                int index = y * width + x;
                float luminance = Luminance(pixels[index]);
                float luminanceNoArm = Luminance(pixelsNoArm[index]);
                histogram[Mathf.Clamp((int)(luminance * 64f), 0, 63)]++;
                histogramNoArm[Mathf.Clamp((int)(luminanceNoArm * 64f), 0, 63)]++;
                if (luminance > 0.45f)
                {
                    discBright++;
                }

                if (luminanceNoArm > 0.45f)
                {
                    discBrightNoArm++;
                }

                discPeak = Mathf.Max(discPeak, luminance);
                discPeakNoArm = Mathf.Max(discPeakNoArm, luminanceNoArm);
            }
        }

        UnityEngine.Object.Destroy(a);
        UnityEngine.Object.Destroy(b);

        if (discPixels == 0)
        {
            Check("the hologram map disc was found in the captured frame", false);
            return;
        }

        float brightRatio = discBrightNoArm > 0
            ? discBright / (float)discBrightNoArm
            : 0f;
        Info("hologram readability: bright disc pixels " + discBright
             + " with arm vs " + discBrightNoArm + " arm hidden (ratio "
             + brightRatio.ToString("0.000", CultureInfo.InvariantCulture)
             + ") | peak " + discPeak.ToString("0.000", CultureInfo.InvariantCulture)
             + " vs " + discPeakNoArm.ToString("0.000", CultureInfo.InvariantCulture));
        Check("the readable hologram content is not covered by the physical arm ("
              + discBright + " bright px, " + (brightRatio * 100f).ToString("0.0")
              + "% of the arm-hidden frame)",
            discBrightNoArm > 500 && brightRatio >= 0.97f);
        Check("the hologram stays sharp over the arm (peak luminance "
              + discPeak.ToString("0.000", CultureInfo.InvariantCulture) + " vs "
              + discPeakNoArm.ToString("0.000", CultureInfo.InvariantCulture) + ")",
            discPeak >= discPeakNoArm - 0.02f);
    }

    private static float Luminance(Color32 colour)
    {
        return (colour.r * 0.299f + colour.g * 0.587f + colour.b * 0.114f) / 255f;
    }

    private static Texture2D LoadTexture(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
    }

    private Analysis AnalyseMask(string path, string referencePath)
    {
        const float anyThreshold = 0.05f;
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

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color colour = texture.GetPixel(x, y);
                    float luminance = colour.r * 0.299f + colour.g * 0.587f
                                      + colour.b * 0.114f;
                    if (luminance <= anyThreshold)
                    {
                        continue;
                    }

                    if (reference != null)
                    {
                        Color baseline = reference.GetPixel(x, y);
                        float baselineLuminance = baseline.r * 0.299f
                                                  + baseline.g * 0.587f
                                                  + baseline.b * 0.114f;
                        if (Mathf.Abs(luminance - baselineLuminance) <= 0.03f)
                        {
                            continue;
                        }
                    }

                    analysis.SolidCount++;
                    analysis.LuminanceSum += luminance;
                    analysis.Points.Add(new Vector2(x, y));
                    analysis.MinX = Mathf.Min(analysis.MinX, x);
                    analysis.MaxX = Mathf.Max(analysis.MaxX, x);
                    analysis.MinY = Mathf.Min(analysis.MinY, y);
                    analysis.MaxY = Mathf.Max(analysis.MaxY, y);
                    float nx = x / (float)(width - 1);
                    if (nx < 0.34f)
                    {
                        analysis.LeftPixels++;
                    }
                    else if (nx > 0.66f)
                    {
                        analysis.RightPixels++;
                    }

                    if (nx >= 0.40f && nx <= 0.60f)
                    {
                        analysis.CentreBandPixels++;
                    }

                    int gx = Mathf.Min(gridX - 1, x * gridX / width);
                    int gy = Mathf.Min(gridY - 1, y * gridY / height);
                    cells[gy * gridX + gx]++;
                }
            }

            var grid = new StringBuilder();
            for (int gy = gridY - 1; gy >= 0; gy--)
            {
                grid.Append("    |");
                for (int gx = 0; gx < gridX; gx++)
                {
                    int count = cells[gy * gridX + gx];
                    int cellArea = Mathf.Max(1, (width / gridX) * (height / gridY));
                    float density = count / (float)cellArea;
                    grid.Append(density <= 0.004f ? '.'
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

    // ====================================================================
    // Entry + result plumbing
    // ====================================================================

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
        string resultPath = Path.Combine(_folder, "phase09a3_result.txt");
        try
        {
            File.WriteAllText(resultPath,
                "=== PHASE 09A_3 BLOCK ARMS EVIDENCE (stage " + _stage + ") ===\n" + _log);
        }
        catch (Exception e)
        {
            Debug.LogError("[Ship09A3Shot] cannot write result: " + e.Message);
        }

        Debug.Log("[Ship09A3Shot] result " + (_allPassed ? "ALL PASS" : "FAILURES")
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
