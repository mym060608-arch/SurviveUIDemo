using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Deterministic, idempotent builder for the NEREID-04 surface slice
/// (Phase 06). Recreates 02_NereidSurface from scratch every run: fixed
/// object names, fixed coordinates, project-local URP materials, one player,
/// one camera, one HUD, one surface controller and exactly three resource
/// nodes. Repeated execution converges to the same authored scene.
/// </summary>
public static class NereidSurfaceBuilder
{
    private const string ScenePath = "Assets/_Project/Scenes/02_NereidSurface.unity";
    private const string MaterialsFolder = "Assets/_Project/Materials/Surface";
    private const string UxmlPath = "Assets/_Project/UI/UXML/SurfaceHUD.uxml";
    private const string UssPath = "Assets/_Project/UI/USS/SurfaceHUD.uss";
    private const string UxmlWristPath = "Assets/_Project/UI/UXML/SurfaceWrist.uxml";
    private const string UssWristPath = "Assets/_Project/UI/USS/SurfaceWrist.uss";
    private const string PanelSettingsPath = "Assets/_Project/UI/Settings/HubPanelSettings.asset";
    private const string WristPanelSettingsPath = "Assets/_Project/UI/Settings/WristPanelSettings.asset";

    public static void RunCli()
    {
        bool ok = BuildSurface();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static bool BuildSurface()
    {
        bool ok = true;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            AssetDatabase.DeleteAsset(ScenePath);
        }

        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (uxml == null || uss == null || panelSettings == null)
        {
            Debug.LogError("[NereidSurfaceBuilder] missing HUD assets/panel settings");
            return false;
        }

        ok &= EnsureMaterials();
        if (!ok)
        {
            return false;
        }

        Material ground = Load("M_SurfaceGround");
        Material rock = Load("M_SurfaceRock");
        Material resource = Load("M_ResourceNode");
        Material depleted = Load("M_ResourceDepleted");
        Material beacon = Load("M_ResourceBeacon");
        Material beaconDim = Load("M_ResourceBeaconDim");
        Material harvester = Load("M_Harvester");
        Material pad = Load("M_LandingPad");
        Material signal = Load("M_SignalAccent");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Ground (36m x 36m cool pale plane).
        GameObject groundGo = CreatePrimitive(PrimitiveType.Plane, "Ground", ground,
            new Vector3(0f, 0f, 0f), new Vector3(3.6f, 1f, 3.6f));
        MoveToScene(scene, groundGo);

        // Directional light.
        var lightGo = new GameObject("Directional Light");
        MoveToScene(scene, lightGo);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.96f, 0.97f, 0.98f);
        light.intensity = 1.05f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

        // Ambient.
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.65f, 0.68f);

        // ---- Landing pad + survey beacon (focal structure) ----
        var padRoot = new GameObject("LandingArea");
        MoveToScene(scene, padRoot);
        BuildLandingPad(padRoot.transform, pad, signal);

        // E-return world interaction over the pad (first-person interactable).
        var returnVolume = padRoot.AddComponent<SphereCollider>();
        returnVolume.isTrigger = true;
        returnVolume.radius = 2.2f;
        returnVolume.center = new Vector3(0f, 1f, 0f);
        padRoot.AddComponent<LandingReturnInteractable>();

        // ---- Decorative rocks (deterministic, off the walk path) ----
        BuildRocks(scene, rock);

        // ---- Three resource nodes ----
        Vector3[] nodePositions =
        {
            new Vector3(-7f, 0f, 2.5f),
            new Vector3(0f, 0f, 9f),
            new Vector3(7.5f, 0f, 1.5f)
        };

        for (int i = 0; i < nodePositions.Length; i++)
        {
            BuildResourceNode(scene, i, nodePositions[i], resource, depleted, beacon, beaconDim,
                harvester, signal);
        }

        // ---- Player: invisible first-person body + eye-level camera ----
        var player = new GameObject("Player");
        MoveToScene(scene, player);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        player.transform.position = new Vector3(0f, 0f, 4.8f);
        player.AddComponent<SurfacePlayerController>();
        player.AddComponent<FirstPersonInteractor>();

        var eyeGo = new GameObject("FirstPersonCamera");
        eyeGo.transform.SetParent(player.transform, false);
        eyeGo.tag = "MainCamera";
        var cam = eyeGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.68f, 0.71f, 0.74f);
        cam.fieldOfView = 70f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 220f;
        eyeGo.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        eyeGo.transform.localRotation = Quaternion.identity;

        // ---- Surface HUD (screen-space UI Toolkit overlay) ----
        var hudGo = new GameObject("SurfaceHUD");
        MoveToScene(scene, hudGo);
        var document = hudGo.AddComponent<UIDocument>();
        document.visualTreeAsset = uxml;
        document.sortingOrder = 1;
        var hud = hudGo.AddComponent<SurfaceHudController>();
        hud.hudStyleSheet = uss;
        hud.panelSettings = panelSettings;

        // ---- Wrist terminal: own UIDocument -> RenderTexture -> tilted
        // world plane (Phase 06 Revision round 3) ----
        var wristUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlWristPath);
        if (wristUxml == null)
        {
            Debug.LogError("[NereidSurfaceBuilder] missing wrist uxml: " + UxmlWristPath);
            return false;
        }

        var wristUss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssWristPath);
        if (wristUss == null)
        {
            Debug.LogError("[NereidSurfaceBuilder] missing wrist uss: " + UssWristPath);
            return false;
        }

        PanelSettings wristPanel = EnsureWristPanelSettings();
        if (wristPanel == null)
        {
            Debug.LogError("[NereidSurfaceBuilder] wrist panel settings missing");
            return false;
        }

        var wristGo = new GameObject("SurfaceWristUI");
        MoveToScene(scene, wristGo);
        var wristDoc = wristGo.AddComponent<UIDocument>();
        wristDoc.visualTreeAsset = wristUxml;
        var wrist = wristGo.AddComponent<SurfaceWristController>();
        wrist.panelSettings = wristPanel;
        wrist.wristStyleSheet = wristUss;

        // Round 8 hologram chain: WristProjectionPivot (camera-relative,
        // raised-wrist offset) -> WristDisplay (child at origin). The pivot is
        // the future hand/wrist attachment point; parallax tilts the display
        // around it at runtime.
        var pivotGo = new GameObject("WristProjectionPivot");
        pivotGo.transform.SetParent(eyeGo.transform, false);
        pivotGo.transform.localPosition = WristPivotLocalPosition;
        pivotGo.transform.localRotation = Quaternion.identity;
        pivotGo.transform.localScale = Vector3.one;

        var displayGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(displayGo.GetComponent<Collider>());
        displayGo.name = "WristDisplay";
        displayGo.transform.SetParent(pivotGo.transform, false);
        displayGo.transform.localPosition = Vector3.zero;
        displayGo.transform.localRotation = Quaternion.identity;
        displayGo.transform.localScale = WristDisplayLocalScale;

        Shader wristShader = Shader.Find("Survive/WristDisplayUnlit");
        if (wristShader == null)
        {
            wristShader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        var displayMaterial = new Material(wristShader)
        {
            name = "M_WristDisplay",
            color = Color.white
        };

        // Transparent unlit presentation (alpha blend, no ZWrite, Cull Off
        // handled in the dedicated shader).
        displayMaterial.SetFloat("_Cull", 0f);

        var displayRenderer = displayGo.GetComponent<Renderer>();
        displayRenderer.sharedMaterial = displayMaterial;
        displayGo.AddComponent<MeshCollider>();
        var anchor = displayGo.AddComponent<WristDisplayAnchor>();
        anchor.Show(false); // controller shows it when the wrist opens

        // ---- Surface coordinator ----
        var controlGo = new GameObject("SurfaceGameController");
        MoveToScene(scene, controlGo);
        controlGo.AddComponent<SurfaceGameController>();

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            Debug.LogError("[NereidSurfaceBuilder] failed to save " + ScenePath);
            return false;
        }

        Debug.Log("[NereidSurfaceBuilder] wrote " + ScenePath);
        return ok;
    }

    // Camera-relative wrist placement (round 8, larger hologram). The runtime
    // pose is computed from the camera basis each open; these values define the
    // WristProjectionPivot offset. Physical display ~1.08m wide -> projected
    // width ~50-60% of the 1920x1080 frame (1280:800 kept).
    private static readonly Vector3 WristPivotLocalPosition = new Vector3(-0.05f, -0.02f, 0.85f);
    private static readonly Vector3 WristDisplayLocalScale = new Vector3(1.08f, 0.675f, 1f);

    private static void BuildLandingPad(Transform parent, Material pad, Material signal)
    {
        GameObject disc = CreatePrimitive(PrimitiveType.Cylinder, "Pad_Disc", pad,
            new Vector3(0f, 0.05f, 0f), new Vector3(4f, 0.05f, 4f));
        disc.transform.SetParent(parent, false);
        Object.DestroyImmediate(disc.GetComponent<Collider>());

        GameObject ring = CreatePrimitive(PrimitiveType.Cylinder, "Pad_Ring", signal,
            new Vector3(0f, 0.1f, 0f), new Vector3(2.4f, 0.02f, 2.4f));
        ring.transform.SetParent(parent, false);
        Object.DestroyImmediate(ring.GetComponent<Collider>());

        // Survey beacon at pad centre.
        GameObject beacon = CreatePrimitive(PrimitiveType.Cylinder, "SurveyBeacon", pad,
            new Vector3(0f, 2.6f, 0f), new Vector3(0.22f, 2.6f, 0.22f));
        beacon.transform.SetParent(parent, false);
        Object.DestroyImmediate(beacon.GetComponent<Collider>());

        GameObject beaconTip = CreatePrimitive(PrimitiveType.Sphere, "SurveyBeacon_Tip", signal,
            new Vector3(0f, 5.1f, 0f), new Vector3(0.4f, 0.4f, 0.4f));
        beaconTip.transform.SetParent(parent, false);
        Object.DestroyImmediate(beaconTip.GetComponent<Collider>());

        // Cardinal survey markers around the pad.
        Vector3[] marks =
        {
            new Vector3(3.2f, 0.16f, 0f),
            new Vector3(-3.2f, 0.16f, 0f),
            new Vector3(0f, 0.16f, 3.2f),
            new Vector3(0f, 0.16f, -3.2f)
        };

        foreach (Vector3 mark in marks)
        {
            GameObject cube = CreatePrimitive(PrimitiveType.Cube, "Pad_Mark", signal,
                mark, new Vector3(0.5f, 0.06f, 0.5f));
            cube.transform.SetParent(parent, false);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
        }
    }

    private static void BuildRocks(Scene scene, Material rock)
    {
        var root = new GameObject("Rocks");
        MoveToScene(scene, root);

        Vector3[] positions =
        {
            new Vector3(-10.5f, 0.4f, -3f), new Vector3(-11f, 0.3f, 2f),
            new Vector3(-8.5f, 0.5f, 6f), new Vector3(-2f, 0.35f, -6f),
            new Vector3(4f, 0.45f, -7f), new Vector3(9.5f, 0.4f, 6f),
            new Vector3(11f, 0.3f, -2f), new Vector3(5.5f, 0.55f, -3.5f),
            new Vector3(-4.5f, 0.3f, 12f), new Vector3(11f, 0.35f, -7f)
        };

        Vector3[] scales =
        {
            new Vector3(1.5f, 0.8f, 1.2f), new Vector3(1.1f, 0.7f, 1.4f),
            new Vector3(1.8f, 0.6f, 1f), new Vector3(1.2f, 0.9f, 1.1f),
            new Vector3(1.3f, 0.7f, 1.5f), new Vector3(2f, 0.8f, 1.2f),
            new Vector3(1f, 1.1f, 1f), new Vector3(1.6f, 0.65f, 1.3f),
            new Vector3(1.2f, 0.7f, 2f), new Vector3(1.4f, 1.2f, 1f)
        };

        for (int i = 0; i < positions.Length; i++)
        {
            GameObject rockGo = CreatePrimitive(PrimitiveType.Cube, "Rock_" + (i + 1).ToString("00"),
                rock, positions[i], scales[i]);
            rockGo.transform.SetParent(root.transform, false);
        }
    }

    private static void BuildResourceNode(Scene scene, int id, Vector3 position,
        Material resource, Material depleted, Material beacon, Material beaconDim,
        Material harvester, Material signal)
    {
        var nodeRoot = new GameObject("ResourceNode_" + (id + 1).ToString("00"));
        MoveToScene(scene, nodeRoot);
        nodeRoot.transform.position = position;

        var renderers = new List<Renderer>();

        GameObject baseDisc = CreatePrimitive(PrimitiveType.Cylinder, "RN_Base", resource,
            new Vector3(0f, 0.06f, 0f), new Vector3(0.85f, 0.06f, 0.85f));
        baseDisc.transform.SetParent(nodeRoot.transform, false);
        Object.DestroyImmediate(baseDisc.GetComponent<Collider>());
        renderers.Add(baseDisc.GetComponent<Renderer>());

        // Ground marker ring.
        GameObject markerRing = CreatePrimitive(PrimitiveType.Cylinder, "RN_Marker", signal,
            new Vector3(0f, 0.03f, 0f), new Vector3(1.15f, 0.01f, 1.15f));
        markerRing.transform.SetParent(nodeRoot.transform, false);
        Object.DestroyImmediate(markerRing.GetComponent<Collider>());

        // 3 mineral columns around the beacon.
        Vector3[] columns =
        {
            new Vector3(-0.5f, 0.45f, 0f), new Vector3(0.3f, 0.3f, 0.45f),
            new Vector3(0.35f, 0.55f, -0.4f)
        };

        foreach (Vector3 column in columns)
        {
            GameObject rockGo = CreatePrimitive(PrimitiveType.Cylinder, "RN_Column", resource,
                column, new Vector3(0.22f, 0.9f, 0.22f));
            rockGo.transform.SetParent(nodeRoot.transform, false);
            Object.DestroyImmediate(rockGo.GetComponent<Collider>());
            renderers.Add(rockGo.GetComponent<Renderer>());
        }

        // Central teal survey beacon (visual differentiator).
        GameObject beaconGo = CreatePrimitive(PrimitiveType.Cylinder, "RN_Beacon", beacon,
            new Vector3(0f, 1.1f, 0f), new Vector3(0.14f, 1.1f, 0.14f));
        beaconGo.transform.SetParent(nodeRoot.transform, false);
        Object.DestroyImmediate(beaconGo.GetComponent<Collider>());
        Renderer beaconRenderer = beaconGo.GetComponent<Renderer>();

        // Signal accent tip.
        GameObject tip = CreatePrimitive(PrimitiveType.Sphere, "RN_Tip", signal,
            new Vector3(0f, 2.05f, 0f), new Vector3(0.2f, 0.2f, 0.2f));
        tip.transform.SetParent(nodeRoot.transform, false);
        Object.DestroyImmediate(tip.GetComponent<Collider>());

        // Placeholder harvester (inactive until deployment).
        GameObject harvesterGo = BuildHarvester(harvester, signal);
        harvesterGo.name = "HarvesterPlaceholder";
        harvesterGo.transform.SetParent(nodeRoot.transform, false);
        harvesterGo.transform.localPosition = Vector3.zero;
        harvesterGo.SetActive(false);

        // Wireless transfer beam column (visual placeholder).
        GameObject beam = CreatePrimitive(PrimitiveType.Cylinder, "TransferBeam", signal,
            new Vector3(0f, 3.2f, 0f), new Vector3(0.06f, 3.2f, 0.06f));
        beam.transform.SetParent(nodeRoot.transform, false);
        Object.DestroyImmediate(beam.GetComponent<Collider>());
        beam.SetActive(false);

        var component = nodeRoot.AddComponent<SurfaceResourceNode>();
        component.nodeId = id;
        component.mineralYield = GameSession.SurfaceNodeYield;
        component.baseRenderers = renderers.ToArray();
        component.beaconRenderer = beaconRenderer;
        component.harvester = harvesterGo;
        component.transferBeam = beam;
        component.depletedBaseMaterial = depleted;
        component.depletedBeaconMaterial = beaconDim;

        // First-person gaze ray needs a trigger volume on the node root.
        var interactVolume = nodeRoot.AddComponent<SphereCollider>();
        interactVolume.isTrigger = true;
        interactVolume.radius = 1.15f;
        interactVolume.center = new Vector3(0f, 1.15f, 0f);
    }

    private static GameObject BuildHarvester(Material body, Material signal)
    {
        var root = new GameObject("HarvesterVisual");
        GameObject core = CreatePrimitive(PrimitiveType.Cylinder, "Core", body,
            new Vector3(0f, 0.42f, 0f), new Vector3(0.5f, 0.42f, 0.5f));
        core.transform.SetParent(root.transform, false);
        Object.DestroyImmediate(core.GetComponent<Collider>());

        GameObject antenna = CreatePrimitive(PrimitiveType.Cylinder, "Antenna", signal,
            new Vector3(0f, 1.05f, 0f), new Vector3(0.05f, 0.4f, 0.05f));
        antenna.transform.SetParent(root.transform, false);
        Object.DestroyImmediate(antenna.GetComponent<Collider>());

        Vector3[] legs =
        {
            new Vector3(0.35f, 0.1f, 0.2f), new Vector3(-0.35f, 0.1f, 0.2f),
            new Vector3(0f, 0.1f, -0.4f)
        };

        foreach (Vector3 leg in legs)
        {
            GameObject legGo = CreatePrimitive(PrimitiveType.Cylinder, "Leg", body,
                leg + new Vector3(0f, 0.12f, 0f), new Vector3(0.09f, 0.5f, 0.09f));
            legGo.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(legGo.GetComponent<Collider>());
        }

        return root;
    }

    // ---- helpers ----

    /// <summary>Creates (idempotently) the wrist panel settings asset that
    /// renders the SurfaceWrist UI into its RenderTexture at fixed pixels.
    /// ConstantPixelSize pins the layout to the 1280x800 texture independent
    /// of the player window; reference/match are recorded for clarity.</summary>
    private static PanelSettings EnsureWristPanelSettings()
    {
        var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(WristPanelSettingsPath);
        if (existing != null)
        {
            return existing;
        }

        var panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.scaleMode = PanelScaleMode.ConstantPixelSize;
        panel.referenceResolution = new Vector2Int(1280, 800);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match = 0.5f;
        panel.scale = 1f;
        AssetDatabase.CreateAsset(panel, WristPanelSettingsPath);
        EditorUtility.SetDirty(panel);
        Debug.Log("[NereidSurfaceBuilder] wrote " + WristPanelSettingsPath);
        return panel;
    }

    private static bool EnsureMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials"))
        {
            AssetDatabase.CreateFolder("Assets/_Project", "Materials");
        }

        if (AssetDatabase.IsValidFolder(MaterialsFolder))
        {
            AssetDatabase.DeleteAsset(MaterialsFolder);
        }

        string parent = "Assets/_Project/Materials";
        AssetDatabase.CreateFolder(parent, "Surface");

        return Ensure("M_SurfaceGround", new Color(0.62f, 0.66f, 0.70f))
               & Ensure("M_SurfaceRock", new Color(0.34f, 0.37f, 0.40f))
               & Ensure("M_ResourceNode", new Color(0.47f, 0.51f, 0.54f))
               & Ensure("M_ResourceDepleted", new Color(0.28f, 0.30f, 0.32f))
               & Ensure("M_ResourceBeacon", new Color(0.055f, 0.5f, 0.55f))
               & Ensure("M_ResourceBeaconDim", new Color(0.30f, 0.33f, 0.35f))
               & Ensure("M_Harvester", new Color(0.90f, 0.91f, 0.92f))
               & Ensure("M_LandingPad", new Color(0.20f, 0.23f, 0.25f))
               & Ensure("M_SignalAccent", new Color(0.055f, 0.5f, 0.55f));
    }

    private static bool Ensure(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[NereidSurfaceBuilder] URP Lit shader missing");
            return false;
        }

        var material = new Material(shader);
        material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, MaterialsFolder + "/" + name + ".mat");
        return true;
    }

    private static Material Load(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/" + name + ".mat");
    }

    private static Material CreateMaterialInstance(Material shared, string name)
    {
        var material = new Material(shared) { name = name };
        return material;
    }

    private static GameObject CreatePrimitive(PrimitiveType type, string name, Material material,
        Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = scale;
        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }

        return go;
    }

    private static void MoveToScene(Scene scene, GameObject go)
    {
        SceneManager.MoveGameObjectToScene(go, scene);
    }
}
