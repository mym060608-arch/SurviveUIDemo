using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 08 Revision 2 — title screen UI validation at the competition
/// resolutions (1920x1080, 2560x1440, 3440x1440).
///
/// The panel is rendered offscreen and the same geometry audit runs on every
/// capture, so a clipped 活下去, an option that leaves the frame, a menu column
/// drifting to the middle of the screen or a modal taller than the display fails
/// the suite instead of shipping a bad image.
///
/// IMPORTANT: these captures show the UI DOCUMENT ONLY. The hero ship and the
/// starfield are 3D and are composited by the stage camera in the real frame, so
/// the human-facing screenshots come from the runtime capture probe
/// (-menuCaptureDir). What this suite proves is exactly what an offscreen panel
/// capture can prove: the document paints the RIGHT menu column and paints
/// NOTHING over the LEFT hero window.
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
    public IEnumerator TitleScreen_ThreeResolutions_CleanShots()
    {
        MainMenuSettings.ResetToDefaults();
        InputDeviceTracker.ResetToDefaults();

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

        yield return CaptureAllSizes(menu, panel, "title_screen", null);

        // 选项 is the only modal the title screen still has.
        yield return CaptureAllSizes(menu, panel, "options", () =>
        {
            UiToolkitPointerSimulator.Click(
                menu.UiRoot.Q<Button>(MainMenuNames.OptionsButton));
        }, waitForModal: true);

        _report.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(OutDir, ReportName), _report.ToString());
        Assert.AreEqual(0, _failures, "Phase 08 Revision 2 screenshot/layout checks failed");
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
                yield return WaitUntil(() => menu.IsOptionsOpen, 60);
                Assert.IsTrue(menu.IsOptionsOpen, "选项 open for the screenshot");
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
                "Phase08R2_" + stateName + "_" + size.x + "x" + size.y + ".png");
            File.WriteAllBytes(pngPath, png.EncodeToPNG());

            var saved = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Assert.IsTrue(saved.LoadImage(File.ReadAllBytes(pngPath)), "saved png loads " + label);
            Check(label + " png has real content", new FileInfo(pngPath).Length > 4000);

            // The document paints the RIGHT column and leaves the LEFT hero
            // window empty (the 3D stage shows through there). The 选项 state is
            // exempt: its full-frame dim overlay is the modal's own background.
            Check(label + " right menu column really painted",
                CountLitPixels(saved, (int)(size.x * 0.66f), size.x,
                    (int)(size.y * 0.2f), (int)(size.y * 0.8f)) > 150);
            if (stateName == "title_screen")
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
    /// Composition audit at the captured resolution: the title and the four
    /// options stay on screen and clear of each other, the menu column keeps the
    /// right side and the hero keeps the left.
    /// </summary>
    private bool AuditLayout(MainMenuController menu, string stateName, Vector2Int size)
    {
        VisualElement root = menu.UiRoot;
        Rect frame = root.worldBound;
        Label title = root.Q<Label>(MainMenuNames.Title);
        VisualElement hero = root.Q<VisualElement>(MainMenuNames.HeroColumn);
        VisualElement column = root.Q<VisualElement>(MainMenuNames.ContentColumn);
        VisualElement list = root.Q<VisualElement>(MainMenuNames.MenuList);

        if (title == null || hero == null || column == null || list == null
            || menu.MenuItems.Count != 4)
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

        const float safeMargin = 24f;
        Require(Inside(frame, title.worldBound, safeMargin),
            "title outside safe area: " + title.worldBound);
        Require(Inside(frame, list.worldBound, safeMargin), "menu list outside safe area");
        Require(Inside(frame, hero.worldBound, 0f), "hero window outside the frame");
        Require(title.worldBound.height > 40f, "title too small: " + title.worldBound.height);

        // Revision 2 composition: LEFT window / RIGHT menu, never overlapping.
        Require(hero.worldBound.xMax <= column.worldBound.xMin + 1f,
            "hero window overlaps the menu column");
        Require(column.worldBound.xMin > frame.xMin + frame.width * 0.55f,
            "menu column is not in the right part of the frame: " + column.worldBound);
        Require(hero.worldBound.width > frame.width * 0.5f,
            "hero window is not the dominant left area: " + hero.worldBound.width);

        // The four options are stacked, all visible and never overlapping.
        Button previous = null;
        foreach (Button item in menu.MenuItems)
        {
            Require(Inside(frame, item.worldBound, safeMargin),
                "option outside safe area: " + item.name + " " + item.worldBound);
            Require(item.worldBound.height > 20f, "option too small: " + item.name);
            Require(item.worldBound.width > 100f, "option too narrow: " + item.name);
            if (previous != null)
            {
                Require(previous.worldBound.yMax <= item.worldBound.yMin + 1f,
                    "options overlap: " + previous.name + " / " + item.name);
            }

            previous = item;
        }

        Require(list.worldBound.yMin >= title.worldBound.yMax - 1f,
            "the options overlap the title");
        Require(title.worldBound.xMin >= column.worldBound.xMin - 1f,
            "the title is not aligned with the menu column");

        // Composition invariance: the panel keeps the 1920x1080 logical
        // reference, so the title's LOGICAL height must be identical at every
        // resolution. World bounds are already reported in panel units, which is
        // exactly what this check needs.
        float logicalTitleHeight = title.worldBound.height;
        if (_logicalTitleHeight < 0f)
        {
            _logicalTitleHeight = logicalTitleHeight;
        }
        else
        {
            Require(Mathf.Abs(logicalTitleHeight - _logicalTitleHeight) < 3f,
                "logical layout changed with resolution: " + logicalTitleHeight
                + " vs " + _logicalTitleHeight);
        }

        // Ultrawide must recompose, not stretch: the panel keeps its logical
        // reference, so the menu stays a readable column and the hero grows.
        if (size.x == 3440)
        {
            Require(frame.width > 2100f,
                "ultrawide logical width should exceed the 1920 reference: " + frame.width);
            Require(hero.worldBound.width > frame.width * 0.5f,
                "ultrawide hero window too narrow: " + hero.worldBound.width);
            Require(column.worldBound.width > frame.width * 0.2f,
                "ultrawide menu column too narrow: " + column.worldBound.width);
            Require(column.worldBound.xMax <= frame.xMax + 0.5f,
                "ultrawide menu column escapes the frame");
        }

        if (stateName == "options")
        {
            VisualElement modal = menu.UiRoot.Q<VisualElement>(MainMenuNames.OptionsPanel);
            Require(modal != null && Inside(frame, modal.worldBound, 0f),
                "options panel outside the frame");
            Require(modal != null && modal.worldBound.height < frame.height,
                "options panel taller than the frame");
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
