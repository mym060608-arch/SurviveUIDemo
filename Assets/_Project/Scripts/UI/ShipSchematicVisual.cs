using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Line-art ARK-07 ship schematic (Phase 07B.1) drawn in the wrist central
/// map while the player is inside the ship.
///
/// ShipSchematicLayout owns the DATA (hull ring, divider segments, node
/// roster, deck->pixel mapping). This element only RENDERS that data:
///   - translucent deck hull polyline + interior hairlines (triangle meshes
///     through generateVisualContent),
///   - one labelled chip per functional zone (Bridge / Preparation /
///     Processing / Outpost / Airlock) with Chinese-first text and a small
///     English code,
///   - a live player marker (raised ▲) that follows the real ship position.
///
/// Because the data and the rendering are separate, a later hand-drawn-art
/// pass can replace this class (or its drawing body) while ShipSchematicLayout
/// and the wrist integration keep working unchanged.
///
/// The element fills the whole 1280 x 800 wrist canvas; the chips/marker are
/// absolute-positioned children at authoritative schematic pixels.
/// </summary>
public sealed class ShipSchematicVisual : VisualElement
{
    public const float CanvasWidth = 1280f;
    public const float CanvasHeight = 800f;

    /// <summary>Fixed chip footprint (px); centres sit on the node anchor.
    /// Kept narrow enough that adjacent room chips (Prep vs Processing, 80 px
    /// apart on the plan) never overlap.</summary>
    public const float ChipWidth = 78f;
    public const float ChipHeight = 30f;

    // Hologram palette (matches the wrist language; never opaque white).
    private static readonly Color32 HullColor =
        new Color32(168, 198, 206, 215);
    private static readonly Color32 DividerColor =
        new Color32(120, 150, 160, 125);
    private static readonly Color32 NodeDotColor =
        new Color32(14, 127, 140, 245);
    private static readonly Color32 PlayerColor =
        new Color32(235, 250, 252, 255);

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

        // ---- Functional zone chips (children paint above the mesh) ----
        for (int i = 0; i < ShipSchematicLayout.Nodes.Length; i++)
        {
            Add(CreateZoneChip(ShipSchematicLayout.Nodes[i]));
        }

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

    /// <summary>True when this schematic has been attached to a wrist
    /// document and its chips resolved (binding safety).</summary>
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

    /// <summary>True when the player marker is currently shown (Phase 07B.1
    /// map-mode acceptance reads this directly).</summary>
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

    // ====================================================================
    // Zone chips
    // ====================================================================

    private static VisualElement CreateZoneChip(ShipSchematicNode node)
    {
        Vector2 anchor = ShipSchematicLayout.DeckToPixel(node.AnchorX, node.AnchorZ);

        var chip = new VisualElement();
        chip.name = "ShipZoneChip_" + node.Code;
        chip.pickingMode = PickingMode.Ignore;
        chip.style.position = Position.Absolute;
        chip.style.left = anchor.x - ChipWidth * 0.5f;
        chip.style.top = anchor.y - ChipHeight * 0.5f;
        chip.style.width = ChipWidth;
        chip.style.height = ChipHeight;
        chip.style.backgroundColor = new Color(0.045f, 0.10f, 0.12f, 0.72f);
        chip.style.borderTopLeftRadius = 3f;
        chip.style.borderTopRightRadius = 3f;
        chip.style.borderBottomLeftRadius = 3f;
        chip.style.borderBottomRightRadius = 3f;

        var zh = new Label { text = node.Zh, name = "ShipZoneLabel_" + node.Code };
        zh.pickingMode = PickingMode.Ignore;
        zh.style.position = Position.Absolute;
        zh.style.left = 2f;
        zh.style.top = 1f;
        zh.style.width = ChipWidth - 4f;
        zh.style.height = 16f;
        zh.style.unityTextAlign = TextAnchor.MiddleCenter;
        zh.style.fontSize = 12f;
        zh.style.color = new Color(0.93f, 0.97f, 0.98f, 1f);
        zh.style.unityFontStyleAndWeight = FontStyle.Bold;
        chip.Add(zh);

        // Compact English code line (full technical metadata stays in
        // ShipSchematicLayout.Meta and in the HUD zone chips).
        var meta = new Label { text = node.Code, name = "ShipZoneMeta_" + node.Code };
        meta.pickingMode = PickingMode.Ignore;
        meta.style.position = Position.Absolute;
        meta.style.left = 2f;
        meta.style.top = 16f;
        meta.style.width = ChipWidth - 4f;
        meta.style.height = 12f;
        meta.style.unityTextAlign = TextAnchor.MiddleCenter;
        meta.style.fontSize = 7f;
        meta.style.color = new Color(0.66f, 0.78f, 0.82f, 0.92f);
        meta.style.unityFontStyleAndWeight = FontStyle.Normal;
        chip.Add(meta);

        return chip;
    }

    // ====================================================================
    // Mesh rendering (hull + dividers + node dots)
    // ====================================================================

    private void GenerateVisual(MeshGenerationContext ctx)
    {
        // Hull outline (closed polyline over the deck ring).
        Vector2[] ring = ShipSchematicLayout.HullRing;
        for (int i = 0; i < ring.Length; i++)
        {
            Vector2 a = ShipSchematicLayout.DeckToPixel(ring[i].x, ring[i].y);
            Vector2 b = ShipSchematicLayout.DeckToPixel(
                ring[(i + 1) % ring.Length].x, ring[(i + 1) % ring.Length].y);
            DrawLine(ctx, a, b, HullColor, 1.8f);
        }

        // Interior compartment hairlines.
        (Vector2 A, Vector2 B)[] dividers = ShipSchematicLayout.Dividers;
        for (int i = 0; i < dividers.Length; i++)
        {
            DrawLine(ctx,
                ShipSchematicLayout.DeckToPixel(dividers[i].A.x, dividers[i].A.y),
                ShipSchematicLayout.DeckToPixel(dividers[i].B.x, dividers[i].B.y),
                DividerColor, 1f);
        }

        // Functional console dots (one small accent dot per zone node).
        ShipSchematicNode[] nodes = ShipSchematicLayout.Nodes;
        for (int i = 0; i < nodes.Length; i++)
        {
            DrawDot(ctx, ShipSchematicLayout.NodePixel(nodes[i].NodeId),
                NodeDotColor, 3.2f);
        }
    }

    // ====================================================================
    // Mesh helpers (line + disc, absolute canvas pixels)
    // ====================================================================

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

    private static void DrawDot(
        MeshGenerationContext ctx,
        Vector2 centre,
        Color32 color,
        float radius)
    {
        const int segments = 16;
        MeshWriteData mesh = ctx.Allocate(segments + 1, segments * 3);
        mesh.SetNextVertex(MakeVertex(centre, color));
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            mesh.SetNextVertex(MakeVertex(centre + new Vector2(
                Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius), color));
        }

        for (int i = 0; i < segments; i++)
        {
            mesh.SetNextIndex(0);
            mesh.SetNextIndex((ushort)(i + 1));
            mesh.SetNextIndex((ushort)(((i + 1) % segments) + 1));
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
