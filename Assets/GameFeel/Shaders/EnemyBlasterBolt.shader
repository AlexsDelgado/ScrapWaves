Shader "ScrapWaves/GameFeel/Enemy Blaster Bolt"
{
    Properties
    {
        _CoreColor("Core Color", Color) = (1, 0.28, 0.10, 1)
        _GlowColor("Outer Glow Color", Color) = (0.6, 0.005, 0.002, 1)
        _EmissionIntensity("Luminescence", Range(0, 2.5)) = 1.6
        _Opacity("Opacity", Range(0, 1)) = 1
        _EdgeSoftness("Edge Softness", Range(0.05, 1)) = 0.95
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="SRPDefaultUnlit" }
            // Alpha blending bounds overlapping bolts; no additive white-out or moving lights.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _GlowColor;
                float _EmissionIntensity;
                float _Opacity;
                float _EdgeSoftness;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positions.positionWS);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float facing = saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                float core = pow(facing, 4.0);
                float3 color = lerp(saturate(_GlowColor.rgb), saturate(_CoreColor.rgb), core)
                             * clamp(_EmissionIntensity, 0.0, 2.5);
                float alpha = smoothstep(0.0, max(0.05, _EdgeSoftness), facing) * saturate(_Opacity);
                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
