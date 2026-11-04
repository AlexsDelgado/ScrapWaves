Shader "ScrapWaves/Spawners/Hologram"
{
    Properties
    {
        _BaseColor("Body Color", Color) = (0.08, 0.65, 0.48, 1)
        _RimColor("Rim Color", Color) = (0.35, 1, 0.75, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.7
        _RimPower("Rim Power", Range(0.5, 6)) = 2.2
        _RimStrength("Rim Strength", Range(0, 1)) = 0.75
        _ScanlineDensity("Scan Bands Per World Unit", Range(0.1, 16)) = 2
        _ScanlineSpeed("Scan Drift", Range(-2, 2)) = 0.2
        _ScanlineStrength("Scan Band Strength", Range(0, 0.5)) = 0.12
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Hologram"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _RimColor;
                half _Opacity;
                half _RimPower;
                half _RimStrength;
                float _ScanlineDensity;
                float _ScanlineSpeed;
                half _ScanlineStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half facing = saturate(dot(normal, viewDirection));
                half rim = pow(1.0h - facing, max(0.5h, _RimPower));
                // A gentle fixed form gradient keeps silhouettes legible without scene lights or bloom.
                half form = 0.72h + 0.28h * saturate(dot(normal, half3(0.3h, 0.8h, -0.52h)));
                half band = 0.5h + 0.5h * sin((input.positionWS.y * _ScanlineDensity
                    - _Time.y * _ScanlineSpeed) * 6.2831853);
                half scan = 1.0h - saturate(_ScanlineStrength) * band;
                half3 color = lerp(_BaseColor.rgb * form, _RimColor.rgb, rim * _RimStrength) * scan;
                half alpha = saturate(_Opacity * _BaseColor.a) * lerp(0.82h, 1.0h, rim) * scan;
                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
