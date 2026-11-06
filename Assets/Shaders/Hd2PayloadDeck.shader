Shader "HD2/PayloadDeck"
{
    Properties
    {
        _Origin ("Deck min XZ", Vector) = (-4, -8.61, 0, 0)
        _Size ("Deck size XZ", Vector) = (8, 20, 0, 0)
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
            Name "PayloadDeck"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalOS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Origin;
                float4 _Size;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float across = saturate((input.positionOS.x - _Origin.x) / max(_Size.x, 0.001));
                float along = saturate((input.positionOS.z - _Origin.y) / max(_Size.y, 0.001));
                float edge = min(across, 1.0 - across);

                half3 black = half3(0.055, 0.062, 0.075);
                half3 dashColor = half3(0.78, 0.82, 0.86);
                half3 blue = half3(0.0, 0.34, 0.82);
                half3 blueDark = half3(0.0, 0.15, 0.42);
                half3 cyan = half3(0.45, 0.95, 1.0);

                float rail = step(edge, 0.075);
                float cyanLine = step(edge, 0.095) * step(0.075, edge);
                float lane = min(abs(across - 0.34), abs(across - 0.66));
                float onDash = step(lane, 0.012) * step(0.4, frac(along * 11.0));
                float seam = step(0.97, frac(across * 6.0)) + step(0.97, frac(along * 14.0));

                half3 deck = black;
                deck = lerp(deck, half3(0.09, 0.1, 0.12), saturate(seam));
                deck = lerp(deck, dashColor, onDash);
                deck = lerp(deck, cyan, cyanLine);
                deck = lerp(deck, lerp(blueDark, blue, 0.7), rail);

                float stripe = step(0.5, frac(input.positionOS.z * 0.85 + input.positionOS.y * 1.4));
                half3 side = lerp(blueDark, half3(0.86, 0.9, 0.94), stripe);
                half ny = input.normalOS.y;
                half3 albedo = ny > 0.5 ? deck : (ny < -0.5 ? half3(0.02, 0.025, 0.03) : side);

                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                Light light = GetMainLight();
                float shade = 0.28 + 0.72 * saturate(dot(normalWS, light.direction));
                return half4(albedo * shade, 1);
            }
            ENDHLSL
        }
    }
}
