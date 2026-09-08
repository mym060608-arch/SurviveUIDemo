using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>
/// Phase 01-06 structural validator.
///
/// Checks (edit-mode, deterministic):
/// - scenes exist with the correct build order (0 = 00_Boot, 1 = 01_Hub, 2 = 02_NereidSurface),
/// - MainApp.uxml contains every element the runtime binds to,
/// - 00_Boot keeps its minimal role (BootController, no UI / no authored GameSession),
/// - 01_Hub has exactly one UIDocument + one AppController with valid assets,
/// - 02_NereidSurface authors no GameSession and keeps one player controller,
///   one game controller, one HUD controller, one UIDocument and three resource nodes.
///
/// Run via Tools/SurviveUI/Validate Phase 01 Project or
/// -executeMethod SurviveProjectValidator.RunCli -resultPath &lt;file&gt;
/// </summary>
public static class SurviveProjectValidator
{
    private const string BootScenePath = "Assets/_Project/Scenes/00_Boot.unity";
    private const string HubScenePath = "Assets/_Project/Scenes/01_Hub.unity";
    private const string SurfaceScenePath = "Assets/_Project/Scenes/02_NereidSurface.unity";
    private const string UxmlPath = "Assets/_Project/UI/UXML/MainApp.uxml";
    private const string UssPath = "Assets/_Project/UI/USS/AppTheme.uss";
    private const string OverviewUssPath = "Assets/_Project/UI/USS/OverviewScreen.uss";
    private const string PreparationUssPath = "Assets/_Project/UI/USS/PreparationScreen.uss";
    private const string ProcessingUssPath = "Assets/_Project/UI/USS/ProcessingScreen.uss";
    private const string OutpostUssPath = "Assets/_Project/UI/USS/OutpostScreen.uss";
    private const string SurfaceHudUxmlPath = "Assets/_Project/UI/UXML/SurfaceHUD.uxml";
    private const string SurfaceHudUssPath = "Assets/_Project/UI/USS/SurfaceHUD.uss";
    private const string SurfaceWristUxmlPath = "Assets/_Project/UI/UXML/SurfaceWrist.uxml";
    private const string SurfaceWristUssPath = "Assets/_Project/UI/USS/SurfaceWrist.uss";
    private const string GuidePath = "Docs/UI_LANGUAGE_STYLE.md";

    /// <summary>Phase 06 Revision normal exploration HUD element names
    /// (screen-space SurfaceHUD.uxml).</summary>
    private static readonly string[] SurfaceHudRevisionStructure =
    {
        "HudLayer",
        "HudNavBlock", "HudNavName", "HudNavDistance",
        "HudCrosshairDot", "HudCrosshairRing"
    };

    /// <summary>Phase 06 Revision wrist terminal element names the runtime
    /// binds to (SurfaceWrist.uxml, CodeReference radial architecture: geometry
    /// host + map marker layer + radial status labels + sector content).</summary>
    private static readonly string[] WristRevisionStructure =
    {
        "WristRoot",
        "RadialGeometryHost",
        "MapMarkerLayer",
        "MapPlayer", "MapLanding",
        "MapNode0", "MapNode1", "MapNode2",
        "MapSelectedRing",
        "HealthLabel", "SatietyLabel",
        "EquipContent", "EquipHarvester", "EquipBattery", "EquipDrone",
        "StateContent", "ActionState", "SignalState", "RecoveredState",
        "NavContent", "NavTarget", "NavDistance",
        "HarvestContent", "HarvestNode", "HarvestState",
        "WristIdentity"
    };

    /// <summary>Legacy wrist element names that must NOT come back: the wrist
    /// cursor is Cursor.SetCursor-driven and radial geometry is triangle-mesh
    /// based (no tick rings / MapDisc / overlay elements).</summary>
    private static readonly string[] WristLegacyForbidden =
    {
        "WristCursor",
        "WristMapCircle",
        "WristHealthRing",
        "WristSatietyRing",
        "WristCursorOverlay"
    };

    [MenuItem("Tools/SurviveUI/Validate Phase 01 Project")]
    public static void ValidateFromMenu()
    {
        RunValidation(null);
    }

    /// <summary>CLI entry used by batch automation.</summary>
    public static void RunCli()
    {
        string resultPath = GetArgValue("-resultPath");
        bool ok = RunValidation(resultPath);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool RunValidation(string resultPath)
    {
        var log = new StringBuilder();
        int failures = 0;

        void Record(bool pass, string label)
        {
            log.AppendLine((pass ? "PASS" : "FAIL") + " :: " + label);
            if (pass)
            {
                Debug.Log("[SurviveProjectValidator] PASS :: " + label);
            }
            else
            {
                failures++;
                Debug.LogError("[SurviveProjectValidator] FAIL :: " + label);
            }
        }

        // ---- Assets ----
        string guideFullPath = Path.Combine(Application.dataPath, "..", GuidePath);
        Record(File.Exists(guideFullPath), "language/style guide exists: " + GuidePath);
        Record(File.Exists(BootScenePath), "scene exists: " + BootScenePath);
        Record(File.Exists(HubScenePath), "scene exists: " + HubScenePath);
        Record(File.Exists(SurfaceScenePath), "scene exists: " + SurfaceScenePath);
        Record(File.Exists(UxmlPath), "uxml exists: " + UxmlPath);
        Record(File.Exists(UssPath), "uss exists: " + UssPath);
        Record(File.Exists(OverviewUssPath), "overview uss exists: " + OverviewUssPath);
        Record(File.Exists(PreparationUssPath), "preparation uss exists: " + PreparationUssPath);
        Record(File.Exists(ProcessingUssPath), "processing uss exists: " + ProcessingUssPath);
        Record(File.Exists(OutpostUssPath), "outpost uss exists: " + OutpostUssPath);
        Record(File.Exists(SurfaceHudUxmlPath), "surface HUD uxml exists: " + SurfaceHudUxmlPath);
        Record(File.Exists(SurfaceHudUssPath), "surface HUD uss exists: " + SurfaceHudUssPath);
        Record(File.Exists(SurfaceWristUxmlPath), "surface wrist uxml exists: " + SurfaceWristUxmlPath);
        Record(File.Exists(SurfaceWristUssPath), "surface wrist uss exists: " + SurfaceWristUssPath);

        // ---- Build order ----
        var buildScenes = EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0];
        Record(buildScenes.Length == 3,
            "build contains exactly 3 scenes (found " + buildScenes.Length + ")");
        if (buildScenes.Length >= 1)
        {
            Record(buildScenes[0].path == BootScenePath && buildScenes[0].enabled,
                "build[0] = 00_Boot enabled (actual: " + buildScenes[0].path + ")");
        }

        if (buildScenes.Length >= 2)
        {
            Record(buildScenes[1].path == HubScenePath && buildScenes[1].enabled,
                "build[1] = 01_Hub enabled (actual: " + buildScenes[1].path + ")");
        }

        if (buildScenes.Length >= 3)
        {
            Record(buildScenes[2].path == SurfaceScenePath && buildScenes[2].enabled,
                "build[2] = 02_NereidSurface enabled (actual: " + buildScenes[2].path + ")");
        }

        // ---- MainApp.uxml coverage ----
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        Record(uxml != null, "MainApp.uxml imports as VisualTreeAsset");
        if (uxml != null)
        {
            HashSet<string> present = new HashSet<string>();
            TemplateContainer template = uxml.Instantiate();
            template.Query<VisualElement>().ForEach(element =>
            {
                if (!string.IsNullOrEmpty(element.name))
                {
                    present.Add(element.name);
                }
            });

            foreach (string expected in ElementNames.All)
            {
                Record(present.Contains(expected), "MainApp.uxml contains element: " + expected);
            }

            // Phase 02: Overview visual composition must keep its stable
            // structure (checked by names only, never by pixel values).
            foreach (string expected in ElementNames.OverviewStructure)
            {
                Record(present.Contains(expected), "MainApp.uxml overview structure: " + expected);
            }

            // Phase 03: Preparation composition keeps its stable structure.
            foreach (string expected in ElementNames.PreparationStructure)
            {
                Record(present.Contains(expected), "MainApp.uxml preparation structure: " + expected);
            }

            // Phase 04: Processing composition keeps its stable structure.
            foreach (string expected in ElementNames.ProcessingStructure)
            {
                Record(present.Contains(expected), "MainApp.uxml processing structure: " + expected);
            }

            // Phase 05: Outpost composition keeps its stable structure.
            foreach (string expected in ElementNames.OutpostStructure)
            {
                Record(present.Contains(expected), "MainApp.uxml outpost structure: " + expected);
            }
        }

        // ---- SurfaceHUD.uxml coverage (Phase 06 + Revision HUD structure) ----
        var surfaceUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SurfaceHudUxmlPath);
        Record(surfaceUxml != null, "SurfaceHUD.uxml imports as VisualTreeAsset");
        if (surfaceUxml != null)
        {
            HashSet<string> surfacePresent = new HashSet<string>();
            TemplateContainer surfaceTemplate = surfaceUxml.Instantiate();
            surfaceTemplate.Query<VisualElement>().ForEach(element =>
            {
                if (!string.IsNullOrEmpty(element.name))
                {
                    surfacePresent.Add(element.name);
                }
            });

            foreach (string expected in SurfaceHudRevisionStructure)
            {
                Record(surfacePresent.Contains(expected),
                    "SurfaceHUD.uxml revision structure: " + expected);
            }

            Record(!surfacePresent.Contains("WristCursorOverlay"),
                "SurfaceHUD.uxml has no legacy WristCursorOverlay (cursor = Cursor.SetCursor)");
        }

        // ---- SurfaceWrist.uxml coverage (floating wrist terminal) ----
        var wristUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SurfaceWristUxmlPath);
        Record(wristUxml != null, "SurfaceWrist.uxml imports as VisualTreeAsset");
        if (wristUxml != null)
        {
            HashSet<string> wristPresent = new HashSet<string>();
            TemplateContainer wristTemplate = wristUxml.Instantiate();
            wristTemplate.Query<VisualElement>().ForEach(element =>
            {
                if (!string.IsNullOrEmpty(element.name))
                {
                    wristPresent.Add(element.name);
                }
            });

            foreach (string expected in WristRevisionStructure)
            {
                Record(wristPresent.Contains(expected),
                    "SurfaceWrist.uxml revision structure: " + expected);
            }

            foreach (string forbidden in WristLegacyForbidden)
            {
                Record(!wristPresent.Contains(forbidden),
                    "SurfaceWrist.uxml contains no legacy element: " + forbidden);
            }
        }

        // ---- 00_Boot scene content ----
        if (File.Exists(BootScenePath))
        {
            EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
            Record(CountInScene<BootController>() == 1,
                "00_Boot has exactly one BootController (found " + CountInScene<BootController>() + ")");
            Record(CountInScene<UIDocument>() == 0,
                "00_Boot contains no UIDocument (found " + CountInScene<UIDocument>() + ")");
            Record(CountInScene<GameSession>() == 0,
                "00_Boot authors no GameSession instance (found " + CountInScene<GameSession>() + ")");
        }

        // ---- 01_Hub scene content ----
        if (File.Exists(HubScenePath))
        {
            EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single);
            Record(CountInScene<UIDocument>() == 1,
                "01_Hub has exactly one UIDocument (found " + CountInScene<UIDocument>() + ")");
            Record(CountInScene<AppController>() == 1,
                "01_Hub has exactly one AppController (found " + CountInScene<AppController>() + ")");
            Record(CountInScene<GameSession>() == 0,
                "01_Hub authors no GameSession instance (found " + CountInScene<GameSession>() + ")");

            UIDocument document = FirstInScene<UIDocument>();
            if (document != null)
            {
                string sourceName = document.visualTreeAsset != null ? document.visualTreeAsset.name : "<null>";
                Record(document.visualTreeAsset != null && sourceName == "MainApp",
                    "UIDocument bound to MainApp.uxml (actual: " + sourceName + ")");
                Record(document.panelSettings != null,
                    "UIDocument has PanelSettings assigned");
            }
            else
            {
                Record(false, "UIDocument missing (cannot verify binding)");
            }
        }

        // ---- 02_NereidSurface scene content ----
        if (File.Exists(SurfaceScenePath))
        {
            EditorSceneManager.OpenScene(SurfaceScenePath, OpenSceneMode.Single);
            Record(CountInScene<GameSession>() == 0,
                "02_NereidSurface authors no GameSession instance (found "
                + CountInScene<GameSession>() + ")");
            Record(CountInScene<SurfacePlayerController>() == 1,
                "02_NereidSurface has exactly one SurfacePlayerController (found "
                + CountInScene<SurfacePlayerController>() + ")");
            Record(CountInScene<SurfaceGameController>() == 1,
                "02_NereidSurface has exactly one SurfaceGameController (found "
                + CountInScene<SurfaceGameController>() + ")");
            Record(CountInScene<SurfaceHudController>() == 1,
                "02_NereidSurface has exactly one SurfaceHudController (found "
                + CountInScene<SurfaceHudController>() + ")");
            Record(CountInScene<SurfaceWristController>() == 1,
                "02_NereidSurface has exactly one SurfaceWristController (found "
                + CountInScene<SurfaceWristController>() + ")");
            Record(CountInScene<FirstPersonInteractor>() == 1,
                "02_NereidSurface has exactly one FirstPersonInteractor (found "
                + CountInScene<FirstPersonInteractor>() + ")");
            Record(CountInScene<LandingReturnInteractable>() == 1,
                "02_NereidSurface has exactly one LandingReturnInteractable (found "
                + CountInScene<LandingReturnInteractable>() + ")");
            Record(CountInScene<UIDocument>() == 2,
                "02_NereidSurface has exactly two UIDocuments (HUD + wrist) (found "
                + CountInScene<UIDocument>() + ")");
            Record(CountInScene<SurfaceResourceNode>() == 3,
                "02_NereidSurface has exactly 3 resource nodes (found "
                + CountInScene<SurfaceResourceNode>() + ")");

            // Revision: first-person presentation - eye camera is a child of
            // the player body and no legacy follow camera root exists.
            SurfacePlayerController surfacePlayer = FirstInScene<SurfacePlayerController>();
            bool cameraUnderPlayer = false;
            bool sawCamera = false;
            if (surfacePlayer != null)
            {
                foreach (Camera c in surfacePlayer.GetComponentsInChildren<Camera>(true))
                {
                    sawCamera = true;
                    cameraUnderPlayer = true;
                    Record(c.transform.localPosition.y > 1.4f && c.transform.localPosition.y < 1.9f,
                        "first-person eye camera at eye height (y="
                        + c.transform.localPosition.y.ToString("0.00") + ")");
                }
            }

            Record(sawCamera && cameraUnderPlayer, "surface camera is child of Player (first person)");
            Record(!RootExists("GameplayCamera"), "no legacy third-person follow camera root");
            Record(CountInScene<WristDisplayAnchor>() == 1,
                "02_NereidSurface has exactly one wrist display anchor (found "
                + CountInScene<WristDisplayAnchor>() + ")");

            // Both UIDocuments and their bindings / panel settings.
            Scene active = SceneManager.GetActiveScene();
            bool sawHudDoc = false;
            bool sawWristDoc = false;
            bool hudPanelAssigned = false;
            bool wristPanelAssigned = false;
            foreach (GameObject root in active.GetRootGameObjects())
            {
                foreach (UIDocument doc in root.GetComponentsInChildren<UIDocument>(true))
                {
                    string name = doc.visualTreeAsset != null ? doc.visualTreeAsset.name : "<null>";
                    if (name == "SurfaceHUD")
                    {
                        sawHudDoc = true;
                        SurfaceHudController hud = doc.GetComponent<SurfaceHudController>();
                        hudPanelAssigned = doc.panelSettings != null
                            || (hud != null && hud.panelSettings != null);
                    }
                    else if (name == "SurfaceWrist")
                    {
                        sawWristDoc = true;
                        SurfaceWristController wrist = doc.GetComponent<SurfaceWristController>();
                        wristPanelAssigned = wrist != null && wrist.panelSettings != null;
                    }
                }
            }

            Record(sawHudDoc, "Surface UIDocument bound to SurfaceHUD.uxml");
            Record(sawWristDoc, "Wrist UIDocument bound to SurfaceWrist.uxml");
            Record(hudPanelAssigned, "Surface HUD PanelSettings assigned");
            Record(wristPanelAssigned, "Wrist PanelSettings assigned (RenderTexture target at runtime)");

            // Surface resource node ids must be unique and well-formed.
            var nodeIds = new List<int>();
            Scene surfaceActive = SceneManager.GetActiveScene();
            foreach (GameObject root in surfaceActive.GetRootGameObjects())
            {
                foreach (SurfaceResourceNode node in root.GetComponentsInChildren<SurfaceResourceNode>(true))
                {
                    nodeIds.Add(node.NodeId);
                }
            }

            Record(nodeIds.Count == 3 && new HashSet<int>(nodeIds).Count == 3,
                "surface node ids unique (found " + nodeIds.Count + " distinct)");
        }

        bool ok = failures == 0;
        log.AppendLine(ok ? "RESULT: SUCCESS" : "RESULT: FAILED");
        log.AppendLine("CHECKS: " + CountCheckLines(log) + " | FAILURES: " + failures);

        if (!string.IsNullOrEmpty(resultPath))
        {
            try
            {
                File.WriteAllText(resultPath, log.ToString());
                Debug.Log("[SurviveProjectValidator] wrote result: " + resultPath);
            }
            catch (Exception e)
            {
                Debug.LogError("[SurviveProjectValidator] cannot write result file: " + e);
                ok = false;
            }
        }
        else
        {
            Debug.Log("[SurviveProjectValidator] result:\n" + log);
        }

        return ok;
    }

    private static int CountCheckLines(StringBuilder sb)
    {
        int count = 0;
        foreach (string line in sb.ToString().Split('\n'))
        {
            if (line.StartsWith("PASS") || line.StartsWith("FAIL"))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountInScene<T>() where T : Component
    {
        int count = 0;
        Scene active = SceneManager.GetActiveScene();
        foreach (GameObject root in active.GetRootGameObjects())
        {
            count += root.GetComponentsInChildren<T>(true).Length;
        }

        return count;
    }

    private static bool RootExists(string rootName)
    {
        Scene active = SceneManager.GetActiveScene();
        foreach (GameObject root in active.GetRootGameObjects())
        {
            if (root.name == rootName)
            {
                return true;
            }
        }

        return false;
    }

    private static T FirstInScene<T>() where T : Component
    {
        Scene active = SceneManager.GetActiveScene();
        foreach (GameObject root in active.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
            {
                return component;
            }
        }

        return null;
    }

    private static string GetArgValue(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
