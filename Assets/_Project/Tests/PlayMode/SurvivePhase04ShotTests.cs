using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 04 clean Chinese screenshots (validation/asset pipeline only).
///
/// Editor-panel offscreen rendering (same UI/UUSS/PanelSettings as runtime,
/// no player watermark). Produces Overview / Preparation / Processing at
/// 1920x1080 (canonical), 1600x900 and 1280x720, plus watermark/debug probes
/// and a light no-overlap geometry check per screen.
///
/// Captured states:
/// - Overview: default GameSession state;
/// - Preparation: all modules carried, loadout CONFIRMED;
/// - Processing: batch 5 running at ~50% progress (deterministic advance).
/// Run isolated via: -testFilter SurvivePhase04ShotTests
/// </summary>
public class SurvivePhase04ShotTests
{
    private const string OutDir = @"D:\dsh_gpt_unity_blender\export\surviveui_phase04_shot_clean";
    private const string ReportName = "clean_shot_report.txt";

    private static readonly Vector2Int[] Sizes =
    {
        new Vector2Int(1920, 1080),
        new Vector2Int(1600, 900),
        new Vector2Int(1280, 720)
    };

    private const int MaxWaitFrames = 600;

    private int _failures;
    private readonly System.Text.StringBuilder _report = new System.Text.StringBuilder();

    [UnityTest]
    public IEnumerator CleanChineseScreens_ThreeResolutions()
    {
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

        UIDocument document = app.Document;
        PanelSettings panel = document.panelSettings;
        Assert.IsNotNull(panel, "PanelSettings available");
        for (int i = 0; i < 8; i++)
        {
            yield return null;
        }

        Directory.CreateDirectory(OutDir);
        _report.Clear();
        _failures = 0;

        // One deterministic state snapshot per screen, then render each size.
        yield return CaptureScreen(app, panel, "overview", () =>
        {
            GameSession.Instance.ResetSession();
            app.ShowScreen(AppScreen.Overview);
            return GeometryOk(app, "overview");
        });

        yield return CaptureScreen(app, panel, "preparation", () =>
        {
            GameSession session = GameSession.Instance;
            session.ResetSession();
            session.TrySetBatterySelected(true);
            session.TrySetDroneSelected(true);
            session.TryConfirmLoadout();
            app.ShowScreen(AppScreen.Preparation);
            return GeometryOk(app, "preparation");
        });

        yield return CaptureScreen(app, panel, "processing", () =>
        {
            // Ready state: batch 5 selected by default, storage 20, expected
            // output visible, CTA 开始加工 enabled (deterministic, readable).
            GameSession.Instance.ResetSession();
            app.ShowScreen(AppScreen.Processing);
            return GeometryOk(app, "processing");
        });

        // Canonical names at the export root for the review screenshots.
        foreach (string screen in new[] { "overview", "preparation", "processing" })
        {
            string src = Path.Combine(OutDir, "surviveui_phase04_" + screen + "_1920x1080.png");
            string dst = Path.Combine(@"D:\dsh_gpt_unity_blender\export",
                "surviveui_phase04_" + screen + "_1920x1080.png");
            if (File.Exists(src))
            {
                File.Copy(src, dst, true);
            }
        }

        _report.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(OutDir, ReportName), _report.ToString());
        Assert.AreEqual(0, _failures, "clean Chinese screenshot checks failed");
    }

    private IEnumerator CaptureScreen(AppController app, PanelSettings panel,
        string screenName, System.Func<bool> prepare)
    {
        bool prepared = prepare();
        Assert.IsTrue(prepared, "screen state prepared: " + screenName);
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }

        foreach (Vector2Int size in Sizes)
        {
            string label = screenName + " " + size.x + "x" + size.y;
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
                "surviveui_phase04_" + screenName + "_" + size.x + "x" + size.y + ".png");
            File.WriteAllBytes(pngPath, png.EncodeToPNG());

            var saved = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Assert.IsTrue(saved.LoadImage(File.ReadAllBytes(pngPath)), "saved png loads " + label);
            Assert.Greater(new FileInfo(pngPath).Length, 50000, "saved png has real content " + label);

            // Texture row 0 is the screen bottom (watermark strip).
            int bottomStripDark = CountDarkPixels(saved, 0, size.x, 0, 22);
            int rightBottomDark = CountDarkPixels(saved, (int)(size.x * 0.62f), size.x, 0, 12);

            VisualElement debugPanel = app.UiRoot.Q<VisualElement>(ElementNames.DebugPanel);
            bool debugHidden = debugPanel != null
                               && debugPanel.resolvedStyle.display == DisplayStyle.None;

            Check(label + " no watermark bottom strip", bottomStripDark == 0);
            Check(label + " no watermark bottom-right", rightBottomDark == 0);
            Check(label + " debug hidden", debugHidden);
            Check(label + " layout no overlap", GeometryOk(app, screenName));

            Object.Destroy(png);
            Object.Destroy(saved);
            yield return null;
        }
    }

    private static bool GeometryOk(AppController app, string screen)
    {
        VisualElement root = app.UiRoot;
        if (root == null)
        {
            return false;
        }

        Rect rootRect = Bounds(root);
        VisualElement nav = root.Q<VisualElement>(ElementNames.BottomNav);
        if (nav == null)
        {
            return false;
        }

        Rect navRect = Bounds(nav);

        if (screen == "overview")
        {
            VisualElement ship = root.Q<VisualElement>(ElementNames.ShipStatusPanel);
            VisualElement objective = root.Q<VisualElement>(ElementNames.ObjectivePanel);
            VisualElement cta = root.Q<Button>(ElementNames.OpenPreparationButton);
            if (ship == null || objective == null || cta == null)
            {
                return false;
            }

            Rect ctaRect = Bounds(cta);
            return Bounds(ship).xMax <= Bounds(objective).xMin
                   && ctaRect.xMin >= rootRect.xMin && ctaRect.xMax <= rootRect.xMax
                   && ctaRect.yMax <= navRect.yMin + 1f;
        }

        if (screen == "preparation")
        {
            VisualElement env = root.Q<VisualElement>(ElementNames.EnvironmentPanel);
            VisualElement equipment = root.Q<VisualElement>(ElementNames.EquipmentPanel);
            VisualElement readiness = root.Q<VisualElement>(ElementNames.ReadinessPanel);
            VisualElement confirm = root.Q<Button>(ElementNames.ConfirmLoadoutButton);
            if (env == null || equipment == null || readiness == null || confirm == null)
            {
                return false;
            }

            Rect confirmRect = Bounds(confirm);
            return Bounds(equipment).xMin >= Bounds(env).xMax - 1f
                   && Bounds(readiness).xMin >= Bounds(equipment).xMax - 1f
                   && confirmRect.xMin >= rootRect.xMin && confirmRect.xMax <= rootRect.xMax
                   && confirmRect.yMax <= navRect.yMin + 1f;
        }

        if (screen == "processing")
        {
            VisualElement storage = root.Q<VisualElement>(ElementNames.ProcessingStoragePanel);
            VisualElement core = root.Q<VisualElement>(ElementNames.ProcessingCorePanel);
            VisualElement inventory = root.Q<VisualElement>(ElementNames.ProcessingInventoryPanel);
            VisualElement start = root.Q<Button>(ElementNames.ProcessingStartButton);
            if (storage == null || core == null || inventory == null || start == null)
            {
                return false;
            }

            Rect startRect = Bounds(start);
            return Bounds(core).xMin >= Bounds(storage).xMax - 1f
                   && Bounds(inventory).xMin >= Bounds(core).xMax - 1f
                   && startRect.xMin >= rootRect.xMin && startRect.xMax <= rootRect.xMax
                   && startRect.yMax <= navRect.yMin + 1f;
        }

        return false;
    }

    private static Rect Bounds(VisualElement element)
    {
        return element.worldBound;
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
