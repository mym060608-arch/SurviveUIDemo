using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// First-person arms presentation (Phase 09A_2).
///
/// This component is PRESENTATION ONLY. It is the runtime owner of the Codex
/// first-person arms instance that lives under the authoritative eye camera of
/// the ship player, and it does exactly four things:
///
///   1. frame the imported asset (the prefab's own local transform, authored
///      against the real 1920x1080 screenshot - never a per-bone scale hack),
///   2. push the locomotion presentation (Idle / Walk) from the AUTHORITATIVE
///      movement state of <see cref="SurfacePlayerController"/>, so keyboard and
///      gamepad share one code path (contract §21),
///   3. push the wrist presentation phase handed to it by
///      <see cref="WristPresentationBridge"/> (Raise / Hold / Lower),
///   4. show or hide the arms for the modes that must not be occluded (entry
///      eyelids, console focus, shipboard device panels).
///
/// It is explicitly NOT an authority: it never reads a hardware device, never
/// opens or closes the wrist terminal, never locks player input, never touches
/// the game session and never creates a second player/camera. The existing
/// Wrist / Gameplay systems keep every one of those decisions - this class only
/// consumes their state.
///
/// Physics/render hygiene (contract §14 / §57): the arms carry no collider, so
/// the world interaction sphere cast can never hit them, and they cast no
/// shadows, so they cannot enlarge the known Phase 08 URP shadow atlas cost.
/// </summary>
[DisallowMultipleComponent]
public class FirstPersonArmsController : MonoBehaviour
{
    /// <summary>Resources path of the runtime arms prefab.</summary>
    public const string ResourcePath = "FirstPerson/PF_FirstPersonArms";

    /// <summary>Runtime root object name (never a scene-authored duplicate).</summary>
    public const string RootObjectName = "FirstPersonArmsRoot";

    /// <summary>Layer the arms live on: outside world physics and interaction.</summary>
    public const string ArmsLayerName = "FirstPersonArms";

    // ---- Animator contract (shared with the controller asset builder) ----
    public const string ParameterMoving = "Moving";
    public const string ParameterWristPhase = "WristPhase";

    public const string StateIdle = "Idle";
    public const string StateWalk = "Walk";
    public const string StateWristRaise = "WristRaise";
    public const string StateWristHold = "WristHold";
    public const string StateWristLower = "WristLower";

    /// <summary>Optional right-hand gesture state (contract §20 / §39). The
    /// state and its Codex clip are wired into the controller, but nothing
    /// drives it in this phase - the confirm action must never be delayed.</summary>
    public const string StateInteractRight = "InteractRight";

    /// <summary>Wrist presentation phase pushed into the Animator.</summary>
    public enum WristPhase
    {
        /// <summary>Terminal closed: ordinary Idle / Walk presentation.</summary>
        Rest = 0,

        /// <summary>Left wrist rising towards the terminal (Wrist_Raise).</summary>
        Raise = 1,

        /// <summary>Terminal presented and held (Wrist_Idle loop).</summary>
        Hold = 2,

        /// <summary>Left wrist returning to rest (Wrist_Lower).</summary>
        Lower = 3
    }

    /// <summary>Material slot whose cyan screens glow while the wrist is open.</summary>
    public const string EmissiveMaterialName = "MAT_DeviceEmissive";

    private static readonly int MovingHash = Animator.StringToHash(ParameterMoving);
    private static readonly int WristPhaseHash = Animator.StringToHash(ParameterWristPhase);

    [SerializeField]
    private Animator animator;

    /// <summary>
    /// Camera-relative pitch applied to the arm mount while the arms are in
    /// their ordinary first-person presentation (Idle / Walk).
    ///
    /// Measured from the frozen asset: in FP_Idle / FP_Walk the Codex poses sit
    /// BELOW the level-gaze frustum (the glove centre is at camera-local
    /// y -0.650 m / z 0.265 m, which needs a 34.7 degree downward look to enter
    /// the frame), while the raised Wrist_Idle pose is authored exactly for the
    /// level gaze (device at y -0.211 / z 0.314) and against the existing wrist
    /// UI. The HANDOFF validation renders match this: fp_idle / fp_walk were
    /// reviewed at a 40 / 52 degree look-down, the wrist renders at 0.
    ///
    /// A rigid offset can never satisfy both (the two poses are 0.44 m apart
    /// vertically while the frame is only ~0.2-0.4 m tall at those depths), so
    /// the mount is framed per presentation state: this pitch reproduces the
    /// Codex-validated idle composition at a level gaze, and the wrist states
    /// use the asset's own validated framing. See WristPresentationBridge for
    /// the transition.
    /// </summary>
    public const float DefaultRestPitchDegrees = -40f;

    [SerializeField]
    private float restPitchDegrees = DefaultRestPitchDegrees;

    private float _framingBlend = 1f; // 1 = asset framing, 0 = rest pose framing

    /// <summary>Planar speed above which the authoritative player counts as
    /// moving (metres/second).</summary>
    [SerializeField]
    private float moveSpeedThreshold = 0.15f;

    /// <summary>
    /// Above this speed the change can only be a teleport / deterministic
    /// placement (automation, spawn, airlock), never walking - it must not flash
    /// the Walk presentation.
    /// </summary>
    [SerializeField]
    private float teleportSpeedThreshold = 20f;

    private SurfacePlayerController _player;
    private Vector3 _lastPlayerPosition;
    private bool _hasLastPlayerPosition;
    private Renderer[] _renderers = new Renderer[0];
    private Renderer[] _emissiveRenderers = new Renderer[0];
    private MaterialPropertyBlock _propertyBlock;

    private bool _moving;
    private WristPhase _phase = WristPhase.Rest;
    private bool _visible = true;
    private bool _frozen;
    private bool _screenActive;
    private bool _configured;

    // Dim / active cyan emission of the four wrist screens and the projector
    // aperture (contract §55: restrained, never a bloom source over the UI).
    private static readonly Color ScreenEmissionIdle = new Color(0.10f, 0.42f, 0.50f);
    private static readonly Color ScreenEmissionActive = new Color(0.24f, 0.86f, 0.98f);

    public Animator Animator => animator;

    /// <summary>Live locomotion presentation state.</summary>
    public bool IsMoving => _moving;

    /// <summary>Live wrist presentation phase.</summary>
    public WristPhase CurrentWristPhase => _phase;

    /// <summary>True while the arms renderers are shown.</summary>
    public bool IsVisible => _visible;

    /// <summary>True while the presentation is frozen by the pause menu.</summary>
    public bool IsFrozen => _frozen;

    /// <summary>True while the wrist screens glow at their active level.</summary>
    public bool IsWristScreenActive => _screenActive;

    /// <summary>Camera-relative pitch the rest presentation uses (degrees).</summary>
    public float RestPitchDegrees => restPitchDegrees;

    /// <summary>
    /// 0 = the rest-pose framing (arms presented inside the lower frame),
    /// 1 = the asset's own framing (used by the wrist presentation).
    /// </summary>
    public float FramingBlend => _framingBlend;

    /// <summary>
    /// Presentation framing blend, driven by the wrist bridge. It never changes
    /// the animation, the rig or the asset: it only rotates the arm mount around
    /// the camera origin so the frozen poses land where the player can see them.
    /// </summary>
    public void SetFramingBlend(float blend01)
    {
        float blend = Mathf.Clamp01(blend01);
        if (Mathf.Abs(blend - _framingBlend) < 0.0005f)
        {
            return;
        }

        _framingBlend = blend;
        ApplyFraming();
    }

    private void ApplyFraming()
    {
        float pitch = Mathf.Lerp(restPitchDegrees, 0f, _framingBlend);
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    /// <summary>Renderers the arms instance actually renders with.</summary>
    public int RendererCount => _renderers.Length;

    /// <summary>
    /// Short name of the animator state currently playing on the base layer
    /// (test/diagnostic read-out; "<none>" before the animator is ready).
    /// </summary>
    public string CurrentStateName
    {
        get
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                return "<none>";
            }

            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName(StateWristRaise))
            {
                return StateWristRaise;
            }

            if (info.IsName(StateWristHold))
            {
                return StateWristHold;
            }

            if (info.IsName(StateWristLower))
            {
                return StateWristLower;
            }

            if (info.IsName(StateWalk))
            {
                return StateWalk;
            }

            return info.IsName(StateIdle) ? StateIdle : "<other>";
        }
    }

    /// <summary>
    /// Normalized progress (0..1) of the WristRaise state, or -1 when that
    /// state is not the current one. Used by the bridge to time the existing
    /// wrist UI reveal against the real animation.
    /// </summary>
    public float WristRaiseProgress
    {
        get
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                return -1f;
            }

            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName(StateWristRaise))
            {
                return -1f;
            }

            return Mathf.Clamp01(info.normalizedTime);
        }
    }

    /// <summary>
    /// Creates the single runtime arms instance under the authoritative eye
    /// camera. Returns the existing instance when one is already mounted (the
    /// "Continue Game" round trip can therefore never stack a second copy).
    /// </summary>
    public static FirstPersonArmsController Ensure(Camera eyeCamera, bool verbose = true)
    {
        if (eyeCamera == null)
        {
            Debug.LogError("[FPArms] no eye camera: first-person arms not created");
            return null;
        }

        FirstPersonArmsController existing =
            eyeCamera.GetComponentInChildren<FirstPersonArmsController>(true);
        if (existing != null)
        {
            return existing;
        }

        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogError("[FPArms] missing Resources/" + ResourcePath + ".prefab");
            return null;
        }

        GameObject instance = Instantiate(prefab, eyeCamera.transform, false);
        instance.name = RootObjectName;

        var controller = instance.GetComponent<FirstPersonArmsController>();
        if (controller == null)
        {
            controller = instance.AddComponent<FirstPersonArmsController>();
        }

        if (verbose)
        {
            Debug.Log("[FPArms] first-person arms mounted under " + eyeCamera.name
                      + " layer=" + LayerMask.LayerToName(instance.layer)
                      + " renderers=" + controller.RendererCount);
        }

        return controller;
    }

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        CollectRenderers();
        ApplyRenderFlags();
        ApplyFraming();
    }

    /// <summary>Binds the authoritative movement source (called by the ship
    /// coordinator; no per-frame Find calls anywhere in this class).</summary>
    public void Configure(SurfacePlayerController player)
    {
        _player = player;
        _configured = player != null;
        if (!_configured)
        {
            Debug.LogError("[FPArms] configured without a player: locomotion "
                           + "presentation stays on Idle");
        }
    }

    private void CollectRenderers()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        var emissive = new List<Renderer>();
        foreach (Renderer renderer in _renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null
                    && materials[i].name == EmissiveMaterialName)
                {
                    emissive.Add(renderer);
                    break;
                }
            }
        }

        _emissiveRenderers = emissive.ToArray();
    }

    /// <summary>
    /// The arms are a pure presentation surface: no shadow casting (Phase 08
    /// already carries a URP shadow atlas warning) and no collider is ever
    /// added, so world rays and physics ignore them completely.
    ///
    /// The skinned renderers also get <c>updateWhenOffscreen</c>: the frozen
    /// clips swing the forearms far outside the imported bind-pose bounds (the
    /// wrist raise moves the left arm about 0.45 m), and with the default stale
    /// bounds Unity frustum-culls the raised arm - the pose is correct but
    /// nothing is drawn. The mesh is 4.5k triangles, so the accurate bounds cost
    /// nothing measurable.
    /// </summary>
    private void ApplyRenderFlags()
    {
        foreach (Renderer renderer in _renderers)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.updateWhenOffscreen = true;
            }
        }
    }

    /// <summary>Presentation phase handed in by the wrist bridge.</summary>
    public void SetWristPhase(WristPhase phase)
    {
        if (_phase == phase)
        {
            return;
        }

        _phase = phase;
        PushAnimatorParameters();
    }

    /// <summary>Freezes (pause) or releases the presentation.</summary>
    public void SetFrozen(bool frozen)
    {
        _frozen = frozen;
    }

    /// <summary>Shows or hides every arms renderer (console focus / device
    /// panels / entry eyelids). The animator keeps its state, so restoring is a
    /// continuation - never a reset to Idle.</summary>
    public void SetVisible(bool visible)
    {
        if (_visible == visible && _renderers.Length > 0
            && _renderers[0].enabled == visible)
        {
            return;
        }

        _visible = visible;
        foreach (Renderer renderer in _renderers)
        {
            renderer.enabled = visible;
        }
    }

    /// <summary>
    /// Drives the cyan wrist screens: dim at rest, brighter while the existing
    /// wrist terminal is presented. Uses a MaterialPropertyBlock so the shared
    /// project materials are never instantiated (contract §66).
    /// </summary>
    public void SetWristScreenActive(bool active)
    {
        if (_screenActive == active)
        {
            return;
        }

        _screenActive = active;
        if (_emissiveRenderers.Length == 0)
        {
            return;
        }

        if (_propertyBlock == null)
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        Color emission = active ? ScreenEmissionActive : ScreenEmissionIdle;
        foreach (Renderer renderer in _emissiveRenderers)
        {
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor("_EmissionColor", emission);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private void Update()
    {
        // Pause freezes the presentation: the Animator already stands still at
        // Time.timeScale 0, and pushing parameters while paused would let a
        // held input change the pose behind the menu (contract §23 / §44).
        if (_frozen)
        {
            return;
        }

        bool moving = ResolveMoving();
        if (moving != _moving)
        {
            _moving = moving;
            PushAnimatorParameters();
        }
    }

    /// <summary>
    /// Authoritative locomotion read-out: the real per-frame displacement of the
    /// player rig while exploration input is enabled. Reading the rig's actual
    /// motion (instead of CharacterController.velocity, which any writer of the
    /// same frame can legitimately reset) keeps keyboard, gamepad and automation
    /// on one identical code path, and no raw device is ever read.
    /// </summary>
    private bool ResolveMoving()
    {
        if (_player == null)
        {
            return false;
        }

        Vector3 position = _player.transform.position;
        if (!_hasLastPlayerPosition)
        {
            _lastPlayerPosition = position;
            _hasLastPlayerPosition = true;
            return false;
        }

        Vector3 delta = position - _lastPlayerPosition;
        _lastPlayerPosition = position;
        delta.y = 0f;

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0.0001f)
        {
            return false;
        }

        float speed = delta.magnitude / deltaTime;
        if (speed > teleportSpeedThreshold || !_player.InputEnabled)
        {
            return false;
        }

        return speed > moveSpeedThreshold;
    }

    private void PushAnimatorParameters()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        animator.SetBool(MovingHash, _moving);
        animator.SetInteger(WristPhaseHash, (int)_phase);
    }
}
