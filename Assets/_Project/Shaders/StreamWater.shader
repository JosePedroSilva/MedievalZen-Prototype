// Water of the endless road's streams (TileStreams.Mesh, first submesh). Transparent, lit by a small stylised model, no texture.
//  - Vertex data (StreamRibbonMeshData): uv.x across the stream -1 (left edge) .. 1 (right edge), uv.y along it in metres / 6
//    (continuous from the source, so the pattern never jumps at a tile edge or when the floating origin moves); colour r depth
//    toward the middle (0 at the edges), g flow speed, b foam (riffles, around rocks and stepping stones, a ford's thin water);
//    tangent = flow direction.
//  - Look (2026-10-06, after the first review showed a pale opaque sheet that mirrored the horizon): a colour of its own, clear
//    olive-brown at the edges where the bed shows (low alpha) to a dark green-blue in the middle; streaks stretched along the flow
//    and scrolling downstream as colour, not only as normals; foam at the edges, at rocks and riffles, broken by the same noise.
//  - Lighting: no PBR and no reflection probe. Diffuse = albedo × (SH ambient + main light × shadow); a modest sky tint by a
//    clamped Fresnel term from the SH ambient in the reflected direction (so it follows the day cycle's ambient and darkens at
//    night) and a small, clamped sun glint. No emission: it never glows. Output clamped before fog.
//  - Fog: Unity fog through MixFog; the atmosphere's full-screen fog is applied after the transparents from the opaque depth.
// SRP Batcher compatible (every property in UnityPerMaterial). No depth write, no shadow casting, no depth/normals passes.
Shader "MedievalZen/StreamWater"
{
    Properties
    {
        _ShallowColor ("Shallow colour (bed showing)", Color) = (0.13, 0.15, 0.09, 1)
        _DeepColor ("Deep colour", Color) = (0.018, 0.048, 0.050, 1)
        _StreakColor ("Streak colour (moving bands)", Color) = (0.10, 0.16, 0.15, 1)
        _FoamColor ("Foam colour", Color) = (0.72, 0.75, 0.72, 1)
        _EdgeAlpha ("Opacity at the edges", Range(0, 1)) = 0.32
        _CentreAlpha ("Opacity in the middle", Range(0, 1)) = 0.9
        _SkyTint ("Sky tint at grazing view", Range(0, 1)) = 0.32
        _Glint ("Sun glint", Range(0, 2)) = 0.6
        _FlowSpeed ("Flow speed (m/s at full flow)", Range(0, 3)) = 0.9
        _RippleScale ("Ripple size (m)", Range(0.1, 4)) = 0.9
        _RippleStrength ("Ripple strength", Range(0, 2)) = 0.55
        _StreakStrength ("Streak strength", Range(0, 1)) = 0.55
        _EdgeFoam ("Foam along the edges", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Name "StreamWater"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _StreakColor;
                half4 _FoamColor;
                half _EdgeAlpha;
                half _CentreAlpha;
                half _SkyTint;
                half _Glint;
                half _FlowSpeed;
                half _RippleScale;
                half _RippleStrength;
                half _StreakStrength;
                half _EdgeFoam;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 flowWS : TEXCOORD1;      // unit flow direction (world)
                float2 uv : TEXCOORD2;
                half4 data : TEXCOORD3;         // vertex colour: depth, flow, foam
                half fogFactor : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Integer-lattice value noise (as the atmosphere's fog).
            // Global (the review tool): 0 the normal picture, 1 and 2 flags for non-finite terms over depth / flow / foam.
            float _MZDebugView;

            float Hash21(float2 cell)
            {
                float3 p = frac(float3(cell.xyx) * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // The along coordinate is wrapped every 4096 m so the noise lattice stays in float precision on long streams.
            float WrapAlong(float y) { return y - 4096.0 * floor(y / 4096.0); }

            // Ripple height at stream-space point p (metres across, metres along), flowing downstream.
            float Ripples(float2 p, float speed)
            {
                float t = _Time.y;
                p.y = WrapAlong(p.y);
                float scale = max(_RippleScale, 0.1);
                float a = ValueNoise(float2(p.x * 1.7, (p.y - t * speed) * 0.9) / scale);
                float b = ValueNoise(float2(p.x * 2.9 + 17.3, (p.y - t * speed * 1.37) * 1.6) / scale);
                return a * 0.62 + b * 0.38;
            }

            // Streaks: noise stretched along the flow (about 0.5 m across, 4 m along), scrolling with the water: 0..1.
            float Streaks(float2 p, float speed)
            {
                float t = _Time.y;
                float along = WrapAlong(p.y) - t * speed;
                float a = ValueNoise(float2(p.x * 2.2 + 3.1, along * 0.25));
                float b = ValueNoise(float2(p.x * 4.1 - 7.7, along * 0.55 + 11.0));
                return saturate(a * 0.65 + b * 0.35);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.flowWS = SafeNormalize(TransformObjectToWorldDir(input.tangentOS.xyz));   // a zero tangent must not make NaN
                output.uv = input.uv;
                output.data = input.color;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                output.shadowCoord = GetShadowCoord(position);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half depth = input.data.r;
                half flow = input.data.g;
                half foamAmount = input.data.b;
                float across = input.uv.x;
                // Stream space in metres: across from the centre line (about 1.6 m to an edge), along from the source.
                float2 p = float2(across * 1.6, input.uv.y * 6.0);
                float speed = _FlowSpeed * flow;

                // Ripple normal from the gradient of the ripple height.
                const float e = 0.08;
                float h = Ripples(p, speed);
                float hx = Ripples(p + float2(e, 0.0), speed);
                float hy = Ripples(p + float2(0.0, e), speed);
                float2 slope = float2(hx - h, hy - h) * (1.0 / e) * (_RippleStrength * (0.35 + 0.65 * flow)) * 0.12;
                float3 up = float3(0.0, 1.0, 0.0);
                float3 flowDir = SafeNormalize(float3(input.flowWS.x, 0.0, input.flowWS.z) + float3(0.0, 0.0, 1e-5));
                float3 right = cross(up, flowDir);
                float3 normalWS = SafeNormalize(up - right * slope.x - flowDir * slope.y);
                // Bit tests, not min/max/saturate: those do not strip a NaN on every GPU (white-outs at streams on an AMD card,
                // review tours 2026-10-06).
                bool badNormal = AnyIsNaN(normalWS) || AnyIsInf(normalWS);
                if (badNormal) normalWS = up;

                // Body colour: the bed showing at the edges, dark green-blue in the middle, moving streaks along the flow
                // (stronger where the water runs; pools stay calm and dark, riffles lighter).
                half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depth * 1.25));
                half streak = (half)Streaks(p, speed);
                half streakWeight = _StreakStrength * (0.25 + 0.75 * flow) * smoothstep(0.35, 0.8, streak);
                body = lerp(body, _StreakColor.rgb, streakWeight * (0.4 + 0.6 * saturate(depth * 2.0)));
                body = lerp(body, _StreakColor.rgb * 1.25, saturate(foamAmount) * 0.35);

                // Foam: along the edges and where the stream asks for it (riffles, rocks), broken up by the moving ripples.
                half edge = smoothstep(0.78, 1.0, abs(across)) * _EdgeFoam;
                half want = saturate(max(edge, foamAmount));
                half foam = saturate((h + 0.35 * hx + 0.25 * streak - (1.3 - want)) * 4.0) * want;

                // Light: SH ambient (the day cycle's), the main light with its shadows; no reflection probe.
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #else
                float4 shadowCoord = float4(0.0, 0.0, 0.0, 0.0);
            #endif
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 sun = mainLight.color * shadow;
                half3 ambient = max(SampleSH(normalWS), half3(0.0, 0.0, 0.0));
                half ndl = saturate(dot(normalWS, mainLight.direction));
                half3 diffuseLight = ambient + sun * (0.45 + 0.55 * ndl);
                half3 color = body * diffuseLight;

                // Sky tint: the ambient seen in the reflected direction, by a clamped Fresnel term (grazing views see more of it).
                float3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half nov = saturate(dot(normalWS, viewWS));
                // No pow(): pow(0, n) is not zero on every driver.
                half grazing = 1.0 - nov;
                grazing *= grazing;
                grazing *= grazing;
                half fresnel = _SkyTint * (0.08 + 0.92 * grazing) * (1.0 - foam);
                half3 sky = max(SampleSH(reflect(-viewWS, normalWS)), half3(0.0, 0.0, 0.0));
                color = lerp(color, sky, saturate(fresnel));

                // A small sun glint on the ripples, bounded.
                float3 halfDir = SafeNormalize(mainLight.direction + viewWS);
                // The half vector lies under the surface for distant water under a low or set sun: the base is then 0, and
                // pow(0, 160) must not be evaluated.
                half ndh = saturate(dot(normalWS, halfDir));
                half glint = (ndh > 0.9 ? exp2(160.0 * log2(ndh)) : 0.0) * _Glint * (1.0 - foam);
                color += sun * min(glint, 1.5);

                half3 foamLit = _FoamColor.rgb * (ambient + sun * (0.5 + 0.5 * ndl));
                color = lerp(color, foamLit, foam);

                half alpha = lerp(_EdgeAlpha, _CentreAlpha, saturate(depth * 1.6));
                alpha *= 1.0 - smoothstep(0.93, 1.0, abs(across)) * 0.5;
                alpha = saturate(max(alpha, foam * 0.85 + fresnel * 0.5));

                // Never brighter than a little over white (bloom turned an unbounded glint into a white disc, 2026-10-05).
                color = min(color, half3(2.0, 2.0, 2.0));
                color = MixFog(color, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));
                // Last line of defence: a NaN pixel shows black and bloom turns it into a white glow over half the screen.
                float3 safe = color;
                bool badColour = AnyIsNaN(safe) || AnyIsInf(safe);
                if (_MZDebugView > 0.5)
                {
                    float3 view = float3(depth, flow, foamAmount) * 0.45 + 0.05;
                    if (badNormal) view = float3(0.0, 0.9, 0.0);        // green: the ripple normal
                    else if (badColour) view = float3(0.9, 0.9, 0.9);   // white: the lit colour
                    return half4(view, 1.0);
                }

                if (badColour) safe = _DeepColor.rgb * 0.5;
                safe = clamp(safe, 0.0, 2.0);
                float safeAlpha = alpha;
                if (IsNaN(safeAlpha) || IsInf(safeAlpha)) safeAlpha = _CentreAlpha;
                return half4(safe, saturate(safeAlpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
