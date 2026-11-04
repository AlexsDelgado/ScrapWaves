Shader "ScrapWaves/GameFeel/Powerup Pickup"
{
    Properties { _Opacity("Opacity", Range(0,1)) = 1 }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Opacity;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float4 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION;
                float4 uv:TEXCOORD0;
                float4 color:COLOR;
                float3 positionOS:TEXCOORD1;
                float3 positionWS:TEXCOORD2;
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.positionOS=v.positionOS.xyz;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.uv=v.uv; o.color=v.color; return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 p=i.uv.xy*2-1;
                float mask;
                half3 color=i.color.rgb;
                if(i.uv.z<.5) mask=pow(saturate(1-abs(p.y)),1.6);
                else if(i.uv.z<1.5) mask=pow(saturate(1-dot(p,p)),2.5);
                else if(i.uv.z<2.5)
                {
                    float d=min(max(abs(p.x)-.25,abs(p.y)-.78),max(abs(p.x)-.78,abs(p.y)-.25));
                    float aa=max(fwidth(d),.015);
                    mask=1-smoothstep(-aa,aa,d);
                }
                else if(i.uv.z<3.5)
                {
                    float life=saturate((i.uv.z-3)*4);
                    float2 flow=i.uv.xy*3.8+float2(.28*life,-.9*life);
                    float n=Noise(flow+i.uv.w)*.7+Noise(flow*2.2-i.uv.w+float2(-.4,.3)*life)*.3;
                    mask=pow(saturate(1-dot(p,p)),1.7)*smoothstep(.12,.8,n);
                }
                else if(i.uv.z<4.5)
                {
                    // A faint interior and a soft rim describe the dome without masking combat.
                    float3 normalOS=normalize(float3(i.positionOS.x,
                        (i.positionOS.y-.12)/max(i.uv.w*i.uv.w,.01),i.positionOS.z));
                    float3 normalWS=TransformObjectToWorldNormal(normalOS);
                    float3 viewDir=normalize(GetWorldSpaceViewDir(i.positionWS));
                    float rim=pow(saturate(1-abs(dot(normalWS,viewDir))),2.2);
                    mask=(.18+.82*rim)*smoothstep(0,.06,i.uv.y);
                }
                else
                {
                    float3 faceNormal=cross(ddy(i.positionWS),ddx(i.positionWS));
                    float3 normalWS=faceNormal*rsqrt(max(dot(faceNormal,faceNormal),1e-12));
                    float3 viewDir=normalize(GetWorldSpaceViewDir(i.positionWS));
                    float3 halfDir=normalize(normalize(float3(-.408,.816,.408))+viewDir);
                    float shine=saturate(abs(dot(normalWS,halfDir)));
                    float2 surface=i.uv.xy+i.uv.w;
                    if(i.uv.z<5.5)
                    {
                        // Silver steel: directional scratches with a restrained moving highlight.
                        mask=1;
                        float scratches=Noise(surface*float2(35,5));
                        color=color*(.84+.16*scratches)+pow(shine,28)*.18;
                    }
                    else if(i.uv.z<6.5)
                    {
                        // Corrosion has broad patches and small pits, with very little reflection.
                        mask=1;
                        float patches=Noise(surface*5)*.7+Noise(surface*13)*.3;
                        float pits=Noise(surface*43);
                        color*=lerp(.43,1.18,smoothstep(.2,.8,patches))*(.78+.22*pits);
                        color+=pow(shine,10)*.018;
                    }
                    else
                    {
                        // Thin glass stays see-through; grazing angles and glints reveal its edges.
                        float rim=pow(1-saturate(abs(dot(normalWS,viewDir))),2.6);
                        float glint=pow(shine,64);
                        mask=saturate(.18+.42*rim+.20*glint);
                        color=lerp(color,half3(.84,.95,1),rim*.6)+glint*.48;
                    }
                }
                return half4(color, saturate(mask*i.color.a*_Opacity));
            }
            ENDHLSL
        }
    }
}
