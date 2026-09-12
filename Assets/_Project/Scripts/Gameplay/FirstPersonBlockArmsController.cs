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
///   * while the wrist terminal is being presented ONLY the left arm (the
///     presented one) follows the camera pitch, through its own presentation
///     root. The shared root is never rotated, so the rest / look-down contract
///     stays pure body space. Look is frozen by the existing authority while the
///     wrist is open, so the compensation keeps the device framed against the
///     camera-locked hologram whatever pitch the player opened it from.
///   * the wrist hold pose lays the forearm ACROSS the body (screen-horizontal)
///     with the watch face turned to the camera, and the whole arm is softened
///     into a translucent ghost once the hologram is up, so the UI - not the
///     arm - owns the frame.
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

    // ---- wrist focus (Stage B) softening ----
    //
    // While the hologram is up the physical arm is only the SUPPORT for the UI,
    // so it has to lose the visual competition completely - not by 5%: it drops
    // to a translucent, desaturated, emissive-free ghost. See ApplySoftening for
    // why this is a material swap instead of a real blur (the UI is a world quad
    // rendered by the same camera, so any post-process blur would blur the UI
    // with it, and blurring only the arms would need a second render path).
    /// <summary>Opacity of the physical arm while the hologram owns the frame.</summary>
    private const float SoftenedAlpha = 0.38f;

    /// <summary>How far the softened arm is pushed toward the background grey.</summary>
    private const float SoftenedDesaturation = 0.70f;

    /// <summary>Brightness scale of the softened arm's albedo.</summary>
    private const float SoftenedBaseScale = 0.52f;

    /// <summary>Emission scale of the (physical) watch screen while softened.</summary>
    private const float SoftenedEmissionScale = 0.18f;

    /// <summary>
    /// Soft halo copy of the presented arm: scale + opacity of the bleed.</summary>
    private const float HaloScale = 1.075f;
    private const float HaloAlpha = 0.11f;

    /// <summary>
    /// Render queue of the softened arm.
    ///
    /// The ghost material is alpha blended, so it moves into the transparent
    /// range - where the wrist hologram also lives. The hologram MUST still be
    /// drawn over the arm (it is the interface; the arm is its support), and the
    /// arm is physically CLOSER to the camera, so distance sorting alone would
    /// put the arm in front. An explicit queue just below the hologram's
    /// (Transparent = 3000, see Survive/WristDisplayUnlit) settles it: the arm
    /// blends first, the UI is composited on top of it, and the UI's readable
    /// content is never covered.
    /// </summary>
    private const int SoftenedRenderQueue = 2900;

    /// <summary>
    /// Queue the softened (ghost) arm renders in. Exposed so the structural
    /// validator can pin that it stays BELOW the wrist hologram's queue: the UI
    /// must always composite over the physical arm that supports it.
    /// </summary>
    public static int SoftenedArmRenderQueue => SoftenedRenderQueue;

    private static readonly Color BackgroundGrey = new Color(0.30f, 0.33f, 0.37f);

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
    private Transform _leftHalo;
    private Transform _watch;
    private Renderer[] _renderers = new Renderer[0];
    private Renderer[] _haloRenderers = new Renderer[0];
    private Material[] _normalMaterials = new Material[0];
    private Material[] _ghostMaterials = new Material[0];
    private Renderer _screenRenderer;

    private Material _suit;
    private Material _glove;
    private Material _cuff;
    private Material _accent;
    private Material _device;
    private Material _screen;

    private Material _suitGhost;
    private Material _gloveGhost;
    private Material _cuffGhost;
    private Material _accentGhost;
    private Material _deviceGhost;
    private Material _screenGhost;
    private Material _halo;

    private Quaternion _leftRest;
    private Quaternion _leftLift;
    private Quaternion _leftHold;
    private Quaternion _rightRest;
    private Vector3 _leftRestPosition;
    private Vector3 _leftLiftPosition;
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

        // --- hold pose: the whole forearm is laid ACROSS the body in front of the
        //     chest, so at a level gaze it projects as a near-HORIZONTAL bar in
        //     the lower half of the frame with the watch face turned to the
        //     camera. The arm's own axis is the screen-horizontal world axis
        //     (the player's right); the elbow stays low and to the left, the hand
        //     ends around the middle of the frame.
        //
        //     Geometry (eye space, metres): the root is the elbow, the arm runs
        //     0.30 m to the watch and 0.50 m to the finger tips, so at z = 0.50
        //     the bar covers roughly viewport x 0.25..0.65 and the watch lands
        //     near viewport x 0.49 / y 0.24 (bottom-left origin) - i.e. under the
        //     hologram, which is drawn AFTER the opaque arms.
        Vector3 shoulderLeft = new Vector3(-ShoulderSide, -ShoulderDrop, ShoulderForward);
        Vector3 holdShoulder = new Vector3(-0.310f, -0.170f, 0.480f);
        Vector3 holdDir = new Vector3(0.997f, -0.050f, 0.060f).normalized;
        Vector3 holdFacing = new Vector3(0.10f, 0.30f, -0.95f).normalized;
        _leftHold = ArmRotation(holdDir, holdFacing);

        // --- lift pose: the elbow comes up and forward while the forearm is
        //     still angled inward, which is the natural middle of the motion.
        //     The raise blends rest -> lift -> hold along ONE eased parameter, so
        //     the orientation is distributed over the whole move instead of
        //     snapping in the last 10%.
        Vector3 liftShoulder = new Vector3(-0.315f, -0.115f, 0.300f);
        Vector3 liftDir = new Vector3(-0.350f, 0.200f, 0.915f).normalized;
        Vector3 liftFacing = new Vector3(0.25f, 0.55f, -0.80f).normalized;
        _leftLift = ArmRotation(liftDir, liftFacing);

        _leftArm = BuildArm("LeftArmRoot", shoulderLeft, layer, true);
        _rightArm = BuildArm("RightArmRoot",
            new Vector3(ShoulderSide, -ShoulderDrop, ShoulderForward), layer, false);
        _leftRestPosition = shoulderLeft;
        _leftLiftPosition = liftShoulder;
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

        CacheMaterials();
        BuildSoftHalo(layer);

        _leftArm.localRotation = _leftRest;
        _rightArm.localRotation = _rightRest;
        ApplyMotion();
        ApplySoftening();
        SetVisible(true);
        SetWristScreenActive(false);
    }

    /// <summary>
    /// Remembers each block's normal material next to its softened ghost, so
    /// SetSoftened only has to swap shared materials (no per-frame allocation,
    /// no material instancing).
    /// </summary>
    private void CacheMaterials()
    {
        _normalMaterials = new Material[_renderers.Length];
        _ghostMaterials = new Material[_renderers.Length];
        for (int index = 0; index < _renderers.Length; index++)
        {
            Material normal = _renderers[index] != null
                ? _renderers[index].sharedMaterial
                : null;
            _normalMaterials[index] = normal;
            _ghostMaterials[index] = GhostOf(normal);
        }
    }

    private Material GhostOf(Material normal)
    {
        if (normal == _suit)
        {
            return _suitGhost;
        }

        if (normal == _glove)
        {
            return _gloveGhost;
        }

        if (normal == _cuff)
        {
            return _cuffGhost;
        }

        if (normal == _accent)
        {
            return _accentGhost;
        }

        if (normal == _device)
        {
            return _deviceGhost;
        }

        if (normal == _screen)
        {
            return _screenGhost;
        }

        return _suitGhost;
    }

    /// <summary>
    /// Builds the soft-focus bleed: a slightly larger, very faint copy of the
    /// presented (left) arm that only exists while the hologram is up. It is a
    /// presentation-only object on the arms layer, driven from the real arm's
    /// transform every frame, so it can never desync from the pose.
    /// </summary>
    private void BuildSoftHalo(int layer)
    {
        if (_leftArm == null || _halo == null)
        {
            _haloRenderers = new Renderer[0];
            return;
        }

        GameObject halo = Instantiate(_leftArm.gameObject, _armsRoot);
        halo.name = "LeftArmSoftHalo";
        halo.transform.localPosition = _leftArm.localPosition;
        halo.transform.localRotation = _leftArm.localRotation;
        halo.transform.localScale = Vector3.one * HaloScale;
        halo.layer = layer;

        foreach (Transform child in halo.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }

        foreach (Renderer renderer in halo.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterial = _halo;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.enabled = false;
        }

        _haloRenderers = halo.GetComponentsInChildren<Renderer>(true);
        _leftHalo = halo.transform;
        halo.SetActive(false);
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

        // ---- softened ghost set (Stage B: the UI owns the frame) ----
        // Same geometry, but translucent, desaturated, dark and without the
        // emissive accents that make the physical arm compete with the hologram.
        _suitGhost = MakeTransparentCopy(_suit, "FPBlockArms_Suit_Ghost",
            SoftenColour(SuitColour), SoftenedAlpha, 0.10f, null);
        _gloveGhost = MakeTransparentCopy(_glove, "FPBlockArms_Glove_Ghost",
            SoftenColour(GloveColour), SoftenedAlpha, 0.08f, null);
        _cuffGhost = MakeTransparentCopy(_cuff, "FPBlockArms_Cuff_Ghost",
            SoftenColour(CuffColour), SoftenedAlpha, 0.10f, null);
        _accentGhost = MakeTransparentCopy(_accent, "FPBlockArms_Accent_Ghost",
            SoftenColour(AccentColour), SoftenedAlpha * 0.8f, 0.10f, null);
        _deviceGhost = MakeTransparentCopy(_device, "FPBlockArms_Device_Ghost",
            SoftenColour(DeviceColour), SoftenedAlpha, 0.12f, null);
        _screenGhost = MakeTransparentCopy(_screen, "FPBlockArms_Screen_Ghost",
            SoftenColour(new Color(0.035f, 0.100f, 0.115f)), SoftenedAlpha, 0.20f,
            ScreenEmissionIdle * SoftenedEmissionScale);
        _halo = MakeTransparentCopy(_suit, "FPBlockArms_SoftHalo",
            SoftenColour(SuitColour), HaloAlpha, 0.0f, null);
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

    /// <summary>
    /// Turns an opaque URP Lit material into a TRANSPARENT copy: same look at
    /// full opacity, but it can actually fade. URP needs the surface type, the
    /// blend state and the transparent keyword to agree, otherwise the shader
    /// keeps writing opaque pixels.
    /// </summary>
    private static Material MakeTransparentCopy(Material source, string name, Color colour,
        float alpha, float smoothness, Color? emission)
    {
        if (source == null)
        {
            return null;
        }

        var material = new Material(source) { name = name };
        material.SetOverrideTag("RenderType", "Transparent");
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f); // 0 = opaque, 1 = transparent
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f); // alpha blend
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend",
                (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = SoftenedRenderQueue;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", new Color(colour.r, colour.g, colour.b, alpha));
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", 0f);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }

        if (material.HasProperty("_EmissionColor"))
        {
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
        }

        return material;
    }

    /// <summary>
    /// The softened look of one opaque arm material: desaturated toward the
    /// background grey, darkened, flattened and faded to <see cref="SoftenedAlpha"/>.
    /// </summary>
    private static Color SoftenColour(Color colour)
    {
        Color desaturated = Color.Lerp(colour, BackgroundGrey, SoftenedDesaturation);
        return new Color(desaturated.r * SoftenedBaseScale,
            desaturated.g * SoftenedBaseScale,
            desaturated.b * SoftenedBaseScale,
            1f);
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

        // The soft bleed is part of the arm presentation, never a separate object
        // that could survive a hidden state (console view, device panel, pause).
        if (_leftHalo != null)
        {
            _leftHalo.gameObject.SetActive(visible && _softened);
        }

        foreach (Renderer renderer in _haloRenderers)
        {
            if (renderer != null)
            {
                renderer.enabled = visible && _softened;
            }
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
    /// Stage B of the wrist presentation: the hologram owns the frame, so the
    /// physical arm drops to a translucent desaturated ghost (plus a faint
    /// oversized bleed) and stops competing with the UI. Set false restores the
    /// crisp arm - the bridge does that as soon as the terminal is released,
    /// before the arm lowers.
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

    /// <summary>
    /// Swaps every block to its ghost material (or back) and shows the soft
    /// bleed. A material SWAP rather than a property tweak on purpose: a real
    /// blur is not available without a second render path - the hologram is a
    /// world quad drawn by the same camera, so any post-process blur would blur
    /// the UI with the arm - and alpha blending needs the transparent queue,
    /// which is a blend state of the material.
    /// </summary>
    private void ApplySoftening()
    {
        for (int index = 0; index < _renderers.Length; index++)
        {
            Renderer renderer = _renderers[index];
            if (renderer == null)
            {
                continue;
            }

            Material target = _softened && index < _ghostMaterials.Length
                ? _ghostMaterials[index]
                : index < _normalMaterials.Length ? _normalMaterials[index] : null;
            if (target != null)
            {
                renderer.sharedMaterial = target;
            }
        }

        if (_leftHalo != null)
        {
            _leftHalo.gameObject.SetActive(_softened && _visible);
        }

        foreach (Renderer renderer in _haloRenderers)
        {
            if (renderer != null)
            {
                renderer.enabled = _softened && _visible;
            }
        }

        ApplyScreenEmission();
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

        // The SHARED root is never rotated: rest and look-down stay pure body
        // space, so a level gaze keeps the hands out of frame and a look-down
        // brings both arms in from the lower corners.
        //
        // Only the LEFT arm (the presented one) follows the camera pitch, and
        // through its own presentation root: the existing Wrist UI is camera
        // locked and the player cannot look around while it is open, so the pose
        // the player opened it from has to be compensated or the device would be
        // framed off-screen after looking down first.
        float presentBlend = Mathf.Clamp01(_raiseProgress);
        float pitch = _player != null ? _player.PitchDegrees : 0f;
        Quaternion pitchCompensation = Quaternion.Euler(pitch * presentBlend, 0f, 0f);

        // Eased presentation weight so the raise settles instead of stopping dead.
        float eased = presentBlend * presentBlend * (3f - 2f * presentBlend);

        float sway = _moving ? WalkSwayDegrees * Mathf.Sin(_walkPhase) : 0f;
        float micro = presentBlend > 0.01f
            ? 0.8f * Mathf.Sin(Time.time * 1.7f) * presentBlend
            : 0f;

        // rest -> lift -> hold along ONE eased parameter: the elbow comes up
        // first and the forearm rotates to horizontal through the lift key, so
        // no part of the move snaps (especially not the last 10%).
        Quaternion poseRotation = PathRotation(_leftRest, _leftLift, _leftHold, eased)
                                  * Quaternion.Euler(-sway + micro, 0f, 0f);
        Vector3 posePosition = PathPosition(_leftRestPosition, _leftLiftPosition,
            _leftHoldPosition, eased);

        _leftArm.localRotation = pitchCompensation * poseRotation;
        _leftArm.localPosition = pitchCompensation * posePosition;

        // The soft-focus bleed follows the real arm exactly.
        if (_leftHalo != null)
        {
            _leftHalo.localPosition = _leftArm.localPosition;
            _leftHalo.localRotation = _leftArm.localRotation;
        }

        _rightArm.localRotation = _rightRest
                                  * Quaternion.Euler(sway, 0f, 0f);
        _rightArm.localPosition = _rightRestPosition;
    }

    /// <summary>Two-segment eased rotation path rest -&gt; lift -&gt; hold.</summary>
    private static Quaternion PathRotation(Quaternion rest, Quaternion lift,
        Quaternion hold, float eased)
    {
        const float liftAt = 0.45f;
        if (eased <= liftAt)
        {
            float local = Mathf.SmoothStep(0f, 1f, eased / liftAt);
            return Quaternion.Slerp(rest, lift, local);
        }

        float upper = Mathf.SmoothStep(0f, 1f, (eased - liftAt) / (1f - liftAt));
        return Quaternion.Slerp(lift, hold, upper);
    }

    /// <summary>Two-segment eased position path rest -&gt; lift -&gt; hold.</summary>
    private static Vector3 PathPosition(Vector3 rest, Vector3 lift, Vector3 hold,
        float eased)
    {
        const float liftAt = 0.45f;
        if (eased <= liftAt)
        {
            float local = Mathf.SmoothStep(0f, 1f, eased / liftAt);
            return Vector3.Lerp(rest, lift, local);
        }

        float upper = Mathf.SmoothStep(0f, 1f, (eased - liftAt) / (1f - liftAt));
        return Vector3.Lerp(lift, hold, upper);
    }
}
