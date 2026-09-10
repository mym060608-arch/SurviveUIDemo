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
    private const string ShipScenePath = "Assets/_Project/Scenes/03_ShipInterior.unity";
    private const string MainMenuScenePath = "Assets/_Project/Scenes/04_MainMenu.unity";
    private const string UxmlPath = "Assets/_Project/UI/UXML/MainApp.uxml";
    private const string MainMenuUxmlPath = "Assets/_Project/UI/UXML/MainMenu.uxml";
    private const string MainMenuUssPath = "Assets/_Project/UI/USS/MainMenu.uss";
    private const string UssPath = "Assets/_Project/UI/USS/AppTheme.uss";
    private const string OverviewUssPath = "Assets/_Project/UI/USS/OverviewScreen.uss";
    private const string PreparationUssPath = "Assets/_Project/UI/USS/PreparationScreen.uss";
    private const string ProcessingUssPath = "Assets/_Project/UI/USS/ProcessingScreen.uss";
    private const string OutpostUssPath = "Assets/_Project/UI/USS/OutpostScreen.uss";
    private const string SurfaceHudUxmlPath = "Assets/_Project/UI/UXML/SurfaceHUD.uxml";
    private const string SurfaceHudUssPath = "Assets/_Project/UI/USS/SurfaceHUD.uss";
    private const string SurfaceWristUxmlPath = "Assets/_Project/UI/UXML/SurfaceWrist.uxml";
    private const string SurfaceWristUssPath = "Assets/_Project/UI/USS/SurfaceWrist.uss";
    private const string ShipHudUxmlPath = "Assets/_Project/UI/UXML/ShipHUD.uxml";
    private const string ShipHudUssPath = "Assets/_Project/UI/USS/ShipHUD.uss";
    private const string GuidePath = "Docs/UI_LANGUAGE_STYLE.md";

    /// <summary>Phase 07 ship interior HUD element names the runtime binds to.</summary>
    private static readonly string[] ShipHudStructure =
    {
        "ShipHudRoot",
        "ShipZoneChip", "ShipZoneName", "ShipZoneMeta",
        "ShipTitle",
        "ShipCrosshairDot", "ShipCrosshairRing",
        "ShipPrompt"
    };

    /// <summary>Phase 06 Revision normal exploration HUD element names
    /// (screen-space SurfaceHUD.uxml).</summary>
    private static readonly string[] SurfaceHudRevisionStructure =
    {
        "HudLayer",
        "HudNavBlock", "HudNavName", "HudNavDistance",
        "HudCrosshairDot", "HudCrosshairRing"
    };

    /// <summary>Phase 06 Revision wrist terminal element names the runtime
    /// binds to (SurfaceWrist.uxml, Round 9.2 parallel-cap architecture:
    /// geometry host + map marker layer + radial status labels + independent
    /// text-row slots).</summary>
    private static readonly string[] WristRevisionStructure =
    {
        "WristRoot",
        "RadialGeometryHost",
        "MapMarkerLayer",
        "MapPlayer", "MapLanding",
        "MapNode0", "MapNode1", "MapNode2",
        "MapSelectedRing",
        "HealthLabel", "SatietyLabel",
        "EquipHeading", "EquipHarvester", "EquipBattery", "EquipDrone",
        "StateHeading", "ActionState", "SignalState", "RecoveredState",
        "NavHeading", "NavTarget", "NavDistance",
        "HarvestHeading", "HarvestNode", "HarvestState", "HarvestProgress",
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
        Record(File.Exists(ShipScenePath), "scene exists: " + ShipScenePath);
        Record(File.Exists(MainMenuScenePath), "scene exists: " + MainMenuScenePath);
        Record(File.Exists(MainMenuUxmlPath), "main menu uxml exists: " + MainMenuUxmlPath);
        Record(File.Exists(MainMenuUssPath), "main menu uss exists: " + MainMenuUssPath);
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
        Record(File.Exists(ShipHudUxmlPath), "ship HUD uxml exists: " + ShipHudUxmlPath);
        Record(File.Exists(ShipHudUssPath), "ship HUD uss exists: " + ShipHudUssPath);

        // ---- Build order ----
        var buildScenes = EditorBuildSettings.scenes ?? new EditorBuildSettingsScene[0];
        Record(buildScenes.Length == 5,
            "build contains exactly 5 scenes (found " + buildScenes.Length + ")");
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

        if (buildScenes.Length >= 4)
        {
            Record(buildScenes[3].path == ShipScenePath && buildScenes[3].enabled,
                "build[3] = 03_ShipInterior enabled (actual: " + buildScenes[3].path + ")");
        }

        // Phase 08: the menu is appended after the frozen Phase 01-07 scenes, so
        // the launch scene (index 0 = 00_Boot) and every legacy index stay put.
        if (buildScenes.Length >= 5)
        {
            Record(buildScenes[4].path == MainMenuScenePath && buildScenes[4].enabled,
                "build[4] = 04_MainMenu enabled (actual: " + buildScenes[4].path + ")");
        }

        // Navigation destination validity: the routing constant must name the
        // scene that is actually in the build list (a stale name would leave the
        // human entry into the demo broken).
        string routingSceneName = System.IO.Path.GetFileNameWithoutExtension(MainMenuScenePath);
        Record(SceneRouting.MainMenuSceneName == routingSceneName,
            "SceneRouting.MainMenuSceneName matches the Main Menu scene (actual: "
            + SceneRouting.MainMenuSceneName + ")");
        bool menuInBuild = false;
        bool hubInBuild = false;
        foreach (EditorBuildSettingsScene scene in buildScenes)
        {
            if (scene != null && scene.path == MainMenuScenePath)
            {
                menuInBuild = scene.enabled;
            }

            if (scene != null && scene.path == HubScenePath)
            {
                hubInBuild = scene.enabled;
            }
        }

        Record(menuInBuild, "Main Menu scene is an enabled build scene (16 进入任务 -> Overview)");
        Record(hubInBuild, "Overview destination scene is an enabled build scene");

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

        // ---- ShipHUD.uxml coverage (Phase 07 minimal interior HUD) ----
        var shipUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ShipHudUxmlPath);
        Record(shipUxml != null, "ShipHUD.uxml imports as VisualTreeAsset");
        if (shipUxml != null)
        {
            HashSet<string> shipPresent = new HashSet<string>();
            TemplateContainer shipTemplate = shipUxml.Instantiate();
            shipTemplate.Query<VisualElement>().ForEach(element =>
            {
                if (!string.IsNullOrEmpty(element.name))
                {
                    shipPresent.Add(element.name);
                }
            });

            foreach (string expected in ShipHudStructure)
            {
                Record(shipPresent.Contains(expected), "ShipHUD.uxml structure: " + expected);
            }
        }

        // ---- MainMenu.uxml coverage (Phase 08 product cover) ----
        var menuUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainMenuUxmlPath);
        Record(menuUxml != null, "MainMenu.uxml imports as VisualTreeAsset");
        if (menuUxml != null)
        {
            HashSet<string> menuPresent = new HashSet<string>();
            TemplateContainer menuTemplate = menuUxml.Instantiate();
            menuTemplate.Query<VisualElement>().ForEach(element =>
            {
                if (!string.IsNullOrEmpty(element.name))
                {
                    menuPresent.Add(element.name);
                }
            });

            foreach (string expected in MainMenuNames.All)
            {
                Record(menuPresent.Contains(expected), "MainMenu.uxml contains element: " + expected);
            }

            foreach (string expected in MainMenuNames.Structure)
            {
                Record(menuPresent.Contains(expected), "MainMenu.uxml composition structure: " + expected);
            }

            // The one primary CTA must exist as a real Button (not a label) so
            // the runtime can bind and the smoke can really click it.
            Button cta = menuTemplate.Q<Button>(MainMenuNames.EnterMissionButton);
            Record(cta != null, "MainMenu.uxml primary CTA is a Button (进入任务)");
            Record(cta != null && cta.Q<Label>(MainMenuNames.EnterMissionLabel) != null,
                "MainMenu.uxml primary CTA carries the Chinese label element");
            Record(cta != null
                   && cta.Q<Label>(MainMenuNames.EnterMissionLabel) != null
                   && cta.Q<Label>(MainMenuNames.EnterMissionLabel).text == "进入任务",
                "MainMenu.uxml primary CTA text is 进入任务");
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

        // ---- 03_ShipInterior scene content (Phase 07) ----
        if (File.Exists(ShipScenePath))
        {
            EditorSceneManager.OpenScene(ShipScenePath, OpenSceneMode.Single);
            Record(CountInScene<GameSession>() == 0,
                "03_ShipInterior authors no GameSession instance (found "
                + CountInScene<GameSession>() + ")");
            Record(CountInScene<SurfacePlayerController>() == 1,
                "03_ShipInterior has exactly one first-person player controller (found "
                + CountInScene<SurfacePlayerController>() + ")");
            Record(CountInScene<FirstPersonInteractor>() == 1,
                "03_ShipInterior has exactly one FirstPersonInteractor (found "
                + CountInScene<FirstPersonInteractor>() + ")");
            Record(CountInScene<ShipGameController>() == 1,
                "03_ShipInterior has exactly one ShipGameController (found "
                + CountInScene<ShipGameController>() + ")");
            Record(CountInScene<ShipHudController>() == 1,
                "03_ShipInterior has exactly one ShipHudController (found "
                + CountInScene<ShipHudController>() + ")");
            // Phase 07B.2.2: the ship scene authors exactly four UIDocuments -
            // the screen-space ShipHUD, the ship wrist document, the deployment
            // confirmation page and the shared loadout page. The four physical
            // console screens create their own UIDocuments at RUNTIME, so no
            // console document is ever authored into the scene.
            Record(CountInScene<UIDocument>() == 4,
                "03_ShipInterior authors four UIDocuments - HUD + wrist + "
                + "deployment confirmation + loadout (found "
                + CountInScene<UIDocument>() + ")");
            Record(CountInScene<ShipConsoleInteractable>() == 4,
                "03_ShipInterior has exactly 4 console interactables (found "
                + CountInScene<ShipConsoleInteractable>() + ")");
            Record(CountInScene<ShipDoorInteractable>() == 3,
                "03_ShipInterior has exactly 3 sliding door interactables (found "
                + CountInScene<ShipDoorInteractable>() + ")");
            Record(CountInScene<ShipAirlockInteractable>() == 1,
                "03_ShipInterior has exactly one airlock interactable (found "
                + CountInScene<ShipAirlockInteractable>() + ")");
            Record(CountInScene<ShipSpawnMarker>() == 2,
                "03_ShipInterior has exactly 2 spawn markers (found "
                + CountInScene<ShipSpawnMarker>() + ")");

            // Console identities must be the four production screens in order.
            var consoleIds = new List<int>();
            Scene shipActive = SceneManager.GetActiveScene();
            foreach (GameObject root in shipActive.GetRootGameObjects())
            {
                foreach (ShipConsoleInteractable console in
                         root.GetComponentsInChildren<ShipConsoleInteractable>(true))
                {
                    consoleIds.Add(console.ConsoleId);
                }
            }

            consoleIds.Sort();
            Record(consoleIds.Count == 4 && consoleIds[0] == 1 && consoleIds[1] == 2
                   && consoleIds[2] == 3 && consoleIds[3] == 4,
                "ship console ids are 1..4 (found " + string.Join(",", consoleIds) + ")");

            // First-person presentation: eye camera child of the player body.
            // Phase 07B.2.2: the scene also carries the focus-only
            // SharpScreenCamera, an URP Overlay camera frame-locked to the eye
            // at local zero, so the eye-height rule applies to the BASE camera
            // only (an overlay child is intentionally at the eye origin).
            SurfacePlayerController shipPlayer = FirstInScene<SurfacePlayerController>();
            bool shipCameraUnderPlayer = false;
            bool shipSawCamera = false;
            if (shipPlayer != null)
            {
                foreach (Camera c in shipPlayer.GetComponentsInChildren<Camera>(true))
                {
                    shipSawCamera = true;
                    shipCameraUnderPlayer = true;
                    var cameraData = c.GetComponent<
                        UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    bool isOverlay = cameraData != null
                                     && cameraData.renderType
                                     == UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
                    if (isOverlay)
                    {
                        Record(c.transform.localPosition.sqrMagnitude < 0.0001f,
                            "ship focus overlay camera frame-locked at the eye "
                            + "origin (local=" + c.transform.localPosition + ")");
                        continue;
                    }

                    Record(c.transform.localPosition.y > 1.4f && c.transform.localPosition.y < 1.9f,
                        "ship eye camera at eye height (y="
                        + c.transform.localPosition.y.ToString("0.00") + ")");
                }
            }

            Record(shipSawCamera && shipCameraUnderPlayer,
                "ship camera is child of Player (first person)");

            // Ship HUD UIDocument binding.
            Scene shipDocScene = SceneManager.GetActiveScene();
            bool shipHudDoc = false;
            foreach (GameObject root in shipDocScene.GetRootGameObjects())
            {
                foreach (UIDocument doc in root.GetComponentsInChildren<UIDocument>(true))
                {
                    string name = doc.visualTreeAsset != null ? doc.visualTreeAsset.name : "<null>";
                    if (name == "ShipHUD")
                    {
                        shipHudDoc = true;
                        ShipHudController hud = doc.GetComponent<ShipHudController>();
                        bool panelOk = doc.panelSettings != null
                            || (hud != null && hud.panelSettings != null);
                        Record(panelOk, "ship HUD PanelSettings assigned");
                    }
                }
            }

            Record(shipHudDoc, "ship UIDocument bound to ShipHUD.uxml");
        }

        // ---- 04_MainMenu scene content (Phase 08) ----
        if (File.Exists(MainMenuScenePath))
        {
            EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
            Record(CountInScene<GameSession>() == 0,
                "04_MainMenu authors no GameSession instance (found "
                + CountInScene<GameSession>() + ")");
            Record(CountInScene<UIDocument>() == 1,
                "04_MainMenu has exactly one UIDocument (found "
                + CountInScene<UIDocument>() + ")");
            Record(CountInScene<MainMenuController>() == 1,
                "04_MainMenu has exactly one MainMenuController (found "
                + CountInScene<MainMenuController>() + ")");
            Record(CountInScene<AppController>() == 0,
                "04_MainMenu authors no second UI application / AppController (found "
                + CountInScene<AppController>() + ")");

            // EventSystem / AudioListener uniqueness (§35 / §37): the project
            // authors neither one anywhere, so the menu must not introduce the
            // first (audited by type name so the validator needs no ugui ref).
            Record(CountInSceneByTypeName("EventSystem") == 0,
                "04_MainMenu authors no EventSystem (found "
                + CountInSceneByTypeName("EventSystem") + ")");
            Record(CountInScene<AudioListener>() == 0,
                "04_MainMenu authors no AudioListener (found "
                + CountInScene<AudioListener>() + ")");

            // The shared planet preview rig is NEVER authored: it is created on
            // demand and shared by every host (one authoritative rig).
            Record(CountInScene<OverviewPlanetPreview>() == 0,
                "04_MainMenu authors no planet preview rig (runtime-created, found "
                + CountInScene<OverviewPlanetPreview>() + ")");

            UIDocument menuDocument = FirstInScene<UIDocument>();
            if (menuDocument != null)
            {
                string sourceName = menuDocument.visualTreeAsset != null
                    ? menuDocument.visualTreeAsset.name
                    : "<null>";
                Record(menuDocument.visualTreeAsset != null && sourceName == "MainMenu",
                    "Main Menu UIDocument bound to MainMenu.uxml (actual: " + sourceName + ")");
                Record(menuDocument.panelSettings != null,
                    "Main Menu UIDocument has PanelSettings assigned");
            }
            else
            {
                Record(false, "Main Menu UIDocument missing (cannot verify binding)");
            }

            MainMenuController menu = FirstInScene<MainMenuController>();
            if (menu != null)
            {
                var serialized = new SerializedObject(menu);
                Record(serialized.FindProperty("themeStyleSheet") != null
                       && serialized.FindProperty("themeStyleSheet").objectReferenceValue != null,
                    "Main Menu controller has the shared theme style sheet assigned");
                Record(serialized.FindProperty("mainMenuStyleSheet") != null
                       && serialized.FindProperty("mainMenuStyleSheet").objectReferenceValue != null,
                    "Main Menu controller has its own style sheet assigned");
            }
            else
            {
                Record(false, "MainMenuController missing (cannot verify references)");
            }
        }

        // ---- shared 3D preview rig authoring guard ----
        foreach (string scenePath in new[] { BootScenePath, HubScenePath, SurfaceScenePath, ShipScenePath })
        {
            if (!File.Exists(scenePath))
            {
                continue;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Record(CountInScene<OverviewPlanetPreview>() == 0,
                System.IO.Path.GetFileName(scenePath) + " authors no planet preview rig (found "
                + CountInScene<OverviewPlanetPreview>() + ")");
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

    /// <summary>
    /// Counts components whose runtime type name matches, so uniqueness checks
    /// (EventSystem) need no reference to the ugui assembly from this asmdef.
    /// </summary>
    private static int CountInSceneByTypeName(string typeName)
    {
        int count = 0;
        Scene active = SceneManager.GetActiveScene();
        foreach (GameObject root in active.GetRootGameObjects())
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component != null && component.GetType().Name == typeName)
                {
                    count++;
                }
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

    private static bool RootExists(string rootName)    {
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
