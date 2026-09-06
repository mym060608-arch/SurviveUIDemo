using UnityEngine;

/// <summary>
/// Deterministic procedural planet disc for the Overview main visual.
///
/// Pure C# (no textures, no AI assets): an icy survey-candidate sphere with
/// banded surface noise, soft terminator lighting from the upper-left, a few
/// subtle craters and polar caps. Fixed seed => identical across runs, which
/// keeps screenshots reproducible. Texture is generated once and cached.
/// </summary>
public static class PlanetTextureFactory
{
    private const int Size = 512;
    private const float Radius = 236f;
    private const int CraterCount = 20;
    private const int CraterSeed = 7712;

    private static Texture2D _cached;

    /// <summary>Returns the shared planet texture (created on first call).</summary>
    public static Texture2D Create()
    {
        if (_cached == null)
        {
            _cached = Build();
        }

        return _cached;
    }

    private static Texture2D Build()
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "OverviewPlanet_Procedural"
        };

        float center = (Size - 1) * 0.5f;
        var light = new Vector3(-0.60f, 0.34f, 0.72f).normalized;

        // Deterministic crater field.
        var random = new System.Random(CraterSeed);
        var craters = new Crater[CraterCount];
        for (int i = 0; i < CraterCount; i++)
        {
            float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
            float reach = 0.72f * Radius * Mathf.Sqrt((float)random.NextDouble());
            float cx = center + Mathf.Cos(angle) * reach;
            float cy = center + Mathf.Sin(angle) * reach;
            float radius = 5f + 11f * (float)random.NextDouble();
            craters[i] = new Crater(cx, cy, radius);
        }

        var pixels = new Color32[Size * Size];
        Color32 transparent = new Color32(0, 0, 0, 0);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int index = y * Size + x;
                float dx = x - center;
                float dy = y - center;
                float dist2 = dx * dx + dy * dy;
                float dist = Mathf.Sqrt(dist2);

                // Soft edge: fully transparent outside the silhouette.
                float alpha = Mathf.Clamp01(Radius + 0.9f - dist);
                if (alpha <= 0f)
                {
                    pixels[index] = transparent;
                    continue;
                }

                float horizon = Mathf.Sqrt(Mathf.Max(0f, Radius * Radius - dist2));
                var normal = new Vector3(dx, dy, horizon) / Radius;

                // Latitude band pattern (vertical bands read as survey strata).
                float v = dy / Radius;
                float band = 0.5f + 0.5f * Mathf.Sin(v * Mathf.PI * 6.4f + 0.7f);
                float fine = 0.5f + 0.5f * Mathf.Sin(v * 53.7f + dx * 0.008f);
                band = Mathf.Clamp01(band * 0.86f + fine * 0.14f);

                Color baseA = new Color(0.56f, 0.63f, 0.68f); // cool survey gray
                Color baseB = new Color(0.68f, 0.73f, 0.77f);
                Color ice = new Color(0.90f, 0.93f, 0.95f);

                Color surface = Color.Lerp(baseA, baseB, band);

                // Terminator shading (single light source, upper-left).
                float lit = Mathf.Max(0f, Vector3.Dot(normal, light));
                float shade = 0.42f + 0.58f * lit;

                // Craters: dark pit with a faint bright inner rim.
                for (int c = 0; c < craters.Length; c++)
                {
                    Crater crater = craters[c];
                    float d2 = (dx - crater.X) * (dx - crater.X) + (dy - crater.Y) * (dy - crater.Y);
                    float rr = crater.Radius;
                    if (d2 < rr * rr)
                    {
                        float depth = 1f - Mathf.Sqrt(d2) / rr;
                        shade *= Mathf.Lerp(0.86f, 0.97f, depth);
                    }
                    else if (d2 < rr * rr * 1.55f)
                    {
                        shade *= 1.05f;
                    }
                }

                // Polar caps.
                float cap = Mathf.SmoothStep(0.78f, 0.97f, Mathf.Abs(v));
                surface = Color.Lerp(surface, ice, cap * 0.9f);

                // Slight limb darkening keeps the disc readable against canvas.
                float limb = Mathf.SmoothStep(0.80f, 0.995f, dist / Radius);
                shade *= Mathf.Lerp(1f, 0.88f, limb);

                Color color = surface * shade;
                pixels[index] = new Color(color.r, color.g, color.b, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private readonly struct Crater
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Radius;

        public Crater(float x, float y, float radius)
        {
            X = x;
            Y = y;
            Radius = radius;
        }
    }
}
