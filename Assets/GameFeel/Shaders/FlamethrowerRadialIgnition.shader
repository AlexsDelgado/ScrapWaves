Shader "ScrapWaves/GameFeel/Flamethrower Radial Ignition"
{
    Properties
    {
        [MainTexture] _MainTex("Regular flame detail",2D)="white" {}
        _OuterColor("Outer Flame",Color)=(1,.19,.015,1)
        _HotColor("Hot Flame",Color)=(1,.86,.2,1)
        _Opacity("Opacity",Range(0,1))=1
        _Emission("Emission",Range(0,8))=2
        _BurstTime("Burst Clock",Float)=0
        _Layer("Boundary / Tongues / Embers",Float)=0
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float2 heat:TEXCOORD1; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 heat:TEXCOORD1; float4 color:COLOR; float3 positionOS:TEXCOORD2; };
            CBUFFER_START(UnityPerMaterial)
                float4 _OuterColor, _HotColor;
                float _Opacity, _Emission, _BurstTime, _Layer;
            CBUFFER_END
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; o.heat=v.heat; o.color=v.color; o.positionOS=v.positionOS.xyz; return o;
            }
            // Same billow vocabulary as Manual Flame Sprite; driven by the Q clock so pause freezes it.
            float Hash(float2 p)
            {
                p=frac(p*float2(123.34,456.21)); p+=dot(p,p+45.32); return frac(p.x*p.y);
            }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            float Billow(float2 p)
            {
                return Noise(p)*.57+Noise(p*2.07+7.4)*.29+Noise(p*4.13+19.2)*.14;
            }
            half4 Frag(Varyings i):SV_Target
            {
                // Embers stay small and inexpensive; the flame surface carries the extra detail.
                if (_Layer>1.5)
                {
                    float feather=smoothstep(0,.16,i.uv.x)*smoothstep(0,.16,1-i.uv.x);
                    return half4(lerp(_OuterColor.rgb,_HotColor.rgb,i.heat.x)*(.85+_Emission*.18),i.color.a*_Opacity*feather);
                }
                float boundary=1-step(.5,_Layer);
                float2 radial=normalize(i.positionOS.xz+float2(.0001,.0001));
                float2 advect=float2(_BurstTime*.42,-_BurstTime*2.1);
                float2 p=lerp(i.uv*float2(3.7,4.8)+i.positionOS.xz*3-advect,
                    i.positionOS.xz*18-radial*_BurstTime*3.2,boundary);
                float curl=Noise(p*.57+advect);
                float density=Billow(p+(curl-.5)*1.2);
                // Scalar texture detail retains each upgrade's palette.
                float detail=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,
                    float2(.55+(density-.5)*.12,.5+(curl-.5)*.32)).g;
                float erosion=smoothstep(.2,.65,density+detail*.09);
                float edge=smoothstep(0,.2,i.uv.x)*smoothstep(0,.2,1-i.uv.x);
                float front=smoothstep(.87,1,i.uv.x)*boundary;
                float mask=lerp(edge*erosion,lerp(erosion,1,front),boundary);
                float hot=smoothstep(.38,.88,i.heat.x+(density-.5)*.8+detail*.12);
                // Burning pockets in the trailing annulus connect the tongues to the ground front.
                hot=max(hot,boundary*smoothstep(.48,.78,density)*.82);
                float3 outer=_OuterColor.rgb*(.55+density*.55);
                float3 color=lerp(outer,_HotColor.rgb,hot);
                float3 pale=lerp(_HotColor.rgb,float3(1,1,1),.22);
                color=lerp(color,pale,pow(hot,4)*.45)*(.85+_Emission*.18);
                return half4(color,saturate(i.color.a*_Opacity*mask));
            }
            ENDHLSL
        }
    }
}
