using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Deterministic, idempotent Phase 01 scene + UI authoring.
///
/// Regenerates the project-owned scenes from scratch every run so repeated
/// execution converges to the same authored state (no duplicate objects,
/// lights, UIDocuments or build entries).
///
/// Run via  Tools/SurviveUI/Setup Phase 01  or  -executeMethod SurviveProjectSetup.RunCli
/// </summary>
public static class SurviveProjectSetup
{
    private const string ScenesFolder = "Assets/_Project/Scenes";
    private const string BootScenePath = ScenesFolder + "/00_Boot.unity";
    private const string HubScenePath = ScenesFolder + "/01_Hub.unity";
    private const string MainMenuScenePath = ScenesFolder + "/04_MainMenu.unity";
    private const string UxmlPath = "Assets/_Project/UI/UXML/MainApp.uxml";
    private const string UssPath = "Assets/_Project/UI/USS/AppTheme.uss";
    private const string OverviewUssPath = "Assets/_Project/UI/USS/OverviewScreen.uss";
    private const string PreparationUssPath = "Assets/_Project/UI/USS/PreparationScreen.uss";
    private const string ProcessingUssPath = "Assets/_Project/UI/USS/ProcessingScreen.uss";
    private const string OutpostUssPath = "Assets/_Project/UI/USS/OutpostScreen.uss";
    private const string PanelSettingsPath = "Assets/_Project/UI/Settings/HubPanelSettings.asset";
    private const string MainMenuPanelSettingsPath =
        "Assets/_Project/UI/Settings/MainMenuPanelSettings.asset";
    private const string MainMenuUxmlPath = "Assets/_Project/UI/UXML/MainMenu.uxml";
    private const string MainMenuUssPath = "Assets/_Project/UI/USS/MainMenu.uss";

    [MenuItem("Tools/SurviveUI/Setup Phase 01 Project")]
    public static void RunFromMenu()
    {
        if (RunSetup())
        {
            Debug.Log("[SurviveProjectSetup] SUCCESS");
        }
        else
        {
            Debug.LogError("[SurviveProjectSetup] FAILED");
        }
    }

    /// <summary>CLI entry used by batch automation.</summary>
    public static void RunCli()
    {
        Debug.Log("[SurviveProjectSetup] RunCli start");
        RunSetup();
        Debug.Log("[SurviveProjectSetup] RunCli end");
    }

    /// <summary>
    /// CLI entry that rebuilds ONLY the Phase 08 Main Menu scene and re-applies
    /// the build order. The Phase 01-07 scenes are frozen green baselines, so
    /// Phase 08 tooling never regenerates them: the menu is added to the same
    /// deterministic builder without touching already-validated scene content.
    /// </summary>
    public static void RunMainMenuCli()
    {
        Debug.Log("[SurviveProjectSetup] RunMainMenuCli start");
        bool ok = EnsureFolder(ScenesFolder)
                  & EnsureFolder("Assets/_Project/UI/UXML")
                  & EnsureFolder("Assets/_Project/UI/USS");
        ok &= BuildMainMenuScene();
        ok &= SetBuildOrder();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(ok
            ? "[SurviveProjectSetup] main menu setup OK"
            : "[SurviveProjectSetup] main menu setup reported failures above");
    }

    public static bool RunSetup()
    {
        bool ok = true;
        ok &= EnsureFolder("Assets/_Project");
        ok &= EnsureFolder(ScenesFolder);
        ok &= EnsureFolder("Assets/_Project/UI/UXML");
        ok &= EnsureFolder("Assets/_Project/UI/USS");
        ok &= EnsureFolder("Assets/_Project/UI/Settings");

        // Source assets authored as text; verify they exist and are imported.
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
        var overviewUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(OverviewUssPath);
        var preparationUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(PreparationUssPath);
        var processingUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ProcessingUssPath);
        var outpostUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(OutpostUssPath);
        if (uxml == null)
        {
            Debug.LogError("[SurviveProjectSetup] missing VisualTreeAsset: " + UxmlPath);
            ok = false;
        }

        if (uss == null)
        {
            Debug.LogError("[SurviveProjectSetup] missing StyleSheet: " + UssPath);
            ok = false;
        }

        if (overviewUss == null)
        {
            Debug.LogError("[SurviveProjectSetup] missing StyleSheet: " + OverviewUssPath);
            ok = false;
        }

        if (preparationUss == null)
        {
            Debug.LogError("[SurviveProjectSetup] missing StyleSheet: " + PreparationUssPath);
            ok = false;
        }

        if (processingUss == null)
        {
            Debug.LogError("[SurviveProjectSetup] missing StyleSheet: " + ProcessingUssPath);
            ok = false;
        }

        if (outpostUss == null)
        {
            Debug.LogError("[SurviveProjectSetup] missing StyleSheet: " + OutpostUssPath);
            ok = false;
        }

        // Phase 08 Main Menu assets (verified here so a full setup also reports
        // a missing cover instead of silently skipping the scene).
        if (AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainMenuUxmlPath) == null)
        {
            Debug.LogError("[SurviveProjectSetup] missing VisualTreeAsset: " + MainMenuUxmlPath);
            ok = false;
        }

        if (AssetDatabase.LoadAssetAtPath<StyleSheet>(MainMenuUssPath) == null)
        {
            Debug.LogError("[SurviveProjectSetup] missing StyleSheet: " + MainMenuUssPath);
            ok = false;
        }

        ok &= BuildBootScene();
        ok &= BuildHubScene(uxml, uss, overviewUss, preparationUss, processingUss, outpostUss);
        ok &= NereidSurfaceBuilder.BuildSurface();
        ok &= ShipInteriorBuilder.BuildShipInterior();
        ok &= BuildMainMenuScene();
        ok &= SetBuildOrder();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(ok
            ? "[SurviveProjectSetup] setup OK"
            : "[SurviveProjectSetup] setup reported failures above");
        return ok;
    }

    private static bool EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return true;
        }

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        string created = AssetDatabase.CreateFolder(parent, leaf);
        if (string.IsNullOrEmpty(created))
        {
            Debug.LogError("[SurviveProjectSetup] failed to create folder " + path);
            return false;
        }

        Debug.Log("[SurviveProjectSetup] created folder " + path);
        return true;
    }

    private static Scene NewScene()
    {
        return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    private static void AddCamera(Scene scene, string name)
    {
        AddCamera(scene, name, new Color(0.05f, 0.06f, 0.07f, 1f));
    }

    private static void AddCamera(Scene scene, string name, Color background)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        var camera = go.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = background;
    }

    private static bool BuildBootScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath) != null)
        {
            AssetDatabase.DeleteAsset(BootScenePath);
        }

        Scene scene = NewScene();
        AddCamera(scene, "Main Camera");

        var bootstrap = new GameObject("Bootstrap");
        SceneManager.MoveGameObjectToScene(bootstrap, scene);
        bootstrap.AddComponent<BootController>();

        // Cross-scene surface automation probe (inert unless -autoSurface).
        var probe = new GameObject("AutoSurfaceFlowProbe");
        SceneManager.MoveGameObjectToScene(probe, scene);
        probe.AddComponent<AutoSurfaceFlowProbe>();

        // Clean combined gameplay screenshot probe (inert unless -surfaceShot).
        var shotProbe = new GameObject("SurfaceShotProbe");
        SceneManager.MoveGameObjectToScene(shotProbe, scene);
        shotProbe.AddComponent<SurfaceShotProbe>();

        // Phase 07 ship graybox screenshot probe (inert unless -shipShot).
        var shipShotProbe = new GameObject("ShipShotProbe");
        SceneManager.MoveGameObjectToScene(shipShotProbe, scene);
        shipShotProbe.AddComponent<ShipShotProbe>();

        // Phase 07B.2 release evidence probe (inert unless -ship07B2Shot).
        var b2ShotProbe = new GameObject("ShipB2ShotProbe");
        SceneManager.MoveGameObjectToScene(b2ShotProbe, scene);
        b2ShotProbe.AddComponent<ShipB2ShotProbe>();

        // Phase 07B.2.1 render-recovery probe (inert unless -ship07B21Shot).
        var b21ShotProbe = new GameObject("ShipB21ShotProbe");
        SceneManager.MoveGameObjectToScene(b21ShotProbe, scene);
        b21ShotProbe.AddComponent<ShipB21ShotProbe>();

        // Phase 07B.2.2 release UV / world-occlusion probe (inert unless
        // -ship07B22Shot).
        var b22ShotProbe = new GameObject("ShipB22ShotProbe");
        SceneManager.MoveGameObjectToScene(b22ShotProbe, scene);
        b22ShotProbe.AddComponent<ShipB22ShotProbe>();

        // Phase 07B.2.3 release pointer/UI-flow probe (inert unless
        // -ship07B23Shot).
        var b23ShotProbe = new GameObject("ShipB23ShotProbe");
        SceneManager.MoveGameObjectToScene(b23ShotProbe, scene);
        b23ShotProbe.AddComponent<ShipB23ShotProbe>();

        // Phase 07B.2.4 release rotating-planet-preview probe (inert unless
        // -overviewPlanetShot).
        var planetShotProbe = new GameObject("OverviewPlanetShotProbe");
        SceneManager.MoveGameObjectToScene(planetShotProbe, scene);
        planetShotProbe.AddComponent<OverviewPlanetShotProbe>();

        // Phase 07A.1 normal human-runtime routing probe (inert unless -shipFlow).
        var shipFlowProbe = new GameObject("ShipFlowProbe");
        SceneManager.MoveGameObjectToScene(shipFlowProbe, scene);
        shipFlowProbe.AddComponent<ShipFlowProbe>();

        if (!EditorSceneManager.SaveScene(scene, BootScenePath))
        {
            Debug.LogError("[SurviveProjectSetup] failed to save " + BootScenePath);
            return false;
        }

        Debug.Log("[SurviveProjectSetup] wrote " + BootScenePath);
        return true;
    }

    private static bool BuildHubScene(VisualTreeAsset uxml, StyleSheet uss, StyleSheet overviewUss,
        StyleSheet preparationUss, StyleSheet processingUss, StyleSheet outpostUss)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(HubScenePath) != null)
        {
            AssetDatabase.DeleteAsset(HubScenePath);
        }

        Scene scene = NewScene();
        AddCamera(scene, "Main Camera");

        // Panel settings: 1920x1080 reference, scale with screen.
        var panelSettings = CreatePanelSettings();
        if (panelSettings == null)
        {
            return false;
        }

        // Primary UI application.
        var ui = new GameObject("UI");
        SceneManager.MoveGameObjectToScene(ui, scene);
        var document = ui.AddComponent<UIDocument>();
        document.visualTreeAsset = uxml;
        document.panelSettings = panelSettings;
        document.sortingOrder = 0;

        var app = ui.AddComponent<AppController>();
        app.Configure(uss, overviewUss, preparationUss, processingUss, outpostUss);

        // Standalone smoke probe (inert unless launched with -smokeResult).
        var smoke = new GameObject("StandaloneSmoke");
        SceneManager.MoveGameObjectToScene(smoke, scene);
        smoke.AddComponent<StandaloneSmoke>();

        // Overview screenshot/layout probe (inert unless launched with -captureDir).
        var capture = new GameObject("OverviewCapture");
        SceneManager.MoveGameObjectToScene(capture, scene);
        capture.AddComponent<OverviewCapture>();

        if (!EditorSceneManager.SaveScene(scene, HubScenePath))
        {
            Debug.LogError("[SurviveProjectSetup] failed to save " + HubScenePath);
            return false;
        }

        Debug.Log("[SurviveProjectSetup] wrote " + HubScenePath);
        return true;
    }

    /// <summary>
    /// Phase 08: deterministic Main Menu scene (04_MainMenu).
    ///
    /// One UIDocument bound to MainMenu.uxml + MainMenuController, plus the
    /// runtime smoke probe. No EventSystem, no AudioListener and no authored
    /// GameSession: the menu is presentation only and reuses the persistent
    /// Boot session exactly like every other scene.
    ///
    /// Re-running this builder converges to the same scene (the previous asset
    /// is deleted first, so no "MainMenu (1)" duplicates can accumulate).
    /// </summary>
    private static bool BuildMainMenuScene()
    {
        // Existence check only: the real references are loaded AFTER the scene
        // switch. Switching scenes unloads assets that no open scene references,
        // which would leave a destroyed PanelSettings object behind and silently
        // serialize a null panel into the new scene.
        if (AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainMenuUxmlPath) == null)
        {
            Debug.LogError("[SurviveProjectSetup] missing VisualTreeAsset: " + MainMenuUxmlPath);
            return false;
        }

        if (AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath) == null
            || AssetDatabase.LoadAssetAtPath<StyleSheet>(MainMenuUssPath) == null)
        {
            Debug.LogError("[SurviveProjectSetup] missing Main Menu style sheet ("
                           + UssPath + " / " + MainMenuUssPath + ")");
            return false;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) != null)
        {
            AssetDatabase.DeleteAsset(MainMenuScenePath);
        }

        Scene scene = NewScene();
        // Light canvas clear color: the menu panel covers the frame, so this is
        // only the safety color behind it (never a dark flash).
        AddCamera(scene, "Main Camera", new Color(0.914f, 0.922f, 0.929f, 1f));

        // Fresh asset references, taken after the scene switch.
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainMenuUxmlPath);
        var theme = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
        var menuUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(MainMenuUssPath);

        // The Main Menu owns its OWN panel settings asset with the same
        // configuration as the Hub (1920x1080 reference, scale with screen
        // size). Sharing one PanelSettings instance across two scenes is not
        // safe: the second panel can hang while the first panel is being torn
        // down. The Hub asset itself is never regenerated (it is referenced by
        // the frozen 01_Hub scene).
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(MainMenuPanelSettingsPath);
        if (panelSettings == null)
        {
            panelSettings = CreateMainMenuPanelSettings();
        }

        if (uxml == null || theme == null || menuUss == null || panelSettings == null)
        {
            Debug.LogError("[SurviveProjectSetup] Main Menu assets unavailable after scene switch");
            return false;
        }

        var ui = new GameObject("UI");
        SceneManager.MoveGameObjectToScene(ui, scene);
        var document = ui.AddComponent<UIDocument>();
        document.visualTreeAsset = uxml;
        document.panelSettings = panelSettings;
        document.sortingOrder = 0;

        var controller = ui.AddComponent<MainMenuController>();
        controller.Configure(theme, menuUss);

        // Standalone runtime smoke probe (inert unless launched with -smokeResult).
        var smoke = new GameObject("StandaloneSmoke");
        SceneManager.MoveGameObjectToScene(smoke, scene);
        smoke.AddComponent<StandaloneSmoke>();

        if (!EditorSceneManager.SaveScene(scene, MainMenuScenePath))
        {
            Debug.LogError("[SurviveProjectSetup] failed to save " + MainMenuScenePath);
            return false;
        }

        Debug.Log("[SurviveProjectSetup] wrote " + MainMenuScenePath);
        return true;
    }

    /// <summary>
    /// Creates the Main Menu's own PanelSettings asset, copying the Hub's panel
    /// configuration (including its runtime theme) so both UI applications scale
    /// and theme identically while owning separate panel instances.
    /// </summary>
    private static PanelSettings CreateMainMenuPanelSettings()
    {
        var hub = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panelSettings.match = 0.5f;
        if (hub != null)
        {
            panelSettings.themeStyleSheet = hub.themeStyleSheet;
        }

        AssetDatabase.CreateAsset(panelSettings, MainMenuPanelSettingsPath);
        EditorUtility.SetDirty(panelSettings);
        Debug.Log("[SurviveProjectSetup] wrote " + MainMenuPanelSettingsPath
                  + " (theme=" + (panelSettings.themeStyleSheet != null
                      ? panelSettings.themeStyleSheet.name
                      : "<null>") + ")");
        return panelSettings;
    }

    private static PanelSettings CreatePanelSettings()
    {        if (AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath) != null)
        {
            AssetDatabase.DeleteAsset(PanelSettingsPath);
        }

        var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panelSettings.match = 0.5f;

        AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
        EditorUtility.SetDirty(panelSettings);
        Debug.Log("[SurviveProjectSetup] wrote " + PanelSettingsPath);
        return panelSettings;
    }

    private static bool SetBuildOrder()
    {
        string surfacePath = "Assets/_Project/Scenes/02_NereidSurface.unity";
        string shipPath = "Assets/_Project/Scenes/03_ShipInterior.unity";
        // Phase 08 appends the Main Menu at index 4: indices 0-3 (the frozen
        // Phase 01-07 scenes, with 00_Boot as the launch scene) keep their
        // exact order, and the menu is loaded by name through SceneRouting.
        var scenes = new[]
        {
            new EditorBuildSettingsScene(BootScenePath, true),
            new EditorBuildSettingsScene(HubScenePath, true),
            new EditorBuildSettingsScene(surfacePath, true),
            new EditorBuildSettingsScene(shipPath, true),
            new EditorBuildSettingsScene(MainMenuScenePath, true)
        };

        EditorBuildSettings.scenes = scenes;

        var current = EditorBuildSettings.scenes;
        bool ok = current != null && current.Length == 5
                  && current[0].path == BootScenePath && current[1].path == HubScenePath
                  && current[2].path == surfacePath && current[3].path == shipPath
                  && current[4].path == MainMenuScenePath;
        if (!ok)
        {
            Debug.LogError("[SurviveProjectSetup] build order not applied");
        }
        else
        {
            Debug.Log("[SurviveProjectSetup] build order set: "
                      + "0=00_Boot 1=01_Hub 2=02_NereidSurface 3=03_ShipInterior "
                      + "4=04_MainMenu");
        }

        return ok;
    }
}
