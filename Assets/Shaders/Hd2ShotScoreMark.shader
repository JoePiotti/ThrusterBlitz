Shader "HD2/ShotScoreMark"
{
    Properties
    {
        _MainTex ("Mark", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
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
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -2, -2

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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float radius = length(input.uv * 2.0 - 1.0);
                // Dark center reads on light walls. Bright ring reads on dark walls.
                float core = 1.0 - smoothstep(0.78, 0.86, radius);
                float ring = smoothstep(0.80, 0.86, radius) * (1.0 - smoothstep(0.90, 0.98, radius));
                half glow = max(tex.r, max(tex.g, tex.b));
                half bright = (half)ring * saturate(glow + 0.35h);
                half3 rgb = half3(0.02h, 0.02h, 0.02h) * core;
                rgb = max(rgb, half3(1.0h, 0.96h, 0.82h) * bright);
                rgb *= _Color.rgb;
                half alpha = saturate(max(core * 0.96h, bright));
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
