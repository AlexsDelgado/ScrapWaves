Shader "ScrapWaves/GameFeel/Flamethrower Radial Ignition"
{
    Properties
    {
        _OuterColor("Outer Flame",Color)=(1,.19,.015,1)
        _HotColor("Hot Flame",Color)=(1,.86,.2,1)
        _Opacity("Opacity",Range(0,1))=1
        _Emission("Emission",Range(0,8))=2
        _BurstTime("Burst Clock",Float)=0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float2 heat:TEXCOORD1; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 heat:TEXCOORD1; float4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
                float4 _OuterColor, _HotColor;
                float _Opacity, _Emission, _BurstTime;
            CBUFFER_END
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; o.heat=v.heat; o.color=v.color; return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            half4 Frag(Varyings i):SV_Target
            {
                float noise=Noise(float2(i.uv.x*3,i.uv.y*7-_BurstTime*15));
                float edge=smoothstep(0,.16,i.uv.x)*smoothstep(0,.16,1-i.uv.x);
                float tongue=lerp(1,edge*lerp(.55,1,noise),i.heat.y);
                float heat=saturate(i.heat.x+(noise-.5)*.22);
                float3 color=lerp(_OuterColor.rgb,_HotColor.rgb,pow(heat,1.6));
                color*=.85+_Emission*.18;
                return half4(color,saturate(i.color.a*_Opacity*tongue));
            }
            ENDHLSL
        }
    }
}
