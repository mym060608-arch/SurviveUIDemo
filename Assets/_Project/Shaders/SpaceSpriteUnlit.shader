Shader "Survive/SpaceSpriteUnlit"
{
    // Additive star / nebula sprite for the Main Menu deep-space backdrop
    // (Phase 08 Revision 2).
    //
    // The star plate is generated ONCE at runtime; everything that moves or
    // flickers happens here, so no texture is ever rebuilt per frame:
    // - _ScrollSpeed drifts the plate sideways forever (the ship cruises to the
    //   RIGHT, so the sky flows left past it),
    // - the twinkle noise is sampled in SCROLLED uv space, which means a given
    //   star keeps its own stable phase and speed while it drifts,
    // - faint stars barely change and bright ones twinkle more, because the
    //   modulation is weighted by the star's own coverage,
    // - a rare thresholded spike gives a few stars an occasional pulse.
    Properties
    {
        _BaseMap ("Star Map", 2D) = "black" {}
        _TwinkleMap ("Twinkle Noise", 2D) = "gray" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _ScrollSpeed ("Scroll Speed", Float) = 0.01
        _TwinkleAmount ("Twinkle Amount", Range(0, 1)) = 0.35
        _TwinkleScale ("Twinkle Scale", Float) = 3
        _TwinkleSpeed ("Twinkle Speed", Float) = 0.25
        _SpikeAmount ("Spike Amount", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
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
                float _Intensity;
                float _ScrollSpeed;
                float _TwinkleAmount;
                float _TwinkleScale;
                float _TwinkleSpeed;
                float _SpikeAmount;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_TwinkleMap);
            SAMPLER(sampler_TwinkleMap);

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // Continuous drift; the plate is authored to tile seamlessly.
                float2 uv = i.uv + float2(_Time.y * _ScrollSpeed, 0.0);
                half4 star = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);

                // Two decorrelated octaves of the same noise: different phase and
                // different speed, so the sky never pulses as one block.
                half n1 = SAMPLE_TEXTURE2D(_TwinkleMap, sampler_TwinkleMap,
                    uv * _TwinkleScale + _Time.y * _TwinkleSpeed * float2(0.031, 0.017)).r;
                half n2 = SAMPLE_TEXTURE2D(_TwinkleMap, sampler_TwinkleMap,
                    uv * _TwinkleScale * 2.7 - _Time.y * _TwinkleSpeed * float2(0.013, 0.021)
                    + 0.37).r;

                // Bright stars carry the twinkle; faint ones only shimmer slightly.
                half weight = saturate(star.a * 3.0);
                half twinkle = 1.0
                    + (n1 - 0.5) * 1.5 * _TwinkleAmount * weight
                    + (n2 - 0.5) * 0.7 * _TwinkleAmount * weight;
                half spike = smoothstep(0.90, 1.0, n2) * _SpikeAmount * weight;
                half gain = max(0.0, twinkle) + spike;

                // Only the colour is modulated: the additive contribution is
                // rgb * alpha, so one gain factor keeps the brightness physical.
                return half4(star.rgb * _BaseColor.rgb * _Intensity * gain,
                             star.a * _BaseColor.a * _Intensity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
