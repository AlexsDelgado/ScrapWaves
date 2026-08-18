Shader "ScrapWaves/GameFeel/Enemy Disintegration"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _EdgeColor("Dissolve Edge", Color) = (0.72,0.57,0.39,1)
        _AshColor("Ash Color", Color) = (0.3,0.28,0.27,1)
        _Dissolve("Dissolve", Range(0,1)) = 0
        _NoiseScale("Noise Scale", Range(0.5,20)) = 6.5
        _Opacity("Opacity", Range(0,1)) = 1
        _Luminescence("Luminescence", Range(0,1)) = 0.4
        [HideInInspector] _EffectCenter("Effect Center", Vector) = (0,0,0,0)
        [HideInInspector] _EffectDirection("Effect Direction", Vector) = (0,0,1,0)
        [HideInInspector] _EffectHeight("Effect Height", Float) = 2
        [HideInInspector] _EffectRadius("Effect Radius", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest+18" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "DisintegratingBody"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

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
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 color : COLOR;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _EdgeColor;
                float4 _AshColor;
                float4 _EffectCenter;
                float4 _EffectDirection;
                float _Dissolve;
                float _NoiseScale;
                float _Opacity;
                float _Luminescence;
                float _EffectHeight;
                float _EffectRadius;
            CBUFFER_END

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float seed = Hash31(floor(positionWS * (_NoiseScale * 1.7)));
                float release = saturate((_Dissolve - 0.2 - seed * 0.55) * 2.35);
                float3 attackDirection = normalize(_EffectDirection.xyz + float3(0.001, 0.001, 0.001));
                float3 driftDirection = normalize(normalWS * 0.34 + float3(0, 0.86, 0) + attackDirection * 0.18);
                positionWS += driftDirection * release * _EffectRadius * (0.035 + seed * 0.13);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor * input.color;
                clip(albedo.a - 0.02);

                float height = max(0.1, _EffectHeight);
                float radius = max(0.1, _EffectRadius);
                float vertical = saturate((input.positionWS.y - (_EffectCenter.y - height * 0.5)) / height);
                float2 attackDirection = normalize(_EffectDirection.xz + float2(0.001, 0.001));
                float directional = saturate(dot(input.positionWS.xz - _EffectCenter.xz, attackDirection) / (radius * 2.0) + 0.5);
                float coarse = Hash31(floor(input.positionWS * _NoiseScale));
                float fine = Hash31(floor(input.positionWS * (_NoiseScale * 2.37) + 17.0));
                float noise = coarse * 0.68 + fine * 0.32;
                float field = vertical * 0.48 + directional * 0.19 + noise * 0.33;
                float threshold = lerp(-0.14, 1.14, saturate(_Dissolve));
                float edgeDistance = field - threshold;
                clip(edgeDistance);

                float edge = 1.0 - smoothstep(0.0, 0.075, edgeDistance);
                float3 normalWS = normalize(input.normalWS);
                float simpleLight = 0.58 + saturate(dot(normalWS, normalize(float3(0.32, 0.86, 0.24)))) * 0.42;
                float ashAmount = saturate(_Dissolve * 0.52 + edge * 0.46);
                float3 color = lerp(albedo.rgb * simpleLight, _AshColor.rgb, ashAmount);
                color += _EdgeColor.rgb * edge * (0.28 + _Luminescence * 0.9);
                return half4(color, albedo.a * _Opacity);
            }
            ENDHLSL
        }
    }
}
