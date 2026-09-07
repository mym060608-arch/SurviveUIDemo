using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 05 clean Outpost screenshots (validation/asset pipeline only).
///
/// Editor-panel offscreen rendering. States:
/// A. outpost      : default (Alloy 5, all LV.1, Refinery selected);
/// B. outpost_upgraded : Refinery LV.2 (Alloy 2, duration 1.6s) shown in the
///                       topology/detail.
/// Each at 1920x1080, 1600x900 and 1280x720 with watermark/debug probes and a
/// light no-overlap geometry check.
/// Run isolated via: -testFilter SurvivePhase05ShotTests
/// </summary>
public class SurvivePhase05ShotTests
{
    private const string OutDir = @"D:\dsh_gpt_unity_blender\export\surviveui_phase05_shot_clean";
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
    public IEnumerator CleanOutpost_DefaultAndUpgraded_ThreeResolutions()
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

        yield return CaptureScreen(app, panel, "outpost", () =>
        {
            GameSession.Instance.ResetSession();
            app.OutpostView.SelectModule(GameSession.OutpostModule.Refinery);
            app.ShowScreen(AppScreen.Outpost);
            return GeometryOk(app);
        });

        yield return CaptureScreen(app, panel, "outpost_upgraded", () =>
        {
            GameSession session = GameSession.Instance;
            session.ResetSession();
            session.TryUpgrade(GameSession.OutpostModule.Refinery); // LV.2, alloy 2
            app.OutpostView.SelectModule(GameSession.OutpostModule.Refinery);
            app.ShowScreen(AppScreen.Outpost);
            return GeometryOk(app);
        });

        foreach (string screen in new[] { "outpost", "outpost_upgraded" })
        {
            string src = Path.Combine(OutDir, "surviveui_phase05_" + screen + "_1920x1080.png");
            string dst = Path.Combine(@"D:\dsh_gpt_unity_blender\export",
                "surviveui_phase05_" + screen + "_1920x1080.png");
            if (File.Exists(src))
            {
                File.Copy(src, dst, true);
            }
        }

        _report.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(OutDir, ReportName), _report.ToString());
        Assert.AreEqual(0, _failures, "clean Outpost screenshot checks failed");
    }

    private IEnumerator CaptureScreen(AppController app, PanelSettings panel,
        string screenName, System.Func<bool> prepare)
    {
        prepare();
        for (int i = 0; i < 5; i++)
        {
            yield return null;
        }

        bool geometryOk = GeometryOk(app);
        if (!geometryOk)
        {
            File.WriteAllText(Path.Combine(OutDir, "fail_geometry_" + screenName + ".txt"),
                GeometryDiagnostic(app));
        }

        Assert.IsTrue(geometryOk, "screen state prepared + layout sound: " + screenName);

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
                "surviveui_phase05_" + screenName + "_" + size.x + "x" + size.y + ".png");
            File.WriteAllBytes(pngPath, png.EncodeToPNG());

            var saved = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Assert.IsTrue(saved.LoadImage(File.ReadAllBytes(pngPath)), "saved png loads " + label);
            Assert.Greater(new FileInfo(pngPath).Length, 50000, "saved png has real content " + label);

            int bottomStripDark = CountDarkPixels(saved, 0, size.x, 0, 22);
            int rightBottomDark = CountDarkPixels(saved, (int)(size.x * 0.62f), size.x, 0, 12);
            VisualElement debugPanel = app.UiRoot.Q<VisualElement>(ElementNames.DebugPanel);
            bool debugHidden = debugPanel != null
                               && debugPanel.resolvedStyle.display == DisplayStyle.None;

            Check(label + " no watermark bottom strip", bottomStripDark == 0);
            Check(label + " no watermark bottom-right", rightBottomDark == 0);
            Check(label + " debug hidden", debugHidden);
            Check(label + " layout no overlap", GeometryOk(app));

            Object.Destroy(png);
            Object.Destroy(saved);
            yield return null;
        }
    }

    private static bool GeometryOk(AppController app)
    {
        VisualElement root = app.UiRoot;
        if (root == null)
        {
            return false;
        }

        Rect rootRect = Bounds(root);
        VisualElement status = root.Q<VisualElement>(ElementNames.OutpostStatusPanel);
        VisualElement topology = root.Q<VisualElement>(ElementNames.OutpostTopology);
        VisualElement detail = root.Q<VisualElement>(ElementNames.OutpostDetailPanel);
        VisualElement nav = root.Q<VisualElement>(ElementNames.BottomNav);
        VisualElement action = root.Q<Button>(ElementNames.OutpostUpgradeButton);
        VisualElement core = root.Q<Button>(ElementNames.CoreModuleNode);
        VisualElement harvester = root.Q<Button>(ElementNames.HarvesterModuleNode);
        VisualElement refinery = root.Q<Button>(ElementNames.RefineryModuleNode);

        if (status == null || topology == null || detail == null || nav == null || action == null
            || core == null || harvester == null || refinery == null)
        {
            return false;
        }

        Rect actionRect = Bounds(action);
        Rect navRect = Bounds(nav);

        bool columnsClear = Bounds(topology).xMin >= Bounds(status).xMax - 1f
                            && Bounds(detail).xMin >= Bounds(topology).xMax - 1f;
        bool nodesClear = Bounds(refinery).center.x > Bounds(harvester).center.x + 50f
                          && rootRect.Contains(Bounds(core).center);
        bool actionVisible = actionRect.xMin >= rootRect.xMin
                             && actionRect.xMax <= rootRect.xMax
                             && actionRect.yMax <= navRect.yMin + 1f;
        return columnsClear && nodesClear && actionVisible;
    }

    private static string GeometryDiagnostic(AppController app)
    {
        var sb = new System.Text.StringBuilder();
        VisualElement root = app.UiRoot;
        if (root == null)
        {
            return "root null";
        }

        sb.AppendLine("root " + Bounds(root));
        foreach (var pair in new[]
        {
            new[] { "status", ElementNames.OutpostStatusPanel },
            new[] { "topology", ElementNames.OutpostTopology },
            new[] { "detail", ElementNames.OutpostDetailPanel },
            new[] { "action", ElementNames.OutpostUpgradeButton },
            new[] { "harvester", ElementNames.HarvesterModuleNode },
            new[] { "refinery", ElementNames.RefineryModuleNode },
            new[] { "core", ElementNames.CoreModuleNode },
            new[] { "nav", ElementNames.BottomNav }
        })
        {
            VisualElement e = root.Q<VisualElement>(pair[1]);
            sb.AppendLine(pair[0] + " -> " + (e == null ? "null" : Bounds(e).ToString()));
        }

        Rect status = Bounds(root.Q<VisualElement>(ElementNames.OutpostStatusPanel));
        Rect topology = Bounds(root.Q<VisualElement>(ElementNames.OutpostTopology));
        Rect detail = Bounds(root.Q<VisualElement>(ElementNames.OutpostDetailPanel));
        Rect nav = Bounds(root.Q<VisualElement>(ElementNames.BottomNav));
        Rect action = Bounds(root.Q<Button>(ElementNames.OutpostUpgradeButton));
        Rect harvester = Bounds(root.Q<Button>(ElementNames.HarvesterModuleNode));
        Rect refinery = Bounds(root.Q<Button>(ElementNames.RefineryModuleNode));
        Rect core = Bounds(root.Q<Button>(ElementNames.CoreModuleNode));
        sb.AppendLine("colStatusTopo=" + (topology.xMin >= status.xMax - 1f));
        sb.AppendLine("colTopoDetail=" + (detail.xMin >= topology.xMax - 1f));
        sb.AppendLine("nodes=" + (refinery.center.x > harvester.center.x + 50f));
        sb.AppendLine("coreInRoot=" + Bounds(root).Contains(core.center));
        sb.AppendLine("actionX=" + (action.xMin >= Bounds(root).xMin && action.xMax <= Bounds(root).xMax));
        sb.AppendLine("actionAboveNav=" + (action.yMax <= nav.yMin + 1f) + " actionBottom=" + action.yMax
                      + " navTop=" + nav.yMin);
        return sb.ToString();
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
