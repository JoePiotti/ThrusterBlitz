Shader "HD2/BlasterBolt"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.08, 0.03, 1)
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
            Name "BlasterBolt"
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
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
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
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // The mesh is a 0.5 m cylinder, 2 cm radius. Fragments land on the
                // surface, so a radial fade that hits zero there deletes the shaft
                // and leaves the end cap looking like a ball.
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float facing = saturate(dot(normal, view));
                half3 color = lerp(_Color.rgb, _Core.rgb, pow(facing, 1.2));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
