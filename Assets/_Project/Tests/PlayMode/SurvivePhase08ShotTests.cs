using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 08 Main Menu (REVISED composition) — UI layout validation at the
/// competition resolutions.
///
/// The panel is rendered offscreen at 1920x1080, 2560x1440 and 3440x1440 and the
/// same geometry audit runs on each capture, so a clipped title, an overflowing
/// CTA, a menu column outside the frame or a modal taller than the screen fails
/// the suite instead of shipping a bad image.
///
/// IMPORTANT (revision): these captures show the UI DOCUMENT ONLY. The hero ship
/// and the starfield are 3D and are composited by the stage camera in the real
/// frame, so the human-facing screenshots come from the runtime capture probe
/// (-menuCaptureDir). What this suite proves for the revision is exactly what an
/// offscreen panel capture can prove: the UI paints the RIGHT column and the
/// hairlines, and it paints NOTHING over the LEFT hero window.
///
/// Run isolated via: -testFilter SurvivePhase08ShotTests
/// </summary>
public class SurvivePhase08ShotTests
{
    private const string OutDir = @"D:\dsh_gpt_unity_blender\export\surviveui_phase08_shots";
    private const string ReportName = "shot_report.txt";
    private const int MaxWaitFrames = 900;

    private static readonly Vector2Int[] Sizes =
    {
        new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1440),
        new Vector2Int(3440, 1440)
    };

    private int _failures;
    private float _logicalTitleHeight = -1f;
    private readonly System.Text.StringBuilder _report = new System.Text.StringBuilder();

    [UnityTest]
    public IEnumerator MainMenu_ThreeResolutions_CleanShots()
    {
        MainMenuSettings.ResetToDefaults();

        SceneManager.LoadScene("00_Boot");
        yield return WaitUntil(() => SceneManager.GetActiveScene().name
                                    == SceneRouting.MainMenuSceneName);

        MainMenuController menu = null;
        yield return WaitUntil(() =>
        {
            menu = Object.FindAnyObjectByType<MainMenuController>();
            return menu != null && menu.IsReady;
        });

        Assert.IsNotNull(menu, "MainMenuController not ready");
        UIDocument document = menu.Document;
        PanelSettings panel = document != null ? document.panelSettings : null;
        Assert.IsNotNull(panel, "PanelSettings available");
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Directory.CreateDirectory(OutDir);
        _report.Clear();
        _failures = 0;

        yield return CaptureAllSizes(menu, panel, "main_menu", null);

        // One settings-modal state is worth keeping for the review pack.
        yield return CaptureAllSizes(menu, panel, "settings", () =>
        {
            UiToolkitPointerSimulator.Click(
                menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton));
        }, waitForModal: true);

        _report.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(OutDir, ReportName), _report.ToString());
        Assert.AreEqual(0, _failures, "Phase 08 screenshot/layout checks failed");
    }

    private IEnumerator CaptureAllSizes(MainMenuController menu, PanelSettings panel,
        string stateName, System.Action prepare, bool waitForModal = false)
    {
        if (prepare != null)
        {
            prepare();
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            if (waitForModal)
            {
                yield return WaitUntil(() => menu.IsSettingsOpen, 60);
                Assert.IsTrue(menu.IsSettingsOpen, "settings modal open for the screenshot");
            }
        }

        foreach (Vector2Int size in Sizes)
        {
            string label = stateName + " " + size.x + "x" + size.y;
            Texture2D png = null;
            var renderTexture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
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

            Assert.IsNotNull(png, "readback produced for " + label);

            string pngPath = Path.Combine(OutDir,
                "Phase08_" + stateName + "_" + size.x + "x" + size.y + ".png");
            File.WriteAllBytes(pngPath, png.EncodeToPNG());

            var saved = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Assert.IsTrue(saved.LoadImage(File.ReadAllBytes(pngPath)), "saved png loads " + label);
            Check(label + " png has real content", new FileInfo(pngPath).Length > 4000);

            // Revision contract: the document paints the RIGHT column and leaves
            // the LEFT hero window empty (the 3D ship shows through there). The
            // 系统设置 state is exempt: its full-frame dim overlay is the modal's
            // own background, not menu chrome.
            Check(label + " right menu column really painted",
                CountLitPixels(saved, (int)(size.x * 0.66f), size.x,
                    (int)(size.y * 0.2f), (int)(size.y * 0.8f)) > 200);
            if (stateName == "main_menu")
            {
                Check(label + " left hero window left unpainted for the 3D ship",
                    CountLitPixels(saved, 0, (int)(size.x * 0.5f),
                        (int)(size.y * 0.1f), (int)(size.y * 0.9f)) < 400);
            }

            Check(label + " no development watermark strip",
                CountLitPixels(saved, 0, size.x, 0, 16) == 0);

            VisualElement debugPanel = menu.UiRoot.Q<VisualElement>("DebugPanel");
            Check(label + " no debug panel in the tree", debugPanel == null);

            Check(label + " layout audit", AuditLayout(menu, stateName, size));

            Object.Destroy(png);
            Object.Destroy(saved);
            yield return null;
        }
    }

    /// <summary>
    /// Composition audit at the captured resolution: nothing clipped, nothing
    /// overlapping, the menu column on the right, the hero window on the left,
    /// and the modal inside the frame when shown.
    /// </summary>
    private bool AuditLayout(MainMenuController menu, string stateName, Vector2Int size)
    {
        VisualElement root = menu.UiRoot;
        Rect frame = root.worldBound;
        VisualElement title = root.Q<Label>(MainMenuNames.Title);
        VisualElement subtitle = root.Q<Label>(MainMenuNames.Subtitle);
        VisualElement column = root.Q<VisualElement>(MainMenuNames.ContentColumn);
        VisualElement hero = root.Q<VisualElement>(MainMenuNames.HeroColumn);
        VisualElement cta = menu.EnterMissionButton;
        VisualElement nav = root.Q<VisualElement>(MainMenuNames.SecondaryNav);
        VisualElement exitRow = root.Q<Button>(MainMenuNames.ExitButton);
        VisualElement stage = root.Q<VisualElement>(MainMenuNames.HeroStage);
        VisualElement telemetry = root.Q<VisualElement>(MainMenuNames.HeroTelemetry);
        VisualElement topBar = root.Q<VisualElement>(MainMenuNames.TopBar);
        VisualElement footer = root.Q<VisualElement>(MainMenuNames.Footer);

        if (title == null || subtitle == null || column == null || hero == null || cta == null
            || nav == null || stage == null || telemetry == null || topBar == null
            || footer == null || exitRow == null)
        {
            _report.AppendLine("audit missing element @ " + size);
            return false;
        }

        bool ok = true;

        void Require(bool condition, string what)
        {
            if (!condition)
            {
                ok = false;
                _report.AppendLine("  detail: " + what);
            }
        }

        // Safe area: the title block, the CTA and the secondary navigation stay
        // clear of the screen edges and inside the frame.
        const float safeMargin = 24f;
        Require(Inside(frame, title.worldBound, safeMargin), "title outside safe area: " + title.worldBound);
        Require(Inside(frame, subtitle.worldBound, safeMargin), "subtitle outside safe area");
        Require(Inside(frame, cta.worldBound, safeMargin), "CTA outside safe area: " + cta.worldBound);
        Require(Inside(frame, nav.worldBound, safeMargin), "secondary nav outside safe area");
        Require(Inside(frame, hero.worldBound, 0f), "hero window outside the frame");
        Require(Inside(frame, stage.worldBound, 0f), "hero stage outside the frame");
        Require(Inside(frame, telemetry.worldBound, 0f), "hero telemetry outside the frame");

        // Revision composition: LEFT window / RIGHT menu, never overlapping.
        Require(hero.worldBound.xMax <= column.worldBound.xMin + 1f,
            "hero window overlaps the menu column");
        Require(column.worldBound.xMin > frame.xMin + frame.width * 0.55f,
            "menu column is not in the right part of the frame: " + column.worldBound);
        Require(hero.worldBound.width > frame.width * 0.5f,
            "hero window is not the dominant left area: " + hero.worldBound.width);

        // Nothing overlaps the primary action.
        Require(cta.worldBound.yMax <= exitRow.worldBound.yMin + 1f, "CTA overlaps the secondary nav");
        Require(title.worldBound.yMin >= topBar.worldBound.yMax - 1f, "title overlaps the top bar");
        Require(nav.worldBound.yMax <= footer.worldBound.yMin + 1f, "secondary nav overlaps the footer");
        Require(title.worldBound.yMax <= cta.worldBound.yMin + 1f, "title overlaps the CTA");

        // The CTA must stay the single dominant action: tallest, filled with the
        // primary treatment and carrying the largest label (style guide §14/§24).
        VisualElement settings = menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton);
        if (settings != null)
        {
            Require(cta.worldBound.height >= settings.worldBound.height * 1.5f,
                "CTA is not the tallest action: " + cta.worldBound.height
                + " vs " + settings.worldBound.height);

            Color ctaFill = cta.resolvedStyle.backgroundColor;
            Color rowFill = settings.resolvedStyle.backgroundColor;
            Require(ctaFill.a > 0.9f && rowFill.a < 0.1f,
                "CTA does not carry the primary fill treatment (cta a=" + ctaFill.a
                + " row a=" + rowFill.a + ")");

            Label ctaLabel = cta.Q<Label>(MainMenuNames.EnterMissionLabel);
            Label rowLabel = settings.Q<Label>(className: "menu-secondary-label");
            Require(ctaLabel != null && rowLabel != null
                    && ctaLabel.resolvedStyle.fontSize > rowLabel.resolvedStyle.fontSize * 1.3f,
                "CTA label is not visually dominant");
        }

        // Composition invariance: the top bar gives the panel scale, so the
        // title's LOGICAL height must be identical at every resolution.
        float scale = topBar.worldBound.height / 46f;
        float logicalTitleHeight = title.worldBound.height / Mathf.Max(scale, 0.001f);
        if (_logicalTitleHeight < 0f)
        {
            _logicalTitleHeight = logicalTitleHeight;
        }
        else
        {
            Require(Mathf.Abs(logicalTitleHeight - _logicalTitleHeight) < 2f,
                "logical layout changed with resolution: " + logicalTitleHeight
                + " vs " + _logicalTitleHeight + " at scale " + scale);
        }

        // Ultrawide must recompose, not stretch: the panel keeps the 1920x1080
        // logical reference and divides it between a wide hero window and a
        // readable menu column.
        if (size.x == 3440)
        {
            Require(frame.width > 2100f,
                "ultrawide logical width should exceed the 1920 reference: " + frame.width);
            Require(hero.worldBound.width > frame.width * 0.5f,
                "ultrawide hero window too narrow: " + hero.worldBound.width);
            Require(column.worldBound.width > frame.width * 0.2f,
                "ultrawide menu column too narrow: " + column.worldBound.width);
            Require(cta.worldBound.width > 200f,
                "ultrawide CTA collapsed: " + cta.worldBound.width);
            Require(column.worldBound.xMax <= frame.xMax + 0.5f,
                "ultrawide menu column escapes the frame");
        }

        if (stateName == "settings")
        {
            VisualElement panel = menu.UiRoot.Q<VisualElement>(MainMenuNames.SettingsPanel);
            Require(panel != null && Inside(frame, panel.worldBound, 0f),
                "settings panel outside the frame");
            Require(panel != null && panel.worldBound.height < frame.height,
                "settings panel taller than the frame");
        }

        return ok;
    }

    private static bool Inside(Rect frame, Rect element, float margin)
    {
        return element.xMin >= frame.xMin + margin - 0.5f
               && element.xMax <= frame.xMax - margin + 0.5f
               && element.yMin >= frame.yMin + margin - 0.5f
               && element.yMax <= frame.yMax - margin + 0.5f;
    }

    /// <summary>Pixels above the "something was drawn here" threshold.</summary>
    private static int CountLitPixels(Texture2D texture, int x0, int x1, int y0, int y1)
    {
        x0 = Mathf.Max(0, x0);
        y0 = Mathf.Max(0, y0);
        x1 = Mathf.Min(texture.width, x1);
        y1 = Mathf.Min(texture.height, y1);

        int count = 0;
        for (int y = y0; y < y1; y += 4)
        {
            for (int x = x0; x < x1; x += 4)
            {
                Color c = texture.GetPixel(x, y);
                float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                if (lum > 0.16f)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private void Check(string label, bool condition)
    {
        _report.AppendLine((condition ? "PASS" : "FAIL") + " :: " + label);
        if (!condition)
        {
            _failures++;
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
}
