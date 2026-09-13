using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// One process-flow connector of the Processing page (Phase 09B PASS 5).
///
/// It draws a hairline rail plus short tick marks. While the refinery runs the
/// ticks travel along the rail, which is the page's "energy / data is moving"
/// signal: no particles, no shader, no per-frame layout - the phase lives in the
/// painter and only advances while a run is in progress.
/// </summary>
public sealed class ProcessingFlowLine : VisualElement
{
    /// <summary>Tick travel speed while refining (px / second).</summary>
    public const float ActiveSpeed = 34f;

    /// <summary>Distance between two ticks (px).</summary>
    public const float Spacing = 13f;

    private const float TickLength = 4f;

    private static readonly Color RailInk = new Color(0.80f, 0.83f, 0.86f, 1f);
    private static readonly Color IdleTickInk = new Color(0.78f, 0.81f, 0.84f, 1f);
    private static readonly Color ActiveTickInk = new Color(0.05f, 0.52f, 0.58f, 1f);

    private bool _active;
    private float _phase;

    /// <summary>Vertical rail (true) or horizontal rail (false).</summary>
    public bool Vertical { get; set; }

    public ProcessingFlowLine(bool vertical = false)
    {
        Vertical = vertical;
        pickingMode = PickingMode.Ignore;
        AddToClassList("process-flow-line");
        generateVisualContent += Paint;
    }

    /// <summary>True while the ticks travel (the refinery is running).</summary>
    public bool Active => _active;

    /// <summary>Current tick offset along the rail (diagnostics/tests).</summary>
    public float Phase => _phase;

    /// <summary>Switches the rail between the still (idle/complete) state and
    /// the travelling (running) state.</summary>
    public void SetActive(bool active)
    {
        if (_active == active)
        {
            return;
        }

        _active = active;
        if (!_active)
        {
            _phase = 0f;
        }

        MarkDirtyRepaint();
    }

    /// <summary>Advances the travelling ticks; a no-op while idle, so an idle
    /// page costs nothing.</summary>
    public void Tick(float deltaTime)
    {
        if (!_active || deltaTime <= 0f)
        {
            return;
        }

        _phase = Mathf.Repeat(_phase + ActiveSpeed * deltaTime, Spacing);
        MarkDirtyRepaint();
    }

    private void Paint(MeshGenerationContext context)
    {
        Rect bounds = contentRect;
        float length = Vertical ? bounds.height : bounds.width;
        if (length < 6f)
        {
            return;
        }

        Painter2D pen = context.painter2D;

        // ---- rail ----
        pen.strokeColor = RailInk;
        pen.lineWidth = 1f;
        pen.BeginPath();
        if (Vertical)
        {
            float x = bounds.center.x;
            pen.MoveTo(new Vector2(x, bounds.yMin));
            pen.LineTo(new Vector2(x, bounds.yMax));
        }
        else
        {
            float y = bounds.center.y;
            pen.MoveTo(new Vector2(bounds.xMin, y));
            pen.LineTo(new Vector2(bounds.xMax, y));
        }

        pen.Stroke();

        // ---- travelling ticks ----
        pen.strokeColor = _active ? ActiveTickInk : IdleTickInk;
        pen.lineWidth = _active ? 1.6f : 1f;
        for (float position = _phase; position <= length; position += Spacing)
        {
            if (position < 1f)
            {
                continue;
            }

            pen.BeginPath();
            if (Vertical)
            {
                float x = bounds.center.x;
                pen.MoveTo(new Vector2(x, bounds.yMin + position));
                pen.LineTo(new Vector2(x, bounds.yMin + position + TickLength));
            }
            else
            {
                float y = bounds.center.y;
                pen.MoveTo(new Vector2(bounds.xMin + position, y));
                pen.LineTo(new Vector2(bounds.xMin + position + TickLength, y));
            }

            pen.Stroke();
        }
    }
}
