using UnityEngine;

/// <summary>
/// Deep-space backdrop for the title screen (Phase 08, Revision 2).
///
/// Three additive quads parented to the stage camera at very different
/// distances build the "deep space" read: a far star field with faint nebula, a
/// mid field and a sparse near field that provides the parallax. Everything is
/// generated procedurally from a fixed seed, ONCE, so the menu never depends on
/// an imported texture and every run composes identically.
///
/// Motion (Revision 2): the plates drift sideways forever in the shader
/// (<c>_ScrollSpeed</c>, no per-frame texture work) and each layer twinkles with
/// its own decorrelated noise octaves, so the sky feels alive without flashing.
/// The ship cruises to the RIGHT, so the stars flow LEFT past it - that relative
/// motion is what sells the cruise while the ship holds its composition.
///
/// The layers are camera children and keep their authored distance, so the
/// camera never flies out of the backdrop. Depth is real: the quads are ordinary
/// transparent geometry and the hero ship occludes them normally.
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
        public Vector2 Drift;
    }

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int TwinkleMapId = Shader.PropertyToID("_TwinkleMap");
    private static readonly int ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");
    private static readonly int TwinkleAmountId = Shader.PropertyToID("_TwinkleAmount");
    private static readonly int TwinkleScaleId = Shader.PropertyToID("_TwinkleScale");
    private static readonly int TwinkleSpeedId = Shader.PropertyToID("_TwinkleSpeed");
    private static readonly int SpikeAmountId = Shader.PropertyToID("_SpikeAmount");

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

    /// <summary>Per-layer sideways drift in uv units per second (verification).</summary>
    public float ScrollSpeedOf(int layer)
    {
        return layer >= 0 && layer < _layers.Length && _layers[layer]?.Material != null
            ? _layers[layer].Material.GetFloat(ScrollSpeedId)
            : 0f;
    }

    /// <summary>Per-layer twinkle strength (verification).</summary>
    public float TwinkleAmountOf(int layer)
    {
        return layer >= 0 && layer < _layers.Length && _layers[layer]?.Material != null
            ? _layers[layer].Material.GetFloat(TwinkleAmountId)
            : 0f;
    }

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

        // One shared twinkle noise plate: the shader samples it in two
        // decorrelated octaves, so no per-star CPU work is needed.
        Texture2D twinkle = BuildTwinkleNoise(128, seed ^ 0x5f3a);

        // Far: the main field + the nebula wash that makes the sky read as deep
        // space instead of "black with dots". Slowest drift and the calmest
        // twinkle.
        AddLayer(0, 900f, 0.05f, 1f, 1024, 512, 1900, 1.05f, 0.60f,
            nebula: true, random: random, twinkle: twinkle,
            scrollSpeed: 0.0035f, twinkleAmount: 0.28f, twinkleScale: 2.2f,
            twinkleSpeed: 0.22f, spikeAmount: 0.30f,
            parallaxDrift: new Vector2(0.35f, 0.22f));
        // Mid: brighter and slightly larger stars.
        AddLayer(1, 560f, 0.18f, 0.85f, 768, 384, 620, 1.5f, 0.85f,
            nebula: false, random: random, twinkle: twinkle,
            scrollSpeed: 0.008f, twinkleAmount: 0.38f, twinkleScale: 3.4f,
            twinkleSpeed: 0.3f, spikeAmount: 0.45f,
            parallaxDrift: new Vector2(1.1f, 0.7f));
        // Near: few, large stars - these carry both the parallax and the most
        // visible twinkle.
        AddLayer(2, 300f, 0.42f, 0.65f, 640, 320, 170, 2.4f, 0.9f,
            nebula: false, random: random, twinkle: twinkle,
            scrollSpeed: 0.017f, twinkleAmount: 0.5f, twinkleScale: 4.6f,
            twinkleSpeed: 0.38f, spikeAmount: 0.6f,
            parallaxDrift: new Vector2(3.2f, 2.0f));
        LayerCount = 3;
    }

    private void AddLayer(int index, float distance, float parallax, float intensity,
        int width, int height, int stars, float starSize, float brightness,
        bool nebula, System.Random random, Texture2D twinkle,
        float scrollSpeed, float twinkleAmount, float twinkleScale,
        float twinkleSpeed, float spikeAmount, Vector2 parallaxDrift)
    {
        var go = new GameObject("StarLayer_" + index);
        go.transform.SetParent(stageCamera.transform, false);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localPosition = new Vector3(0f, 0f, distance);

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = _quad;

        var renderer = go.AddComponent<MeshRenderer>();
        var material = new Material(spriteMaterial) { name = "M_StarLayer_" + index };
        material.SetTexture(BaseMapId, BuildTexture(width, height, stars, starSize,
            brightness, nebula, random, out int drawn));
        material.SetTexture(TwinkleMapId, twinkle);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat(IntensityId, intensity);
        material.SetFloat(ScrollSpeedId, scrollSpeed);
        material.SetFloat(TwinkleAmountId, twinkleAmount);
        material.SetFloat(TwinkleScaleId, twinkleScale);
        material.SetFloat(TwinkleSpeedId, twinkleSpeed);
        material.SetFloat(SpikeAmountId, spikeAmount);
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
            Drift = parallaxDrift
        };
    }

    private void Update()
    {
        if (!IsActive || stageCamera == null)
        {
            return;
        }

        // The drift and the twinkle live in the shader, so the only per-frame
        // work left is the aspect refit and the cinematic parallax nudge.
        if (!Mathf.Approximately(_lastAspect, stageCamera.aspect))
        {
            FitLayers();
        }

        if (_parallaxBoost <= 0.0001f)
        {
            return;
        }

        for (int i = 0; i < _layers.Length; i++)
        {
            Layer layer = _layers[i];
            if (layer?.Transform == null)
            {
                continue;
            }

            layer.Transform.localPosition = new Vector3(
                _parallaxBoost * layer.Parallax * 24f, 0f, layer.Distance);
        }
    }

    /// <summary>
    /// Nudges the whole backdrop sideways (a fraction of each layer's parallax
    /// factor). The cinematic raises this while the ship accelerates away, so the
    /// star field answers the departure instead of sitting still.
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
            if (layer?.Transform == null)
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
    /// Deterministic seamless twinkle noise: a wrapped value-noise lattice so the
    /// drifting plate never shows a seam where the noise repeats.
    /// </summary>
    private static Texture2D BuildTwinkleNoise(int size, int randomSeed)
    {
        var random = new System.Random(randomSeed);
        const int lattice = 16;
        var values = new float[lattice * lattice];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (float)random.NextDouble();
        }

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = y / (float)size * lattice;
            int y0 = (int)v % lattice;
            int y1 = (y0 + 1) % lattice;
            float fy = Smooth(v - Mathf.Floor(v));
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size * lattice;
                int x0 = (int)u % lattice;
                int x1 = (x0 + 1) % lattice;
                float fx = Smooth(u - Mathf.Floor(u));

                float a = values[y0 * lattice + x0];
                float b = values[y0 * lattice + x1];
                float c = values[y1 * lattice + x0];
                float d = values[y1 * lattice + x1];
                float value = Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);

                byte grey = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
                pixels[y * size + x] = new Color32(grey, grey, grey, 255);
            }
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
        {
            name = "MainMenuTwinkleNoise",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private static float Smooth(float t)
    {
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Deterministic deep-space plate: soft nebula wash (far layer only) plus
    /// point stars. rgb carries the star colour and a carries coverage, which is
    /// exactly what the additive sprite shader consumes. The plate wraps
    /// seamlessly because the shader scrolls it forever.
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
            wrapMode = TextureWrapMode.Repeat,
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
        Texture2D twinkle = null;
        for (int i = 0; i < _layers.Length; i++)
        {
            if (_layers[i]?.Material == null)
            {
                continue;
            }

            if (_layers[i].Material.HasProperty(BaseMapId))
            {
                Destroy(_layers[i].Material.GetTexture(BaseMapId));
            }

            if (twinkle == null && _layers[i].Material.HasProperty(TwinkleMapId))
            {
                twinkle = _layers[i].Material.GetTexture(TwinkleMapId) as Texture2D;
            }

            Destroy(_layers[i].Material);
        }

        if (twinkle != null)
        {
            Destroy(twinkle);
        }

        if (_quad != null)
        {
            Destroy(_quad);
            _quad = null;
        }
    }
}
