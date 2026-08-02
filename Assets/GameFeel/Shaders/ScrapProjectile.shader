Shader "ScrapWaves/GameFeel/Scrap Projectile"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.2,0.08,0.02,1)
        _EmissionColor("Emission Color", Color) = (1,0.45,0.05,1)
        _EmissionIntensity("Emission Intensity", Range(0,12)) = 2
        _Heat("Heat", Range(0,1)) = 0
        _Metallic("Metallic", Range(0,1)) = 0.65
        _Smoothness("Smoothness", Range(0,1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float3 viewDirWS:TEXCOORD1; };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _EmissionColor;
                float _EmissionIntensity;
                float _Heat;
                float _Metallic;
                float _Smoothness;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positions.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                Light mainLight = GetMainLight();
                float ndl = saturate(dot(normalWS, mainLight.direction));
                float steppedLight = floor(ndl * 4.0) / 3.0;
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 3.0);
                float3 lit = _BaseColor.rgb * (0.18 + steppedLight * mainLight.color);
                float3 emission = _EmissionColor.rgb * _EmissionIntensity * (0.45 + _Heat * 0.8 + fresnel * 0.65);
                return half4(lit + emission, 1);
            }
            ENDHLSL
        }
    }
}
