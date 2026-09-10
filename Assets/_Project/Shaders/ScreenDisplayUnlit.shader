Shader "Survive/ScreenDisplayUnlit"
{
    Properties
    {
        _BaseMap ("Screen Texture", 2D) = "white" {}
        // Authoritative console UV orientation (ConsoleUvAuthority):
        // _FlipU mirrors the mesh U axis ONCE (viewer right = quad local -x),
        // _FlipV stays 0 (the UI Toolkit RenderTexture already carries the panel
        // bottom row at texture v = 0). The defaults below MUST equal the
        // ConsoleUvAuthority constants so an authored material that is never
        // touched by ApplyToMaterial still shows the panel upright.
        _FlipU ("Flip U", Float) = 1
        _FlipV ("Flip V", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
        }

        ZWrite On
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
                float _FlipU;
                float _FlipV;
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
                // Single authoritative orientation correction.
                float2 uv = i.uv;
                uv.x = lerp(uv.x, 1.0 - uv.x, _FlipU);
                uv.y = lerp(uv.y, 1.0 - uv.y, _FlipV);
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            }
            ENDHLSL
        }
    }

    Fallback "Unlit/Texture"
}
