using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 09A_2: the import contract for the Codex first-person arms asset.
///
/// The FBX is produced by the frozen Phase 09A_1 Blender workflow and is the
/// AUTHORITATIVE art asset: this file never edits, rescales or renames the
/// source. It only enforces the Unity Import Settings the HANDOFF contract
/// requires, and it re-applies them on EVERY reimport so a stale import can
/// never silently drift:
///
///   Rig            = Generic, avatar created from this model
///   Root Motion    = off (the whole chain is presentation only)
///   Clips          = FP_Idle / FP_Walk / Wrist_Raise / Wrist_Idle /
///                    Wrist_Lower / Interact_Right, exact Codex names
///   Loops          = FP_Idle, FP_Walk, Wrist_Idle
///   Colliders      = none (the arms must never touch world physics/rays)
///   Materials      = mapped to the project URP materials by the exact
///                    MAT_* slot names of the HANDOFF (stable slot order)
///
/// The matching copy step, material authoring and evidence report live in
/// <see cref="SurviveFirstPersonArmsSetup"/>.
/// </summary>
public class FirstPersonArmsImportContract : AssetPostprocessor
{
    /// <summary>Unity runtime path of the imported first-person arms model.</summary>
    public const string ModelPath = "Assets/_Project/Models/FirstPerson/FP_PlayerArms.fbx";

    /// <summary>Folder holding the project URP materials used by the arms.</summary>
    public const string MaterialsFolder = "Assets/_Project/Materials/FirstPerson";

    /// <summary>The five HANDOFF material slots, in their stable order.</summary>
    public static readonly string[] MaterialSlotNames =
    {
        "MAT_Glove",
        "MAT_Sleeve",
        "MAT_DeviceBody",
        "MAT_DeviceAccent",
        "MAT_DeviceEmissive"
    };

    /// <summary>The six Codex clip names, in HANDOFF order.</summary>
    public static readonly string[] ClipNames =
    {
        "FP_Idle",
        "FP_Walk",
        "Wrist_Raise",
        "Wrist_Idle",
        "Wrist_Lower",
        "Interact_Right"
    };

    /// <summary>Clips that must loop (HANDOFF §5).</summary>
    public static readonly string[] LoopClipNames =
    {
        "FP_Idle",
        "FP_Walk",
        "Wrist_Idle"
    };

    public static bool IsLoopClip(string clipName)
    {
        for (int i = 0; i < LoopClipNames.Length; i++)
        {
            if (string.Equals(LoopClipNames[i], clipName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsArmsModel(string assetPath)
    {
        return string.Equals(Normalize(assetPath), ModelPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }

    /// <summary>
    /// Enforces the FBX import settings before Unity imports the model, so the
    /// contract holds for the first import and for every reimport.
    /// </summary>
    private void OnPreprocessModel()
    {
        if (!IsArmsModel(assetPath))
        {
            return;
        }

        var importer = assetImporter as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("[FPArmsImport] " + assetPath + " is not a ModelImporter");
            return;
        }

        // ---- Rig: Generic + avatar from this model, no root motion ----
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar = null;
        importer.importAnimation = true;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importVisibility = false;
        importer.addCollider = false;       // never a world collider (contract §57)
        importer.isReadable = false;
        importer.importBlendShapes = false;
        importer.optimizeGameObjects = false; // WristDevice_L must stay addressable

        // The baked 30 FPS clips carry subtle breathing/micro motion; keyframe
        // reduction would eat exactly that, so compression stays off.
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.resampleCurves = true;

        // ---- Materials: keep the FBX slot identity, remap by name ----
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.External;
        importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
        importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;

        // ---- Clips: exact Codex names, exact loop contract ----
        // Unity only exposes the discovered takes here once the model has been
        // scanned with animation import ON, so the very first pass can legitimately
        // report none. SurviveFirstPersonArmsSetup applies the authoritative clip
        // contract right after that pass; from then on this block re-asserts it on
        // every reimport (a stale loop flag can never survive).
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
        {
            return;
        }

        for (int i = 0; i < clips.Length; i++)
        {
            string name = clips[i].name;
            string matched = MatchClipName(name);
            if (matched == null)
            {
                Debug.LogError("[FPArmsImport] unexpected animation take '" + name
                               + "' in " + assetPath);
                continue;
            }

            clips[i].name = matched;
            clips[i].loopTime = IsLoopClip(matched);
            clips[i].loopPose = IsLoopClip(matched);
            clips[i].lockRootRotation = true;
            clips[i].lockRootHeightY = true;
            clips[i].lockRootPositionXZ = true;
            clips[i].keepOriginalOrientation = true;
            clips[i].keepOriginalPositionY = true;
            clips[i].keepOriginalPositionXZ = true;
        }

        importer.clipAnimations = clips;
    }

    /// <summary>
    /// Maps an FBX take name onto its contract clip name. Blender may export a
    /// take as "RIG_FPArms|FP_Idle" depending on the NLA setup, so the take is
    /// matched by its trailing clip segment.
    /// </summary>
    public static string MatchClipName(string takeName)
    {
        if (string.IsNullOrEmpty(takeName))
        {
            return null;
        }

        for (int i = 0; i < ClipNames.Length; i++)
        {
            if (string.Equals(takeName, ClipNames[i], StringComparison.Ordinal))
            {
                return ClipNames[i];
            }
        }

        for (int i = 0; i < ClipNames.Length; i++)
        {
            if (takeName.EndsWith("|" + ClipNames[i], StringComparison.Ordinal)
                || takeName.EndsWith("_" + ClipNames[i], StringComparison.Ordinal))
            {
                return ClipNames[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Assigns the project URP materials to the imported model by the source
    /// material name, so the five HANDOFF slots never depend on Unity's
    /// auto-generated material search results.
    /// </summary>
    private Material OnAssignMaterialModel(Material material, Renderer renderer)
    {
        if (!IsArmsModel(assetPath) || material == null)
        {
            return null;
        }

        Material project = AssetDatabase.LoadAssetAtPath<Material>(
            MaterialsFolder + "/" + material.name + ".mat");
        return project; // null keeps Unity's default behaviour
    }
}
