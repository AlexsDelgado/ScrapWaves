Shader "ScrapWaves/Level/Below Level Fog"
{
    Properties
    {
        _BaseColor("Fog Color", Color) = (0.45, 0.52, 0.6, 0.92)
        _NoiseScale("Noise Scale", Range(0.05, 8)) = 1.2
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.2
        _EdgeFade("Edge Fade", Range(0.05, 0.5)) = 0.15
        _Visibility("Visibility", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _NoiseScale;
                float _NoiseStrength;
                float _EdgeFade;
                float _Visibility;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
                float edgeMask = smoothstep(0.0, _EdgeFade, edge);

                float n = ValueNoise(input.positionWS.xz * _NoiseScale + _Time.y * 0.05);
                float alpha = _BaseColor.a * edgeMask * saturate(_Visibility);
                alpha *= lerp(1.0, n, _NoiseStrength);

                return half4(_BaseColor.rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
