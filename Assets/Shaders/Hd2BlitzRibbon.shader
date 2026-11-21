Shader "HD2/BlitzRibbon"
{
    Properties
    {
        _CoreWidth ("Core Width", Range(0.02, 0.6)) = 0.22
        _CoreStrength ("Core Strength", Range(0, 1)) = 0.95
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
            Name "BlitzRibbon"
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
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half _CoreWidth;
                half _CoreStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Across the ribbon (uv.y): -1 at one edge, 0 in the middle, 1 at the other.
                half across = abs(input.uv.y * 2 - 1);
                // Team color fills the band, a white core runs down the middle, and only
                // the outer edges soften to clear.
                half core = exp(-(across * across) / max(0.0001, _CoreWidth * _CoreWidth)) * _CoreStrength;
                half edge = 1 - smoothstep(0.7, 1.0, across);
                half3 color = lerp(input.color.rgb, half3(1, 1, 1), core);
                return half4(color, input.color.a * edge);
            }
            ENDHLSL
        }
    }
}
