using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Title screen capture + journey probe (Phase 08 Revision 2, validation
/// instrumentation only).
///
/// Inert during normal play: it only activates when the player is launched with
/// <c>-menuCaptureDir &lt;absolute path&gt;</c>. It then drives the real product
/// flow through REAL UI clicks and captures the composited frame (3D stage + UI)
/// at 1920x1080:
///
///   01  Title screen idle (活下去 + four options, ship on the left)
///   01b The same shot later (proves the cruise, the star drift and the twinkle)
///   02  选项 open
///   03a Cinematic: menu fade
///   03b Cinematic: engine boost / ship departure (LEFT to RIGHT)
///   03c Cinematic: blackout
///   04a Eyelids: beat 1 (small crack)
///   04b Eyelids: beat 2 (bigger)
///   04c Eyelids: beat 3 (fully open)
///   04d Ship interior: first person, control released
///   05a Ship terminal approach
///   05b Ship terminal: the Overview screen
///
/// It also asserts the Revision 2 contracts (minimal copy, four options,
/// continue availability, left-to-right flight, three eyelid beats, first-person
/// landing, terminal reachable) and writes
/// <c>main_menu_capture_report.txt</c> next to the images.
/// </summary>
public class MainMenuCapture : MonoBehaviour
{
    private const string CaptureDirFlag = "-menuCaptureDir";
    private const int MaxFrames = 1200;

    private string _captureDir;
    private bool _active;
    private readonly StringBuilder _report = new StringBuilder();
    private int _failures;

    private void Awake()
    {
        _captureDir = ParseFlag(CaptureDirFlag);
        _active = !string.IsNullOrEmpty(_captureDir);
        if (!_active)
        {
            enabled = false;
            return;
        }

        DontDestroyOnLoad(gameObject);

        // Automation must not depend on the window having OS focus: a background
        // player stops ticking, which would freeze the cinematic mid-shot.
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
    }

    private void Start()
    {
        if (!_active)
        {
            return;
        }

        StartCoroutine(RunCapture());
        StartCoroutine(Watchdog());
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

    private IEnumerator Watchdog()
    {
        yield return new WaitForSecondsRealtime(420f);
        Debug.LogError("[MainMenuCapture] watchdog timeout - forcing quit");
        Application.Quit(2);
    }

    private IEnumerator RunCapture()
    {
        Directory.CreateDirectory(_captureDir);
        _report.AppendLine("SURVIVE // DRIFT - Phase 08 Revision 2 title screen capture");
        _report.AppendLine("scene at start: " + SceneManager.GetActiveScene().name);

        Screen.SetResolution(1920, 1080, false);
        int settle = 0;
        while ((Screen.width != 1920 || Screen.height != 1080) && settle < 240)
        {
            settle++;
            yield return null;
        }

        _report.AppendLine("resolution: " + Screen.width + "x" + Screen.height);
        Check("capture resolution is 1920x1080", Screen.width == 1920 && Screen.height == 1080);

        MainMenuController menu = null;
        yield return WaitUntil(() =>
        {
            menu = Object.FindAnyObjectByType<MainMenuController>();
            return menu != null && menu.IsReady;
        });

        if (menu == null)
        {
            Fail("title screen never became ready");
            Finish(1);
            yield break;
        }

        yield return new WaitForSecondsRealtime(1.2f);

        // ---- Composition contract ----
        MainMenuShipHero hero = Object.FindAnyObjectByType<MainMenuShipHero>();
        MainMenuStarfield stars = Object.FindAnyObjectByType<MainMenuStarfield>();
        Camera stageCamera = menu.StageCamera;
        MainMenuCinematic cinematic = menu.Cinematic;

        // ---- Minimal copy ----
        Label title = menu.UiRoot.Q<Label>(MainMenuNames.Title);
        Check("the only headline is 活下去",
            title != null && title.text == "活下去");
        Check("exactly four options exist", menu.MenuItems.Count == 4);
        string[] expected = { "新游戏", "继续游戏", "选项", "退出" };
        for (int i = 0; i < expected.Length && i < menu.MenuItems.Count; i++)
        {
            Label label = menu.MenuItems[i].Q<Label>(className: "menu-item-label");
            Check("option " + (i + 1) + " reads " + expected[i],
                label != null && label.text == expected[i]);
        }

        var copy = new StringBuilder();
        menu.UiRoot.Query<Label>().ForEach(label => copy.Append(label.text).Append('\n'));
        string screen = copy.ToString();
        bool clean = true;
        foreach (string removed in MainMenuNames.RemovedCopy)
        {
            if (screen.Contains(removed))
            {
                clean = false;
                _report.AppendLine("  still visible: " + removed);
            }
        }

        Check("no removed HUD / telemetry copy is visible", clean);
        Check("no 制作信息 button", menu.UiRoot.Q<Button>("CreditsButton") == null);
        Check("no top system bar", menu.UiRoot.Q<VisualElement>("MenuTopBar") == null);
        Check("no footer bar", menu.UiRoot.Q<VisualElement>("MenuFooter") == null);
        Check("no telemetry strip", menu.UiRoot.Q<VisualElement>("HeroTelemetry") == null);

        // ---- Hero + sky ----
        Check("hero ship present (" + (hero != null ? hero.ShipRendererCount : 0) + " renderers)",
            hero != null && hero.IsShipPresent && hero.ShipRendererCount >= 40);
        Check("two thruster plumes built from the asset anchors",
            hero != null && hero.NozzleCount == 2);
        Check("deep-space starfield built (" + (stars != null ? stars.StarCount : 0) + " stars)",
            stars != null && stars.IsActive && stars.LayerCount == 3 && stars.StarCount > 1000);
        Check("star layers drift sideways in the shader (parallax)",
            stars != null && stars.ScrollSpeedOf(2) > stars.ScrollSpeedOf(0));
        Check("star layers twinkle", stars != null && stars.TwinkleAmountOf(1) > 0.2f);
        Check("cinematic authored and idle", cinematic != null && !cinematic.IsPlaying);

        // ---- Direction: LEFT to RIGHT ----
        if (stageCamera != null && hero != null)
        {
            stageCamera.aspect = 16f / 9f;
            float shipX = stageCamera.WorldToViewportPoint(hero.WorldBounds.center).x;
            Check("ship centre is in the left half (x=" + shipX.ToString("F3") + ")", shipX < 0.5f);

            float hereX = stageCamera.WorldToViewportPoint(hero.WorldBounds.center).x;
            float aheadX = stageCamera.WorldToViewportPoint(
                hero.WorldBounds.center + hero.ForwardWorld * 8f).x;
            Check("ship nose points to the RIGHT of the screen ("
                  + hereX.ToString("F3") + " -> " + aheadX.ToString("F3") + ")",
                aheadX > hereX);

            float plumeX = stageCamera.WorldToViewportPoint(
                hero.WorldBounds.center
                + hero.transform.TransformDirection(hero.AftDirection) * 8f).x;
            Check("plumes stream to the LEFT, opposite the flight ("
                  + plumeX.ToString("F3") + " < " + hereX.ToString("F3") + ")",
                plumeX < hereX);
            stageCamera.ResetAspect();
        }

        // ---- 01 / 01b: idle, then idle later ----
        yield return Capture("01_title_screen_idle");

        Vector3 positionA = hero != null ? hero.transform.position : Vector3.zero;
        float engineA = hero != null && hero.Flames.Count > 0 ? hero.Flames[0].PlumeLength : 0f;
        yield return new WaitForSecondsRealtime(1.6f);
        yield return Capture("01b_title_screen_idle_later");

        if (hero != null && hero.Flames.Count > 0)
        {
            Vector3 moved = hero.transform.position - positionA;
            float engineB = hero.Flames[0].PlumeLength;
            _report.AppendLine("idle motion: moved " + moved.magnitude.ToString("F4")
                               + " m, plume " + engineA.ToString("F2") + " -> "
                               + engineB.ToString("F2") + " m");
            Check("the title screen is alive (hero moved or plume flickered)",
                moved.magnitude > 0.0005f || Mathf.Abs(engineA - engineB) > 0.0005f);
        }

        // ---- 02: 选项 ----
        VisualElement optionsButton = menu.UiRoot.Q<Button>(MainMenuNames.OptionsButton);
        if (optionsButton != null)
        {
            UiToolkitPointerSimulator.Click(optionsButton);
            yield return WaitUntil(() => menu.IsOptionsOpen, 120);
            Check("选项 opens from a real click", menu.IsOptionsOpen);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Capture("02_options");

            menu.HandleBackInput();
            yield return WaitUntil(() => !menu.IsOptionsOpen, 120);
            Check("选项 closes again", !menu.IsOptionsOpen);
        }

        // ---- 03: 新游戏 -> cinematic ----
        yield return new WaitForSecondsRealtime(0.5f);
        GameSession session = GameSession.Instance;
        Check("新游戏 resets the authoritative session",
            session != null && !session.HasStartedRun);
        Check("继续游戏 is disabled before the first run",
            menu.ContinueButton != null && !menu.ContinueButton.enabledSelf);

        menu.SetFocus(0);
        Check("A confirms 新游戏", menu.ConfirmFocused());
        yield return null;
        Check("the cinematic started", cinematic != null && cinematic.IsPlaying);
        Check("新游戏 started a run (继续游戏 becomes available)",
            session != null && session.HasStartedRun);

        bool sawFade = false;
        bool sawBoost = false;
        bool sawDeparture = false;
        bool sawBlack = false;
        bool capturedFade = false;
        bool capturedDeparture = false;
        bool capturedBlack = false;
        float maxBlack = 0f;
        float departureStartX = -1f;
        float departureEndX = -1f;
        int frames = 0;

        while (frames < MaxFrames
               && SceneManager.GetActiveScene().name == SceneRouting.MainMenuSceneName)
        {
            frames++;
            if (cinematic != null)
            {
                maxBlack = Mathf.Max(maxBlack, cinematic.BlackAlpha);

                if (!sawFade && cinematic.MenuAlpha < 0.35f)
                {
                    sawFade = true;
                    if (!capturedFade)
                    {
                        capturedFade = true;
                        yield return Capture("03a_cinematic_menu_fade");
                    }
                }

                if (!sawBoost && hero != null && hero.EngineTarget > 0.6f)
                {
                    sawBoost = true;
                    _report.AppendLine("engine boost at t="
                                       + cinematic.ElapsedSeconds.ToString("F2") + "s (plume "
                                       + (hero.Flames.Count > 0
                                           ? hero.Flames[0].PlumeLength.ToString("F2")
                                           : "-") + " m)");
                }

                if (hero != null && hero.IsDeparting && stageCamera != null)
                {
                    stageCamera.aspect = 16f / 9f;
                    float x = stageCamera.WorldToViewportPoint(hero.WorldBounds.center).x;
                    stageCamera.ResetAspect();
                    if (departureStartX < 0f)
                    {
                        departureStartX = x;
                    }

                    departureEndX = x;

                    if (!sawDeparture && hero.DepartureProgress > 0.35f)
                    {
                        sawDeparture = true;
                        _report.AppendLine("departure at t="
                                           + cinematic.ElapsedSeconds.ToString("F2") + "s: screen x "
                                           + departureStartX.ToString("F3") + " -> "
                                           + departureEndX.ToString("F3"));
                        if (!capturedDeparture)
                        {
                            capturedDeparture = true;
                            yield return Capture("03b_cinematic_ship_departure");
                        }
                    }
                }

                if (!sawBlack && cinematic.BlackAlpha > 0.85f)
                {
                    sawBlack = true;
                    if (!capturedBlack)
                    {
                        capturedBlack = true;
                        yield return Capture("03c_cinematic_blackout");
                    }
                }
            }

            yield return null;
        }

        Check("the menu faded out", sawFade);
        Check("the thrusters boosted", sawBoost);
        Check("the ship departed", sawDeparture);
        Check("the shot reached the blackout (max alpha " + maxBlack.ToString("F2") + ")",
            sawBlack || maxBlack > 0.85f);
        Check("the ship departed LEFT to RIGHT on screen ("
              + departureStartX.ToString("F3") + " -> " + departureEndX.ToString("F3") + ")",
            departureStartX >= 0f && departureEndX > departureStartX);

        // ---- 04: ship interior + the three eyelid beats ----
        ShipGameController ship = null;
        yield return WaitUntil(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady;
        });

        if (ship == null)
        {
            Fail("新游戏 never reached the ship interior");
            Finish(1);
            yield break;
        }

        Check("landing scene is the ship interior",
            SceneManager.GetActiveScene().name == GameSession.ShipInteriorSceneName);
        Check("the eyelids were armed", ship.EntryBlinkArmed);
        Check("control is locked while the eyes are shut", ship.IsEntryInputLocked);

        ShipEntryBlinkFade blink = ship.EntryBlink;
        Check("the eyelid component exposes both curtains",
            blink != null && blink.TopEyelid != null && blink.BottomEyelid != null);

        string beat1 = null;
        string beat2 = null;
        string beat3 = null;
        int guard = 0;
        while (ship.EntryBlink != null && guard < MaxFrames)
        {
            guard++;
            ShipEntryBlinkFade current = ship.EntryBlink;
            if (current != null)
            {
                // Capture each beat near its PEAK (the eyes are actually open a
                // crack at that moment), not on its first frame.
                if (beat1 == null && current.StageLabel == "beat1"
                    && current.Openness > 0.12f)
                {
                    beat1 = current.Openness.ToString("F3");
                    yield return Capture("04a_eyelid_beat1");
                    continue;
                }

                if (beat2 == null && current.StageLabel == "beat2"
                    && current.Openness > 0.32f)
                {
                    beat2 = current.Openness.ToString("F3");
                    yield return Capture("04b_eyelid_beat2");
                    continue;
                }

                if (beat3 == null && current.StageLabel == "beat3"
                    && current.Openness > 0.85f)
                {
                    beat3 = current.Openness.ToString("F3");
                    yield return Capture("04c_eyelid_beat3");
                    continue;
                }
            }

            yield return null;
        }

        _report.AppendLine("eyelid beats (openness): beat1=" + (beat1 ?? "-")
                           + ", beat2=" + (beat2 ?? "-")
                           + ", beat3=" + (beat3 ?? "-"));
        Check("beat 1 was observed", beat1 != null);
        Check("beat 2 was observed", beat2 != null);
        Check("beat 3 was observed", beat3 != null);
        Check("the eyes opened progressively",
            beat1 != null && beat2 != null && beat3 != null
            && float.Parse(beat1) < float.Parse(beat2)
            && float.Parse(beat2) < float.Parse(beat3));
        Check("the curtains were removed when the eyes opened", ship.EntryBlink == null);

        // ShipGameController releases the lock in its own Update, one frame after
        // the eyelid component retires itself.
        yield return WaitUntil(() => !ship.IsEntryInputLocked, 240);
        Check("control is released after the eyes open", !ship.IsEntryInputLocked);
        Check("the first-person camera renders",
            ship.Player != null && ship.Player.EyeCamera != null && ship.Player.EyeCamera.enabled);
        Check("the player can move", ship.Player.InputEnabled);
        Check("no terminal UI covers the frame",
            !ship.IsConsoleFocusActive && !ship.IsOverlayOpen);

        yield return new WaitForSecondsRealtime(0.4f);
        yield return Capture("04d_ship_interior_firstperson");

        // ---- 05: terminal -> Overview ----
        ShipConsoleInteractable overviewConsole = null;
        foreach (ShipConsoleInteractable console in ship.Consoles)
        {
            if (console.ConsoleId == 1)
            {
                overviewConsole = console;
                break;
            }
        }

        Check("the 总览 terminal exists on the bridge", overviewConsole != null);
        if (overviewConsole != null && ship.Focus != null)
        {
            if (ship.Player != null)
            {
                ship.Player.TeleportTo(overviewConsole.transform.position
                                       + overviewConsole.transform.forward * 2.4f);
                ship.Player.SetFacing(overviewConsole.transform.eulerAngles.y + 180f, 0f);
            }

            yield return new WaitForSecondsRealtime(0.6f);
            yield return Capture("05a_ship_terminal_approach");

            bool entered = ship.Focus.RequestEnter(overviewConsole);
            yield return WaitUntil(() => ship.Focus.FocusActive, 300);
            Check("the player can enter the terminal from inside the ship",
                entered && ship.Focus.FocusActive);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Capture("05b_ship_terminal_overview");

            ShipConsoleScreenHost host = null;
            foreach (ShipConsoleScreenHost candidate
                     in Object.FindObjectsByType<ShipConsoleScreenHost>())
            {
                if (candidate.MappedScreen == AppScreen.Overview)
                {
                    host = candidate;
                }
            }

            Check("the terminal really hosts the Overview screen",
                host != null && host.IsBound && host.CountVisibleScreens() == 1);

            ship.Focus.RequestExit();
            yield return WaitUntil(() => !ship.Focus.FocusActive, 300);
            Check("leaving the terminal restores first-person control",
                !ship.Focus.FocusActive && ship.Player.InputEnabled);
        }

        Finish(_failures == 0 ? 0 : 1);
    }

    // ---- helpers ----

    private IEnumerator Capture(string label)
    {
        string path = Path.Combine(_captureDir, label + ".png");

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[MainMenuCapture] cannot clear " + path + ": " + e.Message);
        }

        ScreenCapture.CaptureScreenshot(path);

        bool written = false;
        for (int i = 0; i < 400; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 2000)
            {
                written = true;
                break;
            }

            yield return null;
        }

        if (!written)
        {
            Fail("capture " + label + " was never written");
            yield break;
        }

        Vector2Int size = ReadPngSize(path);
        _report.AppendLine("capture " + label + " -> " + path + " ("
                           + size.x + "x" + size.y + ")");
        Check("capture " + label + " is 1920x1080", size.x == 1920 && size.y == 1080);
    }

    private IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = MaxFrames)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }

    private void Check(string label, bool condition)
    {
        _report.AppendLine((condition ? "PASS" : "FAIL") + " :: " + label);
        if (!condition)
        {
            _failures++;
        }
    }

    private void Fail(string label)
    {
        Check(label, false);
    }

    private void Finish(int exitCode)
    {
        _report.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        _report.AppendLine("failures: " + _failures);
        try
        {
            File.WriteAllText(Path.Combine(_captureDir, "main_menu_capture_report.txt"),
                _report.ToString());
        }
        catch (System.Exception e)
        {
            Debug.LogError("[MainMenuCapture] cannot write report: " + e);
        }

        Debug.Log("[MainMenuCapture] finished with " + _failures + " failure(s)");
        Application.Quit(exitCode);
    }

    /// <summary>PNG dimensions straight from the IHDR chunk (no image library).</summary>
    private static Vector2Int ReadPngSize(string path)
    {
        try
        {
            byte[] header = new byte[24];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                if (stream.Read(header, 0, 24) < 24)
                {
                    return Vector2Int.zero;
                }
            }

            int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return new Vector2Int(width, height);
        }
        catch (System.Exception)
        {
            return Vector2Int.zero;
        }
    }
}
