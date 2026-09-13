using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The light cyan dot matrix that runs along the top of the Processing work
/// area (Phase 09B PASS 5) - the one decorative element the approved reference
/// added, drawn in code so no UXML is bloated with hundreds of dots.
///
/// It is decoration with a small piece of meaning: the dots brighten while the
/// refinery runs and settle back when it stops, so the strip reads as the
/// unit's activity line rather than as noise.
/// </summary>
public sealed class ProcessingDotStrip : VisualElement
{
    private const float DotStep = 9f;
    private const float DotSize = 2.4f;

    private static readonly Color IdleInk = new Color(0.62f, 0.72f, 0.75f, 1f);
    private static readonly Color ActiveInk = new Color(0.22f, 0.66f, 0.71f, 1f);

    private bool _active;

    public ProcessingDotStrip()
    {
        pickingMode = PickingMode.Ignore;
        AddToClassList("process-dot-strip");
        generateVisualContent += Paint;
    }

    /// <summary>True while the strip carries the active (brighter) ink.</summary>
    public bool Active => _active;

    public void SetActive(bool active)
    {
        if (_active == active)
        {
            return;
        }

        _active = active;
        MarkDirtyRepaint();
    }

    private void Paint(MeshGenerationContext context)
    {
        Rect bounds = contentRect;
        if (bounds.width < DotStep || bounds.height < DotSize)
        {
            return;
        }

        Painter2D pen = context.painter2D;
        pen.fillColor = _active ? ActiveInk : IdleInk;
        float y = bounds.yMin + DotSize;

        // The matrix fades out towards the right, so the strip stays a corner
        // decoration instead of a rule competing with the panel header.
        int count = Mathf.FloorToInt((bounds.width - DotSize) / DotStep) + 1;
        for (int i = 0; i < count; i++)
        {
            float fade = Mathf.Lerp(1f, 0.25f, i / (float)Mathf.Max(1, count - 1));
            Color ink = pen.fillColor;
            ink.a = fade;
            pen.fillColor = ink;

            float x = bounds.xMin + i * DotStep;
            pen.BeginPath();
            pen.MoveTo(new Vector2(x, y));
            pen.LineTo(new Vector2(x + DotSize, y));
            pen.LineTo(new Vector2(x + DotSize, y + DotSize));
            pen.LineTo(new Vector2(x, y + DotSize));
            pen.ClosePath();
            pen.Fill();
        }
    }
}
