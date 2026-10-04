Shader "HD2/WallGrid"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 0.82, 0.08, 1)
        _LineColor ("Line Color", Color) = (1, 1, 1, 1)
        _GridSpacing ("Grid Spacing", Float) = 1
        _LineWidth ("Line Width", Float) = 0.04
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "WallGrid"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
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
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _LineColor;
                float _GridSpacing;
                float _LineWidth;
            CBUFFER_END

            float GridLine(float coord, float spacing, float width)
            {
                float dist = abs(frac(coord / spacing) - 0.5) * spacing;
                return dist < width ? 1.0 : 0.0;
            }

            float GridPlane(float2 uv, float spacing, float width)
            {
                return max(GridLine(uv.x, spacing, width), GridLine(uv.y, spacing, width));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 n = abs(normalize(input.normalWS));
                float spacing = max(_GridSpacing, 0.001);
                float width = max(_LineWidth, 0.001);
                float3 p = input.positionWS;
                float grid = GridPlane(p.yz, spacing, width) * n.x
                    + GridPlane(p.xz, spacing, width) * n.y
                    + GridPlane(p.xy, spacing, width) * n.z;
                grid = saturate(grid);
                float3 color = lerp(_BaseColor.rgb, _LineColor.rgb, grid);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
