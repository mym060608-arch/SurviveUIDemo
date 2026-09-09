using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authoritative data + world->wrist-pixel mapping for the ARK-07 ship
/// FLOORPLAN shown inside the wrist terminal central map while the player is
/// in the ship (Phase 07B.2).
///
/// The Phase 07B.2 human drawing is the authority:
///   solid line  = wall
///   dashed line = door
///   monitor icon = terminal
///   capsule / descent-pod icon = airlock
///   small ▲ = player
///
/// Topology (exactly four doors):
///   01 Bridge   bottom-wall HORIZONTAL door (Bridge <-> Central Spine)
///   02 Warehouse right/east wall VERTICAL door   (Warehouse <-> Central Spine)
///   03 Processing left/west wall VERTICAL door   (Central Spine <-> Processing)
///   04 Airlock   top-wall HORIZONTAL door        (Central Spine <-> Airlock)
///   Outpost is an OPEN station on the lower central spine - it has NO door
///   and is never drawn as an enclosed room.
///
/// Pure data/math only - no UI Toolkit dependency on purpose, so a future
/// hand-drawn art pass can replace the *renderer* (ShipSchematicVisual)
/// without touching terminal ids, door topology, player mapping or zone
/// semantics.
///
/// Deck convention (mirrors ShipGameController.Zones and ShipInteriorBuilder):
///   world XZ in meters, X -5.5..5.5, Z -14.4 (aft) .. 10.6 (forward).
///   +Z forward, drawn "up" on screen (UI y down, so py = originY - z*scale).
///
/// Pixel space: wrist canvas 1280 x 800, map disc centre (640,400), map
/// radius 235. Deck origin sits slightly ABOVE the disc centre (originY 352)
/// so the aft hull never collides with the ARK-07 identity footer.
/// </summary>
public enum ShipSchematicZone
{
    Bridge,
    Warehouse,
    Spine,
    Processing,
    Outpost,
    Airlock
}

/// <summary>One solid interior wall run (deck meters). The visual renders it
/// as a solid line. Wall runs never cross a DoorSegment span - the door
/// segments replace the matching part of the wall.</summary>
public sealed class ShipWallSegment
{
    public readonly int Id;
    public readonly Vector2 A;
    public readonly Vector2 B;

    public ShipWallSegment(int id, Vector2 a, Vector2 b)
    {
        Id = id;
        A = a;
        B = b;
    }

    /// <summary>Length in deck meters.</summary>
    public float Length => Vector2.Distance(A, B);
}

/// <summary>Door axis orientation (the drawing contract only allows these two).</summary>
public enum ShipDoorOrientation
{
    Horizontal,
    Vertical
}

/// <summary>One dashed door segment. It replaces an interrupted part of a
/// wall: e.g. the Bridge bottom wall between A and B, drawn dashed.</summary>
public sealed class ShipDoorSegment
{
    public readonly int Id;
    public readonly ShipSchematicZone From;
    public readonly ShipSchematicZone To;
    public readonly ShipDoorOrientation Orientation;
    public readonly Vector2 A;
    public readonly Vector2 B;

    public ShipDoorSegment(int id, ShipSchematicZone from, ShipSchematicZone to,
        ShipDoorOrientation orientation, Vector2 a, Vector2 b)
    {
        Id = id;
        From = from;
        To = to;
        Orientation = orientation;
        A = a;
        B = b;
    }

    public float Length => Vector2.Distance(A, B);
}

/// <summary>One physical/functional terminal. ConsoleId maps to the four
/// permanent consoles (01 总览 / 02 仓库 / 03 加工 / 04 据点).</summary>
public sealed class ShipTerminalMarker
{
    public readonly int ConsoleId;
    public readonly string Code;
    public readonly string Zh;
    public readonly Vector2 DeckPosition;
    public readonly ShipSchematicZone Zone;

    public ShipTerminalMarker(int consoleId, string code, string zh,
        Vector2 deckPosition, ShipSchematicZone zone)
    {
        ConsoleId = consoleId;
        Code = code;
        Zh = zh;
        DeckPosition = deckPosition;
        Zone = zone;
    }
}

/// <summary>The single airlock / descent-chamber marker (capsule icon, never
/// a monitor icon).</summary>
public sealed class ShipAirlockMarker
{
    public readonly Vector2 DeckPosition;

    public ShipAirlockMarker(Vector2 deckPosition)
    {
        DeckPosition = deckPosition;
    }
}

/// <summary>Room label drawn inside a zone (kept apart from gameplay data).</summary>
public sealed class ShipRoomLabel
{
    public readonly ShipSchematicZone Zone;
    public readonly string Code;
    public readonly string Zh;
    public readonly Vector2 DeckPosition;

    public ShipRoomLabel(ShipSchematicZone zone, string code, string zh,
        Vector2 deckPosition)
    {
        Zone = zone;
        Code = code;
        Zh = zh;
        DeckPosition = deckPosition;
    }
}

/// <summary>
/// Deterministic deck floorplan: hull ring, solid wall runs, exactly four
/// dashed door segments, four terminal markers, one airlock marker, room
/// labels. All coordinates are authoritative ship-deck meters;
/// ShipSchematicVisual turns them into hologram line art.
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

    // ====================================================================
    // Door topology - EXACTLY 4 segments (Phase 07B.2 authoritative drawing).
    // ====================================================================

    /// <summary>Deck positions of the four contract doors.</summary>
    public static readonly ShipDoorSegment[] Doors =
    {
        // Door 01: Bridge bottom wall, centre x, HORIZONTAL.
        new ShipDoorSegment(1, ShipSchematicZone.Bridge, ShipSchematicZone.Spine,
            ShipDoorOrientation.Horizontal,
            new Vector2(-0.9f, 6.3f), new Vector2(0.9f, 6.3f)),
        // Door 02: Warehouse RIGHT / EAST wall (x = -1.1), VERTICAL.
        new ShipDoorSegment(2, ShipSchematicZone.Warehouse, ShipSchematicZone.Spine,
            ShipDoorOrientation.Vertical,
            new Vector2(-1.1f, 2.7f), new Vector2(-1.1f, 4.1f)),
        // Door 03: Processing LEFT / WEST wall (x = +1.1), VERTICAL.
        new ShipDoorSegment(3, ShipSchematicZone.Spine, ShipSchematicZone.Processing,
            ShipDoorOrientation.Vertical,
            new Vector2(1.1f, 2.7f), new Vector2(1.1f, 4.1f)),
        // Door 04: Airlock top wall, centre x, HORIZONTAL.
        new ShipDoorSegment(4, ShipSchematicZone.Spine, ShipSchematicZone.Airlock,
            ShipDoorOrientation.Horizontal,
            new Vector2(-0.8f, -9.4f), new Vector2(0.8f, -9.4f))
    };

    // ====================================================================
    // Solid interior walls (deck meters). Wall runs stop at door spans; the
    // dashed DoorSegments above replace the interrupted wall parts.
    // ====================================================================

    public static readonly ShipWallSegment[] Walls =
    {
        // Bridge aft band z = 6.3, flanking the bridge bottom door (-0.9..0.9).
        // The run from x -5.5..-1.1 doubles as the Warehouse top wall and the
        // x 1.1..5.5 run doubles as the Processing top wall (same band).
        new ShipWallSegment(1, new Vector2(-5.5f, 6.3f), new Vector2(-0.9f, 6.3f)),
        new ShipWallSegment(2, new Vector2(0.9f, 6.3f), new Vector2(5.5f, 6.3f)),
        // Side-room bottom walls at z = 0.3 (central mouth stays open: Outpost
        // is an open station on the lower spine, NOT an enclosed room).
        new ShipWallSegment(3, new Vector2(-5.5f, 0.3f), new Vector2(-1.1f, 0.3f)),
        new ShipWallSegment(4, new Vector2(1.1f, 0.3f), new Vector2(5.5f, 0.3f)),
        // Warehouse right/east wall x = -1.1, split around the vertical door.
        new ShipWallSegment(5, new Vector2(-1.1f, 6.3f), new Vector2(-1.1f, 4.1f)),
        new ShipWallSegment(6, new Vector2(-1.1f, 2.7f), new Vector2(-1.1f, 0.3f)),
        // Processing left/west wall x = +1.1, split around the vertical door.
        new ShipWallSegment(7, new Vector2(1.1f, 6.3f), new Vector2(1.1f, 4.1f)),
        new ShipWallSegment(8, new Vector2(1.1f, 2.7f), new Vector2(1.1f, 0.3f)),
        // Bridge side walls.
        new ShipWallSegment(9, new Vector2(-3.4f, 6.3f), new Vector2(-3.4f, ZMax)),
        new ShipWallSegment(10, new Vector2(3.4f, 6.3f), new Vector2(3.4f, ZMax)),
        // Airlock bulkhead z = -9.4, flanking the airlock top door (-0.8..0.8).
        new ShipWallSegment(11, new Vector2(-5.5f, -9.4f), new Vector2(-0.8f, -9.4f)),
        new ShipWallSegment(12, new Vector2(0.8f, -9.4f), new Vector2(5.5f, -9.4f)),
        // Airlock chamber side walls.
        new ShipWallSegment(13, new Vector2(-3.0f, -9.4f), new Vector2(-3.0f, ZMin)),
        new ShipWallSegment(14, new Vector2(3.0f, -9.4f), new Vector2(3.0f, ZMin))
    };

    // ====================================================================
    // Terminal + airlock + room labels (canonical deck positions).
    // ====================================================================

    /// <summary>The four permanent terminal markers (monitor icon). No
    /// Preparation terminal exists anymore (Console02 = 仓库总览).</summary>
    public static readonly ShipTerminalMarker[] Terminals =
    {
        new ShipTerminalMarker(1, "01 总览", "总览", new Vector2(0.0f, 8.7f),
            ShipSchematicZone.Bridge),
        new ShipTerminalMarker(2, "02 仓库", "仓库", new Vector2(-3.3f, 3.9f),
            ShipSchematicZone.Warehouse),
        new ShipTerminalMarker(3, "03 加工", "加工", new Vector2(3.3f, 3.9f),
            ShipSchematicZone.Processing),
        new ShipTerminalMarker(4, "04 据点", "据点", new Vector2(0.0f, -4.4f),
            ShipSchematicZone.Outpost)
    };

    /// <summary>The single airlock / descent-chamber marker (capsule icon).</summary>
    public static readonly ShipAirlockMarker Airlock =
        new ShipAirlockMarker(new Vector2(0.0f, -12.1f));

    /// <summary>Room labels (large zone identity, small technical code).</summary>
    public static readonly ShipRoomLabel[] RoomLabels =
    {
        new ShipRoomLabel(ShipSchematicZone.Bridge, "BRIDGE // OPS", "舰桥",
            new Vector2(0.0f, 9.7f)),
        new ShipRoomLabel(ShipSchematicZone.Warehouse, "WAREHOUSE", "仓库",
            new Vector2(-3.3f, 5.5f)),
        new ShipRoomLabel(ShipSchematicZone.Processing, "PROCESSING", "加工",
            new Vector2(3.3f, 5.5f)),
        new ShipRoomLabel(ShipSchematicZone.Spine, "SPINE", "中央通道",
            new Vector2(0.0f, 1.2f)),
        new ShipRoomLabel(ShipSchematicZone.Outpost, "OUTPOST // COMMS", "据点",
            new Vector2(0.0f, -7.0f)),
        new ShipRoomLabel(ShipSchematicZone.Airlock, "AIRLOCK // EVA", "气闸",
            new Vector2(0.0f, -13.6f))
    };

    /// <summary>Deck hull ring (closed, deck meters, forward at top). The
    /// outline follows the real graybox: a tapered bow over the bridge
    /// (x +-3.4 above z 6.3), full-width mid/aft hull, narrowed airlock tail.
    /// Drawn as a straight polyline through these points.</summary>
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

    /// <summary>Deck meters -> absolute wrist canvas pixels.</summary>
    public static Vector2 DeckToPixel(float x, float z)
    {
        return new Vector2(
            OriginPixel.x + x * PxPerMeter,
            OriginPixel.y - z * PxPerMeter);
    }

    public static Vector2 DeckToPixel(Vector2 deck)
    {
        return DeckToPixel(deck.x, deck.y);
    }

    /// <summary>World position (player/objects) -> absolute wrist pixels.</summary>
    public static Vector2 WorldToSchematicPixel(Vector3 worldPosition)
    {
        return DeckToPixel(worldPosition.x, worldPosition.z);
    }

    /// <summary>Terminal anchor in wrist canvas pixels.</summary>
    public static Vector2 TerminalPixel(int consoleId)
    {
        for (int i = 0; i < Terminals.Length; i++)
        {
            if (Terminals[i].ConsoleId == consoleId)
            {
                return DeckToPixel(Terminals[i].DeckPosition);
            }
        }

        return Vector2.zero;
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

    /// <summary>Every hull/deck outline point stays inside the map disc.</summary>
    public static bool HullFitsMapDisc(float mapRadius, float paddingPx)
    {
        for (int i = 0; i < HullRing.Length; i++)
        {
            if (!IsPixelInsideMapDisc(DeckToPixel(HullRing[i]), mapRadius, paddingPx))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Every wall endpoint + door endpoint + terminal + airlock +
    /// room-label anchor stays inside the map disc (whole floorplan fits).</summary>
    public static bool FloorplanFitsMapDisc(float mapRadius, float paddingPx)
    {
        for (int i = 0; i < Walls.Length; i++)
        {
            if (!IsPixelInsideMapDisc(DeckToPixel(Walls[i].A), mapRadius, paddingPx)
                || !IsPixelInsideMapDisc(DeckToPixel(Walls[i].B), mapRadius, paddingPx))
            {
                return false;
            }
        }

        for (int i = 0; i < Doors.Length; i++)
        {
            if (!IsPixelInsideMapDisc(DeckToPixel(Doors[i].A), mapRadius, paddingPx)
                || !IsPixelInsideMapDisc(DeckToPixel(Doors[i].B), mapRadius, paddingPx))
            {
                return false;
            }
        }

        for (int i = 0; i < Terminals.Length; i++)
        {
            if (!IsPixelInsideMapDisc(DeckToPixel(Terminals[i].DeckPosition),
                mapRadius, paddingPx + 8f))
            {
                return false;
            }
        }

        if (!IsPixelInsideMapDisc(DeckToPixel(Airlock.DeckPosition),
            mapRadius, paddingPx + 8f))
        {
            return false;
        }

        for (int i = 0; i < RoomLabels.Length; i++)
        {
            if (!IsPixelInsideMapDisc(DeckToPixel(RoomLabels[i].DeckPosition),
                mapRadius, paddingPx))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Door by id (1..4), null when absent.</summary>
    public static ShipDoorSegment DoorById(int id)
    {
        for (int i = 0; i < Doors.Length; i++)
        {
            if (Doors[i].Id == id)
            {
                return Doors[i];
            }
        }

        return null;
    }

    /// <summary>True when the door segment is horizontal in deck space
    /// (its span runs along X, like the Bridge bottom wall / Airlock top
    /// wall doors).</summary>
    public static bool IsHorizontal(ShipDoorSegment door)
    {
        return door.Orientation == ShipDoorOrientation.Horizontal;
    }
}
