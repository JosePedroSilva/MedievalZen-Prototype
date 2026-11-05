// Banks of the endless road's streams (TileStreams.Mesh, second submesh): the wet lip at the water's edge, the bank rising to the
// ground and the strip draped over the ground around it. Opaque, receives shadows, casts none.
//  - Textures are the terrain's own layers, set by the palette menu from WorldPalette.terrainLayers: the lush grass (GroundLook tint),
//    the mud and the dirt, with each layer's tile size and offset. They are sampled with the terrain's UVs (tile-local position
//    + offset) / tile size; the mesh sits at the tile's origin like the terrain, so where the drape meets the ground the grass
//    continues texel for texel (the ground around a stream is painted lush grass for that).
//  - Vertex data (StreamRibbonMeshData): colour r mud (the lip), g wet (darker and a little glossy at the water), b worn path (the
//    ford's footpath, a ford's road bed, a bridge's deck), a drape (1 where the strip lies over the ground: exactly the terrain's
//    lush grass, so it meets the terrain unseen); uv = stream space in metres (x beyond the water's edge, + 100 on the right bank;
//    y along the stream), continuous across tiles.
//  - The bank between the lip and the drape is broken (2026-10-06): patches of bare earth, gravel and darker grass tufts from
//    value noise in stream space, so it no longer reads as a moulded, striped canal.
//  - Lighting as URP Terrain Lit ends: the albedo times the layer tint, metallic 0, smoothness 0 (wet lip excepted), the layers'
//    normal maps in the terrain's tangent frame, UniversalFragmentPBR with main-light shadows, additional lights, SSAO and the
//    SH ambient; Unity fog for the fallback mode. DepthOnly and DepthNormalsOnly passes as the URP 17.3 Unlit / SimpleLit ones.
// SRP Batcher compatible (every property in UnityPerMaterial, the same layout in every pass; one material for every tile).
Shader "MedievalZen/StreamBank"
{
    Properties
    {
        [NoScaleOffset] _GrassMap ("Grass (terrain layer)", 2D) = "grey" {}
        [NoScaleOffset][Normal] _GrassNormal ("Grass normals", 2D) = "bump" {}
        _GrassTint ("Grass tint (layer diffuse remap, linear)", Vector) = (1, 1, 1, 1)
        _GrassTile ("Grass tile size (xy) and offset (zw), metres", Vector) = (2, 2, 0, 0)
        _GrassNormalScale ("Grass normal scale", Float) = 1

        [NoScaleOffset] _MudMap ("Mud (terrain layer)", 2D) = "grey" {}
        [NoScaleOffset][Normal] _MudNormal ("Mud normals", 2D) = "bump" {}
        _MudTint ("Mud tint (linear)", Vector) = (1, 1, 1, 1)
        _MudTile ("Mud tile size and offset, metres", Vector) = (2, 2, 0, 0)
        _MudNormalScale ("Mud normal scale", Float) = 1

        [NoScaleOffset] _DirtMap ("Dirt (terrain layer)", 2D) = "grey" {}
        _DirtTint ("Dirt tint (linear)", Vector) = (1, 1, 1, 1)
        _DirtTile ("Dirt tile size and offset, metres", Vector) = (2, 2, 0, 0)

        _EarthColor ("Bare earth multiplier on the mud layer (linear)", Vector) = (2.4, 1.9, 1.35, 1)
        _Broken ("How broken the bank is", Range(0, 1)) = 0.8
        _WetDarken ("Albedo where wet", Range(0, 1)) = 0.62
        _WetSmoothness ("Smoothness where wet", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _GrassTint;
            float4 _GrassTile;
            half _GrassNormalScale;
            half4 _MudTint;
            float4 _MudTile;
            half _MudNormalScale;
            half4 _DirtTint;
            float4 _DirtTile;
            half4 _EarthColor;
            half _Broken;
            half _WetDarken;
            half _WetSmoothness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "StreamBank"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GrassMap); SAMPLER(sampler_GrassMap);
            TEXTURE2D(_GrassNormal); SAMPLER(sampler_GrassNormal);
            TEXTURE2D(_MudMap); SAMPLER(sampler_MudMap);
            TEXTURE2D(_MudNormal);
            TEXTURE2D(_DirtMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;   // tile-local: the terrain layers' UVs
                float2 stream : TEXCOORD6;       // stream space, metres
                float3 normalWS : TEXCOORD2;
                half4 data : TEXCOORD3;          // mud, wet, path
                half fogFactor : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
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
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.data = input.color;
                output.stream = float2(input.uv.x, input.uv.y * 6.0);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                output.shadowCoord = GetShadowCoord(position);
                return output;
            }

            // Global (Shader.SetGlobalFloat, the review tool): 0 the normal picture, 1 flags for non-finite terms over a map of
            // the bank's parts, 2 the albedo unlit. Not a material property, so the shader stays SRP Batcher compatible.
            float _MZDebugView;

            float BankHash(float2 cell)
            {
                float3 p = frac(float3(cell.xyx) * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float BankNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(BankHash(i), BankHash(i + float2(1.0, 0.0)), u.x), lerp(BankHash(i + float2(0.0, 1.0)), BankHash(i + float2(1.0, 1.0)), u.x), u.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Every term is tested for NaN and infinity on its own (bit tests) and replaced by a plain value: one bad term
                // must not poison the mix (0 x infinity is NaN, so even a term with no weight does). The drawn banks' albedo was
                // non-finite on the drape strip on the user's GPU and the cause was not found by reading (2026-10-06): debug view 1
                // paints which term it is.
                half4 data = input.data;
                bool badData = AnyIsNaN(data.rgb) || AnyIsInf(data.rgb) || IsNaN(data.a) || IsInf(data.a);
                if (badData) data = half4(0.0, 0.0, 0.0, 1.0);
                data = saturate(data);
                float2 streamUV = input.stream;
                bool badStream = AnyIsNaN(streamUV) || AnyIsInf(streamUV);
                if (badStream) streamUV = float2(0.0, 0.0);
                streamUV = clamp(streamUV, -100000.0, 100000.0);
                half mud = data.r;
                half wet = data.g;
                half path = data.b;
                half drape = data.a;
                float2 grassUV = (input.positionOS.xz + _GrassTile.zw) / _GrassTile.xy;
                float2 mudUV = (input.positionOS.xz + _MudTile.zw) / _MudTile.xy;
                float2 dirtUV = (input.positionOS.xz + _DirtTile.zw) / _DirtTile.xy;

                half3 grass = SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, grassUV).rgb * _GrassTint.rgb;
                half3 earth = SAMPLE_TEXTURE2D(_MudMap, sampler_MudMap, mudUV).rgb * _MudTint.rgb;
                half3 dirt = SAMPLE_TEXTURE2D(_DirtMap, sampler_MudMap, dirtUV).rgb * _DirtTint.rgb;
                // The bank between lip and drape: earth, gravel and grass tufts in patches (stream space, wrapped for precision).
                float2 q = float2(streamUV.x, streamUV.y - 2048.0 * floor(streamUV.y / 2048.0));
                half n1 = (half)BankNoise(q * float2(1.6, 0.9));
                half n2 = (half)BankNoise(q * float2(3.7, 2.9) + 31.7);
                bool badGrass = AnyIsNaN(grass) || AnyIsInf(grass);
                bool badEarth = AnyIsNaN(earth) || AnyIsInf(earth);
                bool badDirt = AnyIsNaN(dirt) || AnyIsInf(dirt);
                bool badNoise = IsNaN(n1) || IsInf(n1) || IsNaN(n2) || IsInf(n2);
                if (badGrass) grass = half3(0.155, 0.222, 0.009);
                if (badEarth) earth = half3(0.056, 0.040, 0.025);
                if (badDirt) dirt = half3(0.32, 0.24, 0.16);
                if (badNoise)
                {
                    n1 = 0.5;
                    n2 = 0.5;
                }

                n1 = saturate(n1);
                n2 = saturate(n2);
                half bank = (1.0 - drape) * (1.0 - mud) * _Broken;
                half bare = smoothstep(0.45, 0.7, n1 * 0.75 + n2 * 0.25) * bank;
                half gravel = smoothstep(0.62, 0.8, n2) * bank * 0.8;
                half3 tufts = grass * lerp(0.7, 1.05, n2);
                half3 ground = lerp(grass, tufts, bank);
                ground = lerp(ground, earth * _EarthColor.rgb, bare);
                ground = lerp(ground, dirt, gravel);
                // The lip: wet mud with flecks of gravel.
                half3 lip = lerp(earth, dirt * 0.8, smoothstep(0.6, 0.85, n2) * 0.6);
                half3 albedo = lerp(lerp(ground, lip, mud), dirt, path);
                albedo *= lerp(1.0, _WetDarken, wet);

                half3 nGrass = UnpackNormalScale(SAMPLE_TEXTURE2D(_GrassNormal, sampler_GrassNormal, grassUV), _GrassNormalScale);
                half3 nMud = UnpackNormalScale(SAMPLE_TEXTURE2D(_MudNormal, sampler_GrassNormal, mudUV), _MudNormalScale);
                half3 normalTS = SafeNormalize(lerp(nGrass, nMud, max(mud, bare)));

                // The terrain's tangent frame (URP Terrain Lit, per-pixel normals): tangent = cross(z, normal). The reference axis
                // must never be parallel to the normal: a vertical skirt facing along z made a zero tangent, NaN normals, and
                // bloom turned the NaN pixels into a white blob covering half the screen (review tour 2026-10-05).
                float3 geometric = SafeNormalize(input.normalWS);
                float3 axis = abs(geometric.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(0.0, 0.0, 1.0);
                float3 tangentWS = SafeNormalize(cross(axis, geometric));
                float3 bitangentWS = cross(geometric, tangentWS);
                float3 normalWS = SafeNormalize(TransformTangentToWorld(normalTS, half3x3(-tangentWS, bitangentWS, geometric)));
                bool badNormal = AnyIsNaN(normalWS) || AnyIsInf(normalWS) || dot(normalWS, normalWS) < 0.25;
                bool badAlbedo = AnyIsNaN(albedo) || AnyIsInf(albedo);
                if (_MZDebugView > 0.5)
                {
                    // Where each part of the bank is (red the mud lip, green the drape over the ground, blue the worn path), and
                    // over it a solid colour for the first non-finite term found.
                    float3 view = float3(mud, drape, path) * 0.45 + 0.05;
                    if (_MZDebugView > 1.5) view = badAlbedo ? float3(0.0, 0.0, 0.0) : saturate(albedo);
                    if (badStream) view = float3(0.9, 0.0, 0.9);        // magenta: the stream coordinates (uv)
                    else if (badData) view = float3(0.0, 0.9, 0.9);     // cyan: the vertex colour
                    else if (badGrass) view = float3(0.9, 0.0, 0.0);    // red: the grass sample
                    else if (badEarth) view = float3(0.9, 0.45, 0.0);   // orange: the mud sample
                    else if (badDirt) view = float3(0.9, 0.9, 0.0);     // yellow: the dirt sample
                    else if (badNoise) view = float3(0.0, 0.0, 0.9);    // blue: the noise
                    else if (badAlbedo) view = float3(0.9, 0.9, 0.9);   // white: the mix itself
                    else if (badNormal) view = float3(0.0, 0.9, 0.0);   // green: the normal
                    return half4(view, 1.0);
                }

                if (badNormal) normalWS = float3(0.0, 1.0, 0.0);
                if (badAlbedo) albedo = half3(0.12, 0.11, 0.07);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #else
                inputData.shadowCoord = float4(0.0, 0.0, 0.0, 0.0);
            #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.vertexLighting = half3(0.0, 0.0, 0.0);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1.0, 1.0, 1.0, 1.0);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = 0.0;
                surface.specular = half3(0.0, 0.0, 0.0);
                surface.smoothness = wet * _WetSmoothness;
                surface.normalTS = normalTS;
                surface.emission = half3(0.0, 0.0, 0.0);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                // Bit tests, not min/max: a NaN pixel shows black and bloom turns it into a white glow (review tours 2026-10-06).
                float3 safe = color.rgb;
                if (AnyIsNaN(safe) || AnyIsInf(safe)) safe = float3(0.05, 0.05, 0.03);
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
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
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
                float3 normalWS = SafeNormalize(input.normalWS);
                if (AnyIsNaN(normalWS) || AnyIsInf(normalWS) || dot(normalWS, normalWS) < 0.25) normalWS = float3(0.0, 1.0, 0.0);
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
