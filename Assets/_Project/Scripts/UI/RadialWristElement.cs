using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Draws the complete radial geometry for the Phase 06 wrist hologram
/// (authoritative CodeReference architecture).
///
/// Implementation rules (enforced by validation):
/// - No Painter2D.
/// - No tick VisualElements / dot-generated arcs.
/// - No rectangle-card geometry and no one-continuous full outer functional
///   ring (the four functional annular sectors keep visible angular gaps).
/// - Geometry is produced with triangle meshes only.
///
/// Fixed design space: 1280 x 800 logical pixels, centre (640, 400).
/// Angle convention: 0 deg = right, 90 = up, 180 = left, 270 = down.
/// UI Toolkit Y grows downward, so Polar() inverts sin for Y.
/// </summary>
public sealed class RadialWristElement : VisualElement
{
    public const float DesignWidth = 1280f;
    public const float DesignHeight = 800f;

    public static readonly Vector2 Center =
        new Vector2(640f, 400f);

    public const float MapRadius = 235f;

    // Status arcs sit OUTSIDE the functional ring (Round 9.1). The 1280x800
    // logical canvas only allows a whole ring up to ~399px radius (apex
    // y = 400 - radius), so the arcs use the maximum whole-ring radius 392
    // and the functional ring stops at 372 -> a real 13px radial gap
    // (arcInner 385 - ringOuter 372) between status arcs and the sectors.
    public const float StatusArcRadius = 392f;
    public const float StatusArcThickness = 14f;

    public const float SectorInnerRadius = 245f;
    public const float SectorOuterRadius = 372f;

    /// <summary>Polar text anchor radius for the four functional sectors.
    /// Text container boxes are sized so ALL FOUR CORNERS stay inside the
    /// safe annular band (inner 255 .. outer 400) and inside the sector
    /// angular range minus 3 deg padding.</summary>
    public const float SectorTextRadius = 328f;

    // Exact CodeReference angular extents (degrees, 0 = right / +y-up space).
    public const float HealthArcStartDeg = 55f;
    public const float HealthArcEndDeg = 125f;

    public const float EquipmentSectorStartDeg = 135f;
    public const float EquipmentSectorEndDeg = 190f;

    public const float NavigationSectorStartDeg = 195f;
    public const float NavigationSectorEndDeg = 235f;

    public const float SatietyArcStartDeg = 235f;
    public const float SatietyArcEndDeg = 305f;

    public const float HarvestSectorStartDeg = 305f;
    public const float HarvestSectorEndDeg = 345f;

    public const float StateSectorStartDeg = 350f;
    public const float StateSectorEndDeg = 405f;

    // --------------------------------------------------------------------
    // Colors (translucent hologram palette; the central map is never white).
    // --------------------------------------------------------------------

    private static readonly Color32 AccentWeak =
        new Color32(14, 127, 140, 72);

    private static readonly Color32 Hairline =
        new Color32(105, 122, 132, 105);

    private static readonly Color32 VeryWeakHairline =
        new Color32(105, 122, 132, 42);

    // Central map MUST NOT become a white board: fill alpha stays ~0.10-0.15.
    private static readonly Color32 MapFill =
        new Color32(185, 198, 204, 30);

    private static readonly Color32 SectorFill =
        new Color32(190, 204, 210, 42);

    private static readonly Color32 InactiveArc =
        new Color32(145, 156, 162, 58);

    // Round 9 user requirement (overrides the old single-teal accent for the
    // two personal status tracks): Health = muted green, Satiety = dark muted
    // orange. No fluorescent green / bright orange.
    public static readonly Color32 HealthActiveColor =
        new Color32(0x5E, 0xAD, 0x68, 235);

    public static readonly Color32 SatietyActiveColor =
        new Color32(0xA9, 0x63, 0x2F, 235);

    // --------------------------------------------------------------------

    private float _health01 = 1f;
    private float _satiety01 = 0.82f;

    public RadialWristElement()
    {
        pickingMode = PickingMode.Ignore;

        style.position = Position.Absolute;
        style.left = 0f;
        style.top = 0f;
        style.width = DesignWidth;
        style.height = DesignHeight;

        generateVisualContent += GenerateVisual;
    }

    /// <summary>Health arc active fraction 0..1 (higher = healthier).</summary>
    public void SetHealth(float value01)
    {
        _health01 = Mathf.Clamp01(value01);
        MarkDirtyRepaint();
    }

    /// <summary>Satiety arc active fraction 0..1 (higher = more satiated).</summary>
    public void SetSatiety(float value01)
    {
        _satiety01 = Mathf.Clamp01(value01);
        MarkDirtyRepaint();
    }

    public float HealthFraction01 => _health01;
    public float SatietyFraction01 => _satiety01;

    private void GenerateVisual(MeshGenerationContext ctx)
    {
        // ================================================================
        // 1. LOCAL MAP (translucent disc + boundary + distance rings + cross)
        // ================================================================

        DrawDisc(ctx, Center, MapRadius, MapFill, 96);

        // Map outside boundary (map edge, not a functional sector ring).
        DrawRingSector(ctx, Center, MapRadius - 1.5f, MapRadius,
            0f, 360f, Hairline, 96);

        // Distance rings.
        DrawRingSector(ctx, Center, 78f, 79f, 0f, 360f, VeryWeakHairline, 64);
        DrawRingSector(ctx, Center, 156f, 157f, 0f, 360f, VeryWeakHairline, 96);

        // Very subtle map cross reference.
        DrawLine(ctx,
            new Vector2(Center.x - 175f, Center.y),
            new Vector2(Center.x + 175f, Center.y),
            VeryWeakHairline, 1f);

        DrawLine(ctx,
            new Vector2(Center.x, Center.y - 175f),
            new Vector2(Center.x, Center.y + 175f),
            VeryWeakHairline, 1f);

        // ================================================================
        // 2 + 3. STATUS TRACKS - one shared helper, drawn OUTSIDE the
        // functional ring (Round 9.1). Health upper / Satiety lower, exactly
        // mirrored: same radius 392, same thickness 14, same 70 deg span.
        // Satiety active fill stays at 82% while its full inactive track
        // keeps the perceived bar length identical to Health.
        // ================================================================

        DrawStatusTrack(ctx, HealthArcStartDeg, _health01, HealthActiveColor);

        DrawStatusTrack(ctx, SatietyArcStartDeg, _satiety01, SatietyActiveColor);

        // ================================================================
        // 4. FOUR THICK FUNCTIONAL SECTORS (with visible angular gaps)
        // ================================================================

        DrawFunctionalSector(ctx, EquipmentSectorStartDeg, EquipmentSectorEndDeg);
        DrawFunctionalSector(ctx, NavigationSectorStartDeg, NavigationSectorEndDeg);
        DrawFunctionalSector(ctx, HarvestSectorStartDeg, HarvestSectorEndDeg);
        DrawFunctionalSector(ctx, StateSectorStartDeg, StateSectorEndDeg);
    }

    // ====================================================================
    // Status arcs
    // ====================================================================

    /// <summary>One mirrored status track (Health upper / Satiety lower).
    /// Geometry is shared so both tracks can never diverge: 70 deg span at
    /// StatusArcRadius with StatusArcThickness. The inactive track always
    /// covers the FULL span; the active fill only runs to the current value.
    /// Both tracks end BEFORE the functional ring region starts (13px gap),
    /// so no status arc ever touches a sector boundary.</summary>
    private static void DrawStatusTrack(
        MeshGenerationContext ctx,
        float startDeg,
        float value01,
        Color32 activeColor)
    {
        const float spanDeg = 70f;
        float endDeg = startDeg + spanDeg;
        float halfThickness = StatusArcThickness * 0.5f;

        float inner = StatusArcRadius - halfThickness;
        float outer = StatusArcRadius + halfThickness;

        // Inactive track: ALWAYS the complete 70 deg span.
        DrawRingSector(ctx, Center, inner, outer, startDeg, endDeg, InactiveArc, 40);

        // Active progress fill (health 100% / satiety 82%).
        float activeEnd = Mathf.Lerp(startDeg, endDeg, Mathf.Clamp01(value01));
        DrawRingSector(ctx, Center, inner, outer, startDeg, activeEnd, activeColor, 40);
    }

    // ====================================================================
    // Functional annular sectors
    // ====================================================================

    private static void DrawFunctionalSector(
        MeshGenerationContext ctx,
        float startDeg,
        float endDeg)
    {
        // Main translucent sector body.
        DrawRingSector(ctx, Center, SectorInnerRadius, SectorOuterRadius,
            startDeg, endDeg, SectorFill, 36);

        // Thin outer accent/hairline.
        DrawRingSector(ctx, Center, SectorOuterRadius - 1.5f, SectorOuterRadius,
            startDeg, endDeg, AccentWeak, 36);

        // Thin inner boundary.
        DrawRingSector(ctx, Center, SectorInnerRadius, SectorInnerRadius + 1.5f,
            startDeg, endDeg, Hairline, 36);

        // Radial delimiters.
        DrawRadialLine(ctx, startDeg);
        DrawRadialLine(ctx, endDeg);
    }

    private static void DrawRadialLine(
        MeshGenerationContext ctx,
        float angleDeg)
    {
        Vector2 a = Polar(Center, SectorInnerRadius, angleDeg);
        Vector2 b = Polar(Center, SectorOuterRadius, angleDeg);

        DrawLine(ctx, a, b, Hairline, 1f);
    }

    // ====================================================================
    // Coordinate helpers
    // ====================================================================

    /// <summary>Polar to pixel (UI Toolkit Y grows downward, so sin is
    /// inverted). 0 deg = right, 90 = up, 180 = left, 270 = down.</summary>
    public static Vector2 Polar(
        Vector2 center,
        float radius,
        float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;

        return new Vector2(
            center.x + Mathf.Cos(rad) * radius,
            center.y - Mathf.Sin(rad) * radius);
    }

    /// <summary>Wrist-pixel angle around the design centre in conventional
    /// +y-up degrees (0 = right, 90 = up). Used by geometry validation.</summary>
    public static float WristAngleDeg(Vector2 pixel)
    {
        Vector2 d = pixel - Center;

        // Restore conventional +Y-up for atan.
        float angle = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;
        if (angle < 0f)
        {
            angle += 360f;
        }

        return angle;
    }

    /// <summary>Round 9 unified polar anchor for functional sector content.
    /// Text containers are positioned with:
    /// left = anchor.x - width/2  /  top = anchor.y - height/2
    /// (never hand-tuned left/top that can drift outside the sector).</summary>
    public static Vector2 SectorTextAnchor(
        float startDeg,
        float endDeg)
    {
        float midDeg = (startDeg + endDeg) * 0.5f;

        return Polar(Center, SectorTextRadius, midDeg);
    }

    // ====================================================================
    // Disc mesh
    // ====================================================================

    private static void DrawDisc(
        MeshGenerationContext ctx,
        Vector2 center,
        float radius,
        Color32 color,
        int segments)
    {
        segments = Mathf.Clamp(segments, 8, 180);

        int vertexCount = segments + 1;
        int indexCount = segments * 3;

        MeshWriteData mesh = ctx.Allocate(vertexCount, indexCount);

        mesh.SetNextVertex(MakeVertex(center, color));

        for (int i = 0; i < segments; i++)
        {
            float angle = i * 360f / segments;

            mesh.SetNextVertex(
                MakeVertex(Polar(center, radius, angle), color));
        }

        for (int i = 0; i < segments; i++)
        {
            ushort centreIndex = 0;
            ushort current = (ushort)(i + 1);
            ushort next = (ushort)(((i + 1) % segments) + 1);

            mesh.SetNextIndex(centreIndex);
            mesh.SetNextIndex(current);
            mesh.SetNextIndex(next);
        }
    }

    // ====================================================================
    // Annular / ring-sector mesh
    // ====================================================================

    private static void DrawRingSector(
        MeshGenerationContext ctx,
        Vector2 center,
        float innerRadius,
        float outerRadius,
        float startDeg,
        float endDeg,
        Color32 color,
        int segments)
    {
        if (outerRadius <= innerRadius)
        {
            return;
        }

        segments = Mathf.Clamp(segments, 1, 180);

        float span = endDeg - startDeg;

        int vertexCount = (segments + 1) * 2;
        int indexCount = segments * 6;

        MeshWriteData mesh = ctx.Allocate(vertexCount, indexCount);

        // Vertices:
        //
        // 0 inner0
        // 1 outer0
        // 2 inner1
        // 3 outer1
        // ...

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = startDeg + span * t;

            Vector2 innerPos = Polar(center, innerRadius, angle);
            Vector2 outerPos = Polar(center, outerRadius, angle);

            mesh.SetNextVertex(MakeVertex(innerPos, color));
            mesh.SetNextVertex(MakeVertex(outerPos, color));
        }

        for (int i = 0; i < segments; i++)
        {
            ushort i0 = (ushort)(i * 2);
            ushort o0 = (ushort)(i * 2 + 1);
            ushort i1 = (ushort)((i + 1) * 2);
            ushort o1 = (ushort)((i + 1) * 2 + 1);

            // Triangle 1
            mesh.SetNextIndex(i0);
            mesh.SetNextIndex(o0);
            mesh.SetNextIndex(o1);

            // Triangle 2
            mesh.SetNextIndex(i0);
            mesh.SetNextIndex(o1);
            mesh.SetNextIndex(i1);
        }
    }

    // ====================================================================
    // Simple line quad
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

        Vector2 v0 = start - offset;
        Vector2 v1 = start + offset;
        Vector2 v2 = end + offset;
        Vector2 v3 = end - offset;

        MeshWriteData mesh = ctx.Allocate(4, 6);

        mesh.SetNextVertex(MakeVertex(v0, color));
        mesh.SetNextVertex(MakeVertex(v1, color));
        mesh.SetNextVertex(MakeVertex(v2, color));
        mesh.SetNextVertex(MakeVertex(v3, color));

        mesh.SetNextIndex(0);
        mesh.SetNextIndex(1);
        mesh.SetNextIndex(2);

        mesh.SetNextIndex(0);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(3);
    }

    // ====================================================================
    // Vertex helper
    // ====================================================================

    private static Vertex MakeVertex(
        Vector2 position,
        Color32 tint)
    {
        Vertex vertex = new Vertex();

        vertex.position = new Vector3(
            position.x,
            position.y,
            Vertex.nearZ);

        vertex.tint = tint;
        vertex.uv = Vector2.zero;

        return vertex;
    }
}
