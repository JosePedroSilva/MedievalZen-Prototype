// Full-screen atmospheric fog for MedievalZen (URP 17, Render Graph).
// Used by a FullScreenPassRendererFeature (injection: Before Rendering Post Processing, requirements: Depth,
// fetch colour buffer: off) that MedievalZen/Atmosphere/Apply To Open Scene adds to PC_Renderer. The pass alpha-blends
// the fog colour over the finished opaque + transparent image, using the opaque scene depth.
//
// Every parameter is a global set each frame by AtmosphereController (MedievalZen.Environment). When no controller runs
// the globals are zero, _AtmoShape.x (max opacity) is 0 and the pass leaves the image untouched.
Shader "MedievalZen/AtmosphericFog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "AtmosphericFog"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _AtmoFogColor;      // rgb: fog colour (linear)
            float4 _AtmoSunColor;      // rgb: in-scattered colour toward the light (sun, or moon at night)
            float4 _AtmoAntiSunColor;  // rgb: multiplier on the fog colour away from the light; a: strength
            float4 _AtmoSunDir;        // xyz: world direction toward the light; w: scatter strength
            float4 _AtmoDensity;       // x: haze density (exp2, 1/m); y: height-fog density at base (1/m); z: height falloff (1/m); w: base altitude (m)
            float4 _AtmoShape;         // x: max opacity; y: fog start distance (m); z: scatter lobe exponent; w: sky test factor
            float4 _AtmoSky;           // x: horizon fog amount on the sky; y: fade elevation (sine); z: sky radius (horizontal, m); w: sky height (m)
            float4 _AtmoSkyCenter;     // xyz: centre of the skydome
            float4 _AtmoSkyTail;       // x: haze tail amount on the sky layer; y: tail end elevation (sine); z: cloud floor above the sky centre (m, 0 = no cloud test); w: cloud min distance (m)
            float4 _AtmoNoise;         // x: strength 0..1; y: frequency (1/m); zw: wind offset (m)

            // Integer-lattice value noise, periodic every 1024 cells (the controller wraps the wind offset to that period).
            float Hash21(float2 cell)
            {
                cell = cell - 1024.0 * floor(cell / 1024.0);
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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float rawDepth = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                float deviceDepth = rawDepth;
                bool farPlane = rawDepth <= 1e-7;
            #else
                float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                bool farPlane = rawDepth >= 1.0 - 1e-7;
            #endif

                float3 positionWS = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                float3 cameraWS = _WorldSpaceCameraPos.xyz;
                float3 ray = positionWS - cameraWS;
                float dist = length(ray);
                float3 dir = ray / max(dist, 1e-4);

                // The Synty skydome is real geometry: anything at (or beyond) the dome is sky and gets an elevation fade
                // instead of a distance fog wall. _AtmoSky.zw are the dome's radii; the dome follows the player.
                float skyDistance = _ProjectionParams.z * 0.97;
                if (_AtmoSky.z > 1.0)
                {
                    float3 q = dir / float3(_AtmoSky.z, max(_AtmoSky.w, 1.0), _AtmoSky.z);
                    float dome = rsqrt(max(dot(q, q), 1e-12));
                    skyDistance = min(skyDistance, dome * _AtmoShape.w);
                }
                float isSky = (farPlane || dist >= skyDistance) ? 1.0 : 0.0;

                // The Synty cloud ring is opaque geometry kilometres away: without this its inner (higher) part would get the
                // distance haze of geometry and its outer part the sky fade. Pixels high above the sky centre and far away
                // are clouds and join the sky layer, so every cloud is fogged by elevation, like the dome behind it.
                float cloudHeight = positionWS.y - _AtmoSkyCenter.y;
                float isCloud = (_AtmoSkyTail.z > 0.0 && cloudHeight >= _AtmoSkyTail.z && dist >= _AtmoSkyTail.w) ? 1.0 : 0.0;
                isSky = max(isSky, isCloud);

                // Distance haze (exponential squared).
                float d = max(dist - _AtmoShape.y, 0.0);
                float haze = _AtmoDensity.x * d;
                float tauHaze = haze * haze;

                // Height fog: exact integral of density(h) = D * exp(-k * max(h - base, 0)) along the ray.
                float k = max(_AtmoDensity.z, 1e-5);
                float h0 = max(cameraWS.y - _AtmoDensity.w, 0.0);
                float h1 = max(positionWS.y - _AtmoDensity.w, 0.0);
                float dh = h1 - h0;
                float e0 = exp(-k * h0);
                float ratio = abs(k * dh) > 1e-3 ? (e0 - exp(-k * h1)) / (k * dh) : e0;
                float tauMist = _AtmoDensity.y * d * ratio;

                // Two octaves of slow world-space noise, scrolling with the wind, break up the mist (faded out far away).
                float2 np = (positionWS.xz + _AtmoNoise.zw) * _AtmoNoise.y;
                float n = ValueNoise(np) * 0.65 + ValueNoise(np * 2.0 + 17.0) * 0.35;
                float noiseFade = saturate(1.0 - dist / 1500.0);
                tauMist *= max(0.0, 1.0 + _AtmoNoise.x * noiseFade * (n * 2.0 - 1.0));

                float fogGeometry = 1.0 - exp(-(tauHaze + tauMist));

                // Sky layer (dome + clouds): a horizon band fading out with elevation, combined with a longer convex haze tail
                // scaled by the current haze; both reach zero well below the zenith (AtmosphereMath.SkyFog mirrors this).
                float fogCore = saturate(_AtmoSky.x) * (1.0 - smoothstep(0.0, max(_AtmoSky.y, 1e-3), dir.y));
                float tailFade = 1.0 - saturate(dir.y / max(_AtmoSkyTail.y, 1e-3));
                float fogTail = saturate(_AtmoSkyTail.x) * tailFade * tailFade;
                float fogSky = 1.0 - (1.0 - fogCore) * (1.0 - fogTail);
                float fog = lerp(fogGeometry, fogSky, isSky);
                fog = min(fog, _AtmoShape.x);

                // Colour: warm in-scattering toward the light, cooler on the far side.
                float cosTheta = dot(dir, _AtmoSunDir.xyz);
                float3 color = _AtmoFogColor.rgb;
                color *= lerp(float3(1.0, 1.0, 1.0), _AtmoAntiSunColor.rgb, saturate(-cosTheta) * _AtmoAntiSunColor.a);
                float lobe = pow(saturate(cosTheta), max(_AtmoShape.z, 1.0));
                float wide = saturate(cosTheta * 0.5 + 0.5);
                float sunAmount = saturate(_AtmoSunDir.w * (0.7 * lobe + 0.3 * wide * wide));
                color = lerp(color, _AtmoSunColor.rgb, sunAmount);

                return half4(color, fog);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
