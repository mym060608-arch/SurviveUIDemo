using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase 08 Revision hero visual: the Main Menu's real 3D ship.
///
/// The ship is the project's authoritative main-menu asset
/// (<c>Assets/_Project/Models/MainMenuShip/MainMenuShip.fbx</c>, exported from
/// <c>Artsource/MainMenuShip</c>) instantiated into 04_MainMenu as the LEFT hero,
/// floating in deep space. This component owns everything about that hero:
///
/// - it discovers the two authored exhaust anchors (VFX_Thruster_L / _R) and
///   builds one additive plume per nozzle, so the "double thruster flame" is a
///   property of the asset, not a hardcoded offset;
/// - it keeps the ship alive while the menu is idle (slow bob + yaw/pitch sway
///   and a slow camera-independent drift), which the 系统设置 动态效果 switch
///   pauses;
/// - it drives the engine level (idle flame -> boost) and the departure
///   translation used by the start cinematic.
///
/// The component never touches UI, routing or the session: the menu controller
/// and the cinematic own those. Motion is presentation only - disabling it
/// freezes the hero, it never removes the ship, the plumes or the starfield.
/// </summary>
public class MainMenuShipHero : MonoBehaviour
{
    /// <summary>Nominal engine bell radius in metres. The authored asset has an
    /// annular exhaust recess with an inner radius of 0.70 m and an outer bell
    /// radius of 1.04 m; the plume is authored at the geometric middle.</summary>
    public const float NozzleRadius = 0.86f;

    /// <summary>Anchors authored by the Blender asset build script.</summary>
    public const string LeftAnchorName = "VFX_Thruster_L";
    public const string RightAnchorName = "VFX_Thruster_R";

    /// <summary>Metres the ship travels forward while it leaves the frame.</summary>
    public const float DepartureDistance = 260f;

    /// <summary>Departure duration in seconds (PASS 3 §11.4).</summary>
    public const float DepartureSeconds = 1.15f;

    [SerializeField]
    private Material plumeMaterial;

    [SerializeField]
    private float bobAmplitude = 0.16f;

    [SerializeField]
    private float bobSeconds = 7.5f;

    [SerializeField]
    private float swayDegrees = 0.7f;

    [SerializeField]
    private float swaySeconds = 11f;

    [SerializeField]
    private float driftMetres = 0.55f;

    private readonly List<MainMenuThrusterFlame> _flames = new List<MainMenuThrusterFlame>();
    private Transform _shipVisual;
    private Vector3 _basePosition;
    private Quaternion _baseRotation;
    private Vector3 _aftLocal = Vector3.forward;
    private Vector3 _forwardWorld = Vector3.back;
    private float _engineTarget;
    private float _engineLevel;
    private float _departureTime;
    private bool _departing;
    private Renderer[] _shipRenderers;

    /// <summary>Root of the authored ship instance (the visual child).</summary>
    public Transform ShipVisual => _shipVisual;

    /// <summary>Number of plume renderers built from the asset's exhaust anchors.</summary>
    public int NozzleCount => _flames.Count;

    /// <summary>The two plumes (verification + presentation settings checks).</summary>
    public IReadOnlyList<MainMenuThrusterFlame> Flames => _flames;

    /// <summary>Smoothed engine level: 0 idle, 1 boost.</summary>
    public float EngineLevel => _engineLevel;

    /// <summary>Requested engine level (set by the cinematic).</summary>
    public float EngineTarget => _engineTarget;

    /// <summary>True once the ship is flying out of frame.</summary>
    public bool IsDeparting => _departing;

    /// <summary>0..1 departure progress.</summary>
    public float DepartureProgress => Mathf.Clamp01(_departureTime / DepartureSeconds);

    /// <summary>True when the whole ship hierarchy is present and rendering.</summary>
    public bool IsShipPresent => _shipVisual != null
                                 && _shipRenderers != null
                                 && _shipRenderers.Length > 0;

    /// <summary>Renderer count of the authored ship (46 meshes in the asset).</summary>
    public int ShipRendererCount => _shipRenderers != null ? _shipRenderers.Length : 0;

    /// <summary>Ship-space aft direction (towards the thrusters).</summary>
    public Vector3 AftDirection => _aftLocal;

    /// <summary>Authored idle position the hero returns to (and departs from).</summary>
    public Vector3 SpawnPosition => _basePosition;

    /// <summary>Current world-space bounds of the whole hero (composition audit).</summary>
    public Bounds WorldBounds
    {
        get
        {
            if (!IsShipPresent)
            {
                return new Bounds(transform.position, Vector3.zero);
            }

            Bounds bounds = _shipRenderers[0].bounds;
            for (int i = 1; i < _shipRenderers.Length; i++)
            {
                if (_shipRenderers[i] != null && _shipRenderers[i].enabled)
                {
                    bounds.Encapsulate(_shipRenderers[i].bounds);
                }
            }

            return bounds;
        }
    }

    /// <summary>Build-time wiring: the ship instance authored under this pivot.</summary>
    public void ConfigureShip(Transform shipVisual, Material flameMaterial)
    {
        _shipVisual = shipVisual;
        if (flameMaterial != null)
        {
            plumeMaterial = flameMaterial;
        }
    }

    private void Awake()
    {
        _basePosition = transform.position;
        _baseRotation = transform.rotation;
    }

    private void Start()
    {
        if (_shipVisual == null && transform.childCount > 0)
        {
            _shipVisual = transform.GetChild(0);
        }

        if (_shipVisual == null)
        {
            Debug.LogError("[MainMenuShipHero] no ship instance under the hero pivot");
            return;
        }

        _shipRenderers = _shipVisual.GetComponentsInChildren<Renderer>(true);
        ResolveExhaustAnchors();
        BuildFlames();
        Debug.Log("[MainMenuShipHero] hero ready renderers=" + _shipRenderers.Length
                  + " nozzles=" + _flames.Count
                  + " aft=" + _aftLocal.ToString("F3")
                  + " bounds=" + WorldBounds.size.ToString("F2"));
    }

    /// <summary>
    /// Reads the exhaust anchors out of the authored asset. The aft direction is
    /// MEASURED (root -> anchor midpoint) instead of assumed, so the hero, the
    /// plumes and the departure all keep working whatever axis convention the
    /// FBX importer produced.
    /// </summary>
    private void ResolveExhaustAnchors()
    {
        Transform left = FindDeep(_shipVisual, LeftAnchorName);
        Transform right = FindDeep(_shipVisual, RightAnchorName);
        if (left == null || right == null)
        {
            Debug.LogError("[MainMenuShipHero] exhaust anchors missing ("
                           + LeftAnchorName + " / " + RightAnchorName + ")");
            return;
        }

        Vector3 midpoint = (left.position + right.position) * 0.5f;
        Vector3 aftWorld = midpoint - _shipVisual.position;
        if (aftWorld.sqrMagnitude < 0.0001f)
        {
            aftWorld = _shipVisual.forward;
        }

        _aftLocal = transform.InverseTransformDirection(aftWorld.normalized);
        // The pivot carries the authored hero rotation, so the flight direction
        // is kept in WORLD space: the departure offset is added to a world
        // position and must not be rotated a second time.
        _forwardWorld = -aftWorld.normalized;
    }

    private void BuildFlames()
    {
        if (plumeMaterial == null)
        {
            Debug.LogError("[MainMenuShipHero] plume material not assigned");
            return;
        }

        Transform left = FindDeep(_shipVisual, LeftAnchorName);
        Transform right = FindDeep(_shipVisual, RightAnchorName);
        if (left == null || right == null)
        {
            return;
        }

        _flames.Add(MainMenuThrusterFlame.Create(transform,
            transform.InverseTransformPoint(left.position), _aftLocal,
            NozzleRadius, plumeMaterial, "Plume_L"));
        _flames.Add(MainMenuThrusterFlame.Create(transform,
            transform.InverseTransformPoint(right.position), _aftLocal,
            NozzleRadius, plumeMaterial, "Plume_R"));
    }

    /// <summary>Requests an engine level (0 idle flame, 1 full boost).</summary>
    public void SetEngineLevel(float level)
    {
        _engineTarget = Mathf.Clamp01(level);
    }

    /// <summary>Starts the departure (the ship accelerates out of frame).</summary>
    public void BeginDeparture()
    {
        _departing = true;
        _departureTime = 0f;
    }

    /// <summary>Returns the hero to its authored idle pose.</summary>
    public void ResetHero()
    {
        _departing = false;
        _departureTime = 0f;
        _engineLevel = 0f;
        _engineTarget = 0f;
        transform.position = _basePosition;
        transform.rotation = _baseRotation;
    }

    private void Update()
    {
        float time = Time.unscaledTime;
        float delta = Time.unscaledDeltaTime;

        _engineLevel = Mathf.MoveTowards(_engineLevel, _engineTarget, delta * 2.6f);

        if (_departing)
        {
            _departureTime += delta;
        }

        ApplyPose(time);
        ApplyEngine(time);
    }

    private void ApplyPose(float time)
    {
        if (MainMenuSettings.MotionEnabled)
        {
            float bob = Mathf.Sin(time * (Mathf.PI * 2f / bobSeconds)) * bobAmplitude;
            float yaw = Mathf.Sin(time * (Mathf.PI * 2f / swaySeconds)) * swayDegrees;
            float pitch = Mathf.Sin(time * (Mathf.PI * 2f / swaySeconds) * 0.73f + 1.1f)
                          * swayDegrees * 0.6f;
            float driftX = Mathf.Sin(time * (Mathf.PI * 2f / (swaySeconds * 1.7f)) + 0.4f)
                           * driftMetres;

            transform.position = _basePosition
                                 + new Vector3(driftX, bob, 0f)
                                 + DepartureOffset();
            transform.rotation = _baseRotation * Quaternion.Euler(pitch, yaw, 0f);
        }
        else
        {
            transform.position = _basePosition + DepartureOffset();
            transform.rotation = _baseRotation;
        }

        if (_departing)
        {
            // A restrained bank while the ship accelerates away; the roll is
            // applied about the flight axis so it reads as flight, not as a
            // spinning prop.
            float t = DepartureProgress;
            transform.rotation *= Quaternion.AngleAxis(
                Mathf.SmoothStep(0f, 1f, t) * 7f, _forwardWorld);
        }
    }

    private Vector3 DepartureOffset()
    {
        if (!_departing)
        {
            return Vector3.zero;
        }

        float t = DepartureProgress;
        float distance = DepartureDistance * t * t;
        return _forwardWorld * distance;
    }

    private void ApplyEngine(float time)
    {
        for (int i = 0; i < _flames.Count; i++)
        {
            if (_flames[i] != null)
            {
                _flames[i].ApplyEngine(_engineLevel, time);
            }
        }
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
}
