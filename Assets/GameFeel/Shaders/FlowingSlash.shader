Shader "ScrapWaves/GameFeel/Flowing Slash"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.35,0.9,1,0.9)
        _EmissionColor("Emission Color", Color) = (0.55,0.96,1,1)
        _EmissionIntensity("Emission Intensity", Range(0,12)) = 2.8
        _Luminescence("Luminescence", Range(0,1)) = 0.4
        _Heat("Heat", Range(0,1)) = 0
        _Pulse("Pulse", Range(0,1)) = 1
        _Dissolve("Dissolve", Range(0,1)) = 0
        _NoiseScale("Flow Scale", Range(0.1,30)) = 5.5
        _NoiseSpeed("Flow Speed", Range(0,10)) = 4
        _VertexJitter("Ribbon Wave", Range(0,0.15)) = 0.006
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
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
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float SmoothValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                float2 blend = local * local * (3.0 - 2.0 * local);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, blend.x), lerp(c, d, blend.x), blend.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float wave = sin(input.uv.y * 18.0 - _Time.y * _NoiseSpeed * 2.2 + input.uv.x * 3.0);
                float3 positionOS = input.positionOS.xyz;
                positionOS.y += wave * _VertexJitter * input.color.a;
                output.positionCS = TransformObjectToHClip(positionOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _Time.y * _NoiseSpeed;
                float2 flowUv = float2(
                    input.uv.y * _NoiseScale - time * 0.42,
                    input.uv.x * 3.2 + sin(input.uv.y * 5.0 - time * 0.18));
                float broadFlow = SmoothValueNoise(flowUv);
                float fineFlow = SmoothValueNoise(flowUv * 1.73 + float2(4.7, -2.3));
                float flow = lerp(broadFlow, fineFlow, 0.32);

                float flowingBand = sin(input.uv.y * 20.0 - time * 2.1 + flow * 4.0) * 0.5 + 0.5;
                flowingBand = smoothstep(0.08, 0.92, flowingBand);
                float lifetimeFade = 1.0 - smoothstep(0.64, 1.0, _Dissolve);

                float edge = smoothstep(0.2, 1.0, input.uv.x);
                float whiteCore = smoothstep(0.72, 0.96, input.uv.x);
                float pulse = 0.65 + _Pulse * 0.35;
                float intensity = _EmissionIntensity * _Luminescence * pulse;
                intensity *= lerp(0.88, 1.08, flowingBand) * lerp(0.9, 1.18, edge);

                float4 baseColor = _BaseColor * input.color;
                float3 saturatedBody = lerp(baseColor.rgb, _EmissionColor.rgb, 0.62);
                float3 color = saturatedBody * (0.55 + intensity * 0.45);
                color = lerp(color, float3(1.0, 1.0, 1.0) * (0.92 + intensity * 0.18), whiteCore);
                float alpha = baseColor.a * lifetimeFade;
                alpha *= lerp(0.92, 1.0, flowingBand);
                alpha = lerp(alpha, saturate(input.color.a * lifetimeFade), whiteCore);
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
