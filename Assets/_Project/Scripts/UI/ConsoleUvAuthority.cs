using UnityEngine;

/// <summary>
/// SINGLE AUTHORITATIVE console screen UV transform (Phase 07B.2.1).
///
/// Canonical screen space - what a player standing in front of a physical
/// console actually sees:
///
///     U: 0 = UI left     1 = UI right
///     V: 0 = UI bottom   1 = UI top
///
/// Physical quad convention: the display surface is a Unity Quad whose mesh
/// u grows along local +x and mesh v along local +y. A viewer in front of a
/// console looks along the console's local -z (the console's authored
/// outward direction), so for EVERY console the viewer's right hand points
/// along the quad's local -x. Sampling the raw mesh uv would therefore show
/// the UI mirrored horizontally - the single authoritative correction is the
/// U flip below, applied ONCE in the screen shader.
///
/// Everything that must agree about orientation reads these values:
///   - the screen shader (_FlipU / _FlipV material properties),
///   - the world pointer router (mesh uv -> panel pixel),
///   - the TL/TR/BL/BR diagnostic and its runtime test.
///
/// Never compensate twice (no extra quad rotation, no pointer-side flip on
/// top of the shader flip).
/// </summary>
public static class ConsoleUvAuthority
{
    /// <summary>1 = mirror U once (required: viewer right = quad local -x).
    /// Measured on the real Release physical screen by the TL/TR/BL/BR
    /// diagnostic (ShipB21ShotProbe): with FlipU=0 the RED panel top-left
    /// marker appears on the screen's right side.</summary>
    public const float FlipU = 1f;

    /// <summary>1 = flip V once (required: the UI Toolkit RenderTexture stores
    /// the panel TOP row at texture v=0, so the mesh V axis must be mirrored
    /// to show the panel upright). Measured: with FlipV=0 the RED top-left
    /// marker appeared at the screen's BOTTOM-left.</summary>
    public const float FlipV = 1f;

    public const string FlipUProperty = "_FlipU";
    public const string FlipVProperty = "_FlipV";

    /// <summary>Canonical sampled texture coordinate for a mesh uv.</summary>
    public static Vector2 SampleUv(Vector2 meshUv)
    {
        float u = FlipU > 0.5f ? 1f - meshUv.x : meshUv.x;
        float v = FlipV > 0.5f ? 1f - meshUv.y : meshUv.y;
        return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
    }

    /// <summary>Mesh uv (from the screen collider hit) -> panel pixel.
    /// The measured RT convention is that texture v=0 carries the panel TOP
    /// row, so after applying the authoritative flips a plain multiply is the
    /// whole mapping. This single function keeps the displayed UI and the
    /// click position identical.</summary>
    public static Vector2 UvToPanelPixel(Vector2 meshUv, Vector2 panelSize)
    {
        Vector2 sampled = SampleUv(meshUv);
        return new Vector2(sampled.x * panelSize.x, sampled.y * panelSize.y);
    }

    /// <summary>Inverse: panel pixel -> mesh uv (used by every probe/test that
    /// has to turn a UI element into a point on the physical quad).</summary>
    public static Vector2 MeshUvFromPanelPixel(Vector2 panelPixel, Vector2 panelSize)
    {
        float sx = panelSize.x > 0f ? Mathf.Clamp01(panelPixel.x / panelSize.x) : 0f;
        float sy = panelSize.y > 0f ? Mathf.Clamp01(panelPixel.y / panelSize.y) : 0f;
        float u = FlipU > 0.5f ? 1f - sx : sx;
        float v = FlipV > 0.5f ? 1f - sy : sy;
        return new Vector2(u, v);
    }

    /// <summary>Applies the authoritative flip properties to a screen
    /// material instance (idempotent; called for every console).</summary>
    public static void ApplyToMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty(FlipUProperty))
        {
            material.SetFloat(FlipUProperty, FlipU);
        }

        if (material.HasProperty(FlipVProperty))
        {
            material.SetFloat(FlipVProperty, FlipV);
        }
    }
}
