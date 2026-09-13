using UnityEngine;

/// <summary>
/// Camera-relative tilted wrist display plane (Phase 06 Revision round 3-4).
///
/// A Quad child of the first-person camera with a MeshCollider. The wrist
/// RenderTexture is attached through URP-correct material properties (both
/// _MainTex and URP _BaseMap), culling is disabled so orientation mistakes
/// can never hide the surface, and the surface is double-sided opaque white.
/// Show/SetScale animate the "raise the wrist" feel; RaycastToLocal converts
/// a hardware cursor ray into display UV coordinates used by the map hit-test.
/// </summary>
public class WristDisplayAnchor : MonoBehaviour
{
    private Renderer _renderer;
    private Collider _collider;
    private Material _materialInstance;
    private Vector3 _baseLocalScale = Vector3.one;
    private bool _visible;
    private float _frostAmount;

    public Renderer SurfaceRenderer => _renderer;
    public Material SurfaceMaterial => _materialInstance;
    public Collider SurfaceCollider => _collider;
    public bool Visible => _visible;

    /// <summary>Unity's built-in Quad faces its front toward -Z; therefore the
    /// display front normal is the negated transform.forward. This is the
    /// direction the readable UI texture faces, used by visibility checks.</summary>
    public Vector3 FrontNormal => -transform.forward;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _collider = GetComponent<Collider>();
        _baseLocalScale = transform.localScale;
        EnsureMaterial();
    }

    private void EnsureMaterial()
    {
        if (_renderer == null)
        {
            return;
        }

        // Runtime instance only; never mutates the shared project material.
        if (_materialInstance == null)
        {
            _materialInstance = _renderer.material;
            _materialInstance.color = Color.white;

            // The frosted lens needs the map disc's position in the UI texture.
            // Both come from the accepted radial layout, so the mask can never
            // drift from the geometry the player reads.
            Vector2 centre = new Vector2(
                RadialWristElement.Center.x / RadialWristElement.DesignWidth,
                RadialWristElement.Center.y / RadialWristElement.DesignHeight);
            Vector2 radius = new Vector2(
                RadialWristElement.MapRadius / RadialWristElement.DesignWidth,
                RadialWristElement.MapRadius / RadialWristElement.DesignHeight);
            if (_materialInstance.HasProperty("_MapCentre"))
            {
                _materialInstance.SetVector("_MapCentre", new Vector4(centre.x, centre.y, 0f, 0f));
            }

            if (_materialInstance.HasProperty("_MapRadius"))
            {
                _materialInstance.SetVector("_MapRadius", new Vector4(radius.x, radius.y, 0f, 0f));
            }

            SetFrost(0f);
            ApplyStructureOpacity();
        }
    }

    /// <summary>
    /// Phase 09B PASS 3: how opaque the PERIPHERAL structure of the hologram is
    /// (the four module plates and the status arcs - everything outside the map
    /// disc). The UI Toolkit render target hands the sheet its pixels with their
    /// authored colours but not their authored translucency, so the one place the
    /// peripheral opacity can live is this sheet shader; 0.66 keeps the world
    /// visibly behind the instrument while leaving the readable layer crisp.
    /// The central disc is explicitly EXCLUDED (its own frosted lens owns it), so
    /// the accepted "frosted map + cyan core showing through" relation is
    /// untouched.
    /// </summary>
    public const float StructureSheetOpacity = 0.66f;

    private void ApplyStructureOpacity()
    {
        if (_materialInstance != null && _materialInstance.HasProperty("_StructureOpacity"))
        {
            _materialInstance.SetFloat("_StructureOpacity", StructureSheetOpacity);
        }
    }

    /// <summary>Current peripheral structure opacity of the sheet material.</summary>
    public float StructureOpacity =>
        _materialInstance != null && _materialInstance.HasProperty("_StructureOpacity")
            ? _materialInstance.GetFloat("_StructureOpacity")
            : 1f;

    /// <summary>
    /// Frosted hologram lens amount (0 = clear sheet, 1 = fully frosted).
    ///
    /// Inside the map disc the sheet becomes partly transparent and picks up a
    /// milky tint plus a faint grain, so the physical wrist behind it reads as a
    /// soft cyan/blue glow instead of being hidden. The UI's own content is drawn
    /// with its own pixels: nothing here blurs or dims the readable layer, and
    /// nothing outside the disc changes at all.
    /// </summary>
    public void SetFrost(float amount)
    {
        EnsureMaterial();
        _frostAmount = Mathf.Clamp01(amount);
        if (_materialInstance == null)
        {
            return;
        }

        float blend = _frostAmount;
        if (_materialInstance.HasProperty("_Opacity"))
        {
            _materialInstance.SetFloat("_Opacity", 1f);
        }

        if (_materialInstance.HasProperty("_FrostOpacity"))
        {
            _materialInstance.SetFloat("_FrostOpacity",
                Mathf.Lerp(1f, FrostedDiscOpacity, blend));
        }

        if (_materialInstance.HasProperty("_FrostTint"))
        {
            _materialInstance.SetFloat("_FrostTint", FrostMilkyTint * blend);
        }

        if (_materialInstance.HasProperty("_FrostGrain"))
        {
            _materialInstance.SetFloat("_FrostGrain", FrostGrain * blend);
        }
    }

    /// <summary>
    /// Re-asserts the frosted lens every frame. The presentation drives the value,
    /// and this makes the material state impossible to drift from it (a shader
    /// property left over from another state would silently change how much of the
    /// physical watch shows through the map).
    /// </summary>
    private void LateUpdate()
    {
        if (_materialInstance == null)
        {
            return;
        }

        float expected = Mathf.Lerp(1f, FrostedDiscOpacity, _frostAmount);
        if (_materialInstance.HasProperty("_FrostOpacity")
            && !Mathf.Approximately(_materialInstance.GetFloat("_FrostOpacity"), expected))
        {
            SetFrost(_frostAmount);
        }

        // Same protection for the PASS 3 structure sheet: a leftover shader value
        // would silently change how much of the world shows through the plates.
        if (_materialInstance.HasProperty("_StructureOpacity")
            && !Mathf.Approximately(_materialInstance.GetFloat("_StructureOpacity"),
                StructureSheetOpacity))
        {
            ApplyStructureOpacity();
        }
    }

    /// <summary>Opacity of the hologram sheet inside the map disc when frosted.</summary>
    public const float FrostedDiscOpacity = 0.70f;

    /// <summary>Milky lift applied inside the frosted disc.</summary>
    public const float FrostMilkyTint = 0.26f;

    /// <summary>Static per-pixel grain applied inside the frosted disc.</summary>
    public const float FrostGrain = 0.035f;

    public void Show(bool visible)
    {
        _visible = visible;
        EnsureMaterial();
        if (_renderer != null)
        {
            _renderer.enabled = visible;
        }

        if (_collider != null)
        {
            _collider.enabled = visible;
        }
    }

    public void AttachTexture(RenderTexture texture)
    {
        EnsureMaterial();
        if (_materialInstance == null || texture == null)
        {
            return;
        }

        _materialInstance.SetTexture("_BaseMap", texture);
    }

    public void SetScale(float scale01)
    {
        if (_visible)
        {
            transform.localScale = _baseLocalScale * scale01;
        }
    }

    /// <summary>True when this renderer currently draws the given texture.</summary>
    public bool HasTexture(RenderTexture texture)
    {
        EnsureMaterial();
        return texture != null && _materialInstance != null
               && _materialInstance.GetTexture("_BaseMap") == texture;
    }

    /// <summary>Converts a world point on the display plane to its UV
    /// (0..1, bottom-left origin) using the same local-space math as the
    /// hit-test. u grows toward local +X (display right), v grows toward local
    /// +Y (display top).</summary>
    public Vector2 WorldPointToUV(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        return new Vector2(Mathf.Clamp01(local.x + 0.5f),
            Mathf.Clamp01(local.y + 0.5f));
    }

    /// <summary>Casts a cursor ray onto the display plane and returns its UV
    /// (0..1, bottom-left origin) when hit.</summary>
    public bool RaycastToLocal(Ray ray, out Vector2 uv)
    {
        uv = Vector2.zero;
        if (_collider == null || !_collider.enabled)
        {
            return false;
        }

        RaycastHit hit;
        if (!_collider.Raycast(ray, out hit, 300f))
        {
            return false;
        }

        uv = WorldPointToUV(hit.point);
        return true;
    }
}
