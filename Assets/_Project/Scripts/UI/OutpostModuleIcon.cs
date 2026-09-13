using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The three outpost module marks (Phase 09B PASS 6; redrawn against the
/// approved reference in the same pass).
///
/// The reference shows chunky, mostly FILLED marks instead of hairline
/// outlines, so each icon is built from two or three flat facets plus a couple
/// of accent strokes - still pure Painter2D in the shared 100 x 72 drawing
/// space, still rebuildable from a five-line SVG:
///
///   Core       中枢核心 - an isometric module cube (three faces) with a port
///   Harvester  钻采     - a drill bit on a mount, with debris triangles
///   Refinery   精炼炉   - a furnace body with a dome, a stack and heat marks
///
/// One graphite ink carries the silhouette (like the reference); a single
/// accent detail per mark keeps it on-palette without becoming decorative.
/// </summary>
public sealed class OutpostModuleIcon : VisualElement
{
    public enum Kind
    {
        Core,
        Harvester,
        Refinery
    }

    /// <summary>Class every drawing carries, so a host sizes it from USS.</summary>
    public const string SketchClass = "outpost-sketch";

    private const float SpaceW = 100f;
    private const float SpaceH = 72f;

    // Graphite family: the marks read as one material set.
    private static readonly Color BodyDark = new Color(0.16f, 0.20f, 0.25f, 1f);
    private static readonly Color BodyMid = new Color(0.23f, 0.28f, 0.34f, 1f);
    private static readonly Color BodyLight = new Color(0.33f, 0.39f, 0.45f, 1f);

    private static readonly Color CoreAccent = new Color(0.05f, 0.50f, 0.55f, 1f);
    private static readonly Color HarvesterAccent = new Color(0.05f, 0.50f, 0.55f, 1f);
    private static readonly Color RefineryAccent = new Color(0.86f, 0.47f, 0.10f, 1f);

    private readonly Kind _kind;

    public OutpostModuleIcon(Kind kind)
    {
        _kind = kind;
        pickingMode = PickingMode.Ignore;
        AddToClassList(SketchClass);
        generateVisualContent += Paint;
    }

    /// <summary>Accent colour of a module mark.</summary>
    public static Color Accent(Kind kind)
    {
        switch (kind)
        {
            case Kind.Harvester:
                return HarvesterAccent;
            case Kind.Refinery:
                return RefineryAccent;
            default:
                return CoreAccent;
        }
    }

    private void Paint(MeshGenerationContext context)
    {
        Rect bounds = contentRect;
        if (bounds.width < 2f || bounds.height < 2f)
        {
            return;
        }

        Painter2D pen = context.painter2D;
        float scale = Mathf.Max(bounds.width / SpaceW, 0.01f);
        pen.lineWidth = Mathf.Clamp(1.2f / scale, 0.5f, 6f);
        pen.lineJoin = LineJoin.Round;

        switch (_kind)
        {
            case Kind.Core:
                PaintCore(pen, bounds);
                break;
            case Kind.Harvester:
                PaintHarvester(pen, bounds);
                break;
            default:
                PaintRefinery(pen, bounds);
                break;
        }
    }

    /// <summary>中枢核心: an isometric module cube with a port on its front face.</summary>
    private static void PaintCore(Painter2D pen, Rect bounds)
    {
        // Top face, left face, right face: three flat facets give the volume.
        Facet(pen, bounds, BodyLight, 50f, 8f, 78f, 22f, 50f, 36f, 22f, 22f);
        Facet(pen, bounds, BodyMid, 22f, 22f, 50f, 36f, 50f, 64f, 22f, 50f);
        Facet(pen, bounds, BodyDark, 78f, 22f, 78f, 50f, 50f, 64f, 50f, 36f);

        pen.strokeColor = BodyDark;
        Line(pen, bounds, 50f, 8f, 78f, 22f, 78f, 50f, 50f, 64f, 22f, 50f, 22f, 22f, 50f, 8f);
        Line(pen, bounds, 50f, 36f, 50f, 64f);
        Line(pen, bounds, 22f, 22f, 50f, 36f, 78f, 22f);

        // Port on the front-left face (the module's one accent).
        pen.strokeColor = CoreAccent;
        pen.fillColor = new Color(CoreAccent.r, CoreAccent.g, CoreAccent.b, 0.18f);
        Polygon(pen, bounds, 36f, 36f, 44f, 32f, 44f, 46f, 36f, 50f);
    }

    /// <summary>钻采: a drill bit on a mount, with the debris the reference shows.</summary>
    private static void PaintHarvester(Painter2D pen, Rect bounds)
    {
        // Mount plate.
        Facet(pen, bounds, BodyLight, 30f, 14f, 70f, 14f, 70f, 24f, 30f, 24f);
        pen.strokeColor = BodyDark;
        Line(pen, bounds, 30f, 14f, 70f, 14f, 70f, 24f, 30f, 24f, 30f, 14f);

        // Shaft.
        Facet(pen, bounds, BodyMid, 42f, 24f, 58f, 24f, 58f, 38f, 42f, 38f);

        // Bit: a filled cone with two cut lines.
        Facet(pen, bounds, BodyDark, 42f, 38f, 58f, 38f, 50f, 62f);
        pen.strokeColor = HarvesterAccent;
        Line(pen, bounds, 44f, 44f, 56f, 44f);
        Line(pen, bounds, 46f, 51f, 54f, 51f);

        // Debris triangles under the bit.
        pen.fillColor = BodyMid;
        Triangle(pen, bounds, 36f, 66f, 44f, 66f, 40f, 59f);
        Triangle(pen, bounds, 48f, 68f, 56f, 68f, 52f, 61f);
        pen.fillColor = BodyLight;
        Triangle(pen, bounds, 58f, 65f, 66f, 65f, 62f, 58f);
    }

    /// <summary>精炼炉: a furnace body with a dome, a stack and heat marks.</summary>
    private static void PaintRefinery(Painter2D pen, Rect bounds)
    {
        // Body.
        Facet(pen, bounds, BodyMid, 24f, 34f, 62f, 34f, 62f, 62f, 24f, 62f);
        pen.strokeColor = BodyDark;
        Line(pen, bounds, 24f, 34f, 62f, 34f, 62f, 62f, 24f, 62f, 24f, 34f);

        // Dome on top.
        pen.fillColor = BodyLight;
        Arc(pen, bounds, 43f, 34f, 19f, 180f, 180f, true);
        pen.strokeColor = BodyDark;
        Arc(pen, bounds, 43f, 34f, 19f, 180f, 180f, false);

        // Stack with two bubbles.
        Facet(pen, bounds, BodyDark, 64f, 20f, 76f, 20f, 76f, 46f, 64f, 46f);
        pen.fillColor = BodyLight;
        CircleFilled(pen, Point(bounds, 70f, 13f), 3.4f);
        CircleFilled(pen, Point(bounds, 78f, 9f), 2.4f);

        // Heat line + port.
        pen.strokeColor = RefineryAccent;
        Line(pen, bounds, 30f, 56f, 56f, 56f);
        Line(pen, bounds, 30f, 66f, 56f, 66f);
        pen.strokeColor = BodyDark;
        Circle(pen, bounds, 36f, 44f, 5f);
    }

    private static void Circle(Painter2D pen, Rect bounds, float cx, float cy, float radius)
    {
        pen.BeginPath();
        for (int i = 0; i <= 24; i++)
        {
            float a = i * Mathf.PI * 2f / 24f;
            Vector2 point = Point(bounds, cx + radius * Mathf.Cos(a), cy + radius * Mathf.Sin(a));
            if (i == 0)
            {
                pen.MoveTo(point);
            }
            else
            {
                pen.LineTo(point);
            }
        }

        pen.Stroke();
    }

    private static void CircleFilled(Painter2D pen, Vector2 centre, float radius)
    {
        pen.BeginPath();
        for (int i = 0; i <= 20; i++)
        {
            float a = i * Mathf.PI * 2f / 20f;
            Vector2 point = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            if (i == 0)
            {
                pen.MoveTo(point);
            }
            else
            {
                pen.LineTo(point);
            }
        }

        pen.ClosePath();
        pen.Fill();
    }

    /// <summary>Strokes or fills an arc of the drawing space (degrees, 0 = +x,
    /// positive sweeps downwards in panel space).</summary>
    private static void Arc(Painter2D pen, Rect bounds, float cx, float cy, float radius,
        float startDegrees, float sweepDegrees, bool fill)
    {
        int steps = Mathf.Max(6, Mathf.CeilToInt(Mathf.Abs(sweepDegrees) / 12f));
        pen.BeginPath();
        for (int i = 0; i <= steps; i++)
        {
            float a = (startDegrees + sweepDegrees * i / steps) * Mathf.Deg2Rad;
            Vector2 point = Point(bounds, cx + radius * Mathf.Cos(a), cy + radius * Mathf.Sin(a));
            if (i == 0)
            {
                pen.MoveTo(point);
            }
            else
            {
                pen.LineTo(point);
            }
        }

        if (fill)
        {
            pen.ClosePath();
            pen.Fill();
        }
        else
        {
            pen.Stroke();
        }
    }

    private static void Triangle(Painter2D pen, Rect bounds,
        float ax, float ay, float bx, float by, float cx, float cy)
    {
        pen.BeginPath();
        pen.MoveTo(Point(bounds, ax, ay));
        pen.LineTo(Point(bounds, bx, by));
        pen.LineTo(Point(bounds, cx, cy));
        pen.ClosePath();
        pen.Fill();
    }

    private static void Polygon(Painter2D pen, Rect bounds, params float[] xy)
    {
        if (xy.Length < 6)
        {
            return;
        }

        pen.BeginPath();
        pen.MoveTo(Point(bounds, xy[0], xy[1]));
        for (int i = 2; i + 1 < xy.Length; i += 2)
        {
            pen.LineTo(Point(bounds, xy[i], xy[i + 1]));
        }

        pen.ClosePath();
        pen.Fill();
        pen.Stroke();
    }

    private static void Facet(Painter2D pen, Rect bounds, Color color, params float[] xy)
    {
        pen.fillColor = color;
        Polygon(pen, bounds, xy);
    }

    /// <summary>Strokes a polyline given in the 100 x 72 drawing space.</summary>
    private static void Line(Painter2D pen, Rect bounds, params float[] xy)
    {
        pen.BeginPath();
        for (int i = 0; i < xy.Length; i += 2)
        {
            Vector2 point = Point(bounds, xy[i], xy[i + 1]);
            if (i == 0)
            {
                pen.MoveTo(point);
            }
            else
            {
                pen.LineTo(point);
            }
        }

        pen.Stroke();
    }

    private static Vector2 Point(Rect bounds, float x, float y)
    {
        return new Vector2(bounds.x + bounds.width * x / SpaceW,
            bounds.y + bounds.height * y / SpaceH);
    }
}
