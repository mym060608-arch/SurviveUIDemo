Shader "Survive/WristDisplayUnlit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)
        // Frosted hologram lens (Phase 09A final): inside the map disc the sheet
        // lets the physical wrist behind it show through as soft colour while the
        // readable UI stays on top and sharp.
        _Opacity ("Sheet Opacity", Range(0, 1)) = 1
        // Phase 09B PASS 3: the PERIPHERAL structure (the four module plates and
        // the status arcs, everything outside the map disc) is a translucent
        // industrial sheet, so the world keeps showing through the instrument
        // instead of the panels reading as opaque cards. 1 = untouched.
        _StructureOpacity ("Structure Opacity (outside the disc)", Range(0, 1)) = 1
        _FrostOpacity ("Map Disc Opacity", Range(0, 1)) = 1
        _MapCentre ("Map Centre (UV)", Vector) = (0.5, 0.5, 0, 0)
        _MapRadius ("Map Radius (UV)", Vector) = (0.18359, 0.29375, 0, 0)
        _FrostSoftness ("Frost Edge Softness", Range(0.001, 0.5)) = 0.08
        _FrostTint ("Frost Milky Tint", Range(0, 1)) = 0
        _FrostGrain ("Frost Grain", Range(0, 0.2)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _MapCentre;
                float4 _MapRadius;
                float _Opacity;
                float _StructureOpacity;
                float _FrostOpacity;
                float _FrostSoftness;
                float _FrostTint;
                float _FrostGrain;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                // _BaseColor defaults to white, so the wrist hologram quad is
                // unaffected; the frosted block-arm set uses it as its flat
                // frosted-glass colour.
                half4 colour = sampled * _BaseColor;

                // Frosted lens mask: 1 inside the central map disc, fading out at
                // its rim. It only ever affects the SHEET, never the UI content
                // itself - text, map lines and glyphs keep their own pixels.
                float2 centred = (i.uv - _MapCentre.xy) / max(_MapRadius.xy, 1e-4);
                float distanceFromCentre = length(centred);
                float lens = 1.0 - smoothstep(1.0 - _FrostSoftness,
                                              1.0 + _FrostSoftness, distanceFromCentre);

                // PASS 3: outside the disc the sheet is a translucent structure
                // (the plates and arcs let the world through); inside it the
                // frosted lens below keeps its own accepted behaviour.
                colour.a *= lerp(_StructureOpacity, 1.0, lens);

                colour.a *= lerp(_Opacity, _FrostOpacity, lens);
                colour.rgb = lerp(colour.rgb, colour.rgb * 0.90 + 0.08, lens * _FrostTint);

                if (_FrostGrain > 0.0)
                {
                    // Subtle frost grain: a static per-pixel dither, not a blur.
                    float grain = frac(sin(dot(i.uv * _ScreenParams.xy,
                                               float2(12.9898, 78.233))) * 43758.5453);
                    colour.rgb += (grain - 0.5) * _FrostGrain * lens;
                }

                return colour;
            }
            ENDHLSL
        }
    }

    Fallback "Unlit/Texture"
}
