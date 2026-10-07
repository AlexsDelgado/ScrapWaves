Shader "ScrapWaves/EntranceSoftGround"
{
    Properties { _BaseMap("Soft texture",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1,-1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR; };
            Varyings vert(Attributes input){Varyings output;output.positionCS=TransformObjectToHClip(input.positionOS.xyz);output.uv=input.uv;output.color=input.color*_BaseColor;return output;}
            half4 frag(Varyings input):SV_Target {return SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*input.color;}
            ENDHLSL
        }
    }
}
