using UnityEngine;

/// <summary>
/// Camera-relative tilted wrist display plane (Phase 06 Revision round 3).
///
/// A Quad child of the first-person camera with a MeshCollider. The wrist
/// RenderTexture is attached as its texture; Show/SetScale animate the
/// "raise the wrist" feel. RaycastToLocal converts a hardware cursor ray into
/// display UV coordinates used by SurfaceWristController's map hit-test.
/// </summary>
public class WristDisplayAnchor : MonoBehaviour
{
    private Renderer _renderer;
    private Collider _collider;
    private Material _materialInstance;
    private Vector3 _baseLocalScale = Vector3.one;
    private bool _visible;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _collider = GetComponent<Collider>();
        _baseLocalScale = transform.localScale;
    }

    public void Show(bool visible)
    {
        _visible = visible;
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
        if (_renderer == null || texture == null)
        {
            return;
        }

        if (_materialInstance == null)
        {
            _materialInstance = _renderer.material; // runtime instance only
        }

        _materialInstance.mainTexture = texture;
    }

    public void SetScale(float scale01)
    {
        if (_visible)
        {
            transform.localScale = _baseLocalScale * scale01;
        }
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
