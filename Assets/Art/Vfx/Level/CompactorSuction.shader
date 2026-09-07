Shader "ScrapWaves/Level/Compactor Suction"
{
    Properties
    {
        _Activity("Suction Activity", Range(0, 1)) = 0
        _FlowColor("Air Flow Color", Color) = (0.22, 0.25, 0.27, 1)
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

        float InwardStreaks(float angle, float radius, float time, float lanes, float seed)
        {
            float laneCoordinate = frac(angle) * lanes;
            float lane = floor(laneCoordinate);
            float random = Hash(lane + seed);
            float center = lerp(0.23, 0.77, random);
            float width = lerp(0.035, 0.12, Hash(lane + seed + 17.0));
            float distanceFromLane = abs(frac(laneCoordinate) - center);
            float antialias = max(fwidth(laneCoordinate), 0.005);
            float strand = 1.0 - smoothstep(width, width + antialias, distanceFromLane);

            // Increasing time moves each pulse toward smaller radii: into the opening.
            float phase = frac(radius * 1.9 + time * lerp(0.62, 1.08, random) + random);
            float pulse = smoothstep(0.02, 0.10, phase) * (1.0 - smoothstep(0.13, 0.48, phase));
            return strand * pulse * lerp(0.35, 1.0, random);
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 position = input.uv * 2.0 - 1.0;
            float radius = length(position);
            float angle = atan2(position.y, position.x + 0.00001) * (1.0 / TWO_PI) + 0.5;
            float time = _Time.y * max(_Speed, 0.0);

            float flow = InwardStreaks(angle, radius, time, 28.0, 4.0);
            flow += InwardStreaks(angle + 0.137, radius, time * 0.73, 19.0, 71.0) * 0.32;

            // Keep the center dark and fade at the rectangular aperture's edges.
            float centerFade = smoothstep(0.10, 0.38, radius);
            float edgeFade = 1.0 - smoothstep(0.78, 1.0, max(abs(position.x), abs(position.y)));
            float intensity = saturate(_Activity) * centerFade * edgeFade * saturate(flow);
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
