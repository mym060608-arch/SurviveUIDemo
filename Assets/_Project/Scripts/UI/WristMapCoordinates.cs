using UnityEngine;

/// <summary>
/// Single authoritative world -> wrist-map coordinate source (Phase 06
/// Revision CodeReference).
///
/// The SAME function drives the player marker, the node markers, the landing
/// marker, the selected ring, hover hit-test and left-click hit-test. There is
/// deliberately no second coordinate implementation (no separate 0-500 local
/// map coordinate system).
///
/// Output is absolute wrist RenderTexture pixel space (1280 x 800 design,
/// top-left origin), so the returned Vector2 can be used directly with
/// marker.style.left / marker.style.top.
/// </summary>
public static class WristMapCoordinates
{
    /// <summary>World-space half extent of the deterministic surface (XZ),
    /// matching the surface player bounds (~+/-16.5) and GameController.</summary>
    public const float SurfaceHalfExtent = 17f;

    /// <summary>Map marker travel radius inside the circular map (map visual
    /// radius is 235 px; this keeps markers inside the disc with margin).</summary>
    public const float UsableMapRadius = 205f;

    /// <summary>
    /// Map content zoom limits (Phase 09B). The zoom scales the world-to-disc
    /// mapping around the map centre, so "zoom in" spreads the markers apart and
    /// "zoom out" gathers them - the disc, the frame and every marker class are
    /// untouched, and at the default 1 the mapping is byte-identical to Phase 06.
    /// </summary>
    public const float MinZoom = 0.7f;
    public const float MaxZoom = 1.6f;

    /// <summary>World XZ -> absolute wrist pixel. World +Z is map "north"
    /// (up on screen, UI y down -> -nz). The offset is clamped into the usable
    /// disc so markers can never render outside the circular map boundary.</summary>
    public static Vector2 WorldToWristMap(
        Vector3 worldPosition)
    {
        return WorldToWristMap(worldPosition, 1f);
    }

    /// <summary>
    /// Zoomed world XZ -> absolute wrist pixel. The zoom is applied to the OFFSET
    /// (the distance from the map centre), never to the centre itself, so the
    /// player marker, the node markers, the selected ring, the hover test and the
    /// click test all stay on one coordinate source at every zoom level.
    /// </summary>
    public static Vector2 WorldToWristMap(
        Vector3 worldPosition,
        float zoom)
    {
        float nx = Mathf.Clamp(
            worldPosition.x / SurfaceHalfExtent,
            -1f,
            1f);

        float nz = Mathf.Clamp(
            worldPosition.z / SurfaceHalfExtent,
            -1f,
            1f);

        float scale = Mathf.Clamp(zoom, MinZoom, MaxZoom);

        Vector2 offset = new Vector2(
            nx * UsableMapRadius,
            -nz * UsableMapRadius) * scale;

        // Markers stay inside the circular map disc (CodeReference: nothing
        // outside the circle). Diagonal world corners may exceed the usable
        // radius, so the offset is scaled down radially, direction kept.
        if (offset.sqrMagnitude > UsableMapRadius * UsableMapRadius)
        {
            offset = offset.normalized * UsableMapRadius;
        }

        return RadialWristElement.Center + offset;
    }
}
