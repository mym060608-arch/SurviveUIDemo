using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authoritative data + world->wrist-pixel mapping for the ARK-07 ship
/// schematic shown inside the wrist terminal central map while the player is
/// in the ship (Phase 07B.1). Pure data/math only - no UI Toolkit dependency
/// on purpose, so a future hand-drawn art pass can replace the *renderer*
/// (ShipSchematicVisual) without touching layout semantics.
///
/// Deck convention (mirrors ShipGameController.Zones and ShipInteriorBuilder):
///   world XZ in meters, X -5.5..5.5, Z -14.4 (aft) .. 10.6 (forward).
///   +Z forward, drawn "up" on screen (UI y down, so py = originY - z*scale).
///
/// Pixel space: the wrist canvas is 1280 x 800, map disc centre (640,400) and
/// map radius 235. The schematic is centered slightly ABOVE the disc centre
/// (originY 352) so the plan ends around y ~ 525 and never collides with the
/// ARK-07 identity footer (Round 9.2.2 lower band, y 564+).
/// </summary>
public sealed class ShipSchematicNode
{
    public int NodeId;        // 0..4 stable schematic node index
    public string Code;       // short English code, e.g. BRIDGE
    public string Meta;       // technical metadata, e.g. BRIDGE // OPS
    public string Zh;         // Chinese primary identity, e.g. 舰桥
    public int ConsoleId;     // physical console 1..4 (0 = no console)
    public float AnchorX;     // deck x (m)
    public float AnchorZ;     // deck z (m)

    public ShipSchematicNode(int nodeId, string code, string meta, string zh,
        int consoleId, float anchorX, float anchorZ)
    {
        NodeId = nodeId;
        Code = code;
        Meta = meta;
        Zh = zh;
        ConsoleId = consoleId;
        AnchorX = anchorX;
        AnchorZ = anchorZ;
    }
}

/// <summary>
/// Deterministic deck layout: hull ring, interior divider segments and the
/// five functional nodes (Bridge / Mission Preparation / Processing /
/// Outpost / Airlock). All coordinates are authoritative ship-deck meters;
/// ShipSchematicVisual turns them into hologram lines/chips.
/// </summary>
public static class ShipSchematicLayout
{
    // ---- Deck bounds (mirror ShipInteriorBuilder clear-space) ----
    public const float XMin = -5.5f;
    public const float XMax = 5.5f;
    public const float ZMin = -14.4f; // aft
    public const float ZMax = 10.6f;  // forward

    // ---- Wrist pixel mapping ----
    // px = OriginPx + x * PxPerMeter ; py = OriginPy - z * PxPerMeter
    public const float PxPerMeter = 12f;
    public static readonly Vector2 OriginPixel = new Vector2(640f, 352f);

    /// <summary>Stable five-node roster (Bridge first, then consoles 2-4,
    /// then the airlock). Contract order: Bridge / Preparation / Processing /
    /// Outpost / Airlock.</summary>
    public static readonly ShipSchematicNode[] Nodes =
    {
        new ShipSchematicNode(0, "BRIDGE", "BRIDGE // OPS", "舰桥", 1, 0f, 8.2f),
        new ShipSchematicNode(1, "PREP", "MISSION PREPARATION", "出发准备", 2, -3.3f, 3.3f),
        new ShipSchematicNode(2, "FAB", "RESOURCE PROCESSING", "资源加工", 3, 3.3f, 3.3f),
        new ShipSchematicNode(3, "OUTPOST", "OUTPOST // COMMS", "据点", 4, 0f, -4.6f),
        new ShipSchematicNode(4, "AIRLOCK", "AIRLOCK // EVA", "气闸舱", 0, 0f, -11.9f)
    };

    /// <summary>Deck hull ring (closed, deck meters, forward at top). The
    /// outline follows the real graybox: a tapered bow over the bridge
    /// (x +-3.4 above z 6.3), full-width mid/aft hull, narrowed airlock tail.
    /// Drawn as straight polyline segments through these points.</summary>
    public static readonly Vector2[] HullRing =
    {
        new Vector2(-3.4f, 10.6f),
        new Vector2(3.4f, 10.6f),
        new Vector2(5.5f, 6.3f),
        new Vector2(5.5f, -9.4f),
        new Vector2(3.0f, -9.4f),
        new Vector2(3.0f, -14.4f),
        new Vector2(-3.0f, -14.4f),
        new Vector2(-3.0f, -9.4f),
        new Vector2(-5.5f, -9.4f),
        new Vector2(-5.5f, 6.3f)
    };

    /// <summary>Interior divider segments (deck meters) - compartments and
    /// door mouths follow ShipInteriorBuilder walls. Pairs of world points.
    /// Deliberately simplified: single hairlines per wall band.</summary>
    public static readonly (Vector2 A, Vector2 B)[] Dividers =
    {
        // Bridge aft band z 6.3 (x from bridge sides outward).
        (new Vector2(-5.5f, 6.3f), new Vector2(-0.9f, 6.3f)),
        (new Vector2(0.9f, 6.3f), new Vector2(5.5f, 6.3f)),
        // Prep | spine | processing columns (z 0.3..6.3).
        (new Vector2(-1.1f, 0.3f), new Vector2(-1.1f, 6.3f)),
        (new Vector2(1.1f, 0.3f), new Vector2(1.1f, 6.3f)),
        // Mid band aft wall z 0.3 (central corridor mouth stays open).
        (new Vector2(-5.5f, 0.3f), new Vector2(-1.1f, 0.3f)),
        (new Vector2(1.1f, 0.3f), new Vector2(5.5f, 0.3f)),
        // Outpost -> airlock bulkhead z -9.4 (door mouth x -0.8..0.8).
        (new Vector2(-5.5f, -9.4f), new Vector2(-0.8f, -9.4f)),
        (new Vector2(0.8f, -9.4f), new Vector2(5.5f, -9.4f))
    };

    /// <summary>Deck meters -> absolute wrist canvas pixels.</summary>
    public static Vector2 DeckToPixel(float x, float z)
    {
        return new Vector2(
            OriginPixel.x + x * PxPerMeter,
            OriginPixel.y - z * PxPerMeter);
    }

    /// <summary>World position (player/objects) -> absolute wrist pixels.</summary>
    public static Vector2 WorldToSchematicPixel(Vector3 worldPosition)
    {
        return DeckToPixel(worldPosition.x, worldPosition.z);
    }

    /// <summary>Node anchor in wrist canvas pixels (chip centre).</summary>
    public static Vector2 NodePixel(int nodeId)
    {
        ShipSchematicNode node = NodeById(nodeId);
        return node != null ? DeckToPixel(node.AnchorX, node.AnchorZ) : Vector2.zero;
    }

    public static ShipSchematicNode NodeById(int nodeId)
    {
        for (int i = 0; i < Nodes.Length; i++)
        {
            if (Nodes[i].NodeId == nodeId)
            {
                return Nodes[i];
            }
        }

        return null;
    }

    /// <summary>True when a canvas pixel lies inside the wrist central map
    /// disc with the given safety padding (px).</summary>
    public static bool IsPixelInsideMapDisc(Vector2 pixel, float mapRadius,
        float paddingPx)
    {
        Vector2 delta = pixel - RadialWristElement.Center;
        return delta.sqrMagnitude <= (mapRadius - paddingPx)
            * (mapRadius - paddingPx);
    }

    /// <summary>Every hull/deck point (converted to canvas pixels) stays
    /// inside the map disc - used by the structural schematic test.</summary>
    public static bool HullFitsMapDisc(float mapRadius, float paddingPx)
    {
        for (int i = 0; i < HullRing.Length; i++)
        {
            Vector2 px = DeckToPixel(HullRing[i].x, HullRing[i].y);
            if (!IsPixelInsideMapDisc(px, mapRadius, paddingPx))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when all five node chips fit the map disc (chip is
    /// centered on the anchor; only its centre + half diagonal are checked
    /// with a generous padding).</summary>
    public static bool NodesFitMapDisc(float mapRadius, float chipRadiusPx,
        float paddingPx)
    {
        for (int i = 0; i < Nodes.Length; i++)
        {
            Vector2 px = NodePixel(i);
            if (!IsPixelInsideMapDisc(px, mapRadius, paddingPx + chipRadiusPx))
            {
                return false;
            }
        }

        return true;
    }
}
