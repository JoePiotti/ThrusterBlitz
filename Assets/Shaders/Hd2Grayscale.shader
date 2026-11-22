Shader "HD2/Grayscale"
{
    // Full-screen black and white, used by Hd2GrayscaleFeature. XR single-pass safe.
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "Grayscale"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half gray = dot(color.rgb, half3(0.299, 0.587, 0.114));
                return half4(gray, gray, gray, color.a);
            }
            ENDHLSL
        }

        Pass
        {
            // Copies the scene depth (in _BlitTexture) into a depth target, so players
            // and bots can be drawn again in color over the gray image, still hidden
            // behind walls.
            Name "DepthCopy"
            ZWrite On
            ZTest Always
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float Frag(Varyings input) : SV_Depth
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).r;
            }
            ENDHLSL
        }
    }
}
