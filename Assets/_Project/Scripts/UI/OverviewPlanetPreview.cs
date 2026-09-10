using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Authoritative rotating 3D planet preview for the Overview screen
/// (Phase 07B.2.4).
///
/// ONE shared rig, ONE RenderTexture, any number of Overview hosts:
///
///     OverviewPlanetPreview            (DontDestroyOnLoad, built on demand)
///     ├─ PlanetPreviewCamera           (perspective, FOV 28, 4.5 radii back)
///     ├─ PlanetPivot                   (axial tilt)
///     │   └─ PlanetSphere              (real Sphere mesh, radius 1)
///     └─ PreviewKeyLight               (Directional; direction authority)
///
/// The rig is placed far below the world origin, i.e. outside every scene
/// camera's far plane (ship 60 m, surface 220 m, hub/boot default 1000 m), so it
/// can never appear in a gameplay camera and needs no layer-mask changes. The
/// preview camera renders only the sphere into OverviewPlanetRT and has
/// post-processing DISABLED, so the console-focus Depth Of Field (a global
/// volume) can never blur the planet; the direction of its key light is fed to
/// the material every frame, and the material shades analytically, so scene
/// lights can never contaminate the survey look.
///
/// Lifecycle: hosts Acquire()/Release() the rig and mark themselves as viewers.
/// The camera renders only while at least one viewer is visible - and because
/// the ship's Overview terminal is an always-on physical screen, that viewer
/// stays registered for the whole ship interior, so the bridge planet keeps
/// turning even when the player is not looking at it. The spin angle is
/// accumulated every frame and never reset, so console focus, wrist, scene
/// routing and re-entry all leave the rotation continuous.
/// </summary>
public sealed class OverviewPlanetPreview : MonoBehaviour
{
    // ---- Tuning (Phase 07B.2.4 spec) ----

    /// <summary>Real Sphere mesh radius in preview units.</summary>
    public const float SphereRadius = 1f;

    /// <summary>Camera field of view: a low-FOV perspective, not a flat disc.</summary>
    public const float CameraFieldOfView = 28f;

    /// <summary>Camera distance in sphere radii.</summary>
    public const float CameraDistance = 4.5f;

    /// <summary>Axial tilt of the spin axis (degrees).</summary>
    public const float AxialTiltDegrees = 11f;

    /// <summary>Continuous spin speed (degrees per second).</summary>
    public const float SpinDegreesPerSecond = 5f;

    /// <summary>Preview RenderTexture edge length (square).</summary>
    public const int TextureSize = 512;

    /// <summary>Distance below the world origin: beyond every scene far plane.</summary>
    public const float RigDepthBelowWorld = 3000f;

    /// <summary>Direction TOWARD the key light (upper-left-front), matching the
    /// static survey disc this preview replaces.</summary>
    public static readonly Vector3 KeyLightDirection =
        new Vector3(-0.60f, 0.34f, 0.72f).normalized;

    /// <summary>Build-safe authored material (Resources folder: a runtime-created
    /// rig has no scene reference that could keep the shader in a Release build).</summary>
    public const string MaterialResourcePath = "OverviewPlanet/M_OverviewPlanetPreview";

    private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");

    private static OverviewPlanetPreview _shared;

    /// <summary>The one shared rig (null until the first Acquire).</summary>
    public static OverviewPlanetPreview Shared => _shared;

    /// <summary>True once a rig exists.</summary>
    public static bool HasShared => _shared != null;

    /// <summary>
    /// Returns the shared rig, building it on first use. Owners must pair this
    /// with Release(owner) and may mark themselves visible with SetViewed.
    /// </summary>
    public static OverviewPlanetPreview Acquire(Object owner)
    {
        if (_shared == null)
        {
            var go = new GameObject("OverviewPlanetPreview");
            _shared = go.AddComponent<OverviewPlanetPreview>();
        }

        if (owner != null)
        {
            _shared._viewers[owner] = false;
        }

        return _shared;
    }

    // ---- Runtime state ----

    private readonly Dictionary<Object, bool> _viewers = new Dictionary<Object, bool>();
    private RenderTexture _renderTexture;
    private Camera _camera;
    private Transform _root;
    private Transform _pivot;
    private Transform _sphere;
    private Light _keyLight;
    private Material _material;

    /// <summary>The RenderTexture every Overview host displays.</summary>
    public RenderTexture Texture => _renderTexture;

    /// <summary>The dedicated preview camera (never in any camera stack).</summary>
    public Camera PreviewCamera => _camera;

    /// <summary>Spin axis pivot (carries the axial tilt).</summary>
    public Transform PlanetPivot => _pivot;

    /// <summary>The real sphere mesh transform.</summary>
    public Transform PlanetSphere => _sphere;

    /// <summary>Dedicated preview key light (direction authority).</summary>
    public Light KeyLight => _keyLight;

    /// <summary>Accumulated spin angle in degrees (never reset).</summary>
    public float SpinAngleDegrees { get; private set; }

    /// <summary>True while the preview camera renders into the RenderTexture.</summary>
    public bool IsRendering => _camera != null && _camera.enabled;

    /// <summary>Number of hosts currently marked visible.</summary>
    public int ViewerCount
    {
        get
        {
            int count = 0;
            foreach (KeyValuePair<Object, bool> pair in _viewers)
            {
                if (pair.Key != null && pair.Value)
                {
                    count++;
                }
            }

            return count;
        }
    }

    private void Awake()
    {
        if (_shared != null && _shared != this)
        {
            // Defensive: never allow a second rig (duplicate cameras / updates).
            Debug.LogWarning("[OverviewPlanetPreview] duplicate rig destroyed");
            Destroy(gameObject);
            return;
        }

        _shared = this;
        DontDestroyOnLoad(gameObject);
        Build();
    }

    /// <summary>Marks an owner as visible/hidden; the camera follows the total.</summary>
    public void SetViewed(Object owner, bool viewed)
    {
        if (owner == null)
        {
            return;
        }

        _viewers[owner] = viewed;
        PruneViewers();
    }

    /// <summary>Drops an owner (its viewer flag goes with it).</summary>
    public void Release(Object owner)
    {
        if (owner != null)
        {
            _viewers.Remove(owner);
        }

        PruneViewers();
    }

    private void PruneViewers()
    {
        List<Object> dead = null;
        foreach (KeyValuePair<Object, bool> pair in _viewers)
        {
            if (pair.Key == null)
            {
                dead ??= new List<Object>();
                dead.Add(pair.Key);
            }
        }

        if (dead == null)
        {
            return;
        }

        foreach (Object key in dead)
        {
            _viewers.Remove(key);
        }
    }

    private void Update()
    {
        PruneViewers();

        // Continuous, never-reset rotation: focus transitions, wrist opens and
        // scene routing all leave the angle untouched.
        SpinAngleDegrees = Mathf.Repeat(SpinAngleDegrees
                                        + SpinDegreesPerSecond * Time.deltaTime, 360f);
        if (_sphere != null)
        {
            _sphere.localRotation = Quaternion.Euler(0f, SpinAngleDegrees, 0f);
        }

        if (_material != null && _keyLight != null)
        {
            // The light object stays the single authority for the direction.
            _material.SetVector(LightDirectionId, -_keyLight.transform.forward);
        }

        bool shouldRender = ViewerCount > 0;
        if (_camera != null && _camera.enabled != shouldRender)
        {
            _camera.enabled = shouldRender;
        }
    }

    private void OnDestroy()
    {
        if (_shared == this)
        {
            _shared = null;
        }

        if (_material != null)
        {
            Destroy(_material);
            _material = null;
        }

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }
    }

    // ---- Rig construction ----

    private void Build()
    {
        _root = transform;
        _root.position = new Vector3(0f, -RigDepthBelowWorld, 0f);
        _root.rotation = Quaternion.identity;
        _root.localScale = Vector3.one;

        _renderTexture = new RenderTexture(TextureSize, TextureSize, 24,
            RenderTextureFormat.ARGB32)
        {
            name = "OverviewPlanetRT",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1
        };
        _renderTexture.Create();

        // ---- preview camera ----
        var cameraGo = new GameObject("PlanetPreviewCamera");
        cameraGo.transform.SetParent(_root, false);
        cameraGo.transform.localPosition = new Vector3(0f, 0f, -CameraDistance);
        cameraGo.transform.localRotation = Quaternion.identity;
        _camera = cameraGo.AddComponent<Camera>();
        _camera.clearFlags = CameraClearFlags.SolidColor;
        // Transparent clear: the sphere silhouette is the only thing drawn, so
        // the UI element shows the overview background around it (never a black
        // RenderTexture plate).
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.fieldOfView = CameraFieldOfView;
        _camera.nearClipPlane = 1.0f;
        _camera.farClipPlane = 20f;
        _camera.cullingMask = ~0; // only the sphere exists inside this volume
        _camera.targetTexture = _renderTexture;
        _camera.allowHDR = false;
        _camera.allowMSAA = true;
        _camera.depth = -100f;
        _camera.enabled = false; // enabled only while a viewer is visible

        // No post-processing and no volumes: the console-focus DOF volume is
        // global and would otherwise blur the planet.
        var cameraData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderType = CameraRenderType.Base;
        cameraData.renderPostProcessing = false;
        cameraData.volumeLayerMask = 0;
        cameraData.antialiasing = AntialiasingMode.None;
        cameraData.renderShadows = false;

        // ---- planet pivot + real sphere mesh ----
        var pivotGo = new GameObject("PlanetPivot");
        pivotGo.transform.SetParent(_root, false);
        pivotGo.transform.localPosition = Vector3.zero;
        pivotGo.transform.localRotation = Quaternion.Euler(0f, 0f, -AxialTiltDegrees);
        _pivot = pivotGo.transform;

        var sphereGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereGo.name = "PlanetSphere";
        sphereGo.transform.SetParent(_pivot, false);
        sphereGo.transform.localPosition = Vector3.zero;
        sphereGo.transform.localRotation = Quaternion.identity;
        sphereGo.transform.localScale = Vector3.one * (SphereRadius * 2f);
        Collider collider = sphereGo.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        _sphere = sphereGo.transform;

        _material = CreatePlanetMaterial();
        Renderer sphereRenderer = sphereGo.GetComponent<Renderer>();
        if (sphereRenderer != null)
        {
            sphereRenderer.sharedMaterial = _material;
            sphereRenderer.shadowCastingMode = ShadowCastingMode.Off;
            sphereRenderer.receiveShadows = false;
            sphereRenderer.lightProbeUsage = LightProbeUsage.Off;
            sphereRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ---- dedicated key light (direction authority) ----
        var lightGo = new GameObject("PreviewKeyLight");
        lightGo.transform.SetParent(_root, false);
        lightGo.transform.localRotation = Quaternion.LookRotation(-KeyLightDirection);
        _keyLight = lightGo.AddComponent<Light>();
        _keyLight.type = LightType.Directional;
        _keyLight.color = new Color(0.98f, 1f, 1f);
        _keyLight.intensity = 1.0f;
        _keyLight.shadows = LightShadows.None;
        // The surface shades analytically, so the light must never touch scene
        // geometry (cullingMask 0) - only its direction is consumed.
        _keyLight.cullingMask = 0;

        Debug.Log("[OverviewPlanetPreview] rig built: rt=" + _renderTexture.width
                  + "x" + _renderTexture.height + " fov=" + CameraFieldOfView
                  + " distance=" + CameraDistance + " tilt=" + AxialTiltDegrees
                  + " speed=" + SpinDegreesPerSecond + "deg/s");
    }

    private Material CreatePlanetMaterial()
    {
        var asset = Resources.Load<Material>(MaterialResourcePath);
        Material material = asset != null
            ? new Material(asset)
            : CreateRuntimeMaterial();

        if (material != null)
        {
            material.name = "M_OverviewPlanetPreview_Runtime";
            material.SetVector(LightDirectionId, KeyLightDirection);
        }

        return material;
    }

    private Material CreateRuntimeMaterial()
    {
        Shader shader = Shader.Find("Survive/PlanetPreviewUnlit");
        if (shader == null)
        {
            Debug.LogError("[OverviewPlanetPreview] missing "
                           + MaterialResourcePath + " and shader fallback");
            return null;
        }

        return new Material(shader);
    }

    // ---- Diagnostics used by the runtime test / probe ----

    /// <summary>True when the rig has a real mesh renderer carrying a real mesh
    /// (the point of the enhancement: no sprite, no UV scroll, no flat disc).</summary>
    public bool HasRealPlanetMesh(out string report)
    {
        report = "sphere=" + (_sphere != null);
        MeshRenderer renderer = _sphere != null
            ? _sphere.GetComponent<MeshRenderer>()
            : null;
        MeshFilter filter = _sphere != null ? _sphere.GetComponent<MeshFilter>() : null;
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        report += " meshRenderer=" + (renderer != null)
                  + " mesh=" + (mesh != null ? mesh.name : "<null>")
                  + " vertices=" + (mesh != null ? mesh.vertexCount : 0)
                  + " bounds=" + (mesh != null ? mesh.bounds.size.ToString() : "-");
        return renderer != null && mesh != null && mesh.vertexCount > 100;
    }

    /// <summary>Full structural report for tests and the release probe.</summary>
    public bool VerifySetup(out string report)
    {
        var sb = new System.Text.StringBuilder();
        bool ok = true;
        sb.Append("rig=").Append(name)
          .Append(" position=").Append(transform.position)
          .Append(" sphere=").Append(_sphere != null)
          .Append(" pivot=").Append(_pivot != null)
          .Append(" camera=").Append(_camera != null)
          .Append(" light=").Append(_keyLight != null)
          .Append(" angle=").Append(SpinAngleDegrees.ToString("0.0"));

        if (_renderTexture == null || !_renderTexture.IsCreated())
        {
            sb.Append(" ERROR: RenderTexture missing");
            ok = false;
        }
        else
        {
            sb.Append(" rt=").Append(_renderTexture.width).Append("x")
              .Append(_renderTexture.height);
            if (_renderTexture.width != _renderTexture.height)
            {
                sb.Append(" ERROR: RT must be square");
                ok = false;
            }
        }

        if (_camera != null)
        {
            sb.Append(" cameraTarget=")
              .Append(_camera.targetTexture != null ? _camera.targetTexture.name : "<null>")
              .Append(" postProcess=").Append(
                  _camera.GetComponent<UniversalAdditionalCameraData>() != null
                  && _camera.GetComponent<UniversalAdditionalCameraData>()
                      .renderPostProcessing);
            if (_camera.targetTexture != _renderTexture)
            {
                sb.Append(" ERROR: camera does not render into the preview RT");
                ok = false;
            }

            var data = _camera.GetComponent<UniversalAdditionalCameraData>();
            if (data != null && data.renderPostProcessing)
            {
                sb.Append(" ERROR: preview camera must not post-process");
                ok = false;
            }
        }
        else
        {
            sb.Append(" ERROR: preview camera missing");
            ok = false;
        }

        if (!HasRealPlanetMesh(out string meshReport))
        {
            sb.Append(" ERROR: no real planet mesh");
            ok = false;
        }

        sb.Append(" | ").Append(meshReport);
        sb.Append(" viewers=").Append(ViewerCount)
          .Append(" rendering=").Append(IsRendering);

        report = sb.ToString();
        return ok;
    }
}
