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

    // When the camera origin sits inside a target's trigger volume, plain
    // ray/SphereCast misses. A small overlap fallback still requires the
    // target to be roughly in front (gaze-directed), so a node behind the
    // player is never picked just because it is near.
    public float veryNearOverlapRadius = 0.8f;
    public float veryNearConeDot = 0.45f; // ~63 degrees half-angle

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

        // World interaction is offered on E (keyboard) and A (gamepad); the
        // device that was used last decides which one the HUD prompt shows.
        InputDeviceTracker.Poll();

        bool pressed = false;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            pressed = true;
        }

        Gamepad pad = Gamepad.current;
        if (pad != null && pad.buttonSouth.wasPressedThisFrame)
        {
            pressed = true;
        }

        if (pressed && IsInteractionAllowed())
        {
            TryInteract();
        }
    }

    /// <summary>Re-evaluates the gazed target from the camera center.</summary>
    public void RefreshTarget()
    {
        _currentTarget = null;
        _lastBlockedTarget = null;
        if (_eyeCamera == null)
        {
            return;
        }

        Vector3 origin = _eyeCamera.transform.position;
        Ray ray = _eyeCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 forward = ray.direction;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = _eyeCamera.transform.forward;
        }

        forward.Normalize();

        // 1) Thin sphere cast first: forgiving of grazing angles near edges.
        if (Physics.SphereCast(ray, 0.15f, out RaycastHit hit, interactRange,
            ~0, QueryTriggerInteraction.Collide))
        {
            if (TryResolveTarget(hit.collider))
            {
                return;
            }
        }

        // 2) Very-near fallback: when the camera origin is inside the target's
        // trigger, casts miss entirely. Overlap the immediate area and only
        // accept a target that is in front of the player (gaze-directed).
        if (Physics.OverlapSphere(origin, veryNearOverlapRadius, ~0,
            QueryTriggerInteraction.Collide) is { Length: > 0 } overlaps)
        {
            IFirstPersonInteractable best = null;
            float bestScore = float.MinValue;
            foreach (Collider overlap in overlaps)
            {
                IFirstPersonInteractable target =
                    overlap.GetComponentInParent<IFirstPersonInteractable>();
                if (target == null)
                {
                    continue;
                }

                Vector3 toTarget = overlap.transform.position - origin;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.0001f)
                {
                    // Standing exactly on the marker: treat any forward as ok.
                    toTarget = forward;
                }

                toTarget.Normalize();
                float dot = Vector3.Dot(forward, toTarget);
                if (dot < veryNearConeDot)
                {
                    continue; // behind/side target must not win by proximity
                }

                if (dot > bestScore)
                {
                    bestScore = dot;
                    best = target;
                }
            }

            if (best != null)
            {
                Classify(best);
                return;
            }
        }
    }

    private bool TryResolveTarget(Collider collider)
    {
        IFirstPersonInteractable target =
            collider.GetComponentInParent<IFirstPersonInteractable>();
        if (target == null)
        {
            return false;
        }

        Classify(target);
        return true;
    }

    private void Classify(IFirstPersonInteractable target)
    {
        if (target.CanInteract())
        {
            _currentTarget = target;
        }
        else
        {
            _lastBlockedTarget = target;
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
