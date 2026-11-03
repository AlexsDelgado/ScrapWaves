Shader "ScrapWaves/Heat/Screen Edges"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _FlameTex ("Baked fire animation", 2D) = "black" {}
        _FrameColumns ("Frame columns", Float) = 8
        _FrameRows ("Frame rows", Float) = 9
        _FrameCount ("Frame count", Float) = 72
        _FrameRate ("Frames per second", Float) = 24
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
        Blend One OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _FlameTex;
            float4 _FlameTex_TexelSize;
            float _FrameColumns, _FrameRows, _FrameCount, _FrameRate;
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
            half4 ReadFrame(float index, float2 uv, float2 grid)
            {
                float2 cell = float2(fmod(index, grid.x), grid.y - 1.0 - floor(index / grid.x));
                float2 pixels = _FlameTex_TexelSize.zw / grid;
                // Two extruded pixels around each frame prevent neighboring-frame bleed.
                float2 address = (cell * pixels + 2.5 + saturate(uv) * (pixels - 5.0))
                    * _FlameTex_TexelSize.xy;
                half4 frame = tex2D(_FlameTex, address);
                frame.rgb *= frame.a;
                return frame;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                clip(_Intensity - 0.001);
                float centerRadius = length((i.uv - 0.5) / float2(0.30, 0.30));
                clip(centerRadius - 1.0);
                float clearCenter = smoothstep(1.0, 1.16, centerRadius);
                float2 grid = max(float2(1, 1), floor(float2(_FrameColumns, _FrameRows)));
                float count = clamp(floor(_FrameCount), 1.0, grid.x * grid.y);
                // Playback uses the existing controller clock, so pause/reduced motion freeze
                // the animation. There is no live simulation or UV deformation in the HUD.
                float playhead = frac(max(0.0, _EffectTime) * max(0.0, _FrameRate) / count) * count;
                float first = floor(playhead);
                float second = fmod(first + 1.0, count);
                half4 frame = lerp(ReadFrame(first, i.uv, grid), ReadFrame(second, i.uv, grid), frac(playhead));
                // Interpolating premultiplied frames keeps extinguishing tips free of dark halos.
                // Fade by distance from the nearest edge, with equal pixel reach on all sides.
                float2 edgeDistance = min(i.uv, 1.0 - i.uv);
                float inward = min(edgeDistance.x * max(_Aspect, 0.1), edgeDistance.y);
                float edgeWeight = 1.0 - smoothstep(0.015, 0.22, inward);
                float opacity = lerp(0.08, 0.29, edgeWeight);
                float amount = clearCenter * opacity * saturate(_Intensity);
                return half4(frame.rgb * (1.08 * amount), frame.a * amount);
            }
            ENDHLSL
        }
    }
}
