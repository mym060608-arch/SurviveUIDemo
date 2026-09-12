using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Phase 09A_2 runtime asset builder: the Animator Controller and the arms
/// prefab that the ship interior mounts under its authoritative eye camera.
///
/// Everything here is IDEMPOTENT: re-running rebuilds the controller and the
/// prefab from the imported FBX, so the assets can never drift from the Codex
/// clips. Nothing in this file touches the FBX or its import settings (that is
/// <see cref="FirstPersonArmsImportContract"/>).
///
/// Structure of the prefab:
///
///     FirstPersonArmsRoot           layer FirstPersonArms, framing local pose
///       FirstPersonArmsController   presentation only
///       FP_PlayerArms               the imported asset, Unity conversion kept
///         Animator                  Generic avatar, Root Motion OFF
///
/// CLI: -executeMethod FirstPersonArmsAssetsBuilder.BuildCli -resultPath &lt;file&gt;
///      [-states idle|walk|wrist]
/// </summary>
public static class FirstPersonArmsAssetsBuilder
{
    public const string AnimationsFolder = "Assets/_Project/Animations/FirstPerson";
    public const string ControllerPath = AnimationsFolder + "/FP_ArmsWatch.controller";
    public const string ResourcesFolder = "Assets/_Project/Resources/FirstPerson";
    public const string PrefabPath = ResourcesFolder + "/PF_FirstPersonArms.prefab";

    /// <summary>Cross-fade used between the two locomotion states (seconds).</summary>
    private const float LocomotionBlend = 0.16f;

    /// <summary>Cross-fade into the wrist presentation states (seconds).</summary>
    private const float WristBlend = 0.12f;

    /// <summary>
    /// Framing of the arms under the eye camera.
    ///
    /// The Codex asset is authored around the armature origin = the player's
    /// eye: the HANDOFF measured the presented wrist device at camera-local
    /// (-0.237, -0.190, 0.34) with a 70 degree vertical FOV, which is exactly
    /// what the identity pose reproduces (the ship eye camera already runs at
    /// FOV 70, so the validated composition is preserved without touching the
    /// game camera). The framing therefore stays identity and is exposed here
    /// only so a later visual pass can nudge it deliberately.
    /// </summary>
    private static readonly Vector3 FramingLocalPosition = Vector3.zero;
    private static readonly Vector3 FramingLocalEuler = Vector3.zero;
    private const float FramingScale = 1f;

    private enum Stage
    {
        /// <summary>Idle only (PASS 2: prove the arms render in the interior).</summary>
        Idle,

        /// <summary>Idle + Walk (PASS 3: locomotion presentation).</summary>
        Walk,

        /// <summary>Full graph including the wrist states (PASS 4+).</summary>
        Wrist
    }

    [MenuItem("Tools/SurviveUI/Phase 09A_2/Build First-Person Arms Assets")]
    public static void BuildFromMenu()
    {
        bool ok = Build(Stage.Wrist, out string report);
        Debug.Log(report);
        if (!ok)
        {
            Debug.LogError("[FPArmsAssets] build reported problems");
        }
    }

    public static void BuildCli()
    {
        string resultPath = GetArgValue("-resultPath");
        string states = GetArgValue("-states");
        Stage stage = Stage.Wrist;
        if (!string.IsNullOrEmpty(states))
        {
            if (!Enum.TryParse(states, true, out stage))
            {
                Debug.LogError("[FPArmsAssets] unknown -states value: " + states);
                stage = Stage.Wrist;
            }
        }

        bool ok = Build(stage, out string report);
        if (string.IsNullOrEmpty(resultPath))
        {
            resultPath = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../export/phase09a2_assets_report.txt"));
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
            File.WriteAllText(resultPath, report);
        }
        catch (Exception e)
        {
            Debug.LogError("[FPArmsAssets] cannot write report: " + e.Message);
            ok = false;
        }

        Debug.Log(report);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool Build(Stage stage, out string report)
    {
        var sb = new StringBuilder();
        bool ok = true;

        void Check(bool pass, string label)
        {
            sb.AppendLine((pass ? "PASS" : "FAIL") + " :: " + label);
            ok &= pass;
        }

        sb.AppendLine("=== PHASE 09A_2 FIRST-PERSON ARMS ASSET REPORT ===");
        sb.AppendLine("stage: " + stage);

        string modelPath = FirstPersonArmsImportContract.ModelPath;
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            Check(false, "imported model available at " + modelPath);
            report = sb.ToString();
            return false;
        }

        var clips = new Dictionary<string, AnimationClip>();
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            {
                clips[clip.name] = clip;
            }
        }

        for (int i = 0; i < FirstPersonArmsImportContract.ClipNames.Length; i++)
        {
            string name = FirstPersonArmsImportContract.ClipNames[i];
            Check(clips.ContainsKey(name), "clip " + name + " available for the controller");
        }

        Avatar avatar = null;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (asset is Avatar candidate)
            {
                avatar = candidate;
            }
        }

        Check(avatar != null && avatar.isValid, "generic avatar available for the Animator");

        // ---- Animator Controller ----
        SurviveFirstPersonArmsSetup.EnsureFolder("Assets/_Project/Animations");
        SurviveFirstPersonArmsSetup.EnsureFolder(AnimationsFolder);
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        if (controller == null)
        {
            Check(false, "animator controller created at " + ControllerPath);
            report = sb.ToString();
            return false;
        }

        controller.AddParameter(FirstPersonArmsController.ParameterMoving,
            AnimatorControllerParameterType.Bool);
        controller.AddParameter(FirstPersonArmsController.ParameterWristPhase,
            AnimatorControllerParameterType.Int);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idle = AddState(machine, FirstPersonArmsController.StateIdle, clips, "FP_Idle");
        AnimatorState walk = null;
        if (stage != Stage.Idle)
        {
            walk = AddState(machine, FirstPersonArmsController.StateWalk, clips, "FP_Walk");
        }

        AnimatorState raise = null;
        AnimatorState hold = null;
        AnimatorState lower = null;
        if (stage == Stage.Wrist)
        {
            raise = AddState(machine, FirstPersonArmsController.StateWristRaise, clips, "Wrist_Raise");
            hold = AddState(machine, FirstPersonArmsController.StateWristHold, clips, "Wrist_Idle");
            lower = AddState(machine, FirstPersonArmsController.StateWristLower, clips, "Wrist_Lower");

            // Contract §20 lists InteractRight among the base states, §39 keeps
            // the clip itself optional. The state therefore exists and owns its
            // Codex clip, but no transition drives it in this phase: the right
            // hand gesture is preserved for a later phase instead of being
            // forced onto every confirm press.
            AddState(machine, FirstPersonArmsController.StateInteractRight, clips, "Interact_Right");
        }

        machine.defaultState = idle;

        // ---- locomotion (Idle <-> Walk from the authoritative movement state) ----
        if (walk != null)
        {
            AddTransition(idle, walk, LocomotionBlend, false, 0.9f,
                new AnimatorCondition
                {
                    mode = AnimatorConditionMode.If,
                    parameter = FirstPersonArmsController.ParameterMoving,
                    threshold = 0f
                });
            AddTransition(walk, idle, LocomotionBlend, false, 0.9f,
                new AnimatorCondition
                {
                    mode = AnimatorConditionMode.IfNot,
                    parameter = FirstPersonArmsController.ParameterMoving,
                    threshold = 0f
                });
        }

        // ---- wrist presentation (driven only by the bridge/authority) ----
        if (raise != null)
        {
            AddTransition(idle, raise, WristBlend, false, 0.9f, WristPhaseEquals(1));
            if (walk != null)
            {
                AddTransition(walk, raise, WristBlend, false, 0.9f, WristPhaseEquals(1));
            }

            // The raise plays out fully (exit time) before the hold loop takes
            // over, so the presented pose never snaps.
            AddTransition(raise, hold, WristBlend, true, 0.92f, WristPhaseEquals(2));
            // Closing while the arm is still rising must still bring it down.
            AddTransition(raise, lower, WristBlend, false, 0.9f, WristPhaseEquals(3));
            AddTransition(hold, lower, WristBlend, false, 0.9f, WristPhaseEquals(3));
        }

        if (lower != null)
        {
            var idleConditions = new List<AnimatorCondition>();
            if (walk != null)
            {
                idleConditions.Add(new AnimatorCondition
                {
                    mode = AnimatorConditionMode.IfNot,
                    parameter = FirstPersonArmsController.ParameterMoving,
                    threshold = 0f
                });
            }

            AddTransition(lower, idle, WristBlend, true, 0.9f, idleConditions.ToArray());
            if (walk != null)
            {
                AddTransition(lower, walk, WristBlend, true, 0.9f,
                    new AnimatorCondition
                    {
                        mode = AnimatorConditionMode.If,
                        parameter = FirstPersonArmsController.ParameterMoving,
                        threshold = 0f
                    });
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        sb.AppendLine("controller: " + ControllerPath);
        sb.AppendLine("states: " + string.Join(", ", StateNames(machine)));
        Check(StateNames(machine).Count == (stage == Stage.Wrist ? 6 : stage == Stage.Walk ? 2 : 1),
            "controller built with the expected states for stage " + stage);

        // ---- Prefab ----
        int armsLayer = LayerMask.NameToLayer(FirstPersonArmsController.ArmsLayerName);
        Check(armsLayer > 0, "layer " + FirstPersonArmsController.ArmsLayerName + " resolvable");

        SurviveFirstPersonArmsSetup.EnsureFolder("Assets/_Project/Resources");
        SurviveFirstPersonArmsSetup.EnsureFolder(ResourcesFolder);

        var root = new GameObject(FirstPersonArmsController.RootObjectName);
        try
        {
            root.layer = armsLayer;
            root.transform.localPosition = FramingLocalPosition;
            root.transform.localRotation = Quaternion.Euler(FramingLocalEuler);
            root.transform.localScale = Vector3.one * FramingScale;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.SetParent(root.transform, false);
            SetLayerRecursively(instance, armsLayer);

            var animator = instance.GetComponentInChildren<Animator>(true);
            Check(animator != null, "imported model carries an Animator");
            if (animator != null)
            {
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            var presenter = root.AddComponent<FirstPersonArmsController>();
            var presenterData = new SerializedObject(presenter);
            presenterData.FindProperty("animator").objectReferenceValue = animator;
            presenterData.ApplyModifiedPropertiesWithoutUndo();

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                // The frozen clips move the forearms well outside the imported
                // bind-pose bounds; stale bounds would let Unity cull the raised
                // arm (see FirstPersonArmsController.ApplyRenderFlags).
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                }
            }

            Check(root.GetComponentsInChildren<Collider>(true).Length == 0,
                "prefab carries no collider");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Check(prefab != null, "prefab saved at " + PrefabPath);
        if (prefab != null)
        {
            var prefabAnimator = prefab.GetComponentInChildren<Animator>(true);
            Check(prefabAnimator != null
                  && prefabAnimator.runtimeAnimatorController != null
                  && !prefabAnimator.applyRootMotion
                  && prefabAnimator.avatar != null && prefabAnimator.avatar.isValid,
                "prefab Animator has avatar + controller + Root Motion OFF");
            Check(prefab.GetComponent<FirstPersonArmsController>() != null,
                "prefab root carries FirstPersonArmsController");
            Check(prefab.layer == armsLayer, "prefab root is on the "
                                             + FirstPersonArmsController.ArmsLayerName + " layer");

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            sb.AppendLine("prefabRenderers: " + renderers.Length);
            foreach (Renderer renderer in renderers)
            {
                sb.AppendLine("  " + renderer.name
                              + " layer=" + LayerMask.LayerToName(renderer.gameObject.layer)
                              + " shadows=" + renderer.shadowCastingMode);
            }
        }

        report = sb.ToString();
        return ok;
    }

    private static AnimatorCondition WristPhaseEquals(int phase)
    {
        return new AnimatorCondition
        {
            mode = AnimatorConditionMode.Equals,
            parameter = FirstPersonArmsController.ParameterWristPhase,
            threshold = phase
        };
    }

    private static AnimatorState AddState(AnimatorStateMachine machine, string stateName,
        Dictionary<string, AnimationClip> clips, string clipName)
    {
        AnimatorState state = machine.AddState(stateName);
        AnimationClip clip;
        if (clips.TryGetValue(clipName, out clip))
        {
            state.motion = clip;
            state.speed = 1f;
        }
        else
        {
            Debug.LogError("[FPArmsAssets] missing clip " + clipName + " for state " + stateName);
        }

        return state;
    }

    private static void AddTransition(AnimatorState from, AnimatorState to, float duration,
        bool hasExitTime, float exitTime, params AnimatorCondition[] conditions)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = hasExitTime;
        transition.exitTime = exitTime;
        transition.duration = duration;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        foreach (AnimatorCondition condition in conditions)
        {
            transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
        }
    }

    private static List<string> StateNames(AnimatorStateMachine machine)
    {
        var names = new List<string>();
        foreach (ChildAnimatorState child in machine.states)
        {
            names.Add(child.state.name);
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
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
