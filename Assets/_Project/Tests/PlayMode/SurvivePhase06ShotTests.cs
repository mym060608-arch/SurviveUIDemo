using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Phase 06 clean NEREID-04 surface screenshots (validation/asset pipeline).
///
/// Composited offscreen frames: the gameplay camera and the SurfaceHUD panel
/// are both retargeted to one RenderTexture, so the PNG shows the real 3D
/// world with the Chinese-first HUD overlay and no engine watermark. States:
/// A. surface_idle        : fresh expedition, HUD idle (搜寻资源节点);
/// B. surface_harvest     : harvester deployed, status card 采集中;
/// C. surface_transfer    : deterministic push to wireless transfer (beam);
/// D. surface_settled     : +5 settled, depleted node, result line shown.
/// Each at 1920x1080, 1600x900 and 1280x720. Run isolated via
/// -testFilter SurvivePhase06ShotTests.
/// </summary>
public class SurvivePhase06ShotTests
{
    private const string OutDir = @"D:\dsh_gpt_unity_blender\export\surviveui_phase06_shot_clean";
    private const string ReportName = "clean_shot_report.txt";

    private static readonly Vector2Int[] Sizes =
    {
        new Vector2Int(1920, 1080),
        new Vector2Int(1600, 900),
        new Vector2Int(1280, 720)
    };

    private const int MaxWaitFrames = 1500;

    private int _failures;
    private readonly System.Text.StringBuilder _report = new System.Text.StringBuilder();

    [UnityTest]
    public IEnumerator CleanSurface_FourStates_ThreeResolutions()
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
        GameSession session = GameSession.Instance;
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        // Real expedition gate, then enter the surface.
        Assert.IsTrue(session.TryConfirmLoadout(), "confirm loadout");
        Assert.IsTrue(session.CanStartExpedition, "expedition gate open");
        SceneManager.LoadScene(GameSession.SurfaceSceneName);
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == GameSession.SurfaceSceneName);

        SurfaceGameController controller = null;
        yield return WaitUntil(() =>
        {
            controller = Object.FindAnyObjectByType<SurfaceGameController>();
            return controller != null && controller.IsReady;
        });

        Assert.IsNotNull(controller, "surface ready");
        SurfacePlayerController player = Object.FindAnyObjectByType<SurfacePlayerController>();
        SurfaceResourceNode node0 = controller.Nodes[0];
        Assert.IsNotNull(player, "player present");
        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        UIDocument hudDocument = null;
        foreach (UIDocument doc in Object.FindObjectsByType<UIDocument>())
        {
            if (doc.visualTreeAsset != null && doc.visualTreeAsset.name == "SurfaceHUD")
            {
                hudDocument = doc;
                break;
            }
        }

        Assert.IsNotNull(hudDocument, "HUD document (screen-space SurfaceHUD)");
        PanelSettings panel = hudDocument.panelSettings;
        Assert.IsNotNull(panel, "HUD panel settings");
        Camera cam = Camera.main;
        Assert.IsNotNull(cam, "gameplay camera");

        Directory.CreateDirectory(OutDir);
        _report.Clear();
        _failures = 0;

        // A: idle (player still on the pad, nothing deployed).
        yield return CaptureState(controller, player, node0, panel, cam, hudDocument,
            "surface_idle", SurfaceResourceNode.NodeState.Available);

        // B: deploy node0 (semantic API) and freeze it in Harvesting.
        player.TeleportTo(node0.transform.position + Vector3.forward * 1.2f);
        yield return null;
        Assert.IsTrue(controller.TryDeployNearest(), "deploy node0");
        node0.enabled = false; // keep the deterministic stage for all sizes
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        yield return CaptureState(controller, player, node0, panel, cam, hudDocument,
            "surface_harvest", SurfaceResourceNode.NodeState.Harvesting);

        // C: deterministic push to wireless transfer (beam on).
        node0.AdvanceTimers(node0.RemainingSeconds);
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        yield return CaptureState(controller, player, node0, panel, cam, hudDocument,
            "surface_transfer", SurfaceResourceNode.NodeState.Transmitting);

        // D: settle -> depleted node, +5 Minerals, result line visible.
        node0.AdvanceTimers(node0.RemainingSeconds);
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        yield return CaptureState(controller, player, node0, panel, cam, hudDocument,
            "surface_settled", SurfaceResourceNode.NodeState.Depleted);

        Assert.AreEqual(25, session.Minerals, "settled minerals 25");
        Assert.AreEqual(1, session.HarvestedNodeCount, "one node settled");

        foreach (string screen in new[] { "surface_idle", "surface_harvest", "surface_transfer", "surface_settled" })
        {
            string src = Path.Combine(OutDir, "surviveui_phase06_" + screen + "_1920x1080.png");
            string dst = Path.Combine(@"D:\dsh_gpt_unity_blender\export",
                "surviveui_phase06_" + screen + "_1920x1080.png");
            if (File.Exists(src))
            {
                File.Copy(src, dst, true);
            }
        }

        _report.AppendLine(_failures == 0 ? "RESULT: SUCCESS" : "RESULT: FAILED");
        File.WriteAllText(Path.Combine(OutDir, ReportName), _report.ToString());
        Assert.AreEqual(0, _failures, "clean surface screenshot checks failed");
    }

    private IEnumerator CaptureState(SurfaceGameController controller, SurfacePlayerController player,
        SurfaceResourceNode node0, PanelSettings panel, Camera cam, UIDocument hudDocument,
        string screenName, SurfaceResourceNode.NodeState expectedState)
    {
        if (expectedState == SurfaceResourceNode.NodeState.Available)
        {
            Assert.AreEqual(SurfaceResourceNode.NodeState.Available, node0.State,
                "node untouched for idle frame");
        }
        else
        {
            Assert.AreEqual(expectedState, node0.State, "expected state prepared: " + screenName);
        }

        if (!HudGeometryOk(hudDocument.rootVisualElement, expectedState,
            expectedState == SurfaceResourceNode.NodeState.Depleted))
        {
            File.WriteAllText(Path.Combine(OutDir, "fail_geometry_" + screenName + ".txt"),
                HudGeometryDiagnostic(hudDocument.rootVisualElement));
        }

        Assert.IsTrue(HudGeometryOk(hudDocument.rootVisualElement, expectedState,
            expectedState == SurfaceResourceNode.NodeState.Depleted),
            "HUD layout prepared: " + screenName);

        foreach (Vector2Int size in Sizes)
        {
            string label = screenName + " " + size.x + "x" + size.y;
            var renderTexture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
            renderTexture.Create();

            RenderTexture previousCameraTarget = cam.targetTexture;
            RenderTexture previousPanelTarget = panel.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            cam.targetTexture = renderTexture;
            panel.targetTexture = renderTexture;

            try
            {
                for (int i = 0; i < 14; i++)
                {
                    yield return null;
                }

                RenderTexture.active = renderTexture;
                var readback = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                readback.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                readback.Apply();
                var png = readback;
                yield return null;

                string pngPath = Path.Combine(OutDir,
                    "surviveui_phase06_" + screenName + "_" + size.x + "x" + size.y + ".png");
                File.WriteAllBytes(pngPath, png.EncodeToPNG());

                var saved = new Texture2D(2, 2, TextureFormat.RGB24, false);
                Assert.IsTrue(saved.LoadImage(File.ReadAllBytes(pngPath)), "saved png loads " + label);
                Assert.Greater(new FileInfo(pngPath).Length, 60000, "saved png has real content " + label);

                // The 3D world must be present (transparent UI-only frames fail).
                int veryDark = CountVeryDarkPixels(saved, 0, size.x, 0, 160);
                int totalBand = Mathf.Min(160, saved.height) * size.x;
                Check(label + " world content rendered (not transparent/black)",
                    veryDark < totalBand * 0.4f);

                Check(label + " HUD layout sound", HudGeometryOk(hudDocument.rootVisualElement,
                    expectedState, expectedState == SurfaceResourceNode.NodeState.Depleted));

                Object.Destroy(png);
                Object.Destroy(saved);
            }
            finally
            {
                RenderTexture.active = previousActive;
                cam.targetTexture = previousCameraTarget;
                panel.targetTexture = previousPanelTarget;
                renderTexture.Release();
            }

            yield return null;
        }
    }

    private static string HudGeometryDiagnostic(VisualElement root)
    {
        var sb = new System.Text.StringBuilder();
        if (root == null)
        {
            return "root null";
        }

        sb.AppendLine("root " + root.worldBound);
        foreach (var pair in new[]
        {
            new[] { "context", null },
            new[] { "status", "HudStatusCard" },
            new[] { "prompt", "HudPrompt" },
            new[] { "result", "HudResult" },
            new[] { "return", "HudReturnButton" }
        })
        {
            VisualElement e;
            if (pair[0] == "context")
            {
                e = root.Q<VisualElement>(null, "hud-context");
            }
            else
            {
                e = root.Q<VisualElement>(pair[1]);
            }

            sb.AppendLine(pair[0] + " -> " + (e == null ? "null"
                : e.worldBound + " display=" + e.resolvedStyle.display));
        }

        return sb.ToString();
    }

    /// <summary>Light no-overlap/placement checks for the surface HUD.</summary>
    private static bool HudGeometryOk(VisualElement root, SurfaceResourceNode.NodeState state,
        bool resultExpected)
    {
        if (root == null)
        {
            return false;
        }

        Rect rootRect = root.worldBound;
        if (rootRect.width < 10f || rootRect.height < 10f)
        {
            return false;
        }

        VisualElement context = root.Q<VisualElement>(null, "hud-context");
        VisualElement statusCard = root.Q<VisualElement>("HudStatusCard");
        Label prompt = root.Q<Label>("HudPrompt");
        Label result = root.Q<Label>("HudResult");
        VisualElement returnButton = root.Q<Button>("HudReturnButton");
        if (context == null || statusCard == null || prompt == null || result == null || returnButton == null)
        {
            return false;
        }

        Rect contextRect = context.worldBound;
        Rect returnRect = returnButton.worldBound;
        Rect promptRect = prompt.worldBound;
        bool contextInside = rootRect.Contains(contextRect.center);

        // Return button is hidden until a node settles (gameplay design);
        // only enforce its placement when it should be visible.
        bool returnShouldShow = state == SurfaceResourceNode.NodeState.Depleted;
        bool returnVisible = returnButton.resolvedStyle.display == DisplayStyle.Flex
                            && returnRect.width > 20f;
        bool returnCorrect = returnShouldShow
            ? returnVisible && rootRect.Contains(returnRect.center)
            : !returnVisible;
        bool contextClearReturn = !returnVisible
                                  || contextRect.xMax <= returnRect.xMin + 1f
                                  || returnRect.xMin >= contextRect.xMax - 1f;

        // Prompt sits in the bottom band, horizontally centred.
        bool promptBottom = promptRect.yMin > rootRect.yMax - rootRect.height * 0.35f;
        bool promptCentered = promptRect.center.x > rootRect.xMin + rootRect.width * 0.3f
                              && promptRect.center.x < rootRect.xMax - rootRect.width * 0.3f;

        // Status card visible only while a node harvests/transmits.
        bool statusShouldShow = state == SurfaceResourceNode.NodeState.Harvesting
                                || state == SurfaceResourceNode.NodeState.Transmitting;
        bool statusCorrect;
        if (statusShouldShow)
        {
            statusCorrect = statusCard.resolvedStyle.display == DisplayStyle.Flex
                           && statusCard.worldBound.width > 20f;
        }
        else
        {
            statusCorrect = statusCard.resolvedStyle.display == DisplayStyle.None;
        }

        // Result line appears only after a settle (+5).
        bool resultCorrect = resultExpected
            ? !string.IsNullOrEmpty(result.text)
            : string.IsNullOrEmpty(result.text);

        return contextInside && returnCorrect && contextClearReturn
               && promptBottom && promptCentered && statusCorrect && resultCorrect;
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

    private static int CountVeryDarkPixels(Texture2D texture, int x0, int x1, int y0, int y1)
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
                if (lum < 0.05f)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
