using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
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
    /// Phase 08 Revision: deterministic Main Menu cover (04_MainMenu).
    ///
    /// The cover is a CINEMATIC DEEP-SPACE SHOT, not a flat UI page:
    ///
    ///   - a perspective stage camera frames the hero ship on the LEFT,
    ///   - the real main-menu ship asset (Artsource/MainMenuShip -> FBX under
    ///     Assets/_Project/Models) floats in the middle-left with its two
    ///     authored exhaust anchors driving additive thruster plumes,
    ///   - a procedural three-layer starfield sits behind everything,
    ///   - the UI Toolkit document paints only the RIGHT menu column, the
    ///     hairline chrome and the two modals (root + left zone transparent),
    ///   - MainMenuCinematic owns 进入任务: menu fade -> engine boost -> ship
    ///     departure -> blackout -> first-person ship interior.
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

        if (!EnsureMainMenuShipModel())
        {
            return false;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) != null)
        {
            AssetDatabase.DeleteAsset(MainMenuScenePath);
        }

        Scene scene = NewScene();

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

        // ---- Stage: camera, deep-space lighting, hero ship, starfield ----
        Camera stageCamera = BuildMainMenuStage(scene, out MainMenuShipHero hero,
            out MainMenuStarfield starfield);

        // ---- UI: the right-hand menu, painted over the stage ----
        var ui = new GameObject("UI");
        SceneManager.MoveGameObjectToScene(ui, scene);
        var document = ui.AddComponent<UIDocument>();
        document.visualTreeAsset = uxml;
        document.panelSettings = panelSettings;
        document.sortingOrder = 0;

        var controller = ui.AddComponent<MainMenuController>();
        controller.Configure(theme, menuUss);

        // ---- Start cinematic (进入任务 -> ship interior) ----
        var cinematicGo = new GameObject("MainMenuCinematic");
        SceneManager.MoveGameObjectToScene(cinematicGo, scene);
        var cinematic = cinematicGo.AddComponent<MainMenuCinematic>();
        cinematic.Configure(controller, hero, starfield, stageCamera);

        // Standalone runtime smoke probe (inert unless launched with -smokeResult).
        var smoke = new GameObject("StandaloneSmoke");
        SceneManager.MoveGameObjectToScene(smoke, scene);
        smoke.AddComponent<StandaloneSmoke>();

        // Runtime capture probe (inert unless launched with -menuCaptureDir).
        var capture = new GameObject("MainMenuCapture");
        SceneManager.MoveGameObjectToScene(capture, scene);
        capture.AddComponent<MainMenuCapture>();

        if (!EditorSceneManager.SaveScene(scene, MainMenuScenePath))
        {
            Debug.LogError("[SurviveProjectSetup] failed to save " + MainMenuScenePath);
            return false;
        }

        Debug.Log("[SurviveProjectSetup] wrote " + MainMenuScenePath);
        return true;
    }

    // ---- Phase 08 Revision: Main Menu stage (hero ship in deep space) ----

    /// <summary>Ship asset the cover uses as its hero (authored in Artsource).</summary>
    private const string ShipModelFolder = "Assets/_Project/Models/MainMenuShip";
    private const string ShipModelPath = ShipModelFolder + "/MainMenuShip.fbx";
    private const string ShipModelSourcePath =
        "Artsource/MainMenuShip/output/MainMenuShip.fbx";
    private const string MenuMaterialsFolder = "Assets/_Project/Materials";

    /// <summary>World-space direction the ship's exhaust points. The camera looks
    /// back along -Z, so its screen right is world -X: the aft therefore points
    /// towards +X (screen LEFT) and the NOSE points screen RIGHT, which is the
    /// Revision 2 hard requirement - the ship cruises and departs LEFT to RIGHT
    /// while both plumes stay visible streaming backwards.</summary>
    private static readonly Vector3 HeroAftDirection =
        new Vector3(0.94f, 0.06f, 0.34f).normalized;

    /// <summary>Initial boom length; the builder solves the final distance.</summary>
    private const float HeroCameraDistance = 32f;

    /// <summary>Camera sits this far above the ship centre (on the boom).</summary>
    private const float HeroCameraHeight = 4.3f;

    private const float HeroCameraFov = 40f;

    /// <summary>Silhouette the framing solver aims for, in viewport units.</summary>
    private const float HeroTargetWidth = 0.56f;
    private const float HeroTargetCenterX = 0.345f;
    private const float HeroTargetCenterY = 0.48f;

    /// <summary>
    /// Ensures the exported ship model exists inside the project. The Blender
    /// source of truth lives in <c>Artsource/MainMenuShip</c>; Unity can only
    /// import assets under Assets/, so the FBX is copied in (never the .blend,
    /// per WORKFLOW §4.1) and the copy is reported.
    /// </summary>
    private static bool EnsureMainMenuShipModel()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ShipModelPath) != null)
        {
            return true;
        }

        string absoluteSource = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", ShipModelSourcePath));
        if (!File.Exists(absoluteSource))
        {
            Debug.LogError("[SurviveProjectSetup] ship model missing and no source to "
                           + "import: " + absoluteSource);
            return false;
        }

        EnsureFolder("Assets/_Project/Models");
        EnsureFolder(ShipModelFolder);
        File.Copy(absoluteSource, Path.GetFullPath(ShipModelPath), true);
        AssetDatabase.ImportAsset(ShipModelPath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        Debug.Log("[SurviveProjectSetup] imported hero ship model: " + ShipModelPath);

        return AssetDatabase.LoadAssetAtPath<GameObject>(ShipModelPath) != null;
    }

    private static Camera BuildMainMenuStage(Scene scene, out MainMenuShipHero hero,
        out MainMenuStarfield starfield)
    {
        // Deep space is dark: the camera clears to near-black and the backdrop
        // is real geometry, so nothing here depends on the built-in skybox.
        var cameraGo = new GameObject("Main Menu Camera");
        SceneManager.MoveGameObjectToScene(cameraGo, scene);
        var camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.004f, 0.007f, 0.013f, 1f);
        camera.fieldOfView = HeroCameraFov;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 4000f;
        camera.allowHDR = true;

        var cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderType = CameraRenderType.Base;
        cameraData.renderPostProcessing = true;
        cameraData.volumeLayerMask = ~0;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;

        // The scene has no skybox: ambient is an explicit flat space blue, so
        // the ship's shadow side stays readable instead of turning black.
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.075f, 0.098f, 0.135f, 1f);
        RenderSettings.fog = false;
        RenderSettings.defaultReflectionMode =
            UnityEngine.Rendering.DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;

        BuildMainMenuLights(scene);
        BuildMainMenuVolume(scene);

        // ---- Hero ship ----
        var heroGo = new GameObject("MainMenuShipHero");
        SceneManager.MoveGameObjectToScene(heroGo, scene);
        hero = heroGo.AddComponent<MainMenuShipHero>();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ShipModelPath);
        if (model == null)
        {
            Debug.LogError("[SurviveProjectSetup] ship model not importable: " + ShipModelPath);
            starfield = null;
            return camera;
        }

        var ship = Object.Instantiate(model);
        ship.name = "SK_MainMenuShip";
        ship.transform.SetParent(heroGo.transform, false);

        // Materials: the FBX round-trip names its slots MAT_Hull.001 etc., so the
        // mapping is by prefix and every renderer is re-pointed at a project
        // URP material (the imported ones cannot be authored or tuned).
        AssignShipMaterials(ship);

        // Orientation is MEASURED from the asset's own exhaust anchors instead of
        // assumed, so the pose survives any FBX axis convention.
        Vector3 aft = MeasureAft(ship.transform, out bool anchorsFound);
        if (!anchorsFound)
        {
            Debug.LogError("[SurviveProjectSetup] ship exhaust anchors missing in "
                           + ShipModelPath + " - the cover needs both plumes");
        }

        heroGo.transform.rotation = Quaternion.FromToRotation(aft, HeroAftDirection);
        heroGo.transform.position = Vector3.zero;

        Bounds bounds = MeasureShipBounds(ship.transform);
        Vector3 center = bounds.center;

        // The framing is SOLVED, not guessed: the solver measures the projected
        // silhouette and moves the camera until the ship fills the left part of
        // the frame with the menu column left clear. That keeps the composition
        // correct even if the asset is re-exported with different proportions.
        FrameHero(camera, ship.transform, center, out string framing);

        // ---- Starfield ----
        var starGo = new GameObject("MainMenuStarfield");
        SceneManager.MoveGameObjectToScene(starGo, scene);
        starfield = starGo.AddComponent<MainMenuStarfield>();
        starfield.Configure(camera, EnsureStarSpriteMaterial(), 20260801);

        // ---- Hero wiring ----
        var plume = EnsurePlumeMaterial();
        hero.ConfigureShip(ship.transform, plume);

        // ---- Composition report (the builder proves the layout numerically) ----
        Debug.Log("[SurviveProjectSetup] main menu stage: shipBounds="
                  + bounds.size.ToString("F2") + " center=" + center.ToString("F2")
                  + " aft=" + aft.ToString("F3") + " anchors=" + anchorsFound
                  + " camera=" + camera.transform.position.ToString("F2")
                  + " | " + framing);

        if (!framing.Contains("OK"))
        {
            Debug.LogWarning("[SurviveProjectSetup] main menu framing out of contract: "
                             + framing);
        }

        return camera;
    }

    /// <summary>
    /// Solves the cover camera: the ship must fill the LEFT of the frame and stop
    /// short of the menu column (which starts at 66% of the frame width). The
    /// solver iterates - it shrinks/grows the boom to hit the target silhouette
    /// WIDTH and then nudges the aim until the silhouette CENTRE lands where the
    /// composition wants it - because a projection is not linear in the camera
    /// shift for a 24 m object seen from 30 m.
    /// </summary>
    private static void FrameHero(Camera camera, Transform ship, Vector3 center,
        out string report)
    {
        // The composition is AUTHORED for the 16:9 reference the UI panel uses.
        // The solving editor's Game View aspect is not a product property (a
        // batch-mode Game View can be square), so the projection is pinned here
        // and the authored pose is left with no aspect override.
        camera.aspect = 16f / 9f;

        Vector3 boom = new Vector3(0f, HeroCameraHeight, HeroCameraDistance).normalized;
        float distance = HeroCameraDistance;
        float shiftRight = 0f;
        float shiftUp = 0f;
        Rect rect = new Rect(0.5f, 0.5f, 0f, 0f);

        for (int iteration = 0; iteration < 5; iteration++)
        {
            camera.transform.position = center + boom * distance;
            Vector3 right = camera.transform.right;
            Vector3 up = camera.transform.up;
            Vector3 aim = center + right * shiftRight + up * shiftUp;
            camera.transform.rotation = Quaternion.LookRotation(aim - camera.transform.position,
                Vector3.up);

            rect = MeasureSilhouette(camera, ship);
            if (rect.width <= 0.0001f)
            {
                camera.ResetAspect();
                report = "ship silhouette could not be projected";
                return;
            }

            // World metres per viewport unit at the ship's plane.
            float metresPerUnit = 2f * distance
                                  * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad)
                                  * Mathf.Max(1f, camera.aspect);

            if (iteration == 0)
            {
                // Scale the boom so the silhouette reaches the target width.
                distance *= Mathf.Clamp(rect.width / HeroTargetWidth, 0.5f, 2f);
                continue;
            }

            // Aim further along the camera's right to push the image LEFT.
            shiftRight += (rect.center.x - HeroTargetCenterX) * metresPerUnit;
            shiftUp += (rect.center.y - HeroTargetCenterY) * metresPerUnit;
        }

        camera.ResetAspect();

        bool left = rect.xMax <= HeroMaxRightFraction;
        bool wide = rect.width >= HeroMinWidthFraction;
        bool onScreen = rect.xMin >= -0.02f && rect.xMax > 0f && rect.yMax > 0f && rect.yMin < 1f;
        report = "silhouette=[" + rect.xMin.ToString("F3") + "," + rect.yMin.ToString("F3")
                 + " -> " + rect.xMax.ToString("F3") + "," + rect.yMax.ToString("F3")
                 + "] width=" + rect.width.ToString("F3")
                 + " distance=" + distance.ToString("F1")
                 + " " + (left && wide && onScreen ? "OK" : "CONTRACT-FAIL");
    }

    /// <summary>
    /// Projects the ship's rendered silhouette (every mesh's own local bounds,
    /// corner by corner) into viewport space. Per-mesh bounds are used
    /// deliberately: the single world AABB of a 24 m ship yawed 20 degrees
    /// projects far wider than the ship really is, which would make any
    /// composition check meaningless.
    /// </summary>
    private static Rect MeasureSilhouette(Camera camera, Transform ship)
    {
        float xMin = float.MaxValue;
        float xMax = float.MinValue;
        float yMin = float.MaxValue;
        float yMax = float.MinValue;
        bool any = false;
        foreach (MeshFilter filter in ship.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
            {
                continue;
            }

            Bounds local = filter.sharedMesh.bounds;
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;
            Vector3 c = local.center;
            Vector3 e = local.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                Vector3 viewport = camera.WorldToViewportPoint(matrix.MultiplyPoint3x4(corner));
                if (viewport.z <= 0f)
                {
                    continue;
                }

                any = true;
                xMin = Mathf.Min(xMin, viewport.x);
                xMax = Mathf.Max(xMax, viewport.x);
                yMin = Mathf.Min(yMin, viewport.y);
                yMax = Mathf.Max(yMax, viewport.y);
            }
        }

        return any
            ? new Rect(xMin, yMin, xMax - xMin, yMax - yMin)
            : new Rect(0.5f, 0.5f, 0f, 0f);
    }

    private static void BuildMainMenuLights(Scene scene)
    {
        AddDirectional(scene, "ShipKeyLight", new Vector3(0.55f, -0.62f, -0.55f),
            new Color(1f, 0.955f, 0.905f, 1f), 1.25f);
        AddDirectional(scene, "ShipFillLight", new Vector3(-0.62f, 0.22f, -0.75f),
            new Color(0.62f, 0.82f, 1f, 1f), 0.45f);
        AddDirectional(scene, "ShipRimLight", new Vector3(0.16f, -0.26f, 0.95f),
            new Color(0.74f, 0.9f, 1f, 1f), 0.85f);
    }

    private static void AddDirectional(Scene scene, string name, Vector3 travelDirection,
        Color colour, float intensity)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = colour;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        go.transform.rotation = Quaternion.LookRotation(travelDirection.normalized, Vector3.up);
    }

    /// <summary>
    /// Cinematic post stack for the cover: bloom is what turns the additive
    /// plumes and the star field into light instead of flat sprites. The profile
    /// is scene-owned (same pattern as the ship's console focus volume).
    /// </summary>
    private static void BuildMainMenuVolume(Scene scene)
    {
        var volumeGo = new GameObject("MainMenuStageVolume");
        SceneManager.MoveGameObjectToScene(volumeGo, scene);
        var volume = volumeGo.AddComponent<UnityEngine.Rendering.Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;

        var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        profile.name = "MainMenuStageProfile";

        var tonemapping = profile.Add<UnityEngine.Rendering.Universal.Tonemapping>(true);
        tonemapping.mode.Override(UnityEngine.Rendering.Universal.TonemappingMode.ACES);

        var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
        bloom.threshold.Override(0.95f);
        bloom.intensity.Override(0.85f);
        bloom.scatter.Override(0.72f);
        bloom.tint.Override(new Color(0.78f, 0.9f, 1f, 1f));

        var vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
        vignette.intensity.Override(0.34f);
        vignette.smoothness.Override(0.58f);
        vignette.color.Override(Color.black);

        var colour = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
        colour.contrast.Override(12f);
        colour.saturation.Override(-6f);
        colour.postExposure.Override(0.12f);

        volume.sharedProfile = profile;
        Debug.Log("[SurviveProjectSetup] main menu stage volume created");
    }

    /// <summary>Fraction of the frame the ship must cover to count as the hero.</summary>
    private const float HeroMinWidthFraction = 0.40f;
    private const float HeroMaxRightFraction = 0.66f;

    /// <summary>
    /// Measured exhaust axis: the direction from the ship origin to the midpoint
    /// of the two authored VFX anchors, expressed in HERO-PIVOT space (which is
    /// the identity at this point in the build). Measuring instead of assuming
    /// keeps the pose correct whatever axis convention the FBX importer produced.
    /// </summary>
    private static Vector3 MeasureAft(Transform ship, out bool anchorsFound)
    {
        Transform left = FindDeep(ship, "VFX_Thruster_L");
        Transform right = FindDeep(ship, "VFX_Thruster_R");
        anchorsFound = left != null && right != null;
        if (!anchorsFound)
        {
            return Vector3.forward;
        }

        Vector3 midpoint = (left.position + right.position) * 0.5f;
        Vector3 aft = midpoint - ship.position;
        return aft.sqrMagnitude > 0.0001f ? aft.normalized : Vector3.forward;
    }

    /// <summary>Renderer bounds computed from the mesh bounds and the transform
    /// chain (never from a possibly stale renderer cache).</summary>
    private static Bounds MeasureShipBounds(Transform root)
    {
        bool has = false;
        Bounds bounds = new Bounds(root.position, Vector3.zero);
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
            {
                continue;
            }

            Bounds local = filter.sharedMesh.bounds;
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;
            Vector3 c = local.center;
            Vector3 e = local.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                Vector3 world = matrix.MultiplyPoint3x4(corner);
                if (!has)
                {
                    bounds = new Bounds(world, Vector3.zero);
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(world);
                }
            }
        }

        return bounds;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Re-points every ship renderer at the project's five URP materials. The
    /// imported slots keep their authored intent (hull / secondary / glass /
    /// accent / engine), which is what makes the ship read as the same asset the
    /// Blender validation renders show.
    /// </summary>
    private static void AssignShipMaterials(GameObject ship)
    {
        Material hull = EnsureShipMaterial("M_MainMenuHull",
            new Color(0.66f, 0.69f, 0.70f, 1f), 0.30f, 0.55f, Color.black);
        Material secondary = EnsureShipMaterial("M_MainMenuSecondary",
            new Color(0.075f, 0.09f, 0.105f, 1f), 0.42f, 0.50f, Color.black);
        Material glass = EnsureShipMaterial("M_MainMenuGlass",
            new Color(0.02f, 0.05f, 0.07f, 1f), 0.55f, 0.86f, Color.black);
        Material accent = EnsureShipMaterial("M_MainMenuAccent",
            new Color(0.95f, 0.235f, 0.025f, 1f), 0.20f, 0.55f,
            new Color(0.55f, 0.09f, 0.01f, 1f));
        Material engine = EnsureShipMaterial("M_MainMenuEngine",
            new Color(0.02f, 0.024f, 0.03f, 1f), 0.35f, 0.42f, Color.black);

        int mapped = 0;
        foreach (Renderer renderer in ship.GetComponentsInChildren<Renderer>(true))
        {
            string name = renderer.sharedMaterial != null
                ? renderer.sharedMaterial.name.ToLowerInvariant()
                : string.Empty;
            Material target = hull;
            if (name.Contains("secondary"))
            {
                target = secondary;
            }
            else if (name.Contains("glass"))
            {
                target = glass;
            }
            else if (name.Contains("accent"))
            {
                target = accent;
            }
            else if (name.Contains("engine"))
            {
                target = engine;
            }

            renderer.sharedMaterial = target;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            mapped++;
        }

        Debug.Log("[SurviveProjectSetup] ship materials mapped on " + mapped + " renderers");
    }

    private static Material EnsureShipMaterial(string name, Color baseColour,
        float metallic, float smoothness, Color emission)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", baseColour);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
        }

        string path = MenuMaterialsFolder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(material, existing);
            Object.DestroyImmediate(material);
            return existing;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>Authored additive plume material (scene reference keeps the
    /// custom shader in the build).</summary>
    private static Material EnsurePlumeMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(
            MenuMaterialsFolder + "/M_MainMenuPlume.mat");
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Survive/ThrusterPlumeUnlit");
        if (shader == null)
        {
            Debug.LogError("[SurviveProjectSetup] Survive/ThrusterPlumeUnlit missing");
            return null;
        }

        var material = new Material(shader) { name = "M_MainMenuPlume" };
        material.SetColor("_BaseColor", new Color(0.35f, 0.66f, 1f, 1f));
        material.SetColor("_CoreColor", new Color(1f, 0.93f, 0.80f, 1f));
        material.SetFloat("_Intensity", 1f);
        material.SetFloat("_Length", 1f);
        material.SetFloat("_CoreMix", 0.55f);
        material.SetFloat("_Falloff", 1.7f);
        material.SetFloat("_NozzleFade", 0.06f);
        AssetDatabase.CreateAsset(material, MenuMaterialsFolder + "/M_MainMenuPlume.mat");
        return material;
    }

    /// <summary>Authored additive star sprite material (the runtime clones it per
    /// parallax layer and assigns a generated star plate).</summary>
    private static Material EnsureStarSpriteMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(
            MenuMaterialsFolder + "/M_MainMenuStarSprite.mat");
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Survive/SpaceSpriteUnlit");
        if (shader == null)
        {
            Debug.LogError("[SurviveProjectSetup] Survive/SpaceSpriteUnlit missing");
            return null;
        }

        var material = new Material(shader) { name = "M_MainMenuStarSprite" };
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Intensity", 1f);
        AssetDatabase.CreateAsset(material,
            MenuMaterialsFolder + "/M_MainMenuStarSprite.mat");
        return material;
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
