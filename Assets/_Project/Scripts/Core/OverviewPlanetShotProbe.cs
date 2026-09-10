using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Phase 07B.2.4 release evidence probe (inert unless launched with
/// -overviewPlanetShot &lt;output folder&gt;).
///
/// Proves, in the Windows standalone Release player, that the Overview central
/// visual is a REAL rotating 3D planet preview and that the UI around it does
/// not move:
///   frame_overview_planet_t0.png / frame_overview_planet_t2.png
///       same camera + same UI state, 2 s apart: the planet surface pattern has
///       visibly rotated, everything else is pixel-identical,
///   frame_console_overview_planet.png
///       the ship's physical Overview terminal rendering the same preview,
/// plus numeric checks: spin continuity, no black RenderTexture plate behind
/// the planet, and an unchanged console pointer owner.
///
/// Writes overviewplanet_result.txt; exit code follows the result.
/// </summary>
public class OverviewPlanetShotProbe : MonoBehaviour
{
    private const string Flag = "-overviewPlanetShot";

    private string _folder;
    private bool _active;
    private readonly StringBuilder _report = new StringBuilder();
    private int _failures;

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
        yield return new WaitForSecondsRealtime(600f);
        Debug.LogError("[PlanetShot] watchdog timeout");
        Application.Quit(2);
    }

    private IEnumerator Run()
    {
        GameSession.Ensure().ResetSession();
        Screen.SetResolution(1920, 1080, false);
        yield return WaitReal(0.8f);

        yield return HubOverview();
        if (_failures == 0)
        {
            yield return ShipConsoleOverview();
        }

        Finish();
    }

    // ====================================================================
    // Hub Overview: t0 / t2 with identical camera + UI state
    // ====================================================================

    private IEnumerator HubOverview()
    {
        SceneManager.LoadScene("01_Hub");
        for (int f = 0; f < 2400; f++)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.name == "01_Hub")
            {
                break;
            }

            yield return null;
        }

        yield return WaitReal(0.4f);

        AppController hub = null;
        for (int f = 0; f < 2400; f++)
        {
            hub = Object.FindAnyObjectByType<AppController>();
            if (hub != null && hub.IsReady && hub.CurrentScreen == AppScreen.Overview)
            {
                break;
            }

            yield return null;
        }

        Check("hub overview ready", hub != null && hub.IsReady);
        OverviewPlanetPreview preview = OverviewPlanetPreview.Shared;
        Check("shared planet preview rig present", preview != null);
        if (preview == null)
        {
            yield break;
        }

        bool rigOk = preview.VerifySetup(out string rigReport);
        Info("preview rig: " + rigReport);
        Check("preview rig structure", rigOk);
        Check("real sphere mesh (not a sprite / flat disc)",
            preview.HasRealPlanetMesh(out _));

        // The Hub's overview must actually show the shared RT.
        VisualElement disc = hub.UiRoot != null
            ? hub.UiRoot.Q<VisualElement>(ElementNames.PlanetDisc)
            : null;
        Check("hub planet disc element present", disc != null);
        if (disc == null)
        {
            yield break;
        }

        Check("planet disc ignores pointer input",
            disc.pickingMode == PickingMode.Ignore);
        Check("preview renders while the hub overview is visible",
            preview.IsRendering);

        yield return WaitReal(1.2f);
        Rect planetRect = ElementScreenRect(hub.UiRoot, disc);

        float angle0 = preview.SpinAngleDegrees;
        float startTime = Time.realtimeSinceStartup;
        yield return Capture("frame_overview_planet_t0");
        yield return WaitReal(2.0f);
        yield return Capture("frame_overview_planet_t2");
        float endTime = Time.realtimeSinceStartup;
        float angle1 = preview.SpinAngleDegrees;
        float spun = Mathf.Repeat(angle1 - angle0, 360f);
        float window = Mathf.Max(0.1f, endTime - startTime);
        float rate = spun / window;
        Info("spin " + angle0.ToString("0.00") + " -> " + angle1.ToString("0.00")
             + " (delta " + spun.ToString("0.00") + " deg over "
             + window.ToString("0.00") + " s = " + rate.ToString("0.00") + " deg/s)");
        Check("planet spins at the authored slow rate (" + rate.ToString("0.00")
              + " deg/s, authored " + OverviewPlanetPreview.SpinDegreesPerSecond
              + ")", Mathf.Abs(rate - OverviewPlanetPreview.SpinDegreesPerSecond) < 1.5f);
        Check("the t0/t2 window is about 2 s (" + window.ToString("0.00") + " s)",
            window > 1.8f && window < 3.2f);

        // The planet area must change, the rest of the frame must not.
        Rect inner = Shrink(planetRect, 0.18f);
        float insideDiff = MeanAbsDiffRegion("frame_overview_planet_t0",
            "frame_overview_planet_t2", inner);
        float changed = ChangedPixelFraction("frame_overview_planet_t0",
            "frame_overview_planet_t2", inner, 8f);
        float outsideDiff = MeanAbsDiffOutside("frame_overview_planet_t0",
            "frame_overview_planet_t2", Grow(planetRect, 0.06f));
        Info("planet surface: mean|diff| " + insideDiff.ToString("0.0000")
             + " | changed pixels " + (changed * 100f).ToString("0.0") + "%"
             + " | outside mean|diff| " + outsideDiff.ToString("0.0000"));
        Check("planet surface visibly rotated (mean|diff| "
              + insideDiff.ToString("0.0000") + " > 0.15)", insideDiff > 0.15f);
        Check("a large part of the planet surface changed over 2 s ("
              + (changed * 100f).ToString("0.0") + "% of the disc > 8/255)",
            changed > 0.05f);
        Check("UI layout is pixel-stable (outside mean|diff| "
              + outsideDiff.ToString("0.0000") + " < 0.01)", outsideDiff < 0.01f);

        // No black RenderTexture plate: the four corners of the planet display
        // box sit outside the sphere silhouette and must show the UI panel.
        float cornerLum = MeanLumCorners("frame_overview_planet_t0", planetRect, 26);
        Info("planet box corner meanLum=" + cornerLum.ToString("0.0"));
        Check("no black RenderTexture plate behind the planet (corner meanLum "
              + cornerLum.ToString("0.0") + " > 150)", cornerLum > 150f);

        Check("Hub overview uses the shared preview texture",
            hub.OverviewView != null && hub.OverviewView.IsPlanetPreviewActive);
        Check("Hub overview did not fall back to the static disc",
            hub.OverviewView != null && !hub.OverviewView.IsPlanetFallbackActive);

        Check("no extra GameSession",
            Object.FindObjectsByType<GameSession>().Length == 1);
    }

    // ====================================================================
    // Ship Overview terminal
    // ====================================================================

    private IEnumerator ShipConsoleOverview()
    {
        SceneRouting.RequestShipEntry(SceneRouting.ShipEntry.Default);
        SceneManager.LoadScene(GameSession.ShipInteriorSceneName);
        for (int f = 0; f < 2400; f++)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.name == GameSession.ShipInteriorSceneName)
            {
                break;
            }

            yield return null;
        }

        yield return WaitReal(0.4f);

        ShipGameController controller = null;
        for (int f = 0; f < 2400; f++)
        {
            controller = Object.FindAnyObjectByType<ShipGameController>();
            if (controller != null && controller.IsReady && controller.Player != null)
            {
                break;
            }

            yield return null;
        }

        Check("ship interior ready", controller != null && controller.IsReady);
        if (controller == null || !controller.IsReady)
        {
            yield break;
        }

        OverviewPlanetPreview preview = OverviewPlanetPreview.Shared;
        Check("ship reuses the same preview rig (no duplicate)",
            preview != null
            && Object.FindObjectsByType<OverviewPlanetPreview>().Length == 1);

        WorldConsoleSurface overview = null;
        for (int i = 0; i < 4; i++)
        {
            WorldConsoleSurface candidate = controller.Consoles[i]
                .GetComponent<WorldConsoleSurface>();
            if (candidate.ScreenHost != null
                && candidate.ScreenHost.MappedScreen == AppScreen.Overview)
            {
                overview = candidate;
                break;
            }
        }

        Check("console 01 hosts the Overview screen", overview != null);
        if (overview == null)
        {
            yield break;
        }

        for (int f = 0; f < 900 && !overview.IsReady; f++)
        {
            yield return null;
        }

        Check("console 01 shares the planet preview",
            overview.ScreenHost.PlanetPreview == preview);
        Check("always-on bridge terminal keeps the preview rendering",
            preview != null && preview.IsRendering);

        // Stand in front of the bridge terminal.
        SurfacePlayerController player = controller.Player;
        Vector3 outward = overview.ScreenQuad != null
            ? overview.ScreenQuad.forward
            : Vector3.forward;
        Vector3 screen = overview.ScreenQuad != null
            ? overview.ScreenQuad.position
            : Vector3.zero;
        player.TeleportTo(screen + outward * 1.7f);
        player.SetFacing(Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg, 0f);
        yield return WaitReal(0.9f);

        Check("console pointer owner unchanged by the preview",
            controller.PointerOwner == ShipGameController.ShipPointerOwner.None);
        Check("console pointer router off in ShipExplore",
            controller.PointerRouter == null || !controller.PointerRouter.enabled);

        float angle0 = preview != null ? preview.SpinAngleDegrees : 0f;
        yield return Capture("frame_console_overview_planet");
        yield return WaitReal(1.0f);
        float angle1 = preview != null ? preview.SpinAngleDegrees : 0f;
        Check("bridge planet keeps turning on the physical terminal ("
              + Mathf.Repeat(angle1 - angle0, 360f).ToString("0.00") + " deg)",
            Mathf.Repeat(angle1 - angle0, 360f) > 2f);

        Check("console 01 screen still samples its own RT",
            overview.VerifyScreenBinding(out string binding));
        Info("console binding: " + binding);
    }

    // ====================================================================
    // Geometry / capture helpers (image space: y grows DOWN from the top)
    // ====================================================================

    /// <summary>Element bounds in image-space screen pixels.</summary>
    private static Rect ElementScreenRect(VisualElement root, VisualElement element)
    {
        float scale = root != null && root.worldBound.width > 1f
            ? root.worldBound.width / Mathf.Max(1f, Screen.width)
            : 1f;
        Rect bounds = element.worldBound;
        return new Rect(bounds.x / scale, bounds.y / scale,
            bounds.width / scale, bounds.height / scale);
    }

    private static Rect Shrink(Rect rect, float fraction)
    {
        float dx = rect.width * fraction;
        float dy = rect.height * fraction;
        return new Rect(rect.x + dx, rect.y + dy,
            Mathf.Max(1f, rect.width - dx * 2f), Mathf.Max(1f, rect.height - dy * 2f));
    }

    private static Rect Grow(Rect rect, float fraction)
    {
        float dx = rect.width * fraction;
        float dy = rect.height * fraction;
        return new Rect(rect.x - dx, rect.y - dy,
            rect.width + dx * 2f, rect.height + dy * 2f);
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

    private bool TryLoad(string name, out Texture2D texture)
    {
        texture = null;
        string path = Path.Combine(_folder, name + ".png");
        if (!File.Exists(path))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (System.Exception)
        {
            return false;
        }

        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Object.Destroy(texture);
            texture = null;
            return false;
        }

        return true;
    }

    private float MeanAbsDiffRegion(string nameA, string nameB, Rect rect)
    {
        if (!TryLoad(nameA, out Texture2D a) || !TryLoad(nameB, out Texture2D b))
        {
            return -1f;
        }

        int width = Mathf.Min(a.width, b.width);
        int height = Mathf.Min(a.height, b.height);
        int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, width - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(rect.xMax), x0 + 1, width);
        int top = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, height - 1);
        int bottom = Mathf.Clamp(Mathf.RoundToInt(rect.yMax), top + 1, height);
        float sum = 0f;
        int total = 0;
        for (int imageY = top; imageY < bottom; imageY += 2)
        {
            int y = height - 1 - imageY;
            for (int x = x0; x < x1; x += 2)
            {
                Color ca = a.GetPixel(x, y);
                Color cb = b.GetPixel(x, y);
                sum += (Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g)
                        + Mathf.Abs(ca.b - cb.b)) / 3f;
                total++;
            }
        }

        Object.Destroy(a);
        Object.Destroy(b);
        return total > 0 ? sum / total * 255f : -1f;
    }

    /// <summary>Fraction of sampled pixels inside a rect whose channel
    /// difference exceeds the threshold (0..255). Used instead of a plain mean
    /// so "the surface really moved" cannot hide in an average over a large,
    /// mostly flat area.</summary>
    private float ChangedPixelFraction(string nameA, string nameB, Rect rect,
        float threshold)
    {
        if (!TryLoad(nameA, out Texture2D a) || !TryLoad(nameB, out Texture2D b))
        {
            return -1f;
        }

        int width = Mathf.Min(a.width, b.width);
        int height = Mathf.Min(a.height, b.height);
        int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, width - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(rect.xMax), x0 + 1, width);
        int top = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, height - 1);
        int bottom = Mathf.Clamp(Mathf.RoundToInt(rect.yMax), top + 1, height);
        float limit = threshold / 255f;
        int changed = 0;
        int total = 0;
        for (int imageY = top; imageY < bottom; imageY += 2)
        {
            int y = height - 1 - imageY;
            for (int x = x0; x < x1; x += 2)
            {
                Color ca = a.GetPixel(x, y);
                Color cb = b.GetPixel(x, y);
                float diff = Mathf.Max(Mathf.Abs(ca.r - cb.r),
                    Mathf.Max(Mathf.Abs(ca.g - cb.g), Mathf.Abs(ca.b - cb.b)));
                if (diff > limit)
                {
                    changed++;
                }

                total++;
            }
        }

        Object.Destroy(a);
        Object.Destroy(b);
        return total > 0 ? changed / (float)total : -1f;
    }

    /// <summary>Mean absolute difference everywhere OUTSIDE the given box.</summary>
    private float MeanAbsDiffOutside(string nameA, string nameB, Rect exclude)
    {
        if (!TryLoad(nameA, out Texture2D a) || !TryLoad(nameB, out Texture2D b))
        {
            return -1f;
        }

        int width = Mathf.Min(a.width, b.width);
        int height = Mathf.Min(a.height, b.height);
        float sum = 0f;
        int total = 0;
        for (int imageY = 0; imageY < height; imageY += 4)
        {
            int y = height - 1 - imageY;
            for (int x = 0; x < width; x += 4)
            {
                if (exclude.Contains(new Vector2(x, imageY)))
                {
                    continue;
                }

                Color ca = a.GetPixel(x, y);
                Color cb = b.GetPixel(x, y);
                sum += (Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g)
                        + Mathf.Abs(ca.b - cb.b)) / 3f;
                total++;
            }
        }

        Object.Destroy(a);
        Object.Destroy(b);
        return total > 0 ? sum / total * 255f : -1f;
    }

    /// <summary>Mean luminance of small boxes at the four corners of a rect.</summary>
    private float MeanLumCorners(string name, Rect rect, float inset)
    {
        if (!TryLoad(name, out Texture2D texture))
        {
            return -1f;
        }

        Vector2[] corners =
        {
            new Vector2(rect.xMin + inset * 0.5f, rect.yMin + inset * 0.5f),
            new Vector2(rect.xMax - inset * 0.5f, rect.yMin + inset * 0.5f),
            new Vector2(rect.xMin + inset * 0.5f, rect.yMax - inset * 0.5f),
            new Vector2(rect.xMax - inset * 0.5f, rect.yMax - inset * 0.5f)
        };

        float sum = 0f;
        int total = 0;
        foreach (Vector2 corner in corners)
        {
            int cx = Mathf.Clamp(Mathf.RoundToInt(corner.x), 0, texture.width - 1);
            int cy = Mathf.Clamp(Mathf.RoundToInt(corner.y), 0, texture.height - 1);
            for (int dy = -6; dy <= 6; dy++)
            {
                for (int dx = -6; dx <= 6; dx++)
                {
                    int x = Mathf.Clamp(cx + dx, 0, texture.width - 1);
                    int imageY = Mathf.Clamp(cy + dy, 0, texture.height - 1);
                    Color c = texture.GetPixel(x, texture.height - 1 - imageY);
                    sum += c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                    total++;
                }
            }
        }

        Object.Destroy(texture);
        return total > 0 ? sum / total * 255f : -1f;
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
        Debug.Log("[PlanetShot] " + (ok ? "PASS :: " : "FAIL :: ") + label);
        if (!ok)
        {
            _failures++;
        }
    }

    private void Info(string label)
    {
        _report.AppendLine("INFO :: " + label);
        Debug.Log("[PlanetShot] INFO :: " + label);
    }

    private void Finish()
    {
        _report.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        string path = Path.Combine(_folder, "overviewplanet_result.txt");
        try
        {
            File.WriteAllText(path, _report.ToString());
            Debug.Log("[PlanetShot] wrote result: " + path);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[PlanetShot] cannot write result: " + e);
        }

        Debug.Log("[PlanetShot] report:\n" + _report);
        Application.Quit(_failures == 0 ? 0 : 1);
    }
}
