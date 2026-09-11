using UnityEngine;

/// <summary>
/// Procedural sliding ship door (Phase 07 Checkpoint A).
///
/// Two panel visuals slide outward along the door-local X axis (the doorway
/// gap direction). The authoritative state machine runs in Update; tests may
/// disable the component and drive AdvanceDoor(deltaTime) deterministically,
/// the same pattern the surface node timers use.
///
/// Door safety: closing is refused while the player occupies the doorway and
/// a closing door reopens if the player steps into it, so the player can
/// never be trapped by a door.
/// </summary>
public class ShipDoorInteractable : MonoBehaviour, IFirstPersonInteractable
{
    public enum ShipDoorState
    {
        Closed,
        Opening,
        Open,
        Closing
    }

    [SerializeField]
    private int _doorId = 1;

    // Doorway geometry in door-local space. The door plane is local z == 0;
    // the sliding gap runs along local x from _localSpanStart .. _localSpanEnd
    // (y is ignored). Authored by the deterministic scene builder.
    [SerializeField]
    private Vector3 _localSpanStart;

    [SerializeField]
    private Vector3 _localSpanEnd;

    [SerializeField]
    private float _planeHalfThickness = 0.45f;

    [SerializeField]
    private float _spanMargin = 0.2f;

    [SerializeField]
    private Transform _panelLeft;

    [SerializeField]
    private Transform _panelRight;

    [SerializeField]
    private float _panelTravel = 1.0f;

    /// <summary>0..1 opening amount per second while animating.</summary>
    [SerializeField]
    private float _slideSpeed01 = 0.9f;

    /// <summary>Solid collider that blocks the doorway while effectively shut.</summary>
    [SerializeField]
    private Collider _blocker;

    private SurfacePlayerController _player;
    private float _openAmount;
    private Vector3 _closedLeftPos;
    private Vector3 _closedRightPos;
    private bool _positionsCaptured;
    private bool _ready;

    public int DoorId => _doorId;
    public ShipDoorState State { get; private set; } = ShipDoorState.Open;

    /// <summary>0 closed .. 1 fully open (read-only).</summary>
    public float OpenAmount => _openAmount;

    /// <summary>True while the player collision volume overlaps the doorway.</summary>
    public bool IsOccupied { get; private set; }

    public bool IsOpen => State == ShipDoorState.Open;

    /// <summary>Solid blocker collider (enabled while effectively shut).</summary>
    public Collider BlockerCollider => _blocker;

    /// <summary>Doorway local-space X span (min, max) that the leaves cover
    /// while closed; leaves slide outside it while open.</summary>
    public Vector2 DoorwayLocalSpanX => new Vector2(
        Mathf.Min(_localSpanStart.x, _localSpanEnd.x),
        Mathf.Max(_localSpanStart.x, _localSpanEnd.x));

    /// <summary>
    /// Phase 07B deterministic closed-door geometry check: the leaf front
    /// planes must sit strictly inside (recessed from) the header/frame front
    /// planes by >= 0.015 m and must never be coplanar with the surrounding
    /// trim. Measured from the real child renderer bounds in door-local space.
    /// </summary>
    public bool VerifyClosedGeometry(out float leafToFrameSeparationM,
        out string report)
    {
        var sb = new System.Text.StringBuilder();
        leafToFrameSeparationM = -1f;
        Renderer header = FindChildRenderer("Frame_Header");
        Renderer panelL = _panelLeft != null ? _panelLeft.GetComponentInChildren<Renderer>() : null;
        Renderer panelR = _panelRight != null ? _panelRight.GetComponentInChildren<Renderer>() : null;
        if (header == null || panelL == null || panelR == null)
        {
            report = "missing header/panel renderers (header=" + (header != null)
                     + " panelL=" + (panelL != null) + " panelR=" + (panelR != null) + ")";
            return false;
        }

        Bounds h = LocalBoundsOf(header);
        Bounds l = LocalBoundsOf(panelL);
        Bounds r = LocalBoundsOf(panelR);

        // Planes are perpendicular to the door-local Z axis (wall normal).
        float leafMaxZ = Mathf.Max(l.max.z, r.max.z);
        float leafMinZ = Mathf.Min(l.min.z, r.min.z);
        float frameMaxZ = h.max.z;
        float frameMinZ = h.min.z;
        sb.AppendLine("INFO :: leaf z " + leafMinZ.ToString("0.000") + ".."
                      + leafMaxZ.ToString("0.000") + " header z "
                      + frameMinZ.ToString("0.000") + ".." + frameMaxZ.ToString("0.000"));
        float sep = Mathf.Min(frameMaxZ - leafMaxZ, leafMinZ - frameMinZ);
        leafToFrameSeparationM = sep;
        sb.AppendLine("INFO :: leaf/frame surface separation " + sep.ToString("0.000") + " m");

        bool ok = sep >= 0.015f && leafMaxZ < frameMaxZ - 0.0149f
                  && leafMinZ > frameMinZ + 0.0149f;
        sb.AppendLine((ok ? "PASS" : "FAIL") + " :: closed leaf recessed >= 0.015 m from frame planes");
        report = sb.ToString();
        return ok;
    }

    /// <summary>
    /// Phase 07B open-door check: with the door fully open both leaves must be
    /// entirely outside the doorway span in door-local X (never overlapping
    /// the walkway or any frame volume), and the solid blocker must be off.
    /// </summary>
    public bool VerifyOpenClearance(out string report)
    {
        var sb = new System.Text.StringBuilder();
        bool ok = true;
        Renderer panelL = _panelLeft != null ? _panelLeft.GetComponentInChildren<Renderer>() : null;
        Renderer panelR = _panelRight != null ? _panelRight.GetComponentInChildren<Renderer>() : null;
        if (panelL == null || panelR == null)
        {
            report = "missing panel renderers";
            return false;
        }

        bool blockerOff = _blocker == null || !_blocker.enabled;
        sb.AppendLine((blockerOff ? "PASS" : "FAIL")
                      + " :: solid blocker disabled while open (enabled="
                      + (_blocker != null && _blocker.enabled) + ")");
        ok &= blockerOff;

        Vector2 span = DoorwayLocalSpanX;
        Bounds l = LocalBoundsOf(panelL);
        Bounds r = LocalBoundsOf(panelR);
        bool leftClear = l.max.x <= span.x + 0.001f;
        bool rightClear = r.min.x >= span.y - 0.001f;
        sb.AppendLine("INFO :: span x " + span.x.ToString("0.000") + ".."
                      + span.y.ToString("0.000") + " leafL x " + l.min.x.ToString("0.000")
                      + ".." + l.max.x.ToString("0.000") + " leafR x " + r.min.x.ToString("0.000")
                      + ".." + r.max.x.ToString("0.000"));
        sb.AppendLine((leftClear ? "PASS" : "FAIL") + " :: left leaf outside doorway span");
        sb.AppendLine((rightClear ? "PASS" : "FAIL") + " :: right leaf outside doorway span");
        ok &= leftClear && rightClear;
        report = sb.ToString();
        return ok;
    }

    /// <summary>Renderer bounds of a named child converted to door-local space.</summary>
    private Bounds LocalBoundsOf(Renderer renderer)
    {
        Bounds world = renderer.bounds;
        Vector3 min = transform.InverseTransformPoint(world.min);
        Vector3 max = transform.InverseTransformPoint(world.max);
        Vector3 center = (min + max) * 0.5f;
        Vector3 size = max - min;
        return new Bounds(center, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y),
            Mathf.Abs(size.z)));
    }

    private Renderer FindChildRenderer(string childName)
    {
        Transform child = null;
        foreach (Transform t in transform)
        {
            if (t.name == childName)
            {
                child = t;
                break;
            }
        }

        return child != null ? child.GetComponentInChildren<Renderer>() : null;
    }

    /// <summary>Doorway half width (gapWidth / 2) in door-local metres.</summary>
    public float DoorHalfWidth { get; private set; } = 0.9f;

    /// <summary>Builder configuration (scene authoring only).</summary>
    public void Configure(int doorId, Vector3 localSpanStart, Vector3 localSpanEnd,
        Transform panelLeft, Transform panelRight, Collider blocker,
        float panelTravel, float slideSpeed01, ShipDoorState initialState)
    {
        _doorId = doorId;
        _localSpanStart = localSpanStart;
        _localSpanEnd = localSpanEnd;
        _panelLeft = panelLeft;
        _panelRight = panelRight;
        _blocker = blocker;
        _panelTravel = panelTravel;
        _slideSpeed01 = slideSpeed01;
        State = initialState;
        _openAmount = initialState == ShipDoorState.Open ? 1f : 0f;
        CapturePositions();
        // No edit-time panel motion: the scene saves the closed-panel pose so
        // runtime Start can animate from the true closed baseline.
        RefreshBlocker();
    }

    private void Awake()
    {
        CapturePositions();
    }

    private void Start()
    {
        _player = Object.FindAnyObjectByType<SurfacePlayerController>();
        if (State == ShipDoorState.Open)
        {
            _openAmount = 1f;
        }
        else if (State == ShipDoorState.Closed)
        {
            _openAmount = 0f;
        }

        // Panels are authored at their closed pose; the first frame snaps them
        // to the authored state (open doors slide outward immediately).
        ApplyPanels();
        RefreshBlocker();
        _ready = true;
    }

    private void Update()
    {
        if (!_ready)
        {
            return;
        }

        RefreshOccupancy();

        // Safety: never trap the player - a closing door reopens.
        if (State == ShipDoorState.Closing && IsOccupied)
        {
            State = ShipDoorState.Opening;
        }

        if (State == ShipDoorState.Opening || State == ShipDoorState.Closing)
        {
            AdvanceDoor(Time.deltaTime);
        }
    }

    private void CapturePositions()
    {
        if (_positionsCaptured || _panelLeft == null || _panelRight == null)
        {
            return;
        }

        _closedLeftPos = _panelLeft.localPosition;
        _closedRightPos = _panelRight.localPosition;
        _positionsCaptured = true;
    }

    /// <summary>
    /// Advances the animation by <paramref name="deltaTime"/> seconds.
    /// Public so deterministic tests can drive the exact same motion the
    /// real Update loop performs.
    /// </summary>
    public void AdvanceDoor(float deltaTime)
    {
        if (State == ShipDoorState.Opening)
        {
            _openAmount = Mathf.Clamp01(_openAmount + deltaTime * _slideSpeed01);
            if (_openAmount >= 1f)
            {
                State = ShipDoorState.Open;
            }
        }
        else if (State == ShipDoorState.Closing)
        {
            if (IsOccupied)
            {
                State = ShipDoorState.Opening;
                return;
            }

            _openAmount = Mathf.Clamp01(_openAmount - deltaTime * _slideSpeed01);
            if (_openAmount <= 0f)
            {
                State = ShipDoorState.Closed;
            }
        }
        else
        {
            return;
        }

        ApplyPanels();
        RefreshBlocker();
    }

    /// <summary>Recomputes doorway occupancy from the real player capsule.</summary>
    public void RefreshOccupancy()
    {
        if (_player == null)
        {
            IsOccupied = false;
            return;
        }

        Vector3 feet = _player.transform.position;
        Vector3 local = transform.InverseTransformPoint(feet);
        local.y = 0f;

        float minX = Mathf.Min(_localSpanStart.x, _localSpanEnd.x) - _spanMargin;
        float maxX = Mathf.Max(_localSpanStart.x, _localSpanEnd.x) + _spanMargin;
        IsOccupied = Mathf.Abs(local.z) <= _planeHalfThickness
                     && local.x >= minX && local.x <= maxX;
    }

    private void ApplyPanels()
    {
        if (!_positionsCaptured)
        {
            return;
        }

        float outward = _openAmount * _panelTravel;
        if (_panelLeft != null)
        {
            _panelLeft.localPosition = new Vector3(
                _closedLeftPos.x - outward, _closedLeftPos.y, _closedLeftPos.z);
        }

        if (_panelRight != null)
        {
            _panelRight.localPosition = new Vector3(
                _closedRightPos.x + outward, _closedRightPos.y, _closedRightPos.z);
        }
    }

    private void RefreshBlocker()
    {
        if (_blocker != null)
        {
            _blocker.enabled = _openAmount < 0.5f;
        }
    }

    // ---- IFirstPersonInteractable ----

    public bool CanInteract()
    {
        return true;
    }

    public string GetInteractionPrompt()
    {
        bool openingOrOpen = State == ShipDoorState.Open || State == ShipDoorState.Opening;
        return InputDeviceTracker.FormatPrompt(openingOrOpen ? "关门" : "开门");
    }

    public void Interact()
    {
        Toggle();
    }

    /// <summary>Requested toggle: open when shut, close when open.</summary>
    public void Toggle()
    {
        if (State == ShipDoorState.Open || State == ShipDoorState.Opening)
        {
            RequestClose();
        }
        else
        {
            RequestOpen();
        }
    }

    public void RequestOpen()
    {
        if (State == ShipDoorState.Open || State == ShipDoorState.Opening)
        {
            return;
        }

        State = ShipDoorState.Opening;
        RefreshBlocker();
    }

    public void RequestClose()
    {
        if (State == ShipDoorState.Closed || State == ShipDoorState.Closing)
        {
            return;
        }

        if (IsOccupied)
        {
            // Door safety: do not close through the player.
            return;
        }

        State = ShipDoorState.Closing;
        RefreshBlocker();
    }
}
