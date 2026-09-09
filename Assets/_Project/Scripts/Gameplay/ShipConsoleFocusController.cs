using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Physical console focus controller (Phase 07B).
///
/// Owns the explicit console mode machine:
///
///   ShipExplore -> (E on console) -> TransitionIn -> ShipConsoleFocus
///   ShipConsoleFocus -> Esc -> TransitionOut -> ShipExplore
///
/// During the ~0.26 s transition the eye camera smoothly moves from the exact
/// pose captured at E to the console's deterministic ConsoleFocusAnchor and
/// back again on exit (no hardcoded direction), the URP Depth-Of-Field volume
/// weight fades in/out (environment soft, screen at the focus distance stays
/// sharp), and the OS cursor is released only when the camera has reached the
/// focused pose. The player capsule never moves and its collider stays valid.
/// </summary>
public class ShipConsoleFocusController : MonoBehaviour
{
    public enum ShipConsoleFocusState
    {
        ShipExplore,
        TransitionIn,
        ShipConsoleFocus,
        TransitionOut
    }

    /// <summary>Recommended transition duration (contract: 0.22-0.30 s).</summary>
    public float transitionDuration = 0.26f;

    /// <summary>Maximum DOF influence while focused (restrained).</summary>
    [Range(0.5f, 1f)]
    public float focusBlurInfluence = 1f;

    private ShipConsoleFocusState _state = ShipConsoleFocusState.ShipExplore;
    private WorldConsoleSurface _active;
    private SurfacePlayerController _player;
    private Camera _eyeCamera;
    private Volume _blurVolume;
    private WristCursorService _cursor;
    private ShipGameController _ship;

    // Transition bookkeeping (single deterministic state).
    private float _transitionStart;
    private float _transitionDuration;
    private Vector3 _entryPosition;
    private Quaternion _entryRotation;
    private Vector3 _anchorPosition;
    private Quaternion _anchorRotation;

    public ShipConsoleFocusState State => _state;

    /// <summary>True during any transition or the focused state (input gating).</summary>
    public bool FocusActive => _state != ShipConsoleFocusState.ShipExplore;

    /// <summary>Console whose terminal is focused (null while exploring).</summary>
    public WorldConsoleSurface ActiveSurface => _active;

    /// <summary>0..1 of the running transition (0 outside transitions).</summary>
    public float TransitionProgress01 { get; private set; }

    public ShipGameController Ship => _ship;

    /// <summary>Console-focus DOF volume (weight animated by this controller).</summary>
    public Volume BlurVolume => _blurVolume;

    /// <summary>Current blur influence 0..1 (volume weight).</summary>
    public float BlurWeight => _blurVolume != null ? _blurVolume.weight : 0f;

    /// <summary>The eye camera that the focus transition moves.</summary>
    public Camera EyeCamera => _eyeCamera;

    /// <summary>Console-mode cursor authority (same dot / dot+ring visual
    /// language as the wrist; no trails, no overlay).</summary>
    public WristCursorService CursorService => _cursor;

    /// <summary>Distance from the screen centre along its front normal used by
    /// the authored anchors (matches a ~70% viewport fill at FOV 70).</summary>
    public static float FocusDistanceForScreen(float screenWidthMeters)
    {
        // horizontal tan = tan(vFOV/2) * aspect(16:9)
        const float horizontalTan = 1.2448f;
        float fill = 0.70f;
        return screenWidthMeters / (fill * 2f * horizontalTan);
    }

    private void Awake()
    {
        _cursor = new WristCursorService();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (_blurVolume != null)
        {
            _blurVolume.weight = 0f;
        }
    }

    /// <summary>
    /// Re-resolves the player / eye camera / DOF volume. Scene-object enable
    /// order is not guaranteed across regenerated scenes, so a single OnEnable
    /// cache may capture a null reference before another object's Awake ran.
    /// Every entry path re-runs this, which is safe after the scene is ready.
    /// </summary>
    private void ResolveReferences()
    {
        if (_ship == null)
        {
            _ship = Object.FindAnyObjectByType<ShipGameController>();
        }

        if (_player == null)
        {
            _player = Object.FindAnyObjectByType<SurfacePlayerController>();
        }

        if (_eyeCamera == null && _player != null)
        {
            _eyeCamera = _player.EyeCamera;
        }

        if (_eyeCamera == null)
        {
            _eyeCamera = Camera.main;
        }

        if (_blurVolume == null)
        {
            _blurVolume = Object.FindAnyObjectByType<Volume>();
        }
    }

    private void OnDisable()
    {
        if (_blurVolume != null)
        {
            _blurVolume.weight = 0f;
        }

        if (_player != null)
        {
            _player.InputEnabled = true;
        }

        if (_cursor != null)
        {
            _cursor.ExitToSurface();
        }

        _state = ShipConsoleFocusState.ShipExplore;
        _active = null;
    }

    private void Update()
    {
        switch (_state)
        {
            case ShipConsoleFocusState.TransitionIn:
                TickTransitionIn();
                break;
            case ShipConsoleFocusState.ShipConsoleFocus:
                TickFocused();
                break;
            case ShipConsoleFocusState.TransitionOut:
                TickTransitionOut();
                break;
        }
    }

    // ---- Mode entry / exit ----

    /// <summary>E on a console: capture the current view and move the camera
    /// smoothly to that console's focus anchor.</summary>
    public bool RequestEnter(ShipConsoleInteractable console)
    {
        if (_state != ShipConsoleFocusState.ShipExplore)
        {
            return false;
        }

        // Scene enable order is not deterministic across regenerated scenes:
        // re-resolve the player/eye/volume cache so E never fails because an
        // OnEnable-time lookup ran before another object's Awake.
        ResolveReferences();

        WorldConsoleSurface surface = console != null
            ? console.GetComponent<WorldConsoleSurface>()
            : null;
        if (surface == null || surface.FocusAnchor == null || _eyeCamera == null)
        {
            Debug.LogWarning("[ShipConsoleFocus] console " + (console != null
                             ? console.ConsoleId.ToString() : "?")
                             + " focus request rejected: surface=" + (surface != null)
                             + " anchor=" + (surface != null && surface.FocusAnchor != null)
                             + " eyeCamera=" + (_eyeCamera != null)
                             + " player=" + (_player != null));
            return false;
        }

        // Capture the exact pre-focus view (never a hardcoded direction).
        _entryPosition = _eyeCamera.transform.position;
        _entryRotation = _eyeCamera.transform.rotation;
        Transform anchor = surface.FocusAnchor;
        _anchorPosition = anchor.position;
        _anchorRotation = anchor.rotation;
        _active = surface;
        _state = ShipConsoleFocusState.TransitionIn;
        _transitionStart = Time.unscaledTime;
        _transitionDuration = Mathf.Max(0.05f, transitionDuration);
        if (_player != null)
        {
            _player.InputEnabled = false; // freeze WASD + mouse look
        }

        if (_ship != null)
        {
            _ship.HandleConsoleFocusChanged();
        }

        Debug.Log("[ShipConsoleFocus] entering console " + surface.ConsoleId
                  + " duration=" + _transitionDuration);
        return true;
    }

    /// <summary>Esc (or explicit request) leaves the focused terminal.</summary>
    public void RequestExit()
    {
        if (_state == ShipConsoleFocusState.TransitionIn
            || _state == ShipConsoleFocusState.ShipConsoleFocus)
        {
            _state = ShipConsoleFocusState.TransitionOut;
            _transitionStart = Time.unscaledTime;
            // Hide + lock the cursor before movement is restored.
            ApplyShipExploreCursor();
            if (_cursor != null)
            {
                _cursor.SetHover(false);
            }

            if (_ship != null)
            {
                _ship.HandleConsoleFocusChanged();
            }
        }
    }

    // ---- Update ticks ----

    private void TickTransitionIn()
    {
        float elapsed = Time.unscaledTime - _transitionStart;
        float t = Mathf.Clamp01(elapsed / _transitionDuration);
        float smooth = Mathf.SmoothStep(0f, 1f, t);
        ApplyCameraPose(Vector3.Lerp(_entryPosition, _anchorPosition, smooth),
            Quaternion.Slerp(_entryRotation, _anchorRotation, smooth));
        TransitionProgress01 = smooth;
        SetBlurWeight(smooth * focusBlurInfluence);

        if (t >= 1f)
        {
            _state = ShipConsoleFocusState.ShipConsoleFocus;
            ApplyConsoleCursor();
            Debug.Log("[ShipConsoleFocus] focused console "
                      + (_active != null ? _active.ConsoleId.ToString() : "?"));
        }
    }

    private void TickFocused()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            RequestExit();
        }
    }

    private void TickTransitionOut()
    {
        float elapsed = Time.unscaledTime - _transitionStart;
        float t = Mathf.Clamp01(elapsed / _transitionDuration);
        float smooth = Mathf.SmoothStep(0f, 1f, t);
        ApplyCameraPose(Vector3.Lerp(_anchorPosition, _entryPosition, smooth),
            Quaternion.Slerp(_anchorRotation, _entryRotation, smooth));
        TransitionProgress01 = 1f - smooth;
        SetBlurWeight((1f - smooth) * focusBlurInfluence);

        if (t >= 1f)
        {
            _state = ShipConsoleFocusState.ShipExplore;
            _active = null;
            TransitionProgress01 = 0f;
            if (_player != null)
            {
                _player.InputEnabled = true; // WASD + mouse look restored
            }

            ApplyShipExploreCursor();
            Debug.Log("[ShipConsoleFocus] back to ShipExplore");
        }
    }

    private void ApplyCameraPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (_eyeCamera != null)
        {
            _eyeCamera.transform.SetPositionAndRotation(worldPosition, worldRotation);
        }
    }

    private void SetBlurWeight(float weight)
    {
        if (_blurVolume != null)
        {
            _blurVolume.weight = Mathf.Clamp01(weight);
        }
    }

    // ---- Cursor semantics ----

    private void ApplyConsoleCursor()
    {
        if (_cursor != null)
        {
            _cursor.EnterWristMode(); // unlocked + visible, dot cursor
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void ApplyShipExploreCursor()
    {
        if (_cursor != null)
        {
            _cursor.ExitToSurface(); // locked + hidden
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
