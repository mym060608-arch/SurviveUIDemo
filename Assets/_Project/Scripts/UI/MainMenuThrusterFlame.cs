using UnityEngine;

/// <summary>
/// One procedural thruster plume of the Phase 08 Main Menu hero ship.
///
/// The plume is a flared cone authored along local +Z (v = 0 at the nozzle,
/// v = 1 at the open tip) shaded additively by
/// <c>Survive/ThrusterPlumeUnlit</c>; no particle system, no sprite and no
/// texture are involved, so the whole effect is deterministic, allocation-free
/// per frame and cheap enough to run in the same frame as the menu.
///
/// The component owns three numbers only - engine level (0 idle .. 1 boost),
/// flicker and the derived length/intensity - and the ship hero drives them.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MainMenuThrusterFlame : MonoBehaviour
{
    /// <summary>Idle plume length in metres (low but always present).</summary>
    public const float IdleLength = 5.4f;

    /// <summary>Boost plume length in metres (departure ignition).</summary>
    public const float BoostLength = 15.5f;

    /// <summary>Idle additive intensity (visible, never blinding).</summary>
    public const float IdleIntensity = 0.95f;

    /// <summary>Boost additive intensity.</summary>
    public const float BoostIntensity = 3.6f;

    /// <summary>Idle plume radius as a fraction of the nozzle radius.</summary>
    public const float IdleRadius = 0.66f;

    /// <summary>Boost plume radius as a fraction of the nozzle radius.</summary>
    public const float BoostRadius = 1.02f;

    /// <summary>Reference length the generated mesh is authored with.</summary>
    private const float MeshLength = 1f;
    private const int Segments = 12;

    private static readonly float[] RingV = { 0f, 0.05f, 0.16f, 0.32f, 0.52f, 0.72f, 0.88f, 1f };
    private static readonly float[] RingR = { 0.60f, 1f, 0.93f, 0.76f, 0.56f, 0.36f, 0.19f, 0.03f };

    private MeshRenderer _renderer;
    private Material _material;
    private float _nozzleRadius = 1f;
    private float _seed;
    private float _sideways;
    private Vector3 _baseLocalDirection = Vector3.forward;
    private Vector3 _baseLocalUp = Vector3.up;

    private static Mesh _sharedMesh;

    /// <summary>0 = idle flame, 1 = full boost (smoothed every frame).</summary>
    public float EngineLevel { get; private set; }

    /// <summary>Current plume length in metres (verifiable in tests).</summary>
    public float PlumeLength { get; private set; }

    /// <summary>Current additive intensity (verifiable in tests).</summary>
    public float Intensity { get; private set; }

    /// <summary>Current flicker multiplier around 1.</summary>
    public float Flicker { get; private set; } = 1f;

    /// <summary>Builds one plume under <paramref name="parent"/>.</summary>
    /// <param name="parent">Ship-space parent (the ship hero root).</param>
    /// <param name="localPosition">Nozzle position in parent space.</param>
    /// <param name="localDirection">Exhaust direction in parent space.</param>
    /// <param name="nozzleRadius">Engine bell radius in metres.</param>
    /// <param name="sourceMaterial">Authored plume material (scene reference).</param>
    /// <param name="name">GameObject name.</param>
    public static MainMenuThrusterFlame Create(Transform parent, Vector3 localPosition,
        Vector3 localDirection, float nozzleRadius, Material sourceMaterial, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        Vector3 direction = localDirection.sqrMagnitude > 0.0001f
            ? localDirection.normalized
            : Vector3.forward;

        var flame = go.AddComponent<MainMenuThrusterFlame>();
        flame._baseLocalDirection = direction;
        flame._baseLocalUp = Vector3.up;
        go.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);
        flame.Configure(nozzleRadius, sourceMaterial);
        return flame;
    }

    /// <summary>Wires the nozzle geometry and the per-flame material instance.</summary>
    public void Configure(float nozzleRadius, Material sourceMaterial)
    {
        _nozzleRadius = Mathf.Max(0.05f, nozzleRadius);
        _seed = Random.Range(0f, 100f);
        _sideways = Random.Range(-1f, 1f);

        var filter = GetComponent<MeshFilter>();
        filter.sharedMesh = GetSharedMesh();

        _renderer = GetComponent<MeshRenderer>();
        // The authored material asset carries the build-safe shader reference;
        // the flame clones it so its own intensity never touches the asset.
        _material = sourceMaterial != null
            ? new Material(sourceMaterial)
            : null;
        if (_material != null)
        {
            _renderer.sharedMaterial = _material;
        }

        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        ApplyEngine(0f, 0f);
    }

    /// <summary>
    /// Applies an engine level (0 idle .. 1 boost). Called every frame by the
    /// ship hero with unscaled time so the menu keeps breathing even if a
    /// timescale change ever reaches this scene.
    /// </summary>
    public void ApplyEngine(float level, float time)
    {
        float target = Mathf.Clamp01(level);
        EngineLevel = target;
        float smooth = target * target * (3f - 2f * target);

        // 动态效果 关闭 freezes the flame shape as well: the plume keeps burning
        // at its current level but stops trembling, so the setting is observable
        // on the hero instead of only on the UI chrome.
        float slow;
        float fast;
        if (MainMenuSettings.MotionEnabled)
        {
            // Two incommensurate noise octaves: a slow pulse plus a fast tremor.
            slow = Mathf.PerlinNoise(time * 3.1f + _seed, 0.37f) - 0.5f;
            fast = Mathf.PerlinNoise(time * 11.7f + _seed, 4.13f) - 0.5f;
        }
        else
        {
            slow = 0f;
            fast = 0f;
        }

        float amplitude = Mathf.Lerp(0.26f, 0.13f, smooth);
        Flicker = 1f + (slow * 1.3f + fast * 0.7f) * amplitude;

        PlumeLength = Mathf.Lerp(IdleLength, BoostLength, smooth) * Flicker;
        Intensity = Mathf.Lerp(IdleIntensity, BoostIntensity, smooth) * Flicker;

        float radius = _nozzleRadius * Mathf.Lerp(IdleRadius, BoostRadius, smooth);
        float length = Mathf.Max(0.1f, PlumeLength);
        transform.localScale = new Vector3(
            radius * (1f + fast * 0.10f),
            radius * (1f - fast * 0.10f),
            length / MeshLength);

        // A degree of sideways wobble keeps the plume from looking stamped on.
        transform.localRotation = Quaternion.LookRotation(_baseLocalDirection, _baseLocalUp)
            * Quaternion.Euler(fast * 2.4f * _sideways, slow * 2.0f, 0f);

        if (_material != null)
        {
            _material.SetFloat("_Intensity", Intensity);
            _material.SetFloat("_Length", length);
        }
    }

    private void OnDestroy()
    {
        if (_material != null)
        {
            Destroy(_material);
            _material = null;
        }
    }

    /// <summary>
    /// The shared plume mesh: a flared cone along +Z with uv.y = 0 at the nozzle
    /// and uv.x spanning the circumference. Built once per process.
    /// </summary>
    private static Mesh GetSharedMesh()
    {
        if (_sharedMesh != null)
        {
            return _sharedMesh;
        }

        int rings = RingV.Length;
        var vertices = new Vector3[rings * (Segments + 1)];
        var uvs = new Vector2[vertices.Length];
        var triangles = new int[(rings - 1) * Segments * 6];

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s <= Segments; s++)
            {
                float angle = (s / (float)Segments) * Mathf.PI * 2f;
                int index = r * (Segments + 1) + s;
                vertices[index] = new Vector3(
                    Mathf.Cos(angle) * RingR[r],
                    Mathf.Sin(angle) * RingR[r],
                    RingV[r] * MeshLength);
                uvs[index] = new Vector2(s / (float)Segments, RingV[r]);
            }
        }

        int t = 0;
        for (int r = 0; r < rings - 1; r++)
        {
            for (int s = 0; s < Segments; s++)
            {
                int a = r * (Segments + 1) + s;
                int b = a + 1;
                int c = a + Segments + 1;
                int d = c + 1;
                triangles[t++] = a;
                triangles[t++] = c;
                triangles[t++] = b;
                triangles[t++] = b;
                triangles[t++] = c;
                triangles[t++] = d;
            }
        }

        _sharedMesh = new Mesh { name = "MainMenuThrusterPlume" };
        _sharedMesh.vertices = vertices;
        _sharedMesh.uv = uvs;
        _sharedMesh.triangles = triangles;
        _sharedMesh.RecalculateBounds();
        _sharedMesh.UploadMeshData(true);
        return _sharedMesh;
    }
}
