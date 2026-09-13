using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The payload crate mark of the Warehouse page (Phase 09B PASS 9).
///
/// The warehouse reuses the project's EXISTING item vocabulary so one item never
/// shows two different icons across screens:
///
///   矿物 / 燃料 / 合金      ProcessingSketchIcon (the Processing page's marks)
///   采集器 / 电池 / 无人机   PreparationSketchIcon (the Preparation page's marks)
///   载荷                    THIS crate - the payload mass has no counterpart on
///                           another page, so it carries its own minimal mark
///
/// Like every other mark in the project it is code-drawn (Painter2D, no
/// textures, no imported art) in a shared 100 x 100 space, so the same element
/// renders the small cell mark and the enlarged detail mark.
/// </summary>
public sealed class WarehouseItemIcon : VisualElement
{
    /// <summary>Class every mark carries, so hosts size it from USS.</summary>
    public const string IconClass = "warehouse-icon";

    private static readonly Color Ink = new Color(0.17f, 0.20f, 0.24f, 1f);
    private static readonly Color InkSoft = new Color(0.42f, 0.46f, 0.50f, 1f);
    private static readonly Color Accent = new Color(0.05f, 0.50f, 0.55f, 1f);

    public WarehouseItemIcon()
    {
        pickingMode = PickingMode.Ignore;
        AddToClassList(IconClass);
        generateVisualContent += Paint;
    }

    private void Paint(MeshGenerationContext context)
    {
        Rect bounds = contentRect;
        if (bounds.width < 4f || bounds.height < 4f)
        {
            return;
        }

        Painter2D pen = context.painter2D;
        float size = Mathf.Min(bounds.width, bounds.height);
        float ox = (bounds.width - size) * 0.5f;
        float oy = (bounds.height - size) * 0.5f;
        pen.lineJoin = LineJoin.Round;

        // Crate: lid, body, latch.
        Polygon(pen, size, ox, oy, InkSoft, 18f, 26f, 82f, 26f, 82f, 40f, 18f, 40f);
        Polygon(pen, size, ox, oy, Ink, 22f, 40f, 78f, 40f, 78f, 84f, 22f, 84f);
        Polygon(pen, size, ox, oy, Accent, 46f, 40f, 54f, 40f, 54f, 56f, 46f, 56f);
    }

    private static Vector2 P(float size, float ox, float oy, float x, float y)
    {
        return new Vector2(ox + size * x * 0.01f, oy + size * y * 0.01f);
    }

    private static void Polygon(Painter2D pen, float size, float ox, float oy,
        Color color, params float[] xy)
    {
        pen.fillColor = color;
        pen.BeginPath();
        pen.MoveTo(P(size, ox, oy, xy[0], xy[1]));
        for (int i = 2; i + 1 < xy.Length; i += 2)
        {
            pen.LineTo(P(size, ox, oy, xy[i], xy[i + 1]));
        }

        pen.ClosePath();
        pen.Fill();
    }
}
