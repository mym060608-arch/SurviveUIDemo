using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

/// <summary>
/// Phase 09B editor entry points: attach the controller-interaction evidence probe
/// to the boot scene (the same one-inert-root-object pattern every Phase 07/08/09
/// probe uses) and import the guard assets the phase needs.
///
/// The probe is inert unless the player is launched with
/// <c>-ship09BShot &lt;folder&gt;</c>, so the shipped boot route is unchanged. The
/// boot scene is opened ADDITIVELY and only that scene is written, so no other
/// scene can be touched.
/// </summary>
public static class SurvivePhase09BSetup
{
    private const string BootScenePath = "Assets/_Project/Scenes/00_Boot.unity";
    private const string ProbeObjectName = "Ship09BControllerProbe";

    [MenuItem("Tools/SurviveUI/Phase 09B/Attach Evidence Probe To Boot Scene")]
    public static void AttachProbeFromMenu()
    {
        Debug.Log(AttachProbeToBootScene());
    }

    /// <summary>
    /// Batch entry point:
    /// -executeMethod SurvivePhase09BSetup.AttachProbe
    /// </summary>
    public static void AttachProbe()
    {
        string report = AttachProbeToBootScene();
        Debug.Log("[Phase09BSetup] " + report);
        bool ok = report.StartsWith("updated", StringComparison.Ordinal)
                  || report.StartsWith("probe already", StringComparison.Ordinal);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>Adds the Phase 09B probe to the boot scene exactly once.</summary>
    public static string AttachProbeToBootScene()
    {
        if (!File.Exists(BootScenePath))
        {
            return "FAILED: " + BootScenePath + " not found";
        }

        Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
        bool changed = false;

        bool alreadyAttached = false;
        foreach (Ship09BControllerProbe existing in UnityEngine.Object
                     .FindObjectsByType<Ship09BControllerProbe>(FindObjectsInactive.Include))
        {
            if (existing.gameObject.scene == scene)
            {
                alreadyAttached = true;
            }
        }

        if (!alreadyAttached)
        {
            var probeGo = new GameObject(ProbeObjectName);
            SceneManager.MoveGameObjectToScene(probeGo, scene);
            probeGo.AddComponent<Ship09BControllerProbe>();
            changed = true;
        }

        if (!changed)
        {
            EditorSceneManager.CloseScene(scene, true);
            return "probe already attached to " + BootScenePath;
        }

        bool saved = EditorSceneManager.SaveScene(scene, BootScenePath);
        EditorSceneManager.CloseScene(scene, true);
        return (saved ? "updated " : "FAILED to update ") + ProbeObjectName
               + " in " + BootScenePath;
    }
}
