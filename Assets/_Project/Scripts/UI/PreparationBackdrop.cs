using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Preparation-only backdrop: one very weak engineering grid, painted with
/// Painter2D so the page needs no texture asset.
///
/// It exists to give the light canvas a material reading ("a panel behind the
/// glass") instead of a flat fill. It is deliberately inert:
///
///   * alpha 0.05 - visible only when the player looks for it,
///   * painted once per geometry change, never per frame,
///   * pickingMode Ignore, so it can never eat a click meant for a slot,
///   * absolutely positioned, so it never takes part in layout.
/// </summary>
public sealed class PreparationBackdrop : VisualElement
{
    /// <summary>Stable element name (idempotency + probes).</summary>
    public const string ElementName = "PrepBackdrop";

    /// <summary>Grid pitch in panel pixels.</summary>
    private const float Pitch = 96f;

    private static readonly Color LineColor = new Color(0.09f, 0.11f, 0.13f, 0.05f);

    public PreparationBackdrop()
    {
        name = ElementName;
        pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute;
        style.left = 0f;
        style.top = 0f;
        style.right = 0f;
        style.bottom = 0f;
        generateVisualContent += Paint;
    }

    private void Paint(MeshGenerationContext context)
    {
        Rect bounds = contentRect;
        if (bounds.width < Pitch || bounds.height < Pitch)
        {
            return;
        }

        Painter2D pen = context.painter2D;
        pen.strokeColor = LineColor;
        pen.lineWidth = 1f;

        pen.BeginPath();
        for (float x = Pitch; x < bounds.width; x += Pitch)
        {
            pen.MoveTo(new Vector2(bounds.x + x, bounds.y));
            pen.LineTo(new Vector2(bounds.x + x, bounds.yMax));
        }

        for (float y = Pitch; y < bounds.height; y += Pitch)
        {
            pen.MoveTo(new Vector2(bounds.x, bounds.y + y));
            pen.LineTo(new Vector2(bounds.xMax, bounds.y + y));
        }

        pen.Stroke();
    }
}
