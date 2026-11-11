Shader "HD2/ShieldRipple"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.45, 0.85, 1, 1)
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
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
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
                float2 uv : TEXCOORD0;
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
                output.uv = input.uv;
                return output;
            }

            half Band(float radius, float edge, half width)
            {
                half distance = abs(radius - edge);
                return saturate(1.0h - distance / width);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 centered = input.uv * 2.0 - 1.0;
                float radius = length(centered);
                if (radius > 1.0)
                    return half4(0, 0, 0, 0);

                half wave = Band(radius, 0.74, 0.09h);
                half trail = Band(radius, 0.46, 0.07h) * 0.55h;
                half wash = saturate(1.0h - (half)radius) * 0.16h;
                half alpha = (wave + trail + wash) * _BaseColor.a;
                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
