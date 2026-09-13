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
    private const string ShipModelPath =
        "Assets/_Project/Models/MainMenuShip/MainMenuShip.fbx";
    private const string PauseUxmlPath = "Assets/_Project/Resources/ShipPauseMenu.uxml";
    private const string PauseUssPath = "Assets/_Project/Resources/ShipPauseMenu.uss";
    private const string DeployUxmlPath = "Assets/_Project/UI/UXML/ShipDeployConfirm.uxml";
    private const string UssPath = "Assets/_Project/UI/USS/AppTheme.uss";
    private const string OverviewUssPath = "Assets/_Project/UI/USS/OverviewScreen.uss";
    private const string PreparationUssPath = "Assets/_Project/UI/USS/PreparationScreen.uss";
    private const string ProcessingUssPath = "Assets/_Project/UI/USS/ProcessingScreen.uss";
    private const string OutpostUssPath = "Assets/_Project/UI/USS/OutpostScreen.uss";
    private const string CursorDotPath = "Assets/_Project/UI/Cursors/T_CursorDot.png";
    private const string CursorRingPath = "Assets/_Project/UI/Cursors/T_CursorRing.png";
    private const string CursorBlankPath = "Assets/_Project/UI/Cursors/T_CursorBlank.png";
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
        // Phase 09B PASS 3: the navigation radar's standby "?" symbol (the radar
        // ring and its sweep are RadialWristElement mesh geometry).
        "NavRadarGlyph",
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

        // ---- Phase 09B PASS 6: the UI cursor assets ----
        // UI Toolkit applies these as the OS cursor on hover. A cursor texture
        // that is not CPU readable (or is compressed) makes SetCursor fail and
        // silently brings the stock Windows arrow back - which is exactly the
        // bug this pass spent several rounds chasing, so it is pinned here.
        foreach (string cursorPath in new[] { CursorDotPath, CursorRingPath, CursorBlankPath })
        {
            var importer = AssetImporter.GetAtPath(cursorPath) as TextureImporter;
            Record(importer != null, "cursor texture imports: " + cursorPath);
            Record(importer != null && importer.isReadable,
                "cursor texture is CPU readable (SetCursor would fall back to the arrow): " + cursorPath);
            Record(importer != null
                   && importer.textureCompression == TextureImporterCompression.Uncompressed,
                "cursor texture is uncompressed: " + cursorPath);
        }

        var themeDeps = new System.Collections.Generic.List<string>(
            AssetDatabase.GetDependencies(UssPath, true));
        var outpostDeps = new System.Collections.Generic.List<string>(
            AssetDatabase.GetDependencies(OutpostUssPath, true));
        Record(themeDeps.Contains(CursorDotPath) && themeDeps.Contains(CursorRingPath),
            "AppTheme.uss really references the dot / ring cursor assets");
        Record(outpostDeps.Contains(CursorBlankPath),
            "OutpostScreen.uss really references the blank cursor asset");
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

        Record(menuInBuild, "Main Menu scene is an enabled build scene (进入任务 -> Ship Interior)");
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

        // ---- Phase 09B PASS 7 follow-up: ONE planet display name ----
        // Every player-facing surface names the CURRENT planet, and the old
        // display name must not survive anywhere the player can read it. The
        // internal IDs (scene 02_NereidSurface, the surface builder, the routing
        // and the GameSession fields) deliberately keep their NEREID names.
        string[] planetCopySources =
        {
            UxmlPath,
            DeployUxmlPath,
            SurfaceHudUxmlPath,
            SurfaceWristUxmlPath,
            ShipHudUxmlPath,
            MainMenuUxmlPath
        };

        foreach (string path in planetCopySources)
        {
            string fullPath = Path.Combine(Application.dataPath, "..", path);
            if (!File.Exists(fullPath))
            {
                Record(false, "planet copy source exists: " + path);
                continue;
            }

            string copy = File.ReadAllText(fullPath);
            Record(!copy.Contains("NEREID-04"),
                "player-facing copy has no stale planet name: " + path);
        }

        string projectRoot = Path.Combine(Application.dataPath, "..");
        string mainAppCopy = File.ReadAllText(Path.Combine(projectRoot, UxmlPath));
        Record(mainAppCopy.Contains("勘测 " + PlanetIdentity.DisplayName),
            "MainApp.uxml objective names the current planet");
        Record(mainAppCopy.Contains(PlanetIdentity.ExpeditionContext),
            "MainApp.uxml preparation context names the current planet");

        string deployCopy = File.ReadAllText(Path.Combine(projectRoot, DeployUxmlPath));
        Record(deployCopy.Contains("text=\"" + PlanetIdentity.DisplayName + "\""),
            "deployment sheet names the current planet: " + PlanetIdentity.DisplayName);

        string surfaceHudCopy = File.ReadAllText(Path.Combine(projectRoot, SurfaceHudUxmlPath));
        Record(surfaceHudCopy.Contains("text=\"" + PlanetIdentity.DisplayName + "\""),
            "surface HUD names the current planet: " + PlanetIdentity.DisplayName);

        string wristCopy = File.ReadAllText(Path.Combine(projectRoot, SurfaceWristUxmlPath));
        Record(wristCopy.Contains(PlanetIdentity.SurfaceIdentity),
            "wrist identity names the current planet: " + PlanetIdentity.SurfaceIdentity);

        // The airlock prompt reads the same source instead of hardcoding a name.
        string airlockPath = Path.Combine(Application.dataPath,
            "..", "Assets/_Project/Scripts/Gameplay/ShipAirlockInteractable.cs");
        if (File.Exists(airlockPath))
        {
            string airlock = File.ReadAllText(airlockPath);
            Record(airlock.Contains("PlanetIdentity.SurfacePrompt"),
                "airlock prompt reads the shared planet name");
            Record(!airlock.Contains("NEREID-04"),
                "airlock prompt carries no stale planet name");
        }
        else
        {
            Record(false, "airlock interactable exists: " + airlockPath);
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

            // ---- Phase 08 pause menu (loaded from Resources at runtime) ----
            var pauseUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PauseUxmlPath);
            Record(pauseUxml != null, "pause menu uxml imports: " + PauseUxmlPath);
            Record(AssetDatabase.LoadAssetAtPath<StyleSheet>(PauseUssPath) != null,
                "pause menu uss imports: " + PauseUssPath);

            if (pauseUxml != null)
            {
                TemplateContainer pauseTemplate = pauseUxml.Instantiate();
                Label pauseTitle = pauseTemplate.Q<Label>("PauseTitle");
                Record(pauseTitle != null && pauseTitle.text == "暂停",
                    "pause menu title is 暂停 (actual: "
                    + (pauseTitle != null ? pauseTitle.text : "<missing>") + ")");

                string[] pauseOptions = { "继续游戏", "返回主菜单", "退出游戏" };
                string[] pauseButtons =
                {
                    "PauseResumeButton", "PauseMainMenuButton", "PauseQuitButton"
                };
                for (int i = 0; i < pauseButtons.Length; i++)
                {
                    Button option = pauseTemplate.Q<Button>(pauseButtons[i]);
                    bool optionOk = option != null
                                    && option.Q<Label>(className: "pause-label") != null
                                    && option.Q<Label>(className: "pause-label").text
                                    == pauseOptions[i];
                    Record(optionOk, "pause menu option reads " + pauseOptions[i]);
                }

                Record(pauseTemplate.Q<Label>("PauseHint") != null,
                    "pause menu carries the device hint line");
                Record(pauseTemplate.Q<Button>("PauseResumeButton") != null
                       && pauseTemplate.Query<Button>().ToList().Count == 3,
                    "pause menu has exactly three options (no extra items)");
            }

            // The pause menu is created at runtime, so the frozen ship scene must
            // NOT author one (same policy as the entry eyelids).
            Record(CountInScene<ShipPauseMenu>() == 0,
                "03_ShipInterior authors no pause menu (runtime-created, found "
                + CountInScene<ShipPauseMenu>() + ")");

            // ---- Phase 08 §14: the two Airlock device panels ----
            // 出征前准备 must show its real Back binding ("[B] 返回" on a
            // controller / "[Esc] 返回" on the keyboard), rewritten at runtime by
            // ShipDeployConfirmPage for the device in hand.
            var deployUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DeployUxmlPath);
            Record(deployUxml != null, "deployment panel uxml imports: " + DeployUxmlPath);
            if (deployUxml != null)
            {
                TemplateContainer deployTemplate = deployUxml.Instantiate();
                Label deployHint = deployTemplate.Q<Label>(ShipDeployConfirmPage.BackHintName);
                Record(deployHint != null,
                    "出征前准备 carries its Back hint line (element "
                    + ShipDeployConfirmPage.BackHintName + ")");
                Record(deployHint != null && deployHint.text.Contains("返回"),
                    "the 出征前准备 Back hint really reads 返回 (actual: "
                    + (deployHint != null ? deployHint.text : "<missing>") + ")");
                Record(deployTemplate.Q<Button>(ShipDeployConfirmPage.CancelButtonName) != null,
                    "出征前准备 keeps its 取消 button (the same Back action)");
            }

            // Revision 2: the title screen carries 活下去 and exactly four
            // options as real Buttons, and none of the previous cover's chrome.
            Label menuTitle = menuTemplate.Q<Label>(MainMenuNames.Title);
            Record(menuTitle != null && menuTitle.text == "活下去",
                "MainMenu.uxml title is 活下去 (actual: "
                + (menuTitle != null ? menuTitle.text : "<missing>") + ")");

            string[] expectedItems = { "新游戏", "继续游戏", "选项", "退出" };
            for (int i = 0; i < MainMenuNames.MenuItemOrder.Length; i++)
            {
                Button item = menuTemplate.Q<Button>(MainMenuNames.MenuItemOrder[i]);
                bool itemOk = item != null
                              && item.Q<Label>(className: "menu-item-label") != null
                              && item.Q<Label>(className: "menu-item-label").text == expectedItems[i];
                Record(itemOk, "MainMenu.uxml option is a Button reading " + expectedItems[i]);
            }

            Record(menuTemplate.Q<VisualElement>(MainMenuNames.OptionsModal) != null,
                "MainMenu.uxml carries the 选项 modal");

            // The removed chrome must not come back: check the instantiated tree
            // for any of the copy Revision 2 deleted from the title screen.
            var menuCopy = new System.Text.StringBuilder();
            menuTemplate.Query<Label>().ForEach(label => menuCopy.Append(label.text).Append('\n'));
            string copy = menuCopy.ToString();
            foreach (string removed in MainMenuNames.RemovedCopy)
            {
                Record(!copy.Contains(removed),
                    "MainMenu.uxml no longer shows: " + removed);
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

            // Phase 08 §14: ONE authoritative device-panel state. The two Airlock
            // panels (装备整理 / 出征前准备) are the two modes of this single
            // coordinator plus the two existing pages - never a duplicate panel,
            // a second modal manager or a second input system.
            Record(CountInScene<ShipOverlayUi>() == 1,
                "03_ShipInterior has exactly one device-panel authority "
                + "ShipOverlayUi (found " + CountInScene<ShipOverlayUi>() + ")");
            Record(CountInScene<ShipDeployConfirmPage>() == 1,
                "03_ShipInterior has exactly one 出征前准备 page (found "
                + CountInScene<ShipDeployConfirmPage>() + ")");
            Record(CountInScene<ShipLoadoutPage>() == 1,
                "03_ShipInterior has exactly one 装备整理 page (found "
                + CountInScene<ShipLoadoutPage>() + ")");
            Record(CountInScene<EquipmentLockerInteractable>() == 1,
                "03_ShipInterior has exactly one equipment locker interactable "
                + "(found " + CountInScene<EquipmentLockerInteractable>() + ")");

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

            // ---- Phase 08 Revision: the cover is a cinematic deep-space shot ----
            Record(CountInScene<MainMenuShipHero>() == 1,
                "04_MainMenu authors exactly one hero ship (found "
                + CountInScene<MainMenuShipHero>() + ")");
            Record(CountInScene<MainMenuStarfield>() == 1,
                "04_MainMenu authors exactly one starfield (found "
                + CountInScene<MainMenuStarfield>() + ")");
            Record(CountInScene<MainMenuCinematic>() == 1,
                "04_MainMenu authors exactly one start cinematic (found "
                + CountInScene<MainMenuCinematic>() + ")");
            Record(CountInScene<MainMenuCapture>() == 1,
                "04_MainMenu authors the runtime capture probe (found "
                + CountInScene<MainMenuCapture>() + ")");
            Record(AssetDatabase.LoadAssetAtPath<GameObject>(ShipModelPath) != null,
                "hero ship model imported: " + ShipModelPath);

            MainMenuShipHero hero = FirstInScene<MainMenuShipHero>();
            if (hero != null)
            {
                var serialized = new SerializedObject(hero);
                Record(serialized.FindProperty("plumeMaterial") != null
                       && serialized.FindProperty("plumeMaterial").objectReferenceValue != null,
                    "hero ship has the authored additive plume material assigned");
            }

            MainMenuStarfield starfield = FirstInScene<MainMenuStarfield>();
            if (starfield != null)
            {
                var serialized = new SerializedObject(starfield);
                Record(serialized.FindProperty("spriteMaterial") != null
                       && serialized.FindProperty("spriteMaterial").objectReferenceValue != null,
                    "starfield has the authored additive sprite material assigned");
                Record(serialized.FindProperty("stageCamera") != null
                       && serialized.FindProperty("stageCamera").objectReferenceValue != null,
                    "starfield is bound to the stage camera");
            }

            // The stage camera must be a real deep-space camera: near-black
            // background, HDR, post-processing on, and a wide far plane for the
            // 900 m star layer.
            int spaceCameras = 0;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    float lum = 0.2126f * camera.backgroundColor.r
                                + 0.7152f * camera.backgroundColor.g
                                + 0.0722f * camera.backgroundColor.b;
                    if (camera.clearFlags == CameraClearFlags.SolidColor && lum < 0.02f
                        && camera.farClipPlane >= 1000f)
                    {
                        spaceCameras++;
                    }
                }
            }

            Record(spaceCameras == 1,
                "04_MainMenu has exactly one deep-space stage camera (found "
                + spaceCameras + ")");
            Record(CountInScene<Light>() == 3,
                "04_MainMenu authors exactly three stage lights (found "
                + CountInScene<Light>() + ")");
            Record(CountInScene<UnityEngine.Rendering.Volume>() == 1,
                "04_MainMenu authors exactly one post-processing volume (found "
                + CountInScene<UnityEngine.Rendering.Volume>() + ")");
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

        // ---- Phase 09A_2 first-person arms / wrist integration ----
        ValidateFirstPersonArms(Record);

        // ---- Phase 09A_3 block arms finalization ----
        ValidateBlockArms(Record);

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

    /// <summary>
    /// Phase 09A_2 structural contract: the Codex first-person arms asset, its
    /// import contract, the runtime prefab and the wrist presentation bridge all
    /// exist and are wired the way the phase requires - and the arms are never
    /// authored into a scene (they are created at runtime exactly once, so a
    /// "Continue Game" round trip cannot duplicate them).
    /// </summary>
    private static void ValidateFirstPersonArms(Action<bool, string> record)
    {
        const string modelPath = FirstPersonArmsImportContract.ModelPath;
        const string controllerPath = FirstPersonArmsAssetsBuilder.ControllerPath;
        const string prefabPath = FirstPersonArmsAssetsBuilder.PrefabPath;
        const string sourceFbx = SurviveFirstPersonArmsSetup.SourceFbxPath;

        record(File.Exists(modelPath), "first-person arms fbx imported: " + modelPath);
        record(File.Exists(sourceFbx), "codex source fbx present (Artsource): " + sourceFbx);
        record(File.Exists(controllerPath), "arms animator controller exists: " + controllerPath);
        record(File.Exists(prefabPath), "arms prefab exists: " + prefabPath);

        for (int i = 0; i < FirstPersonArmsImportContract.MaterialSlotNames.Length; i++)
        {
            string materialPath = FirstPersonArmsImportContract.MaterialsFolder + "/"
                                  + FirstPersonArmsImportContract.MaterialSlotNames[i] + ".mat";
            record(File.Exists(materialPath),
                "arms material exists: " + FirstPersonArmsImportContract.MaterialSlotNames[i]);
        }

        int layer = LayerMask.NameToLayer(FirstPersonArmsController.ArmsLayerName);
        record(layer > 0, "layer " + FirstPersonArmsController.ArmsLayerName + " exists (index "
                          + layer + ")");

        // ---- import contract ----
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        record(importer != null
               && importer.animationType == ModelImporterAnimationType.Generic,
            "arms fbx imports as a Generic rig");
        record(importer != null && importer.addCollider == false,
            "arms fbx imports without colliders");

        var clips = new List<AnimationClip>();
        Avatar avatar = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (asset is AnimationClip clip
                && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            {
                clips.Add(clip);
            }

            if (asset is Avatar candidate)
            {
                avatar = candidate;
            }
        }

        record(avatar != null && avatar.isValid && !avatar.isHuman,
            "arms avatar is a valid generic avatar");

        for (int i = 0; i < FirstPersonArmsImportContract.ClipNames.Length; i++)
        {
            string name = FirstPersonArmsImportContract.ClipNames[i];
            AnimationClip clip = clips.Find(c => string.Equals(c.name, name, StringComparison.Ordinal));
            bool shouldLoop = FirstPersonArmsImportContract.IsLoopClip(name);
            record(clip != null && clip.isLooping == shouldLoop,
                "clip " + name + (shouldLoop ? " loops" : " does not loop"));
        }

        // ---- prefab contract ----
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab != null)
        {
            var animator = prefab.GetComponentInChildren<Animator>(true);
            record(animator != null && animator.avatar != null && animator.avatar.isValid
                   && animator.runtimeAnimatorController != null && !animator.applyRootMotion,
                "prefab animator: generic avatar + controller + Root Motion OFF");
            record(prefab.GetComponent<FirstPersonArmsController>() != null,
                "prefab root carries FirstPersonArmsController");
            record(prefab.GetComponentsInChildren<Collider>(true).Length == 0,
                "prefab authors no collider");

            var skinnedRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bool boundsOk = skinnedRenderers.Length > 0;
            foreach (SkinnedMeshRenderer skinned in skinnedRenderers)
            {
                if (!skinned.updateWhenOffscreen)
                {
                    boundsOk = false;
                }
            }

            record(boundsOk,
                "every skinned renderer recomputes its bounds (the raised arm can never be culled)");

            Transform device = null;
            foreach (Transform transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == "WristDevice_L")
                {
                    device = transform;
                    break;
                }
            }

            record(device != null, "prefab exposes WristDevice_L");
        }

        // ---- runtime ownership: never authored into a scene ----
        EditorSceneManager.OpenScene(ShipScenePath, OpenSceneMode.Single);
        record(CountInScene<FirstPersonArmsController>() == 0,
            "03_ShipInterior authors no arms instance (created at runtime exactly once)");
        record(CountInScene<WristPresentationBridge>() == 0,
            "03_ShipInterior authors no wrist bridge (created at runtime)");
        record(CountInScene<SurfaceWristController>() == 1,
            "03_ShipInterior keeps exactly one existing wrist terminal host");

        Camera eye = null;
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            if (camera.CompareTag("MainCamera"))
            {
                eye = camera;
            }
        }

        record(eye != null && (eye.cullingMask & (1 << layer)) != 0,
            "the authoritative eye camera renders the arms layer");
        record(eye != null && Mathf.Abs(eye.fieldOfView - 70f) < 0.01f,
            "the eye camera keeps the Phase 08 FOV of 70");
    }

    /// <summary>
    /// Phase 09A_3 structural contract: the runtime first-person presentation is
    /// the code-built block arms; the legacy FBX arms are history and must not be
    /// authored into the scene; the scene keeps one authoritative eye camera, no
    /// duplicate AudioListener, and the hologram still renders above the opaque
    /// arms in the transparent queue.
    /// </summary>
    private static void ValidateBlockArms(Action<bool, string> record)
    {
        EditorSceneManager.OpenScene(ShipScenePath, OpenSceneMode.Single);

        record(CountInScene<FirstPersonBlockArmsController>() == 0,
            "03_ShipInterior authors no block arms (created at runtime exactly once)");
        record(CountInScene<FirstPersonArmsController>() == 0,
            "03_ShipInterior authors no legacy FBX arms either");

        int layer = LayerMask.NameToLayer(FirstPersonBlockArmsController.ArmsLayerName);
        record(layer > 0, "layer " + FirstPersonBlockArmsController.ArmsLayerName
                          + " exists (index " + layer + ")");

        Camera eye = null;
        int listeners = 0;
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            if (camera.CompareTag("MainCamera"))
            {
                eye = camera;
            }

            if (camera.GetComponent<AudioListener>() != null)
            {
                listeners++;
            }
        }

        record(eye != null, "the ship interior keeps an authoritative eye camera");
        record(listeners <= 1, "no duplicate AudioListener (found " + listeners + ")");
        record(eye != null && (eye.cullingMask & (1 << layer)) != 0,
            "the eye camera renders the FirstPersonArms layer");

        SurfaceWristController wrist = Object.FindAnyObjectByType<SurfaceWristController>(
            FindObjectsInactive.Include);
        record(wrist != null, "the existing wrist terminal host is present");

        // The controller resolves its display anchor at RUNTIME, so in the editor
        // it is still null: resolve it here instead of silently skipping the
        // render-order contract (which is exactly what used to happen).
        WristDisplayAnchor anchor = wrist != null ? wrist.Display : null;
        if (anchor == null)
        {
            anchor = Object.FindAnyObjectByType<WristDisplayAnchor>(
                FindObjectsInactive.Include);
        }

        record(anchor != null, "the wrist display anchor is authored in the ship scene");
        if (anchor != null)
        {
            // The anchor's material is bound at RUNTIME, so the editor can only
            // pin the shader contract plus the arm's queue. The on-screen proof
            // that the hologram really composites over the physical arm is
            // measured by the 09A_3 probe (hologram readability: the readable
            // pixels of the map disc are identical with the arm visible and with
            // the arm hidden).
            Shader hologramShader = Shader.Find("Survive/WristDisplayUnlit");
            record(hologramShader != null,
                "the wrist hologram shader exists (Survive/WristDisplayUnlit)");
            int hologramQueue = hologramShader != null
                ? hologramShader.renderQueue
                : (int)UnityEngine.Rendering.RenderQueue.Transparent;
            record(hologramShader != null
                   && hologramQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent,
                "the wrist hologram shader is authored in the transparent queue (queue "
                + hologramQueue + "), i.e. after the opaque block arms");

            // The softened arm is alpha blended, so it also lives in the
            // transparent range - but it must be composited BEFORE the hologram:
            // the UI is the interface and the arm is only its support, so the
            // readable content may never be covered by the physical watch.
            record(FirstPersonBlockArmsController.SoftenedArmRenderQueue < hologramQueue,
                "the softened block arm renders before the hologram (queue "
                + FirstPersonBlockArmsController.SoftenedArmRenderQueue + " < "
                + hologramQueue + "), so the UI always composites over the arm");
        }
        else
        {
            record(false, "the wrist hologram render-order contract could not be verified "
                          + "(no WristDisplayAnchor found)");
        }

        EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
        record(CountInScene<Ship09A3ShotProbe>() == 1,
            "00_Boot hosts exactly one Phase 09A_3 evidence probe (inert unless flagged)");

        ValidateEvidenceProbeHygiene(record);

        EditorSceneManager.OpenScene(ShipScenePath, OpenSceneMode.Single);
    }

    /// <summary>
    /// Phase 09A_3 UI hotfix guard: no evidence probe may park a UIDocument
    /// through <c>enabled</c>.
    ///
    /// No UXML in this project carries a &lt;Style&gt; reference and none hides
    /// anything by default: every controller attaches its style sheets and
    /// applies its display state in code at bind time (AppController shows one
    /// of its four screens, ShipPauseMenu hides its overlay). Disabling and
    /// re-enabling a document therefore rebuilds the visual tree from the UXML
    /// alone and silently drops all of it - the game comes back unstyled with
    /// several screens stacked and the pause overlay stuck on screen. That is
    /// exactly what the Phase 09A_2 arms-mask measurement did.
    ///
    /// Probes must hide screen-space panels through a restorable style property
    /// instead (see UiIntegrityAudit and the mask capture in the 09A_3 probe).
    /// </summary>
    private static void ValidateEvidenceProbeHygiene(Action<bool, string> record)
    {
        string[] probeFolders =
        {
            "Assets/_Project/Scripts/Core",
            "Assets/_Project/Scripts/Gameplay"
        };

        int scanned = 0;
        var offenders = new System.Collections.Generic.List<string>();
        foreach (string folder in probeFolders)
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), folder);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (!name.Contains("Probe") && !name.Contains("Smoke")
                    && !name.Contains("Capture"))
                {
                    continue;
                }

                scanned++;
                string source = File.ReadAllText(file);
                if (source.Contains("document.enabled = false")
                    || source.Contains("Document.enabled = false"))
                {
                    offenders.Add(name);
                }
            }
        }

        record(scanned > 0, "evidence probes found for the UI hygiene audit (" + scanned + " files)");
        record(offenders.Count == 0,
            "no evidence probe parks a UIDocument through enabled (offenders: "
            + (offenders.Count == 0 ? "none" : string.Join(", ", offenders.ToArray()))
            + ")");
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
