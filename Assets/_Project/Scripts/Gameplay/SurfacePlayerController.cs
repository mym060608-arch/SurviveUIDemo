using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person WASD + mouse-look surface controller (Phase 06 Revision).
///
/// CharacterController-based ground movement with world bounds, gravity and
/// no jump. Horizontal look rotates the invisible body (yaw), vertical look
/// pitches the attached eye-level camera. No third-person capsule body is
/// rendered; the CharacterController stays as the invisible collision volume.
///
/// Input is centralized here: movement + mouse look only run while
/// InputEnabled (the wrist terminal mode disables it). The semantic methods
/// ApplyLook / MoveByInput / SetFacing are public so automation can drive the
/// exact same paths a human would with WASD / mouse without faking devices.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SurfacePlayerController : MonoBehaviour
{
    public float moveSpeed = 4.5f;
    public float boundaryHalfExtent = 16.5f;
    public float lookSensitivity = 0.12f; // degrees per mouse delta unit
    public float pitchLimitDegrees = 80f;
    public float eyeHeight = 1.65f;

    private CharacterController _controller;
    private Camera _eyeCamera;
    private Vector3 _velocity;
    private float _yawDegrees;
    private float _pitchDegrees;

    /// <summary>False while the wrist terminal is open (WASD/look frozen).</summary>
    public bool InputEnabled { get; set; } = true;

    public CharacterController Controller => _controller;
    public float YawDegrees => _yawDegrees;
    public float PitchDegrees => _pitchDegrees;
    public Camera EyeCamera => _eyeCamera;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _eyeCamera = GetComponentInChildren<Camera>(true);
        if (_eyeCamera == null)
        {
            _eyeCamera = Camera.main;
        }

        if (_eyeCamera != null)
        {
            _eyeCamera.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
        }
    }

    private void Update()
    {
        if (!InputEnabled)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            if (delta.sqrMagnitude > 0f)
            {
                ApplyLook(delta.x * lookSensitivity, -delta.y * lookSensitivity);
            }
        }

        Keyboard keyboard = Keyboard.current;
        Vector2 input = keyboard == null
            ? Vector2.zero
            : new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
        MoveByInput(input, Time.deltaTime);
    }

    /// <summary>Applies look deltas in degrees: horizontal on body yaw,
    /// vertical on eye pitch (clamped to +/-pitchLimitDegrees).</summary>
    public void ApplyLook(float deltaYaw, float deltaPitch)
    {
        _yawDegrees = Mathf.Repeat(_yawDegrees + deltaYaw, 360f);
        _pitchDegrees = Mathf.Clamp(_pitchDegrees + deltaPitch,
            -pitchLimitDegrees, pitchLimitDegrees);
        ApplyRotation();
    }

    /// <summary>Deterministic facing used by automation/probes (yaw 0 faces
    /// world +Z, which the map treats as north/up).</summary>
    public void SetFacing(float yawDegrees, float pitchDegrees)
    {
        _yawDegrees = Mathf.Repeat(yawDegrees, 360f);
        _pitchDegrees = Mathf.Clamp(pitchDegrees,
            -pitchLimitDegrees, pitchLimitDegrees);
        ApplyRotation();
    }

    /// <summary>Movement integration driven by Update with real keyboard
    /// input; public for semantic automation (no physical key presses).
    /// Input is camera/body-relative: x = strafe right (+), y = forward (+).</summary>
    public void MoveByInput(Vector2 input, float deltaTime)
    {
        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        // Facing-relative planar motion: project the body axes onto XZ so W
        // always follows the current horizontal forward after turning.
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 planarMove = right * input.x + forward * input.y;
        if (planarMove.sqrMagnitude > 1f)
        {
            planarMove.Normalize();
        }

        if (_controller.isGrounded)
        {
            _velocity.y = -1.5f;
        }
        else
        {
            _velocity.y += Physics.gravity.y * deltaTime;
        }

        Vector3 horizontal = planarMove * moveSpeed;
        _velocity.x = horizontal.x;
        _velocity.z = horizontal.z;

        Vector3 before = transform.position;
        _controller.Move(_velocity * deltaTime);

        Vector3 after = transform.position;
        after.x = Mathf.Clamp(after.x, -boundaryHalfExtent, boundaryHalfExtent);
        after.z = Mathf.Clamp(after.z, -boundaryHalfExtent, boundaryHalfExtent);
        if (after != before)
        {
            transform.position = after;
        }
    }

    private void ApplyRotation()
    {
        transform.rotation = Quaternion.Euler(0f, _yawDegrees, 0f);
        if (_eyeCamera != null)
        {
            _eyeCamera.transform.localRotation = Quaternion.Euler(_pitchDegrees, 0f, 0f);
        }
    }

    /// <summary>Deterministic placement helper (tests / probes).</summary>
    public void TeleportTo(Vector3 position)
    {
        if (_controller == null)
        {
            _controller = GetComponent<CharacterController>();
        }

        _controller.enabled = false;
        transform.position = position;
        _controller.enabled = true;
    }
}
