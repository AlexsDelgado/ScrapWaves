Shader "ScrapWaves/UI/Liquid Health Bar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Fill ("Health", Range(0,1)) = 1
        _LiquidTime ("Animation time", Float) = 0
        _Impact ("Damage agitation", Range(0,1)) = 0
        _WaveStrength ("Waves", Range(0,2)) = 1
        _GlowStrength ("Soft glow", Range(0,1)) = 0.16
        _LowHealth ("Low health pulse", Float) = 0
        _LiquidSize ("Displayed bar size", Vector) = (316,18,0,0)
        _SpriteUVRect ("Sprite UV origin and size", Vector) = (0,0,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" "PreviewType"="Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 liquidUv : TEXCOORD1;
                float4 localPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            sampler2D _MainTex;
            float4 _Color, _TextureSampleAdd, _ClipRect, _LiquidSize, _SpriteUVRect;
            float _Fill, _LiquidTime, _Impact, _WaveStrength, _LowHealth, _GlowStrength;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.localPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = v.uv;
                // Canvas batching changes vertex positions into canvas space.
                // Sprite UVs remain stable, including sprites packed in an atlas.
                o.liquidUv = (v.uv - _SpriteUVRect.xy) / _SpriteUVRect.zw;
                return o;
            }

            float hash(float n) { return frac(sin(n * 127.1 + 311.7) * 43758.5453); }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.liquidUv;
                float aspect = _LiquidSize.x / _LiquidSize.y;
                float2 p = float2(uv.x * aspect, uv.y);
                float t = _LiquidTime;
                float aa = max(fwidth(p.y), 0.002);
                float energy = _WaveStrength * (1.0 + _Impact * 2.8);

                // Two travelling waves form the air/liquid surface across the bar.
                float wave = sin(p.x * 2.5 - t * 2.1) * 0.027
                    + sin(p.x * 4.9 + t * 1.35) * 0.015;
                float surface = 0.88 + wave * energy;
                float edgeMotion = sin(uv.y * 7.0 + t * 3.0) * 0.04 * energy;
                // Taper movement near empty/full so those values remain unambiguous.
                float taper = saturate(_Fill * aspect) * saturate((1.0 - _Fill) * aspect);
                float end = _Fill * aspect + (edgeMotion - 0.10 * pow(abs(uv.y - 0.45) * 2.0, 2.0)) * taper;
                float bottom = smoothstep(0.015, 0.015 + aa, uv.y);
                float top = 1.0 - smoothstep(surface - aa, surface + aa, uv.y);
                float right = 1.0 - smoothstep(end - aa, end + aa, p.x);
                float alpha = bottom * top * right * step(0.00001, _Fill);

                float depth = saturate(uv.y / surface);
                float current = sin(p.x * 1.6 + uv.y * 5.0 - t * 1.2)
                    * sin(p.x * 0.75 - uv.y * 6.0 + t * 0.8);
                float3 liquid = i.color.rgb * (0.40 + depth * 0.66 + current * 0.10);
                float rim = exp(-abs(uv.y - surface + 0.018) * 65.0);
                float meniscus = exp(-abs(p.x - end + 0.018) * 40.0) * taper;
                float3 gleam = lerp(i.color.rgb, float3(0.77, 1.0, 0.53), 0.65);
                liquid += gleam * (rim * 0.70 + meniscus * 0.25);
                liquid += i.color.rgb * exp(-abs(uv.y - 0.14) * 13.0) * 0.18;

                // Procedural rising bubbles; no textures or particle objects required.
                float cell = floor(p.x / 0.75);
                float bubbles = 0.0;
                [unroll] for (int n = -1; n <= 1; n++)
                {
                    float id = cell + n;
                    float seed = hash(id + 8.0);
                    float phase = frac(t * (0.17 + seed * 0.16) + hash(id + 42.0));
                    float2 center = float2((id + 0.5) * 0.75 + sin(t * 1.1 + id) * 0.065,
                        lerp(-0.12, 1.06, phase));
                    float radius = lerp(0.028, 0.069, seed);
                    float d = length(p - center);
                    float ring = (1.0 - smoothstep(radius - aa, radius + aa, d))
                        * smoothstep(radius * 0.45 - aa, radius * 0.45 + aa, d);
                    bubbles += ring * (0.25 + seed * 0.28);
                }
                liquid += gleam * bubbles;
                float pulse = _LowHealth * (0.5 + 0.5 * sin(t * 4.8));
                liquid += gleam * (_Impact * 0.16 + pulse * 0.12);

                // Subtle light spreading inward from the surface and fill edge.
                // Kept inside the existing fill mask so empty HP stays empty.
                float softGlow = exp(-abs(uv.y - surface + 0.018) * 14.0) * 0.65
                    + exp(-abs(p.x - end + 0.018) * 10.0) * taper * 0.30;
                liquid = liquid * (1.0 + _GlowStrength * 0.35) + gleam * softGlow * _GlowStrength;

                alpha *= i.color.a * (tex2D(_MainTex, i.uv) + _TextureSampleAdd).a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(i.localPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif
                return float4(saturate(liquid), alpha);
            }
            ENDCG
        }
    }
}
