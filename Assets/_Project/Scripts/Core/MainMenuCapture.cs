using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Main Menu cover capture + journey probe (Phase 08 Revision, validation
/// instrumentation only).
///
/// Inert during normal play: it only activates when the player is launched with
/// <c>-menuCaptureDir &lt;absolute path&gt;</c>. It then drives the real product
/// flow through REAL UI Toolkit clicks and captures the composited frame (3D
/// stage + UI overlay) at 1920x1080:
///
///   01  Main Menu idle (left ship / right menu / deep space)
///   01b Main Menu idle 1.6 s later (proves the cover is alive)
///   02  Main Menu 系统设置
///   03a cinematic: menu fade-out
///   03b cinematic: engine boost + ship departure
///   03c cinematic: blackout
///   04a ship interior: first-person blink fade-in
///   04b ship interior: first-person, terminal ahead
///   05  ship terminal: the Overview screen, reached by walking in
///
/// It also asserts the revision contracts (ship on the left, menu on the right,
/// starfield present, plumes present, cinematic phases in order, ship interior
/// reached, terminal reachable) and writes
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
        // player stops ticking, which would freeze the cinematic mid-shot (and
        // would also stall the coroutine that captures it).
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
        _report.AppendLine("SURVIVE // DRIFT - Phase 08 Revision Main Menu capture");
        _report.AppendLine("scene at start: " + SceneManager.GetActiveScene().name);

        // ---- Window: the review pack is captured at 1920x1080 ----
        Screen.SetResolution(1920, 1080, false);
        int settle = 0;
        while ((Screen.width != 1920 || Screen.height != 1080) && settle < 240)
        {
            settle++;
            yield return null;
        }

        _report.AppendLine("resolution: " + Screen.width + "x" + Screen.height
                           + " (settled after " + settle + " frames)");
        Check("capture resolution is 1920x1080", Screen.width == 1920 && Screen.height == 1080);

        // ---- Main Menu ready ----
        MainMenuController menu = null;
        yield return WaitUntil(() =>
        {
            menu = Object.FindAnyObjectByType<MainMenuController>();
            return menu != null && menu.IsReady;
        });

        if (menu == null)
        {
            Fail("main menu never became ready");
            Finish(1);
            yield break;
        }

        // Let the starfield generate and the first frames settle.
        yield return new WaitForSecondsRealtime(1.2f);

        // ---- Composition contract ----
        MainMenuShipHero hero = Object.FindAnyObjectByType<MainMenuShipHero>();
        MainMenuStarfield stars = Object.FindAnyObjectByType<MainMenuStarfield>();
        Camera stageCamera = Object.FindAnyObjectByType<Camera>();
        MainMenuCinematic cinematic = menu.Cinematic;

        Check("hero ship present (" + (hero != null ? hero.ShipRendererCount : 0) + " renderers)",
            hero != null && hero.IsShipPresent && hero.ShipRendererCount >= 40);
        Check("two thruster plumes built from the asset anchors",
            hero != null && hero.NozzleCount == 2);
        Check("deep-space starfield built (" + (stars != null ? stars.StarCount : 0) + " stars)",
            stars != null && stars.IsActive && stars.LayerCount == 3 && stars.StarCount > 1000);
        Check("cinematic authored and idle", cinematic != null && !cinematic.IsPlaying);

        VisualElement heroZone = menu.UiRoot.Q<VisualElement>(MainMenuNames.HeroColumn);
        VisualElement menuColumn = menu.UiRoot.Q<VisualElement>(MainMenuNames.ContentColumn);
        Rect rootRect = menu.UiRoot.worldBound;
        Check("menu column is on the RIGHT of the hero window",
            heroZone != null && menuColumn != null
            && heroZone.worldBound.xMax <= menuColumn.worldBound.xMin + 1f);
        Check("menu column starts past 55% of the frame width",
            menuColumn != null && menuColumn.worldBound.xMin > rootRect.width * 0.55f);

        if (stageCamera != null && hero != null)
        {
            float shipX = stageCamera.WorldToViewportPoint(hero.WorldBounds.center).x;
            Check("ship centre is in the left half (x=" + shipX.ToString("F3") + ")", shipX < 0.62f);
        }

        // ---- 01 / 01b: idle cover (motion proof) ----
        yield return Capture("01_main_menu_idle");

        Vector3 positionA = hero != null ? hero.transform.position : Vector3.zero;
        float plumeA = hero != null && hero.Flames.Count > 0 ? hero.Flames[0].PlumeLength : 0f;
        yield return new WaitForSecondsRealtime(1.6f);
        yield return Capture("01b_main_menu_idle_later");

        if (hero != null && hero.Flames.Count > 0)
        {
            Vector3 positionB = hero.transform.position;
            float plumeB = hero.Flames[0].PlumeLength;
            Check("idle cover is alive: hero moved "
                  + Vector3.Distance(positionA, positionB).ToString("F4") + " m",
                Vector3.Distance(positionA, positionB) > 0.0005f);
            Check("idle plume flickers (" + plumeA.ToString("F3") + " -> "
                  + plumeB.ToString("F3") + " m)", Mathf.Abs(plumeA - plumeB) > 0.0005f);
            Check("idle plume is a real flame (" + plumeB.ToString("F2") + " m / intensity "
                  + hero.Flames[0].Intensity.ToString("F2") + ")",
                plumeB > 3f && hero.Flames[0].Intensity > 0.4f);
        }

        // ---- 02: settings modal ----
        VisualElement settingsButton = menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton);
        if (settingsButton != null)
        {
            UiToolkitPointerSimulator.Click(settingsButton);
            yield return WaitUntil(() => menu.IsSettingsOpen, 120);
            Check("系统设置 opens from a real click", menu.IsSettingsOpen);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Capture("02_main_menu_settings");

            VisualElement close = menu.UiRoot.Q<Button>(MainMenuNames.SettingsCloseButton);
            if (close != null)
            {
                UiToolkitPointerSimulator.Click(close);
                yield return WaitUntil(() => !menu.IsSettingsOpen, 120);
            }

            Check("系统设置 closes again", !menu.IsSettingsOpen);
        }

        // ---- 03: 进入任务 -> the start cinematic ----
        yield return new WaitForSecondsRealtime(0.5f);
        Button cta = menu.EnterMissionButton;
        Check("进入任务 CTA exists", cta != null);
        if (cta == null)
        {
            Finish(1);
            yield break;
        }

        UiToolkitPointerSimulator.Click(cta);
        yield return null;
        Check("进入任务 starts the cinematic", cinematic != null && cinematic.IsPlaying);

        bool sawFade = false;
        bool sawBoost = false;
        bool sawDeparture = false;
        bool sawBlack = false;
        bool capturedFade = false;
        bool capturedDeparture = false;
        bool capturedBlack = false;
        float maxBlackAlign = 0f;
        float maxMenuAlphaSeen = 1f;
        int frames = 0;

        while (frames < MaxFrames
               && SceneManager.GetActiveScene().name == SceneRouting.MainMenuSceneName)
        {
            frames++;
            if (cinematic != null)
            {
                maxBlackAlign = Mathf.Max(maxBlackAlign, cinematic.BlackAlpha);
                maxMenuAlphaSeen = Mathf.Min(maxMenuAlphaSeen, cinematic.MenuAlpha);

                if (!sawFade && cinematic.MenuAlpha < 0.35f)
                {
                    sawFade = true;
                    _report.AppendLine("phase MenuFadeOut observed at t="
                                       + cinematic.ElapsedSeconds.ToString("F2") + "s");
                    if (!capturedFade)
                    {
                        capturedFade = true;
                        yield return Capture("03a_cinematic_menu_fadeout");
                    }
                }

                if (!sawBoost && hero != null && hero.EngineTarget > 0.6f)
                {
                    sawBoost = true;
                    _report.AppendLine("phase EngineBoost observed at t="
                                       + cinematic.ElapsedSeconds.ToString("F2") + "s"
                                       + " (plume " + (hero.Flames.Count > 0
                                           ? hero.Flames[0].PlumeLength.ToString("F2")
                                           : "-") + " m)");
                }

                if (!sawDeparture && hero != null && hero.DepartureProgress > 0.35f)
                {
                    sawDeparture = true;
                    _report.AppendLine("phase ShipDeparture observed at t="
                                       + cinematic.ElapsedSeconds.ToString("F2") + "s"
                                       + " moved " + Vector3.Distance(hero.transform.position,
                                           hero.SpawnPosition).ToString("F1") + " m");
                    if (!capturedDeparture)
                    {
                        capturedDeparture = true;
                        yield return Capture("03b_cinematic_ship_departure");
                    }
                }

                if (!sawBlack && cinematic.BlackAlpha > 0.85f)
                {
                    sawBlack = true;
                    _report.AppendLine("phase FadeToBlack observed at t="
                                       + cinematic.ElapsedSeconds.ToString("F2") + "s");
                    if (!capturedBlack)
                    {
                        capturedBlack = true;
                        yield return Capture("03c_cinematic_blackout");
                    }
                }
            }

            yield return null;
        }

        _report.AppendLine("cinematic sampled: frames=" + frames
                           + " minMenuAlpha=" + maxMenuAlphaSeen.ToString("F2")
                           + " maxBlackAlpha=" + maxBlackAlign.ToString("F2"));
        Check("cinematic faded the menu out (min alpha "
              + maxMenuAlphaSeen.ToString("F2") + ")", sawFade);
        Check("cinematic boosted the thrusters", sawBoost);
        Check("cinematic departed the ship", sawDeparture);
        Check("cinematic reached the blackout (max alpha "
              + maxBlackAlign.ToString("F2") + ")", sawBlack || maxBlackAlign > 0.85f);

        // ---- 04: the ship interior opens in first person with a blink ----
        ShipGameController ship = null;
        yield return WaitUntil(() =>
        {
            ship = Object.FindAnyObjectByType<ShipGameController>();
            return ship != null && ship.IsReady;
        });

        if (ship == null)
        {
            Fail("进入任务 never reached the ship interior");
            Finish(1);
            yield break;
        }

        yield return new WaitForSecondsRealtime(0.35f);
        Check("landing scene is the ship interior",
            SceneManager.GetActiveScene().name == GameSession.ShipInteriorSceneName);
        Check("first-person camera is rendering",
            ship.Player != null && ship.Player.EyeCamera != null && ship.Player.EyeCamera.enabled);
        Check("no terminal UI covers the frame on entry",
            !ship.IsConsoleFocusActive && !ship.IsOverlayOpen);
        Check("the menu hand-over armed the first-person blink", ship.EntryBlinkArmed);

        float liveBlinkAlpha = ship.EntryBlink != null ? ship.EntryBlink.Alpha : -1f;
        _report.AppendLine("live blink alpha at sample: " + liveBlinkAlpha.ToString("F2")
                           + (ship.EntryBlink != null ? " (playing)" : " (already open)"));
        yield return Capture("04a_ship_interior_blink");

        yield return WaitUntil(() => ship.EntryBlink == null, 400);
        yield return new WaitForSecondsRealtime(0.4f);
        Check("the blink finished and the interior is fully visible",
            ship.EntryBlink == null && ship.Player.InputEnabled);

        // The blink lasts under a second, so a slow first frame can finish it
        // before the capture above. When that happened the pack replays the SAME
        // production component once, so the review image always shows the real
        // eyelid presentation instead of an already-open cockpit.
        if (liveBlinkAlpha < 0.5f)
        {
            ShipEntryBlinkFade replay = ship.gameObject.AddComponent<ShipEntryBlinkFade>();
            yield return new WaitForSecondsRealtime(0.5f);
            _report.AppendLine("replayed the entry blink for the review frame (alpha "
                               + (replay != null ? replay.Alpha.ToString("F2") : "-") + ")");
            yield return Capture("04a_ship_interior_blink");
            yield return WaitUntil(() => replay == null, 400);
        }

        yield return Capture("04b_ship_interior_firstperson");

        // ---- 05: the 总览 terminal is reachable from inside the ship ----
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
            // Walk-in presentation: the player stands in the terminal trigger and
            // the HUD prompt is what a human sees before pressing E.
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

        string bootstrap = "";
        if (hero != null)
        {
            bootstrap += " heroBounds=" + hero.WorldBounds.size.ToString("F2");
        }

        _report.AppendLine("stage:" + bootstrap);
        Finish(_failures == 0 ? 0 : 1);
    }

    // ---- helpers ----

    private IEnumerator Capture(string label)
    {
        string path = Path.Combine(_captureDir, label + ".png");

        // Always start from a clean slate: a stale file from an earlier capture
        // would otherwise be mistaken for the new frame.
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
                           + size.x + "x" + size.y + ", "
                           + new FileInfo(path).Length + " bytes)");
        Check("capture " + label + " is " + 1920 + "x" + 1080,
            size.x == 1920 && size.y == 1080);
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
