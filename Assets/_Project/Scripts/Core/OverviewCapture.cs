using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Overview visual validation instrumentation (validation only).
///
/// Inert during normal play: it only activates when the player is launched
/// with  -captureDir &lt;absolute path&gt;. It then:
/// 1. resets the session and shows the Overview on the default state,
/// 2. for each requested resolution (1920x1080, 1600x900, 1280x720):
///    - sizes the window,
///    - asserts layout geometry from the live visual tree (no overlap, no
///      clipping, nav usable, bars proportional to session values),
///    - captures a PNG,
///    - verifies the PNG dimensions from its header,
/// 3. writes  overview_capture_report.txt  and quits with a matching code.
/// </summary>
public class OverviewCapture : MonoBehaviour
{
    private const string CaptureDirFlag = "-captureDir";

    private static readonly Vector2Int[] TargetResolutions =
    {
        new Vector2Int(1920, 1080),
        new Vector2Int(1600, 900),
        new Vector2Int(1280, 720)
    };

    private string _captureDir;
    private bool _active;

    private void Awake()
    {
        _captureDir = ParseFlag(CaptureDirFlag);
        _active = !string.IsNullOrEmpty(_captureDir);
        if (!_active)
        {
            // Not a capture launch: remove the probe entirely.
            enabled = false;
        }
    }

    private void Start()
    {
        if (_active)
        {
            StartCoroutine(RunCapture());
            // Watchdog: guarantees the probe always terminates even if an
            // unexpected exception aborts the main coroutine.
            StartCoroutine(Watchdog());
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

    private IEnumerator Watchdog()
    {
        // 10 minutes is far beyond the expected capture time.
        yield return new WaitForSecondsRealtime(600f);
        Debug.LogError("[OverviewCapture] watchdog timeout - forcing quit");
        Application.Quit(2);
    }

    private IEnumerator RunCapture()
    {
        var results = new StringBuilder();
        int failures = 0;

        // Wait until the Hub UI is ready.
        AppController app = null;
        for (int i = 0; i < 900; i++)
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
            Fail(results, ref failures, "hub UI never became ready");
            results.AppendLine(failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
            WriteReport(results.ToString());
            Application.Quit(failures == 0 ? 0 : 1);
            yield break;
        }

        Directory.CreateDirectory(_captureDir);
        GameSession.Instance.ResetSession();
        app.ShowScreen(AppScreen.Overview);

        foreach (Vector2Int resolution in TargetResolutions)
        {
            string label = resolution.x + "x" + resolution.y;
            Screen.SetResolution(resolution.x, resolution.y, false);

            // Give the OS window switch and the layout pass time.
            int settle = 0;
            while ((Screen.width != resolution.x || Screen.height != resolution.y) && settle < 90)
            {
                settle++;
                yield return null;
            }

            yield return new WaitForSecondsRealtime(0.5f);

            VerifyLayout(results, ref failures, app, label);

            string png = Path.Combine(_captureDir,
                "surviveui_phase02_overview_" + label + ".png");
            ScreenCapture.CaptureScreenshot(png);

            bool written = false;
            for (int w = 0; w < 300; w++)
            {
                if (File.Exists(png))
                {
                    var info = new FileInfo(png);
                    if (info.Length > 2000)
                    {
                        written = true;
                        break;
                    }
                }

                yield return null;
            }

            Vector2Int actual = ReadPngSize(png);
            Check(results, ref failures,
                "capture " + label + " png dimensions " + actual.x + "x" + actual.y,
                written && actual.x == resolution.x && actual.y == resolution.y);
        }

        results.AppendLine(failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        WriteReport(results.ToString());
        Application.Quit(failures == 0 ? 0 : 1);
    }

    /// <summary>Geometry assertions over the live visual tree (panel pixels).</summary>
    private static void VerifyLayout(StringBuilder results, ref int failures,
        AppController app, string label)
    {
        VisualElement root = app.UiRoot;
        if (root == null)
        {
            Fail(results, ref failures, label + " ui root missing");
            return;
        }

        Rect rootRect = WorldBounds(root);
        VisualElement ship = root.Q<VisualElement>(ElementNames.ShipStatusPanel);
        VisualElement objective = root.Q<VisualElement>(ElementNames.ObjectivePanel);
        VisualElement planet = root.Q<VisualElement>(ElementNames.PlanetDisc);
        VisualElement cta = root.Q<Button>(ElementNames.OpenPreparationButton);
        VisualElement nav = root.Q<VisualElement>(ElementNames.BottomNav);

        Check(results, ref failures, label + " overview unique + values bound",
            app.CurrentScreen == AppScreen.Overview && app.VisibleScreenCount == 1
            && LabelText(root, ElementNames.EnergyValue) == "74"
            && LabelText(root, ElementNames.MineralsValue) == "20"
            && LabelText(root, ElementNames.DestinationValue) == "NEREID-04");

        if (ship != null && objective != null)
        {
            Rect shipRect = WorldBounds(ship);
            Rect objectiveRect = WorldBounds(objective);
            Check(results, ref failures, label + " left/right columns do not overlap",
                shipRect.xMax <= objectiveRect.xMin);

            // Objective copy must soft-wrap inside the right column instead of
            // being clipped: a wrapped 14px label renders more than one line.
            Label objectiveCopy = root.Q<Label>(ElementNames.ObjectiveCopy);
            if (objectiveCopy != null)
            {
                Rect copyRect = WorldBounds(objectiveCopy);
                Check(results, ref failures,
                    label + " objective copy wrapped inside right column (height "
                    + copyRect.height.ToString("0") + "px, copy x "
                    + copyRect.xMin.ToString("0") + ".." + copyRect.xMax.ToString("0")
                    + " panel x " + objectiveRect.xMin.ToString("0") + ".."
                    + objectiveRect.xMax.ToString("0") + ")",
                    // Chinese body copy wraps to >= 2 lines (line height ~12px
                    // under the CJK system font), never clipped horizontally.
                    copyRect.height > 22f
                    && copyRect.xMin >= objectiveRect.xMin - 4f
                    && copyRect.xMax <= objectiveRect.xMax + 4f);
            }
        }
        else
        {
            Fail(results, ref failures, label + " overview columns missing");
        }

        if (planet != null && cta != null && nav != null)
        {
            Rect planetRect = WorldBounds(planet);
            Rect ctaRect = WorldBounds(cta);
            Rect navRect = WorldBounds(nav);
            bool insideRoot = rootRect.Contains(planetRect.center)
                              && planetRect.xMin >= rootRect.xMin
                              && planetRect.xMax <= rootRect.xMax;
            bool aboveControls = planetRect.yMax <= ctaRect.yMin
                                 && planetRect.yMax <= navRect.yMin;
            Check(results, ref failures, label + " planet visual centered & unclipped",
                insideRoot && aboveControls);
        }
        else
        {
            Fail(results, ref failures, label + " planet/cta/nav missing");
        }

        // CTA stays inside the panel and above the bottom navigation.
        if (cta != null && nav != null)
        {
            Rect ctaRect = WorldBounds(cta);
            Rect navRect = WorldBounds(nav);
            Check(results, ref failures, label + " primary CTA inside root & above nav",
                ctaRect.xMin >= rootRect.xMin && ctaRect.xMax <= rootRect.xMax
                && ctaRect.yMax <= navRect.yMin);
        }

        // Survival bars proportional to the default session values.
        CheckBar(results, ref failures, root, ElementNames.EnergyBarFill, 74f, label + " energy bar");
        CheckBar(results, ref failures, root, ElementNames.FoodBarFill, 51f, label + " food bar");

        // Navigation: all four tabs visible, within the root, non-overlapping.
        Button[] tabs =
        {
            root.Q<Button>(ElementNames.NavOverview),
            root.Q<Button>(ElementNames.NavPreparation),
            root.Q<Button>(ElementNames.NavProcessing),
            root.Q<Button>(ElementNames.NavOutpost)
        };

        bool tabsOk = true;
        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == null)
            {
                tabsOk = false;
                break;
            }

            Rect tabRect = WorldBounds(tabs[i]);
            if (tabRect.xMin < rootRect.xMin - 1f || tabRect.xMax > rootRect.xMax + 1f)
            {
                tabsOk = false;
            }

            if (i > 0)
            {
                Rect prev = WorldBounds(tabs[i - 1]);
                if (tabRect.xMin < prev.xMax - 1f)
                {
                    tabsOk = false;
                }
            }
        }

        Check(results, ref failures, label + " navigation tabs usable (in root, ordered)",
            tabsOk && root.Q<Button>(ElementNames.NavOverview) != null);
    }

    private static void CheckBar(StringBuilder results, ref int failures,
        VisualElement root, string fillName, float expectedPercent, string label)
    {
        VisualElement fill = root.Q<VisualElement>(fillName);
        if (fill == null || fill.parent == null)
        {
            Fail(results, ref failures, label + " element missing");
            return;
        }

        float trackWidth = fill.parent.resolvedStyle.width;
        float fillWidth = fill.resolvedStyle.width;
        float ratio = trackWidth > 1f ? fillWidth / trackWidth * 100f : 0f;
        Check(results, ref failures,
            label + " width ~ " + expectedPercent + "% (actual " + ratio.ToString("0") + "%)",
            System.Math.Abs(ratio - expectedPercent) <= 4f);
    }

    private static void Check(StringBuilder results, ref int failures, string label, bool condition)
    {
        if (condition)
        {
            results.AppendLine("PASS :: " + label);
        }
        else
        {
            failures++;
            results.AppendLine("FAIL :: " + label);
        }
    }

    private static void Fail(StringBuilder results, ref int failures, string label)
    {
        failures++;
        results.AppendLine("FAIL :: " + label);
    }

    private static string LabelText(VisualElement root, string name)
    {
        Label label = root.Q<Label>(name);
        return label != null ? label.text : "<missing>";
    }

    private static Rect WorldBounds(VisualElement element)
    {
        if (element == null)
        {
            return new Rect(0, 0, 0, 0);
        }

        return element.worldBound;
    }

    /// <summary>Reads PNG width/height from the file header (deterministic).</summary>
    private static Vector2Int ReadPngSize(string path)
    {
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var header = new byte[24];
                int read = stream.Read(header, 0, header.Length);
                if (read < 24)
                {
                    return new Vector2Int(0, 0);
                }

                int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return new Vector2Int(width, height);
            }
        }
        catch (System.Exception)
        {
            return new Vector2Int(0, 0);
        }
    }

    private void WriteReport(string content)
    {
        try
        {
            string path = Path.Combine(_captureDir, "overview_capture_report.txt");
            File.WriteAllText(path, content);
            Debug.Log("[OverviewCapture] wrote report: " + path);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[OverviewCapture] cannot write report: " + e);
        }
    }
}
