using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The warehouse item marks (Phase 09B PASS 9).
///
/// Every icon is code-drawn (Painter2D, no textures, no imported art) in one
/// shared 100 x 100 drawing space, so the same class renders the small grid mark
/// and the enlarged detail mark - the host only changes the element size.
///
/// The vocabulary is deliberately hand-makeable: single-colour graphite
/// silhouettes with at most one accent detail, built from a handful of
/// straight edges and circles, exactly what the approved reference asks for
/// ("简单、可以手工用 SVG 或基础图形实现"). No gradients, no shading, no
/// high-detail art:
///
///   矿物  Mineral    three crystal shards
///   燃料  Fuel       a droplet
///   合金  Alloy      two stacked ingots
///   采集器 Harvester  a drill bit on a mount
///   电池  Battery    a battery cell with its terminal
///   无人机 Drone      a quad-rotor frame
///   载荷  Cargo      a supply crate
/// </summary>
public sealed class WarehouseItemIcon : VisualElement
{
    /// <summary>Class every mark carries, so hosts size it from USS.</summary>
    public const string IconClass = "warehouse-icon";

    private const float Space = 100f;

    private static readonly Color Ink = new Color(0.17f, 0.20f, 0.24f, 1f);
    private static readonly Color InkSoft = new Color(0.42f, 0.46f, 0.50f, 1f);
    private static readonly Color Accent = new Color(0.05f, 0.50f, 0.55f, 1f);

    private readonly WarehouseItemKind _kind;

    public WarehouseItemIcon(WarehouseItemKind kind)
    {
        _kind = kind;
        pickingMode = PickingMode.Ignore;
        AddToClassList(IconClass);
        generateVisualContent += Paint;
    }

    /// <summary>Which mark this element draws.</summary>
    public WarehouseItemKind Kind => _kind;

    private void Paint(MeshGenerationContext context)
    {
        Rect bounds = contentRect;
        if (bounds.width < 4f || bounds.height < 4f)
        {
            return;
        }

        Painter2D pen = context.painter2D;
        // Square drawing space: the smaller edge decides the scale, and the mark
        // stays centred in the element.
        float size = Mathf.Min(bounds.width, bounds.height);
        float offsetX = (bounds.width - size) * 0.5f;
        float offsetY = (bounds.height - size) * 0.5f;
        pen.lineJoin = LineJoin.Round;

        switch (_kind)
        {
            case WarehouseItemKind.Mineral:
                PaintMineral(pen, size, offsetX, offsetY);
                break;
            case WarehouseItemKind.Fuel:
                PaintFuel(pen, size, offsetX, offsetY);
                break;
            case WarehouseItemKind.Alloy:
                PaintAlloy(pen, size, offsetX, offsetY);
                break;
            case WarehouseItemKind.Harvester:
                PaintHarvester(pen, size, offsetX, offsetY);
                break;
            case WarehouseItemKind.Battery:
                PaintBattery(pen, size, offsetX, offsetY);
                break;
            case WarehouseItemKind.Drone:
                PaintDrone(pen, size, offsetX, offsetY);
                break;
            default:
                PaintCargo(pen, size, offsetX, offsetY);
                break;
        }
    }

    // ---- drawing helpers (all coordinates are 0..100 fractions) ----

    private static Vector2 P(float size, float offsetX, float offsetY, float x, float y)
    {
        return new Vector2(offsetX + size * x * 0.01f, offsetY + size * y * 0.01f);
    }

    private static void Polygon(Painter2D pen, float size, float offsetX, float offsetY,
        Color color, params float[] xy)
    {
        pen.fillColor = color;
        pen.BeginPath();
        pen.MoveTo(P(size, offsetX, offsetY, xy[0], xy[1]));
        for (int i = 2; i + 1 < xy.Length; i += 2)
        {
            pen.LineTo(P(size, offsetX, offsetY, xy[i], xy[i + 1]));
        }

        pen.ClosePath();
        pen.Fill();
    }

    private static void Stroke(Painter2D pen, float size, float offsetX, float offsetY,
        Color color, float width, params float[] xy)
    {
        pen.strokeColor = color;
        pen.lineWidth = Mathf.Max(1f, size * width * 0.01f);
        pen.BeginPath();
        for (int i = 0; i + 1 < xy.Length; i += 2)
        {
            Vector2 point = P(size, offsetX, offsetY, xy[i], xy[i + 1]);
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

    /// <summary>矿物: three crystal shards of different heights.</summary>
    private static void PaintMineral(Painter2D pen, float size, float ox, float oy)
    {
        // Tall centre shard.
        Polygon(pen, size, ox, oy, Ink,
            50f, 12f, 62f, 40f, 62f, 84f, 50f, 94f, 38f, 84f, 38f, 40f);
        // Left shard.
        Polygon(pen, size, ox, oy, InkSoft,
            30f, 34f, 40f, 52f, 40f, 84f, 30f, 92f, 20f, 84f, 20f, 52f);
        // Right shard.
        Polygon(pen, size, ox, oy, Ink,
            70f, 28f, 80f, 48f, 80f, 84f, 70f, 92f, 60f, 84f, 60f, 48f);
        // One accent facet on the centre shard.
        Stroke(pen, size, ox, oy, Accent, 1.6f, 50f, 20f, 50f, 88f);
    }

    /// <summary>燃料: a single droplet with a highlight.</summary>
    private static void PaintFuel(Painter2D pen, float size, float ox, float oy)
    {
        pen.fillColor = Ink;
        pen.BeginPath();
        pen.MoveTo(P(size, ox, oy, 50f, 12f));
        pen.BezierCurveTo(
            P(size, ox, oy, 50f, 34f), P(size, ox, oy, 82f, 44f), P(size, ox, oy, 82f, 62f));
        pen.Arc(P(size, ox, oy, 50f, 62f), size * 0.32f, 0f, 180f);
        pen.BezierCurveTo(
            P(size, ox, oy, 18f, 44f), P(size, ox, oy, 50f, 34f), P(size, ox, oy, 50f, 12f));
        pen.ClosePath();
        pen.Fill();

        Stroke(pen, size, ox, oy, Accent, 1.6f, 38f, 58f, 38f, 70f);
    }

    /// <summary>合金: two stacked ingots.</summary>
    private static void PaintAlloy(Painter2D pen, float size, float ox, float oy)
    {
        // Lower ingot.
        Polygon(pen, size, ox, oy, Ink,
            16f, 66f, 30f, 54f, 84f, 54f, 70f, 66f, 56f, 78f, 30f, 78f);
        // Upper ingot.
        Polygon(pen, size, ox, oy, InkSoft,
            30f, 44f, 44f, 32f, 84f, 32f, 70f, 44f, 56f, 56f, 44f, 56f);
        Stroke(pen, size, ox, oy, Accent, 1.6f, 32f, 68f, 70f, 68f);
    }

    /// <summary>采集器: a drill bit on its mount.</summary>
    private static void PaintHarvester(Painter2D pen, float size, float ox, float oy)
    {
        // Mount plate.
        Polygon(pen, size, ox, oy, InkSoft, 26f, 14f, 74f, 14f, 74f, 28f, 26f, 28f);
        // Shaft.
        Polygon(pen, size, ox, oy, Ink, 44f, 28f, 56f, 28f, 56f, 52f, 44f, 52f);
        // Drill cone.
        Polygon(pen, size, ox, oy, Ink, 30f, 52f, 70f, 52f, 50f, 90f);
        // Debris.
        Polygon(pen, size, ox, oy, Accent, 22f, 62f, 30f, 58f, 28f, 68f);
        Polygon(pen, size, ox, oy, Accent, 78f, 62f, 70f, 58f, 72f, 68f);
    }

    /// <summary>电池: a cell with its terminal.</summary>
    private static void PaintBattery(Painter2D pen, float size, float ox, float oy)
    {
        // Terminal.
        Polygon(pen, size, ox, oy, Ink, 42f, 12f, 58f, 12f, 58f, 22f, 42f, 22f);
        // Body.
        Polygon(pen, size, ox, oy, Ink, 30f, 22f, 70f, 22f, 70f, 88f, 30f, 88f);
        // Charge window.
        Polygon(pen, size, ox, oy, Accent, 38f, 34f, 62f, 34f, 62f, 44f, 38f, 44f);
        Polygon(pen, size, ox, oy, InkSoft, 38f, 52f, 62f, 52f, 62f, 60f, 38f, 60f);
    }

    /// <summary>无人机: a quad-rotor frame.</summary>
    private static void PaintDrone(Painter2D pen, float size, float ox, float oy)
    {
        // Arms.
        Stroke(pen, size, ox, oy, Ink, 3.2f, 26f, 30f, 74f, 74f, 74f, 30f, 26f, 74f);
        // Rotors.
        Stroke(pen, size, ox, oy, Ink, 3.6f, 12f, 30f, 40f, 30f);
        Stroke(pen, size, ox, oy, Ink, 3.6f, 60f, 30f, 88f, 30f);
        Stroke(pen, size, ox, oy, Ink, 3.6f, 12f, 74f, 40f, 74f);
        Stroke(pen, size, ox, oy, Ink, 3.6f, 60f, 74f, 88f, 74f);
        // Body.
        Polygon(pen, size, ox, oy, Ink, 38f, 40f, 62f, 40f, 62f, 64f, 38f, 64f);
        Polygon(pen, size, ox, oy, Accent, 44f, 46f, 56f, 46f, 56f, 58f, 44f, 58f);
    }

    /// <summary>载荷: a supply crate.</summary>
    private static void PaintCargo(Painter2D pen, float size, float ox, float oy)
    {
        // Lid.
        Polygon(pen, size, ox, oy, InkSoft, 18f, 26f, 82f, 26f, 82f, 40f, 18f, 40f);
        // Body.
        Polygon(pen, size, ox, oy, Ink, 22f, 40f, 78f, 40f, 78f, 84f, 22f, 84f);
        // Latch.
        Polygon(pen, size, ox, oy, Accent, 46f, 40f, 54f, 40f, 54f, 56f, 46f, 56f);
    }
}
