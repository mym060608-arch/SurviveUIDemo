Shader "Survive/ScreenFocusProxyUnlit"
{
    // Phase 07B.2.2 focus-only sharp proxy for the physical console screen.
    //
    // The real console screen is ordinary opaque WORLD geometry rendered by the
    // environment Base camera, so it obeys normal depth/occlusion and receives
    // the console-focus Depth Of Field (the environment softens, the screen
    // softens with it). This proxy is a second, exactly co-located quad that
    // only the focus-only SharpScreenCamera renders: the base camera's
    // post-processed image stays underneath and the proxy restores a crisp
    // screen rectangle on top of it.
    //
    // It must never become a global top-most layer: ZWrite Off + ZTest LEqual
    // keeps it inside the Base camera's real depth buffer, so anything between
    // the eye and the screen still occludes it.
    Properties
    {
        _BaseMap ("Screen Texture", 2D) = "white" {}
        // Same single authoritative orientation as the physical screen
        // (ConsoleUvAuthority), so the proxy can never disagree with the screen
        // it covers nor with the pointer chain.
        _FlipU ("Flip U", Float) = 1
        _FlipV ("Flip V", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        ZWrite Off
        ZTest LEqual
        Cull Off
        Blend One Zero

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
                // Single authoritative orientation correction (identical to the
                // physical screen material).
                float2 uv = i.uv;
                uv.x = lerp(uv.x, 1.0 - uv.x, _FlipU);
                uv.y = lerp(uv.y, 1.0 - uv.y, _FlipV);
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
