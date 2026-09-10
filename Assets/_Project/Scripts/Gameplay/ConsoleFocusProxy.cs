using UnityEngine;

/// <summary>
/// Phase 07B.2.2 focus-only sharp proxy for the physical console screen.
///
/// ARCHITECTURE (why this exists at all):
/// the four real console screens are ordinary opaque WORLD geometry rendered by
/// the environment Base camera. They obey normal depth/occlusion (a wall hides
/// them, the wrist covers them) and they receive the console-focus Depth Of
/// Field like every other surface in the room. That is correct world behaviour,
/// but it means a focused screen would soften together with the environment.
///
/// This proxy is the sharp copy: an exactly co-located quad on the dedicated
/// ConsoleFocusOverlay layer, carrying the SAME RenderTexture and the SAME
/// ConsoleUvAuthority orientation as the console it covers, rendered by the
/// focus-only SharpScreenCamera (URP Overlay camera, post-processing OFF,
/// clearDepth FALSE so it keeps the Base camera's real depth buffer, ZTest
/// LEqual + ZWrite Off so it can never float above the world).
///
/// LIFECYCLE: disabled in ShipExplore, in the wrist, in loadout and in every
/// other state; enabled only by ShipConsoleFocusController while a console is
/// in TransitionIn / ShipConsoleFocus / TransitionOut; at most one surface can
/// be bound at a time.
/// </summary>
public class ConsoleFocusProxy : MonoBehaviour
{
    /// <summary>Offset along the screen normal toward the viewer. Large enough
    /// to beat depth comparison against the physical screen quad underneath,
    /// small enough to stay invisible (0.4% of the ~0.7 m focus distance).</summary>
    public const float NormalOffsetMeters = 0.004f;

    [SerializeField]
    private Camera _overlayCamera;

    [SerializeField]
    private Material _proxyMaterialAsset;

    private Renderer _renderer;
    private Material _materialInstance;
    private WorldConsoleSurface _surface;

    /// <summary>The one console whose screen this proxy currently mirrors
    /// (null whenever the proxy is hidden).</summary>
    public WorldConsoleSurface ActiveSurface => IsVisible ? _surface : null;

    /// <summary>True only while the proxy quad is actually rendered.</summary>
    public bool IsVisible => _renderer != null && _renderer.enabled;

    /// <summary>The focus-only SharpScreenCamera that renders this proxy.</summary>
    public Camera OverlayCamera => _overlayCamera;

    /// <summary>Material instance currently sampling the active RenderTexture.</summary>
    public Material ProxyMaterial => _materialInstance;

    public Renderer ProxyRenderer => _renderer;

    /// <summary>Builder configuration (scene authoring only).</summary>
    public void Configure(Camera overlayCamera, Material proxyMaterialAsset)
    {
        _overlayCamera = overlayCamera;
        _proxyMaterialAsset = proxyMaterialAsset;
    }

    private void Awake()
    {
        EnsureResources();
        // Never visible until a focus transition explicitly shows it.
        if (_renderer != null)
        {
            _renderer.enabled = false;
        }
    }

    private void OnDisable()
    {
        if (_renderer != null)
        {
            _renderer.enabled = false;
        }

        SetOverlayCameraEnabled(false);
    }

    private void OnDestroy()
    {
        if (_materialInstance != null)
        {
            Object.Destroy(_materialInstance);
            _materialInstance = null;
        }
    }

    private void EnsureResources()
    {
        if (_renderer == null)
        {
            _renderer = GetComponent<Renderer>();
        }

        if (_renderer == null)
        {
            Debug.LogError("[ConsoleFocusProxy] proxy quad has no Renderer");
            return;
        }

        if (_materialInstance == null && _proxyMaterialAsset != null)
        {
            _materialInstance = Object.Instantiate(_proxyMaterialAsset);
            _materialInstance.name = "M_ConsoleFocusProxy_Instance";
            ConsoleUvAuthority.ApplyToMaterial(_materialInstance);
        }

        if (_materialInstance == null)
        {
            Debug.LogError("[ConsoleFocusProxy] proxy material asset missing "
                           + "(an authored asset is required: a runtime "
                           + "Shader.Find lookup is stripped from Release)");
            return;
        }

        _renderer.sharedMaterial = _materialInstance;
        _renderer.enabled = false;
    }

    /// <summary>Mirrors the given console's physical screen and turns the
    /// sharp path on. Idempotent; rebinding simply retargets the one proxy.</summary>
    public bool Show(WorldConsoleSurface surface)
    {
        EnsureResources();
        if (surface == null || surface.ScreenQuad == null || _renderer == null)
        {
            Debug.LogWarning("[ConsoleFocusProxy] cannot show: surface="
                             + (surface != null) + " quad="
                             + (surface != null && surface.ScreenQuad != null)
                             + " renderer=" + (_renderer != null));
            return false;
        }

        _surface = surface;
        SyncToScreen(surface.ScreenQuad);
        BindTexture(surface.CurrentRenderTexture);
        _renderer.enabled = true;
        SetOverlayCameraEnabled(true);
        return true;
    }

    /// <summary>Turns the sharp path off and leaves the world screen visible.</summary>
    public void Hide()
    {
        if (_renderer != null)
        {
            _renderer.enabled = false;
        }

        SetOverlayCameraEnabled(false);
        // Unbind so a hidden proxy provably carries no console RenderTexture.
        if (_materialInstance != null)
        {
            _materialInstance.SetTexture("_BaseMap", null);
            _materialInstance.SetTexture("_MainTex", null);
        }

        _surface = null;
    }

    /// <summary>Exact transform match with the active physical screen, offset
    /// toward the viewer so the proxy wins the depth comparison against the
    /// screen underneath it.</summary>
    private void SyncToScreen(Transform screenQuad)
    {
        Vector3 outward = screenQuad.forward; // console-local +z = toward the room
        transform.SetPositionAndRotation(
            screenQuad.position + outward * NormalOffsetMeters,
            screenQuad.rotation);
        transform.localScale = screenQuad.lossyScale;
    }

    private void BindTexture(RenderTexture texture)
    {
        if (_materialInstance == null || texture == null)
        {
            return;
        }

        _materialInstance.SetTexture("_BaseMap", texture);
        _materialInstance.SetTexture("_MainTex", texture);
    }

    private void SetOverlayCameraEnabled(bool value)
    {
        if (_overlayCamera != null)
        {
            _overlayCamera.enabled = value;
        }
    }

    /// <summary>Complete lifecycle report used by ConsoleFocusProxyLifecycle_
    /// Runtime and by the Release probe.</summary>
    public bool VerifySetup(out string report)
    {
        var sb = new System.Text.StringBuilder();
        bool ok = true;

        GameObject go = gameObject;
        sb.Append("proxy=").Append(go.name)
          .Append(" active=").Append(go.activeInHierarchy)
          .Append(" layer=").Append(go.layer)
          .Append(" expectedLayer=").Append(WorldConsoleSurface.FocusOverlayLayer);

        if (go.layer != WorldConsoleSurface.FocusOverlayLayer)
        {
            sb.Append(" ERROR: proxy is not on the ConsoleFocusOverlay layer");
            ok = false;
        }

        if (_renderer == null)
        {
            sb.Append(" ERROR: renderer missing");
            report = sb.ToString();
            return false;
        }

        sb.Append(" rendererEnabled=").Append(_renderer.enabled);

        Material material = _renderer.sharedMaterial;
        if (material == null || material.shader == null)
        {
            sb.Append(" ERROR: proxy material/shader missing");
            report = sb.ToString();
            return false;
        }

        sb.Append(" shader=").Append(material.shader.name)
          .Append(" queue=").Append(material.renderQueue);

        if (material.shader.name != "Survive/ScreenFocusProxyUnlit")
        {
            sb.Append(" ERROR: proxy must use the authored proxy shader");
            ok = false;
        }

        // Transparent queue == the authored ZWrite Off / ZTest LEqual pass.
        if (material.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent)
        {
            sb.Append(" ERROR: proxy must render in the transparent queue "
                      + "(ZWrite Off)");
            ok = false;
        }

        Texture bound = material.GetTexture("_BaseMap");
        sb.Append(" boundTexture=").Append(bound != null ? bound.name : "NULL");
        if (IsVisible && _surface != null && _surface.CurrentRenderTexture != null
            && bound != (Texture)_surface.CurrentRenderTexture)
        {
            sb.Append(" ERROR: proxy does not sample the active console RT");
            ok = false;
        }

        if (_overlayCamera == null)
        {
            sb.Append(" ERROR: overlay camera not wired");
            ok = false;
        }
        else
        {
            var data = _overlayCamera.GetComponent<
                UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            sb.Append(" overlayEnabled=").Append(_overlayCamera.enabled)
              .Append(" overlayLayerMask=").Append(_overlayCamera.cullingMask)
              .Append(" clearDepth=")
              .Append(data != null ? data.clearDepth.ToString() : "-");
            if (data != null && data.clearDepth)
            {
                sb.Append(" ERROR: focus overlay camera clears depth (would "
                          + "float above the world)");
                ok = false;
            }
        }

        report = sb.ToString();
        return ok;
    }
}
