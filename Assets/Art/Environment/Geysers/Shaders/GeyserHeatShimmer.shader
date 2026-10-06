Shader "ScrapWaves/Level/Geyser Heat Shimmer"
{
    Properties { _Distortion("Heat distortion", Range(0,.012))=.0035 _FlowTime("Flow time", Float)=0 }
    SubShader
    {
        Tags { "Queue"="Transparent-5" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalWS:TEXCOORD1; float3 viewWS:TEXCOORD2; };
            CBUFFER_START(UnityPerMaterial)
            float _Distortion; float _FlowTime;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o; VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS=p.positionCS; o.uv=input.uv; o.normalWS=TransformObjectToWorldNormal(input.normalOS);
                o.viewWS=GetWorldSpaceViewDir(p.positionWS); return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float height=smoothstep(0,.12,input.uv.y)*(1-smoothstep(.45,1,input.uv.y));
                float facing=saturate(dot(normalize(input.normalWS),normalize(input.viewWS)));
                float edge=smoothstep(0,.45,facing);
                float flow=input.uv.y*13-_FlowTime*2.3;
                float2 warp=float2(sin(flow+input.uv.x*17),cos(flow*1.3-input.uv.x*23));
                warp*=height*edge*_Distortion;
                float2 uv=saturate(GetNormalizedScreenSpaceUV(input.positionCS)+warp);
                return half4(SampleSceneColor(uv),height*edge*.35);
            }
            ENDHLSL
        }
    }
}
