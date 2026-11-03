Shader "ScrapWaves/Heat/Screen Edges"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Heat intensity", Range(0, 1)) = 0
        _EffectTime ("Animation time", Float) = 0
        _Aspect ("Screen aspect", Float) = 1.77778
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float _Intensity, _EffectTime, _Aspect;
            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + 1.0), f.x), f.y);
            }
            // Coverage and distance along this individual flame, for its color gradient.
            float2 Flame(float along, float inward, float side, float density, float offset)
            {
                float t = _EffectTime;
                // Anchor the roots; advect irregular eddies from each root toward its tip.
                float lane = along * density + offset;
                // Unevenly spaced roots prevent a repeated row of identical tongues.
                lane += (Noise(float2(along * 3.2 + side, offset + side * 0.37)) - 0.5) * 1.2;
                float cell = floor(lane);
                float seed = Hash(float2(cell, side + offset));
                float growth = Noise(float2(cell * 0.73 + side, t * 0.85 + seed * 9.0));
                float height = lerp(0.0252, 0.0648, seed)
                    * lerp(0.65, 1.0, _Intensity)
                    * lerp(0.64, 1.0, growth);
                float depth = inward / max(height, 0.001);
                float tip = saturate(depth);
                float eddy = Noise(float2(cell * 0.61 + side * 1.3,
                    depth * 2.4 - t * (2.0 + seed * 0.55)));
                float curl = Noise(float2(lane * 2.1 + side + eddy * 1.4,
                    depth * 5.2 - t * 3.4));
                // Fine flutter rides the dominant inward flow instead of swaying sideways.
                float bend = tip * ((eddy - 0.5) * 0.50
                    + (curl - 0.5) * 0.28 * tip);
                float rootOffset = (seed - 0.5) * 0.14;
                float x = abs(frac(lane) - 0.5 - rootOffset - bend);
                float width = 0.30 * pow(saturate(1.0 - depth), 0.9)
                    * lerp(0.75, 1.15, curl);
                float body = (1.0 - smoothstep(width, width + 0.075, x))
                    * (1.0 - smoothstep(0.82, 1.0, depth));
                // Thin tips split and dissipate smoothly, without rhythmic brightness pulses.
                float wisps = smoothstep(0.16 + tip * 0.28, 0.46 + tip * 0.28, curl);
                body *= lerp(1.0, wisps, smoothstep(0.25, 0.95, tip) * 0.72);
                float cooling = saturate(tip + x / max(width + 0.075, 0.01) * 0.20
                    - curl * tip * 0.12);
                return float2(body * lerp(0.48, 1.0, curl)
                    * lerp(0.55, 1.0, seed), cooling);
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float border = min(i.uv.x, 1.0 - i.uv.x);
                float horizontal = min(i.uv.y, 1.0 - i.uv.y);
                // The frame has a hard empty center, regardless of the animated flow.
                clip(0.10 - min(border, horizontal));
                clip(_Intensity - 0.001);
                float aspect = max(_Aspect, 0.1);
                float inward = border * aspect;
                clip(0.0864 - min(inward, horizontal));
                float corners = (1.0 - smoothstep(0.0, 0.35, border))
                    * (1.0 - smoothstep(0.0, 0.35, horizontal));
                float reach = lerp(1.0, 1.2, corners);
                float side = step(0.5, i.uv.x) * 17.0;
                float2 sideFlame = float2(0.0, 0.0);
                float2 horizontalFlame = float2(0.0, 0.0);
                if (inward < 0.0864)
                {
                    // Inverse perspective contraction: the tips lean toward screen center.
                    float along = 0.5 + (i.uv.y - 0.5) / max(0.65, 1.0 - border * 2.2);
                    float2 flame = Flame(along, inward / reach, side, 14.0, 0.0);
                    float2 smallFlame = Flame(along, inward * 1.6 / reach, side + 3.0, 26.0, 0.37);
                    smallFlame.x *= 0.55;
                    sideFlame = flame.x >= smallFlame.x ? flame : smallFlame;
                }
                if (horizontal < 0.0864)
                {
                    // Top and bottom flow inward, with independent patterns and equal pixel scale.
                    float along = (0.5 + (i.uv.x - 0.5) / max(0.65, 1.0 - horizontal * 2.2)) * aspect;
                    float edgeSeed = i.uv.y > 0.5 ? 41.0 : 63.0;
                    float2 flame = Flame(along, horizontal / reach, edgeSeed, 14.0, 0.19);
                    float2 smallFlame = Flame(along, horizontal * 1.6 / reach, edgeSeed + 6.0, 26.0, 0.57);
                    smallFlame.x *= 0.55;
                    horizontalFlame = flame.x >= smallFlame.x ? flame : smallFlame;
                }
                // Do not accumulate brightness where the horizontal and lateral flames meet.
                float2 dominantFlame = sideFlame.x >= horizontalFlame.x ? sideFlame : horizontalFlame;
                float body = dominantFlame.x;
                float baseGlow = max((1.0 - smoothstep(0.0, 0.006, inward)) * sideFlame.x,
                    (1.0 - smoothstep(0.0, 0.006, horizontal)) * horizontalFlame.x);
                // Yellow roots transition through orange to the reference red #FF381C.
                half3 flameYellow = half3(1.0, 0.82, 0.10);
                half3 flameRed = half3(1.0, 0.21960784, 0.10980392);
                half3 flameColor = lerp(flameYellow, flameRed, smoothstep(0.04, 0.80, dominantFlame.y));
                #ifndef UNITY_COLORSPACE_GAMMA
                    flameColor = GammaToLinearSpace(flameColor);
                #endif
                // A small brightness lift, still translucent and without an opaque outline.
                float opacity = (body * 0.2464 + baseGlow * 0.0392) * saturate(_Intensity);
                half3 color = flameColor * lerp(0.95, 1.18,
                    saturate(body * 0.65 + baseGlow * 0.35));
                return half4(color, opacity);
            }
            ENDHLSL
        }
    }
}
