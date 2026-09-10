using UnityEngine;

/// <summary>
/// SINGLE AUTHORITATIVE console screen UV transform (Phase 07B.2.2).
///
/// Canonical screen space - what a player standing in front of a physical
/// console actually sees:
///
///     U: 0 = UI left     1 = UI right
///     V: 0 = UI bottom   1 = UI top
///
/// Two independent conventions compose here, and they are NOT the same thing:
///
/// 1. GEOMETRY (platform independent). The physical display is a Unity Quad
///    whose mesh u grows along local +x and mesh v along local +y. A viewer in
///    front of a console looks along the console's local -z (the console's
///    authored outward direction), so the viewer's right hand points along the
///    quad's local -x. Sampling the raw mesh u would show the UI mirrored
///    horizontally: that single correction is FlipU = 1.
///
/// 2. RENDER-TEXTURE ROW ORDER. UI Toolkit's panel renderer writes the panel
///    into "ConsoleUvAuthority"-bound PanelSettings.targetTexture using Unity's
///    standard render-texture vertical convention (texture v = 0 carries the
///    panel BOTTOM row), exactly like a camera-rendered RenderTexture. Unity
///    normalises that convention across graphics APIs, which is why a camera
///    RenderTexture on a quad needs no V flip on any platform. The mesh V axis
///    (v = 0 at the quad bottom) is therefore already canonical and must NOT be
///    flipped: FlipV = 0.
///
///    Measured on the real Release path, not asserted: the TL/TR/BL/BR markers
///    live inside the real UIDocument tree (WorldConsoleSurface
///    .SetUvReferenceMarkersVisible) and are captured through
///    UIDocument -> PanelSettings -> RenderTexture -> ScreenDisplayUnlit ->
///    Windows standalone Release player (D3D12,
///    SystemInfo.graphicsUVStartsAtTop = true). With FlipV = 1 the RED
///    panel-top-left marker was measured at the screen's BOTTOM-left (whole UI
///    upside down); with FlipV = 0 all four markers land canonically.
///
///    The previous release shipped FlipV = 1 because the 07B.2.1 diagnostic
///    scored its own captures with an inverted Y comparison (Texture2D.GetPixel
///    y grows UPWARD while the scorer assumed image y grows downward), so the
///    vertically-flipped mapping scored 4/4 and the canonical one scored 2/4.
///    That is the whole reason "diagnostic PASS but the real Release UI was
///    upside down".
///
/// Everything that must agree about orientation reads these values:
///   - the screen shader (_FlipU / _FlipV material properties),
///   - the world pointer router (mesh uv -> panel pixel),
///   - the focus proxy material,
///   - the TL/TR/BL/BR diagnostic and its runtime test.
///
/// Never compensate twice (no extra quad rotation, no pointer-side flip on top
/// of the shader flip).
/// </summary>
public static class ConsoleUvAuthority
{
    /// <summary>1 = mirror U once (required: viewer right = quad local -x for
    /// every console). Measured on the real Release physical screen by the
    /// TL/TR/BL/BR diagnostic: with FlipU = 0 the RED panel top-left marker
    /// appears on the screen's right side.</summary>
    public const float FlipU = 1f;

    /// <summary>0 = no V flip. The panel RenderTexture already carries the
    /// panel's bottom row at texture v = 0 (Unity's standard render-texture
    /// convention, normalised across graphics APIs), and mesh v = 0 is the
    /// quad's bottom row, so the two already agree. Measured on the real
    /// Release physical screen: with FlipV = 1 the RED panel top-left marker
    /// appeared at the screen's BOTTOM-left (UI upside down).</summary>
    public const float FlipV = 0f;

    public const string FlipUProperty = "_FlipU";
    public const string FlipVProperty = "_FlipV";

    /// <summary>The one platform/API description a reviewer needs to read a
    /// Release UV report. Purely informational: both flips above are canonical
    /// and platform independent (see the class summary), so nothing here is
    /// switched on Application.isEditor or on the graphics API.</summary>
    public static string DescribeConvention()
    {
        return "graphicsAPI=" + SystemInfo.graphicsDeviceType
               + " uvStartsAtTop=" + SystemInfo.graphicsUVStartsAtTop
               + " FlipU=" + FlipU.ToString("0")
               + " FlipV=" + FlipV.ToString("0");
    }

    /// <summary>Canonical sampled texture coordinate for a mesh uv. This is the
    /// coordinate the screen shader samples and the pointer chain inverts.</summary>
    public static Vector2 SampleUv(Vector2 meshUv)
    {
        float u = FlipU > 0.5f ? 1f - meshUv.x : meshUv.x;
        float v = FlipV > 0.5f ? 1f - meshUv.y : meshUv.y;
        return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
    }

    /// <summary>Panel pixel -> mesh uv (the inverse of
    /// <see cref="UvToPanelPixel"/>, used by every probe/test that has to turn a
    /// UI element into a point on the physical quad).</summary>
    public static Vector2 MeshUvFromPanelPixel(Vector2 panelPixel, Vector2 panelSize)
    {
        float cu = panelSize.x > 0f ? Mathf.Clamp01(panelPixel.x / panelSize.x) : 0f;
        float cv = panelSize.y > 0f
            ? Mathf.Clamp01(1f - panelPixel.y / panelSize.y)
            : 0f;
        float u = FlipU > 0.5f ? 1f - cu : cu;
        float v = FlipV > 0.5f ? 1f - cv : cv;
        return new Vector2(u, v);
    }

    /// <summary>Mesh uv (from the screen collider hit) -> panel pixel.
    ///
    /// Canonical V is 0 at the UI bottom while UI Toolkit panel space grows
    /// DOWNWARD from a top-left origin, so the vertical leg carries exactly one
    /// inversion. That inversion belongs here and nowhere else: the shader
    /// samples the canonical uv directly, so the point the shader paints and
    /// the point the pointer resolves are the same point by construction.</summary>
    public static Vector2 UvToPanelPixel(Vector2 meshUv, Vector2 panelSize)
    {
        Vector2 canonical = SampleUv(meshUv);
        return new Vector2(canonical.x * panelSize.x,
            (1f - canonical.y) * panelSize.y);
    }

    /// <summary>Applies the authoritative flip properties to a screen
    /// material instance (idempotent; called for every console and for the
    /// focus proxy).</summary>
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
