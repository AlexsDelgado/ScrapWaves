Shader "ScrapWaves/Level/Geyser Wisp"
{
    Properties
    {
        _BaseColor("Flow tint", Color) = (1,1,1,1)
        _RibbonWidth("Ribbon width", Range(.005,.1)) = .05
        _RibbonWaviness("Ribbon waviness", Range(0,.15)) = .1
        _WispOpacity("Surrounding wisp opacity", Range(0,1)) = .16
    }
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
            float _RibbonWidth;
            float _RibbonWaviness;
            float _WispOpacity;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz); o.uv=input.uv; o.color=input.color*_BaseColor; return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p=(input.uv-.5)*float2(2.25,2);
                float radial=saturate(1-dot(p,p));
                // A narrow, gently bent ribbon carries direction. The surrounding air
                // stays translucent instead of building an opaque stack of smoke puffs.
                // Stretched particle billboards map U along their velocity axis.
                float along=input.uv.x;
                float taper=sin(saturate(along)*PI);
                float center=.5+sin(along*9)*_RibbonWaviness*taper;
                float width=max(.001,_RibbonWidth*taper);
                float distanceToRibbon=abs(input.uv.y-center);
                float antialias=max(fwidth(distanceToRibbon),.001);
                float ribbon=1-smoothstep(max(0,width-antialias),width+antialias,distanceToRibbon);
                float ends=smoothstep(0,.12,along)*(1-smoothstep(.75,1,along));
                float alpha=input.color.a*(radial*radial*_WispOpacity+ribbon*ends*.85);
                return half4(lerp(input.color.rgb,1.0.xxx,ribbon*.2), saturate(alpha));
            }
            ENDHLSL
        }
    }
}
