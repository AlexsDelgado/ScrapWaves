Shader "ScrapWaves/GameFeel/Flamethrower Plume"
{
    Properties
    {
        [MainTexture] _MainTex("Particle Texture", 2D) = "white" {}
        _BaseColor("Outer Flame", Color) = (1,0.075,0.008,0.82)
        _EmissionColor("Inner Flame", Color) = (1,0.36,0.025,1)
        _HotColor("Hot Core", Color) = (1,0.86,0.24,1)
        _EmissionIntensity("Emission Intensity", Range(0,8)) = 2.4
        _Luminescence("Luminescence", Range(0,1)) = 0.4
        _Heat("Heat", Range(0,1)) = 0
        _Pulse("Pulse", Range(0,1)) = 1
        _Dissolve("Dissolve", Range(0,1)) = 0
        _NoiseScale("Noise Scale", Range(0.1,8)) = 1.7
        _NoiseSpeed("Noise Speed", Range(0,5)) = 1.35
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
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
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 heatPath : TEXCOORD1;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float2 heatPath : TEXCOORD3;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float4 _HotColor;
                float _EmissionIntensity;
                float _Luminescence;
                float _Heat;
                float _Pulse;
                float _Dissolve;
                float _NoiseScale;
                float _NoiseSpeed;
            CBUFFER_END

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 p)
            {
                float3 cell = floor(p);
                float3 blend = frac(p);
                blend = blend * blend * (3.0 - 2.0 * blend);

                float n000 = Hash31(cell + float3(0, 0, 0));
                float n100 = Hash31(cell + float3(1, 0, 0));
                float n010 = Hash31(cell + float3(0, 1, 0));
                float n110 = Hash31(cell + float3(1, 1, 0));
                float n001 = Hash31(cell + float3(0, 0, 1));
                float n101 = Hash31(cell + float3(1, 0, 1));
                float n011 = Hash31(cell + float3(0, 1, 1));
                float n111 = Hash31(cell + float3(1, 1, 1));

                float nearZ = lerp(lerp(n000, n100, blend.x), lerp(n010, n110, blend.x), blend.y);
                float farZ = lerp(lerp(n001, n101, blend.x), lerp(n011, n111, blend.x), blend.y);
                return lerp(nearZ, farZ, blend.z);
            }

            float FlameNoise(float3 p)
            {
                float value = ValueNoise(p) * 0.55;
                value += ValueNoise(p * 2.03 + 7.1) * 0.28;
                value += ValueNoise(p * 4.07 + 19.7) * 0.17;
                return value;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                output.heatPath = input.heatPath;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _Time.y * _NoiseSpeed;
                float3 noisePosition = input.positionOS * _NoiseScale;
                noisePosition -= float3(time * 0.08, time * 0.68, time * 0.92);
                float broadNoise = FlameNoise(noisePosition);
                float detailNoise = ValueNoise(noisePosition * 2.7 + 31.4);
                float turbulence = saturate(broadNoise * 0.76 + detailNoise * 0.24);

                float erosion = smoothstep(0.25 + _Dissolve * 0.58, 0.64, turbulence + input.color.a * 0.11);
                float facing = abs(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                float softSurface = lerp(0.1, 1.0, pow(saturate(facing), 0.55));
                float life = saturate(input.color.a * (0.55 + _Pulse * 0.45));
                float alpha = saturate(_BaseColor.a * life * erosion * softSurface * (1.0 - _Dissolve));

                float heat = saturate(input.heatPath.x + (turbulence - 0.5) * 0.55 + _Heat * 0.12);
                float hotMask = smoothstep(0.35, 0.92, heat);
                float3 outer = lerp(_BaseColor.rgb * 0.48, _BaseColor.rgb, turbulence);
                float3 flame = lerp(outer, _HotColor.rgb, hotMask);
                float whiteHot = pow(hotMask, 4.0) * (1.0 - input.heatPath.y * 0.58);
                float3 paleCore = lerp(_HotColor.rgb, float3(1, 1, 1), _Luminescence * 0.42);
                flame = lerp(flame, paleCore, whiteHot * 0.64);

                float emission = _EmissionIntensity * _Luminescence;
                float3 color = flame * (0.62 + emission * (0.4 + hotMask * 0.52));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
