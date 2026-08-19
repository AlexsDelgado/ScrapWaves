Shader "ScrapWaves/GameFeel/Scrap VFX"
{
    Properties
    {
        [MainTexture] _MainTex("Particle Texture", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,0.4,0.05,1)
        _EmissionColor("Emission Color", Color) = (1,0.4,0.05,1)
        _EmissionIntensity("Emission Intensity", Range(0,12)) = 2
        _Luminescence("Luminescence", Range(0,1)) = 0.4
        _Heat("Heat", Range(0,1)) = 0
        _Pulse("Pulse", Range(0,1)) = 1
        _Dissolve("Dissolve", Range(0,1)) = 0
        _NoiseScale("Noise Scale", Range(0.1,30)) = 7
        _NoiseSpeed("Noise Speed", Range(0,10)) = 2
        _VertexJitter("Vertex Jitter", Range(0,0.15)) = 0.015
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float _EmissionIntensity;
                float _Luminescence;
                float _Heat;
                float _Pulse;
                float _Dissolve;
                float _NoiseScale;
                float _NoiseSpeed;
                float _VertexJitter;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float wave = sin((input.positionOS.x + input.positionOS.y + input.positionOS.z) * _NoiseScale + _Time.y * _NoiseSpeed);
                float3 positionOS = input.positionOS.xyz + input.normalOS * wave * _VertexJitter * (0.35 + _Heat);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float noise = Hash21(floor((input.uv + _Time.y * _NoiseSpeed * 0.03) * _NoiseScale * 8));
                clip(noise - saturate(_Dissolve - 0.02));
                float fresnel = pow(1.0 - saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS))), 2.0);
                float4 baseColor = _BaseColor * input.color;
                float intensity = _EmissionIntensity * _Luminescence *
                                  (0.4 + _Pulse * 0.6) *
                                  (0.7 + fresnel * 0.8);
                float3 color = baseColor.rgb * 0.25 + _EmissionColor.rgb * intensity;
                float alpha = saturate(baseColor.a * (1.0 - _Dissolve) * (0.35 + _Pulse * 0.65));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
