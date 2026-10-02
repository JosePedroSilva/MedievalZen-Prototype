// Additive, texture-less glow for small particles (fireflies). The particle quad's UVs give a soft round halo with a
// bright core; the particle vertex colour tints it and its alpha fades it. HDR output so bloom picks it up.
Shader "MedievalZen/FireflyGlow"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 4
        _Core ("Core size", Range(1, 40)) = 14
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Core;
            CBUFFER_END

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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float r2 = dot(p, p);
                float halo = saturate(1.0 - r2);
                halo = halo * halo * halo;
                float core = saturate(1.0 - r2 * _Core);
                float a = (halo * 0.5 + core) * input.color.a;
                return half4(input.color.rgb * (a * _Intensity), 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
