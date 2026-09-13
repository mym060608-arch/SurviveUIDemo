Shader "Survive/PlanetPreviewUnlit"
{
    // Overview planet preview surface (Phase 07B.2.4; stylized in Phase 09B PASS 7).
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
    //      "turning a stylized planet" read the overview needs.
    //
    // The dedicated PreviewKeyLight GameObject remains the single authority for
    // the light DIRECTION: its transform drives _LightDirection every frame.
    //
    // Style (Phase 09B PASS 7): a SIMPLIFIED, graphic planet, not a NASA photo.
    // Large flat colour fields with a hard-ish coastline, two soft terminator
    // steps, no grain, no clouds, no crater detail. Palette borrowed from the
    // user's colour reference (deep gray-blue / cool gray / low-saturation
    // cyan, with a few very small warm brown-gray patches) - colours only, no
    // figure, no silhouette, no character artwork of any kind.
    Properties
    {
        _BaseColor ("Light surface", Color) = (0.94, 0.96, 0.97, 1)
        _MidColor ("Cool gray plains", Color) = (0.80, 0.85, 0.88, 1)
        _LandColor ("Gray-blue landmass", Color) = (0.30, 0.37, 0.46, 1)
        _LandDeepColor ("Landmass core", Color) = (0.19, 0.25, 0.32, 1)
        _ShallowColor ("Coastal cyan", Color) = (0.50, 0.72, 0.76, 1)
        _WarmColor ("Warm accent", Color) = (0.62, 0.54, 0.46, 1)
        _IceColor ("Polar cap", Color) = (0.96, 0.98, 0.99, 1)
        _NightColor ("Dark side", Color) = (0.34, 0.39, 0.45, 1)
        _RimColor ("Atmospheric rim", Color) = (0.45, 0.78, 0.82, 1)
        _LightDirection ("Light direction (world, toward light)", Vector) = (-0.60, 0.34, 0.72, 0)
        _LandCoverage ("Land coverage threshold", Range(0.30, 0.70)) = 0.58
        _LandSoftness ("Coastline softness", Range(0.01, 0.15)) = 0.040
        _LandScale ("Landmass scale", Range(0.6, 4)) = 1.55
        _BlockStrength ("Large-block variation", Range(0, 1)) = 0.45
        _WarmCoverage ("Warm patch threshold", Range(0.60, 0.95)) = 0.88
        _WarmStrength ("Warm patch strength", Range(0, 1)) = 0.40
        _FillStrength ("Dark-side fill floor", Range(0, 0.6)) = 0.24
        _RimStrength ("Rim strength", Range(0, 1)) = 0.16
        _RimPower ("Rim falloff", Range(1, 8)) = 3.2
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
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _MidColor;
                float4 _LandColor;
                float4 _LandDeepColor;
                float4 _ShallowColor;
                float4 _WarmColor;
                float4 _IceColor;
                float4 _NightColor;
                float4 _RimColor;
                float4 _LightDirection;
                float _LandCoverage;
                float _LandSoftness;
                float _LandScale;
                float _BlockStrength;
                float _WarmCoverage;
                float _WarmStrength;
                float _FillStrength;
                float _RimStrength;
                float _RimPower;
            CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.objectPos = v.vertex.xyz;
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

            // Two octaves only: broad continents, no fine terrain detail.
            float Landmass(float3 p)
            {
                return 0.72 * ValueNoise(p) + 0.28 * ValueNoise(p * 2.07 + 5.13);
            }

            half4 frag (v2f i) : SV_Target
            {
                float3 unitPos = normalize(i.objectPos);

                // ---- large colour fields, painted in OBJECT space so they spin ----
                float field = Landmass(unitPos * _LandScale);

                // Hard-ish coastline: the graphic edge a stylized planet needs.
                float land = smoothstep(_LandCoverage,
                    _LandCoverage + _LandSoftness, field);
                float coast = smoothstep(_LandCoverage - _LandSoftness * 2.0,
                    _LandCoverage, field) * (1.0 - land);

                // One very low frequency tone per side: big blocks, no grain.
                float blockSea = ValueNoise(unitPos * (_LandScale * 0.55) + 3.7);
                float blockLand = ValueNoise(unitPos * (_LandScale * 0.75) + 11.3);

                float3 sea = lerp(_BaseColor.rgb, _MidColor.rgb,
                    smoothstep(0.42, 0.62, blockSea) * _BlockStrength);
                float3 rock = lerp(_LandColor.rgb, _LandDeepColor.rgb,
                    smoothstep(0.40, 0.64, blockLand));

                float3 surface = lerp(sea, rock, land);

                // A thin low-saturation cyan band where land meets water.
                surface = lerp(surface, _ShallowColor.rgb, coast * 0.55);

                // A few small warm brown-gray patches (colour reference only;
                // never more than a small fraction of the surface).
                float warm = smoothstep(_WarmCoverage, _WarmCoverage + 0.05,
                    ValueNoise(unitPos * (_LandScale * 0.9) + 21.0));
                surface = lerp(surface, _WarmColor.rgb, warm * _WarmStrength * land);

                // Polar caps stay light: they read as the top / bottom of the
                // spin axis while the planet turns.
                float polar = smoothstep(0.80, 0.98, abs(unitPos.y));
                surface = lerp(surface, _IceColor.rgb, polar);

                // ---- two-step terminator (world space, fixed to the camera) ----
                float3 lightDir = normalize(_LightDirection.xyz);
                float3 worldNormal = normalize(i.worldNormal);
                float lambert = saturate(dot(worldNormal, lightDir));
                float toon = saturate(smoothstep(-0.02, 0.32, lambert) * 0.55
                                      + smoothstep(0.32, 0.82, lambert) * 0.45);
                float shade = _FillStrength + (1.0 - _FillStrength) * toon;

                float3 lit = surface * (0.90 + 0.22 * shade);
                float3 color = lerp(_NightColor.rgb, lit, shade);

                // ---- restrained cyan scan / atmosphere rim ----
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
