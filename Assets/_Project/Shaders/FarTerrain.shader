// Distant ground, tree impostors, rocks and houses of the endless road (EndlessTileBuilder far tiles, FarTileMeshData), in the
// same faceted low-poly style as the near world. Opaque.
//  - Flat shading: every triangle's normal is the cross product of the screen-space derivatives of its (camera-relative) position,
//    turned toward the camera, so no normal is stored and every face is one plane of light, in this pass and in the depth-normals
//    pass (SSAO).
//  - Flat colour: the vertex colour (sRGB-encoded in the vertex, decoded here) is not interpolated: each triangle takes its
//    provoking vertex's colour. The mesh stores every triangle's colour on both its first and its last vertex, so either
//    convention (D3D, Vulkan, Metal: first; OpenGL: last) gives the same flat colour.
//  - Lighting: URP's own UniversalFragmentPBR with metallic 0 and smoothness _Smoothness (default 0, as every terrain layer of the
//    project), occlusion 1, ambient from the probe SH, SSAO when enabled. That is the function URP Terrain Lit ends in for its
//    blended layers (albedo x 0.96 diffuse, the roughness-1 specular term and environment reflection), so a far slope is lit exactly
//    as the same slope near by under the same sun. No shadows are received or cast (far tiles lie beyond the shadow distance).
//  - Foliage light (distant-tree pass, 2026-10-06): faces whose vertex alpha is 0 (the leaf crowns of tree impostors) take the sun
//    half-Lambert while it stands high, so a backlit crown is not one flat shade; every other face (alpha 255) is lit as above.
//    Instanced tree impostors also get a ±4 % tone per tree, hashed from their model matrix's rotation and scale.
//  - Seasons (2026-10-06): the alpha also carries a season class (SeasonLook.Alpha); the vertex colour is multiplied by the global
//    tint of its class (_MZSeasonTints, 1 in autumn and for class 0, i.e. alpha 0 and 255 as before).
//  - Unity fog for the fallback fog mode; the atmosphere's full-screen fog works from the depth this writes.
// DepthOnly and DepthNormalsOnly passes as URP 17.3's Unlit / SimpleLit. SRP Batcher compatible (every property in UnityPerMaterial,
// the same layout in every pass; one material shared by every far tile).
Shader "MedievalZen/FarTerrain"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness (terrain layers: 0)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Smoothness;
        CBUFFER_END

        // The plane of the current triangle, from the derivatives of the camera-relative position, facing the camera.
        float3 FlatNormalWS(float3 relativeWS)
        {
            float3 n = cross(ddx(relativeWS), ddy(relativeWS));
            float len = length(n);
            n = len > 1e-12 ? n / len : float3(0.0, 1.0, 0.0);
            // relativeWS points from the camera to the surface: a visible face's normal points back toward the camera.
            return dot(n, relativeWS) > 0.0 ? -n : n;
        }
        ENDHLSL

        Pass
        {
            Name "FarTerrain"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            // Seasons (2026-10-06): one tint per season class (SeasonLook), set by SeasonPainter when the year moves; exactly 1 in
            // autumn. Globals, outside UnityPerMaterial. Unset (_MZSeasonOn 0, e.g. in the editor before Play) every tint is 1.
            float4 _MZSeasonTints[16];
            float _MZSeasonOn;

            // The season tint of a vertex alpha (0..1): a leaf crown is 8·class (below 128), any other face 255 − 8·class; class 0 untinted.
            half3 SeasonTint(half alpha, bool leafFace)
            {
                float a = alpha * 255.0;
                float k = leafFace ? a : 255.0 - a;
                int cls = (int)clamp(floor(k / 8.0 + 0.5), 0.0, 15.0);
                if (cls == 0 || _MZSeasonOn < 0.5) return half3(1.0, 1.0, 1.0);
                float3 tint = _MZSeasonTints[cls].rgb;
                if (AnyIsNaN(tint) || AnyIsInf(tint)) return half3(1.0, 1.0, 1.0);
                return half3(clamp(tint, 0.0, 4.0));
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 relativeWS : TEXCOORD1;          // position relative to the camera (precise derivatives)
                nointerpolation half3 albedo : TEXCOORD2; // the triangle's flat colour (linear)
                half fogFactor : TEXCOORD3;
                nointerpolation half foliage : TEXCOORD4; // 1 on a leaf crown's faces (vertex alpha 0), 0 elsewhere
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.relativeWS = position.positionWS - GetCameraPositionWS();
                // A leaf crown's alpha is below 128 (0 before seasons; 8·class with them), every other face's 135..255.
                bool leafFace = input.color.a * 255.0 < 127.5;
                output.albedo = SRGBToLinear(input.color.rgb) * SeasonTint(input.color.a, leafFace);
                output.foliage = leafFace ? 1.0 : 0.0;
            #if defined(UNITY_INSTANCING_ENABLED)
                // Instanced tree impostors: each tree its own tone (±4 %, small enough not to jump at the 320 m swap), hashed from its turn and scale (the model matrix's first
                // column without the translation, so a floating-origin shift never changes it). Far tiles bake theirs per vertex.
                float4x4 model = UNITY_MATRIX_M;
                float2 key = float2(model._m00, model._m20) * 7.31 + float2(model._m11, 0.0) * 3.17;
                float h = frac(sin(dot(key, float2(12.9898, 78.233))) * 43758.5453);
                output.albedo *= 0.96 + 0.08 * h;
            #endif
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = FlatNormalWS(input.relativeWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = SafeNormalize(-input.relativeWS);
                inputData.shadowCoord = float4(0.0, 0.0, 0.0, 0.0);
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.vertexLighting = half3(0.0, 0.0, 0.0);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1.0, 1.0, 1.0, 1.0);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = input.albedo * _BaseColor.rgb;
                surface.metallic = 0.0;
                surface.specular = half3(0.0, 0.0, 0.0);
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0.0, 0.0, 1.0);
                surface.emission = half3(0.0, 0.0, 0.0);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surface);
                // Foliage light (leaf crowns only): a crown of leaf cards has leaves facing every way, so a high sun lights some of
                // them on every side. Its faces take the sun half-Lambert, (N·L + w) / (1 + w), instead of max(N·L, 0); w fades from
                // 1 with the sun above 30° to 0 at the horizon (a low sun lights only its own side). Added as the difference on top of
                // URP's diffuse (albedo × 0.96 × light × N·L for metallic 0), so everything else stays URP's own.
                if (input.foliage > 0.5)
                {
                    Light sun = GetMainLight();
                    float nl = dot(normalWS, sun.direction);
                    float w = saturate(2.0 * sun.direction.y);
                    float wrapped = saturate((nl + w) / (1.0 + w));
                    color.rgb += surface.albedo * 0.96 * sun.color * sun.distanceAttenuation * max(wrapped - saturate(nl), 0.0);
                }
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                // Bit tests, not max(): a NaN pixel shows black and bloom turns it into a white glow (review tours 2026-10-06).
                float3 safe = color.rgb;
                if (AnyIsNaN(safe) || AnyIsInf(safe)) safe = float3(0.06, 0.07, 0.04);
                return half4(clamp(safe, 0.0, 4.0), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT // forward-only variant
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 relativeWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.relativeWS = positionWS - GetCameraPositionWS();
                return output;
            }

            void DepthNormalsFrag(Varyings input
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWS = FlatNormalWS(input.relativeWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = half3(PackFloat2To888(remappedOctNormalWS));
                outNormalWS = half4(packedNormalWS, 0.0);
            #else
                outNormalWS = half4(normalWS, 0.0);
            #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
