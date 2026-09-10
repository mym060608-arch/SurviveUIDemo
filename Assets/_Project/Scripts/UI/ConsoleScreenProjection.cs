using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Single authority for the physical console screen projection (Phase
/// 07B.2.1). Every probe and test that must turn a UI element (or a panel
/// pixel) into a point on the physical quad goes through here, so the pointer
/// chain and the visual orientation can never drift apart:
///
///   panel pixel  --ConsoleUvAuthority--> mesh uv --> quad local point
///                --quad transform------> world point --camera--> viewport
///
/// The console display quad is a Unity Quad: mesh uv (0,0) is the local
/// bottom-left, (1,1) the local top-right. ConsoleUvAuthority owns the ONE
/// orientation correction (viewer right = quad local -x for every console).
/// </summary>
public static class ConsoleScreenProjection
{
    /// <summary>Quad local (mesh) uv for a panel pixel.</summary>
    public static Vector2 MeshUvFromPanelPixel(Vector2 panelPixel)
    {
        return ConsoleUvAuthority.MeshUvFromPanelPixel(panelPixel,
            new Vector2(WorldConsoleSurface.UiWidth, WorldConsoleSurface.UiHeight));
    }

    /// <summary>Panel pixel -> world point on the physical screen quad.</summary>
    public static bool TryQuadPoint(Transform quad, Vector2 panelPixel,
        out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        if (quad == null)
        {
            return false;
        }

        Vector2 uv = MeshUvFromPanelPixel(panelPixel);
        Vector3 scale = quad.lossyScale;
        worldPoint = quad.position
                     + quad.right * ((uv.x - 0.5f) * scale.x)
                     + quad.up * ((uv.y - 0.5f) * scale.y);
        return true;
    }

    /// <summary>Element centre -> viewport fraction (0..1) of the given camera.</summary>
    public static bool TryElementViewport(WorldConsoleSurface surface,
        Camera camera, VisualElement element, out Vector2 viewport)
    {
        viewport = Vector2.zero;
        if (surface == null || camera == null || element == null
            || surface.ScreenQuad == null)
        {
            return false;
        }

        Rect bounds = element.worldBound;
        if (bounds.width < 1f || bounds.height < 1f)
        {
            bounds = new Rect(element.resolvedStyle.left, element.resolvedStyle.top,
                element.resolvedStyle.width, element.resolvedStyle.height);
        }

        if (bounds.width < 1f || bounds.height < 1f
            || float.IsNaN(bounds.center.x) || float.IsNaN(bounds.center.y))
        {
            return false;
        }

        return TryPanelPixelViewport(surface, camera, bounds.center, out viewport);
    }

    public static bool TryPanelPixelViewport(WorldConsoleSurface surface,
        Camera camera, Vector2 panelPixel, out Vector2 viewport)
    {
        viewport = Vector2.zero;
        if (surface == null || camera == null || surface.ScreenQuad == null)
        {
            return false;
        }

        if (!TryQuadPoint(surface.ScreenQuad, panelPixel, out Vector3 world))
        {
            return false;
        }

        Vector3 vp = camera.WorldToViewportPoint(world);
        if (vp.z <= 0f)
        {
            return false;
        }

        viewport = new Vector2(vp.x, vp.y);
        return true;
    }
}
