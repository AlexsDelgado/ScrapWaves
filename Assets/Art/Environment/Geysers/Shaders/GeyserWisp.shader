Shader "ScrapWaves/Level/Geyser Wisp"
{
    Properties { _BaseColor("Flow tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz); o.uv=input.uv; o.color=input.color*_BaseColor; return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p=(input.uv-.5)*float2(2.25,2);
                float radial=saturate(1-dot(p,p));
                float softness=.88+.12*sin(input.uv.x*6+sin(input.uv.y*5)*1.4);
                return half4(input.color.rgb, input.color.a*radial*radial*softness);
            }
            ENDHLSL
        }
    }
}
