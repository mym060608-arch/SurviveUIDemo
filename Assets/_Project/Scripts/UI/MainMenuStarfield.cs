using UnityEngine;

/// <summary>
/// Phase 08 Revision deep-space backdrop for the Main Menu.
///
/// Three additive quads parented to the stage camera at very different
/// distances (and drift rates) build the "deep space" read: a far star field
/// with faint nebula, a mid field and a sparse near field that provides the
/// parallax. Everything is generated procedurally from a fixed seed, so the
/// menu never depends on an imported texture and every run composes identically.
///
/// The layers are camera children and keep their authored distance; the camera
/// therefore never flies out of the backdrop. Depth is real - the quads are
/// ordinary transparent geometry, so the hero ship occludes them normally.
/// </summary>
public class MainMenuStarfield : MonoBehaviour
{
    [SerializeField]
    private Camera stageCamera;

    [SerializeField]
    private int seed = 20260801;

    [SerializeField]
    private Material spriteMaterial;

    private sealed class Layer
    {
        public Transform Transform;
        public Material Material;
        public MeshRenderer Renderer;
        public float Distance;
        public float Parallax;
        public float Intensity;
        public float TwinklePhase;
        public Vector2 Drift;
    }

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    private readonly Layer[] _layers = new Layer[3];
    private Mesh _quad;
    private float _lastAspect = -1f;
    private float _parallaxBoost;

    /// <summary>Number of starfield layers actually built (verification).</summary>
    public int LayerCount { get; private set; }

    /// <summary>True once the backdrop is built and visible.</summary>
    public bool IsActive => LayerCount > 0;

    /// <summary>Total stars baked into the three layers (verification).</summary>
    public int StarCount { get; private set; }

    /// <summary>Runtime wiring (called by the scene builder).</summary>
    public void Configure(Camera camera, Material material, int randomSeed)
    {
        stageCamera = camera;
        spriteMaterial = material;
        seed = randomSeed;
    }

    private void Start()
    {
        if (stageCamera == null)
        {
            stageCamera = GetComponentInParent<Camera>();
        }

        if (stageCamera == null)
        {
            stageCamera = Object.FindAnyObjectByType<Camera>();
        }

        if (stageCamera == null || spriteMaterial == null)
        {
            Debug.LogError("[MainMenuStarfield] camera or sprite material missing");
            return;
        }

        BuildLayers();
        FitLayers();
        Debug.Log("[MainMenuStarfield] backdrop built layers=" + LayerCount
                  + " stars=" + StarCount);
    }

    private void BuildLayers()
    {
        _quad = BuildQuad();
        var random = new System.Random(seed);

        // Far: the main field + the nebula wash that makes the sky read as deep
        // space instead of "black with dots".
        AddLayer(0, 900f, 0.05f, 1f, 1024, 512, 1900, 1.05f, 0.60f,
            nebula: true, random: random, drift: new Vector2(0.35f, 0.22f));
        // Mid: brighter and slightly larger stars.
        AddLayer(1, 560f, 0.18f, 0.85f, 768, 384, 620, 1.5f, 0.85f,
            nebula: false, random: random, drift: new Vector2(1.1f, 0.7f));
        // Near: few, large, faint - these sell the parallax.
        AddLayer(2, 300f, 0.42f, 0.65f, 640, 320, 170, 2.4f, 0.9f,
            nebula: false, random: random, drift: new Vector2(3.2f, 2.0f));
        LayerCount = 3;
    }

    private void AddLayer(int index, float distance, float parallax, float intensity,
        int width, int height, int stars, float starSize, float brightness,
        bool nebula, System.Random random, Vector2 drift)
    {
        var go = new GameObject("StarLayer_" + index);
        go.transform.SetParent(stageCamera.transform, false);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localPosition = new Vector3(0f, 0f, distance);

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = _quad;

        var renderer = go.AddComponent<MeshRenderer>();
        var material = new Material(spriteMaterial) { name = "M_StarLayer_" + index };
        material.SetTexture("_BaseMap", BuildTexture(width, height, stars, starSize,
            brightness, nebula, random, out int drawn));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat(IntensityId, intensity);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        StarCount += drawn;
        _layers[index] = new Layer
        {
            Transform = go.transform,
            Material = material,
            Renderer = renderer,
            Distance = distance,
            Parallax = parallax,
            Intensity = intensity,
            TwinklePhase = (float)random.NextDouble() * 6.283f,
            Drift = drift
        };
    }

    private void Update()
    {
        if (!IsActive || stageCamera == null)
        {
            return;
        }

        if (!Mathf.Approximately(_lastAspect, stageCamera.aspect))
        {
            FitLayers();
        }

        bool motion = MainMenuSettings.MotionEnabled;
        float time = Time.unscaledTime;

        for (int i = 0; i < _layers.Length; i++)
        {
            Layer layer = _layers[i];
            if (layer == null || layer.Transform == null)
            {
                continue;
            }

            float driftX = 0f;
            float driftY = 0f;
            if (motion)
            {
                driftX = Mathf.Sin(time * 0.037f + i) * layer.Drift.x + time * 0.02f * i;
                driftY = Mathf.Cos(time * 0.029f + i * 1.7f) * layer.Drift.y;
            }

            driftX += _parallaxBoost * layer.Parallax * 24f;
            layer.Transform.localPosition = new Vector3(driftX, driftY, layer.Distance);

            float twinkle = motion
                ? 1f + Mathf.Sin(time * 0.55f + layer.TwinklePhase) * 0.05f
                : 1f;
            if (layer.Material != null)
            {
                layer.Material.SetFloat(IntensityId, layer.Intensity * twinkle);
            }
        }
    }

    /// <summary>
    /// Nudges the whole backdrop sideways (a fraction of each layer's parallax
    /// factor). The cinematic raises this while the ship accelerates away, so
    /// the star field answers the departure instead of sitting still.
    /// </summary>
    public void SetParallaxBoost(float amount)
    {
        _parallaxBoost = amount;
    }

    private void FitLayers()
    {
        if (stageCamera == null)
        {
            return;
        }

        _lastAspect = stageCamera.aspect;
        float halfFov = stageCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        for (int i = 0; i < _layers.Length; i++)
        {
            Layer layer = _layers[i];
            if (layer == null || layer.Transform == null)
            {
                continue;
            }

            // 1.3x margin: the quad must still cover the frustum at ultrawide
            // aspects and while the camera drifts.
            float height = 2f * layer.Distance * Mathf.Tan(halfFov) * 1.3f;
            float width = height * Mathf.Max(1f, _lastAspect);
            layer.Transform.localScale = new Vector3(width, height, 1f);
        }
    }

    private static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "MainMenuStarQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        return mesh;
    }

    /// <summary>
    /// Deterministic deep-space plate: soft nebula wash (far layer only) plus
    /// point stars. rgb carries the star colour and a carries coverage, which is
    /// exactly what the additive sprite shader consumes.
    /// </summary>
    private static Texture2D BuildTexture(int width, int height, int starCount,
        float starSize, float brightness, bool nebula, System.Random random,
        out int drawn)
    {
        var pixels = new Color32[width * height];

        if (nebula)
        {
            AddNebula(pixels, width, height, random);
        }

        drawn = 0;
        for (int i = 0; i < starCount; i++)
        {
            int x = random.Next(width);
            int y = random.Next(height);
            float magnitude = 0.30f + (float)random.NextDouble() * 0.70f;
            float size = starSize * (0.7f + (float)random.NextDouble() * 0.9f);
            Color colour = StarColour(random);
            AddStar(pixels, width, height, x, y, size, magnitude * brightness, colour);
            drawn++;
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, false)
        {
            name = "MainMenuStarPlate",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private static void AddStar(Color32[] pixels, int width, int height, int cx, int cy,
        float size, float magnitude, Color colour)
    {
        int radius = Mathf.Max(1, Mathf.CeilToInt(size * 2f));
        float sigma = Mathf.Max(0.35f, size * 0.5f);
        float twoSigmaSq = 2f * sigma * sigma;
        for (int dy = -radius; dy <= radius; dy++)
        {
            int y = cy + dy;
            if (y < 0 || y >= height)
            {
                continue;
            }

            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = cx + dx;
                if (x < 0)
                {
                    x += width;
                }
                else if (x >= width)
                {
                    x -= width;
                }

                float distanceSq = dx * dx + dy * dy;
                float falloff = Mathf.Exp(-distanceSq / twoSigmaSq);
                float value = magnitude * falloff;
                if (value <= 0.004f)
                {
                    continue;
                }

                int index = y * width + x;
                Color32 existing = pixels[index];
                pixels[index] = new Color32(
                    (byte)Mathf.Min(255f, existing.r + colour.r * value * 255f),
                    (byte)Mathf.Min(255f, existing.g + colour.g * value * 255f),
                    (byte)Mathf.Min(255f, existing.b + colour.b * value * 255f),
                    (byte)Mathf.Min(255f, existing.a + value * 255f));
            }
        }
    }

    private static Color StarColour(System.Random random)
    {
        double roll = random.NextDouble();
        if (roll < 0.68)
        {
            return new Color(1f, 1f, 1f);
        }

        if (roll < 0.86)
        {
            return new Color(0.72f, 0.82f, 1f);
        }

        if (roll < 0.95)
        {
            return new Color(1f, 0.88f, 0.72f);
        }

        return new Color(0.85f, 0.78f, 1f);
    }

    /// <summary>
    /// Faint nebula wash from a coarse bilinear lattice, tinted towards the
    /// project's deep blue so the backdrop never looks like a grey UI canvas.
    /// </summary>
    private static void AddNebula(Color32[] pixels, int width, int height,
        System.Random random)
    {
        const int latticeX = 22;
        const int latticeY = 12;
        var lattice = new float[(latticeX + 1) * (latticeY + 1)];
        for (int i = 0; i < lattice.Length; i++)
        {
            lattice[i] = (float)random.NextDouble();
        }

        for (int y = 0; y < height; y++)
        {
            float v = y / (float)(height - 1) * latticeY;
            int y0 = Mathf.Clamp((int)v, 0, latticeY - 1);
            float fy = v - y0;
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)(width - 1) * latticeX;
                int x0 = Mathf.Clamp((int)u, 0, latticeX - 1);
                float fx = u - x0;

                float a = lattice[y0 * (latticeX + 1) + x0];
                float b = lattice[y0 * (latticeX + 1) + x0 + 1];
                float c = lattice[(y0 + 1) * (latticeX + 1) + x0];
                float d = lattice[(y0 + 1) * (latticeX + 1) + x0 + 1];
                float value = Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);

                // Soft threshold: most of the sky stays black, the ridges glow.
                value = Mathf.Clamp01((value - 0.55f) * 2.1f);
                value *= value;

                // Vertical falloff keeps the upper-left quadrant the brightest.
                float gradient = Mathf.Clamp01(1f - y / (float)height);
                float amount = value * gradient * 0.34f;
                if (amount <= 0.002f)
                {
                    continue;
                }

                pixels[y * width + x] = new Color32(
                    (byte)(42f * amount * 2.2f),
                    (byte)(70f * amount * 2.2f),
                    (byte)(140f * amount * 2.2f),
                    (byte)Mathf.Min(255f, amount * 255f));
            }
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _layers.Length; i++)
        {
            if (_layers[i]?.Material != null)
            {
                if (_layers[i].Material.HasProperty("_BaseMap"))
                {
                    Destroy(_layers[i].Material.GetTexture("_BaseMap"));
                }

                Destroy(_layers[i].Material);
            }
        }

        if (_quad != null)
        {
            Destroy(_quad);
            _quad = null;
        }
    }
}
