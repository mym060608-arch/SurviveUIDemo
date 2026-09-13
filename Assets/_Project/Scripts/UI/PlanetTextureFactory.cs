using UnityEngine;

/// <summary>
/// Deterministic procedural planet disc for the Overview main visual - the 2D
/// FALLBACK used only when the shared 3D preview rig is unavailable (the legacy
/// Hub in a player build; every other host renders the real sphere).
///
/// Stylized in Phase 09B PASS 7 to match the 3D preview exactly: large flat
/// colour fields with a hard-ish coastline, broad deep gray-blue landmasses on
/// a light cool-gray surface, a thin low-saturation cyan coastal band, a few
/// warm brown-gray patches, two soft terminator steps and a restrained cyan rim.
/// No craters, no grain, no NASA-style terrain detail. Pure C# (no textures, no
/// generated art), fixed seed => identical across runs, which keeps screenshots
/// reproducible. Texture is generated once and cached.
/// </summary>
public static class PlanetTextureFactory
{
    private const int Size = 512;
    private const float Radius = 236f;
    private const float LandCoverage = 0.58f;
    private const float LandSoftness = 0.040f;
    private const float LandScale = 1.55f;
    private const float FillStrength = 0.24f;

    // Palette (the same values the preview material authors, sRGB picker space).
    private static readonly Color LightSurface = new Color(0.94f, 0.96f, 0.97f);
    private static readonly Color CoolGray = new Color(0.80f, 0.85f, 0.88f);
    private static readonly Color LandColor = new Color(0.30f, 0.37f, 0.46f);
    private static readonly Color LandDeep = new Color(0.19f, 0.25f, 0.32f);
    private static readonly Color CoastalCyan = new Color(0.50f, 0.72f, 0.76f);
    private static readonly Color WarmAccent = new Color(0.62f, 0.54f, 0.46f);
    private static readonly Color PolarIce = new Color(0.96f, 0.98f, 0.99f);
    private static readonly Color NightSide = new Color(0.34f, 0.39f, 0.45f);
    private static readonly Color RimCyan = new Color(0.45f, 0.78f, 0.82f);

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

        var pixels = new Color32[Size * Size];
        Color32 transparent = new Color32(0, 0, 0, 0);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int index = y * Size + x;
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha = Mathf.Clamp01(Radius + 0.9f - dist);
                if (alpha <= 0f)
                {
                    pixels[index] = transparent;
                    continue;
                }

                float horizon = Mathf.Sqrt(Mathf.Max(0f, Radius * Radius - dist * dist));
                var normal = new Vector3(dx, dy, horizon) / Radius;
                Vector3 unit = normal.normalized;

                // ---- large colour fields (object space == view space here) ----
                float field = Landmass(unit * LandScale);
                float land = Mathf.SmoothStep(LandCoverage,
                    LandCoverage + LandSoftness, field);
                float coast = Mathf.SmoothStep(LandCoverage - LandSoftness * 2f,
                    LandCoverage, field) * (1f - land);

                float blockSea = ValueNoise(unit * (LandScale * 0.55f) + new Vector3(3.7f, 3.7f, 3.7f));
                float blockLand = ValueNoise(unit * (LandScale * 0.75f) + new Vector3(11.3f, 11.3f, 11.3f));

                Color sea = Color.Lerp(LightSurface, CoolGray,
                    Mathf.SmoothStep(0.42f, 0.62f, blockSea) * 0.45f);
                Color rock = Color.Lerp(LandColor, LandDeep,
                    Mathf.SmoothStep(0.40f, 0.64f, blockLand));

                Color surface = Color.Lerp(sea, rock, land);
                surface = Color.Lerp(surface, CoastalCyan, coast * 0.55f);

                float warm = Mathf.SmoothStep(0.88f, 0.93f,
                    ValueNoise(unit * (LandScale * 0.9f) + new Vector3(21f, 21f, 21f)));
                surface = Color.Lerp(surface, WarmAccent, warm * 0.4f * land);

                float polar = Mathf.SmoothStep(0.80f, 0.98f, Mathf.Abs(unit.y));
                surface = Color.Lerp(surface, PolarIce, polar);

                // ---- two-step terminator (single light, upper-left-front) ----
                float lambert = Mathf.Max(0f, Vector3.Dot(normal, light));
                float toon = Mathf.Clamp01(
                    Mathf.SmoothStep(-0.02f, 0.32f, lambert) * 0.55f
                    + Mathf.SmoothStep(0.32f, 0.82f, lambert) * 0.45f);
                float shade = FillStrength + (1f - FillStrength) * toon;

                Color lit = surface * (0.90f + 0.22f * shade);
                Color color = Color.Lerp(NightSide, lit, shade);

                // ---- restrained cyan rim just inside the silhouette ----
                float rim = Mathf.Pow(Mathf.Clamp01(dist / Radius), 5.5f);
                rim *= 0.35f * Mathf.Clamp01(lambert * 0.75f + 0.25f);
                color += RimCyan * rim;

                pixels[index] = new Color(
                    Mathf.Clamp01(color.r),
                    Mathf.Clamp01(color.g),
                    Mathf.Clamp01(color.b),
                    alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    // ---- deterministic value noise (mirrors the preview shader) ----

    private static float Hash13(Vector3 p)
    {
        p = new Vector3(
            Mathf.Repeat(p.x * 0.3183099f + 0.1f, 1f),
            Mathf.Repeat(p.y * 0.3183099f + 0.1f, 1f),
            Mathf.Repeat(p.z * 0.3183099f + 0.1f, 1f));
        p *= 17f;
        return Mathf.Repeat(p.x * p.y * p.z * (p.x + p.y + p.z), 1f);
    }

    private static float ValueNoise(Vector3 p)
    {
        var cell = new Vector3(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z));
        float fx = Smooth(p.x - cell.x);
        float fy = Smooth(p.y - cell.y);
        float fz = Smooth(p.z - cell.z);

        float n000 = Hash13(cell);
        float n100 = Hash13(cell + new Vector3(1f, 0f, 0f));
        float n010 = Hash13(cell + new Vector3(0f, 1f, 0f));
        float n110 = Hash13(cell + new Vector3(1f, 1f, 0f));
        float n001 = Hash13(cell + new Vector3(0f, 0f, 1f));
        float n101 = Hash13(cell + new Vector3(1f, 0f, 1f));
        float n011 = Hash13(cell + new Vector3(0f, 1f, 1f));
        float n111 = Hash13(cell + new Vector3(1f, 1f, 1f));

        float nx00 = Mathf.Lerp(n000, n100, fx);
        float nx10 = Mathf.Lerp(n010, n110, fx);
        float nx01 = Mathf.Lerp(n001, n101, fx);
        float nx11 = Mathf.Lerp(n011, n111, fx);
        return Mathf.Lerp(Mathf.Lerp(nx00, nx10, fy), Mathf.Lerp(nx01, nx11, fy), fz);
    }

    private static float Smooth(float t)
    {
        return t * t * (3f - 2f * t);
    }

    /// <summary>Two octaves only: broad continents, no fine terrain detail.</summary>
    private static float Landmass(Vector3 p)
    {
        return 0.72f * ValueNoise(p) + 0.28f * ValueNoise(p * 2.07f + new Vector3(5.13f, 5.13f, 5.13f));
    }
}
