using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Round 9 screen-space heading tape for the surface compass HUD.
///
/// The element fills the CompassTicksHost clip of the fixed CompassFrame and
/// scrolls with the camera yaw while the frame itself never moves:
/// - ticks are drawn as triangle-mesh quads (minor every 5 deg, major every
///   15 deg, cardinal ticks emphasized),
/// - cardinal letters (N/E/S/W) and degree numerals (000..315) are small UI
///   Toolkit labels repositioned with the tape,
/// - pure bearing math (Normalize360 / TargetBearing / RelativeBearing) is
///   exposed statically so the controller and PlayMode tests share one source.
///
/// Coordinate convention: world +Z is north (0 deg), yaw grows clockwise when
/// viewed from above, and target right of forward is a POSITIVE relative
/// bearing -> waypoint x = tapeCentreX + relativeBearing * pixelsPerDegree.
/// 359/0 wrap uses Mathf.DeltaAngle so the tape never jumps.
/// </summary>
public sealed class CompassTapeElement : VisualElement
{
    /// <summary>Visible tape half window (+-45 deg fills the inner width).</summary>
    public const float VisibleHalfDegrees = 45f;

    private const float TapeSpanDegrees = 90f; // +-45 visible window
    private const float DrawMarginDegrees = 6f;

    private static readonly Color32 TickMinorColor =
        new Color32(150, 168, 180, 120);

    private static readonly Color32 TickMajorColor =
        new Color32(190, 202, 212, 170);

    private static readonly Color32 TickCardinalColor =
        new Color32(214, 226, 234, 210);

    private readonly Label[] _letters; // N/E/S/W
    private readonly Label[] _degrees; // 000..315
    private float _cameraYawDeg;

    public CompassTapeElement()
    {
        pickingMode = PickingMode.Ignore;

        style.position = Position.Absolute;
        style.left = 0f;
        style.top = 0f;
        style.right = 0f;
        style.bottom = 0f;

        string[] letterText = { "N", "E", "S", "W" };
        int[] letterBearing = { 0, 90, 180, 270 };
        _letters = new Label[letterText.Length];
        for (int i = 0; i < letterText.Length; i++)
        {
            Label label = new Label(letterText[i]);
            label.AddToClassList("compass-mark-letter");
            label.name = "CompassLetter" + letterText[i];
            label.userData = letterBearing[i];
            Add(label);
            _letters[i] = label;
        }

        string[] degreeText =
        {
            "000", "045", "090", "135", "180", "225", "270", "315"
        };

        _degrees = new Label[degreeText.Length];
        for (int i = 0; i < degreeText.Length; i++)
        {
            Label label = new Label(degreeText[i]);
            label.AddToClassList("compass-mark-degree");
            label.name = "CompassDegree" + degreeText[i];
            label.userData = i * 45;
            Add(label);
            _degrees[i] = label;
        }

        generateVisualContent += GenerateVisual;
        RegisterCallback<GeometryChangedEvent>(_ => OnGeometryChanged());
    }

    /// <summary>Absolute compass heading the tape is centred on (degrees).</summary>
    public float CameraYawDegrees => _cameraYawDeg;

    /// <summary>Layout width of the visible tape window (0 before layout).</summary>
    public float TapeWidthPx => float.IsNaN(resolvedStyle.width)
        ? 0f
        : resolvedStyle.width;

    /// <summary>Horizontal pixels per degree inside the visible +-45 window.</summary>
    public float PixelsPerDegree
    {
        get
        {
            float width = TapeWidthPx;
            return width > 1f ? width / TapeSpanDegrees : 0f;
        }
    }

    /// <summary>The horizontal tape centre (x of the fixed reference).</summary>
    public float TapeCentreX => TapeWidthPx * 0.5f;

    /// <summary>Centres the tape on a new camera yaw; wraps cleanly through
    /// 359/0 and never jumps the tape.</summary>
    public void SetCameraYaw(float yawDegrees)
    {
        float yaw = Normalize360(yawDegrees);
        if (Mathf.Abs(yaw - _cameraYawDeg) < 0.001f)
        {
            return;
        }

        _cameraYawDeg = yaw;
        MarkDirtyRepaint();
        RepositionMarks();
    }

    /// <summary>True when the given absolute bearing is inside the visible
    /// +-45 tape window (used by edge-cue logic in the HUD controller).</summary>
    public bool IsVisibleOnTape(float bearingDegrees)
    {
        return Mathf.Abs(RelativeBearing(_cameraYawDeg, bearingDegrees))
            <= VisibleHalfDegrees;
    }

    private void OnGeometryChanged()
    {
        MarkDirtyRepaint();
        RepositionMarks();
    }

    private void RepositionMarks()
    {
        float ppd = PixelsPerDegree;
        if (ppd <= 0f)
        {
            return;
        }

        float centreX = TapeCentreX;
        foreach (Label letter in _letters)
        {
            PositionMark(letter, centreX, ppd);
        }

        foreach (Label degree in _degrees)
        {
            PositionMark(degree, centreX, ppd);
        }
    }

    private void PositionMark(Label mark, float centreX, float ppd)
    {
        int bearing = mark.userData is int b ? b : 0;
        float rel = RelativeBearing(_cameraYawDeg, bearing);
        if (Mathf.Abs(rel) > VisibleHalfDegrees + DrawMarginDegrees)
        {
            mark.style.display = DisplayStyle.None;
            return;
        }

        float x = centreX + rel * ppd;
        mark.style.left = x;
        mark.style.display = DisplayStyle.Flex;
    }

    private void GenerateVisual(MeshGenerationContext ctx)
    {
        float width = TapeWidthPx;
        if (width <= 1f)
        {
            return;
        }

        float ppd = PixelsPerDegree;
        float centreX = width * 0.5f;
        float rowCentreY = resolvedStyle.height * 0.5f;
        if (float.IsNaN(rowCentreY) || rowCentreY <= 0f)
        {
            rowCentreY = 26f;
        }

        // Fixed centre reference hairline (belongs to the frame, not the yaw).
        DrawLine(ctx,
            new Vector2(centreX, rowCentreY - 9f),
            new Vector2(centreX, rowCentreY + 9f),
            TickCardinalColor, 1.5f);

        for (int bearing = 0; bearing < 360; bearing += 5)
        {
            float rel = RelativeBearing(_cameraYawDeg, bearing);
            if (Mathf.Abs(rel) > VisibleHalfDegrees + DrawMarginDegrees)
            {
                continue;
            }

            float x = centreX + rel * ppd;
            bool cardinal = bearing % 90 == 0;
            bool major = bearing % 15 == 0;
            Color32 color = cardinal
                ? TickCardinalColor
                : (major ? TickMajorColor : TickMinorColor);
            float halfLength = cardinal ? 7.5f : (major ? 6.5f : 4.5f);
            float thickness = cardinal ? 2f : (major ? 1.5f : 1f);

            DrawLine(ctx,
                new Vector2(x, rowCentreY - halfLength),
                new Vector2(x, rowCentreY + halfLength),
                color, thickness);
        }
    }

    // ====================================================================
    // Pure bearing math (single shared source for the compass HUD + tests)
    // ====================================================================

    public static float Normalize360(float a)
    {
        a %= 360f;
        if (a < 0f)
        {
            a += 360f;
        }

        return a;
    }

    /// <summary>Absolute bearing from -> to in the horizontal plane, 0 = +Z
    /// (north), growing clockwise from above.</summary>
    public static float TargetBearing(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        delta.y = 0f;

        return Normalize360(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg);
    }

    /// <summary>Signed relative bearing in [-180, 180]; 0 = aligned,
    /// positive = target right, negative = target left. 359/0 wraps cleanly.</summary>
    public static float RelativeBearing(float cameraYaw, float targetBearing)
    {
        return Mathf.DeltaAngle(cameraYaw, targetBearing);
    }

    // ====================================================================
    // Small line-quad mesh helper (mirrors the wrist radial mesh style)
    // ====================================================================

    private static void DrawLine(
        MeshGenerationContext ctx,
        Vector2 start,
        Vector2 end,
        Color32 color,
        float thickness)
    {
        Vector2 delta = end - start;
        if (delta.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Vector2 normal = new Vector2(-delta.y, delta.x).normalized;
        Vector2 offset = normal * (thickness * 0.5f);

        MeshWriteData mesh = ctx.Allocate(4, 6);
        mesh.SetNextVertex(MakeVertex(start - offset, color));
        mesh.SetNextVertex(MakeVertex(start + offset, color));
        mesh.SetNextVertex(MakeVertex(end + offset, color));
        mesh.SetNextVertex(MakeVertex(end - offset, color));

        mesh.SetNextIndex(0);
        mesh.SetNextIndex(1);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(3);
    }

    private static Vertex MakeVertex(Vector2 position, Color32 tint)
    {
        Vertex vertex = new Vertex();
        vertex.position = new Vector3(position.x, position.y, Vertex.nearZ);
        vertex.tint = tint;
        vertex.uv = Vector2.zero;
        return vertex;
    }
}
