using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The cursor the Outpost page draws INSIDE the game UI (Phase 09B PASS 6).
///
/// Why it exists: the OS cursor is not reliably ours. UI Toolkit re-applies the
/// hovered element's own cursor on every hover change, Windows refuses the small
/// hardware-cursor texture, and the result was the stock arrow appearing for a
/// frame and then staying on screen. The page therefore stops fighting for the
/// OS cursor: it HIDES it and paints the pointer itself, so what the player sees
/// is always the project's dot / dot+ring, on every frame, on every element.
///
/// The drawing matches <see cref="WristCursorService"/> pixel for pixel - a 6 px
/// light dot with a faint outline, plus a 1 px teal ring when the pointer is
/// over something the player can act on - because it IS the same cursor visual,
/// only composited by the game instead of by the desktop.
///
/// It is inert: absolute, pickingMode Ignore, never part of layout, painted only
/// when its position or hover state changes.
/// </summary>
public sealed class UiCursorOverlay : VisualElement
{
    /// <summary>Stable element name (probes / tests).</summary>
    public const string ElementName = "UiCursorOverlay";

    /// <summary>Canvas size of the drawing (the dot sits in its centre).</summary>
    public const float CanvasSize = 32f;

    private const float DotRadius = 3.0f;
    private const float RingInner = 6.0f;
    private const float RingOuter = 7.2f;

    private static readonly Color DotInk = new Color32(230, 235, 238, 255);
    private static readonly Color DotOutline = new Color(0f, 0f, 0f, 0.45f);
    private static readonly Color RingInk = new Color32(14, 127, 140, 255);

    private bool _hovering;

    /// <summary>
    /// A fully transparent 32x32 cursor. The OS cursor is not hidden (that is
    /// unreliable in a windowed Windows player, and UI Toolkit re-applies a
    /// hardware cursor on every hover change anyway) - it is made INVISIBLE
    /// instead, both through Cursor.SetCursor and through the page elements'
    /// own cursor style, so whatever the runtime applies draws nothing and the
    /// only pointer the player sees is the one painted here.
    /// </summary>
    private static Texture2D _invisibleTexture;

    public static Texture2D InvisibleTexture
    {
        get
        {
            if (_invisibleTexture == null)
            {
                _invisibleTexture = new Texture2D(CanvasSize32, CanvasSize32,
                    TextureFormat.RGBA32, false)
                {
                    name = "UiCursorOverlay.Invisible",
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                    filterMode = FilterMode.Point
                };

                var clear = new Color32(0, 0, 0, 0);
                var pixels = new Color32[CanvasSize32 * CanvasSize32];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = clear;
                }

                _invisibleTexture.SetPixels32(pixels);
                _invisibleTexture.Apply();
            }

            return _invisibleTexture;
        }
    }

    private const int CanvasSize32 = 32;

    /// <summary>The invisible cursor as a UI Toolkit style value.</summary>
    public static UnityEngine.UIElements.Cursor InvisibleUiCursor =>
        new UnityEngine.UIElements.Cursor
        {
            texture = InvisibleTexture,
            hotspot = new Vector2(CanvasSize32 * 0.5f, CanvasSize32 * 0.5f)
        };

    public UiCursorOverlay()
    {
        name = ElementName;
        pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute;
        style.width = CanvasSize;
        style.height = CanvasSize;
        style.display = DisplayStyle.None;
        generateVisualContent += Paint;
    }

    /// <summary>True while the ring is drawn (pointer over an operable element).</summary>
    public bool IsHovering => _hovering;

    /// <summary>Turns the ring on/off (no repaint when unchanged).</summary>
    public void SetHover(bool hovering)
    {
        if (_hovering == hovering)
        {
            return;
        }

        _hovering = hovering;
        MarkDirtyRepaint();
    }

    /// <summary>
    /// Places the cursor with the CENTRE of the canvas on the given panel point
    /// (UI Toolkit panel space, top-left origin) and shows it.
    /// </summary>
    public void PlaceAt(Vector2 panelPoint)
    {
        style.left = panelPoint.x - CanvasSize * 0.5f;
        style.top = panelPoint.y - CanvasSize * 0.5f;
        if (resolvedStyle.display == DisplayStyle.None)
        {
            style.display = DisplayStyle.Flex;
        }
    }

    /// <summary>Hides the cursor (the page stopped owning the pointer).</summary>
    public void Hide()
    {
        style.display = DisplayStyle.None;
        SetHover(false);
    }

    private void Paint(MeshGenerationContext context)
    {
        Rect bounds = contentRect;
        if (bounds.width < 2f || bounds.height < 2f)
        {
            return;
        }

        Painter2D pen = context.painter2D;
        Vector2 centre = bounds.center;

        if (_hovering)
        {
            pen.strokeColor = RingInk;
            pen.lineWidth = 1.2f;
            Circle(pen, centre, (RingInner + RingOuter) * 0.5f);
        }

        // The dot carries a faint dark outline so it stays readable on the light
        // page and on a bright console render alike.
        pen.strokeColor = DotOutline;
        pen.lineWidth = 1f;
        CircleFilled(pen, centre, DotRadius + 0.6f);
        pen.fillColor = DotInk;
        CircleFilled(pen, centre, DotRadius);
    }

    private static void Circle(Painter2D pen, Vector2 centre, float radius)
    {
        pen.BeginPath();
        for (int i = 0; i <= 24; i++)
        {
            float a = i * Mathf.PI * 2f / 24f;
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

    private static void CircleFilled(Painter2D pen, Vector2 centre, float radius)
    {
        pen.BeginPath();
        for (int i = 0; i <= 24; i++)
        {
            float a = i * Mathf.PI * 2f / 24f;
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
}
