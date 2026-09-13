using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 03 clean review screenshots (validation/asset pipeline only).
///
/// Same Editor-panel offscreen rendering approach as Phase 02: the Windows
/// player overlays an engine watermark on its backbuffer, so review images
/// are rendered from the same UXML/USS/PanelSettings through the Editor-only
/// UI Toolkit path (PanelSettings.targetTexture). No cropping, covering or
/// post-editing. Captured state: all three modules carried, cargo 90/100,
/// loadout CONFIRMED, Debug hidden.
///
/// Produces 1920x1080 (primary), 1600x900 and 1280x720 PNGs plus per-size
/// geometric/no-overlap checks and watermark/debug probes.
/// Run isolated via: -testFilter SurvivePhase03ShotTests
/// </summary>
public class SurvivePhase03ShotTests
{
    private const string OutDir = @"D:\dsh_gpt_unity_blender\export\surviveui_phase03_shot_clean";
    private const string ReportName = "clean_shot_report.txt";

    private static readonly Vector2Int[] Sizes =
    {
        new Vector2Int(1920, 1080),
        new Vector2Int(1600, 900),
        new Vector2Int(1280, 720)
    };

    private const int MaxWaitFrames = 600;

    [UnityTest]
    public IEnumerator CleanPreparation_ThreeResolutions_ConfirmedState()
    {
        GameSession.Ensure().ResetSession();
        // Phase 07A.1: legacy Hub suites load the Hub explicitly.
        SceneManager.LoadScene(BootController.HubSceneName);
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == BootController.HubSceneName);

        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });

        Assert.IsNotNull(app, "AppController not ready");

        // Informative review state: full optional loadout, confirmed package.
        GameSession session = GameSession.Instance;
        Assert.IsTrue(session.TrySetBatterySelected(true), "select battery");
        Assert.IsTrue(session.TrySetDroneSelected(true), "select drone");
        Assert.IsTrue(session.TryConfirmLoadout(), "confirm loadout");
        Assert.AreEqual(90, session.CargoWeight, "cargo 90/100");
        Assert.IsTrue(session.LoadoutConfirmed, "confirmed state");

        app.ShowScreen(AppScreen.Preparation);
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one visible screen");

        // Debug panel must be hidden (element state, not pixel heuristic).
        VisualElement debugPanel = app.UiRoot.Q<VisualElement>(ElementNames.DebugPanel);
        Assert.IsNotNull(debugPanel, "debug panel exists");
        Assert.AreEqual(DisplayStyle.None, debugPanel.resolvedStyle.display,
            "debug panel hidden by default");

        UIDocument document = app.Document;
        Assert.IsNotNull(document, "UIDocument available");
        PanelSettings panel = document.panelSettings;
        Assert.IsNotNull(panel, "PanelSettings available");

        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Directory.CreateDirectory(OutDir);
        string primaryPng = Path.Combine(OutDir, "surviveui_phase03_preparation_1920x1080.png");
        var report = new System.Text.StringBuilder();
        int failures = 0;

        foreach (Vector2Int size in Sizes)
        {
            Texture2D png = null;
            var renderTexture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
            renderTexture.name = "PreparationCleanShot_" + size.x;
            renderTexture.Create();

            RenderTexture previousTarget = panel.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            panel.targetTexture = renderTexture;

            try
            {
                for (int i = 0; i < 12; i++)
                {
                    yield return null;
                }

                RenderTexture.active = renderTexture;
                var readback = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                readback.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                readback.Apply();
                png = readback;
            }
            finally
            {
                RenderTexture.active = previousActive;
                panel.targetTexture = previousTarget;
                renderTexture.Release();
            }

            Assert.IsNotNull(png, "panel readback produced for " + size.x + "x" + size.y);

            string label = size.x + "x" + size.y;
            string pngPath = Path.Combine(OutDir, "surviveui_phase03_preparation_" + label + ".png");
            File.WriteAllBytes(pngPath, png.EncodeToPNG());

            var saved = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Assert.IsTrue(saved.LoadImage(File.ReadAllBytes(pngPath)), "saved png loads " + label);
            Assert.Greater(new FileInfo(pngPath).Length, 50000, "saved png has real content " + label);

            // Watermark probes only: the very bottom edge of the UI is a
            // uniform strip, while engine watermarks paint glyphs against that
            // edge. Two assumptions of the original probe had to become
            // explicit for the dark Preparation terminal:
            //   * it measures pixels that STAND OUT from the strip's own
            //     background instead of "dark pixels" - on a dark page a fixed
            //     dark threshold reports the page's own background as a mark;
            //   * the strip is 12px, the same band the bottom-right probe
            //     always used. The 22px band reached ABOVE the tab label
            //     baseline at 1280x720, so the page's own navigation text was
            //     reported as a watermark at that one scale.
            // Texture2D row 0 is the screen bottom.
            int bottomStripMarks = CountStandoutPixels(saved, 0, size.x, 0, 12);
            int rightBottomMarks = CountStandoutPixels(saved, (int)(size.x * 0.62f), size.x, 0, 12);

            Check(report, ref failures, label + " no watermark bottom strip",
                bottomStripMarks == 0);
            Check(report, ref failures, label + " no watermark bottom-right",
                rightBottomMarks == 0);

            // Geometric sanity: three columns do not overlap, mission head and
            // confirm action sit inside the root, confirm above bottom nav.
            Check(report, ref failures, label + " columns/layout no overlap",
                GeometryIsSound(app));

            if (size.x == 1920 && size.y == 1080)
            {
                // The 1920 file above is already the primary artifact.
            }

            Object.Destroy(png);
            Object.Destroy(saved);
            yield return null;
        }

        // Canonical name at the export root for the review screenshot.
        string canonical = @"D:\dsh_gpt_unity_blender\export\surviveui_phase03_preparation_1920x1080.png";
        if (File.Exists(primaryPng))
        {
            File.Copy(primaryPng, canonical, true);
        }

        report.AppendLine(failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(OutDir, ReportName), report.ToString());

        Assert.AreEqual(0, failures, "clean shot checks failed");
    }

    private static bool GeometryIsSound(AppController app)
    {
        VisualElement root = app.UiRoot;
        if (root == null)
        {
            return false;
        }

        Rect rootRect = Bounds(root);
        VisualElement env = root.Q<VisualElement>(ElementNames.EnvironmentPanel);
        VisualElement equipment = root.Q<VisualElement>(ElementNames.EquipmentPanel);
        VisualElement readiness = root.Q<VisualElement>(ElementNames.ReadinessPanel);
        VisualElement confirm = root.Q<Button>(ElementNames.ConfirmLoadoutButton);
        VisualElement nav = root.Q<VisualElement>(ElementNames.BottomNav);

        if (env == null || equipment == null || readiness == null || confirm == null || nav == null)
        {
            return false;
        }

        Rect envRect = Bounds(env);
        Rect equipmentRect = Bounds(equipment);
        Rect readinessRect = Bounds(readiness);
        Rect confirmRect = Bounds(confirm);
        Rect navRect = Bounds(nav);

        bool columnsClear = equipmentRect.xMin >= envRect.xMax - 1f
                            && readinessRect.xMin >= equipmentRect.xMax - 1f;
        bool confirmVisible = confirmRect.xMin >= rootRect.xMin
                              && confirmRect.xMax <= rootRect.xMax
                              && confirmRect.yMax <= navRect.yMin + 1f;
        return columnsClear && confirmVisible;
    }

    private static Rect Bounds(VisualElement element)
    {
        return element.worldBound;
    }

    private static void Check(System.Text.StringBuilder report, ref int failures, string label, bool condition)
    {
        report.AppendLine((condition ? "PASS" : "FAIL") + " :: " + label);
        if (!condition)
        {
            failures++;
        }
    }

    private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = MaxWaitFrames)
    {
        int frames = 0;
        while (!condition() && frames < maxFrames)
        {
            frames++;
            yield return null;
        }
    }

    /// <summary>
    /// Pixels of a UI strip that are NOT the strip's own background - the
    /// theme-independent form of the old "dark pixel" watermark probe. The
    /// reference luminance is the strip's median, so the same check works on
    /// the light pages and on the dark Preparation terminal.
    /// </summary>
    private static int CountStandoutPixels(Texture2D texture, int x0, int x1, int y0, int y1)
    {
        if (y1 > texture.height)
        {
            y1 = texture.height;
        }

        x0 = Mathf.Max(0, x0);
        x1 = Mathf.Min(texture.width, x1);
        y0 = Mathf.Max(0, y0);

        var luminance = new System.Collections.Generic.List<float>();
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                Color c = texture.GetPixel(x, y);
                luminance.Add(0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
            }
        }

        if (luminance.Count == 0)
        {
            return 0;
        }

        luminance.Sort();
        float background = luminance[luminance.Count / 2];

        int count = 0;
        foreach (float value in luminance)
        {
            if (Mathf.Abs(value - background) > 0.12f)
            {
                count++;
            }
        }

        return count;
    }
}
