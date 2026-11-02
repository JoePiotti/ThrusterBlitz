Shader "HD2/ChargeSpark"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
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
            Name "ChargeSpark"
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
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float seed : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float seed : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Color;
                output.uv = input.uv;
                output.seed = input.seed;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 p = input.uv * 2.0 - 1.0;
                float r2 = dot(p, p);
                // Hard pinpoint plus a thin glint, cut off before the quad edge.
                half core = exp(-r2 * 46.0);
                half glint = (exp(-abs(p.x) * 22.0) * exp(-p.y * p.y * 14.0)
                    + exp(-abs(p.y) * 22.0) * exp(-p.x * p.x * 14.0)) * exp(-r2 * 4.0);
                half mask = saturate(1.0 - smoothstep(0.35, 0.85, r2));
                half twinkle = lerp(0.2, 1.0, abs(sin(_Time.y * (18.0 + input.seed * 46.0) + input.seed * 6.28318)));
                half glow = (core + glint * 0.45) * mask * input.color.a * twinkle;
                return half4(input.color.rgb * glow * 2.4, 1);
            }
            ENDHLSL
        }
    }
}
