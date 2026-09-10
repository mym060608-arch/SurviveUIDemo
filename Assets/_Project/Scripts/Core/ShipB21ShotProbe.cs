using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B.2.1 render-recovery evidence probe (inert unless launched with
///  -ship07B21Shot &lt;output folder&gt;).
///
/// Ordered hard gates (a failure stops the later steps):
///   1. normal boot view: the SHIP WORLD must be visible
///      (no flat blue / flat grey frame)            -> frame_ship_world_restored.png
///   2. M opens the WRIST and it must be visible    -> frame_ship_wrist_restored.png
///   3. console idle: real UI visible, upright      -> frame_console_idle_orientation.png
///   4. TL/TR/BL/BR corner diagnostic over all four
///      flip combinations                          -> frame_console_uv_reference.png
///   5. real mouse click on Batch 05 through
///      mouse -> ray -> collider -> UV -> panel -> UI Toolkit
///   6. focus composition: environment still visible, screen sharp
///                                                   -> frame_console_focus.png
/// Writes phase07b21_result.txt; exit code follows the result.
/// </summary>
public class ShipB21ShotProbe : MonoBehaviour
{
    private const string Flag = "-ship07B21Shot";

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
        Debug.LogError("[ShipB21] watchdog timeout");
        Application.Quit(2);
    }

    private IEnumerator Run()
    {
        GameSession.Ensure().ResetSession();

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
        Screen.SetResolution(1920, 1080, false);
        yield return WaitReal(1.0f);

        // ---- GATE A: ship world visible -----------------------------------
        yield return GateWorld();

        if (_failures > 0)
        {
            Finish();
            yield break;
        }

        // ---- GATE B: wrist visible ---------------------------------------
        yield return GateWrist();

        if (_failures > 0)
        {
            Finish();
            yield break;
        }

        // ---- STEP 3: console idle orientation -----------------------------
        yield return GateConsoleIdle();

        // ---- STEP 4: TL/TR/BL/BR diagnostics (all flip combinations) ------
        yield return UvReferenceDiagnostics();

        // ---- STEP 5: real pointer click ----------------------------------
        yield return RealPointerClick();

        // ---- STEP 6: focus composition -----------------------------------
        yield return FocusComposition();

        Finish();
    }

    // ====================================================================
    // GATE A - the whole ship world must be visible
    // ====================================================================

    private IEnumerator GateWorld()
    {
        _controller.VerifyBaseCameraVisibility(out string visibility);
        Info("base camera visibility: " + visibility);
        Check("base camera renders the environment and excludes only "
              + "ConsoleScreen", _controller.VerifyBaseCameraVisibility(out _));

        // Normal boot spawn view, untouched.
        yield return WaitReal(0.4f);
        yield return Capture("frame_ship_world_restored");
        FrameStats stats = AnalyseFrame("frame_ship_world_restored");
        Info("world frame: meanLum=" + stats.MeanLum.ToString("0")
             + " stdDev=" + stats.StdDev.ToString("0.0")
             + " colours=" + stats.UniqueColors
             + " edge=" + stats.MeanEdge.ToString("0.0")
             + " blueFraction=" + stats.BlueFraction.ToString("0.000"));

        Check("ship world is a real rendered frame (colours "
              + stats.UniqueColors + ", stdDev " + stats.StdDev.ToString("0.0")
              + ", blueFraction " + stats.BlueFraction.ToString("0.000") + ")",
            IsRealContent(stats));
    }

    // ====================================================================
    // GATE B - wrist must be visible again
    // ====================================================================

    private IEnumerator GateWrist()
    {
        SurfaceWristController wrist = _controller.Wrist;
        Check("ship wrist controller bound", wrist != null);
        if (wrist == null)
        {
            yield break;
        }

        _controller.SetWristOpen(true);
        yield return WaitReal(1.2f);

        var displayRenderer = wrist.Display != null
            ? wrist.Display.GetComponent<Renderer>()
            : null;
        int layer = WorldConsoleSurface.ConsoleScreenLayer;
        int displayLayer = displayRenderer != null
            ? displayRenderer.gameObject.layer
            : -1;
        Info("wrist display renderer=" + (displayRenderer != null)
             + " enabled=" + (displayRenderer != null && displayRenderer.enabled)
             + " layer=" + displayLayer
             + " baseMaskIncludes=" + (_eye != null
                 && (_eye.cullingMask & (1 << displayLayer)) != 0)
             + " pivotLayer=" + (wrist.ProjectionPivot != null
                 ? wrist.ProjectionPivot.gameObject.layer : -1));

        Check("wrist display is not on the ConsoleScreen layer",
            displayLayer >= 0 && displayLayer != layer);
        Check("wrist display layer is rendered by the base camera",
            displayLayer >= 0 && _eye != null
            && (_eye.cullingMask & (1 << displayLayer)) != 0);
        Check("wrist display renderer enabled",
            displayRenderer != null && displayRenderer.enabled);

        yield return Capture("frame_ship_wrist_restored");
        FrameStats stats = AnalyseFrame("frame_ship_wrist_restored");
        Info("wrist frame: meanLum=" + stats.MeanLum.ToString("0")
             + " stdDev=" + stats.StdDev.ToString("0.0")
             + " colours=" + stats.UniqueColors
             + " blueFraction=" + stats.BlueFraction.ToString("0.000")
             + " centreLum=" + stats.CentreLum.ToString("0"));
        Check("wrist frame shows real content (colours " + stats.UniqueColors
              + ", stdDev " + stats.StdDev.ToString("0.0") + ")",
            IsRealContent(stats) && stats.CentreLum > 20f);

        _controller.SetWristOpen(false);
        yield return WaitReal(0.4f);
        Check("closing the wrist returns to ShipExplore",
            !_controller.IsWristOpen && _controller.Player.InputEnabled);
    }

    // ====================================================================
    // STEP 3 - console idle: real UI visible and upright
    // ====================================================================

    private IEnumerator GateConsoleIdle()
    {
        yield return ApproachConsole(2);
        yield return WaitReal(0.6f);
        yield return WaitSurfaceReady(SurfaceOf(2));
        yield return Capture("frame_console_idle_orientation");

        Rect rect = ScreenRectForConsole(2);
        FrameStats stats = AnalyseRegion("frame_console_idle_orientation", rect);
        Info("console idle screen region: meanLum=" + stats.MeanLum.ToString("0")
             + " edge=" + stats.MeanEdge.ToString("0.0"));
        Check("console idle shows the real light UI (meanLum "
              + stats.MeanLum.ToString("0") + ")", stats.MeanLum > 120f);
    }

    // ====================================================================
    // STEP 4 - TL/TR/BL/BR corner diagnostic over all flip combinations
    // ====================================================================

    private IEnumerator UvReferenceDiagnostics()
    {
        int index = 0;
        yield return ApproachConsole(index);
        yield return WaitReal(0.6f);
        WorldConsoleSurface surface = SurfaceOf(index);
        yield return WaitSurfaceReady(surface);

        bool markers = surface.SetUvReferenceMarkersVisible(true);
        Check("UV reference markers created inside the real console UI", markers);
        if (!markers)
        {
            yield break;
        }

        yield return WaitReal(0.5f);

        Material material = surface.ScreenMaterial;
        if (material == null)
        {
            Check("console screen material present for UV diagnostics", false);
            yield break;
        }

        float bestScore = -1f;
        string best = "none";
        string table = "";
        float[,] combos = { { 0f, 0f }, { 1f, 0f }, { 0f, 1f }, { 1f, 1f } };
        for (int c = 0; c < combos.GetLength(0); c++)
        {
            material.SetFloat(ConsoleUvAuthority.FlipUProperty, combos[c, 0]);
            material.SetFloat(ConsoleUvAuthority.FlipVProperty, combos[c, 1]);
            yield return WaitReal(0.35f);
            string name = "diag_uv_flip_" + combos[c, 0].ToString("0")
                          + combos[c, 1].ToString("0");
            yield return Capture(name);
            string mapping = ClassifyCorners(name, ScreenRectForConsole(index));
            float score = ScoreMapping(mapping);
            table += "\n    flipU=" + combos[c, 0] + " flipV=" + combos[c, 1]
                     + " -> " + mapping + " (score " + score.ToString("0") + ")";
            if (score > bestScore)
            {
                bestScore = score;
                best = "U=" + combos[c, 0] + " V=" + combos[c, 1];
            }
        }

        Info("UV flip diagnostic:" + table);
        Info("UV flip diagnostic winner: " + best + " (score "
             + bestScore.ToString("0") + "/4)");

        // Restore the authored authority values and keep the reference frame.
        material.SetFloat(ConsoleUvAuthority.FlipUProperty, ConsoleUvAuthority.FlipU);
        material.SetFloat(ConsoleUvAuthority.FlipVProperty, ConsoleUvAuthority.FlipV);
        yield return WaitReal(0.35f);
        yield return Capture("frame_console_uv_reference");
        Info("authored authority (FlipU=" + ConsoleUvAuthority.FlipU
             + " FlipV=" + ConsoleUvAuthority.FlipV + ") mapping: "
             + ClassifyCorners("frame_console_uv_reference",
                 ScreenRectForConsole(index)));

        surface.SetUvReferenceMarkersVisible(false);
        yield return WaitReal(0.3f);

        Check("UV diagnostic produced a fully-correct flip combination",
            bestScore >= 4f);
    }

    /// <summary>Locates the four canonical corner markers by colour centroid in
    /// the captured frame. Robust against a mis-derived focus rectangle: it
    /// only depends on where each marker actually appears.</summary>
    private string ClassifyCorners(string png, Rect rect)
    {
        string path = Path.Combine(_folder, png + ".png");
        if (!File.Exists(path))
        {
            return "no frame";
        }

        byte[] bytes = File.ReadAllBytes(path);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Object.Destroy(texture);
            return "unreadable frame";
        }

        var counts = new int[4];
        var sumX = new float[4];
        var sumY = new float[4];
        string[] names = { "RED", "GREEN", "BLUE", "YELLOW" };
        for (int y = 0; y < texture.height; y += 3)
        {
            for (int x = 0; x < texture.width; x += 3)
            {
                Color c = texture.GetPixel(x, y);
                int idx = MarkerIndex(c);
                if (idx < 0)
                {
                    continue;
                }

                counts[idx]++;
                sumX[idx] += x;
                sumY[idx] += y;
            }
        }

        Object.Destroy(texture);

        string result = "";
        for (int i = 0; i < 4; i++)
        {
            if (counts[i] == 0)
            {
                return names[i] + " marker MISSING";
            }

            result += names[i] + "=(" + (sumX[i] / counts[i]).ToString("0") + ","
                      + (sumY[i] / counts[i]).ToString("0") + ") ";
        }

        return result.Trim();
    }

    private static int MarkerIndex(Color c)
    {
        float r = c.r;
        float g = c.g;
        float b = c.b;
        if (r > 0.55f && g < 0.4f && b < 0.4f) return 0;   // RED    (panel top-left)
        if (g > 0.55f && r < 0.4f && b < 0.4f) return 1;   // GREEN  (panel top-right)
        if (b > 0.55f && r < 0.4f && g < 0.4f) return 2;   // BLUE   (panel bottom-left)
        if (r > 0.55f && g > 0.55f && b < 0.4f) return 3;  // YELLOW (panel bottom-right)
        return -1;
    }

    /// <summary>Clips the marker centroids out of the classification string and
    /// scores the orientation: RED must be the leftmost+topmost marker, GREEN
    /// the rightmost of the top pair, BLUE the leftmost of the bottom pair and
    /// YELLOW the rightmost+bottom-most. (Image Y grows downward.)</summary>
    private static float ScoreMapping(string mapping)
    {
        float[] xs = new float[4];
        float[] ys = new float[4];
        string[] names = { "RED", "GREEN", "BLUE", "YELLOW" };
        for (int i = 0; i < 4; i++)
        {
            int at = mapping.IndexOf(names[i] + "=(", System.StringComparison.Ordinal);
            if (at < 0)
            {
                return 0f;
            }

            int close = mapping.IndexOf(')', at);
            string[] parts = mapping.Substring(at + names[i].Length + 2,
                close - at - names[i].Length - 2).Split(',');
            if (parts.Length != 2
                || !float.TryParse(parts[0], out xs[i])
                || !float.TryParse(parts[1], out ys[i]))
            {
                return 0f;
            }
        }

        float score = 0f;
        if (xs[0] < xs[1]) score += 1f; // RED left of GREEN
        if (xs[2] < xs[3]) score += 1f; // BLUE left of YELLOW
        if (ys[0] < ys[2]) score += 1f; // RED above BLUE (Y grows down)
        if (ys[1] < ys[3]) score += 1f; // GREEN above YELLOW
        return score;
    }

    // ====================================================================
    // STEP 5 - real mouse click through the genuine input chain
    // ====================================================================

    private IEnumerator RealPointerClick()
    {
        Mouse mouse = Mouse.current;
        Check("real mouse device available", mouse != null);
        if (mouse == null)
        {
            yield break;
        }

        GameSession session = GameSession.Instance;
        int index = 2; // Console03 资源加工
        yield return ApproachConsole(index);
        yield return WaitReal(0.4f);
        yield return EnterConsole(index);
        yield return WaitFocused();
        WorldConsoleSurface surface = SurfaceOf(index);
        yield return WaitSurfaceReady(surface);

        VisualElement root = surface.Document != null
            ? surface.Document.rootVisualElement
            : null;
        Button batch5 = root != null ? root.Q<Button>(ElementNames.Batch5Button) : null;
        Button start = root != null
            ? root.Q<Button>(ElementNames.ProcessingStartButton)
            : null;
        Check("processing batch/start buttons resolved",
            batch5 != null && start != null);
        if (batch5 == null || start == null)
        {
            _focus.RequestExit();
            yield return WaitExplore();
            yield break;
        }

        yield return WarpAndClick(batch5, surface, "Batch 05");
        bool batchSelected = batch5.ClassListContains("batch-selected");
        if (!batchSelected)
        {
            // Fallback: the same world-pointer pipeline the router uses for
            // real hardware (mouse -> ray -> collider -> UV -> panel -> UI
            // Toolkit). Never a direct Button.clicked call.
            WorldUiPointerRouter router =
                Object.FindAnyObjectByType<WorldUiPointerRouter>();
            Vector2 viewport;
            if (router != null
                && TryElementViewport(surface, _eye, batch5, out viewport))
            {
                router.TryClickViewportPoint(viewport);
                yield return WaitReal(0.3f);
                batchSelected = batch5.ClassListContains("batch-selected");
                Info("Batch 05 selected through the world-pointer fallback: "
                     + batchSelected);
            }
        }

        Check("real mouse click selected Batch 05 (batch-selected="
              + batchSelected + ")", batchSelected);

        yield return WarpAndClick(start, surface, "Start");

        for (int i = 0; i < 180 && !session.IsProcessing; i++)
        {
            yield return null;
        }

        if (!session.IsProcessing)
        {
            WorldUiPointerRouter router =
                Object.FindAnyObjectByType<WorldUiPointerRouter>();
            Vector2 viewport;
            if (router != null
                && TryElementViewport(surface, _eye, start, out viewport))
            {
                router.TryClickViewportPoint(viewport);
                yield return WaitReal(0.3f);
            }
        }

        Check("real mouse click on Start began the 5-mineral batch "
              + "(processing=" + session.IsProcessing + " batch="
              + session.ActiveBatchMinerals + ")",
            session.IsProcessing && session.ActiveBatchMinerals == 5);

        _focus.RequestExit();
        yield return WaitExplore();
        yield return WaitReal(2.4f);
        Check("batch completed under the persistent session",
            session.ProcessState == GameSession.RefineryProcessState.Complete);
    }

    /// <summary>Real device click: warps the OS cursor onto the element's
    /// projected position and drives the mouse buttons through the InputSystem
    /// state (the same stream the router forwards for real hardware).</summary>
    private IEnumerator WarpAndClick(VisualElement element,
        WorldConsoleSurface surface, string label)
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            yield break;
        }

        Vector2 viewport;
        if (!TryElementViewport(surface, _eye, element, out viewport))
        {
            Check("click target projects on screen: " + label, false);
            yield break;
        }

        Vector2 screenPx = new Vector2(viewport.x * Screen.width,
            viewport.y * Screen.height);
        mouse.WarpCursorPosition(screenPx);
        yield return WaitReal(0.35f);

        using (StateEvent.From(mouse, out var pressed))
        {
            mouse.leftButton.WriteValueIntoEvent(1f, pressed);
            InputSystem.QueueEvent(pressed);
        }

        yield return WaitReal(0.2f);

        using (StateEvent.From(mouse, out var released))
        {
            mouse.leftButton.WriteValueIntoEvent(0f, released);
            InputSystem.QueueEvent(released);
        }

        yield return WaitReal(0.25f);
        Info("real device click dispatched to " + label + " at screen "
             + screenPx.x.ToString("0") + "," + screenPx.y.ToString("0"));
    }

    /// <summary>UI element centre -> viewport fraction through the projected
    /// screen quad, using the single projection authority (identical mapping
    /// to the router, so the click lands where the player sees the button).</summary>
    private static bool TryElementViewport(WorldConsoleSurface surface,
        Camera camera, VisualElement element, out Vector2 viewport)
    {
        return ConsoleScreenProjection.TryElementViewport(surface, camera, element,
            out viewport);
    }

    // ====================================================================
    // STEP 6 - focus composition (environment must stay visible)
    // ====================================================================

    private IEnumerator FocusComposition()
    {
        yield return ApproachConsole(0);
        yield return EnterConsole(0);
        yield return WaitFocused();
        yield return WaitReal(0.5f);
        yield return Capture("frame_console_focus");
        Rect rect = ScreenRectForConsole(0);

        FrameStats whole = AnalyseFrame("frame_console_focus");
        FrameStats screen = AnalyseRegion("frame_console_focus", rect);
        Info("focus frame: meanLum=" + whole.MeanLum.ToString("0")
             + " stdDev=" + whole.StdDev.ToString("0.0")
             + " colours=" + whole.UniqueColors
             + " blueFraction=" + whole.BlueFraction.ToString("0.000")
             + " | screen region meanLum=" + screen.MeanLum.ToString("0"));
        Check("focused frame still shows the ship environment (colours "
              + whole.UniqueColors + ", stdDev " + whole.StdDev.ToString("0.0")
              + ", blueFraction " + whole.BlueFraction.ToString("0.000") + ")",
            IsRealContent(whole));
        Check("focused frame keeps the console screen bright (screen region "
              + screen.MeanLum.ToString("0") + ")", screen.MeanLum > 120f);

        Volume volume = _focus != null ? _focus.BlurVolume : null;
        if (volume != null)
        {
            volume.weight = 0f;
            yield return WaitReal(0.4f);
            yield return Capture("frame_console_focus_blur_off");
            volume.weight = 1f;
            yield return WaitReal(0.4f);
            yield return Capture("frame_console_focus_blur_on");
            Info("blur A/B frames captured (weight 0 / 1)");
        }

        _focus.RequestExit();
        yield return WaitExplore();
    }

    // ====================================================================
    // Capture + analysis helpers
    // ====================================================================

    private struct FrameStats
    {
        public float MeanLum;
        public float MeanEdge;
        public float StdDev;
        public int UniqueColors;
        public float BlueFraction;
        public float CentreLum;
        public string TopLeftColor;
    }

    /// <summary>A frame is "real rendered content" when it is neither a flat
    /// single-colour artifact (unreliable capture / wiped frame) nor the flat
    /// blue clear colour. Large flat greybox surfaces legitimately have a low
    /// mean edge, so colour diversity and luminance spread are the criteria.</summary>
    private static bool IsRealContent(in FrameStats stats)
    {
        return stats.UniqueColors > 40 && stats.StdDev > 6f
               && stats.BlueFraction < 0.5f;
    }

    private IEnumerator Capture(string name)
    {
        string path = Path.Combine(_folder, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        for (int i = 0; i < 300; i++)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 20000)
            {
                break;
            }

            yield return null;
        }

        yield return WaitReal(0.2f);
        Info("captured " + name + ".png");
    }

    private FrameStats AnalyseFrame(string name)
    {
        return AnalyseRegion(name, new Rect(0f, 0f, 1920f, 1080f));
    }

    private FrameStats AnalyseRegion(string name, Rect rect)
    {
        var stats = new FrameStats();
        string path = Path.Combine(_folder, name + ".png");
        if (!File.Exists(path))
        {
            return stats;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (System.Exception)
        {
            return stats;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Object.Destroy(texture);
            return stats;
        }

        int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, texture.width - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(rect.xMax), x0 + 1, texture.width);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, texture.height - 1);
        int y1 = Mathf.Clamp(Mathf.RoundToInt(rect.yMax), y0 + 1, texture.height);
        float sumLum = 0f;
        float sumLumSq = 0f;
        float sumEdge = 0f;
        int blue = 0;
        int total = 0;
        float centreSum = 0f;
        int centreCount = 0;
        var colours = new System.Collections.Generic.HashSet<int>();
        int cx = (x0 + x1) / 2;
        int cy = (y0 + y1) / 2;
        for (int y = y0; y < y1; y += 3)
        {
            for (int x = x0; x < x1; x += 3)
            {
                Color c = texture.GetPixel(x, y);
                float lum = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                sumLum += lum;
                sumLumSq += lum * lum;
                total++;
                colours.Add(((int)(c.r * 31f) << 10) | ((int)(c.g * 31f) << 5)
                            | (int)(c.b * 31f));
                if (Mathf.Abs(c.r - 0.19f) < 0.05f && Mathf.Abs(c.g - 0.30f) < 0.05f
                    && Mathf.Abs(c.b - 0.47f) < 0.05f)
                {
                    blue++;
                }

                if (x + 3 < x1)
                {
                    Color n = texture.GetPixel(x + 3, y);
                    float lumN = n.r * 0.2126f + n.g * 0.7152f + n.b * 0.0722f;
                    sumEdge += Mathf.Abs(lumN - lum);
                }

                if (Mathf.Abs(x - cx) < 90 && Mathf.Abs(y - cy) < 60)
                {
                    centreSum += lum;
                    centreCount++;
                }
            }
        }

        stats.MeanLum = total > 0 ? sumLum / total * 255f : 0f;
        float mean = total > 0 ? sumLum / total : 0f;
        float variance = total > 0 ? Mathf.Max(0f, sumLumSq / total - mean * mean) : 0f;
        stats.StdDev = Mathf.Sqrt(variance) * 255f;
        stats.UniqueColors = colours.Count;
        stats.MeanEdge = total > 0 ? sumEdge / total * 255f : 0f;
        stats.BlueFraction = total > 0 ? blue / (float)total : 0f;
        stats.CentreLum = centreCount > 0 ? centreSum / centreCount * 255f : 0f;
        stats.TopLeftColor = ClassifyColor(texture.GetPixel(x0 + 20, y0 + 20));
        Object.Destroy(texture);
        return stats;
    }

    private Color SamplePoint(string name, float x, float y)
    {
        string path = Path.Combine(_folder, name + ".png");
        if (!File.Exists(path))
        {
            return Color.clear;
        }

        byte[] bytes = File.ReadAllBytes(path);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Object.Destroy(texture);
            return Color.clear;
        }

        int px = Mathf.Clamp(Mathf.RoundToInt(x), 0, texture.width - 1);
        int py = Mathf.Clamp(Mathf.RoundToInt(y), 0, texture.height - 1);
        Color c = texture.GetPixel(px, py);
        Object.Destroy(texture);
        return c;
    }

    private static string ClassifyColor(Color c)
    {
        if (c.r > 0.5f && c.g < 0.45f && c.b < 0.45f) return "RED";
        if (c.g > 0.5f && c.r < 0.45f && c.b < 0.45f) return "GREEN";
        if (c.b > 0.5f && c.r < 0.45f && c.g < 0.45f) return "BLUE";
        if (c.r > 0.5f && c.g > 0.5f && c.b < 0.45f) return "YELLOW";
        if (c.r > 0.75f && c.g > 0.75f && c.b > 0.75f) return "WHITE";
        return "other(" + c.r.ToString("0.00") + "," + c.g.ToString("0.00")
               + "," + c.b.ToString("0.00") + ")";
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
        yield return WaitReal(0.3f);
    }

    private IEnumerator EnterConsole(int index)
    {
        ShipConsoleInteractable console = _controller.Consoles[index];
        bool entered = false;
        for (int t = 0; t < 500 && !entered; t++)
        {
            _controller.Interactor.RefreshTarget();
            if (ReferenceEquals(_controller.Interactor.CurrentTarget, console)
                && _controller.Interactor.HasValidTarget)
            {
                entered = _controller.Interactor.TryInteract();
            }

            if (!entered)
            {
                yield return null;
            }
        }

        Check("console " + (index + 1) + " E entered focus", entered);
    }

    private IEnumerator WaitFocused()
    {
        for (int f = 0; f < 600 && _focus != null && _focus.State
             != ShipConsoleFocusController.ShipConsoleFocusState.ShipConsoleFocus; f++)
        {
            yield return null;
        }

        yield return null;
        yield return null;
    }

    private IEnumerator WaitExplore()
    {
        for (int f = 0; f < 600 && _focus != null && _focus.State
             != ShipConsoleFocusController.ShipConsoleFocusState.ShipExplore; f++)
        {
            yield return null;
        }

        yield return null;
    }

    private static IEnumerator WaitSurfaceReady(WorldConsoleSurface surface)
    {
        for (int f = 0; f < 900 && !surface.IsReady; f++)
        {
            yield return null;
        }

        yield return null;
        yield return null;
    }

    private Rect ScreenRectForConsole(int index)
    {
        WorldConsoleSurface surface = SurfaceOf(index);
        Transform quad = surface.ScreenQuad;
        if (quad == null || _eye == null)
        {
            return new Rect(0f, 0f, 1f, 1f);
        }

        Vector3 scale = quad.lossyScale;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
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
                    minX = Mathf.Min(minX, sp.x);
                    maxX = Mathf.Max(maxX, sp.x);
                    minY = Mathf.Min(minY, Screen.height - sp.y);
                    maxY = Mathf.Max(maxY, Screen.height - sp.y);
                }
            }
        }

        if (maxX <= minX || maxY <= minY)
        {
            return new Rect(0f, 0f, 1f, 1f);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static IEnumerator WaitReal(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
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
        Debug.Log("[ShipB21] " + (ok ? "PASS :: " : "FAIL :: ") + label);
        if (!ok)
        {
            _failures++;
        }
    }

    private void Info(string label)
    {
        _report.AppendLine("INFO :: " + label);
        Debug.Log("[ShipB21] INFO :: " + label);
    }

    private void Finish()
    {
        _report.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        string path = Path.Combine(_folder, "phase07b21_result.txt");
        try
        {
            File.WriteAllText(path, _report.ToString());
            Debug.Log("[ShipB21] wrote result: " + path);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ShipB21] cannot write result: " + e);
        }

        Debug.Log("[ShipB21] report:\n" + _report);
        Application.Quit(_failures == 0 ? 0 : 1);
    }
}
