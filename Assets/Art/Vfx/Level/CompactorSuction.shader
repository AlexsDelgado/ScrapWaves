Shader "ScrapWaves/Level/Compactor Suction"
{
    Properties
    {
        _Activity("Suction Activity", Range(0, 1)) = 0
        _FlowColor("Air Flow Color", Color) = (0.23, 0.24, 0.23, 1)
        _Speed("Flow Speed", Range(0, 5)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
        }

        Cull Off
        ZWrite On
        ZTest LEqual
        Blend One Zero

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _FlowColor;
            float _Activity;
            float _Speed;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = input.uv;
            return output;
        }

        float Hash(float value)
        {
            return frac(sin(value * 127.1 + 311.7) * 43758.5453);
        }

        // UV.x is distance from one jamb, UV.y runs along that jamb. Small
        // independent wisps accelerate inward, then disperse near the edge.
        float EdgeDraft(float2 uv, float time, float seed)
        {
            float flow = 0.0;
            [unroll]
            for (int i = 0; i < 3; i++)
            {
                float strandSeed = seed + i * 19.31;
                float clock = time * lerp(0.25, 0.43, Hash(strandSeed)) + Hash(strandSeed + 2.0) * 8.0;
                float cycle = floor(clock);
                float age = frac(clock);
                float random = Hash(strandSeed + cycle * 7.13);
                float along = (i + lerp(0.2, 0.8, random)) / 3.0;
                float depth = lerp(0.012, 0.24, age * age);
                float length = lerp(0.025, 0.065, Hash(strandSeed + cycle + 8.0)) * (0.7 + age);
                float width = lerp(0.009, 0.022, random) * (1.0 - age * 0.45);

                // Uneven curved filaments suggest a draft caught at the frame,
                // with no radial spokes, rings, or common convergence point.
                float bend = uv.x * lerp(-0.23, 0.23, Hash(strandSeed + 14.0));
                bend += 0.012 * sin(uv.x * 35.0 + strandSeed + time * 0.65);
                width *= 0.8 + 0.2 * sin(uv.x * 71.0 + strandSeed);
                float alongDistance = (uv.y - along - bend) / max(width, fwidth(uv.y));
                float depthDistance = (uv.x - depth) / length;
                float filament = exp2(-3.0 * alongDistance * alongDistance - 2.0 * depthDistance * depthDistance);
                float haze = exp2(-0.55 * alongDistance * alongDistance - 3.0 * depthDistance * depthDistance) * 0.18;
                float lifetime = smoothstep(0.0, 0.15, age) * (1.0 - smoothstep(0.60, 1.0, age));
                // Some lanes skip a cycle, avoiding a continuously lit border.
                flow += (filament + haze) * lifetime * smoothstep(0.2, 0.5, random);
            }
            float edgeFade = smoothstep(0.0, 0.018, uv.x) * (1.0 - smoothstep(0.17, 0.30, uv.x));
            float cornerFade = smoothstep(0.02, 0.10, uv.y) * (1.0 - smoothstep(0.90, 0.98, uv.y));
            return flow * edgeFade * cornerFade;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float time = _Time.y * max(_Speed, 0.0);
            float2 uv = input.uv;
            float flow = EdgeDraft(uv, time, 4.0);
            flow += EdgeDraft(float2(1.0 - uv.x, 1.0 - uv.y), time, 53.0);
            flow += EdgeDraft(float2(uv.y, 1.0 - uv.x), time, 97.0) * 0.65;
            flow += EdgeDraft(float2(1.0 - uv.y, uv.x), time, 151.0) * 0.45;
            float intensity = saturate(_Activity) * saturate(flow) * 0.9;
            return half4(saturate(_FlowColor.rgb) * intensity, 1.0);
        }

        half4 DepthFrag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return input.positionCS.z;
        }

        half4 DepthNormalsFrag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(frontFace, 1.0, -1.0);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormal = PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "Suction"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }

    Fallback Off
}
