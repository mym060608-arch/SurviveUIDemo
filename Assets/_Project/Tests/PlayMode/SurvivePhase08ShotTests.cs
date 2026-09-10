using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 08 Main Menu clean screenshots + responsive layout validation.
///
/// Editor-panel offscreen rendering at the three competition resolutions
/// (1920x1080 hero, 2560x1440, 3440x1440 ultrawide). Every capture runs the same
/// geometry audit, so a clipped title, an overflowing CTA, an off-frame hero or
/// a modal outside the frame fails the suite instead of shipping a bad image.
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

        // One settings-modal state is worth keeping for the review pack (§81).
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
            Check(label + " png has real content", new FileInfo(pngPath).Length > 40000);

            // The menu canvas is light (#E9EBED): a capture that is mostly dark
            // means the UI never rendered (or the camera clear color leaked).
            Check(label + " canvas is the light menu background", IsLightCanvas(saved));
            Check(label + " no development watermark strip", CountDarkPixels(saved, 0, size.x, 0, 20) == 0);

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
    /// overlapping, the hero framed, and the modal inside the frame when shown.
    /// </summary>
    private bool AuditLayout(MainMenuController menu, string stateName, Vector2Int size)
    {
        VisualElement root = menu.UiRoot;
        Rect frame = root.worldBound;
        VisualElement title = root.Q<Label>(MainMenuNames.Title);
        VisualElement subtitle = root.Q<Label>(MainMenuNames.Subtitle);
        VisualElement content = root.Q<VisualElement>(MainMenuNames.ContentColumn);
        VisualElement cta = menu.EnterMissionButton;
        VisualElement nav = root.Q<VisualElement>(MainMenuNames.SecondaryNav);
        VisualElement exitRow = root.Q<Button>(MainMenuNames.ExitButton);
        VisualElement disc = root.Q<VisualElement>(MainMenuNames.HeroPlanetDisc);
        VisualElement stage = root.Q<VisualElement>(MainMenuNames.HeroStage);
        VisualElement topBar = root.Q<VisualElement>(MainMenuNames.TopBar);
        VisualElement footer = root.Q<VisualElement>(MainMenuNames.Footer);

        if (title == null || subtitle == null || content == null || cta == null || nav == null
            || disc == null || stage == null || topBar == null || footer == null
            || exitRow == null)
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

        // Safe area: the brand block, the CTA and the secondary navigation stay
        // clear of the screen edges and inside the frame.
        const float safeMargin = 24f;
        Require(Inside(frame, title.worldBound, safeMargin), "title outside safe area: " + title.worldBound);
        Require(Inside(frame, subtitle.worldBound, safeMargin), "subtitle outside safe area");
        Require(Inside(frame, cta.worldBound, safeMargin), "CTA outside safe area: " + cta.worldBound);
        Require(Inside(frame, nav.worldBound, safeMargin), "secondary nav outside safe area");
        Require(Inside(frame, disc.worldBound, 0f), "hero planet outside the frame: " + disc.worldBound);
        Require(Inside(frame, stage.worldBound, 0f), "hero stage outside the frame");

        // Nothing overlaps the primary action.
        Require(cta.worldBound.yMax <= exitRow.worldBound.yMin + 1f, "CTA overlaps the secondary nav");
        Require(content.worldBound.xMax <= disc.worldBound.xMin + 1f,
            "navigation column overlaps the hero visual");
        Require(title.worldBound.yMin >= topBar.worldBound.yMax - 1f, "title overlaps the top bar");
        Require(nav.worldBound.yMax <= footer.worldBound.yMin + 1f, "secondary nav overlaps the footer");

        // The hero must dominate: at least ~30% of the frame height.
        Require(disc.worldBound.height >= frame.height * 0.30f,
            "hero planet too small: " + disc.worldBound.height + " / " + frame.height);

        // The CTA must stay the single dominant action: tallest, filled with the
        // primary ink treatment and carrying the largest label (§14 / §24).
        VisualElement settings = menu.UiRoot.Q<Button>(MainMenuNames.SettingsButton);
        if (settings != null)
        {
            Require(cta.worldBound.height >= settings.worldBound.height * 1.8f,
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

        // Composition invariance: the 64 px top bar gives the panel scale, so the
        // title's LOGICAL height must be identical at every resolution. The
        // composition is never simply scaled up (2560x1440) or stretched (3440).
        float scale = topBar.worldBound.height / 64f;
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

        // Ultrawide must recompose, not stretch: the composition frame is capped
        // and centred, so both margins stay substantial.
        if (size.x == 3440)
        {
            float leftMargin = content.worldBound.xMin - frame.xMin;
            float rightMargin = frame.xMax - disc.worldBound.xMax;
            Require(leftMargin > 60f && rightMargin > 40f,
                "ultrawide margins collapsed: left=" + leftMargin + " right=" + rightMargin);
            Require(frame.width > 2100f,
                "ultrawide logical width should exceed the 1920 reference: " + frame.width);
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

    /// <summary>True when the frame is dominated by the light menu canvas.</summary>
    private static bool IsLightCanvas(Texture2D texture)
    {
        int light = 0;
        int samples = 0;
        for (int y = 0; y < texture.height; y += 24)
        {
            for (int x = 0; x < texture.width; x += 24)
            {
                Color c = texture.GetPixel(x, y);
                float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                samples++;
                if (lum > 0.55f)
                {
                    light++;
                }
            }
        }

        return samples > 0 && light >= samples * 0.6f;
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
