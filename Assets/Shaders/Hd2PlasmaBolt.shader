Shader "HD2/PlasmaBolt"
{
    Properties
    {
        _Color ("Color", Color) = (0.45, 0.9, 1, 1)
        _Core ("Core", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "PlasmaBolt"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Core;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Unity sphere is 1 unit across. Z runs from the tail to the head.
                float radial = saturate(length(input.positionOS.xy) * 2.0);
                float along = saturate(input.positionOS.z + 0.5);
                float edge = saturate(1.0 - radial);
                float core = pow(edge, 2.2);
                float tail = smoothstep(0.0, 0.45, along);
                float glow = edge * tail;
                half3 color = lerp(_Color.rgb, _Core.rgb, core * lerp(0.35, 1.0, along));
                return half4(color * glow, 1);
            }
            ENDHLSL
        }
    }
}
