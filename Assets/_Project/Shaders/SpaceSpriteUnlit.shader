Shader "Survive/SpaceSpriteUnlit"
{
    // Additive star / nebula sprite for the Phase 08 Main Menu deep-space
    // backdrop. The generated texture already carries the star shapes; the
    // material only tints and fades whole layers, so perceived depth comes from
    // three quads at different distances and drift rates (parallax).
    Properties
    {
        _BaseMap ("Star Map", 2D) = "black" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
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
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                return half4(tex.rgb * _BaseColor.rgb * _Intensity,
                             tex.a * _BaseColor.a * _Intensity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
