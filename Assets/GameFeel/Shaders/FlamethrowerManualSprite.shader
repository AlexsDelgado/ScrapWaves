Shader "ScrapWaves/GameFeel/Manual Flame Sprite"
{
    Properties
    {
        [MainTexture] _MainTex("Existing flame detail", 2D) = "white" {}
        _CloudAge("Simulation time", Float) = 0
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
            CBUFFER_START(UnityPerMaterial)
                float _CloudAge;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half4 color : COLOR; };
            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1,0)), f.x),
                    lerp(Hash(cell + float2(0,1)), Hash(cell + 1), f.x), f.y);
            }
            float Billow(float2 p)
            {
                return Noise(p) * .57 + Noise(p * 2.07 + 7.4) * .29 + Noise(p * 4.13 + 19.2) * .14;
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2 - 1;
                float2 advect = float2(_CloudAge * .42, -_CloudAge * 2.1);
                float2 anchor = input.positionWS.xz * .38 + input.positionWS.y * .17;
                float curl = Billow(p * 2.1 + anchor + advect);
                p.x += (curl - .5) * .55 * (1 - abs(p.y));
                float density = Billow(p * 3.7 + anchor - advect * 1.17);
                // Irregular, soft tongues; no hard circle cutout or straight ribbon edge.
                float envelope = exp(-2.6 * dot(p * float2(1.15,.86), p * float2(1.15,.86)));
                envelope *= (1 - smoothstep(.6,1,abs(p.x))) * (1 - smoothstep(.6,1,abs(p.y)));
                float erosion = smoothstep(.21,.69,density + envelope * .3);
                float detail = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,float2(.55+p.x*.06,.5+p.y*.16)).g;
                float alpha = input.color.a * envelope * erosion * lerp(.85,1.1,detail);
                float hot = smoothstep(.55,.83,density + envelope * .12);
                float burning = smoothstep(.15,.5,max(input.color.r,max(input.color.g,input.color.b)));
                float3 pale = lerp(input.color.rgb,float3(1,.95,.72),.36);
                float3 color = lerp(input.color.rgb * (.65 + density*.7), pale, hot * burning);
                color *= lerp(1,1.35,burning);
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}
