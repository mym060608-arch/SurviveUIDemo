using System;
using System.Collections.Generic;

/// <summary>
/// Minimal data source the shared wrist terminal needs (Phase 07B).
///
/// SurfaceGameController (surface scene) and ShipGameController (ship
/// interior) both drive the SAME SurfaceWristController-hosted wrist
/// document/render-texture/radial/cursor/pointer chain; only the content
/// context changes. A context tells the wrist which return mode to restore,
/// exposes whatever live surface/ship data exists and owns the
/// WristOpenChanged lifecycle event the wrist subscribes to.
/// </summary>
public interface IWristContext
{
    /// <summary>Which exploration mode the wrist was opened from.</summary>
    WristReturnMode WristSourceMode { get; }

    /// <summary>The player rig the wrist projects from (camera + yaw).</summary>
    SurfacePlayerController Player { get; }

    /// <summary>Live surface nodes. Ship contexts expose an empty list.</summary>
    IReadOnlyList<SurfaceResourceNode> Nodes { get; }

    /// <summary>Node currently harvesting/transmitting (null on ship).</summary>
    SurfaceResourceNode ActiveNode { get; }

    bool IsNavigationActive { get; }
    int NavigationTargetNodeId { get; }
    bool NavigationReached { get; }
    float NavigationDistance { get; }

    SurfaceResourceNode GetNodeById(int nodeId);

    /// <summary>Selects a surface navigation target through the owner
    /// controller (rejected on ship / depleted nodes).</summary>
    bool TrySelectNavigationTarget(int nodeId);

    /// <summary>Raised by the owner whenever the wrist opens or closes.</summary>
    event Action<bool> WristOpenChanged;

    /// <summary>Raised by the owner whenever scene navigation state changes.</summary>
    event Action NavigationChanged;
}
