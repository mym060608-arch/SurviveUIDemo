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
        return openingOrOpen ? "E  关门" : "E  开门";
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
