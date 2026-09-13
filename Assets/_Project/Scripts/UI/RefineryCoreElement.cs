using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The refinery core - the Processing page's first visual anchor (Phase 09B
/// PASS 5).
///
/// It is one procedurally drawn circular industrial unit, deliberately built
/// from primitives (housing ring, bolt marks, a three-spoke rotor, an orange
/// heat core and a cyan progress arc) so it stays consistent with the page's
/// sketch icons and can be rebuilt in Unity primitives:
///
///   Idle       housing static, rotor still, core low and cool, no arc
///   Running    rotor turns, core lights up orange, the progress arc sweeps,
///              the halo breathes with the run
///   Complete   rotor decelerates and stops, the core keeps its heat for a
///              short acknowledgement flash, the arc stays full
///
/// Restraint is part of the contract: no particles, no glow sprites, no
/// gradients - the whole animation is rotation, two colours and a short decay.
/// </summary>
public sealed class RefineryCoreElement : VisualElement
{
    public enum CoreState
    {
        Idle,
        Running,
        Complete
    }

    /// <summary>Stable element name for probes and tests.</summary>
    public const string ElementName = "RefineryCore";

    /// <summary>Rotor speed while refining (degrees / second).</summary>
    public const float RunningDegreesPerSecond = 96f;

    /// <summary>Rotor speed during the completion acknowledgement.</summary>
    public const float DecayDegreesPerSecond = 42f;

    /// <summary>Length of the completion flash (seconds).</summary>
    public const float FlashSeconds = 0.55f;

    private static readonly Color HousingInk = new Color(0.72f, 0.76f, 0.79f, 1f);
    private static readonly Color HousingInner = new Color(0.82f, 0.85f, 0.87f, 1f);
    private static readonly Color BoltInk = new Color(0.66f, 0.71f, 0.75f, 1f);
    private static readonly Color RotorInk = new Color(0.62f, 0.67f, 0.71f, 1f);
    private static readonly Color ToothInk = new Color(0.55f, 0.61f, 0.66f, 1f);
    private static readonly Color ColdCore = new Color(0.55f, 0.62f, 0.67f, 0.55f);
    private static readonly Color HeatCore = new Color(0.97f, 0.55f, 0.13f, 1f);
    private static readonly Color HeatHalo = new Color(0.97f, 0.60f, 0.20f, 0.16f);
    private static readonly Color ArcInk = new Color(0.05f, 0.50f, 0.55f, 1f);

    private CoreState _state = CoreState.Idle;
    private float _angle;
    private float _progress;
    private float _flash;
    private float _halo;

    public RefineryCoreElement()
    {
        pickingMode = PickingMode.Ignore;
        name = ElementName;
        AddToClassList("refinery-core-graphic");
        generateVisualContent += Paint;
    }

    /// <summary>Current visual state (diagnostics/tests).</summary>
    public CoreState State => _state;

    /// <summary>Rotor angle in degrees (diagnostics/tests).</summary>
    public float RotorAngle => _angle;

    /// <summary>0..1 completion flash left over from the last run.</summary>
    public float Flash01 => Mathf.Clamp01(_flash / FlashSeconds);

    /// <summary>Applies the authoritative session state. Called from Refresh()
    /// only, so a redraw happens on state change - never per frame by itself.</summary>
    public void SetState(CoreState state, float progress01)
    {
        float next = Mathf.Clamp01(progress01);
        if (_state != state)
        {
            if (state == CoreState.Complete)
            {
                // Only a completed RUN lights the acknowledgement flash; entering
                // the page with a settled result must not replay it.
                _flash = FlashSeconds;
            }
            else if (state == CoreState.Running)
            {
                _flash = 0f;
            }
        }

        if (_state == state && Mathf.Abs(_progress - next) < 0.0005f)
        {
            return;
        }

        _state = state;
        _progress = next;
        Dirty();
    }

    /// <summary>
    /// Per-frame animation. Only the rotation, the halo breath and the flash
    /// decay are animated; the authoritative progress still comes from
    /// GameSession through <see cref="SetState"/>.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        switch (_state)
        {
            case CoreState.Running:
                _angle = Mathf.Repeat(_angle + RunningDegreesPerSecond * deltaTime, 360f);
                _halo = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.1f));
                Dirty();
                break;

            case CoreState.Complete:
                if (_flash > 0f)
                {
                    _flash = Mathf.Max(0f, _flash - deltaTime);
                    // The rotor coasts to a stop instead of cutting.
                    float speed = DecayDegreesPerSecond * Mathf.Clamp01(_flash / FlashSeconds);
                    _angle = Mathf.Repeat(_angle + speed * deltaTime, 360f);
                    Dirty();
                }

                break;

            default:
                _halo = 0f;
                break;
        }
    }

    private void Dirty()
    {
        MarkDirtyRepaint();
    }

    private void Paint(MeshGenerationContext context)
    {
        Rect bounds = contentRect;
        if (bounds.width < 12f || bounds.height < 12f)
        {
            return;
        }

        Painter2D pen = context.painter2D;
        float unit = Mathf.Min(bounds.width, bounds.height) * 0.5f;
        float outer = unit - 1.5f;
        float inner = outer * 0.80f;
        float rotor = outer * 0.60f;
        float arc = outer * 0.72f;
        Vector2 centre = bounds.center;

        // ---- housing: two hairline rings + four bolt marks ----
        pen.strokeColor = HousingInk;
        pen.lineWidth = 1.4f;
        Circle(pen, centre, outer);
        pen.strokeColor = HousingInner;
        pen.lineWidth = 1f;
        Circle(pen, centre, inner);

        pen.fillColor = BoltInk;
        for (int i = 0; i < 4; i++)
        {
            float a = (45f + i * 90f) * Mathf.Deg2Rad;
            Square(pen, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (outer * 0.90f), 2.2f);
        }

        // ---- rotor: a ring with three teeth, the only turning part ----
        pen.strokeColor = RotorInk;
        pen.lineWidth = 2.6f;
        Circle(pen, centre, rotor);
        pen.strokeColor = ToothInk;
        pen.lineWidth = 4.2f;
        for (int i = 0; i < 3; i++)
        {
            float a = (_angle + i * 120f) * Mathf.Deg2Rad;
            Vector2 from = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (rotor * 0.42f);
            Vector2 to = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (rotor * 0.92f);
            pen.BeginPath();
            pen.MoveTo(from);
            pen.LineTo(to);
            pen.Stroke();
        }

        // ---- heat core: cool slate at rest, orange while it works ----
        float settled = _state == CoreState.Complete ? 1f : _progress;
        float heat = _state == CoreState.Idle ? 0f : Mathf.Clamp01(0.55f + 0.45f * settled);
        float flash = Flash01;

        if (_state != CoreState.Idle && (heat + flash) > 0.01f)
        {
            Color halo = HeatHalo;
            halo.a *= Mathf.Clamp01(0.45f + 0.55f * _halo + flash * 0.4f);
            pen.fillColor = halo;
            CircleFilled(pen, centre, rotor * (0.74f + flash * 0.10f));
        }

        pen.fillColor = _state == CoreState.Idle
            ? ColdCore
            : Color.Lerp(new Color(0.72f, 0.66f, 0.60f, 0.75f), HeatCore,
                Mathf.Clamp01(0.35f + 0.65f * heat + flash * 0.5f));
        CircleFilled(pen, centre, rotor * (0.34f + flash * 0.06f));

        // ---- progress arc: cyan, drawn from the top clockwise ----
        if (_state != CoreState.Idle && _progress > 0.001f)
        {
            pen.strokeColor = ArcInk;
            pen.lineWidth = 2.2f;
            Arc(pen, centre, arc, -90f, 360f * Mathf.Clamp01(_progress));
        }
    }

    private static void Circle(Painter2D pen, Vector2 centre, float radius)
    {
        Arc(pen, centre, radius, 0f, 360f);
    }

    private static void CircleFilled(Painter2D pen, Vector2 centre, float radius)
    {
        pen.BeginPath();
        for (int i = 0; i <= 32; i++)
        {
            float a = i * Mathf.PI * 2f / 32f;
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

    /// <summary>Strokes an arc: <paramref name="startDegrees"/> 0 = +x axis,
    /// positive sweeps clockwise in panel space (y grows downwards).</summary>
    private static void Arc(Painter2D pen, Vector2 centre, float radius,
        float startDegrees, float sweepDegrees)
    {
        int steps = Mathf.Max(6, Mathf.CeilToInt(Mathf.Abs(sweepDegrees) / 12f));
        pen.BeginPath();
        for (int i = 0; i <= steps; i++)
        {
            float a = (startDegrees + sweepDegrees * i / steps) * Mathf.Deg2Rad;
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

        pen.Stroke();
    }

    private static void Square(Painter2D pen, Vector2 centre, float half)
    {
        pen.BeginPath();
        pen.MoveTo(new Vector2(centre.x - half, centre.y - half));
        pen.LineTo(new Vector2(centre.x + half, centre.y - half));
        pen.LineTo(new Vector2(centre.x + half, centre.y + half));
        pen.LineTo(new Vector2(centre.x - half, centre.y + half));
        pen.ClosePath();
        pen.Fill();
    }
}
