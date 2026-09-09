using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Deterministic, idempotent builder for the walkable ARK-07 ship interior
/// (Phase 07 Checkpoint A). Recreates 03_ShipInterior.unity from scratch on
/// every run: fixed object names, fixed coordinates, project-local URP
/// materials, one first-person rig + HUD, one ShipGameController, four
/// physical console placeholders, three sliding doors, one airlock station
/// and two spawn markers. Repeated execution converges to the same scene.
///
/// Layout contract (top view, +z = forward):
///
///   x -5.5..5.5, z -14.4..10.6 single hull deck, ceiling 2.7
///   BRIDGE / OPS        x -3.4..3.4, z 6.3..10.6      console 01 总览
///   MISSION PREPARATION x -5.5..-1.1, z 0.3..6.3      console 02 出发准备
///   SPINE corridor      x -1.1..1.1,   z 0.3..6.3
///   RESOURCE PROCESSING x 1.1..5.5,    z 0.3..6.3      console 03 资源加工
///   OUTPOST // COMMS    x -5.5..5.5,   z -9.4..0.3     console 04 据点
///   AIRLOCK // EVA      x -3.0..3.0,   z -14.4..-9.4   airlock station
///
/// Door gaps: bridge z=6.3 x -0.9..0.9 (D1); x=+/-1.1 walls z 2.7..4.1
/// (D2/D3); airlock divider z=-9.4 x -0.8..0.8 (open doorway).
/// </summary>
public static class ShipInteriorBuilder
{
    private const string ScenePath = "Assets/_Project/Scenes/03_ShipInterior.unity";
    private const string MaterialsFolder = "Assets/_Project/Materials/Ship";
    private const string UxmlHudPath = "Assets/_Project/UI/UXML/ShipHUD.uxml";
    private const string UssHudPath = "Assets/_Project/UI/USS/ShipHUD.uss";
    private const string PanelSettingsPath = "Assets/_Project/UI/Settings/HubPanelSettings.asset";

    // ---- Hull layout (interior clear-space) ----
    private const float XMin = -5.5f;
    private const float XMax = 5.5f;
    private const float ZMin = -14.4f;
    private const float ZMax = 10.6f;
    private const float Ceiling = 2.7f;
    private const float WallT = 0.2f;

    public static void RunCli()
    {
        bool ok = BuildShipInterior();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    [MenuItem("Tools/SurviveUI/Build Ship Interior")]
    public static void BuildFromMenu()
    {
        if (BuildShipInterior())
        {
            Debug.Log("[ShipInteriorBuilder] SUCCESS");
        }
        else
        {
            Debug.LogError("[ShipInteriorBuilder] FAILED");
        }
    }

    public static bool BuildShipInterior()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            AssetDatabase.DeleteAsset(ScenePath);
        }

        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlHudPath);
        var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssHudPath);
        var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (uxml == null || uss == null || panelSettings == null)
        {
            Debug.LogError("[ShipInteriorBuilder] missing ShipHUD assets/panel settings");
            return false;
        }

        if (!EnsureMaterials())
        {
            return false;
        }

        Material floor = Load("M_ShipFloor");
        Material ceiling = Load("M_ShipCeiling");
        Material wall = Load("M_ShipWallLight");
        Material dark = Load("M_ShipFrameDark");
        Material doorPanel = Load("M_ShipDoor");
        Material accent = Load("M_ShipAccent");
        Material glow = Load("M_ShipAccentGlow");
        Material consoleLight = Load("M_ShipConsoleLight");
        Material consoleDark = Load("M_ShipConsoleDark");
        Material screen = Load("M_ShipScreen");
        Material prop = Load("M_ShipProp");
        Material airlock = Load("M_ShipAirlock");
        Material viewport = Load("M_ShipViewport");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Cool industrial interior ambience; real-time point lights carry the
        // rooms (the hull ceiling blocks any sun).
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.30f, 0.33f, 0.36f);
        RenderSettings.fog = false;

        var structure = new GameObject("ShipStructure");
        BuildStructure(structure.transform, floor, ceiling, wall, dark, accent, glow,
            viewport, doorPanel);

        var doorsRoot = new GameObject("ShipDoors");
        BuildDoors(doorsRoot.transform, dark, doorPanel, glow);

        var consolesRoot = new GameObject("ShipConsoles");
        BuildConsoles(consolesRoot.transform, consoleLight, consoleDark, screen, glow);

        var airlockRoot = new GameObject("ShipAirlock");
        BuildAirlock(airlockRoot.transform, airlock, dark, glow);

        BuildSpawns(accent, glow);
        BuildProps(prop, dark, accent, glow, airlock);

        var lightsRoot = new GameObject("ShipLights");
        BuildLights(lightsRoot.transform, wall);

        // ---- Player: invisible first-person body + eye-level camera ----
        var player = new GameObject("Player");
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        player.transform.position = new Vector3(0f, 0f, 3.6f);
        player.AddComponent<SurfacePlayerController>();
        player.AddComponent<FirstPersonInteractor>();

        var eyeGo = new GameObject("FirstPersonCamera");
        eyeGo.transform.SetParent(player.transform, false);
        eyeGo.tag = "MainCamera";
        var cam = eyeGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.07f);
        cam.fieldOfView = 70f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 60f;
        eyeGo.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        eyeGo.transform.localRotation = Quaternion.identity;

        // ---- Ship HUD (screen-space UI Toolkit overlay) ----
        var hudGo = new GameObject("ShipHUD");
        var document = hudGo.AddComponent<UIDocument>();
        document.visualTreeAsset = uxml;
        document.sortingOrder = 1;
        var hud = hudGo.AddComponent<ShipHudController>();
        hud.hudStyleSheet = uss;
        hud.panelSettings = panelSettings;

        // ---- Ship coordinator ----
        var controlGo = new GameObject("ShipGameController");
        controlGo.AddComponent<ShipGameController>();

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            Debug.LogError("[ShipInteriorBuilder] failed to save " + ScenePath);
            return false;
        }

        Debug.Log("[ShipInteriorBuilder] wrote " + ScenePath);
        return true;
    }

    // ---- Structure (hull + compartment dividers) ----

    private static void BuildStructure(Transform root, Material floor, Material ceiling,
        Material wall, Material dark, Material accent, Material glow, Material viewport,
        Material doorPanel)
    {
        // Global deck floor + ceiling slabs (top face at y 0 / underside 2.7).
        CreateWorldBox(root, "Deck_Floor", floor, new Vector3(-0.25f, -0.1f, -1.9f),
            new Vector3(12.2f, 0.2f, 26.2f), true);
        CreateWorldBox(root, "Deck_Ceiling", ceiling, new Vector3(-0.25f, Ceiling + 0.1f, -1.9f),
            new Vector3(12.2f, 0.2f, 26.2f), false);

        // Hull perimeter (inner face on the clear bounds).
        WallAlongZ(root, XMin - WallT, ZMin, ZMax, wall, 0.3f);
        WallAlongZ(root, XMax + WallT, ZMin, ZMax, wall, 0.3f);
        WallAlongX(root, XMin, XMax, ZMax + 0.15f, wall, 0.3f);
        WallAlongX(root, XMin, XMax, ZMin - 0.15f, wall, 0.3f);

        // Bridge front viewport window (visual, on the forward hull inner face).
        CreateWorldBox(root, "Bridge_Viewport", dark, new Vector3(0f, 1.55f, ZMax - 0.04f),
            new Vector3(3.6f, 1.2f, 0.06f), false);
        CreateWorldBox(root, "Bridge_ViewportGlow", viewport,
            new Vector3(0f, 1.55f, ZMax - 0.12f), new Vector3(2.6f, 0.8f, 0.03f), false);

        // Bridge side walls (also seal the dead hull pockets at +/-x).
        WallAlongZ(root, -3.4f, 6.3f, ZMax, wall, WallT);
        WallAlongZ(root, 3.4f, 6.3f, ZMax, wall, WallT);

        // z = 6.3 band: bridge aft wall + corridor top + pocket caps.
        WallAlongX(root, XMin, -3.4f, 6.3f, wall, WallT);
        WallAlongX(root, -3.4f, -0.9f, 6.3f, wall, WallT);
        WallAlongX(root, 0.9f, 3.4f, 6.3f, wall, WallT);
        WallAlongX(root, 3.4f, XMax, 6.3f, wall, WallT);

        // Mid band: three columns (prep | corridor | fabrication).
        WallAlongZ(root, -1.1f, 0.3f, 2.7f, wall, WallT);
        WallAlongZ(root, -1.1f, 4.1f, 6.3f, wall, WallT);
        WallAlongZ(root, 1.1f, 0.3f, 2.7f, wall, WallT);
        WallAlongZ(root, 1.1f, 4.1f, 6.3f, wall, WallT);

        // Column aft walls at z = 0.3 (centre corridor mouth stays open).
        WallAlongX(root, XMin, -1.1f, 0.3f, wall, WallT);
        WallAlongX(root, 1.1f, XMax, 0.3f, wall, WallT);

        // Outpost - airlock bulkhead with the centre doorway (x -0.8..0.8).
        WallAlongX(root, XMin, -0.8f, -9.4f, wall, WallT);
        WallAlongX(root, 0.8f, XMax, -9.4f, wall, WallT);

        // Airlock chamber side walls.
        WallAlongZ(root, -3.0f, ZMin, -9.4f, wall, WallT);
        WallAlongZ(root, 3.0f, ZMin, -9.4f, wall, WallT);

        // Restrained teal accent strips along the corridor ceiling + doorway
        // headers (visual only, give the industrial cool read).
        CreateWorldBox(root, "AccentStrip_Spine_L", glow, new Vector3(-0.75f, 2.55f, 3.3f),
            new Vector3(0.06f, 0.05f, 5.6f), false);
        CreateWorldBox(root, "AccentStrip_Spine_R", glow, new Vector3(0.75f, 2.55f, 3.3f),
            new Vector3(0.06f, 0.05f, 5.6f), false);
    }

    // ---- Doors ----

    private static void BuildDoors(Transform root, Material dark, Material doorPanel,
        Material glow)
    {
        // D1: bridge door at z = 6.3, gap x -0.9..0.9 (approached from -z).
        BuildDoor(root, 1, new Vector3(0f, 0f, 6.3f), 0f,
            new Vector3(-0.9f, 0f, 6.3f), new Vector3(0.9f, 0f, 6.3f),
            1.8f, 1.15f, dark, doorPanel, glow);

        // D2: preparation door at x = -1.1, gap z 2.7..4.1.
        BuildDoor(root, 2, new Vector3(-1.1f, 0f, 3.4f), 90f,
            new Vector3(-1.1f, 0f, 2.7f), new Vector3(-1.1f, 0f, 4.1f),
            1.4f, 0.92f, dark, doorPanel, glow);

        // D3: fabrication door at x = +1.1, gap z 2.7..4.1.
        BuildDoor(root, 3, new Vector3(1.1f, 0f, 3.4f), 270f,
            new Vector3(1.1f, 0f, 2.7f), new Vector3(1.1f, 0f, 4.1f),
            1.4f, 0.92f, dark, doorPanel, glow);
    }

    private static void BuildDoor(Transform root, int doorId, Vector3 center, float yawDeg,
        Vector3 spanA, Vector3 spanB, float gapWidth, float travel, Material dark,
        Material panelMat, Material glow)
    {
        var door = new GameObject("ShipDoor_" + doorId.ToString("00"));
        door.transform.SetParent(root, false);
        door.transform.position = center;
        door.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

        // Solid blocker while effectively shut (door safety collider).
        var blockerGo = new GameObject("Blocker");
        blockerGo.transform.SetParent(door.transform, false);
        blockerGo.transform.localPosition = new Vector3(0f, 1.25f, 0f);
        var blocker = blockerGo.AddComponent<BoxCollider>();
        blocker.size = new Vector3(gapWidth, 2.5f, 0.2f);
        blocker.enabled = false;

        // Door panels (children, local space). Closed panels exactly cover the
        // gap; sliding outward hides them inside the adjacent bulkhead.
        float half = gapWidth * 0.5f;
        GameObject panelL = CreateLocalBox(door.transform, "Panel_L", panelMat,
            new Vector3(-half * 0.5f, 1.2f, 0f), new Vector3(half, 2.3f, 0.14f), false);
        GameObject panelR = CreateLocalBox(door.transform, "Panel_R", panelMat,
            new Vector3(half * 0.5f, 1.2f, 0f), new Vector3(half, 2.3f, 0.14f), false);

        // Jamb / header frame trims (visual edge of the opening).
        CreateLocalBox(door.transform, "Frame_Jamb_L", dark,
            new Vector3(-half - 0.08f, 1.25f, 0f), new Vector3(0.16f, 2.5f, 0.5f), false);
        CreateLocalBox(door.transform, "Frame_Jamb_R", dark,
            new Vector3(half + 0.08f, 1.25f, 0f), new Vector3(0.16f, 2.5f, 0.5f), false);
        CreateLocalBox(door.transform, "Frame_Header", dark,
            new Vector3(0f, 2.52f, 0f), new Vector3(gapWidth + 0.3f, 0.2f, 0.5f), false);
        CreateLocalBox(door.transform, "Header_Accent", glow,
            new Vector3(0f, 2.46f, 0.05f), new Vector3(gapWidth + 0.2f, 0.05f, 0.06f), false);

        // Gaze target trigger across the whole doorway (works open or shut).
        var trigger = door.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 1.15f, 0f);
        trigger.size = new Vector3(gapWidth, 2.3f, 0.4f);

        var component = door.AddComponent<ShipDoorInteractable>();
        Vector3 localA = door.transform.InverseTransformPoint(spanA);
        Vector3 localB = door.transform.InverseTransformPoint(spanB);
        component.Configure(doorId, localA, localB, panelL.transform, panelR.transform,
            blocker, travel, 0.85f, ShipDoorInteractable.ShipDoorState.Open);
    }

    // ---- Physical consoles (placeholders for 07A) ----

    private static void BuildConsoles(Transform root, Material consoleLight,
        Material consoleDark, Material screenMat, Material glow)
    {
        BuildConsole(root, 1, "总览", new Vector3(0f, 0f, 10.05f), 180f);
        BuildConsole(root, 2, "出发准备", new Vector3(-4.95f, 0f, 3.4f), 90f);
        BuildConsole(root, 3, "资源加工", new Vector3(4.95f, 0f, 3.4f), 270f);
        BuildConsole(root, 4, "据点", new Vector3(-2.2f, 0f, -8.85f), 0f);

        void BuildConsole(Transform parent, int id, string zhName, Vector3 position, float yawDeg)
        {
            var console = new GameObject("ShipConsole_" + id.ToString("00") + "_" + zhName);
            console.transform.SetParent(parent, false);
            console.transform.position = position;
            console.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

            // Local +z = screen normal (faces the room). Body behind the
            // screen toward the wall occupies local z -0.55..0.
            CreateLocalBox(console.transform, "Pedestal", consoleDark,
                new Vector3(0f, 0.43f, -0.28f), new Vector3(0.6f, 0.86f, 0.6f), false);
            CreateLocalBox(console.transform, "Housing", consoleLight,
                new Vector3(0f, 1.32f, -0.06f), new Vector3(0.95f, 0.92f, 0.2f), false);
            CreateLocalBox(console.transform, "Housing_Trim", consoleDark,
                new Vector3(0f, 1.32f, 0.0f), new Vector3(1.0f, 0.98f, 0.03f), false);

            // Screen quad (16:10 logical, 0.62 x 0.39 with the frame trim).
            GameObject screen = CreateLocalQuad(console.transform, "Screen", screenMat,
                new Vector3(0f, 1.34f, 0.045f), new Vector3(0.62f, 0.39f, 1f));
            screen.transform.localRotation = Quaternion.identity;

            // Small teal status LED + top accent strip (restrained).
            CreateLocalBox(console.transform, "LedStrip", glow,
                new Vector3(0f, 0.86f, 0.035f), new Vector3(0.5f, 0.03f, 0.05f), false);

            // Solid console body collider (players cannot walk through it).
            var body = console.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.92f, -0.22f);
            body.size = new Vector3(0.85f, 1.8f, 0.62f);

            // Gaze interaction trigger in front of the screen.
            var trigger = console.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 1.15f, 0.95f);
            trigger.size = new Vector3(1.6f, 1.5f, 1.0f);

            var interactable = console.AddComponent<ShipConsoleInteractable>();
            interactable.Configure(id, zhName);
        }
    }

    // ---- Airlock / EVA deployment station ----

    private static void BuildAirlock(Transform root, Material airlockMat, Material dark,
        Material glow)
    {
        var station = new GameObject("Airlock_DeployStation");
        station.transform.SetParent(root, false);
        station.transform.position = new Vector3(0f, 0f, -13.85f);

        // Low console/pedestal style station backing onto the aft hull.
        CreateLocalBox(station.transform, "Station_Body", airlockMat,
            new Vector3(0f, 0.95f, -0.18f), new Vector3(1.3f, 1.9f, 0.6f), false);
        CreateLocalBox(station.transform, "Station_Trim", dark,
            new Vector3(0f, 1.95f, -0.1f), new Vector3(1.5f, 0.15f, 0.7f), false);
        CreateLocalBox(station.transform, "Station_Accent", glow,
            new Vector3(0f, 0.12f, 0.25f), new Vector3(1.2f, 0.04f, 0.06f), false);

        // Wall hatch plate + teal flanking bars (visual marker of the EVA
        // hatch; all station children are authored in station-local space).
        CreateLocalBox(station.transform, "Hatch_Plate", dark,
            new Vector3(0f, 1.45f, -0.52f), new Vector3(1.5f, 1.9f, 0.1f), false);
        CreateLocalBox(station.transform, "Hatch_Bar_L", glow,
            new Vector3(-0.8f, 1.45f, -0.48f), new Vector3(0.08f, 1.7f, 0.05f), false);
        CreateLocalBox(station.transform, "Hatch_Bar_R", glow,
            new Vector3(0.8f, 1.45f, -0.48f), new Vector3(0.08f, 1.7f, 0.05f), false);

        var body = station.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, 0.95f, -0.18f);
        body.size = new Vector3(1.3f, 1.9f, 0.6f);

        var trigger = station.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 1.1f, 1.0f);
        trigger.size = new Vector3(2.0f, 1.8f, 1.4f);

        station.AddComponent<ShipAirlockInteractable>();
    }

    // ---- Spawn markers ----

    private static void BuildSpawns(Material accent, Material glow)
    {
        BuildSpawn("ShipSpawn_Default", ShipSpawnMarker.DefaultId, new Vector3(0f, 0f, 3.6f), 0f);
        BuildSpawn("ShipSpawn_FromSurface", ShipSpawnMarker.FromSurfaceId,
            new Vector3(0f, 0f, -11.8f), 0f);

        void BuildSpawn(string name, string markerId, Vector3 position, float yawDeg)
        {
            var marker = new GameObject(name);
            marker.transform.position = position;
            marker.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

            // Disc authored in marker-local space (parent already positioned).
            GameObject disc = CreatePrimitive(PrimitiveType.Cylinder, "Spawn_Disc", accent,
                Vector3.zero, new Vector3(0.45f, 0.03f, 0.45f));
            disc.transform.SetParent(marker.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            Object.DestroyImmediate(disc.GetComponent<Collider>());

            var component = marker.AddComponent<ShipSpawnMarker>();
            component.Configure(markerId);
        }
    }

    // ---- Compartment props (primitive silhouettes only) ----

    private static void BuildProps(Material prop, Material dark, Material accent,
        Material glow, Material airlockMat)
    {
        // Preparation / gear bay: lockers + equipment rack hints (west side).
        var prepProps = new GameObject("Prep_Props");
        CreateWorldBox(prepProps.transform, "GearLocker_01", dark, new Vector3(-4.5f, 1.0f, 5.6f),
            new Vector3(0.9f, 2.0f, 0.55f), false);
        CreateWorldBox(prepProps.transform, "GearLocker_02", dark, new Vector3(-3.3f, 1.0f, 5.6f),
            new Vector3(0.9f, 2.0f, 0.55f), false);
        CreateWorldBox(prepProps.transform, "GearLocker_03", prop, new Vector3(-2.1f, 1.0f, 5.6f),
            new Vector3(0.9f, 2.0f, 0.55f), false);
        CreateWorldBox(prepProps.transform, "GearRack", dark, new Vector3(-4.9f, 1.25f, 5.9f),
            new Vector3(1.15f, 2.5f, 0.2f), false);
        CreateLocalBox(prepProps.transform, "Rack_Led", glow, new Vector3(-4.9f, 1.3f, 5.85f),
            new Vector3(0.9f, 0.04f, 0.06f), false);

        // Fabrication bay: fabricator unit + tanks + ceiling pipes.
        var fabProps = new GameObject("Fab_Props");
        CreateWorldBox(fabProps.transform, "FabricatorUnit", prop, new Vector3(3.4f, 1.0f, 5.3f),
            new Vector3(1.7f, 2.0f, 1.1f), false);
        CreateWorldBox(fabProps.transform, "Fabricator_Trim", dark,
            new Vector3(3.4f, 2.0f, 5.3f), new Vector3(1.85f, 0.1f, 1.25f), false);
        GameObject tank1 = CreatePrimitive(PrimitiveType.Cylinder, "Tank_01", prop,
            new Vector3(4.7f, 0.9f, 4.6f), new Vector3(0.3f, 1.8f, 0.3f));
        tank1.transform.SetParent(fabProps.transform, false);
        Object.DestroyImmediate(tank1.GetComponent<Collider>());
        GameObject tank2 = CreatePrimitive(PrimitiveType.Cylinder, "Tank_02", dark,
            new Vector3(2.1f, 0.85f, 5.7f), new Vector3(0.24f, 1.7f, 0.24f));
        tank2.transform.SetParent(fabProps.transform, false);
        Object.DestroyImmediate(tank2.GetComponent<Collider>());

        // Outpost / comms bay: mast + dish + terminal bank against the walls.
        var outpostProps = new GameObject("Outpost_Props");
        GameObject mast = CreatePrimitive(PrimitiveType.Cylinder, "CommsMast", airlockMat,
            new Vector3(-5.0f, 1.2f, -1.4f), new Vector3(0.14f, 2.4f, 0.14f));
        mast.transform.SetParent(outpostProps.transform, false);
        Object.DestroyImmediate(mast.GetComponent<Collider>());
        GameObject dish = CreatePrimitive(PrimitiveType.Sphere, "CommsDish", dark,
            new Vector3(-4.55f, 2.35f, -1.4f), new Vector3(0.6f, 0.18f, 0.6f));
        dish.transform.SetParent(outpostProps.transform, false);
        Object.DestroyImmediate(dish.GetComponent<Collider>());
        CreateWorldBox(outpostProps.transform, "CommsBank", dark, new Vector3(5.1f, 0.9f, -2.6f),
            new Vector3(0.7f, 1.8f, 3.2f), false);
        CreateLocalBox(outpostProps.transform, "CommsBank_Led", glow,
            new Vector3(5.15f, 0.95f, -2.6f), new Vector3(0.05f, 0.06f, 2.6f), false);

        // Airlock: suit lockers flanking the deployment station.
        var airlockProps = new GameObject("Airlock_Props");
        CreateWorldBox(airlockProps.transform, "SuitLocker_W", prop, new Vector3(-2.6f, 1.0f, -12.6f),
            new Vector3(0.6f, 2.0f, 1.2f), false);
        CreateWorldBox(airlockProps.transform, "SuitLocker_E", prop, new Vector3(2.6f, 1.0f, -12.6f),
            new Vector3(0.6f, 2.0f, 1.2f), false);
    }

    // ---- Lighting ----

    private static void BuildLights(Transform root, Material mat)
    {
        AddPoint(root, "PointLight_Bridge", new Vector3(0f, 2.4f, 8.6f), 2.8f, 9f, true);
        AddPoint(root, "PointLight_Spine_01", new Vector3(0f, 2.4f, 4.8f), 2.4f, 8f, true);
        AddPoint(root, "PointLight_Spine_02", new Vector3(0f, 2.4f, 1.0f), 2.2f, 7f, false);
        AddPoint(root, "PointLight_Prep", new Vector3(-3.3f, 2.4f, 3.4f), 2.4f, 8f, false);
        AddPoint(root, "PointLight_Fab", new Vector3(3.3f, 2.4f, 3.4f), 2.4f, 8f, false);
        AddPoint(root, "PointLight_Outpost_01", new Vector3(-1.8f, 2.4f, -4.0f), 2.3f, 8f, false);
        AddPoint(root, "PointLight_Outpost_02", new Vector3(2.0f, 2.4f, -6.5f), 2.2f, 8f, false);
        AddPoint(root, "PointLight_Airlock", new Vector3(0f, 2.4f, -11.9f), 2.3f, 8f, false);
    }

    private static void AddPoint(Transform root, string name, Vector3 position,
        float intensity, float range, bool softShadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.position = position;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.92f, 0.96f, 1.0f);
        light.intensity = intensity;
        light.range = range;
        light.shadows = softShadows ? LightShadows.Soft : LightShadows.None;
    }

    // ---- Materials ----

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
        AssetDatabase.CreateFolder(parent, "Ship");

        return Ensure("M_ShipFloor", new Color(0.235f, 0.262f, 0.278f), 0.4f)
               & Ensure("M_ShipCeiling", new Color(0.83f, 0.86f, 0.89f), 0.5f)
               & Ensure("M_ShipWallLight", new Color(0.80f, 0.84f, 0.88f), 0.35f)
               & Ensure("M_ShipFrameDark", new Color(0.16f, 0.18f, 0.21f), 0.3f)
               & Ensure("M_ShipDoor", new Color(0.62f, 0.67f, 0.71f), 0.4f)
               & Ensure("M_ShipAccent", new Color(0.02f, 0.33f, 0.38f), 0.3f)
               & EnsureGlow("M_ShipAccentGlow", new Color(0.015f, 0.10f, 0.12f),
                   new Color(0.5f, 2.6f, 2.8f))
               & Ensure("M_ShipConsoleLight", new Color(0.85f, 0.88f, 0.90f), 0.4f)
               & Ensure("M_ShipConsoleDark", new Color(0.12f, 0.14f, 0.16f), 0.3f)
               & Ensure("M_ShipScreen", new Color(0.015f, 0.02f, 0.025f), 0.2f)
               & Ensure("M_ShipProp", new Color(0.21f, 0.24f, 0.27f), 0.35f)
               & Ensure("M_ShipAirlock", new Color(0.52f, 0.58f, 0.62f), 0.35f)
               & EnsureGlow("M_ShipViewport", new Color(0.05f, 0.20f, 0.24f),
                   new Color(0.30f, 1.1f, 1.3f));
    }

    private static bool Ensure(string name, Color baseColor, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[ShipInteriorBuilder] URP Lit shader missing");
            return false;
        }

        var material = new Material(shader);
        material.SetColor("_BaseColor", baseColor);
        material.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(material, MaterialsFolder + "/" + name + ".mat");
        return true;
    }

    private static bool EnsureGlow(string name, Color baseColor, Color emission)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[ShipInteriorBuilder] URP Lit shader missing");
            return false;
        }

        var material = new Material(shader);
        material.SetColor("_BaseColor", baseColor);
        material.SetFloat("_Smoothness", 0.3f);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission);
        AssetDatabase.CreateAsset(material, MaterialsFolder + "/" + name + ".mat");
        return true;
    }

    private static Material Load(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/" + name + ".mat");
    }

    // ---- Geometry helpers ----

    private static void WallAlongZ(Transform parent, float x, float z0, float z1,
        Material material, float thickness)
    {
        CreateWorldBox(parent, "Wall", material,
            new Vector3(x, Ceiling * 0.5f, (z0 + z1) * 0.5f),
            new Vector3(thickness, Ceiling, z1 - z0), true);
    }

    private static void WallAlongX(Transform parent, float x0, float x1, float z,
        Material material, float thickness)
    {
        CreateWorldBox(parent, "Wall", material,
            new Vector3((x0 + x1) * 0.5f, Ceiling * 0.5f, z),
            new Vector3(x1 - x0, Ceiling, thickness), true);
    }

    private static GameObject CreateWorldBox(Transform parent, string name, Material material,
        Vector3 center, Vector3 size, bool withCollider)
    {
        var go = CreatePrimitive(PrimitiveType.Cube, name, material, center, size);
        if (!withCollider)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        if (parent != null)
        {
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
        }

        return go;
    }

    private static GameObject CreateLocalBox(Transform parent, string name, Material material,
        Vector3 localCenter, Vector3 localSize, bool withCollider)
    {
        var go = CreatePrimitive(PrimitiveType.Cube, name, material, Vector3.zero, Vector3.one);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localCenter;
        go.transform.localScale = localSize;
        if (!withCollider)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        return go;
    }

    private static GameObject CreateLocalQuad(Transform parent, string name, Material material,
        Vector3 localCenter, Vector3 localScale)
    {
        var go = CreatePrimitive(PrimitiveType.Quad, name, material, Vector3.zero, Vector3.one);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localCenter;
        go.transform.localScale = localScale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
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
}
