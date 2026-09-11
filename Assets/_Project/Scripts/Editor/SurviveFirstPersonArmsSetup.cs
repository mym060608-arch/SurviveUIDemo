using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Phase 09A_2 import entry point (Unity integration side of the Codex handoff).
///
/// It performs ONLY the integration work the HANDOFF assigns to Phase 09A_2:
///
///   1. copy the frozen FBX out of Artsource/ into the runtime asset tree
///      (Unity can only import assets under Assets/ - WORKFLOW §4.1),
///   2. ensure the FirstPersonArms layer exists,
///   3. author the five project URP materials named exactly after the HANDOFF
///      material slots (the import contract maps them automatically),
///   4. write a machine-readable evidence report of what Unity actually
///      imported (clips, loops, bones, meshes, materials, measured size).
///
/// The Blender source asset is never modified, rescaled or renamed.
///
/// CLI: -executeMethod SurviveFirstPersonArmsSetup.Import -resultPath &lt;file&gt;
/// </summary>
public static class SurviveFirstPersonArmsSetup
{
    public const string SourceFbxPath =
        "ArtSource/Phase09A_1/output/FP_PlayerArms.fbx";

    public const string ModelPath = FirstPersonArmsImportContract.ModelPath;
    public const string ModelFolder = "Assets/_Project/Models/FirstPerson";
    public const string MaterialsFolder = FirstPersonArmsImportContract.MaterialsFolder;
    public const string ArmsLayerName = "FirstPersonArms";

    /// <summary>Default evidence path when no -resultPath is supplied.</summary>
    private const string DefaultReportRelativePath = "../export/phase09a2_import_report.txt";

    [MenuItem("Tools/SurviveUI/Phase 09A_2/Import First-Person Arms")]
    public static void ImportFromMenu()
    {
        bool ok = Import(out string report);
        Debug.Log(report);
        if (!ok)
        {
            Debug.LogError("[FPArmsSetup] import reported problems");
        }
    }

    /// <summary>Boot scene the evidence probe lives in (same pattern as every
    /// other Phase 07/08 probe: one inert root object).</summary>
    private const string BootScenePath = "Assets/_Project/Scenes/00_Boot.unity";
    private const string ProbeObjectName = "Ship09A2ShotProbe";

    [MenuItem("Tools/SurviveUI/Phase 09A_2/Attach Evidence Probe To Boot Scene")]
    public static void AttachProbeFromMenu()
    {
        Debug.Log(AttachProbeToBootScene());
    }

    /// <summary>
    /// Adds the Phase 09A_2 evidence probe to 00_Boot exactly once. The probe is
    /// inert unless it is launched with -ship09A2Shot, so the shipped boot route
    /// is unchanged; the scene is opened additively and only that scene is
    /// written, so no other scene can be touched.
    /// </summary>
    public static string AttachProbeToBootScene()
    {
        UnityEngine.SceneManagement.Scene scene =
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(BootScenePath,
                UnityEditor.SceneManagement.OpenSceneMode.Additive);

        foreach (Ship09A2ShotProbe existing in UnityEngine.Object.FindObjectsByType<Ship09A2ShotProbe>(
                     FindObjectsInactive.Include))
        {
            if (existing.gameObject.scene == scene)
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                return "probe already attached to " + BootScenePath;
            }
        }

        var probeGo = new GameObject(ProbeObjectName);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(probeGo, scene);
        probeGo.AddComponent<Ship09A2ShotProbe>();
        bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, BootScenePath);
        UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        return (saved ? "attached " : "FAILED to attach ") + ProbeObjectName
               + " to " + BootScenePath;
    }

    public static void Import()
    {
        string resultPath = GetArgValue("-resultPath");
        bool ok = Import(out string report);

        if (string.IsNullOrEmpty(resultPath))
        {
            resultPath = Path.GetFullPath(Path.Combine(Application.dataPath,
                DefaultReportRelativePath));
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
            File.WriteAllText(resultPath, report);
            Debug.Log("[FPArmsSetup] wrote report " + resultPath);
        }
        catch (Exception e)
        {
            Debug.LogError("[FPArmsSetup] cannot write report: " + e.Message);
            ok = false;
        }

        Debug.Log(report);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>
    /// Runs the whole import contract and returns the evidence report.
    /// </summary>
    public static bool Import(out string report)
    {
        var sb = new StringBuilder();
        bool ok = true;

        void Check(bool pass, string label)
        {
            sb.AppendLine((pass ? "PASS" : "FAIL") + " :: " + label);
            ok &= pass;
        }

        sb.AppendLine("=== PHASE 09A_2 FIRST-PERSON ARMS IMPORT REPORT ===");
        sb.AppendLine("unity: " + Application.unityVersion);
        sb.AppendLine("project: " + Path.GetFullPath(Path.Combine(Application.dataPath, "..")));

        // ---- 1. source asset -> runtime asset tree ----
        string absoluteSource = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", SourceFbxPath));
        sb.AppendLine("source: " + absoluteSource);
        sb.AppendLine("sourceExists: " + File.Exists(absoluteSource));
        sb.AppendLine("sourceBytes: " + (File.Exists(absoluteSource)
            ? new FileInfo(absoluteSource).Length.ToString(CultureInfo.InvariantCulture)
            : "<missing>"));

        if (!File.Exists(absoluteSource))
        {
            Check(false, "Codex FBX present in " + SourceFbxPath);
            report = sb.ToString();
            return false;
        }

        EnsureFolder("Assets/_Project/Models");
        EnsureFolder(ModelFolder);
        string absoluteTarget = Path.GetFullPath(ModelPath);
        bool copyNeeded = !File.Exists(absoluteTarget)
                          || new FileInfo(absoluteTarget).Length
                          != new FileInfo(absoluteSource).Length
                          || !FilesEqual(absoluteSource, absoluteTarget);
        if (copyNeeded)
        {
            File.Copy(absoluteSource, absoluteTarget, true);
            sb.AppendLine("copied: yes");
        }
        else
        {
            sb.AppendLine("copied: no (runtime copy already identical to the frozen source)");
        }

        // ---- 2. layer ----
        int layer = EnsureLayer(ArmsLayerName, out string layerReport);
        Check(layer > 0, "layer " + ArmsLayerName + " available (" + layerReport + ")");
        sb.AppendLine("layerIndex: " + layer);

        // ---- 3. materials ----
        EnsureFolder("Assets/_Project/Materials");
        EnsureFolder(MaterialsFolder);
        for (int i = 0; i < FirstPersonArmsImportContract.MaterialSlotNames.Length; i++)
        {
            string name = FirstPersonArmsImportContract.MaterialSlotNames[i];
            EnsureMaterial(name);
        }

        AssetDatabase.Refresh();

        // ---- 4. import (contract postprocessor applies the rig/animation) ----
        AssetDatabase.ImportAsset(ModelPath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Check(false, "imported model loadable at " + ModelPath);
            report = sb.ToString();
            return false;
        }

        // ---- 4b. clip contract (names + loop flags) ----
        // The take list only exists after the first real import pass, so the
        // loop contract is applied here through the importer and saved.
        var clipImporter = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        ModelImporterClipAnimation[] takes = clipImporter.defaultClipAnimations;
        sb.AppendLine("takesDiscovered: " + (takes != null ? takes.Length : 0));
        if (takes != null && takes.Length > 0)
        {
            for (int i = 0; i < takes.Length; i++)
            {
                string matched = FirstPersonArmsImportContract.MatchClipName(takes[i].name);
                if (matched == null)
                {
                    Check(false, "take '" + takes[i].name + "' is not one of the six Codex clips");
                    continue;
                }

                takes[i].name = matched;
                takes[i].loopTime = FirstPersonArmsImportContract.IsLoopClip(matched);
                takes[i].loopPose = FirstPersonArmsImportContract.IsLoopClip(matched);
                takes[i].lockRootRotation = true;
                takes[i].lockRootHeightY = true;
                takes[i].lockRootPositionXZ = true;
            }

            clipImporter.clipAnimations = takes;
            clipImporter.SaveAndReimport();
            model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        }
        else
        {
            Check(false, "the FBX exposes animation takes");
        }

        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Check(importer != null && importer.animationType == ModelImporterAnimationType.Generic,
            "rig type is Generic (actual: "
            + (importer != null ? importer.animationType.ToString() : "<no importer>") + ")");

        // ---- 5. avatar ----
        Avatar avatar = null;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is Avatar candidate)
            {
                avatar = candidate;
            }
        }

        Check(avatar != null, "avatar sub-asset created");
        if (avatar != null)
        {
            sb.AppendLine("avatarName: " + avatar.name);
            sb.AppendLine("avatarIsValid: " + avatar.isValid);
            sb.AppendLine("avatarIsHuman: " + avatar.isHuman);
            Check(avatar.isValid, "avatar is valid");
            Check(!avatar.isHuman, "avatar is a generic rig (not humanoid)");
        }

        // ---- 6. clips ----
        var clips = new List<AnimationClip>();
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            {
                clips.Add(clip);
            }
        }

        sb.AppendLine("clipsFound: " + clips.Count);
        var foundNames = new List<string>();
        for (int i = 0; i < FirstPersonArmsImportContract.ClipNames.Length; i++)
        {
            string wanted = FirstPersonArmsImportContract.ClipNames[i];
            AnimationClip clip = clips.Find(c => string.Equals(c.name, wanted,
                StringComparison.Ordinal));
            if (clip == null)
            {
                Check(false, "clip " + wanted + " imported with its exact Codex name");
                continue;
            }

            foundNames.Add(clip.name);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            bool shouldLoop = FirstPersonArmsImportContract.IsLoopClip(wanted);
            sb.AppendLine("clip: " + clip.name
                          + " length=" + clip.length.ToString("0.###", CultureInfo.InvariantCulture)
                          + "s frameRate=" + clip.frameRate.ToString("0.###", CultureInfo.InvariantCulture)
                          + " loop=" + settings.loopTime
                          + " isLooping=" + clip.isLooping
                          + " expectedLoop=" + shouldLoop
                          + " frames~" + Mathf.RoundToInt(clip.length * clip.frameRate + 1f));
            Check(settings.loopTime == shouldLoop && clip.isLooping == shouldLoop,
                "clip " + wanted + (shouldLoop ? " loops" : " does not loop"));
        }

        sb.AppendLine("clipNames: [" + string.Join(", ", foundNames) + "]");
        Check(foundNames.Count == FirstPersonArmsImportContract.ClipNames.Length,
            "all six Codex clips imported");

        // ---- 7. hierarchy / bones / meshes / materials, measured in Unity ----
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var hierarchy = new List<string>();
        var transforms = instance.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in transforms)
        {
            var componentNames = new List<string>();
            foreach (Component component in t.GetComponents<Component>())
            {
                if (component is Transform)
                {
                    continue;
                }

                componentNames.Add(component.GetType().Name);
            }

            Vector3 euler = t.localRotation.eulerAngles;
            hierarchy.Add(RelativePath(instance.transform, t)
                          + "  localPos=" + V(t.localPosition)
                          + " localEuler=" + V(euler)
                          + " localScale=" + V(t.localScale)
                          + " components=[" + string.Join(",", componentNames) + "]");
        }

        sb.AppendLine("transforms: " + transforms.Length);
        foreach (string line in hierarchy)
        {
            sb.AppendLine("  " + line);
        }

        Check(transforms.Length >= 20,
            "rig transforms imported (20 bones + meshes, actual " + transforms.Length + ")");

        Transform rootBone = FindDeep(instance.transform, "Root");
        Transform wristDevice = FindDeep(instance.transform, "WristDevice_L");
        Check(rootBone != null, "Root bone present");
        Check(wristDevice != null, "WristDevice_L present");
        if (wristDevice != null)
        {
            sb.AppendLine("wristDeviceLocalPos: " + V(wristDevice.localPosition));
            sb.AppendLine("wristDeviceInstancePos: " + V(wristDevice.position));
        }

        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        Bounds combined = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            var slotNames = new List<string>();
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                slotNames.Add(materials[i] != null ? materials[i].name : "<null>");
            }

            sb.AppendLine("renderer: " + RelativePath(instance.transform, renderer.transform)
                          + " type=" + renderer.GetType().Name
                          + " slots=[" + string.Join(",", slotNames) + "]"
                          + " boundsCenter=" + V(renderer.bounds.center)
                          + " boundsSize=" + V(renderer.bounds.size)
                          + " materialCount=" + (renderer is SkinnedMeshRenderer skinned
                              ? skinned.sharedMesh != null
                                  ? skinned.sharedMesh.subMeshCount.ToString()
                                  : "0"
                              : "<mesh>"));

            if (hasBounds)
            {
                combined.Encapsulate(renderer.bounds);
            }
            else
            {
                combined = renderer.bounds;
                hasBounds = true;
            }
        }

        if (hasBounds)
        {
            sb.AppendLine("modelBoundsMin: " + V(combined.min));
            sb.AppendLine("modelBoundsMax: " + V(combined.max));
            sb.AppendLine("modelBoundsSize: " + V(combined.size));
            // First-person arms contract §8: 1 unit == 1 m, so a plausible
            // forearm package is a fraction of a metre, never centimetres or
            // tens of metres.
            float size = combined.size.magnitude;
            Check(size > 0.2f && size < 3f,
                "imported scale is plausible in metres (diagonal "
                + size.ToString("0.###", CultureInfo.InvariantCulture) + " m)");
            Check(combined.max.z > 0f,
                "arms sit in front of the imported root (max z "
                + combined.max.z.ToString("0.###", CultureInfo.InvariantCulture) + ")");
        }

        int colliderCount = instance.GetComponentsInChildren<Collider>(true).Length;
        Check(colliderCount == 0, "no colliders imported (actual " + colliderCount + ")");

        // Material slot contract: the imported meshes carry exactly the slots
        // the Blender source assigns to them (the FBX only writes the USED
        // materials per mesh), and every one of those slots must reference a
        // project URP material from Materials/FirstPerson - never a material
        // Unity auto-generated next to the model.
        bool slotsOk = renderers.Length > 0;
        var usedMaterialNames = new List<string>();
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            foreach (Material material in materials)
            {
                if (material == null)
                {
                    slotsOk = false;
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(material);
                bool projectMaterial = string.Equals(
                    System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/'),
                    MaterialsFolder, StringComparison.OrdinalIgnoreCase);
                if (!projectMaterial
                    || Array.IndexOf(FirstPersonArmsImportContract.MaterialSlotNames,
                        material.name) < 0)
                {
                    slotsOk = false;
                }

                if (!usedMaterialNames.Contains(material.name))
                {
                    usedMaterialNames.Add(material.name);
                }
            }
        }

        sb.AppendLine("materialsUsed: [" + string.Join(", ", usedMaterialNames) + "]");
        Check(slotsOk, "every mesh slot references a project material from " + MaterialsFolder);

        bool allFiveUsed = usedMaterialNames.Count
                           == FirstPersonArmsImportContract.MaterialSlotNames.Length;
        Check(allFiveUsed, "all five HANDOFF material slots are used by the asset ("
                           + usedMaterialNames.Count + "/"
                           + FirstPersonArmsImportContract.MaterialSlotNames.Length + ")");

        UnityEngine.Object.DestroyImmediate(instance);

        report = sb.ToString();
        return ok;
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static string V(Vector3 value)
    {
        return "(" + value.x.ToString("0.####", CultureInfo.InvariantCulture)
               + "," + value.y.ToString("0.####", CultureInfo.InvariantCulture)
               + "," + value.z.ToString("0.####", CultureInfo.InvariantCulture) + ")";
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (root == target)
        {
            return "<root>";
        }

        var parts = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            parts.Insert(0, current.name);
            current = current.parent;
        }

        return string.Join("/", parts);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                return t;
            }
        }

        return null;
    }

    private static bool FilesEqual(string a, string b)
    {
        var left = new FileInfo(a);
        var right = new FileInfo(b);
        if (!left.Exists || !right.Exists || left.Length != right.Length)
        {
            return false;
        }

        byte[] leftBytes = File.ReadAllBytes(a);
        byte[] rightBytes = File.ReadAllBytes(b);
        for (int i = 0; i < leftBytes.Length; i++)
        {
            if (leftBytes[i] != rightBytes[i])
            {
                return false;
            }
        }

        return true;
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && parent != "Assets")
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, leaf);
    }

    /// <summary>
    /// Ensures a project layer exists (the arms must live outside every world
    /// physics/interaction layer, contract §14).
    /// </summary>
    public static int EnsureLayer(string layerName, out string report)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 0; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
            {
                report = "already present at index " + i;
                return i;
            }
        }

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                report = "created at index " + i;
                return i;
            }
        }

        report = "no free user layer slot";
        return -1;
    }

    /// <summary>
    /// Authors (or refreshes) one of the five project arms materials. Colours
    /// follow the HANDOFF palette: light suit shell, graphite gloves/joints,
    /// restrained orange identification and low cyan emission only on device
    /// emissive surfaces.
    /// </summary>
    public static Material EnsureMaterial(string name)
    {
        string path = MaterialsFolder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogError("[FPArmsSetup] URP Lit shader not found");
            return existing;
        }

        Material material = existing;
        if (material == null)
        {
            material = new Material(lit);
            material.name = name;
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != lit)
        {
            material.shader = lit;
        }

        switch (name)
        {
            case "MAT_Glove":
                // Thick graphite EVA glove shell.
                material.SetColor("_BaseColor", new Color(0.105f, 0.113f, 0.125f));
                material.SetFloat("_Metallic", 0.05f);
                material.SetFloat("_Smoothness", 0.32f);
                SetEmission(material, Color.black);
                break;
            case "MAT_Sleeve":
                // Off-white / light grey pressurised suit sleeve.
                material.SetColor("_BaseColor", new Color(0.66f, 0.68f, 0.70f));
                material.SetFloat("_Metallic", 0.05f);
                material.SetFloat("_Smoothness", 0.38f);
                SetEmission(material, Color.black);
                break;
            case "MAT_DeviceBody":
                // Layered carrier plate + off-white centre cap of the terminal.
                material.SetColor("_BaseColor", new Color(0.52f, 0.545f, 0.575f));
                material.SetFloat("_Metallic", 0.12f);
                material.SetFloat("_Smoothness", 0.45f);
                SetEmission(material, Color.black);
                break;
            case "MAT_DeviceAccent":
                // Restrained orange identification strip / button collars.
                material.SetColor("_BaseColor", new Color(0.72f, 0.32f, 0.10f));
                material.SetFloat("_Metallic", 0.10f);
                material.SetFloat("_Smoothness", 0.40f);
                SetEmission(material, Color.black);
                break;
            case "MAT_DeviceEmissive":
                // Four cyan screen structures + the projector aperture. Kept
                // deliberately dim (contract §55): the runtime presentation
                // brightens it through a MaterialPropertyBlock while the wrist
                // terminal is open, and it must never bloom over the UI.
                material.SetColor("_BaseColor", new Color(0.035f, 0.10f, 0.115f));
                material.SetFloat("_Metallic", 0.0f);
                material.SetFloat("_Smoothness", 0.55f);
                SetEmission(material, new Color(0.10f, 0.42f, 0.50f));
                break;
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void SetEmission(Material material, Color colour)
    {
        if (colour.maxColorComponent <= 0f)
        {
            material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            return;
        }

        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", colour);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
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
