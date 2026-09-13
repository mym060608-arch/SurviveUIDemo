using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Small procedural line drawings for the Preparation page only.</summary>
public sealed class PreparationSketchIcon : VisualElement
{
    public enum Kind
    {
        Temperature, Gravity, Radiation, Atmosphere, Surface,
        Harvester, Battery, Drone, Resource
    }

    /// <summary>Class every drawing carries, so a host sizes it from USS.</summary>
    public const string SketchClass = "prep-sketch";

    /// <summary>Flat tint of the crystal facet that faces the light.</summary>
    private static readonly Color FacetTint = new Color(0.72f, 0.80f, 0.86f, 0.14f);

    private readonly Kind _kind;

    public PreparationSketchIcon(Kind kind)
    {
        _kind = kind;
        pickingMode = PickingMode.Ignore;
        // The drawing fills whatever host box it is mounted in; the fill is a
        // USS rule ("prep-sketch") rather than an inline style, because an
        // inline flex-grow outranks every layout rule and stretched the mark
        // inside the round resource token.
        AddToClassList(SketchClass);
        generateVisualContent += Paint;
    }

    private void Paint(MeshGenerationContext context)
    {
        Rect bounds = contentRect;
        if (bounds.width < 2f || bounds.height < 2f) return;
        Painter2D pen = context.painter2D;
        pen.strokeColor = StrokeColor(_kind);

        // The drawings live in a 100 x 72 space, so a fixed pen width would be a
        // different weight on every host. Targeting ~1.1 px on screen keeps the
        // engraved line identical on the 26 px telemetry marks and the 128 px
        // equipment sketches.
        float scale = Mathf.Max(bounds.width / 100f, 0.01f);
        pen.lineWidth = Mathf.Clamp(1.1f / scale, 0.6f, 6f);

        switch (_kind)
        {
            case Kind.Temperature:
                Line(pen, bounds, 48, 13, 48, 51);
                Line(pen, bounds, 48, 13, 56, 13, 56, 49);
                Circle(pen, bounds, 52, 55, 11);
                Line(pen, bounds, 52, 35, 52, 56);
                break;
            case Kind.Gravity:
                Circle(pen, bounds, 50, 36, 23);
                Line(pen, bounds, 50, 17, 50, 51);
                Line(pen, bounds, 42, 43, 50, 51, 58, 43);
                break;
            case Kind.Radiation:
                Circle(pen, bounds, 50, 36, 5);
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.PI * 2f / 3f;
                    Line(pen, bounds, 50 + 11 * Mathf.Cos(a), 36 + 11 * Mathf.Sin(a),
                        50 + 25 * Mathf.Cos(a), 36 + 25 * Mathf.Sin(a));
                }
                break;
            case Kind.Atmosphere:
                Line(pen, bounds, 25, 47, 20, 41, 23, 34, 31, 31, 38, 33,
                    43, 22, 52, 20, 61, 27, 61, 34, 72, 35, 79, 43, 75, 49, 25, 49);
                break;
            case Kind.Surface:
                Line(pen, bounds, 17, 55, 34, 30, 43, 41, 55, 18, 80, 55, 17, 55);
                Line(pen, bounds, 34, 30, 39, 36, 43, 34);
                break;
            case Kind.Harvester:
                Line(pen, bounds, 24, 24, 70, 24, 78, 34, 74, 54, 23, 54, 19, 35, 24, 24);
                Line(pen, bounds, 31, 30, 40, 30, 40, 47, 31, 47, 31, 30);
                Line(pen, bounds, 47, 30, 65, 30, 65, 37, 47, 37, 47, 30);
                Line(pen, bounds, 27, 54, 20, 63, 32, 63, 36, 54);
                Line(pen, bounds, 63, 54, 68, 63, 81, 63, 73, 54);
                Line(pen, bounds, 73, 34, 86, 34, 86, 50, 75, 50);
                break;
            case Kind.Battery:
                Line(pen, bounds, 18, 25, 56, 25, 62, 32, 62, 56, 18, 56, 18, 25);
                Line(pen, bounds, 62, 30, 75, 30, 80, 36, 80, 56, 62, 56);
                Line(pen, bounds, 27, 34, 42, 34, 42, 47, 27, 47, 27, 34);
                Line(pen, bounds, 49, 34, 55, 34, 55, 47, 49, 47);
                Line(pen, bounds, 28, 21, 28, 25);
                Line(pen, bounds, 39, 21, 39, 25);
                break;
            case Kind.Drone:
                Line(pen, bounds, 39, 32, 61, 32, 66, 48, 34, 48, 39, 32);
                Line(pen, bounds, 39, 35, 25, 27, 18, 27);
                Line(pen, bounds, 61, 35, 75, 27, 82, 27);
                Line(pen, bounds, 39, 45, 25, 54, 18, 54);
                Line(pen, bounds, 61, 45, 75, 54, 82, 54);
                Circle(pen, bounds, 18, 27, 9); Circle(pen, bounds, 82, 27, 9);
                Circle(pen, bounds, 18, 54, 9); Circle(pen, bounds, 82, 54, 9);
                Circle(pen, bounds, 50, 40, 4);
                break;
            case Kind.Resource:
                // Mineral cluster: one tall quartz point with a shorter one
                // beside it. The flat facet fills are what make it read as a
                // solid mineral instead of an outlined box.
                Facet(pen, bounds, FacetTint, 35, 5, 49, 21, 49, 51, 35, 63);
                Facet(pen, bounds, FacetTint, 70, 27, 79, 37, 79, 55, 70, 64);
                Line(pen, bounds, 35, 5, 21, 21, 21, 51, 35, 63, 49, 51, 49, 21, 35, 5);
                Line(pen, bounds, 21, 21, 49, 21);
                Line(pen, bounds, 35, 5, 35, 63);
                Line(pen, bounds, 70, 27, 61, 37, 61, 55, 70, 64, 79, 55, 79, 37, 70, 27);
                Line(pen, bounds, 70, 27, 70, 64);
                break;
        }
    }

    /// <summary>Line colour on the dark terminal panel: cool grey ink for a
    /// known reading, restrained amber for the one real caution, and a dim grey
    /// for the reading that is still unknown.</summary>
    private static Color StrokeColor(Kind kind)
    {
        switch (kind)
        {
            case Kind.Radiation:
                return new Color(0.80f, 0.60f, 0.36f, 0.95f);
            case Kind.Surface:
                return new Color(0.47f, 0.55f, 0.62f, 0.85f);
            default:
                return new Color(0.72f, 0.80f, 0.86f, 0.90f);
        }
    }

    private static void Circle(Painter2D pen, Rect bounds, float x, float y, float radius)
    {
        pen.BeginPath();
        for (int i = 0; i <= 24; i++)
        {
            float a = i * Mathf.PI * 2f / 24f;
            Vector2 point = Point(bounds, x + radius * Mathf.Cos(a), y + radius * Mathf.Sin(a));
            if (i == 0) pen.MoveTo(point); else pen.LineTo(point);
        }
        pen.Stroke();
    }

    /// <summary>Fills one flat facet polygon (used by the mineral cluster).</summary>
    private static void Facet(Painter2D pen, Rect bounds, Color color, params float[] xy)
    {
        if (xy.Length < 6) return;
        pen.fillColor = color;
        pen.BeginPath();
        pen.MoveTo(Point(bounds, xy[0], xy[1]));
        for (int i = 2; i + 1 < xy.Length; i += 2)
        {
            pen.LineTo(Point(bounds, xy[i], xy[i + 1]));
        }

        pen.ClosePath();
        pen.Fill();
    }

    private static void Line(Painter2D pen, Rect bounds, params float[] xy)
    {
        pen.BeginPath();
        for (int i = 0; i < xy.Length; i += 2)
        {
            Vector2 point = Point(bounds, xy[i], xy[i + 1]);
            if (i == 0) pen.MoveTo(point); else pen.LineTo(point);
        }
        pen.Stroke();
    }

    private static Vector2 Point(Rect bounds, float x, float y)
    {
        return new Vector2(bounds.x + bounds.width * x / 100f,
            bounds.y + bounds.height * y / 72f);
    }
}
