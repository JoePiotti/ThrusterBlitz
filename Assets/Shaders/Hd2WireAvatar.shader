Shader "HD2/WireAvatar"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.2, 0.92, 0.28, 0.2)
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
            Name "WireAvatar"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 bary : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.bary = float3(input.uv, saturate(1.0 - input.uv.x - input.uv.y));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 bary = input.bary;
                float3 width = fwidth(bary);
                float edge = min(bary.x / max(width.x, 1e-5), min(bary.y / max(width.y, 1e-5), bary.z / max(width.z, 1e-5)));
                float wire = 1.0 - smoothstep(0.6, 1.6, edge);
                clip(wire - 0.15);
                return half4(_BaseColor.rgb, _BaseColor.a * wire);
            }
            ENDHLSL
        }
    }
}
