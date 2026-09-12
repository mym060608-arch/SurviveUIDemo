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

        // ---- PASS 4: wrist motion, reveal timing, frosted focus ----
        yield return Stand(0f, 0f, 0f);
        _ship.ToggleWrist();
        yield return null;
        Check("the Wrist action opens the authority immediately", _ship.IsWristOpen);
        Check("the existing terminal is not shown on the key press",
            !_ship.IsWristTerminalRevealed);
        Check("the wrist opens CRISP (the frost is a focus state, not the idle look)",
            !blockArms.IsSoftened && blockArms.FocusBlend <= 0.001f);
        Info("crisp arm material: " + blockArms.MaterialDiagnostics());

        // ---- one continuous raise: sample the timeline and shoot 20/40/60/80% ----
        var timeline = new List<Vector3>();
        var raiseFrames = new List<KeyValuePair<float, string>>
        {
            new KeyValuePair<float, string>(0.20f, "03_wrist_raise_20"),
            new KeyValuePair<float, string>(0.40f, "04_wrist_raise_40"),
            new KeyValuePair<float, string>(0.60f, "05_wrist_raise_60"),
            new KeyValuePair<float, string>(0.80f, "06_wrist_raise_80")
        };

        var shotState = new bool[raiseFrames.Count];
        var pendingShots = new List<string>();
        float raiseStart = Time.realtimeSinceStartup;
        while (blockArms.WristPresentationBlend < 0.999f
               && Time.realtimeSinceStartup - raiseStart < 4f)
        {
            timeline.Add(new Vector3(blockArms.WristPresentationBlend,
                blockArms.PositionProgress, blockArms.RotationProgress));

            for (int index = 0; index < raiseFrames.Count; index++)
            {
                if (!shotState[index]
                    && blockArms.WristPresentationBlend >= raiseFrames[index].Key)
                {
                    shotState[index] = true;
                    // Request-only capture: the file lands at the end of THIS
                    // frame, so the motion keyframes really are the requested
                    // percentages instead of "whenever the write finished".
                    pendingShots.Add(raiseFrames[index].Value);
                    ScreenCapture.CaptureScreenshot(
                        Path.Combine(_folder, raiseFrames[index].Value + ".png"));
                }
            }

            yield return null;
        }

        float raiseDuration = Time.realtimeSinceStartup - raiseStart;
        yield return SettleScreenshots(pendingShots);
        Info("raise duration observed=" + raiseDuration.ToString("0.000")
             + "s declared=" + FirstPersonBlockArmsController.RaiseDurationSeconds
             + "s frames=" + timeline.Count);
        WriteMotionReport(timeline, raiseDuration);

        // ---- 07: the physical hold pose (the arm has arrived) ----
        yield return WaitUntil(() => blockArms.WristPresentationBlend >= 0.999f, 3f);
        yield return Capture("07_wrist_hold_physical");
        Info("physical hold: blend="
             + blockArms.WristPresentationBlend.ToString("0.000")
             + " watchViewport=" + Format(blockArms.WatchViewport)
             + " focusBlend=" + blockArms.FocusBlend.ToString("0.00"));

        // ---- 08/09: the hologram starts to appear BEFORE the raise ends ----
        yield return WaitUntil(() => _ship.IsWristTerminalRevealed, 3f);
        yield return Capture("08_wrist_before_ui");
        float revealProgress = _ship.WristPresentation.LastRevealProgress;
        Check("the hologram starts appearing before the raise ends (progress "
              + revealProgress.ToString("0.00") + ")",
            revealProgress > 0.7f && revealProgress < 0.99f);
        Check("the existing wrist display is visible",
            _ship.Wrist != null && _ship.Wrist.Display != null && _ship.Wrist.Display.Visible);

        yield return WaitUntil(() => blockArms.FocusBlend >= 0.35f, 2f);
        yield return Capture("09_wrist_ui_reveal");
        Info("reveal: focusBlend=" + blockArms.FocusBlend.ToString("0.00")
             + " armAlpha=" + blockArms.PhysicalArmAlpha.ToString("0.00"));

        yield return WaitUntil(() => blockArms.FocusBlend >= 0.999f, 3f);
        yield return new WaitForSecondsRealtime(0.45f);
        Info("lens at focus: " + LensState());
        yield return CaptureAndAnalyse("10_wrist_hold_frosted",
            "Wrist hold - hologram focused, physical arm frosted",
            ProbeExpectation.WristHoldPose, analyseHologram: true);
        Check("the physical arms are frosted while the hologram is up",
            blockArms.IsSoftened && blockArms.FocusBlend >= 0.999f);
        Check("the watch screen is active while the wrist is presented",
            blockArms.IsWristScreenActive);
        Check("the frosted arm is translucent, never blacked out (alpha "
              + blockArms.PhysicalArmAlpha.ToString("0.00") + ", brightness "
              + blockArms.PhysicalArmBrightness.ToString("0.00") + ")",
            blockArms.PhysicalArmAlpha >= 0.4f && blockArms.PhysicalArmBrightness >= 0.7f);
        Color coreEmission = blockArms.WatchCoreEmission;
        Check("the cyan watch core keeps emitting (" + coreEmission.g.ToString("0.00")
              + "/" + coreEmission.b.ToString("0.00") + ")",
            coreEmission.g > 0.05f && coreEmission.b > coreEmission.r);
        Info("frosted arm material: " + blockArms.MaterialDiagnostics());
        ReportWristRenderTexture();

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

        // ---- 11 + 13: the UI-only reference and the through-map proof ----
        // Both frames are captured with the ship tick frozen, so nothing (bridge,
        // warm-up animation, state push) can differ between them: the only
        // variable is whether the physical arm is being drawn.
        yield return CoreThroughMapCaptures(blockArms);

        // ---- arm masks: crisp vs frosted at the identical pose ----
        blockArms.SetSoftened(false);
        yield return WaitUntil(() => blockArms.FocusBlend <= 0.001f, 2f);
        yield return MaskCapture("12_wrist_arm_crisp", false);
        yield return MaskCapture("12_wrist_arm_crisp_dark", true);
        Analysis unsoftened = AnalyseMask(
            Path.Combine(_folder, "12_wrist_arm_crisp.png"),
            Path.Combine(_folder, "12_wrist_arm_crisp_dark.png"));
        blockArms.SetSoftened(true);
        yield return WaitUntil(() => blockArms.FocusBlend >= 0.999f, 2f);
        yield return MaskCapture("12_wrist_arm_frosted", false);
        yield return MaskCapture("12_wrist_arm_frosted_dark", true);
        Analysis frosted = AnalyseMask(
            Path.Combine(_folder, "12_wrist_arm_frosted.png"),
            Path.Combine(_folder, "12_wrist_arm_frosted_dark.png"));

        float crispWeight = unsoftened.SolidCount * unsoftened.MeanLuminance;
        float frostedWeight = frosted.SolidCount * frosted.MeanLuminance;
        float weightRatio = crispWeight > 0f ? frostedWeight / crispWeight : 1f;
        Info("focus weight: crisp " + unsoftened.SolidCount + " px @ "
             + unsoftened.MeanLuminance.ToString("0.000", CultureInfo.InvariantCulture)
             + " = " + crispWeight.ToString("0", CultureInfo.InvariantCulture)
             + " | frosted " + frosted.SolidCount + " px @ "
             + frosted.MeanLuminance.ToString("0.000", CultureInfo.InvariantCulture)
             + " = " + frostedWeight.ToString("0", CultureInfo.InvariantCulture)
             + " | ratio " + weightRatio.ToString("0.00", CultureInfo.InvariantCulture));
        float frostingRatio = unsoftened.MeanLuminance > 0f
            ? frosted.MeanLuminance / unsoftened.MeanLuminance
            : 1f;
        Check("the frosted arm recedes (flatter, translucent) but stays visible (luminance "
              + frostingRatio.ToString("0.00", CultureInfo.InvariantCulture)
              + " of the crisp arm, still " + frosted.SolidCount + " px)",
            frostingRatio <= 0.92f && frostingRatio >= 0.35f
            && frosted.SolidCount > 2000);

        yield return BuildFocusComparison();
        CheckCyanCoreThroughMap();
        WriteFrostedReport(blockArms, weightRatio, unsoftened, frosted);

        // ---- 17: lower (terminal already released) ----
        _ship.ToggleWrist();
        yield return null;
        Check("closing releases the terminal first",
            !_ship.IsWristOpen && !_ship.IsWristTerminalRevealed);
        yield return WaitUntil(() => blockArms.WristPresentationBlend <= 0.45f, 3f);
        yield return Capture("17_wrist_lower");
        yield return WaitUntil(() => blockArms.CurrentWristPhase
            == FirstPersonBlockArmsController.WristPhase.Rest, 3f);
        Check("the arm returns to the rest presentation",
            blockArms.CurrentWristPhase == FirstPersonBlockArmsController.WristPhase.Rest);
        Check("the arm is crisp again after the wrist closes",
            !blockArms.IsSoftened && blockArms.FocusBlend <= 0.01f);
        Check("the lower is one continuous move (translation and rotation together)",
            Mathf.Abs(blockArms.PositionProgress - blockArms.RotationProgress) < 0.02f);

        if (_stage < 4)
        {
            Finish(_allPassed ? 0 : 1);
            yield break;
        }

        // ---- PASS 5: keyboard terminal + compatibility ----
        yield return TerminalStage(blockArms);
        yield return CompatibilityStage(blockArms);

        Finish(_allPassed ? 0 : 1);
    }

    /// <summary>
    /// PASS 5 - the keyboard terminal contract: the gaze prompt, entering the
    /// console view with E, leaving it with Escape (never the pause), and the
    /// gamepad pair remaining identical.
    /// </summary>
    private IEnumerator TerminalStage(FirstPersonBlockArmsController blockArms)
    {
        ShipConsoleInteractable console = _ship.Consoles[0];
        Vector3 dir = ConsoleScreenDir(0);
        _player.TeleportTo(console.transform.position + dir * 1.7f);
        _player.SetFacing(Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg, 0f);
        yield return null;
        yield return new WaitForSecondsRealtime(0.4f);

        _ship.Interactor.RefreshTarget();
        Check("the gaze resolves the terminal", _ship.Interactor.HasValidTarget);
        yield return Capture("14_terminal_prompt_keyboard");
        Info("terminal prompt: " + (_ship.Interactor.ResolvePrompt() ?? "<none>")
             + " focus=" + _ship.IsConsoleFocusActive);

        // The keyboard E is the physical Interact press; the harness cannot
        // deliver a synthetic keyboard EDGE, so the seam reports the physical
        // press and every layer below (gaze gating, focus controller, Back board)
        // runs for real. See UiInput.InjectInteractForAutomation.
        UiInput.InjectInteractForAutomation();
        yield return null;
        yield return WaitUntil(() => _ship.Focus.State
            == ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus, 4f);
        Check("keyboard E opens the console view", _ship.IsConsoleFocusActive);
        Check("opening a terminal never pauses", !_ship.PauseMenu.IsOpen);
        yield return Capture("15_terminal_open_keyboard");

        UiInput.InjectEscapeForAutomation();
        yield return null;
        yield return WaitUntil(() => !_ship.IsConsoleFocusActive, 4f);
        Check("keyboard Esc leaves the console view", !_ship.IsConsoleFocusActive);
        Check("Esc on a terminal never opens the pause",
            !_ship.PauseMenu.IsOpen && Mathf.Approximately(Time.timeScale, 1f));
        yield return WaitUntil(() => _player.InputEnabled, 3f);
        Check("movement and look come back after the Escape exit", _player.InputEnabled);
        _ship.Interactor.RefreshTarget();
        yield return Capture("16_console_exit_esc");

        // Gamepad pair stays identical (real queued device events are covered by
        // the PlayMode suite; here the same public entry points are used).
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
        Check("the gamepad confirm path still enters the console view", entered);
        _ship.Focus.RequestExit();
        yield return WaitUntil(() => !_ship.IsConsoleFocusActive, 4f);
        Check("leaving the console view never pauses", !_ship.PauseMenu.IsOpen);
        yield return new WaitForSecondsRealtime(0.4f);
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
        yield return CaptureAndAnalyse("20_console_focus_compat", "Console focus",
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
            yield return CaptureAndAnalyse("21_device_panel_compat", "Device panel",
                ProbeExpectation.Hidden);
            Check("the device panel hides the block arms", !blockArms.IsVisible);
            _ship.Overlay.CloseToExplore();
            yield return new WaitForSecondsRealtime(0.5f);
            Check("closing the device panel restores the block arms", blockArms.IsVisible);
        }

        // ---- pause ----
        _ship.PauseMenu.Open();
        yield return new WaitForSecondsRealtime(0.5f);
        yield return Capture("18_pause");
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
        yield return Capture("19_return_main_menu");
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
        string crispPath = Path.Combine(_folder, "08_wrist_before_ui.png");
        string focusedPath = Path.Combine(_folder, "10_wrist_hold_frosted.png");
        string crispMaskPath = Path.Combine(_folder, "12_wrist_arm_crisp.png");
        string softMaskPath = Path.Combine(_folder, "12_wrist_arm_frosted.png");
        string outPath = Path.Combine(_folder, "12_wrist_hold_frosted_comparison.png");

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

        Check("the focus comparison board is written (top: physical | frosted, "
              + "bottom: crisp/frosted arm)", File.Exists(outPath));
        Info("focus comparison board: " + outPath);
        yield return null;
    }

    /// <summary>
    /// Waits for the non-blocking keyframe screenshots of the raise to land.
    /// They are requested DURING the motion (so each file really is the sampled
    /// percentage) and only verified afterwards.
    /// </summary>
    private IEnumerator SettleScreenshots(List<string> names)
    {
        for (int attempt = 0; attempt < 600; attempt++)
        {
            bool allThere = true;
            foreach (string name in names)
            {
                string path = Path.Combine(_folder, name + ".png");
                if (!File.Exists(path) || new FileInfo(path).Length < 20000)
                {
                    allThere = false;
                    break;
                }
            }

            if (allThere)
            {
                break;
            }

            yield return null;
        }

        foreach (string name in names)
        {
            string path = Path.Combine(_folder, name + ".png");
            Check("the raise keyframe is written: " + name + ".png", File.Exists(path));
        }

        yield return new WaitForSecondsRealtime(0.15f);
    }

    /// <summary>
    /// wrist_motion_report.txt - the numbers that prove translation and rotation
    /// ride ONE timeline: both channels are sampled at 20/40/60/80/100% and have
    /// to agree, so no part of the move can read as "lift, then turn".
    /// </summary>
    private void WriteMotionReport(List<Vector3> timeline, float observedDuration)
    {
        var builder = new StringBuilder();
        builder.AppendLine("=== WRIST MOTION REPORT (Phase 09A final) ===");
        builder.AppendLine("raise duration      : "
                           + FirstPersonBlockArmsController.RaiseDurationSeconds.ToString("0.000",
                               CultureInfo.InvariantCulture) + " s (observed "
                           + observedDuration.ToString("0.000", CultureInfo.InvariantCulture) + " s)");
        builder.AppendLine("lower duration      : "
                           + FirstPersonBlockArmsController.LowerDurationSeconds.ToString("0.000",
                               CultureInfo.InvariantCulture) + " s");
        builder.AppendLine("ui reveal normalized: "
                           + WristPresentationBridge.UiRevealNormalizedTime.ToString("0.00",
                               CultureInfo.InvariantCulture));
        builder.AppendLine("hold settle         : "
                           + WristPresentationBridge.HoldSettleSeconds.ToString("0.00",
                               CultureInfo.InvariantCulture) + " s");
        builder.AppendLine("focus crossfade     : "
                           + FirstPersonBlockArmsController.FocusCrossfadeDuration.ToString("0.00",
                               CultureInfo.InvariantCulture) + " s");
        builder.AppendLine("position curve      : easeOutCubic on t (quadratic Bezier path)");
        builder.AppendLine("rotation curve      : easeOutCubic on t (Quaternion.Slerp, geodesic)");
        builder.AppendLine("stages              : ONE (rest -> hold); the elbow pose is a Bezier");
        builder.AppendLine("                      control point only, never an endpoint");
        builder.AppendLine();
        builder.AppendLine("samples (blend / position progress / rotation progress):");

        float[] marks = { 0.20f, 0.40f, 0.60f, 0.80f, 1.00f };
        float worstGap = 0f;
        foreach (float mark in marks)
        {
            Vector3 best = Vector3.zero;
            float bestDelta = float.MaxValue;
            foreach (Vector3 sample in timeline)
            {
                float delta = Mathf.Abs(sample.x - mark);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = sample;
                }
            }

            if (bestDelta > 0.12f && mark < 1f)
            {
                builder.AppendLine("  " + (mark * 100f).ToString("0") + "%: <not sampled>");
                continue;
            }

            float gap = Mathf.Abs(best.y - best.z);
            worstGap = Mathf.Max(worstGap, gap);
            builder.AppendLine("  " + (mark * 100f).ToString("0") + "%"
                               + "  position=" + best.y.ToString("0.000", CultureInfo.InvariantCulture)
                               + "  rotation=" + best.z.ToString("0.000", CultureInfo.InvariantCulture)
                               + "  gap=" + gap.ToString("0.000", CultureInfo.InvariantCulture));
        }

        builder.AppendLine();
        builder.AppendLine("worst |position - rotation| gap: "
                           + worstGap.ToString("0.000", CultureInfo.InvariantCulture)
                           + (worstGap < 0.02f
                               ? "  -> translation and rotation are locked together (no two-stage feel)"
                               : "  -> WARNING: the two channels drift apart"));
        builder.AppendLine("first sampled frame: blend="
                           + (timeline.Count > 0 ? timeline[0].x.ToString("0.000", CultureInfo.InvariantCulture) : "-")
                           + " position="
                           + (timeline.Count > 0 ? timeline[0].y.ToString("0.000", CultureInfo.InvariantCulture) : "-")
                           + " rotation="
                           + (timeline.Count > 0 ? timeline[0].z.ToString("0.000", CultureInfo.InvariantCulture) : "-")
                           + "  (both move from the first frame)");

        File.WriteAllText(Path.Combine(_folder, "wrist_motion_report.txt"), builder.ToString());
        Info("motion report written (worst gap "
             + worstGap.ToString("0.000", CultureInfo.InvariantCulture) + ")");
        Check("translation and rotation share one timeline (worst gap "
              + worstGap.ToString("0.000", CultureInfo.InvariantCulture) + ")",
            worstGap < 0.02f);
    }

    /// <summary>
    /// wrist_focus_report.txt - the frosted presentation contract, with the real
    /// numbers instead of adjectives. It states plainly that this is a
    /// pseudo-frosted implementation (translucency + desaturation + a soft halo),
    /// not a Gaussian blur.
    /// </summary>
    private void WriteFrostedReport(FirstPersonBlockArmsController arms, float weightRatio,
        Analysis crisp, Analysis frosted)
    {
        Material displayMaterial = _ship.Wrist != null && _ship.Wrist.Display != null
            ? _ship.Wrist.Display.SurfaceMaterial
            : null;
        var builder = new StringBuilder();
        builder.AppendLine("=== WRIST FOCUS / FROSTED REPORT (Phase 09A final) ===");
        builder.AppendLine("implementation      : pseudo-frosted (translucent + desaturated + "
                           + "milky tint + soft halo).");
        builder.AppendLine("                      NOT a Gaussian blur: the hologram is a world "
                           + "quad drawn by the");
        builder.AppendLine("                      same camera, so any post-process blur would blur "
                           + "the UI with it.");
        builder.AppendLine("ui sorting (hologram): queue "
                           + (displayMaterial != null ? displayMaterial.renderQueue.ToString() : "?")
                           + "  ("
                           + (displayMaterial != null && displayMaterial.shader != null
                               ? displayMaterial.shader.name
                               : "?") + ")");
        builder.AppendLine("physical arm sorting: queue "
                           + FirstPersonBlockArmsController.SoftenedArmRenderQueue
                           + "  -> the UI always composites over the physical watch");
        builder.AppendLine("physical alpha      : " + arms.PhysicalArmAlpha.ToString("0.00",
                           CultureInfo.InvariantCulture) + "  (1.00 = crisp, never 0)");
        builder.AppendLine("physical saturation : " + (1f - arms.PhysicalArmDesaturation)
                           .ToString("0.00", CultureInfo.InvariantCulture)
                           + "  (desaturation " + arms.PhysicalArmDesaturation.ToString("0.00",
                               CultureInfo.InvariantCulture) + ")");
        builder.AppendLine("physical brightness : " + arms.PhysicalArmBrightness.ToString("0.00",
                           CultureInfo.InvariantCulture));
        builder.AppendLine("cyan core emission  : " + arms.WatchCoreEmission.ToString("F3")
                           + "  (kept above a floor so the device stays readable through the UI)");
        builder.AppendLine("halo (soft edge)    : alpha " + arms.HaloOpacity.ToString("0.000",
                           CultureInfo.InvariantCulture) + ", scale 1.060, unfocused bleed");
        builder.AppendLine("frost crossfade     : " + FirstPersonBlockArmsController
                           .FocusCrossfadeDuration.ToString("0.00", CultureInfo.InvariantCulture)
                           + " s (continuous, no one-frame material step)");
        builder.AppendLine("world blur          : NONE - no DOF, no post-process pass was added");
        builder.AppendLine("ui sharpness        : untouched; the readable content is measured "
                           + "below");
        builder.AppendLine();
        builder.AppendLine("measured arm weight  : crisp " + crisp.SolidCount + " px @ "
                           + crisp.MeanLuminance.ToString("0.000", CultureInfo.InvariantCulture)
                           + "  ->  frosted " + frosted.SolidCount + " px @ "
                           + frosted.MeanLuminance.ToString("0.000", CultureInfo.InvariantCulture)
                           + "  (ratio " + weightRatio.ToString("0.00", CultureInfo.InvariantCulture)
                           + ")");
        builder.AppendLine("measured focus blend : " + arms.FocusBlend.ToString("0.00",
                           CultureInfo.InvariantCulture));
        builder.AppendLine("reported by          : Ship09A3ShotProbe (export/phase09a_final_shots)");

        File.WriteAllText(Path.Combine(_folder, "wrist_focus_report.txt"), builder.ToString());
        Info("frosted report written");
    }

    /// <summary>
    /// Reads the wrist hologram's RenderTexture and reports its ALPHA
    /// distribution. That number decides whether the physical watch can show
    /// through the hologram at all: a quad whose texture is opaque everywhere
    /// hides whatever is behind it, no matter how translucent the arm is.
    /// </summary>
    private void ReportWristRenderTexture()
    {
        RenderTexture texture = _ship != null && _ship.Wrist != null
            ? _ship.Wrist.CurrentRenderTexture
            : null;
        if (texture == null || !texture.IsCreated())
        {
            Info("wrist render texture: <none>");
            return;
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = texture;
        var readable = new Texture2D(texture.width, texture.height,
            TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
        readable.Apply();
        RenderTexture.active = previous;

        Color32[] pixels = readable.GetPixels32();
        int transparent = 0;
        int translucent = 0;
        int opaque = 0;
        double alphaSum = 0.0;
        foreach (Color32 pixel in pixels)
        {
            alphaSum += pixel.a / 255.0;
            if (pixel.a <= 8)
            {
                transparent++;
            }
            else if (pixel.a < 240)
            {
                translucent++;
            }
            else
            {
                opaque++;
            }
        }

        int centre = (texture.height / 2) * texture.width + texture.width / 2;
        Info("wrist render texture " + texture.width + "x" + texture.height
             + ": alpha mean=" + (alphaSum / pixels.Length).ToString("0.000",
                 CultureInfo.InvariantCulture)
             + " transparent=" + (100f * transparent / pixels.Length).ToString("0.0") + "%"
             + " translucent=" + (100f * translucent / pixels.Length).ToString("0.0") + "%"
             + " opaque=" + (100f * opaque / pixels.Length).ToString("0.0") + "%"
             + " centreAlpha=" + (pixels[centre].a / 255f).ToString("0.00",
                 CultureInfo.InvariantCulture));
        Destroy(readable);

        Material display = _ship.Wrist.Display != null
            ? _ship.Wrist.Display.SurfaceMaterial
            : null;
        if (display != null)
        {
            Info("hologram lens material: " + display.name
                 + " queue=" + display.renderQueue
                 + " mapCentre=" + (display.HasProperty("_MapCentre")
                     ? display.GetVector("_MapCentre").ToString("F3")
                     : "<none>")
                 + " mapRadius=" + (display.HasProperty("_MapRadius")
                     ? display.GetVector("_MapRadius").ToString("F3")
                     : "<none>")
                 + " frostOpacity=" + (display.HasProperty("_FrostOpacity")
                     ? display.GetFloat("_FrostOpacity").ToString("0.00")
                     : "<none>")
                 + " frostTint=" + (display.HasProperty("_FrostTint")
                     ? display.GetFloat("_FrostTint").ToString("0.00")
                     : "<none>"));
        }
        else
        {
            Info("hologram lens material: <none>");
        }
    }

    /// <summary>
    /// The frosted contract, measured: inside the hologram's map disc the physical
    /// watch must still be faintly visible THROUGH the UI (a soft colour/darkness
    /// shift, and a bluer average from the cyan core) while the UI's own readable
    /// content stays untouched (measured separately by CheckHologramReadability).
    /// Comparing the focused frame with the arm hidden frame isolates the arm's
    /// contribution behind the hologram.
    /// </summary>
    private void CheckCyanCoreThroughMap()
    {
        Texture2D withArm = LoadTexture(Path.Combine(_folder, "10_wrist_hold_frosted.png"));
        Texture2D withoutArm = LoadTexture(
            Path.Combine(_folder, "11_wrist_hold_ui_only_reference.png"));
        Texture2D armMask = LoadTexture(Path.Combine(_folder, "12_wrist_arm_frosted.png"));
        if (withArm == null || withoutArm == null || armMask == null)
        {
            Check("the core-through-map frames exist", false);
            return;
        }

        Color32[] a = withArm.GetPixels32();
        Color32[] b = withoutArm.GetPixels32();
        Color32[] mask = armMask.GetPixels32();
        int width = withArm.width;
        int height = withArm.height;
        double sumAbsDiff = 0.0;
        double sumBlueShift = 0.0;
        double sumLuminanceDiff = 0.0;
        int measured = 0;
        int changed = 0;
        int discArmPixels = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                // Region of interest: where the physical watch IS, and only where
                // the hologram sheet covers it. Measuring the whole disc would
                // dilute a small device into a 466k pixel average.
                if (Luminance(mask[index]) <= 0.05f)
                {
                    continue;
                }

                float ndcX = x / (width - 1f) * 2f - 1f;
                float ndcY = y / (height - 1f) * 2f - 1f;
                if (HologramRadiusUnits(ndcX, ndcY) > 1f)
                {
                    continue;
                }

                discArmPixels++;
                Color32 withArmPixel = a[index];
                Color32 withoutArmPixel = b[index];
                float diff = (Mathf.Abs(withArmPixel.r - withoutArmPixel.r)
                              + Mathf.Abs(withArmPixel.g - withoutArmPixel.g)
                              + Mathf.Abs(withArmPixel.b - withoutArmPixel.b)) / 3f;
                sumAbsDiff += diff;
                if (diff > 3f)
                {
                    changed++;
                }

                // Cyan core = the arm pushes blue above red where it sits.
                sumBlueShift += (withArmPixel.b - withArmPixel.r)
                                - (withoutArmPixel.b - withoutArmPixel.r);
                sumLuminanceDiff += Luminance(withArmPixel) - Luminance(withoutArmPixel);
                measured++;
            }
        }

        if (measured == 0)
        {
            // Fall back to the whole disc when the mask overlaps nothing.
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

                    int index = y * width + x;
                    float diff = (Mathf.Abs(a[index].r - b[index].r)
                                  + Mathf.Abs(a[index].g - b[index].g)
                                  + Mathf.Abs(a[index].b - b[index].b)) / 3f;
                    sumAbsDiff += diff;
                    if (diff > 3f)
                    {
                        changed++;
                    }

                    sumBlueShift += (a[index].b - a[index].r) - (b[index].b - b[index].r);
                    sumLuminanceDiff += Luminance(a[index]) - Luminance(b[index]);
                    measured++;
                }
            }
        }

        if (measured == 0)
        {
            Check("the through-map region was found", false);
            Destroy(withArm);
            Destroy(withoutArm);
            Destroy(armMask);
            return;
        }

        float meanAbsDiff = (float)(sumAbsDiff / measured);
        float meanBlueShift = (float)(sumBlueShift / measured);
        float meanLuminanceDiff = (float)(sumLuminanceDiff / measured);
        float changedFraction = changed / (float)measured;

        // The cyan core is the element that has to read through the map. It is a
        // small, bright, saturated block: measuring only it keeps the claim
        // honest instead of averaging a 2% device into the whole arm.
        double coreDiff = 0.0;
        double coreBlue = 0.0;
        int corePixels = 0;
        for (int index = 0; index < mask.Length; index++)
        {
            Color32 maskPixel = mask[index];
            if (maskPixel.b < 120 || maskPixel.b <= maskPixel.r + 40)
            {
                continue; // not a cyan core pixel
            }

            int x = index % width;
            int y = index / width;
            float ndcX = x / (width - 1f) * 2f - 1f;
            float ndcY = y / (height - 1f) * 2f - 1f;
            if (HologramRadiusUnits(ndcX, ndcY) > 1f)
            {
                continue;
            }

            corePixels++;
            coreDiff += (Mathf.Abs(a[index].r - b[index].r)
                         + Mathf.Abs(a[index].g - b[index].g)
                         + Mathf.Abs(a[index].b - b[index].b)) / 3f;
            coreBlue += (a[index].b - a[index].r) - (b[index].b - b[index].r);
        }

        Destroy(withArm);
        Destroy(withoutArm);
        Destroy(armMask);

        float coreMeanDiff = corePixels > 0 ? (float)(coreDiff / corePixels) : 0f;
        float coreBlueShift = corePixels > 0 ? (float)(coreBlue / corePixels) : 0f;
        Info("core through map: armPixelsBehindDisc=" + discArmPixels
             + " measured=" + measured
             + " mean|diff|=" + meanAbsDiff.ToString("0.00")
             + " blue shift=" + meanBlueShift.ToString("0.00")
             + " changed=" + (changedFraction * 100f).ToString("0.0") + "%"
             + " | cyan core px=" + corePixels
             + " coreDiff=" + coreMeanDiff.ToString("0.00")
             + " coreBlue=" + coreBlueShift.ToString("0.00"));

        Check("the physical watch is faintly visible THROUGH the hologram (mean |diff| "
              + meanAbsDiff.ToString("0.00") + " over "
              + (changedFraction * 100f).ToString("0.0") + "% of the covered device area)",
            meanAbsDiff >= 0.2f && changedFraction >= 0.01f);
        Check("the cyan core reads through the map (core pixels changed by "
              + coreMeanDiff.ToString("0.00") + ", blue shift "
              + coreBlueShift.ToString("0.00") + ")", corePixels > 100
                   && coreMeanDiff >= 0.7f && coreBlueShift >= 1.0f);
        Check("the physical watch never dominates the readable area (mean luminance shift "
              + meanLuminanceDiff.ToString("0.000") + ")",
            Mathf.Abs(meanLuminanceDiff) <= 0.12f);
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
        string withArms = Path.Combine(_folder, "10_wrist_hold_frosted.png");
        string withoutArms = Path.Combine(_folder, "11_wrist_hold_ui_only_reference.png");
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

    /// <summary>
    /// Captures the controlled pair that proves the frosted presentation:
    /// 13_wrist_core_through_map (hologram + frosted arm) and
    /// 11_wrist_hold_ui_only_reference (hologram, arm hidden). The ship tick is
    /// parked for both frames, so the only difference is the physical arm.
    /// </summary>
    private IEnumerator CoreThroughMapCaptures(FirstPersonBlockArmsController blockArms)
    {
        bool shipWasEnabled = _ship != null && _ship.enabled;
        if (_ship != null)
        {
            _ship.enabled = false;
        }

        yield return new WaitForSecondsRealtime(0.3f);
        Info("lens at capture: " + LensState());
        yield return Capture("13_wrist_core_through_map");

        blockArms.SetVisible(false);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return Capture("11_wrist_hold_ui_only_reference");

        blockArms.SetVisible(true);
        if (_ship != null)
        {
            _ship.enabled = shipWasEnabled;
        }

        yield return new WaitForSecondsRealtime(0.3f);
        CheckHologramReadability();
    }

    private static void Destroy(Texture2D texture)
    {
        if (texture != null)
        {
            UnityEngine.Object.Destroy(texture);
        }
    }

    /// <summary>One-line read-out of the hologram sheet's frosted lens state.</summary>
    private string LensState()
    {
        Material display = _ship != null && _ship.Wrist != null && _ship.Wrist.Display != null
            ? _ship.Wrist.Display.SurfaceMaterial
            : null;
        if (display == null)
        {
            return "<no display material>";
        }

        return "frostOpacity=" + (display.HasProperty("_FrostOpacity")
                   ? display.GetFloat("_FrostOpacity").ToString("0.00")
                   : "?")
               + " tint=" + (display.HasProperty("_FrostTint")
                   ? display.GetFloat("_FrostTint").ToString("0.00")
                   : "?")
               + " grain=" + (display.HasProperty("_FrostGrain")
                   ? display.GetFloat("_FrostGrain").ToString("0.000")
                   : "?")
               + " focusBlend=" + (_ship != null && _ship.Arms != null
                   ? _ship.Arms.FocusBlend.ToString("0.00")
                   : "?");
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
