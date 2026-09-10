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
    private const string UxmlPath = "Assets/_Project/UI/UXML/MainApp.uxml";
    private const string UssPath = "Assets/_Project/UI/USS/AppTheme.uss";
    private const string OverviewUssPath = "Assets/_Project/UI/USS/OverviewScreen.uss";
    private const string PreparationUssPath = "Assets/_Project/UI/USS/PreparationScreen.uss";
    private const string ProcessingUssPath = "Assets/_Project/UI/USS/ProcessingScreen.uss";
    private const string OutpostUssPath = "Assets/_Project/UI/USS/OutpostScreen.uss";
    private const string PanelSettingsPath = "Assets/_Project/UI/Settings/HubPanelSettings.asset";

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

        ok &= BuildBootScene();
        ok &= BuildHubScene(uxml, uss, overviewUss, preparationUss, processingUss, outpostUss);
        ok &= NereidSurfaceBuilder.BuildSurface();
        ok &= ShipInteriorBuilder.BuildShipInterior();
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
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        var camera = go.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.06f, 0.07f, 1f);
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

    private static PanelSettings CreatePanelSettings()
    {
        if (AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath) != null)
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
        var scenes = new[]
        {
            new EditorBuildSettingsScene(BootScenePath, true),
            new EditorBuildSettingsScene(HubScenePath, true),
            new EditorBuildSettingsScene(surfacePath, true),
            new EditorBuildSettingsScene(shipPath, true)
        };

        EditorBuildSettings.scenes = scenes;

        var current = EditorBuildSettings.scenes;
        bool ok = current != null && current.Length == 4
                  && current[0].path == BootScenePath && current[1].path == HubScenePath
                  && current[2].path == surfacePath && current[3].path == shipPath;
        if (!ok)
        {
            Debug.LogError("[SurviveProjectSetup] build order not applied");
        }
        else
        {
            Debug.Log("[SurviveProjectSetup] build order set: "
                      + "0=00_Boot 1=01_Hub 2=02_NereidSurface 3=03_ShipInterior");
        }

        return ok;
    }
}
