using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase 09A_3: block / voxel style first-person arms — the FINAL runtime
/// presentation for the first-person view.
///
/// The whole object is built in code from Unity primitives (no FBX, no avatar,
/// no Animator, no skinned mesh, no colliders) and animated purely by transform
/// interpolation, so there is nothing that can fail to import, bind or bake.
///
/// Presentation model (this is what makes the composition correct):
///
///   * the arms hang in BODY SPACE — the root is a child of the PLAYER at eye
///     height, so it follows position and yaw but NOT the camera pitch.  At a
///     level gaze the hands are far below the frustum and therefore invisible;
///     when the player looks down the camera pitches into them and both block
///     arms enter from the lower left / lower right, exactly like real arms
///     hanging at the body sides.
///   * while the wrist terminal is being presented the root BLENDS into camera
///     space (pitch follow), because the existing Wrist UI is camera-locked:
///     that keeps the device framed against the hologram whatever pitch the
///     player had when they opened it.  Look is frozen by the existing authority
///     while the wrist is open, so this blend is stable.
///
/// Authority separation: this class owns NO input, NO wrist open/close state,
/// NO pause and NO scene logic.  It consumes the presentation phase pushed by
/// <see cref="WristPresentationBridge"/> (which itself only mirrors
/// <see cref="ShipGameController"/>) and the authoritative movement read from
/// <see cref="SurfacePlayerController"/>.
/// </summary>
[DisallowMultipleComponent]
public class FirstPersonBlockArmsController : MonoBehaviour
{
    /// <summary>Runtime root object name (never authored into a scene).</summary>
    public const string RootObjectName = "FPBlockArmsRoot";

    /// <summary>Layer the arms live on: outside world physics and interaction.</summary>
    public const string ArmsLayerName = "FirstPersonArms";

    /// <summary>Presentation phase pushed in by the wrist bridge.</summary>
    public enum WristPhase
    {
        Rest = 0,
        Raise = 1,
        Hold = 2,
        Lower = 3
    }

    // ---- lifecycle ----

    /// <summary>
    /// Creates the single runtime instance under the authorititative player rig.
    /// Returns the existing instance when one is already mounted, so a
    /// "Continue Game" round trip can never stack a second copy.
    /// </summary>
    public static FirstPersonBlockArmsController Ensure(SurfacePlayerController player,
        bool verbose = true)
    {
        if (player == null)
        {
            Debug.LogError("[BlockArms] no player: first-person block arms not created");
            return null;
        }

        FirstPersonBlockArmsController existing =
            player.GetComponentInChildren<FirstPersonBlockArmsController>(true);
        if (existing != null)
        {
            return existing;
        }

        var root = new GameObject(RootObjectName);
        root.transform.SetParent(player.transform, false);
        root.transform.localPosition = new Vector3(0f, player.eyeHeight, 0f);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        var controller = root.AddComponent<FirstPersonBlockArmsController>();
        controller.Build(player);

        if (verbose)
        {
            Debug.Log("[BlockArms] first-person block arms mounted under " + player.name
                      + " layer=" + LayerMask.LayerToName(root.layer)
                      + " renderers=" + controller.RendererCount);
        }

        return controller;
    }

    // ---- design constants (metres, eye space) ----

    private const float ShoulderSide = 0.300f;
    private const float ShoulderDrop = 0.200f;
    private const float ShoulderForward = 0.020f;

    private const float ForearmLength = 0.340f;
    private const float ForearmWidth = 0.094f;
    private const float CuffLength = 0.034f;
    private const float CuffWidth = 0.110f;
    private const float HandLength = 0.130f;
    private const float HandWidth = 0.114f;

    private const float RaiseSeconds = 0.800f;
    private const float LowerSeconds = 0.667f;

    /// <summary>Idle walk sway amplitude and rate (deliberately small).</summary>
    private const float WalkSwayDegrees = 5.0f;
    private const float WalkRate = 2.6f;

    /// <summary>Weakening applied to the physical arms while the hologram is up.</summary>
    private const float SoftenedBaseScale = 0.62f;
    private const float SoftenedEmissionScale = 0.5f;

    private static readonly Color SuitColour = new Color(0.700f, 0.720f, 0.735f);
    private static readonly Color GloveColour = new Color(0.105f, 0.113f, 0.125f);
    private static readonly Color CuffColour = new Color(0.235f, 0.250f, 0.270f);
    private static readonly Color AccentColour = new Color(0.720f, 0.320f, 0.100f);
    private static readonly Color DeviceColour = new Color(0.520f, 0.545f, 0.575f);
    private static readonly Color ScreenEmissionIdle = new Color(0.10f, 0.42f, 0.50f);
    private static readonly Color ScreenEmissionActive = new Color(0.24f, 0.86f, 0.98f);

    // ---- runtime state ----

    private SurfacePlayerController _player;
    private Transform _armsRoot;
    private Transform _leftArm;
    private Transform _rightArm;
    private Transform _watch;
    private Renderer[] _renderers = new Renderer[0];
    private Renderer _screenRenderer;
    private readonly List<Renderer> _softenedRenderers = new List<Renderer>();

    private Material _suit;
    private Material _glove;
    private Material _cuff;
    private Material _accent;
    private Material _device;
    private Material _screen;

    private Quaternion _leftRest;
    private Quaternion _leftHold;
    private Quaternion _rightRest;
    private Vector3 _leftRestPosition;
    private Vector3 _leftHoldPosition;
    private Vector3 _rightRestPosition;

    private WristPhase _phase = WristPhase.Rest;
    private float _raiseProgress;
    private bool _visible = true;
    private bool _frozen;
    private bool _moving;
    private bool _screenActive;
    private bool _softened;
    private float _walkPhase;
    private Vector3 _lastPlayerPosition;
    private bool _hasLastPosition;
    private float _teleportSpeed = 20f;
    private float _moveSpeedThreshold = 0.15f;
    private MaterialPropertyBlock _block;

    // ---- public read-outs (probes / tests) ----

    public Transform ArmsRoot => _armsRoot;
    public Transform LeftArmRoot => _leftArm;
    public Transform RightArmRoot => _rightArm;
    public Transform Watch => _watch;
    public int RendererCount => _renderers.Length;
    public bool IsVisible => _visible;
    public bool IsFrozen => _frozen;
    public bool IsMoving => _moving;
    public bool IsWristScreenActive => _screenActive;
    public bool IsSoftened => _softened;
    public WristPhase CurrentWristPhase => _phase;
    public float WristPresentationBlend => _raiseProgress;

    /// <summary>Short state name for diagnostics and tests.</summary>
    public string CurrentStateName
    {
        get
        {
            switch (_phase)
            {
                case WristPhase.Raise:
                    return "WristRaise";
                case WristPhase.Hold:
                    return "WristHold";
                case WristPhase.Lower:
                    return "WristLower";
                default:
                    return _moving ? "Walk" : "Rest";
            }
        }
    }

    /// <summary>Raise progress 0..1 while raising, -1 when not raising.</summary>
    public float WristRaiseProgress =>
        _phase == WristPhase.Raise ? Mathf.Clamp01(_raiseProgress) : -1f;

    /// <summary>True when the watch is inside the viewport (measured).</summary>
    public bool WatchOnScreen
    {
        get
        {
            Camera eye = _player != null ? _player.EyeCamera : Camera.main;
            if (eye == null || _watch == null)
            {
                return false;
            }

            Vector3 viewport = eye.WorldToViewportPoint(_watch.position);
            return viewport.z > 0f
                   && viewport.x > 0f && viewport.x < 1f
                   && viewport.y > 0f && viewport.y < 1f;
        }
    }

    /// <summary>Watch viewport position (0,0,0 when not projectable).</summary>
    public Vector3 WatchViewport
    {
        get
        {
            Camera eye = _player != null ? _player.EyeCamera : Camera.main;
            return eye != null && _watch != null
                ? eye.WorldToViewportPoint(_watch.position)
                : Vector3.zero;
        }
    }

    // ====================================================================
    // Build
    // ====================================================================

    private void Build(SurfacePlayerController player)
    {
        _player = player;
        int layer = LayerMask.NameToLayer(ArmsLayerName);
        if (layer < 0)
        {
            Debug.LogError("[BlockArms] layer " + ArmsLayerName + " is missing");
            layer = 0;
        }

        gameObject.layer = layer;
        BuildMaterials();
        _armsRoot = transform;

        // --- rest poses: hanging at the body sides, tilted ~30 degrees forward
        //     so a look-down brings both arms into the lower corners.
        Vector3 restDirLeft = new Vector3(-0.060f, -0.871f, 0.489f).normalized;
        Vector3 restDirRight = new Vector3(0.060f, -0.871f, 0.489f).normalized;
        Vector3 restFacingLeft = new Vector3(-0.32f, 0.10f, 0.94f).normalized;
        Vector3 restFacingRight = new Vector3(0.32f, 0.10f, 0.94f).normalized;
        _leftRest = ArmRotation(restDirLeft, restFacingLeft);
        _rightRest = ArmRotation(restDirRight, restFacingRight);

        // --- hold pose: the left forearm reaches forward AND across to the left
        //     so the watch is presented in the lower-left of a LEVEL-gaze frame.
        //     The across-the-body angle is what keeps the composition readable:
        //     a forearm pushed straight forward projects as a wide diagonal that
        //     climbs into the hologram's panel ring, while a forearm angled away
        //     from the view axis stays inside the lower-left corner underneath
        //     the wrist terminal. The pivot only moves forward, never outward,
        //     so the shoulder motion stays physically plausible.
        Vector3 shoulderLeft = new Vector3(-ShoulderSide, -ShoulderDrop, ShoulderForward);
        Vector3 holdShoulder = new Vector3(-0.260f, -0.219f, 0.173f);
        Vector3 holdDir = new Vector3(-0.583f, -0.086f, 0.808f).normalized;
        Vector3 holdFacing = new Vector3(0.30f, 0.86f, -0.42f).normalized;
        _leftHold = ArmRotation(holdDir, holdFacing);

        _leftArm = BuildArm("LeftArmRoot", shoulderLeft, layer, true);
        _rightArm = BuildArm("RightArmRoot",
            new Vector3(ShoulderSide, -ShoulderDrop, ShoulderForward), layer, false);
        _leftRestPosition = shoulderLeft;
        _leftHoldPosition = holdShoulder;
        _rightRestPosition = _rightArm.localPosition;

        _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in _renderers)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        _leftArm.localRotation = _leftRest;
        _rightArm.localRotation = _rightRest;
        ApplyMotion();
        SetVisible(true);
        SetWristScreenActive(false);
    }

    private void BuildMaterials()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogError("[BlockArms] URP Lit shader not found - arms stay unlit grey");
            lit = Shader.Find("Universal Render Pipeline/Unlit");
        }

        _suit = MakeMaterial(lit, "FPBlockArms_Suit", SuitColour, 0.05f, 0.42f);
        _glove = MakeMaterial(lit, "FPBlockArms_Glove", GloveColour, 0.06f, 0.34f);
        _cuff = MakeMaterial(lit, "FPBlockArms_Cuff", CuffColour, 0.15f, 0.40f);
        _accent = MakeMaterial(lit, "FPBlockArms_Accent", AccentColour, 0.10f, 0.42f);
        _device = MakeMaterial(lit, "FPBlockArms_Device", DeviceColour, 0.18f, 0.46f);
        _screen = MakeMaterial(lit, "FPBlockArms_Screen",
            new Color(0.035f, 0.100f, 0.115f), 0.0f, 0.55f);
        if (_screen != null && _screen.HasProperty("_EmissionColor"))
        {
            _screen.EnableKeyword("_EMISSION");
            _screen.SetColor("_EmissionColor", ScreenEmissionIdle);
            _screen.globalIlluminationFlags =
                MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
    }

    /// <summary>
    /// Rotation that points the arm's local -Y (the axis the blocks are built
    /// along) EXACTLY along <paramref name="armDirection"/> while keeping the
    /// watch face (local +Z) as close as possible to <paramref name="facing"/>.
    ///
    /// Quaternion.LookRotation orthogonalises its inputs, so it cannot be used
    /// with two independently chosen vectors: the arm would end up pointing
    /// somewhere else entirely. The facing is therefore projected perpendicular
    /// to the arm direction first, which makes the result exact.
    /// </summary>
    private static Quaternion ArmRotation(Vector3 armDirection, Vector3 facing)
    {
        Vector3 direction = armDirection.normalized;
        Vector3 forward = facing - direction * Vector3.Dot(facing, direction);
        if (forward.sqrMagnitude < 1.0e-6f)
        {
            forward = Vector3.Cross(direction, Vector3.right);
        }

        return Quaternion.LookRotation(forward.normalized, -direction);
    }

    private static Material MakeMaterial(Shader shader, string name, Color colour,
        float metallic, float smoothness)
    {
        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", colour);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }

        return material;
    }

    /// <summary>
    /// One arm: pivot + forearm + cuff + glove block (+ a restrained thumb block)
    /// and, for the left arm, the compact wrist terminal.  Eight primitives per
    /// arm, every collider removed: the arms are pure presentation geometry.
    /// </summary>
    private Transform BuildArm(string name, Vector3 shoulder, int layer, bool withWatch)
    {
        var pivot = new GameObject(name);
        pivot.transform.SetParent(_armsRoot, false);
        pivot.transform.localPosition = shoulder;
        pivot.transform.localRotation = Quaternion.identity;
        pivot.gameObject.layer = layer;

        float forearmCentre = -(ForearmLength * 0.5f);
        Box("Forearm_" + (withWatch ? "L" : "R"), pivot.transform,
            new Vector3(0f, forearmCentre, 0f),
            new Vector3(ForearmWidth, ForearmLength, ForearmWidth), _suit, layer);

        float cuffCentre = -(ForearmLength + CuffLength * 0.5f);
        Box("Cuff_" + (withWatch ? "L" : "R"), pivot.transform,
            new Vector3(0f, cuffCentre, 0f),
            new Vector3(CuffWidth, CuffLength, CuffWidth), _cuff, layer);

        float handCentre = -(ForearmLength + CuffLength + HandLength * 0.5f);
        Transform hand = Box("Hand_" + (withWatch ? "L" : "R"), pivot.transform,
            new Vector3(0f, handCentre, 0f),
            new Vector3(HandWidth, HandLength, HandWidth), _glove, layer);

        // Restrained thumb block: enough to read a hand, no fingers.
        float side = withWatch ? 1f : -1f;
        Box("Thumb_" + (withWatch ? "L" : "R"), hand,
            new Vector3(side * (HandWidth * 0.5f + 0.016f), 0.045f, 0.012f),
            new Vector3(0.030f, 0.070f, 0.052f), _glove, layer);

        if (!withWatch)
        {
            return pivot.transform;
        }

        // ---- compact wrist terminal: base + screen + two keys ----
        var device = new GameObject("WristDevice_L");
        device.transform.SetParent(pivot.transform, false);
        device.transform.localPosition = new Vector3(0f, -(ForearmLength - 0.040f), 0f);
        device.transform.localRotation = Quaternion.identity;
        device.gameObject.layer = layer;

        Box("WatchBase", device.transform, new Vector3(0f, 0f, 0.072f),
            new Vector3(0.076f, 0.098f, 0.026f), _device, layer);
        _screenRenderer = Box("WatchScreen", device.transform,
            new Vector3(0f, 0f, 0.089f),
            new Vector3(0.056f, 0.076f, 0.010f), _screen, layer).GetComponent<Renderer>();
        Box("WatchKey_L", device.transform, new Vector3(-0.048f, 0f, 0.074f),
            new Vector3(0.014f, 0.030f, 0.020f), _accent, layer);
        Box("WatchKey_R", device.transform, new Vector3(0.048f, 0f, 0.074f),
            new Vector3(0.014f, 0.030f, 0.020f), _accent, layer);

        _watch = device.transform;
        return pivot.transform;
    }

    private static Transform Box(string name, Transform parent, Vector3 localPosition,
        Vector3 size, Material material, int layer)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
        {
            DestroyImmediate(collider);
        }

        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = size;
        go.layer = layer;
        var renderer = go.GetComponent<Renderer>();
        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }

        return go.transform;
    }

    // ====================================================================
    // Presentation API (called by WristPresentationBridge only)
    // ====================================================================

    public void Configure(SurfacePlayerController player)
    {
        _player = player;
    }

    public void SetWristPhase(WristPhase phase)
    {
        _phase = phase;
    }

    public void SetFrozen(bool frozen)
    {
        _frozen = frozen;
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;
        foreach (Renderer renderer in _renderers)
        {
            if (renderer != null && renderer != _screenRenderer)
            {
                renderer.enabled = visible;
            }
        }

        if (_screenRenderer != null)
        {
            _screenRenderer.enabled = visible && _screenActive;
        }
    }

    /// <summary>Watch screen brightness: dim at rest, brighter while presented.</summary>
    public void SetWristScreenActive(bool active)
    {
        if (_screenActive == active)
        {
            return;
        }

        _screenActive = active;
        if (_screenRenderer == null)
        {
            return;
        }

        _screenRenderer.enabled = _visible && active;
        ApplyScreenEmission();
    }

    /// <summary>
    /// Softening used while the hologram is up: the physical arm is only the
    /// support for the UI, so it is dimmed and desaturated a little instead of
    /// competing with the sharp UI layer above it.
    /// </summary>
    public void SetSoftened(bool softened)
    {
        if (_softened == softened)
        {
            return;
        }

        _softened = softened;
        ApplySoftening();
    }

    private void ApplyScreenEmission()
    {
        if (_screenRenderer == null)
        {
            return;
        }

        if (_block == null)
        {
            _block = new MaterialPropertyBlock();
        }

        float scale = _softened ? SoftenedEmissionScale : 1f;
        _block.Clear();
        _block.SetColor("_EmissionColor",
            (_screenActive ? ScreenEmissionActive : ScreenEmissionIdle) * scale);
        _screenRenderer.SetPropertyBlock(_block);
    }

    private void ApplySoftening()
    {
        if (_block == null)
        {
            _block = new MaterialPropertyBlock();
        }

        foreach (Renderer renderer in _renderers)
        {
            if (renderer == null || renderer == _screenRenderer)
            {
                continue;
            }

            Color baseColour = BaseColourOf(renderer);
            renderer.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor",
                _softened ? baseColour * SoftenedBaseScale : baseColour);
            renderer.SetPropertyBlock(_block);
        }

        ApplyScreenEmission();
    }

    private Color BaseColourOf(Renderer renderer)
    {
        Material material = renderer != null ? renderer.sharedMaterial : null;
        if (material == null)
        {
            return Color.grey;
        }

        if (material == _suit)
        {
            return SuitColour;
        }

        if (material == _glove)
        {
            return GloveColour;
        }

        if (material == _cuff)
        {
            return CuffColour;
        }

        if (material == _accent)
        {
            return AccentColour;
        }

        if (material == _device)
        {
            return DeviceColour;
        }

        return material.HasProperty("_BaseColor")
            ? material.GetColor("_BaseColor")
            : Color.grey;
    }

    // ====================================================================
    // Per-frame presentation
    // ====================================================================

    private void Update()
    {
        if (_frozen)
        {
            return;
        }

        UpdateLocomotion();
        AdvancePhase();
        ApplyMotion();
    }

    /// <summary>
    /// Authoritative movement read-out: the real per-frame displacement of the
    /// player rig while exploration input is enabled (never a raw device).
    /// </summary>
    private void UpdateLocomotion()
    {
        if (_player == null)
        {
            _moving = false;
            return;
        }

        Vector3 position = _player.transform.position;
        if (!_hasLastPosition)
        {
            _lastPlayerPosition = position;
            _hasLastPosition = true;
            _moving = false;
            return;
        }

        Vector3 delta = position - _lastPlayerPosition;
        _lastPlayerPosition = position;
        delta.y = 0f;
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0.0001f)
        {
            return;
        }

        float speed = delta.magnitude / deltaTime;
        if (speed > _teleportSpeed || !_player.InputEnabled)
        {
            _moving = false;
            return;
        }

        _moving = speed > _moveSpeedThreshold;
        if (_moving)
        {
            _walkPhase += deltaTime * WalkRate;
            if (_walkPhase > Mathf.PI * 2f)
            {
                _walkPhase -= Mathf.PI * 2f;
            }
        }
    }

    /// <summary>Raise / hold / lower are pure transform blends (no hard cuts).</summary>
    private void AdvancePhase()
    {
        float step = Time.deltaTime;
        switch (_phase)
        {
            case WristPhase.Raise:
                _raiseProgress = Mathf.MoveTowards(_raiseProgress, 1f,
                    RaiseSeconds > 0f ? step / RaiseSeconds : 1f);
                break;
            case WristPhase.Hold:
                _raiseProgress = 1f;
                break;
            case WristPhase.Lower:
                _raiseProgress = Mathf.MoveTowards(_raiseProgress, 0f,
                    LowerSeconds > 0f ? step / LowerSeconds : 1f);
                break;
            default:
                _raiseProgress = Mathf.MoveTowards(_raiseProgress, 0f,
                    LowerSeconds > 0f ? step / LowerSeconds : 1f);
                break;
        }
    }

    private void ApplyMotion()
    {
        if (_armsRoot == null || _leftArm == null || _rightArm == null)
        {
            return;
        }

        // Body space at rest, camera space while the wrist terminal is presented.
        float presentBlend = Mathf.Clamp01(_raiseProgress);
        float pitch = _player != null ? _player.PitchDegrees : 0f;
        _armsRoot.localRotation = Quaternion.Euler(pitch * presentBlend, 0f, 0f);

        // Eased presentation weight so the raise settles instead of stopping dead.
        float eased = presentBlend * presentBlend * (3f - 2f * presentBlend);

        float sway = _moving ? WalkSwayDegrees * Mathf.Sin(_walkPhase) : 0f;
        float micro = presentBlend > 0.01f
            ? 0.8f * Mathf.Sin(Time.time * 1.7f) * presentBlend
            : 0f;

        _leftArm.localRotation = Quaternion.Slerp(_leftRest, _leftHold, eased)
                                 * Quaternion.Euler(-sway + micro, 0f, 0f);
        _leftArm.localPosition = Vector3.Lerp(_leftRestPosition, _leftHoldPosition, eased);
        _rightArm.localRotation = _rightRest
                                  * Quaternion.Euler(sway, 0f, 0f);
        _rightArm.localPosition = _rightRestPosition;
    }
}
