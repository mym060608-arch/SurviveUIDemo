using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The two cyan dot-matrix fields that flank the landing confirmation panel
/// (Phase 09B PASS 8).
///
/// The approved reference puts a regular cyan dot grid on the dim hangar
/// background on BOTH sides of the light panel - it is the page's only
/// decorative element and it must stay restrained. Like every other mark in
/// this project it is drawn in code (Painter2D, no textures, no imported art):
/// one flat cyan dot per grid cell, with a soft vertical falloff so the field
/// fades toward the top and bottom instead of ending in a hard edge.
/// </summary>
public sealed class LandingDotMatrix : VisualElement
{
    /// <summary>Class the host styles (size / position) from USS.</summary>
    public const string ElementClass = "landing-dots";

    /// <summary>Grid pitch in layout pixels.</summary>
    public const float DotSpacing = 21f;

    /// <summary>Dot radius in layout pixels.</summary>
    public const float DotRadius = 1.6f;

    private static readonly Color DotColor = new Color(0.30f, 0.78f, 0.85f, 1f);
    private const float MinAlpha = 0.10f;
    private const float MaxAlpha = 0.42f;

    /// <summary>Number of dots the last paint produced (diagnostics / tests).</summary>
    public int PaintedDotCount { get; private set; }

    public LandingDotMatrix()
    {
        pickingMode = PickingMode.Ignore;
        AddToClassList(ElementClass);
        generateVisualContent += Paint;
    }

    private void Paint(MeshGenerationContext context)
    {
        Rect bounds = contentRect;
        PaintedDotCount = 0;
        if (bounds.width < DotSpacing || bounds.height < DotSpacing)
        {
            return;
        }

        Painter2D pen = context.painter2D;

        // One flat dot per cell; the alpha falls off toward the top and bottom
        // so the matrix reads as a field, not as a pasted rectangle.
        int columns = Mathf.FloorToInt(bounds.width / DotSpacing);
        int rows = Mathf.FloorToInt(bounds.height / DotSpacing);
        float x0 = (bounds.width - (columns - 1) * DotSpacing) * 0.5f;
        float y0 = (bounds.height - (rows - 1) * DotSpacing) * 0.5f;

        for (int row = 0; row < rows; row++)
        {
            float y = y0 + row * DotSpacing;
            float vertical = rows > 1 ? (float)row / (rows - 1) : 0.5f;
            // 0 at both ends, 1 in the middle.
            float falloff = Mathf.Sin(vertical * Mathf.PI);
            float alpha = Mathf.Lerp(MinAlpha, MaxAlpha, falloff * falloff);

            for (int column = 0; column < columns; column++)
            {
                float x = x0 + column * DotSpacing;
                pen.fillColor = new Color(DotColor.r, DotColor.g, DotColor.b, alpha);
                pen.BeginPath();
                pen.Arc(new Vector2(x, y), DotRadius, 0f, 360f);
                pen.Fill();
                PaintedDotCount++;
            }
        }
    }
}
