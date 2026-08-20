Shader "ScrapWaves/UI/Scrap Menu Background"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.035, 0.043, 0.039, 1)
        _SecondaryColor ("Secondary Steel", Color) = (0.067, 0.078, 0.075, 1)
        _AccentColor ("Accent", Color) = (0.659, 0.78, 0.561, 1)
        _BreathAmount ("Light Breath Amount", Range(0, 0.1)) = 0.035
        _HazardScale ("Hazard Stripe Scale", Range(2, 80)) = 22
        _HazardSpeed ("Hazard Stripe Speed", Range(-2, 2)) = 0.05
        _HazardOpacity ("Hazard Stripe Opacity", Range(0, 0.3)) = 0.07
        _GrainScale ("Grain Scale", Range(10, 800)) = 260
        _GrainStrength ("Grain Strength", Range(0, 0.15)) = 0.025
        _ScanlineDensity ("Scanline Density", Range(10, 800)) = 280
        _ScanlineStrength ("Scanline Strength", Range(0, 0.15)) = 0.018
        _VignetteStrength ("Vignette Strength", Range(0, 1)) = 0.58
        _ImpactPulse ("Impact Pulse", Range(0, 1)) = 0
        _UnscaledTime ("Unscaled Time", Float) = 0
        _MotionScale ("Motion Scale", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            fixed4 _BaseColor;
            fixed4 _SecondaryColor;
            fixed4 _AccentColor;
            float _BreathAmount;
            float _HazardScale;
            float _HazardSpeed;
            float _HazardOpacity;
            float _GrainScale;
            float _GrainStrength;
            float _ScanlineDensity;
            float _ScanlineStrength;
            float _VignetteStrength;
            float _ImpactPulse;
            float _UnscaledTime;
            float _MotionScale;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float Hash21(float2 point)
            {
                point = frac(point * float2(123.34, 456.21));
                point += dot(point, point + 45.32);
                return frac(point.x * point.y);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 uv = input.uv;
                float verticalSteel = smoothstep(0.05, 0.92, uv.y);
                fixed3 color = lerp(_BaseColor.rgb, _SecondaryColor.rgb, verticalSteel * 0.72);

                float breath = sin(_UnscaledTime * 1.72) * 0.5 + 0.5;
                color += _AccentColor.rgb * breath * _BreathAmount * _MotionScale;

                float stripeCoordinate = (uv.x * 1.4 + uv.y + _UnscaledTime * _HazardSpeed * _MotionScale) * _HazardScale;
                float stripe = step(0.58, frac(stripeCoordinate));
                float stripeMask = smoothstep(0.05, 0.18, uv.y) * (1.0 - smoothstep(0.26, 0.42, uv.y));
                color = lerp(color, _AccentColor.rgb * 0.42, stripe * stripeMask * _HazardOpacity);

                float grain = Hash21(floor(uv * _GrainScale + _UnscaledTime * 8.0 * _MotionScale)) - 0.5;
                color += grain * _GrainStrength;

                float scanline = sin(uv.y * _ScanlineDensity * 6.28318) * 0.5 + 0.5;
                color -= scanline * _ScanlineStrength;

                float2 centered = uv * 2.0 - 1.0;
                float vignette = saturate(1.0 - dot(centered, centered) * _VignetteStrength);
                color *= lerp(0.58, 1.0, vignette);
                color = lerp(color, _AccentColor.rgb, _ImpactPulse * 0.18);

                return fixed4(saturate(color), _BaseColor.a) * input.color;
            }
            ENDCG
        }
    }
}
