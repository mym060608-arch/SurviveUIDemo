Shader "Survive/PlanetPreviewUnlit"
{
    // Overview planet preview surface (Phase 07B.2.4).
    //
    // The preview planet is a REAL sphere mesh, but its shading is computed
    // ANALYTICALLY here instead of by scene lights. Two reasons:
    //   1. the preview rig lives in the same scene as gameplay, and a real
    //      directional light (sun) would contaminate the survey look; a real
    //      spot/point light would have to be culled away from every world
    //      object. An analytic terminator cannot leak either way;
    //   2. the surface pattern is evaluated from the OBJECT-SPACE position, so
    //      it is painted onto the mesh and rotates with it, while the
    //      terminator stays fixed relative to the camera - exactly the
    //      "scanning a rotating sphere" read the overview needs.
    //
    // The dedicated PreviewKeyLight GameObject remains the single authority for
    // the light DIRECTION: its transform drives _LightDirection every frame.
    //
    // Style: unknown frozen / resource-survey planet. Cool survey gray, charcoal
    // dark side, faint latitudinal bands, restrained ice caps, very low teal
    // rim. No continents, no clouds, no fantasy colors.
    Properties
    {
        _BaseColor ("Base (cool survey gray)", Color) = (0.52, 0.59, 0.64, 1)
        _BrightColor ("Lit highlight", Color) = (0.78, 0.83, 0.86, 1)
        _MareColor ("Dark basin / maria", Color) = (0.30, 0.35, 0.40, 1)
        _DarkColor ("Dark side (charcoal)", Color) = (0.10, 0.12, 0.14, 1)
        _IceColor ("Polar cap", Color) = (0.90, 0.93, 0.95, 1)
        _LightDirection ("Light direction (world, toward light)", Vector) = (-0.60, 0.34, 0.72, 0)
        _FillStrength ("Dark-side fill floor", Range(0, 0.6)) = 0.18
        _BandStrength ("Latitudinal bands", Range(0, 1)) = 0.45
        _NoiseStrength ("Surface variation", Range(0, 1)) = 0.55
        _MareStrength ("Dark basin coverage", Range(0, 1)) = 0.60
        _NoiseScale ("Surface variation scale", Range(0.5, 8)) = 2.6
        _RimColor ("Atmospheric rim", Color) = (0.55, 0.85, 0.88, 1)
        _RimStrength ("Rim strength", Range(0, 1)) = 0.20
        _RimPower ("Rim falloff", Range(1, 8)) = 3.4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        ZWrite On
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 objectPos : TEXCOORD0;
                float3 objectNormal : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldPos : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BrightColor;
                float4 _MareColor;
                float4 _DarkColor;
                float4 _IceColor;
                float4 _LightDirection;
                float _FillStrength;
                float _BandStrength;
                float _NoiseStrength;
                float _MareStrength;
                float _NoiseScale;
                float4 _RimColor;
                float _RimStrength;
                float _RimPower;
            CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.objectPos = v.vertex.xyz;
                o.objectNormal = v.normal;
                o.worldNormal = TransformObjectToWorldNormal(v.normal);
                o.worldPos = TransformObjectToWorld(v.vertex.xyz);
                return o;
            }

            // ---- deterministic value noise (no textures, reproducible) ----
            float Hash13(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float ValueNoise(float3 p)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash13(cell + float3(0.0, 0.0, 0.0));
                float n100 = Hash13(cell + float3(1.0, 0.0, 0.0));
                float n010 = Hash13(cell + float3(0.0, 1.0, 0.0));
                float n110 = Hash13(cell + float3(1.0, 1.0, 0.0));
                float n001 = Hash13(cell + float3(0.0, 0.0, 1.0));
                float n101 = Hash13(cell + float3(1.0, 0.0, 1.0));
                float n011 = Hash13(cell + float3(0.0, 1.0, 1.0));
                float n111 = Hash13(cell + float3(1.0, 1.0, 1.0));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            float Fbm(float3 p)
            {
                return 0.60 * ValueNoise(p)
                     + 0.28 * ValueNoise(p * 2.13 + 7.31)
                     + 0.12 * ValueNoise(p * 4.37 + 13.77);
            }

            half4 frag (v2f i) : SV_Target
            {
                float3 objectNormal = normalize(i.objectNormal);
                float3 unitPos = normalize(i.objectPos);

                // ---- surface pattern, painted in OBJECT space so it spins ----
                // Two octaves at different scales: broad basins/maria that make
                // the rotation unmistakable on the lit hemisphere, plus finer
                // survey strata and grain. Restrained cool grays only.
                float broad = Fbm(i.objectPos * _NoiseScale);
                float detail = Fbm(i.objectPos * (_NoiseScale * 3.2) + 17.0);
                float band = 0.5 + 0.5 * sin(unitPos.y * 6.4 + broad * 2.2);
                float pattern = saturate(band * 0.55 + detail * 0.45);

                float3 surface = lerp(_BaseColor.rgb, _BrightColor.rgb,
                    pattern * _BandStrength + (detail - 0.5) * _NoiseStrength);

                // Broad darker basins: the strongest readable surface feature,
                // so a slow spin is visible without any texture animation.
                float mare = smoothstep(0.47, 0.29, broad);
                surface = lerp(surface, _MareColor.rgb, mare * _MareStrength);

                // Polar caps: restrained ice at both ends of the spin axis.
                float polar = smoothstep(0.80, 0.985, abs(unitPos.y));
                surface = lerp(surface, _IceColor.rgb, polar * 0.85);

                // ---- terminator (world space, fixed relative to the camera) ----
                float3 lightDir = normalize(_LightDirection.xyz);
                float3 worldNormal = normalize(i.worldNormal);
                float lambert = saturate(dot(worldNormal, lightDir));
                float shade = _FillStrength
                              + (1.0 - _FillStrength) * pow(lambert, 0.85);

                float3 lit = surface * (0.72 + 0.42 * shade);
                float3 color = lerp(_DarkColor.rgb, lit, shade);

                // ---- very restrained atmospheric / scan rim ----
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = pow(1.0 - saturate(dot(worldNormal, viewDir)), _RimPower);
                rim *= _RimStrength * saturate(lambert * 0.75 + 0.25);
                color += _RimColor.rgb * rim;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
