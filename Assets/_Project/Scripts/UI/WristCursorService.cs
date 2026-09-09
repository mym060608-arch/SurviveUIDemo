using UnityEngine;

/// <summary>
/// Wrist-mode cursor authority (Phase 06 Revision CodeReference, Round 9.1).
///
/// The custom wrist pointer stays a runtime texture assigned through
/// Cursor.SetCursor(...); there is intentionally NO UI Toolkit cursor overlay,
/// no cursor-trail VisualElement and no cursor-history list.
///
/// Round 9.1 visual fix (top-left "residual arc"): the previous normal cursor
/// was a hollow ring + tiny centre dot, which read as a stray teal/light arc
/// whenever the pointer sat on empty screen space. The normal cursor is now a
/// SOLID 6px light dot (no ring at all); the hover cursor keeps the same
/// small dot and adds a ~14px teal thin ring - (•).
/// </summary>
public sealed class WristCursorService
{
    public enum CursorKind
    {
        /// <summary>Solid light dot only (normal pointer over the wrist).</summary>
        SolidDot,

        /// <summary>Same solid dot plus a thin teal ring (hovering a node).</summary>
        DotWithRing
    }

    private const int DotCursorSize = 16;
    private const int DotRingCursorSize = 32;

    private const float DotRadiusPx = 3.0f;      // solid 6px dot
    private const float RingInnerPx = 6.0f;      // thin teal ring 12..14px
    private const float RingOuterPx = 7.2f;

    private Texture2D _normalCursor;
    private Texture2D _hoverCursor;
    private CursorKind _normalKind = CursorKind.SolidDot;
    private CursorKind _hoverKind = CursorKind.DotWithRing;

    private bool _hoverState;

    public Texture2D NormalCursorTexture => _normalCursor;
    public Texture2D HoverCursorTexture => _hoverCursor;
    public bool IsHovering => _hoverState;

    /// <summary>Normal pointer is a solid dot, never a ring/arc shape.</summary>
    public CursorKind NormalKind => _normalKind;

    /// <summary>Hover pointer is the dot plus the teal thin ring.</summary>
    public CursorKind HoverKind => _hoverKind;

    /// <summary>Diagnostic diameter of the solid dot in texels.</summary>
    public float SolidDotDiameterPx => DotRadiusPx * 2f;

    /// <summary>Diagnostic outer diameter of the hover ring in texels.</summary>
    public float HoverRingOuterDiameterPx => RingOuterPx * 2f;

    /// <summary>Only the two runtime textures exist (single cursor source).</summary>
    public int CachedTextureCount =>
        (_normalCursor != null ? 1 : 0) + (_hoverCursor != null ? 1 : 0);

    public void Initialize()
    {
        if (_normalCursor == null)
        {
            _normalCursor = CreateDotCursor(
                new Color32(230, 235, 238, 255), DotCursorSize, DotRadiusPx);
        }

        if (_hoverCursor == null)
        {
            _hoverCursor = CreateDotRingCursor(
                new Color32(230, 235, 238, 255),
                new Color32(14, 127, 140, 255),
                DotRingCursorSize,
                DotRadiusPx,
                RingInnerPx,
                RingOuterPx);
        }
    }

    /// <summary>Enters wrist mode: OS cursor released and visible with the
    /// normal solid-dot cursor assigned.</summary>
    public void EnterWristMode()
    {
        Initialize();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        _hoverState = false;

        Cursor.SetCursor(_normalCursor, DotCursorHotSpot, CursorMode.Auto);
    }

    /// <summary>Switches between the neutral solid dot and the dot+ring hover
    /// cursor while the pointer is over a clickable node (no-op when
    /// unchanged).</summary>
    public void SetHover(bool hoveringNode)
    {
        if (_hoverState == hoveringNode)
        {
            return;
        }

        _hoverState = hoveringNode;

        Cursor.SetCursor(
            hoveringNode ? _hoverCursor : _normalCursor,
            hoveringNode ? DotRingCursorHotSpot : DotCursorHotSpot,
            CursorMode.Auto);
    }

    /// <summary>Back to the first-person surface: default cursor restored
    /// internally, OS cursor hidden and locked to the mouse-look.</summary>
    public void ExitToSurface()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        _hoverState = false;
    }

    /// <summary>Hub / 2D UI mode: default cursor restored, visible and free.</summary>
    public void EnterHubUi()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        _hoverState = false;
    }

    /// <summary>Releases the cached runtime textures (scene teardown).</summary>
    public void Dispose()
    {
        if (_normalCursor != null)
        {
            Object.Destroy(_normalCursor);
            _normalCursor = null;
        }

        if (_hoverCursor != null)
        {
            Object.Destroy(_hoverCursor);
            _hoverCursor = null;
        }

        _hoverState = false;
    }

    private static Vector2 DotCursorHotSpot => new Vector2(DotCursorSize * 0.5f,
        DotCursorSize * 0.5f);

    private static Vector2 DotRingCursorHotSpot => new Vector2(
        DotRingCursorSize * 0.5f, DotRingCursorSize * 0.5f);

    /// <summary>5-6px SOLID light dot with a very faint 1px outline. Deliberately
    /// contains no ring, no crescent and no arc so it never reads as a stray
    /// curved mark over empty wrist space.</summary>
    private static Texture2D CreateDotCursor(Color32 color, int size, float dotRadius)
    {
        Texture2D texture = NewCursorTexture(size, "Runtime_WristCursor_Dot");

        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 outline = new Color32(color.r, color.g, color.b, 90);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);

                if (distance <= dotRadius)
                {
                    texture.SetPixel(x, y, color);
                }
                else if (distance <= dotRadius + 1.0f)
                {
                    texture.SetPixel(x, y, outline);
                }
                else
                {
                    texture.SetPixel(x, y, clear);
                }
            }
        }

        texture.Apply(false, false);
        return texture;
    }

    /// <summary>(•) = the same solid dot plus a 12..14px teal THIN ring.</summary>
    private static Texture2D CreateDotRingCursor(
        Color32 dotColor,
        Color32 ringColor,
        int size,
        float dotRadius,
        float ringInner,
        float ringOuter)
    {
        Texture2D texture = NewCursorTexture(size, "Runtime_WristCursor_DotRing");

        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 outline = new Color32(dotColor.r, dotColor.g, dotColor.b, 90);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);

                if (distance <= dotRadius)
                {
                    texture.SetPixel(x, y, dotColor);
                }
                else if (distance <= dotRadius + 1.0f)
                {
                    texture.SetPixel(x, y, outline);
                }
                else if (distance >= ringInner && distance <= ringOuter)
                {
                    texture.SetPixel(x, y, ringColor);
                }
                else
                {
                    texture.SetPixel(x, y, clear);
                }
            }
        }

        texture.Apply(false, false);
        return texture;
    }

    private static Texture2D NewCursorTexture(int size, string name)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = name;
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }
}
