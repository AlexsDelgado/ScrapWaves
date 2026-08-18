Shader "ScrapWaves/GameFeel/Enemy Status Line"
{
    Properties
    {
        _Brightness("Brightness", Range(0.5,2)) = 1.08
        _Luminescence("Luminescence", Range(0,1)) = 0.4
        _Pulse("Pulse", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "StatusLine"
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
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Brightness;
                float _Luminescence;
                float _Pulse;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float pulse = 1.0 + sin(_Time.y * 2.35) * _Pulse * 0.045;
                float sameHueLift = 1.0 + _Luminescence * 0.18;
                float3 color = input.color.rgb * _Brightness * sameHueLift * pulse;
                return half4(color, saturate(input.color.a));
            }
            ENDHLSL
        }
    }
}
