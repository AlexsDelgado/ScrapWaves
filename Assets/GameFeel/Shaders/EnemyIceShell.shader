Shader "ScrapWaves/GameFeel/Enemy Ice Shell"
{
    Properties
    {
        _IceColor("Ice Color", Color) = (0.36,0.72,0.9,1)
        _EdgeColor("Edge Color", Color) = (0.86,0.98,1,1)
        _Opacity("Opacity", Range(0,1)) = 0.58
        _Frost("Frost", Range(0,1)) = 0.72
        _Glint("Glint", Range(0,1)) = 0.42
        _Luminescence("Luminescence", Range(0,1)) = 0.4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+12" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "IceShell"
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
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _IceColor;
                float4 _EdgeColor;
                float _Opacity;
                float _Frost;
                float _Glint;
                float _Luminescence;
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
                float3 positionOS = input.positionOS.xyz + input.normalOS * 0.004;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 2.15);

                float3 coarseCell = floor(input.positionWS * 7.5);
                float3 fineCell = floor(input.positionWS * 18.0 + 9.0);
                float coarse = Hash31(coarseCell);
                float fine = Hash31(fineCell);
                float frost = saturate((coarse * 0.65 + fine * 0.35 - 0.28) * 1.8) * _Frost;

                float glintBand = abs(frac(input.positionWS.y * 0.72 + input.positionWS.x * 0.31 - _Time.y * 0.18) - 0.5);
                float glint = smoothstep(0.075, 0.0, glintBand) * fresnel * _Glint;
                float fracture = smoothstep(0.48, 0.52, abs(coarse - fine)) * 0.24;

                float3 color = lerp(_IceColor.rgb * 0.78, _EdgeColor.rgb, saturate(fresnel * 0.82 + frost * 0.42));
                color += _EdgeColor.rgb * (glint + fracture) * (0.52 + _Luminescence * 0.82);
                float alpha = _Opacity * saturate(0.28 + fresnel * 0.52 + frost * 0.3 + fracture * 0.34);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
