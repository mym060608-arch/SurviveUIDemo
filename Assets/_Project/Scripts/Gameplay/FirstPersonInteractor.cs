using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Camera-center world interaction (Phase 06 Revision).
///
/// Every frame a ray from the eye camera center finds the gazed interactable
/// within range. E triggers the current target through the same small
/// IFirstPersonInteractable contract a future ship console / door will use.
///
/// Movement and wrist gating come from SurfacePlayerController.InputEnabled:
/// while the wrist terminal is open no world interaction is offered.
/// </summary>
public class FirstPersonInteractor : MonoBehaviour
{
    public float interactRange = 3.0f;

    private Camera _eyeCamera;
    private SurfacePlayerController _player;
    private IFirstPersonInteractable _currentTarget;
    private IFirstPersonInteractable _lastBlockedTarget;

    /// <summary>Currently gazed interactable (null when nothing is valid).</summary>
    public IFirstPersonInteractable CurrentTarget => _currentTarget;

    /// <summary>True when the gaze is on a target that can be interacted with now.</summary>
    public bool HasValidTarget => _currentTarget != null && _currentTarget.CanInteract();

    /// <summary>True when the gaze is on an interactable that is currently
    /// busy/depleted (crosshair shows the blocked state).</summary>
    public bool HasBlockedTarget => _lastBlockedTarget != null;

    private void Awake()
    {
        _player = GetComponent<SurfacePlayerController>();
    }

    private void Start()
    {
        if (_eyeCamera == null)
        {
            _eyeCamera = _player != null ? _player.EyeCamera : Camera.main;
        }

        if (_eyeCamera == null)
        {
            _eyeCamera = GetComponentInChildren<Camera>(true);
        }
    }

    private void Update()
    {
        RefreshTarget();

        // E world interaction (disabled while the wrist is open).
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame && IsInteractionAllowed())
        {
            TryInteract();
        }
    }

    /// <summary>Re-evaluates the gazed target from the camera center ray.</summary>
    public void RefreshTarget()
    {
        _currentTarget = null;
        _lastBlockedTarget = null;
        if (_eyeCamera == null)
        {
            return;
        }

        Ray ray = _eyeCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange,
            ~0, QueryTriggerInteraction.Collide))
        {
            IFirstPersonInteractable target =
                hit.collider.GetComponentInParent<IFirstPersonInteractable>();
            if (target != null)
            {
                if (target.CanInteract())
                {
                    _currentTarget = target;
                }
                else
                {
                    // Remember a looked-at busy/depleted object so the HUD can
                    // show its state prompt instead of the generic one.
                    _lastBlockedTarget = target;
                }
            }
        }
    }

    /// <summary>World interaction is only allowed while exploration input is
    /// enabled (wrist closed).</summary>
    public bool IsInteractionAllowed()
    {
        return _player == null || _player.InputEnabled;
    }

    /// <summary>Performs the interaction on the current gaze target. Returns
    /// false when there is nothing valid to interact with.</summary>
    public bool TryInteract()
    {
        if (!IsInteractionAllowed())
        {
            return false;
        }

        RefreshTarget();
        if (!HasValidTarget)
        {
            return false;
        }

        _currentTarget.Interact();
        return true;
    }

    /// <summary>Player-facing prompt for the current gaze state:
    /// null means "nothing to say" (HUD falls back to its own default copy).</summary>
    public string ResolvePrompt()
    {
        if (_currentTarget != null)
        {
            return _currentTarget.GetInteractionPrompt();
        }

        if (_lastBlockedTarget != null)
        {
            return _lastBlockedTarget.GetInteractionPrompt();
        }

        return null;
    }
}
