Shader "ScrapWaves/Level/Geyser Debris"
{
    Properties { _BaseColor("Light scrap tint", Color)=(.76,.72,.56,1) }
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
            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o;o.positionCS=TransformObjectToHClip(input.positionOS.xyz);o.color=input.color*_BaseColor;return o;
            }
            half4 Frag(Varyings input):SV_Target { return input.color; }
            ENDHLSL
        }
    }
}
