Shader "HD2/SpawnField"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.25, 0.82, 1, 0.62)
        [HDR] _EmissionColor ("Emission", Color) = (0.35, 1.15, 1.8, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SpawnField"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Off

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
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
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

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float scan = 0.72 + 0.28 * sin(input.uv.y * 36.0 + _Time.y * 1.6);
                float edgeX = smoothstep(0.12, 0.0, input.uv.x) + smoothstep(0.88, 1.0, input.uv.x);
                float edgeY = smoothstep(0.10, 0.0, input.uv.y) + smoothstep(0.90, 1.0, input.uv.y);
                float edge = saturate(edgeX + edgeY);

                half3 rgb = _BaseColor.rgb * scan + _EmissionColor.rgb * (0.85 + edge);
                half alpha = saturate(_BaseColor.a + edge * 0.35);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
