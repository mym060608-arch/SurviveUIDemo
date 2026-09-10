Shader "Survive/ThrusterPlumeUnlit"
{
    // Additive thruster plume for the Phase 08 Main Menu hero ship.
    //
    // The plume mesh is authored aft-pointing in local space with v = 0 at the
    // nozzle and v = 1 at the tip, so the axial falloff needs no texture: the
    // shading is procedural and the runtime only animates _Intensity / _BaseColor
    // (idle flicker, engine boost, departure stretch).
    Properties
    {
        _BaseColor ("Plume Colour (HDR)", Color) = (1.4, 1.9, 2.6, 1)
        _CoreColor ("Core Colour (HDR)", Color) = (3.2, 3.0, 2.6, 1)
        _Intensity ("Intensity", Float) = 1
        _Length ("Length", Float) = 1
        _CoreMix ("Core Mix", Range(0, 1)) = 0.55
        _Falloff ("Axial Falloff", Float) = 1.7
        _NozzleFade ("Nozzle Fade", Range(0.001, 1)) = 0.06
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
                float4 _BaseColor;
                float4 _CoreColor;
                float _Intensity;
                float _Length;
                float _CoreMix;
                float _Falloff;
                float _NozzleFade;
            CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // v = 0 at the nozzle, v = 1 at the open tip.
                float v = i.uv.y;
                float u = abs(i.uv.x * 2.0 - 1.0);

                // Bright at the nozzle, gone at the tip.
                float axial = pow(saturate(1.0 - v), _Falloff);
                // Soft radial edge so the plume reads as a volume, not a cone.
                float radial = saturate(1.0 - u * u);
                // Thin dark seam right at the nozzle mouth, so the plume does
                // not paint over the engine bell it comes out of.
                float mouth = smoothstep(0.0, _NozzleFade, v);

                float body = axial * radial * mouth;
                float core = pow(axial, 2.4) * pow(radial, 3.0) * mouth;

                half3 colour = lerp(_BaseColor.rgb, _CoreColor.rgb, core * _CoreMix);
                half alpha = saturate(body + core * 0.6) * _Intensity;
                return half4(colour * _Intensity, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
