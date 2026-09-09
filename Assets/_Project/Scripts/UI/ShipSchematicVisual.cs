using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Line-art ARK-07 ship FLOORPLAN (Phase 07B.2) drawn in the wrist central
/// map while the player is inside the ship.
///
/// ShipSchematicLayout owns the DATA (hull ring, solid wall runs, the exact
/// four dashed door segments, terminal markers, airlock marker, room labels,
/// deck->pixel mapping). This element only RENDERS that data:
///   - hull outline polyline (subtle),
///   - SOLID lines for every WallSegment,
///   - DASHED lines for every DoorSegment (the door replaces the wall part),
///   - a small monitor pictogram per TerminalMarker (01 总览 / 02 仓库 /
///     03 加工 / 04 据点) with a Chinese label below,
///   - a capsule / descent-pod pictogram for the airlock (never a monitor),
///   - restrained room labels,
///   - a live player marker (raised ▲) that follows the real ship position.
///
/// Because the data and the rendering are separate, a later hand-drawn-art
/// pass can replace this class (or its drawing body) while
/// ShipSchematicLayout and the wrist integration keep working unchanged.
///
/// The element fills the whole 1280 x 800 wrist canvas; the labels/markers
/// are absolute-positioned children at authoritative schematic pixels.
/// </summary>
public sealed class ShipSchematicVisual : VisualElement
{
    public const float CanvasWidth = 1280f;
    public const float CanvasHeight = 800f;

    // Hologram palette (matches the wrist language; never opaque white).
    private static readonly Color32 HullColor =
        new Color32(168, 198, 206, 170);
    private static readonly Color32 WallColor =
        new Color32(178, 206, 212, 235);
    private static readonly Color32 DoorColor =
        new Color32(178, 206, 212, 235);
    private static readonly Color32 IconColor =
        new Color32(236, 249, 251, 245);
    private static readonly Color32 IconFill =
        new Color32(14, 127, 140, 150);
    private static readonly Color32 AirlockColor =
        new Color32(236, 249, 251, 245);
    private static readonly Color32 PlayerColor =
        new Color32(235, 250, 252, 255);
    private static readonly Color32 LabelColor =
        new Color32(216, 236, 240, 255);
    private static readonly Color32 MetaColor =
        new Color32(150, 182, 190, 235);

    private readonly Label _playerMarker;
    private bool _playerVisible;
    private Vector2 _lastPlayerPixel = new Vector2(float.NaN, float.NaN);
    private float _lastPlayerRotateDeg;

    public ShipSchematicVisual()
    {
        name = "ShipSchematicVisual";
        pickingMode = PickingMode.Ignore;

        style.position = Position.Absolute;
        style.left = 0f;
        style.top = 0f;
        style.width = CanvasWidth;
        style.height = CanvasHeight;

        generateVisualContent += GenerateVisual;

        BuildTerminalMarkers();
        BuildAirlockMarker();
        BuildRoomLabels();

        // ---- Live player marker (raised triangle follows the player) ----
        _playerMarker = new Label
        {
            name = "ShipSchematicPlayer",
            text = "\u25B2" // ▲
        };
        _playerMarker.pickingMode = PickingMode.Ignore;
        _playerMarker.style.position = Position.Absolute;
        _playerMarker.style.width = 18f;
        _playerMarker.style.height = 18f;
        _playerMarker.style.unityTextAlign = TextAnchor.MiddleCenter;
        _playerMarker.style.fontSize = 13f;
        _playerMarker.style.color = new StyleColor(PlayerColor);
        _playerMarker.style.unityFontStyleAndWeight = FontStyle.Bold;
        Add(_playerMarker);
        SetPlayerVisible(false);
    }

    /// <summary>True once this schematic has been attached to a wrist
    /// document and its children resolved (binding safety).</summary>
    public bool IsReady => childCount > 0;

    /// <summary>Current player pixel (design space) or Vector2.zero when the
    /// player has never been set.</summary>
    public Vector2 CurrentPlayerPixel =>
        _playerVisible
            ? new Vector2(_playerMarker.style.left.value.value + 9f,
                _playerMarker.style.top.value.value + 9f)
            : Vector2.zero;

    /// <summary>Updates the live player marker from the ship world position.
    /// Marker points up when yaw 0 (facing ship forward = map up).</summary>
    public void SetPlayer(Vector3 worldPosition, float yawDeg)
    {
        Vector2 pixel = ShipSchematicLayout.WorldToSchematicPixel(worldPosition);
        SetPlayerVisible(true);
        if (float.IsNaN(_lastPlayerPixel.x)
            || (pixel - _lastPlayerPixel).sqrMagnitude > 0.25f)
        {
            _playerMarker.style.left = pixel.x - 9f;
            _playerMarker.style.top = pixel.y - 9f;
            _lastPlayerPixel = pixel;
        }

        float rotateDeg = -yawDeg; // same convention as the surface MapPlayer
        if (Mathf.Abs(rotateDeg - _lastPlayerRotateDeg) > 0.05f)
        {
            _playerMarker.style.rotate = new Rotate(new Angle(rotateDeg));
            _lastPlayerRotateDeg = rotateDeg;
        }
    }

    public void SetPlayerVisible(bool visible)
    {
        _playerVisible = visible;
        _playerMarker.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>True when the player marker is currently shown.</summary>
    public bool IsPlayerVisible => _playerVisible;

    /// <summary>Marker style helpers used by tests (position centre pixel).
    /// Returns false when the marker is hidden.</summary>
    public bool TryGetPlayerCentre(out Vector2 pixel)
    {
        pixel = Vector2.zero;
        if (!_playerVisible)
        {
            return false;
        }

        pixel = new Vector2(
            _playerMarker.style.left.value.value + 9f,
            _playerMarker.style.top.value.value + 9f);
        return true;
    }

    /// <summary>Terminal marker centre in canvas pixels (icon anchor).</summary>
    public Vector2 GetTerminalPixel(int consoleId)
    {
        return ShipSchematicLayout.TerminalPixel(consoleId);
    }

    /// <summary>Creates the monitor-icon containers with their Chinese labels
    /// (01 总览 / 02 仓库 / 03 加工 / 04 据点).</summary>
    private void BuildTerminalMarkers()
    {
        ShipTerminalMarker[] terminals = ShipSchematicLayout.Terminals;
        for (int i = 0; i < terminals.Length; i++)
        {
            ShipTerminalMarker terminal = terminals[i];
            Vector2 pixel = ShipSchematicLayout.DeckToPixel(terminal.DeckPosition);

            var container = new VisualElement
            {
                name = "ShipTerminal_" + terminal.ConsoleId
            };
            container.pickingMode = PickingMode.Ignore;
            container.style.position = Position.Absolute;
            container.style.left = pixel.x - 45f;
            container.style.top = pixel.y + 8f;
            container.style.width = 90f;
            container.style.height = 22f;
            Add(container);

            var zh = new Label { text = terminal.Zh, name = "ShipTerminalZh_" + terminal.ConsoleId };
            zh.pickingMode = PickingMode.Ignore;
            zh.style.position = Position.Absolute;
            zh.style.left = 0f;
            zh.style.top = 0f;
            zh.style.width = 90f;
            zh.style.height = 14f;
            zh.style.unityTextAlign = TextAnchor.MiddleCenter;
            zh.style.fontSize = 10f;
            zh.style.color = new StyleColor(LabelColor);
            zh.style.unityFontStyleAndWeight = FontStyle.Bold;
            container.Add(zh);

            var meta = new Label
            {
                text = terminal.Code,
                name = "ShipTerminalMeta_" + terminal.ConsoleId
            };
            meta.pickingMode = PickingMode.Ignore;
            meta.style.position = Position.Absolute;
            meta.style.left = 0f;
            meta.style.top = 13f;
            meta.style.width = 90f;
            meta.style.height = 9f;
            meta.style.unityTextAlign = TextAnchor.MiddleCenter;
            meta.style.fontSize = 6.5f;
            meta.style.color = new StyleColor(MetaColor);
            container.Add(meta);
        }
    }

    /// <summary>Creates the single airlock (capsule icon) container.</summary>
    private void BuildAirlockMarker()
    {
        Vector2 pixel = ShipSchematicLayout.DeckToPixel(
            ShipSchematicLayout.Airlock.DeckPosition);

        var container = new VisualElement { name = "ShipAirlockMarker" };
        container.pickingMode = PickingMode.Ignore;
        container.style.position = Position.Absolute;
        container.style.left = pixel.x - 45f;
        container.style.top = pixel.y + 9f;
        container.style.width = 90f;
        container.style.height = 22f;
        Add(container);

        var zh = new Label { text = "气闸" };
        zh.pickingMode = PickingMode.Ignore;
        zh.style.position = Position.Absolute;
        zh.style.left = 0f;
        zh.style.top = 0f;
        zh.style.width = 90f;
        zh.style.height = 14f;
        zh.style.unityTextAlign = TextAnchor.MiddleCenter;
        zh.style.fontSize = 10f;
        zh.style.color = new StyleColor(LabelColor);
        zh.style.unityFontStyleAndWeight = FontStyle.Bold;
        container.Add(zh);

        var meta = new Label { text = "AIRLOCK // EVA" };
        meta.pickingMode = PickingMode.Ignore;
        meta.style.position = Position.Absolute;
        meta.style.left = 0f;
        meta.style.top = 13f;
        meta.style.width = 90f;
        meta.style.height = 9f;
        meta.style.unityTextAlign = TextAnchor.MiddleCenter;
        meta.style.fontSize = 6.5f;
        meta.style.color = new StyleColor(MetaColor);
        container.Add(meta);
    }

    /// <summary>Room labels: larger Chinese identity + tiny technical code.</summary>
    private void BuildRoomLabels()
    {
        ShipRoomLabel[] labels = ShipSchematicLayout.RoomLabels;
        for (int i = 0; i < labels.Length; i++)
        {
            ShipRoomLabel room = labels[i];
            Vector2 pixel = ShipSchematicLayout.DeckToPixel(room.DeckPosition);

            var zh = new Label { text = room.Zh, name = "ShipRoomLabel_" + room.Code };
            zh.pickingMode = PickingMode.Ignore;
            zh.style.position = Position.Absolute;
            zh.style.left = pixel.x - 70f;
            zh.style.top = pixel.y - 12f;
            zh.style.width = 140f;
            zh.style.height = 16f;
            zh.style.unityTextAlign = TextAnchor.MiddleCenter;
            zh.style.fontSize = 11f;
            zh.style.color = new StyleColor(LabelColor);
            zh.style.unityFontStyleAndWeight = FontStyle.Bold;
            Add(zh);

            var code = new Label { text = room.Code };
            code.pickingMode = PickingMode.Ignore;
            code.style.position = Position.Absolute;
            code.style.left = pixel.x - 70f;
            code.style.top = pixel.y + 2f;
            code.style.width = 140f;
            code.style.height = 8f;
            code.style.unityTextAlign = TextAnchor.MiddleCenter;
            code.style.fontSize = 6f;
            code.style.color = new StyleColor(MetaColor);
            Add(code);
        }
    }

    // ====================================================================
    // Mesh rendering (hull + walls + doors + icons)
    // ====================================================================

    private void GenerateVisual(MeshGenerationContext ctx)
    {
        // Hull outline (closed polyline over the deck ring) - subtle frame.
        Vector2[] ring = ShipSchematicLayout.HullRing;
        for (int i = 0; i < ring.Length; i++)
        {
            Vector2 a = ShipSchematicLayout.DeckToPixel(ring[i]);
            Vector2 b = ShipSchematicLayout.DeckToPixel(
                ring[(i + 1) % ring.Length]);
            DrawLine(ctx, a, b, HullColor, 1.4f);
        }

        // Solid wall runs.
        ShipWallSegment[] walls = ShipSchematicLayout.Walls;
        for (int i = 0; i < walls.Length; i++)
        {
            DrawLine(ctx,
                ShipSchematicLayout.DeckToPixel(walls[i].A),
                ShipSchematicLayout.DeckToPixel(walls[i].B),
                WallColor, 2.2f);
        }

        // Dashed door segments (replace the interrupted wall part).
        ShipDoorSegment[] doors = ShipSchematicLayout.Doors;
        for (int i = 0; i < doors.Length; i++)
        {
            DrawDashedLine(ctx,
                ShipSchematicLayout.DeckToPixel(doors[i].A),
                ShipSchematicLayout.DeckToPixel(doors[i].B),
                DoorColor, 2.2f, 8f, 6f);
        }

        // Terminal monitor pictograms.
        ShipTerminalMarker[] terminals = ShipSchematicLayout.Terminals;
        for (int i = 0; i < terminals.Length; i++)
        {
            DrawMonitorIcon(ctx,
                ShipSchematicLayout.DeckToPixel(terminals[i].DeckPosition));
        }

        // Airlock capsule / descent-pod pictogram.
        DrawCapsuleIcon(ctx,
            ShipSchematicLayout.DeckToPixel(ShipSchematicLayout.Airlock.DeckPosition));
    }

    // ---- Geometric icon helpers ----

    /// <summary>Minimal monitor pictogram: display outline + stand.</summary>
    private static void DrawMonitorIcon(MeshGenerationContext ctx, Vector2 centre)
    {
        float halfW = 6.5f;
        float halfH = 4.6f;
        // Display panel outline.
        DrawRectOutline(ctx, centre + new Vector2(0f, -1f), halfW, halfH,
            IconColor, 1.2f, IconFill);
        // Stand neck + foot.
        DrawLine(ctx, centre + new Vector2(0f, 3.4f), centre + new Vector2(0f, 6f),
            IconColor, 1.2f);
        DrawLine(ctx, centre + new Vector2(-3.4f, 6f), centre + new Vector2(3.4f, 6f),
            IconColor, 1.2f);
    }

    /// <summary>Simplified descent capsule: rounded pod outline + centre band.</summary>
    private static void DrawCapsuleIcon(MeshGenerationContext ctx, Vector2 centre)
    {
        DrawEllipseOutline(ctx, centre + new Vector2(0f, -0.5f), 6.6f, 10.5f,
            AirlockColor, 1.3f);
        // Capsule waist band (reads as a return-pod ring).
        DrawLine(ctx, centre + new Vector2(-6.6f, 1.6f), centre + new Vector2(6.6f, 1.6f),
            AirlockColor, 1.1f);
        // Small landing feet hint.
        DrawLine(ctx, centre + new Vector2(-4f, 9.6f), centre + new Vector2(-2.2f, 11.6f),
            AirlockColor, 1.1f);
        DrawLine(ctx, centre + new Vector2(4f, 9.6f), centre + new Vector2(2.2f, 11.6f),
            AirlockColor, 1.1f);
    }

    // ---- Primitive mesh helpers (absolute canvas pixels) ----

    private static void DrawLine(
        MeshGenerationContext ctx,
        Vector2 start,
        Vector2 end,
        Color32 color,
        float thickness)
    {
        Vector2 delta = end - start;
        if (delta.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector2 normal = new Vector2(-delta.y, delta.x).normalized;
        Vector2 offset = normal * (thickness * 0.5f);

        MeshWriteData mesh = ctx.Allocate(4, 6);
        mesh.SetNextVertex(MakeVertex(start - offset, color));
        mesh.SetNextVertex(MakeVertex(start + offset, color));
        mesh.SetNextVertex(MakeVertex(end + offset, color));
        mesh.SetNextVertex(MakeVertex(end - offset, color));
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(1);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(3);
    }

    /// <summary>Draws a wall-aligned door as evenly spaced dashes; the dash
    /// run exactly covers A..B so the door reads as a replaced wall part.</summary>
    private static void DrawDashedLine(
        MeshGenerationContext ctx,
        Vector2 start,
        Vector2 end,
        Color32 color,
        float thickness,
        float dashPx,
        float gapPx)
    {
        float length = Vector2.Distance(start, end);
        if (length <= 0.0001f)
        {
            return;
        }

        Vector2 dir = (end - start) / length;
        float pitch = dashPx + gapPx;
        int count = Mathf.Max(1, Mathf.RoundToInt(length / pitch));
        pitch = length / count; // exact fit: dashes + gaps tile the span
        float dash = Mathf.Max(1f, pitch * dashPx / (dashPx + gapPx));
        float cursor = 0f;
        while (cursor < length - 0.5f)
        {
            float a = cursor;
            float b = Mathf.Min(cursor + dash, length);
            if (b - a > 0.4f)
            {
                DrawLine(ctx, start + dir * a, start + dir * b, color, thickness);
            }

            cursor += pitch;
        }
    }

    private static void DrawRectOutline(MeshGenerationContext ctx, Vector2 centre,
        float halfW, float halfH, Color32 color, float thickness, Color32 fill)
    {
        // Translucent display fill first.
        MeshWriteData fillMesh = ctx.Allocate(4, 6);
        Vector2 min = centre - new Vector2(halfW, halfH);
        Vector2 max = centre + new Vector2(halfW, halfH);
        fillMesh.SetNextVertex(MakeVertex(new Vector2(min.x, min.y), fill));
        fillMesh.SetNextVertex(MakeVertex(new Vector2(max.x, min.y), fill));
        fillMesh.SetNextVertex(MakeVertex(new Vector2(max.x, max.y), fill));
        fillMesh.SetNextVertex(MakeVertex(new Vector2(min.x, max.y), fill));
        fillMesh.SetNextIndex(0);
        fillMesh.SetNextIndex(1);
        fillMesh.SetNextIndex(2);
        fillMesh.SetNextIndex(0);
        fillMesh.SetNextIndex(2);
        fillMesh.SetNextIndex(3);

        DrawLine(ctx, new Vector2(min.x, min.y), new Vector2(max.x, min.y), color, thickness);
        DrawLine(ctx, new Vector2(max.x, min.y), new Vector2(max.x, max.y), color, thickness);
        DrawLine(ctx, new Vector2(max.x, max.y), new Vector2(min.x, max.y), color, thickness);
        DrawLine(ctx, new Vector2(min.x, max.y), new Vector2(min.x, min.y), color, thickness);
    }

    private static void DrawEllipseOutline(MeshGenerationContext ctx, Vector2 centre,
        float radiusX, float radiusY, Color32 color, float thickness)
    {
        const int segments = 28;
        Vector2 previous = centre + new Vector2(radiusX, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector2 next = centre + new Vector2(
                Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
            DrawLine(ctx, previous, next, color, thickness);
            previous = next;
        }
    }

    private static Vertex MakeVertex(Vector2 position, Color32 tint)
    {
        return new Vertex
        {
            position = new Vector3(position.x, position.y, Vertex.nearZ),
            tint = tint,
            uv = Vector2.zero
        };
    }
}
