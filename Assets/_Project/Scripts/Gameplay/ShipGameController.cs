using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Authoritative coordinator of the ARK-07 walkable interior (Phase 07B).
///
/// Phase 07B responsibilities on top of the 07A shell:
/// - deterministic spawn application (ShipSpawn_Default / FromSurface),
/// - explicit input modes (ShipExplore / console focus transitions / wrist)
///   through one small state authority - ShipConsoleFocusController owns the
///   console camera/blur/cursor machine, this controller owns exploration,
///   the wrist terminal and HUD suppression,
/// - the M / I wrist toggle with WristReturnMode.ShipExplore return context
///   (the wrist is NEVER allowed to stack on a focused physical console),
/// - the registries of the four physical consoles, the doors, the airlock and
///   the first-person stack reused from Phase 06.
/// </summary>
public class ShipGameController : MonoBehaviour, IWristContext
{
    // Compartment zones match the deterministic ShipInteriorBuilder layout.
    // Chinese primary + restrained English technical metadata.
    private static readonly (string Zh, string Meta, float X0, float Z0, float X1, float Z1)[] Zones =
    {
        ("舰桥", "BRIDGE // OPS", -3.4f, 6.3f, 3.4f, 10.6f),
        ("仓库", "WAREHOUSE // STORAGE", -5.5f, 0.3f, -1.1f, 6.3f),
        ("中央通道", "SPINE", -1.1f, 0.3f, 1.1f, 6.3f),
        ("资源加工", "RESOURCE PROCESSING", 1.1f, 0.3f, 5.5f, 6.3f),
        ("据点", "OUTPOST // COMMS", -5.5f, -9.4f, 5.5f, 0.3f),
        ("气闸舱", "AIRLOCK // EVA", -3.0f, -14.4f, 3.0f, -9.4f)
    };

    private static readonly SurfaceResourceNode[] EmptyNodes = new SurfaceResourceNode[0];

    private SurfacePlayerController _player;
    private FirstPersonInteractor _interactor;
    private ShipHudController _hud;
    private ShipAirlockInteractable _airlock;
    private ShipConsoleFocusController _focus;
    private ShipOverlayUi _overlayUi;
    private SurfaceWristController _wrist;
    private readonly List<ShipConsoleInteractable> _consoles = new List<ShipConsoleInteractable>();
    private readonly List<ShipDoorInteractable> _doors = new List<ShipDoorInteractable>();
    private bool _ready;
    private bool _isWristOpen;

    public bool IsReady => _ready;
    public SurfacePlayerController Player => _player;
    public FirstPersonInteractor Interactor => _interactor;
    public ShipHudController Hud => _hud;
    public ShipAirlockInteractable Airlock => _airlock;
    public ShipConsoleFocusController Focus => _focus;
    public SurfaceWristController Wrist => _wrist;
    public IReadOnlyList<ShipConsoleInteractable> Consoles => _consoles;
    public IReadOnlyList<ShipDoorInteractable> Doors => _doors;

    /// <summary>Wrist mode currently open inside the ship.</summary>
    public bool IsWristOpen => _isWristOpen;

    /// <summary>True while a console transition or focus is active (the wrist
    /// must never open on top of a physical terminal).</summary>
    public bool IsConsoleFocusActive => _focus != null && _focus.FocusActive;

    /// <summary>True while a shipboard overlay (deployment confirmation /
    /// loadout) is open - movement, world E and the wrist stay gated.</summary>
    public bool IsOverlayOpen => _overlayUi != null && _overlayUi.IsOpen;

    /// <summary>True while ANY modal interface is open (console focus, wrist
    /// or overlay) - used by HUD suppression and input gating.</summary>
    public bool IsAnyModalActive => IsConsoleFocusActive || _isWristOpen
                                    || IsOverlayOpen;

    // ---- IWristContext (Phase 07B) ----
    // Ship contexts expose no live surface nodes/navigation: the wrist shows
    // ship semantics (舰内 / 地表目标已记录 memory) instead.

    public WristReturnMode WristSourceMode => WristReturnMode.ShipExplore;

    public IReadOnlyList<SurfaceResourceNode> Nodes => EmptyNodes;

    public SurfaceResourceNode ActiveNode => null;

    public bool IsNavigationActive => false;

    public int NavigationTargetNodeId => -1;

    public bool NavigationReached => false;

    public float NavigationDistance => 0f;

    public SurfaceResourceNode GetNodeById(int nodeId)
    {
        return null;
    }

    public bool TrySelectNavigationTarget(int nodeId)
    {
        return false;
    }

    // Custom-accessor events (interface contract; the wrist subscribes).
    private Action<bool> _wristOpenChanged;
    private Action _navigationChanged;

    public event Action<bool> WristOpenChanged
    {
        add => _wristOpenChanged += value;
        remove => _wristOpenChanged -= value;
    }

    public event Action NavigationChanged
    {
        add => _navigationChanged += value;
        remove => _navigationChanged -= value;
    }

    private void Start()
    {
        _player = UnityEngine.Object.FindAnyObjectByType<SurfacePlayerController>();
        _interactor = UnityEngine.Object.FindAnyObjectByType<FirstPersonInteractor>();
        _hud = UnityEngine.Object.FindAnyObjectByType<ShipHudController>();
        _focus = UnityEngine.Object.FindAnyObjectByType<ShipConsoleFocusController>();
        _wrist = UnityEngine.Object.FindAnyObjectByType<SurfaceWristController>();
        _overlayUi = UnityEngine.Object.FindAnyObjectByType<ShipOverlayUi>(
            FindObjectsInactive.Include);

        _consoles.Clear();
        foreach (ShipConsoleInteractable console in UnityEngine.Object.FindObjectsByType<ShipConsoleInteractable>())
        {
            _consoles.Add(console);
        }

        _consoles.Sort((a, b) => a.ConsoleId.CompareTo(b.ConsoleId));

        _doors.Clear();
        foreach (ShipDoorInteractable door in UnityEngine.Object.FindObjectsByType<ShipDoorInteractable>())
        {
            _doors.Add(door);
        }

        _doors.Sort((a, b) => a.DoorId.CompareTo(b.DoorId));

        _airlock = UnityEngine.Object.FindAnyObjectByType<ShipAirlockInteractable>();

        // Phase 07B.2 focus composition: enforce the sharp-screen camera stack
        // at runtime. The scene authoring adds the overlay camera to the base
        // camera's URP stack, but that relationship is not guaranteed to
        // survive every scene serialization/load path; re-asserting it here is
        // idempotent and makes the always-on sharp console screens robust in
        // the Release player.
        EnsureScreenComposition();

        // Consume the routing reason (Default for normal boot, FromSurface for
        // the landing return) and place the player on the matching marker.
        SceneRouting.ShipEntry entry = SceneRouting.ConsumeShipEntry();
        string markerId = entry == SceneRouting.ShipEntry.FromSurface
            ? ShipSpawnMarker.FromSurfaceId
            : ShipSpawnMarker.DefaultId;
        ApplySpawn(markerId);

        if (_hud != null)
        {
            _hud.Bind(this);
        }

        // The ship wrist is the SAME wrist document/render-texture/radial/
        // cursor chain Phase 06 validated; only its content mode is ship.
        if (_wrist != null)
        {
            _wrist.Bind(this);
        }

        ApplyShipExploreMode();
        _ready = true;
        Debug.Log("[ShipGameController] ShipExplore ready entry=" + entry
                  + " spawn=" + markerId + " scene=" + gameObject.scene.name
                  + " focus=" + (_focus != null) + " wrist=" + (_wrist != null));
    }

    private void EnsureScreenComposition()
    {
        if (_player == null)
        {
            return;
        }

        Camera eye = _player.EyeCamera;
        if (eye == null)
        {
            return;
        }

        int layer = WorldConsoleSurface.ConsoleScreenLayer;
        Camera sharp = null;
        Transform sharpTransform = eye.transform.Find("SharpScreenCamera");
        if (sharpTransform != null)
        {
            sharp = sharpTransform.GetComponent<Camera>();
        }

        if (sharp == null)
        {
            Debug.LogError("[ShipGameController] SharpScreenCamera missing - "
                           + "console screens will not render");
            return;
        }

        // Environment base camera: post-processing ON, ConsoleScreen layer OUT.
        var eyeData = eye.GetComponent<UniversalAdditionalCameraData>();
        var sharpData = sharp.GetComponent<UniversalAdditionalCameraData>();
        if (eyeData == null)
        {
            eyeData = eye.gameObject.AddComponent<UniversalAdditionalCameraData>();
        }

        if (sharpData == null)
        {
            sharpData = sharp.gameObject.AddComponent<UniversalAdditionalCameraData>();
        }

        eye.cullingMask &= ~(1 << layer);
        eyeData.renderPostProcessing = true;
        eyeData.volumeLayerMask = ~0;
        eye.depth = 0f;

        // Sharp screen camera: independent second camera rendering ONLY the
        // ConsoleScreen layer after the (post-processed) environment frame;
        // a depth-only clear preserves the environment colors and the camera
        // has post-processing OFF, so physical screens stay crisp under DOF.
        sharp.cullingMask = 1 << layer;
        sharp.enabled = true;
        sharp.depth = 1f;
        sharp.clearFlags = CameraClearFlags.Depth;
        sharpData.renderType = CameraRenderType.Base;
        sharpData.renderPostProcessing = false;

        Debug.Log("[ShipGameController] screen composition enforced: baseMask="
                  + eye.cullingMask + " sharpMask=" + sharp.cullingMask
                  + " baseDepth=" + eye.depth + " sharpDepth=" + sharp.depth);
    }

    private void Update()
    {
        if (!_ready)
        {
            return;
        }

        PollWristKeys();

        if (IsAnyModalActive)
        {
            // A focused console, an overlay or the wrist is the only
            // interface; the HUD chrome stays suppressed (SetUiSuppressed
            // handles hiding).
            if (_hud != null)
            {
                _hud.SetUiSuppressed(true);
            }
        }
        else
        {
            if (_hud != null)
            {
                _hud.SetUiSuppressed(false);
            }

            RefreshHud();
        }

        if (_wrist != null)
        {
            _wrist.Tick();
        }
    }

    private void PollWristKeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (_isWristOpen)
        {
            if (keyboard.mKey.wasPressedThisFrame || keyboard.iKey.wasPressedThisFrame
                || keyboard.escapeKey.wasPressedThisFrame)
            {
                SetWristOpen(false);
            }

            return;
        }

        // M / I open the wrist ONLY from plain ShipExplore. While a console
        // is focused, a transition runs or an overlay is open, both keys must
        // do nothing (console/wrist/overlay never stack).
        if (IsConsoleFocusActive || IsOverlayOpen)
        {
            return;
        }

        if (keyboard.mKey.wasPressedThisFrame || keyboard.iKey.wasPressedThisFrame)
        {
            SetWristOpen(true);
        }
    }

    /// <summary>Wrist toggle used by tests and M/I input.</summary>
    public void ToggleWrist()
    {
        if (IsAnyModalActive && !_isWristOpen)
        {
            return;
        }

        SetWristOpen(!_isWristOpen);
    }

    public void SetWristOpen(bool open)
    {
        if (open == _isWristOpen)
        {
            return;
        }

        if (open && (IsConsoleFocusActive || IsOverlayOpen))
        {
            // Phase 07B: never stack Wrist on a focused physical console or
            // on a shipboard overlay.
            return;
        }

        _isWristOpen = open;

        if (_player != null)
        {
            _player.InputEnabled = !open;
        }

        if (_wrist != null)
        {
            _wrist.SetOpen(open);
        }

        if (_hud != null)
        {
            _hud.SetUiSuppressed(open);
        }

        ApplyCursorForMode();
        _wristOpenChanged?.Invoke(open);
    }

    private void ApplyCursorForMode()
    {
        if (_isWristOpen)
        {
            if (_wrist != null)
            {
                _wrist.EnterWristCursor();
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
        else
        {
            ApplyShipExploreMode();
        }
    }

    /// <summary>Called by the focus controller / overlay whenever a modal
    /// state changes (entry, focus reached, exit started/finished) so HUD /
    /// wrist gating stay consistent.</summary>
    public void HandleConsoleFocusChanged()
    {
        if (_hud != null)
        {
            _hud.SetUiSuppressed(IsAnyModalActive);
        }
    }

    private void RefreshHud()
    {
        if (_hud == null)
        {
            return;
        }

        if (_player != null)
        {
            (string zh, string meta) zone = ResolveZone(_player.transform.position);
            _hud.SetZoneTag(zone.zh, zone.meta);
        }

        if (_interactor != null)
        {
            string prompt = _interactor.ResolvePrompt() ?? "";
            _hud.SetPrompt(prompt);
            _hud.SetCrosshairState(_interactor.HasValidTarget
                ? ShipHudController.ShipHudCrosshairState.ValidTarget
                : (_interactor.HasBlockedTarget
                    ? ShipHudController.ShipHudCrosshairState.BlockedTarget
                    : ShipHudController.ShipHudCrosshairState.None));
        }
        else
        {
            _hud.SetPrompt("");
            _hud.SetCrosshairState(ShipHudController.ShipHudCrosshairState.None);
        }
    }

    /// <summary>ShipExplore mode: OS cursor locked + hidden (mouse look).</summary>
    public void ApplyShipExploreMode()
    {
        if (_wrist != null)
        {
            _wrist.ExitToSurfaceCursor();
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>Deterministic placement on the requested spawn marker.</summary>
    public bool ApplySpawn(string markerId)
    {
        if (_player == null)
        {
            _player = UnityEngine.Object.FindAnyObjectByType<SurfacePlayerController>();
        }

        if (_player == null)
        {
            return false;
        }

        foreach (ShipSpawnMarker marker in UnityEngine.Object.FindObjectsByType<ShipSpawnMarker>())
        {
            if (marker.MarkerId != markerId)
            {
                continue;
            }

            _player.TeleportTo(marker.transform.position);
            _player.SetFacing(marker.transform.eulerAngles.y, 0f);
            return true;
        }

        return false;
    }

    /// <summary>Chinese compartment name at a world position (empty = outside).</summary>
    public static string ResolveZoneZh(Vector3 position)
    {
        return ResolveZone(position).zh;
    }

    private static (string zh, string meta) ResolveZone(Vector3 position)
    {
        for (int i = 0; i < Zones.Length; i++)
        {
            (string zh, string meta, float x0, float z0, float x1, float z1) = Zones[i];
            if (position.x >= x0 && position.x <= x1 && position.z >= z0 && position.z <= z1)
            {
                return (zh, meta);
            }
        }

        return ("", "");
    }
}
