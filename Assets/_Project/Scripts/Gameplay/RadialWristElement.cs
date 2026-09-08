using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Custom-drawn radial backdrop for the wrist hologram (Phase 06 Revision
/// round 8). Everything annular is painted once per repaint into the wrist
/// RenderTexture layout (1280x800): translucent circular map disc + distance
/// rings + cross reference, health/satiety value arcs, the four sector
/// outlines and short radial separators. No rectangular cards, no tick dot
/// fields, no extra UI framework.
///
/// Angle convention: 0 deg = 12 o'clock, increasing clockwise. A canvas point
/// for radius r is (cx + r*sin(a), cy - r*cos(a)) in UI coordinates (y down).
/// </summary>
public class RadialWristElement : VisualElement
{
    public const int CanvasWidth = 1280;
    public const int CanvasHeight = 800;
    public const float CenterX = 640f;
    public const float CenterY = 400f;
    public const float MapRadius = 250f;

    // Annular band outside the map disc.
    public const float RingInner = 285f;
    public const float RingOuter = 370f;

    public const int MapLeft = 390; // kept in sync for marker layer
    public const int MapTop = 150;
    public const int MapSize = 500;

    // Sector arcs (degrees, clockwise from 12 o'clock).
    private static readonly Vector2[] SectorAngles =
    {
        new Vector2(45f, 135f),   // health (top)
        new Vector2(135f, 195f),  // equip (upper left)
        new Vector2(195f, 235f),  // nav (lower left)
        new Vector2(235f, 305f),  // satiety (bottom)
        new Vector2(305f, 345f),  // harvest (lower right)
        new Vector2(345f, 405f)   // state (upper right, wraps)
    };

    private static readonly Color MapFill = new Color(0.55f, 0.60f, 0.63f, 0.20f);
    private static readonly Color MapRingColor = new Color(0.45f, 0.55f, 0.58f, 0.55f);
    private static readonly Color ArcColor = new Color(0.055f, 0.5f, 0.55f, 0.95f);
    private static readonly Color SectorColor = new Color(0.10f, 0.55f, 0.60f, 0.35f);
    private static readonly Color SeparatorColor = new Color(0.45f, 0.55f, 0.58f, 0.6f);

    public static Vector2 PointOn(float angleClockwise, float radius)
    {
        float rad = angleClockwise * Mathf.Deg2Rad;
        return new Vector2(CenterX + Mathf.Sin(rad) * radius,
            CenterY - Mathf.Cos(rad) * radius);
    }

    public RadialWristElement()
    {
        name = "WristRadialBackdrop";
        generateVisualContent += Draw;
    }

    /// <summary>Health (0..1) and satiety (0..1) active-arc fractions.</summary>
    public float HealthFraction01 { get; set; } = 1f;
    public float SatietyFraction01 { get; set; } = 0.82f;

    public void Redraw(float health01, float satiety01)
    {
        HealthFraction01 = Mathf.Clamp01(health01);
        SatietyFraction01 = Mathf.Clamp01(satiety01);
        MarkDirtyRepaint();
    }

    private void Draw(MeshGenerationContext ctx)
    {
        var paint = ctx.painter2D;

        // Translucent map disc (never opaque white).
        FillCircle(paint, CenterX, CenterY, MapRadius, MapFill);
        StrokeCircle(paint, CenterX, CenterY, MapRadius, MapRingColor, 2f);

        // Distance rings + cross/grid reference.
        StrokeCircle(paint, CenterX, CenterY, MapRadius * 0.66f, MapRingColor, 1f);
        StrokeCircle(paint, CenterX, CenterY, MapRadius * 0.33f, MapRingColor, 1f);
        paint.strokeColor = MapRingColor;
        paint.lineWidth = 1f;
        paint.BeginPath();
        paint.MoveTo(new Vector2(CenterX - MapRadius, CenterY));
        paint.LineTo(new Vector2(CenterX + MapRadius, CenterY));
        paint.MoveTo(new Vector2(CenterX, CenterY - MapRadius));
        paint.LineTo(new Vector2(CenterX, CenterY + MapRadius));
        paint.Stroke();

        // Annular sector outlines in the ring band + radial separators.
        foreach (Vector2 sector in SectorAngles)
        {
            DrawSectorArc(paint, sector.x, sector.y, false);
        }

        DrawSeparators(paint);

        // Health upper arc and Satiety lower arc with active fractions.
        float healthEnd = Mathf.Lerp(45f, 135f, HealthFraction01);
        DrawSectorArc(paint, 45f, healthEnd, true);
        float satietyStart = Mathf.Lerp(235f, 305f, 1f - SatietyFraction01);
        DrawSectorArc(paint, satietyStart, 305f, true);
    }

    private static void DrawSectorArc(Painter2D paint, float startDeg, float endDeg, bool active)
    {
        paint.lineWidth = active ? 5f : 2f;
        paint.strokeColor = active ? ArcColor : SectorColor;

        // Outer arc from start to end (both ends wrapped to < start+360).
        float start = startDeg;
        float end = endDeg > start ? endDeg : endDeg + 360f;
        List<Vector2> outer = SampleArc(start, end, RingOuter);
        if (outer.Count < 2)
        {
            return;
        }

        paint.BeginPath();
        paint.MoveTo(outer[0]);
        for (int i = 1; i < outer.Count; i++)
        {
            paint.LineTo(outer[i]);
        }

        paint.Stroke();
    }

    private static List<Vector2> SampleArc(float startDeg, float endDeg, float radius)
    {
        var points = new List<Vector2>();
        int steps = Mathf.Max(4, Mathf.CeilToInt((endDeg - startDeg) / 4f));
        for (int i = 0; i <= steps; i++)
        {
            float a = startDeg + (endDeg - startDeg) * i / steps;
            points.Add(PointOn(a, radius));
        }

        return points;
    }

    private static void DrawSeparators(Painter2D paint)
    {
        // Radial hairline separators between the six sectors.
        float[] angles = { 45f, 135f, 195f, 235f, 305f, 345f, 405f };
        paint.lineWidth = 1f;
        paint.strokeColor = SeparatorColor;
        foreach (float a in angles)
        {
            Vector2 inner = PointOn(a < 360f ? a : a - 360f, RingInner);
            Vector2 outer = PointOn(a < 360f ? a : a - 360f, RingOuter);
            paint.BeginPath();
            paint.MoveTo(inner);
            paint.LineTo(outer);
            paint.Stroke();
        }
    }

    private static void StrokeCircle(Painter2D paint, float cx, float cy,
        float radius, Color color, float width)
    {
        paint.strokeColor = color;
        paint.lineWidth = width;
        paint.BeginPath();
        paint.Arc(new Vector2(cx, cy), radius, new Angle(0), new Angle(360));
        paint.Stroke();
    }

    private static void FillCircle(Painter2D paint, float cx, float cy,
        float radius, Color color)
    {
        paint.fillColor = color;
        paint.BeginPath();
        paint.Arc(new Vector2(cx, cy), radius, new Angle(0), new Angle(360));
        paint.Fill();
    }
}
