using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple WASD ground controller for the NEREID-04 survey unit (Phase 06).
///
/// CharacterController-based; no animation, no physics body. Facing follows
/// the movement direction. The playfield is clamped so the player cannot
/// wander infinitely.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SurfacePlayerController : MonoBehaviour
{
    public float moveSpeed = 4.5f;
    public float boundaryHalfExtent = 16.5f;

    private CharacterController _controller;
    private Vector3 _velocity;

    public CharacterController Controller => _controller;

    /// <summary>Last horizontal velocity in world space (m/s).</summary>
    public Vector3 Velocity => _velocity;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        Vector2 input = ReadMoveInput();
        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        Vector3 planarMove = new Vector3(input.x, 0f, input.y);

        // Apply simple gravity so the unit stays grounded on slopes.
        if (_controller.isGrounded)
        {
            _velocity.y = -1.5f;
        }
        else
        {
            _velocity.y += Physics.gravity.y * Time.deltaTime;
        }

        Vector3 horizontal = planarMove * moveSpeed;
        _velocity.x = horizontal.x;
        _velocity.z = horizontal.z;

        if (planarMove.sqrMagnitude > 0.001f)
        {
            // Face movement direction (smooth turn, no erratic spin).
            Quaternion target = Quaternion.LookRotation(planarMove, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, 720f * Time.deltaTime);
        }

        Vector3 before = transform.position;
        _controller.Move(_velocity * Time.deltaTime);

        Vector3 after = transform.position;
        after.x = Mathf.Clamp(after.x, -boundaryHalfExtent, boundaryHalfExtent);
        after.z = Mathf.Clamp(after.z, -boundaryHalfExtent, boundaryHalfExtent);
        if (after != before)
        {
            transform.position = after;
        }
    }

    /// <summary>WASD via the active Input System device.</summary>
    private static Vector2 ReadMoveInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return Vector2.zero;
        }

        float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        return new Vector2(x, z);
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
