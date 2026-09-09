using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authoritative coordinator of the ARK-07 walkable interior (Phase 07
/// Checkpoint A).
///
/// 07A responsibilities only:
/// - deterministic spawn application (ShipSpawn_Default / FromSurface),
/// - ShipExplore input-mode semantics (cursor locked + hidden),
/// - the minimal ship HUD (compartment tag + gaze prompt + crosshair),
/// - registries of the four physical consoles, the sliding doors and the
///   airlock station (single source for tests and the HUD).
///
/// Reuses the proven Phase 06 first-person stack (SurfacePlayerController +
/// FirstPersonInteractor + IFirstPersonInteractable); no new movement or
/// interaction framework is introduced. Scene transition work (airlock -&gt;
/// surface, Boot migration) is later-checkpoint scope and stays out of 07A.
/// </summary>
public class ShipGameController : MonoBehaviour
{
    // Compartment zones match the deterministic ShipInteriorBuilder layout.
    // Chinese primary + restrained English technical metadata.
    private static readonly (string Zh, string Meta, float X0, float Z0, float X1, float Z1)[] Zones =
    {
        ("舰桥", "BRIDGE // OPS", -3.4f, 6.3f, 3.4f, 10.6f),
        ("出发准备", "MISSION PREPARATION", -5.5f, 0.3f, -1.1f, 6.3f),
        ("中央通道", "SPINE", -1.1f, 0.3f, 1.1f, 6.3f),
        ("资源加工", "RESOURCE PROCESSING", 1.1f, 0.3f, 5.5f, 6.3f),
        ("据点", "OUTPOST // COMMS", -5.5f, -9.4f, 5.5f, 0.3f),
        ("气闸舱", "AIRLOCK // EVA", -3.0f, -14.4f, 3.0f, -9.4f)
    };

    private SurfacePlayerController _player;
    private FirstPersonInteractor _interactor;
    private ShipHudController _hud;
    private ShipAirlockInteractable _airlock;
    private readonly List<ShipConsoleInteractable> _consoles = new List<ShipConsoleInteractable>();
    private readonly List<ShipDoorInteractable> _doors = new List<ShipDoorInteractable>();
    private bool _ready;

    public bool IsReady => _ready;
    public SurfacePlayerController Player => _player;
    public FirstPersonInteractor Interactor => _interactor;
    public ShipHudController Hud => _hud;
    public ShipAirlockInteractable Airlock => _airlock;
    public IReadOnlyList<ShipConsoleInteractable> Consoles => _consoles;
    public IReadOnlyList<ShipDoorInteractable> Doors => _doors;

    private void Start()
    {
        _player = Object.FindAnyObjectByType<SurfacePlayerController>();
        _interactor = Object.FindAnyObjectByType<FirstPersonInteractor>();
        _hud = Object.FindAnyObjectByType<ShipHudController>();

        _consoles.Clear();
        foreach (ShipConsoleInteractable console in Object.FindObjectsByType<ShipConsoleInteractable>())
        {
            _consoles.Add(console);
        }

        _consoles.Sort((a, b) => a.ConsoleId.CompareTo(b.ConsoleId));

        _doors.Clear();
        foreach (ShipDoorInteractable door in Object.FindObjectsByType<ShipDoorInteractable>())
        {
            _doors.Add(door);
        }

        _doors.Sort((a, b) => a.DoorId.CompareTo(b.DoorId));

        _airlock = Object.FindAnyObjectByType<ShipAirlockInteractable>();

        ApplySpawn(ShipSpawnMarker.DefaultId);
        ApplyShipExploreMode();
        if (_hud != null)
        {
            _hud.Bind(this);
        }

        _ready = true;
    }

    private void Update()
    {
        if (!_ready)
        {
            return;
        }

        RefreshHud();
    }

    private void RefreshHud()
    {
        if (_hud == null)
        {
            return;
        }

        // Compartment tag from the real player position (Chinese first).
        if (_player != null)
        {
            (string zh, string meta) zone = ResolveZone(_player.transform.position);
            _hud.SetZoneTag(zone.zh, zone.meta);
        }

        // Gaze prompt + crosshair through the same FirstPersonInteractor the
        // world E action uses.
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
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>Deterministic placement on the requested spawn marker.</summary>
    public bool ApplySpawn(string markerId)
    {
        if (_player == null)
        {
            _player = Object.FindAnyObjectByType<SurfacePlayerController>();
        }

        if (_player == null)
        {
            return false;
        }

        foreach (ShipSpawnMarker marker in Object.FindObjectsByType<ShipSpawnMarker>())
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
