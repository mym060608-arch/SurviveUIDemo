using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 02 clean review screenshot (validation/asset pipeline only).
///
/// The Windows Standalone player on this machine renders an engine-level
/// "trialVersion" watermark on the final backbuffer even though the project
/// and build data contain no such text (known engine-side behaviour, see
/// Unity IssueTracker "trial version watermark visible on builds using
/// non-trial licenses"). That watermark is drawn by the player presentation
/// layer, so it cannot be removed from the project without cropping,
/// covering or post-editing - all forbidden for the review screenshot.
///
/// This test therefore renders the SAME Overview UI (same MainApp.uxml,
/// AppTheme.uss, OverviewScreen.uss and HubPanelSettings) offscreen through
/// the Editor-only UI Toolkit path (PanelSettings.targetTexture) at exactly
/// 1920x1080, default GameSession state, Debug UI hidden. The Editor panel
/// path never passes through the player watermark layer, so the output PNG is
/// clean without any post-processing.
///
/// Run isolated via: -testFilter SurvivePhase02ShotTests
/// </summary>
public class SurvivePhase02ShotTests
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const string OutDir = @"D:\dsh_gpt_unity_blender\export\surviveui_phase02_shot_clean";
    private const string PngName = "surviveui_phase02_overview_1920x1080.png";
    private const string ReportName = "clean_shot_report.txt";

    private const int MaxWaitFrames = 600;

    [UnityTest]
    public IEnumerator CleanOverview1920_RenderEditorPanel_NoWatermark()
    {
        // Deterministic starting state: fresh defaults, Overview first screen.
        GameSession.Ensure().ResetSession();
        SceneManager.LoadScene("00_Boot");
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == BootController.HubSceneName);

        AppController app = null;
        yield return WaitUntil(() =>
        {
            app = Object.FindAnyObjectByType<AppController>();
            return app != null && app.IsReady;
        });

        Assert.IsNotNull(app, "AppController not ready");
        app.ShowScreen(AppScreen.Overview);
        Assert.AreEqual(1, app.VisibleScreenCount, "exactly one visible screen");

        UIDocument document = app.Document;
        Assert.IsNotNull(document, "UIDocument available");
        PanelSettings panel = document.panelSettings;
        Assert.IsNotNull(panel, "PanelSettings available");

        // Let the panel complete several layout/render passes.
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        // Offscreen render target; Editor-only rendering path (no player
        // backbuffer, therefore no engine watermark layer).
        Texture2D png = null;
        var renderTexture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
        renderTexture.name = "OverviewCleanShot";
        renderTexture.Create();

        RenderTexture previousTarget = panel.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        panel.targetTexture = renderTexture;

        try
        {
            // Allow the runtime panel to repaint into the texture.
            for (int i = 0; i < 12; i++)
            {
                yield return null;
            }

            RenderTexture.active = renderTexture;
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            readback.Apply();
            png = readback;
        }
        finally
        {
            RenderTexture.active = previousActive;
            panel.targetTexture = previousTarget;
            renderTexture.Release();
        }

        Assert.IsNotNull(png, "panel readback texture produced");

        Directory.CreateDirectory(OutDir);
        string pngPath = Path.Combine(OutDir, PngName);
        byte[] bytes = png.EncodeToPNG();
        File.WriteAllBytes(pngPath, bytes);

        // Re-read the saved file and verify cleanliness on the real artifact.
        var saved = new Texture2D(2, 2, TextureFormat.RGB24, false);
        Assert.IsTrue(saved.LoadImage(File.ReadAllBytes(pngPath)), "saved png loads");
        Assert.Greater(new FileInfo(pngPath).Length, 100000, "saved png has real content");

        // Texture2D row 0 is the framebuffer BOTTOM, so screen-bottom bands are
        // texture rows near 0 (PNG files are stored top-down; probes on the
        // saved file therefore use y near Height).
        int bottomStripDark = CountDarkPixels(saved, 0, Width, 0, 22);
        int rightBottomDark = CountDarkPixels(saved, (int)(Width * 0.62f), Width, 0, 12);

        // Debug panel zone (hidden by default): screen top-down y 940..990
        // maps to texture rows 89..140.
        int debugAreaDark = CountDarkPixels(saved, 10, 200, 89, 140);

        // Engine watermark text sits against the very bottom edge; the UI's
        // lowest text (navigation labels) ends around screen y = Height - 34,
        // i.e. texture row ~34.
        Assert.AreEqual(0, bottomStripDark, "no watermark glyphs in bottom strip");
        Assert.AreEqual(0, rightBottomDark, "no watermark glyphs in bottom-right corner");
        Assert.AreEqual(0, debugAreaDark, "no debug panel in the capture");

        string report = "PASS :: clean shot rendered 1920x1080\n"
                        + "PASS :: no watermark glyphs (bottom strip dark px = " + bottomStripDark + ")\n"
                        + "PASS :: no watermark glyphs (bottom-right dark px = " + rightBottomDark + ")\n"
                        + "PASS :: no debug panel (debug area dark px = " + debugAreaDark + ")\n"
                        + "file: " + pngPath + "\n"
                        + "RESULT: SUCCESS\n";
        File.WriteAllText(Path.Combine(OutDir, ReportName), report);

        Object.Destroy(png);
        Object.Destroy(saved);
        yield return null;
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

    private static int CountDarkPixels(Texture2D texture, int x0, int x1, int y0, int y1)
    {
        if (y1 > texture.height)
        {
            y1 = texture.height;
        }

        int count = 0;
        for (int y = Mathf.Max(0, y0); y < y1; y++)
        {
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(texture.width, x1); x++)
            {
                Color c = texture.GetPixel(x, y);
                float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                if (lum < 0.45f)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
