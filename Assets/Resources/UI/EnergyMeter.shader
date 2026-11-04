Shader "ScrapWaves/UI/Energy Meter"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ExperienceMode ("XP mode", Float) = 0
        _Fill ("Progress", Range(0,1)) = 0
        _EffectTime ("Animation time", Float) = 0
        _Overheat ("Overheat", Range(0,1)) = 0
        _HeatBurst ("Overheat ignition", Range(0,1)) = 0
        _PickupSweep ("XP pickup sweep", Float) = -1
        _LevelSweep ("Level up sweep", Float) = -1
        _MeterSize ("Displayed size", Vector) = (200,25,0,0)
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
                float2 meterUv : TEXCOORD1;
                float4 localPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            sampler2D _MainTex;
            float4 _Color, _TextureSampleAdd, _ClipRect, _MeterSize, _SpriteUVRect;
            float _ExperienceMode, _Fill, _EffectTime, _Overheat, _HeatBurst, _PickupSweep, _LevelSweep;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.localPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = v.uv;
                // Stable under Canvas batching and sprite atlasing.
                o.meterUv = (v.uv - _SpriteUVRect.xy) / _SpriteUVRect.zw;
                return o;
            }

            float hash(float n) { return frac(sin(n * 127.1 + 311.7) * 43758.5453); }

            float FireNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n = cell.x + cell.y * 57.0;
                return lerp(lerp(hash(n), hash(n + 1.0), f.x),
                    lerp(hash(n + 57.0), hash(n + 58.0), f.x), f.y);
            }

            float FireTurbulence(float2 p)
            {
                return FireNoise(p) * 0.57
                    + FireNoise(p * 2.07 + 11.3) * 0.28
                    + FireNoise(p * 4.13 + 27.1) * 0.15;
            }

            float4 FireMeter(float2 uv)
            {
                float heat = saturate(_Fill);
                float t = _EffectTime;
                float2 q = (uv - 0.5) * 2.0;
                float radius = length(q);
                float circle = 1.0 - smoothstep(0.97, 1.0, radius);
                // Fire spans the disk's width; its visible level rises with heat.
                float bend = sin(uv.y * 5.0 - t * 0.8) * 0.20;
                float2 flow = float2(uv.x * 5.8 + bend, uv.y * 2.3 - t * 1.65);
                float curl = FireTurbulence(flow);
                float tongues = FireTurbulence(float2(flow.x + curl * 1.2, flow.y * 0.72));
                float detail = FireNoise(flow * float2(2.4, 1.3) + float2(curl, -t * 0.55));
                float flame = smoothstep(0.27, 0.72, tongues);
                float temperature = saturate(0.06 + (1.0 - uv.y) * 0.36
                    + flame * 0.47 + detail * 0.12 + heat * 0.11 + _Overheat * 0.07);
                float3 red = float3(0.58, 0.015, 0.002);
                float3 orange = float3(1.0, 0.31, 0.018);
                float3 gold = float3(1.0, 0.78, 0.18);
                float3 whiteHot = float3(1.0, 0.97, 0.74);
                float3 fire = lerp(red, orange, smoothstep(0.02, 0.30, temperature));
                fire = lerp(fire, gold, smoothstep(0.24, 0.69, temperature));
                fire = lerp(fire, whiteHot, smoothstep(0.67, 1.0, temperature) * (0.35 + heat * 0.45 + _Overheat * 0.20));
                float flicker = 0.96 + sin(t * 8.0) * 0.025 + sin(t * 13.7) * 0.015;
                fire *= (0.20 + heat * 0.75 + _Overheat * 0.12) * flicker;
                float rim = exp(-abs(radius - 0.94) * 42.0);
                float3 ambient = orange * rim * (0.012 + heat * 0.08 + _Overheat * 0.04);
                float sparks = 0.0;
                [unroll] for (int k = 0; k < 6; k++)
                {
                    float seed = hash(float(k) + 21.0);
                    float phase = frac(t * (0.19 + seed * 0.16) + seed);
                    float2 pos = float2(0.5 + (seed - 0.5) * 0.92
                        + sin(t * 1.6 + seed * 21.0) * 0.035 * phase,
                        phase);
                    float2 d = uv - pos;
                    d.y *= 0.55;
                    sparks += exp(-dot(d, d) * 16000.0) * sin(phase * 3.14159265)
                        * smoothstep(seed * 0.65, seed * 0.65 + 0.25, heat);
                }
                float3 col = ambient + fire + gold * sparks * (0.2 + heat * 0.8);
                col += whiteHot * _HeatBurst * flame * 0.16;
                // Advected, warped turbulence forms the flame front. No repeated
                // geometric tongues: thin strands erode, split and fade as they rise.
                float taper = saturate(heat * 20.0) * saturate((1.0 - heat) * 20.0);
                float2 plumeFlow = float2(uv.x * 14.0 + (curl - 0.5) * 2.7,
                    uv.y * 4.6 - t * 2.7);
                float plume = FireTurbulence(plumeFlow);
                float erosion = FireNoise(plumeFlow * float2(2.8, 2.1)
                    + float2(detail * 1.3, -t));
                float band = max(0.001, 0.17 * taper);
                float density = (heat - uv.y) / band + (plume - 0.5) * 1.8
                    + (erosion - 0.5) * 0.32;
                float feather = max(fwidth(density), 0.065);
                float body = smoothstep(-0.09 - feather, 0.15 + feather, density)
                    * step(0.00001, heat);
                float crest = smoothstep(heat - 0.10 * taper - 0.001,
                    heat + 0.025 * taper + 0.001, uv.y) * taper;
                float3 crestColor = lerp(float3(0.80, 0.09, 0.012), gold,
                    smoothstep(0.02, 0.95, density));
                crestColor *= (0.35 + heat * 0.65) * (0.85 + erosion * 0.15);
                col = lerp(col, crestColor, crest);
                // Only a faint ember remains at zero; it does not show a filled meter.
                float ember = exp(-q.x * q.x * 18.0 - pow((uv.y - 0.055) * 35.0, 2.0))
                    * (1.0 - saturate(heat * 20.0));
                col = lerp(orange * 0.24, col, body);
                return float4(saturate(col), circle * max(body, ember * 0.40));
            }

            float4 Experience(float2 uv, float3 tint)
            {
                float t = _EffectTime;
                float aspect = _MeterSize.x / _MeterSize.y;
                float2 p = float2(uv.x * aspect, uv.y);
                float aa = max(fwidth(uv.x), 0.0005);
                float body = (1.0 - smoothstep(_Fill - aa, _Fill + aa, uv.x)) * step(0.00001, _Fill);
                float channel = 0.5 + sin(p.x * 1.7 - t * 1.4) * 0.09;
                float rail = exp(-abs(uv.y - channel) * 24.0);
                float haze = exp(-abs(uv.y - channel) * 6.0);
                float3 cyan = float3(0.34, 0.88, 1.0);
                float3 col = tint * (0.46 + 0.33 * sin(uv.y * 3.14159265));
                col += cyan * (rail * 0.20 + haze * 0.10);
                float edge = exp(-abs(uv.x - _Fill) * aspect * 30.0);
                col += cyan * edge * 0.33;

                float sparks = 0.0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float seed = hash(k + 31.0);
                    float x = frac(t * (0.10 + seed * 0.07) + seed) * aspect;
                    float2 d = p - float2(x, 0.23 + seed * 0.54);
                    sparks += exp(-abs(d.x) * 11.0 - d.y * d.y * 150.0);
                }
                col += cyan * sparks * 0.34;
                float pickup = step(0.0, _PickupSweep) * sin(saturate(_PickupSweep) * 3.14159265);
                float pickupFront = _PickupSweep * _Fill;
                col += cyan * exp(-abs(uv.x - pickupFront) * 32.0) * pickup * 0.43;
                float level = step(0.0, _LevelSweep) * sin(saturate(_LevelSweep) * 3.14159265);
                float levelFront = _LevelSweep * 1.3 - 0.15;
                col += float3(0.78, 0.97, 1.0) * exp(-abs(uv.x - levelFront) * 22.0) * level * 0.8;
                col += cyan * level * 0.08;
                // An unfilled meter has a thin moving pilot line, not a filled bar.
                // Gains still reveal the much brighter, full-height blue body.
                float pilot = exp(-abs(uv.y - 0.5) * 42.0);
                float pilotFlow = 0.5 + 0.5 * sin(p.x * 2.8 - t * 1.6);
                float3 idle = float3(0.018, 0.045, 0.065)
                    + cyan * pilot * (0.16 + pilotFlow * 0.10);
                float idleAlpha = 0.20 + pilot * 0.44;
                return float4(lerp(idle, saturate(col), body), lerp(idleAlpha, 1.0, body));
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 col;
                if (_ExperienceMode > 0.5)
                    col = Experience(i.meterUv, i.color.rgb);
                else
                    col = FireMeter(i.meterUv);
                col.a *= i.color.a * (tex2D(_MainTex, i.uv) + _TextureSampleAdd).a;
                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.localPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif
                return col;
            }
            ENDCG
        }
    }
}
