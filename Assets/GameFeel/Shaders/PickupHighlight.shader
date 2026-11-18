Shader "ScrapWaves/GameFeel/Pickup Highlight"
{
    // _Mode 0 = fresnel rim shell drawn over the pickup mesh, 1 = ground ring on a flat quad (uv 0..1).
    Properties
    {
        [HDR] _Color("Color", Color) = (1,1,1,1)
        _Mode("Mode (0 rim, 1 ring)", Float) = 0
        _Intensity("Intensity", Range(0,4)) = 1
        _Pulse("Pulse Phase", Float) = 0
        _Expand("Rim Expand (world)", Float) = 0.03
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "PickupHighlight"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Mode;
                float _Intensity;
                float _Pulse;
                float _Expand;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 nWS = TransformObjectToWorldNormal(input.normalOS);
                float3 pWS = TransformObjectToWorld(input.positionOS.xyz);
                pWS += nWS * _Expand * (1.0 - _Mode);
                o.positionCS = TransformWorldToHClip(pWS);
                o.normalWS = nWS;
                o.viewDirWS = GetWorldSpaceViewDir(pWS);
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float pulse = 0.85 + 0.15 * sin(_Pulse);
                if (_Mode < 0.5)
                {
                    float facing = saturate(abs(dot(normalize(i.normalWS), normalize(i.viewDirWS))));
                    float rim = pow(1.0 - facing, 2.2);
                    return half4(_Color.rgb * _Intensity * pulse, saturate(rim * 1.2) * _Color.a);
                }
                float r = length(i.uv - 0.5) * 2.0;
                float band = smoothstep(0.62, 0.78, r) * (1.0 - smoothstep(0.88, 1.0, r));
                float fill = (1.0 - smoothstep(0.0, 0.9, r)) * 0.18;
                float a = saturate(band + fill) * _Color.a;
                return half4(_Color.rgb * _Intensity * pulse, a);
            }
            ENDHLSL
        }
    }
}
