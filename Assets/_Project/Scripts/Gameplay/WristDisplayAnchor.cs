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

    public Renderer SurfaceRenderer => _renderer;
    public Material SurfaceMaterial => _materialInstance;
    public Collider SurfaceCollider => _collider;
    public bool Visible => _visible;

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
        }
    }

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

        // URP Unlit samples _BaseMap; legacy paths sample _MainTex. Set both
        // so whichever shader variant is active shows the wrist UI.
        _materialInstance.mainTexture = texture;
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
        return texture != null
               && (_materialInstance != null
                   && (_materialInstance.mainTexture == texture
                       || _materialInstance.GetTexture("_BaseMap") == texture));
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

        // Quad mesh spans +-0.5 in its local XY plane regardless of scale.
        Vector3 local = transform.InverseTransformPoint(hit.point);
        uv = new Vector2(Mathf.Clamp01(local.x + 0.5f),
            Mathf.Clamp01(local.y + 0.5f));
        return true;
    }
}
