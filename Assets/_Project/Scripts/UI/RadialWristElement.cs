using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Draws the complete radial geometry for the Phase 06 wrist hologram
/// (Round 9.2 parallel-cap architecture).
///
/// Round 9.2 rules (enforced by validation):
/// - No Painter2D.
/// - No tick VisualElements / dot-generated arcs.
/// - No rectangle-card geometry and no one-continuous full outer functional
///   ring. The four surrounding panels are Parallel-Cap Annular Panels:
///   annulus (PanelInnerRadius..PanelOuterRadius) intersected with a
///   horizontal Y band, so every panel has a horizontal top edge and a
///   horizontal bottom edge (top/bottom exactly parallel) plus curved inner
///   and outer edges.
/// - Health and Satiety share one status-track helper whose inner radius is
///   the SAME shared PanelInnerRadius (248), so the status arcs are visually
///   integrated with the panels instead of floating on a distant orbit.
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

    // Map (unchanged: the accepted central circular map stays radius 235).
    public const float MapRadius = 235f;

    // --------------------------------------------------------------------
    // Round 9.2 shared radial geometry (exact constants, validated).
    // The panel inner radius and the status track inner radius are ONE value
    // (248) so Health/Satiety visually join the functional assembly.
    // --------------------------------------------------------------------

    public const float PanelInnerRadius = 248f;
    public const float PanelOuterRadius = 365f;

    public const float StatusInnerRadius = 248f;
    public const float StatusOuterRadius = 262f;
    public const float StatusThickness = 14f; // 262 - 248

    // Parallel-cap panel horizontal Y bands (UI Toolkit y down).
    public const float UpperPanelTopY = 205f;
    public const float UpperPanelBottomY = 345f;
    public const float LowerPanelTopY = 455f;
    public const float LowerPanelBottomY = 595f;

    /// <summary>Radial thickness of the four functional panels (365-248).</summary>
    public const float FunctionalPanelThickness =
        PanelOuterRadius - PanelInnerRadius;

    // Status arc angular spans (degrees, 0 = right / +y-up space).
    public const float HealthArcStartDeg = 55f;
    public const float HealthArcEndDeg = 125f;
    public const float HealthArcSpanDeg = HealthArcEndDeg - HealthArcStartDeg; // 70

    public const float SatietyArcStartDeg = 235f;
    public const float SatietyArcEndDeg = 305f;
    public const float SatietyArcSpanDeg = SatietyArcEndDeg - SatietyArcStartDeg; // 70

    // --------------------------------------------------------------------
    // Colors (translucent hologram palette; the central map is never white).
    // --------------------------------------------------------------------

    private static readonly Color32 Accent =
        new Color32(14, 127, 140, 230);

    private static readonly Color32 AccentWeak =
        new Color32(14, 127, 140, 72);

    private static readonly Color32 Hairline =
        new Color32(105, 122, 132, 105);

    private static readonly Color32 VeryWeakHairline =
        new Color32(105, 122, 132, 42);

    // Central map MUST NOT become a white board: fill alpha stays ~0.10-0.15.
    private static readonly Color32 MapFill =
        new Color32(185, 198, 204, 30);

    // Round 9.2: very light translucent gray panel fill (never opaque white)
    // with thin muted outlines (caps + inner/outer arcs).
    private static readonly Color32 PanelFill =
        new Color32(196, 207, 213, 38);

    // The same inactive track color is used by BOTH status tracks.
    private static readonly Color32 InactiveArc =
        new Color32(145, 156, 162, 58);

    /// <summary>Health active fill: muted green #5EAD68 (no fluorescent green).</summary>
    public static readonly Color32 HealthActiveColor =
        new Color32(0x5E, 0xAD, 0x68, 235);

    /// <summary>Satiety active fill: dark muted orange #A9632F (no bright orange).</summary>
    public static readonly Color32 SatietyActiveColor =
        new Color32(0xA9, 0x63, 0x2F, 235);

    // --------------------------------------------------------------------

    private float _health01 = 1f;
    private float _satiety01 = 0.82f;

    /// <summary>
    /// Phase 07B.1: when true the central map is a SHIP schematic host - the
    /// generic surface-map decorations (distance rings + reference cross) are
    /// suppressed and ShipSchematicVisual draws the deck plan inside the same
    /// circular frame. The disc fill + boundary stay so the wrist keeps its
    /// round technical interface in both modes.
    /// </summary>
    public bool ShipSchematicMode { get; set; }

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

    /// <summary>Satiety arc active fraction 0..1 (higher = more satiated).
    /// The full inactive track is always drawn so both physical track lengths
    /// stay identical.</summary>
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

        // Map outside boundary (map edge, not a functional ring).
        DrawRingSector(ctx, Center, MapRadius - 1.5f, MapRadius,
            0f, 360f, Hairline, 96);

        if (!ShipSchematicMode)
        {
            // Surface-map decorations only: distance rings + reference cross.
            // Ship mode leaves the disc clean for the deck schematic.
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
        }

        // ================================================================
        // 2 + 3. STATUS TRACKS - ONE shared helper at the SAME inner radius
        // as the functional panels (248). Round 9.2 removes the distant
        // 392-orbit arcs entirely.
        // ================================================================

        DrawStatusTrack(ctx, HealthArcStartDeg, _health01, HealthActiveColor);
        DrawStatusTrack(ctx, SatietyArcStartDeg, _satiety01, SatietyActiveColor);

        // ================================================================
        // 4. FOUR PARALLEL-CAP ANNULAR PANELS (annulus 248..365 ∩ band).
        // Equipment upper-left, State upper-right, Navigation lower-left,
        // Harvest lower-right. They never form one continuous outer ring.
        // ================================================================

        // Equipment: upper-left band.
        DrawParallelPanel(ctx, true, UpperPanelTopY, UpperPanelBottomY,
            PanelFill, 48);

        // State: upper-right band.
        DrawParallelPanel(ctx, false, UpperPanelTopY, UpperPanelBottomY,
            PanelFill, 48);

        // Navigation: lower-left band.
        DrawParallelPanel(ctx, true, LowerPanelTopY, LowerPanelBottomY,
            PanelFill, 48);

        // Harvest: lower-right band.
        DrawParallelPanel(ctx, false, LowerPanelTopY, LowerPanelBottomY,
            PanelFill, 48);
    }

    // ====================================================================
    // Status arcs
    // ====================================================================

    /// <summary>One mirrored status track (Health upper / Satiety lower).
    /// Geometry is shared so both tracks can never diverge: 70 deg span,
    /// inner 248 == PanelInnerRadius, outer 262, thickness 14. The inactive
    /// track always covers the FULL span; the active fill only runs to the
    /// current value.</summary>
    private static void DrawStatusTrack(
        MeshGenerationContext ctx,
        float startDeg,
        float value01,
        Color32 activeColor)
    {
        const float spanDeg = 70f;
        float endDeg = startDeg + spanDeg;

        // Inactive track: ALWAYS the complete 70 deg span (same color both).
        DrawRingSector(ctx, Center, StatusInnerRadius, StatusOuterRadius,
            startDeg, endDeg, InactiveArc, 40);

        // Active progress fill (health 100% / satiety 82%).
        float activeEnd = Mathf.Lerp(startDeg, endDeg, Mathf.Clamp01(value01));
        DrawRingSector(ctx, Center, StatusInnerRadius, StatusOuterRadius,
            startDeg, activeEnd, activeColor, 40);
    }

    // ====================================================================
    // Parallel-Cap Annular Panels
    // ====================================================================

    /// <summary>One Parallel-Cap Annular Panel: the annulus between
    /// PanelInnerRadius and PanelOuterRadius intersected with the horizontal
    /// band [topY..bottomY] on the chosen side of the centre. The generated
    /// mesh naturally has a horizontal top edge and a horizontal bottom edge
    /// (parallel by construction) while the left/right edges follow the inner
    /// and outer circle arcs.
    ///
    /// leftSide=true  -> panel on the LEFT of the centre (Equipment/Nav).
    /// leftSide=false -> panel on the RIGHT of the centre (State/Harvest).</summary>
    private static void DrawParallelPanel(
        MeshGenerationContext ctx,
        bool leftSide,
        float topY,
        float bottomY,
        Color32 fill,
        int segments)
    {
        // ---- Translucent body fill (triangle mesh rows along Y) ----
        segments = Mathf.Clamp(segments, 4, 128);
        int vertexCount = (segments + 1) * 2;
        int indexCount = segments * 6;
        MeshWriteData mesh = ctx.Allocate(vertexCount, indexCount);

        // Pre-compute one row of inner/outer points for the fill and reuse it
        // for the hairline outline below.
        var innerX = new float[segments + 1];
        var outerX = new float[segments + 1];

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float y = Mathf.Lerp(topY, bottomY, t);
            float dy = y - Center.y;

            float innerDx = Mathf.Sqrt(Mathf.Max(0f,
                PanelInnerRadius * PanelInnerRadius - dy * dy));
            float outerDx = Mathf.Sqrt(Mathf.Max(0f,
                PanelOuterRadius * PanelOuterRadius - dy * dy));

            float xInner;
            float xOuter;
            if (leftSide)
            {
                xInner = Center.x - innerDx;   // closer to the centre
                xOuter = Center.x - outerDx;   // further from the centre
            }
            else
            {
                xInner = Center.x + innerDx;
                xOuter = Center.x + outerDx;
            }

            innerX[i] = xInner;
            outerX[i] = xOuter;

            mesh.SetNextVertex(MakeVertex(new Vector2(xInner, y), fill));
            mesh.SetNextVertex(MakeVertex(new Vector2(xOuter, y), fill));
        }

        for (int i = 0; i < segments; i++)
        {
            ushort i0 = (ushort)(i * 2);
            ushort o0 = (ushort)(i * 2 + 1);
            ushort i1 = (ushort)((i + 1) * 2);
            ushort o1 = (ushort)((i + 1) * 2 + 1);

            mesh.SetNextIndex(i0);
            mesh.SetNextIndex(o0);
            mesh.SetNextIndex(o1);

            mesh.SetNextIndex(i0);
            mesh.SetNextIndex(o1);
            mesh.SetNextIndex(i1);
        }

        // ---- Thin muted outline (inner arc, outer arc, top cap, bottom cap)
        // ---- using the very same row points, so the boundary always hugs the
        // ---- body mesh. Horizontal caps make top/bottom edges parallel.
        DrawOutlinePolyline(ctx, innerX, outerX, topY, bottomY, segments);
    }

    /// <summary>Draws a hairline around one parallel-cap panel: top cap from
    /// (innerX[0], topY) to (outerX[0], topY), bottom cap at bottomY, plus the
    /// inner and outer arc polylines.</summary>
    private static void DrawOutlinePolyline(
        MeshGenerationContext ctx,
        float[] innerX,
        float[] outerX,
        float topY,
        float bottomY,
        int segments)
    {
        // Top horizontal cap (both ends share the exact same Y -> parallel).
        DrawLine(ctx,
            new Vector2(innerX[0], topY),
            new Vector2(outerX[0], topY),
            Hairline, 1.5f);

        // Bottom horizontal cap.
        DrawLine(ctx,
            new Vector2(innerX[segments], bottomY),
            new Vector2(outerX[segments], bottomY),
            Hairline, 1.5f);

        // Inner arc polyline (closest to the centre).
        for (int i = 0; i < segments; i++)
        {
            float y0 = Mathf.Lerp(topY, bottomY, i / (float)segments);
            float y1 = Mathf.Lerp(topY, bottomY, (i + 1) / (float)segments);
            DrawLine(ctx,
                new Vector2(innerX[i], y0),
                new Vector2(innerX[i + 1], y1),
                Hairline, 1f);
        }

        // Outer arc polyline (further from the centre).
        for (int i = 0; i < segments; i++)
        {
            float y0 = Mathf.Lerp(topY, bottomY, i / (float)segments);
            float y1 = Mathf.Lerp(topY, bottomY, (i + 1) / (float)segments);
            DrawLine(ctx,
                new Vector2(outerX[i], y0),
                new Vector2(outerX[i + 1], y1),
                AccentWeak, 1f);
        }
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

    // ====================================================================
    // Round 9.2 authoritative geometry helpers (shared by text layout,
    // panel-cap validation and text-slot/staircase validation)
    // ====================================================================

    /// <summary>Horizontal text range of a parallel-cap panel cross-section at
    /// scanline y. This is the AUTHORITATIVE slot bound for every text row:
    /// text rows must lie between minX and maxX with at least 'padding' from
    /// the panel's real inner/outer circle edges. Returns false when the
    /// panel does not exist at that y.</summary>
    public static bool GetPanelHorizontalRange(
        bool leftSide,
        float y,
        float innerRadius,
        float outerRadius,
        float padding,
        out float minX,
        out float maxX)
    {
        minX = 0f;
        maxX = 0f;

        float dy = y - Center.y;

        float innerSq = innerRadius * innerRadius - dy * dy;
        float outerSq = outerRadius * outerRadius - dy * dy;
        if (outerSq <= 0f)
        {
            return false;
        }

        float innerDx = Mathf.Sqrt(Mathf.Max(0f, innerSq));
        float outerDx = Mathf.Sqrt(outerSq);

        if (leftSide)
        {
            minX = Center.x - outerDx + padding;
            maxX = Center.x - innerDx - padding;
        }
        else
        {
            minX = Center.x + innerDx + padding;
            maxX = Center.x + outerDx - padding;
        }

        return maxX > minX;
    }

    /// <summary>Row centres of one parallel panel across a scanline y: the
    /// middle of the real panel cross-section (after padding). Used by the
    /// staircase layout so the stairs emerge from geometry, never from
    /// hand-added indentation offsets.</summary>
    public static bool TryGetPanelRowCentre(
        bool leftSide,
        float y,
        float padding,
        out float centreX,
        out float minX,
        out float maxX)
    {
        if (!GetPanelHorizontalRange(leftSide, y,
                PanelInnerRadius, PanelOuterRadius, padding,
                out minX, out maxX))
        {
            centreX = 0f;
            return false;
        }

        centreX = (minX + maxX) * 0.5f;
        return true;
    }

    /// <summary>True when the row centre Y falls inside one of the two panel
    /// bands (upper 205..345 or lower 455..595).</summary>
    public static bool YInsidePanelBand(float y)
    {
        return (y >= UpperPanelTopY && y <= UpperPanelBottomY)
               || (y >= LowerPanelTopY && y <= LowerPanelBottomY);
    }

    /// <summary>The un-padded panel cross-section endpoints at scanline y:
    /// innerX (closest to the centre, radius PanelInnerRadius) and outerX
    /// (farthest, radius PanelOuterRadius) on the given side. This mirrors the
    /// exact math the parallel-cap mesh rows use, so validation and drawing
    /// can never drift apart. Returns false when the outer circle does not
    /// reach that y.</summary>
    public static bool GetPanelCrossSection(
        bool leftSide,
        float y,
        out float innerX,
        out float outerX)
    {
        innerX = 0f;
        outerX = 0f;

        float dy = y - Center.y;
        float outerSq = PanelOuterRadius * PanelOuterRadius - dy * dy;
        if (outerSq <= 0f)
        {
            return false;
        }

        float innerDx = Mathf.Sqrt(Mathf.Max(0f,
            PanelInnerRadius * PanelInnerRadius - dy * dy));
        float outerDx = Mathf.Sqrt(outerSq);

        if (leftSide)
        {
            innerX = Center.x - innerDx;
            outerX = Center.x - outerDx;
        }
        else
        {
            innerX = Center.x + innerDx;
            outerX = Center.x + outerDx;
        }

        return true;
    }

    /// <summary>Top and bottom cap endpoints of one parallel-cap panel
    /// (annulus 248..365 x band [topY..bottomY] on one side). The top cap runs
    /// horizontally from (innerX(topY), topY) to (outerX(topY), topY), the
    /// bottom cap at bottomY. Both caps share the exact same Y at both ends,
    /// so top and bottom edges are horizontal and therefore parallel.</summary>
    public static bool GetPanelCapEndpoints(
        bool leftSide,
        float topY,
        float bottomY,
        out Vector2 topInner,
        out Vector2 topOuter,
        out Vector2 bottomInner,
        out Vector2 bottomOuter)
    {
        topInner = Vector2.zero;
        topOuter = Vector2.zero;
        bottomInner = Vector2.zero;
        bottomOuter = Vector2.zero;

        if (!GetPanelCrossSection(leftSide, topY, out float topInX, out float topOutX))
        {
            return false;
        }

        if (!GetPanelCrossSection(leftSide, bottomY, out float bottomInX, out float bottomOutX))
        {
            return false;
        }

        topInner = new Vector2(topInX, topY);
        topOuter = new Vector2(topOutX, topY);
        bottomInner = new Vector2(bottomInX, bottomY);
        bottomOuter = new Vector2(bottomOutX, bottomY);
        return true;
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
