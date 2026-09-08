using UnityEngine;

/// <summary>
/// Wrist-mode cursor authority (Phase 06 Revision CodeReference).
///
/// The custom wrist pointer is a runtime ring cursor assigned through
/// Cursor.SetCursor(...). There is intentionally NO UI Toolkit cursor overlay,
/// no cursor-trail VisualElement, no mesh trail and no cursor-history list.
/// Two cached runtime-generated ring textures (normal + hover) switch by
/// hover state while the wrist terminal is open.
/// </summary>
public sealed class WristCursorService
{
    private Texture2D _normalCursor;
    private Texture2D _hoverCursor;

    private bool _hoverState;

    public Texture2D NormalCursorTexture => _normalCursor;
    public Texture2D HoverCursorTexture => _hoverCursor;
    public bool IsHovering => _hoverState;

    public void Initialize()
    {
        if (_normalCursor == null)
        {
            _normalCursor =
                CreateRingCursor(new Color32(230, 235, 238, 255));
        }

        if (_hoverCursor == null)
        {
            _hoverCursor =
                CreateRingCursor(new Color32(14, 127, 140, 255));
        }
    }

    /// <summary>Enters wrist mode: OS cursor released and visible with the
    /// normal ring cursor assigned.</summary>
    public void EnterWristMode()
    {
        Initialize();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        _hoverState = false;

        Cursor.SetCursor(_normalCursor, new Vector2(16f, 16f), CursorMode.Auto);
    }

    /// <summary>Switches between the neutral and the teal hover ring while the
    /// pointer is over a clickable node (no-op when unchanged).</summary>
    public void SetHover(bool hoveringNode)
    {
        if (_hoverState == hoveringNode)
        {
            return;
        }

        _hoverState = hoveringNode;

        Cursor.SetCursor(
            hoveringNode ? _hoverCursor : _normalCursor,
            new Vector2(16f, 16f),
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

    private static Texture2D CreateRingCursor(Color32 color)
    {
        const int size = 32;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

        texture.name = "Runtime_WristCursor";
        texture.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2(15.5f, 15.5f);

        Color32 clear = new Color32(0, 0, 0, 0);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);

                bool ring = distance >= 5.0f && distance <= 6.8f;
                bool centreDot = distance <= 1.4f;

                texture.SetPixel(x, y,
                    ring || centreDot ? color : clear);
            }
        }

        texture.Apply(false, false);

        return texture;
    }
}
