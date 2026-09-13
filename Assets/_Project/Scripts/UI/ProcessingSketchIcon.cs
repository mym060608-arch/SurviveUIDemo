using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Small procedural line drawings for the Processing / 资源加工 page only
/// (Phase 09B PASS 5).
///
/// The page needs exactly three material marks, and they are deliberately
/// primitive - the review contract is "an artist can rebuild this from Unity
/// primitives / a five-line SVG", so there is no high-poly machinery, no PBR
/// shading and no imported art:
///
///   Ore     a simple faceted mineral chunk (one flat polygon + two facet lines)
///   Fuel    a simple canister with a flame mark on its label
///   Alloy   a simple metal ingot (a trapezoid bar with a top face)
///
/// Same drawing technique as <see cref="PreparationSketchIcon"/>: a 100 x 72
/// local space so one drawing works at every host size, a pen width that stays
/// ~1.1 px on screen, and flat facet fills instead of gradients.
/// </summary>
public sealed class ProcessingSketchIcon : VisualElement
{
    public enum Kind
    {
        Ore,
        Fuel,
        Alloy
    }

    /// <summary>Class every drawing carries, so a host sizes it from USS.</summary>
    public const string SketchClass = "process-sketch";

    private const float SpaceW = 100f;
    private const float SpaceH = 72f;

    private static readonly Color OreInk = new Color(0.36f, 0.42f, 0.47f, 0.95f);
    private static readonly Color FuelInk = new Color(0.80f, 0.45f, 0.14f, 0.95f);
    private static readonly Color AlloyInk = new Color(0.16f, 0.44f, 0.49f, 0.95f);

    private static readonly Color OreFacet = new Color(0.55f, 0.62f, 0.68f, 0.20f);
    private static readonly Color FuelFacet = new Color(0.90f, 0.55f, 0.20f, 0.16f);
    private static readonly Color AlloyFacet = new Color(0.24f, 0.55f, 0.60f, 0.16f);

    private readonly Kind _kind;

    /// <summary>Heavier pen + facet fills, used by the flow nodes and the
    /// current-recipe mark (the small list/inventory marks stay hairline).</summary>
    private readonly bool _hero;

    public ProcessingSketchIcon(Kind kind, bool hero = false)
    {
        _kind = kind;
        _hero = hero;
        pickingMode = PickingMode.Ignore;
        AddToClassList(SketchClass);
        generateVisualContent += Paint;
    }

    /// <summary>Accent colour of a material mark (used by labels/rules that
    /// belong to the same material and must not import a second palette).</summary>
    public static Color Accent(Kind kind)
    {
        return Ink(kind);
    }

    private static Color Ink(Kind kind)
    {
        switch (kind)
        {
            case Kind.Fuel: return FuelInk;
            case Kind.Alloy: return AlloyInk;
            default: return OreInk;
        }
    }

    private static Color Facet(Kind kind)
    {
        switch (kind)
        {
            case Kind.Fuel: return FuelFacet;
            case Kind.Alloy: return AlloyFacet;
            default: return OreFacet;
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
        float target = _hero ? 1.6f : 1.1f;
        pen.strokeColor = Ink(_kind);
        pen.lineWidth = Mathf.Clamp(target / scale, 0.5f, 6f);
        pen.lineJoin = LineJoin.Round;

        switch (_kind)
        {
            case Kind.Ore:
                PaintOre(pen, bounds);
                break;
            case Kind.Fuel:
                PaintFuel(pen, bounds);
                break;
            case Kind.Alloy:
                PaintAlloy(pen, bounds);
                break;
        }
    }

    /// <summary>矿物 // 简单多边形石块: one hexagonal chunk, a flat lit facet
    /// and a single cleavage line. No bumps, no texture.</summary>
    private static void PaintOre(Painter2D pen, Rect bounds)
    {
        Facet(pen, bounds, OreFacet, 22, 34, 38, 15, 68, 16, 82, 36, 66, 58, 36, 57);
        Line(pen, bounds, 22, 34, 38, 15, 68, 16, 82, 36, 66, 58, 36, 57, 22, 34);
        Line(pen, bounds, 38, 15, 44, 36, 36, 57);
        Line(pen, bounds, 44, 36, 82, 36);
    }

    /// <summary>燃料 // 简单罐体 + 火焰符号: a canister with a neck, a handle
    /// notch and a flame mark. The flame is three straight strokes, not a
    /// particle-shaped blob.</summary>
    private static void PaintFuel(Painter2D pen, Rect bounds)
    {
        Facet(pen, bounds, FuelFacet, 28, 24, 62, 24, 62, 60, 28, 60);
        Line(pen, bounds, 28, 24, 62, 24, 62, 60, 28, 60, 28, 24);
        // Neck + cap.
        Line(pen, bounds, 40, 24, 40, 16, 50, 16, 50, 24);
        Line(pen, bounds, 36, 16, 54, 16);
        // Handle notch on the shoulder.
        Line(pen, bounds, 28, 31, 22, 31, 22, 44, 28, 44);
        // Flame mark: two rising strokes with a wide base.
        Line(pen, bounds, 45, 56, 39, 47, 42, 40, 45, 45, 48, 38, 51, 47, 45, 56);
        // Contents rule (the canister reads as filled).
        Line(pen, bounds, 28, 50, 62, 50);
    }

    /// <summary>合金 // 简单金属锭: a trapezoid bar with a top face and one
    /// centre seam. The classic ingot silhouette, nothing more.</summary>
    private static void PaintAlloy(Painter2D pen, Rect bounds)
    {
        Facet(pen, bounds, AlloyFacet, 26, 30, 74, 30, 64, 54, 36, 54);
        Line(pen, bounds, 26, 30, 74, 30, 64, 54, 36, 54, 26, 30);
        // Top face (the ingot narrows towards the top).
        Line(pen, bounds, 26, 30, 34, 22, 66, 22, 74, 30);
        Line(pen, bounds, 50, 22, 50, 30);
        Line(pen, bounds, 50, 30, 50, 54);
    }

    /// <summary>Fills one flat facet polygon.</summary>
    private static void Facet(Painter2D pen, Rect bounds, Color color, params float[] xy)
    {
        if (xy.Length < 6)
        {
            return;
        }

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
