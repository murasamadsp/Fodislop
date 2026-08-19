Shader "Fodinae/UI/PlanetSurface"
{
    // Rocky crust of the GJ-1132b analogue: cold basalt highlands under a thick
    // sulfur/chlorine haze, cut by rift systems that glow from below.
    //
    // Everything here is evaluated per-fragment on a plain sphere - there is no
    // displacement, so the silhouette stays smooth (which is correct: a planet
    // seen from orbit has no visible profile relief) and all the terrain reads
    // through shading alone.
    Properties
    {
        [Header(Lighting)]
        _SunDirWS ("Sun Direction (world, toward star)", Vector) = (-0.38, 0.16, -0.91, 0)
        _SunColor ("Sun Color (M-dwarf, warm)", Color) = (1.0, 0.90, 0.76, 1)
        _SunIntensity ("Sun Intensity", Range(0, 6)) = 5.0
        _NightAmbient ("Night Ambient", Range(0, 0.1)) = 0.004
        _TwilightColor ("Twilight Scatter Color", Color) = (0.30, 0.34, 0.13, 1)
        _TwilightIntensity ("Twilight Intensity", Range(0, 2)) = 1.10
        _Roughness ("Surface Roughness (Oren-Nayar)", Range(0, 1)) = 0.85

        [Header(Terrain)]
        _ContinentScale ("Continent Scale", Range(0.5, 8)) = 3.0
        _WarpStrength ("Domain Warp Strength", Range(0, 2)) = 0.50
        _RidgeScale ("Mountain Ridge Scale", Range(1, 30)) = 11.0
        _MountainHeight ("Mountain Height", Range(0, 1)) = 0.28
        _DetailScale ("Detail Scale", Range(10, 400)) = 140
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.20
        _ReliefStrength ("Relief (normal) Strength", Range(0, 4)) = 0.75

        [Header(Materials)]
        _BasaltColor ("Basalt (steep rock)", Color) = (0.070, 0.072, 0.062, 1)
        _RegolithColor ("Olive Regolith", Color) = (0.085, 0.088, 0.052, 1)
        _CrustColor ("Sulfur Crust (flats)", Color) = (0.190, 0.180, 0.115, 1)
        _PeakColor ("Peak Rock", Color) = (0.145, 0.140, 0.112, 1)
        _BasinLevel ("Basin Level", Range(0, 1)) = 0.42
        _PeakLevel ("Peak Level", Range(0, 1)) = 0.72

        [Header(Rifts)]
        _MagmaColor ("Magma Color", Color) = (1.0, 0.24, 0.045, 1)
        _MagmaIntensity ("Magma Intensity", Range(0, 12)) = 3.5
        _CrackScale ("Crack Network Scale", Range(1, 40)) = 9.0
        _CrackThreshold ("Crack Threshold", Range(0.5, 1)) = 0.865

        [Header(Liquid Sulfur)]
        _PoolAlbedo ("Pool Albedo", Color) = (0.048, 0.030, 0.014, 1)
        _PoolSpecColor ("Pool Specular Tint", Color) = (1.0, 0.66, 0.28, 1)
        _PoolIntensity ("Pool Specular Intensity", Range(0, 12)) = 3.0
        _PoolGloss ("Pool Gloss (specular exponent)", Range(32, 4096)) = 900
        _PoolF0 ("Pool Normal Reflectance", Range(0, 0.2)) = 0.045
        _PoolScale ("Pool Patch Scale", Range(1, 60)) = 22.0
        _PoolCoverage ("Pool Coverage", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // Integer bit ops in the pcg3d hash need SM4+.
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PlanetNoise.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _SunDirWS;
                float4 _SunColor;
                float _SunIntensity;
                float _NightAmbient;
                float4 _TwilightColor;
                float _TwilightIntensity;
                float _Roughness;

                float _ContinentScale;
                float _WarpStrength;
                float _RidgeScale;
                float _MountainHeight;
                float _DetailScale;
                float _DetailStrength;
                float _ReliefStrength;

                float4 _BasaltColor;
                float4 _RegolithColor;
                float4 _CrustColor;
                float4 _PeakColor;
                float _BasinLevel;
                float _PeakLevel;

                float4 _MagmaColor;
                float _MagmaIntensity;
                float _CrackScale;
                float _CrackThreshold;

                float4 _PoolAlbedo;
                float4 _PoolSpecColor;
                float _PoolIntensity;
                float _PoolGloss;
                float _PoolF0;
                float _PoolScale;
                float _PoolCoverage;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            // Tectonic elevation field in [0, 1].
            //
            // The domain warp is the load-bearing part: unwarped fBm gives
            // isotropic blobs that look like clouds no matter how many octaves
            // you stack. Warping the lookup by another fBm shears those blobs
            // into the elongated, branching provinces that read as crust.
            // Octave counts are held down on purpose. Elevation() is evaluated
            // three times per pixel (once for height, twice more for the analytic
            // normal), so every octave added here costs three lookups across the
            // whole disc. The detail octaves are the ones that carry the visible
            // crispness, so the savings come out of the low-frequency warp and
            // continent layers instead, where they are almost invisible.
            float Elevation(float3 dir)
            {
                float3 c = dir * _ContinentScale;
                float3 warp = float3(
                    Fbm(c + float3(17.1, 3.2, 8.9), 2),
                    Fbm(c + float3(43.7, 21.4, 2.6), 2),
                    Fbm(c + float3(91.3, 12.8, 33.1), 2));

                float continents = Fbm(c + (warp * _WarpStrength), 4);
                float elev = saturate((continents * 0.5) + 0.5);

                // Ranges only rise on already-uplifted crust, so mountains form
                // belts along province edges instead of speckling the basins.
                float uplift = smoothstep(0.40, 0.78, elev);
                float ranges = RidgedFbm(dir * _RidgeScale, 4);
                elev += ranges * uplift * _MountainHeight;

                // High-frequency roughness. Carried at low amplitude but it is
                // what the normals pick up, so it dominates the close-up feel.
                elev += Fbm(dir * _DetailScale, 3) * _DetailStrength * 0.12;

                return saturate(elev);
            }

            // Rift network: ridged-noise crests thresholded into thin connected
            // lines, gated to low ground so rifts sit in basins, not on peaks.
            //
            // Takes the fault field as an argument rather than evaluating it,
            // because the sulfur pools need the same field to know where the
            // geothermal zones are - and RidgedFbm is far too expensive to run
            // twice per pixel.
            float CrackMask(float3 dir, float elev, float faultField)
            {
                // Not named 'line': that is an HLSL geometry-shader primitive
                // keyword and using it as an identifier fails to compile.
                float ridge = smoothstep(_CrackThreshold, 1.0, faultField);

                // Break the network up along its length so it reads as a
                // discontinuous fault system rather than one drawn contour.
                float breakUp = smoothstep(-0.25, 0.35, Fbm(dir * (_CrackScale * 2.7), 3));
                float lowGround = 1.0 - smoothstep(_BasinLevel, _BasinLevel + 0.30, elev);

                return ridge * breakUp * lowGround;
            }

            // Ponded liquid sulfur.
            //
            // Sulfur melts around 115 C, so on a world with this one's
            // geothermal gradient it pools wherever hot ground meets a flat
            // floor - which is why this is gated on three things at once: near
            // the fault network (heat), low ground (it runs downhill), and
            // genuinely flat (a pool cannot sit on a slope). Patchiness on top
            // keeps it from filling every basin uniformly.
            float PoolMask(float3 dir, float elev, float slope, float faultField)
            {
                float geothermal = smoothstep(0.40, 0.86, faultField);
                float flatGround = 1.0 - smoothstep(0.05, 0.18, slope);
                float lowGround = 1.0 - smoothstep(_BasinLevel - 0.06, _BasinLevel + 0.06, elev);
                float patches = smoothstep(0.05, 0.55, (Fbm(dir * _PoolScale, 3) * 0.5) + 0.5);

                return saturate(geothermal * flatGround * lowGround * patches * _PoolCoverage);
            }

            // Oren-Nayar (qualitative form). Dusty basalt regolith is strongly
            // backscattering: pure Lambert makes the terminator roll off too
            // fast and the disc centre look waxy.
            float OrenNayar(float3 N, float3 L, float3 V, float roughness)
            {
                float s2 = roughness * roughness;
                float A = 1.0 - (0.5 * (s2 / (s2 + 0.33)));
                float B = 0.45 * (s2 / (s2 + 0.09));

                float ndl = dot(N, L);
                float ndv = dot(N, V);
                float lit = saturate(ndl);

                float3 lPerp = normalize(L - (N * ndl));
                float3 vPerp = normalize(V - (N * ndv));
                float cosPhi = saturate(dot(lPerp, vPerp));

                float thetaI = acos(clamp(ndl, -1.0, 1.0));
                float thetaR = acos(clamp(ndv, -1.0, 1.0));
                float alpha = max(thetaI, thetaR);
                float beta = min(thetaI, thetaR);

                return lit * (A + (B * cosPhi * sin(alpha) * tan(beta)));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.positionOS);
                float elev = Elevation(dir);

                // Analytic normal from finite differences on the sphere's own
                // tangent frame. eps is tied to the detail scale so the slope
                // estimate stays consistent when detail frequency is retuned.
                float3 up = abs(dir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 tangent = normalize(cross(up, dir));
                float3 bitangent = cross(dir, tangent);
                float eps = 1.6 / _DetailScale;

                float eT = Elevation(normalize(dir + (tangent * eps)));
                float eB = Elevation(normalize(dir + (bitangent * eps)));
                float2 slopeVec = float2(eT - elev, eB - elev) / eps;

                float3 normalOS = normalize(dir - (((tangent * slopeVec.x) + (bitangent * slopeVec.y)) * _ReliefStrength * 0.05));
                float3 N = normalize(TransformObjectToWorldNormal(normalOS));
                float3 geoN = normalize(TransformObjectToWorldNormal(dir));
                float3 L = normalize(_SunDirWS.xyz);
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));

                // ---- Albedo ------------------------------------------------
                // Steepness sorts the materials: wind/haze-deposited sulfur can
                // only accumulate on flats, so cliffs and scarps stay bare dark
                // basalt. This slope-sorting is what makes procedural rock look
                // geological instead of like a tinted noise texture.
                float slope = saturate(length(slopeVec) * 0.09);

                float3 albedo = lerp(_CrustColor.rgb, _RegolithColor.rgb, smoothstep(0.10, 0.42, slope));
                albedo = lerp(albedo, _BasaltColor.rgb, smoothstep(0.38, 0.80, slope));

                // Basins pond the pale evaporite crust; peaks strip back to rock.
                float basin = 1.0 - smoothstep(_BasinLevel - 0.10, _BasinLevel + 0.14, elev);
                albedo = lerp(albedo, _CrustColor.rgb, basin * (1.0 - smoothstep(0.30, 0.65, slope)) * 0.75);
                albedo = lerp(albedo, _PeakColor.rgb, smoothstep(_PeakLevel, _PeakLevel + 0.22, elev));

                // Fault field, evaluated once and shared by the rift glow and the
                // sulfur pools - both need to know where the geothermal zones
                // are, and RidgedFbm is the single most expensive call here.
                float faultField = RidgedFbm(dir * _CrackScale, 4);
                float pool = PoolMask(dir, elev, slope, faultField);

                // Liquid sulfur is much darker than the crust it sits in - the
                // dark pool is what makes the highlight on it read as wet.
                albedo = lerp(albedo, _PoolAlbedo.rgb, pool);

                // Broad mineral mottling, decorrelated from elevation so the
                // colour does not simply restate the height map.
                float mottle = Fbm(dir * 5.3 + float3(61.0, 12.0, 44.0), 4);
                albedo *= lerp(0.88, 1.14, saturate((mottle * 0.5) + 0.5));

                // Fine grain at pixel scale keeps midtones from flattening out.
                // 480 aliased into visible speckle at the size this actually renders.
                float grain = GradientNoise(dir * 240.0);
                albedo *= lerp(0.94, 1.06, saturate((grain * 0.5) + 0.5));

                // ---- Direct light ------------------------------------------
                float diffuse = OrenNayar(N, L, V, _Roughness);

                // Relief self-shadowing: as the sun goes grazing, slopes tilted
                // away from it drop into shadow faster than N.L alone predicts.
                // Both normals are taken in world space here - comparing an
                // object-space normal against the world-space sun vector silently
                // produces garbage as soon as the planet carries any rotation.
                float ndlGeo = dot(geoN, L);
                float ndlRelief = dot(N, L);
                float grazing = saturate(1.0 - ndlGeo);
                float shadow = saturate(1.0 - (max(0.0, ndlGeo - ndlRelief) * grazing * 1.2));

                float3 sun = _SunColor.rgb * _SunIntensity;
                float3 color = albedo * diffuse * shadow * sun;

                // ---- Specular glint off ponded sulfur ----------------------
                // This is the one cue that separates liquid from a pale mineral
                // crust at orbital distance: a mirror-smooth surface returns a
                // single bright specular point rather than scattering. It is the
                // same signature Cassini used to confirm Titan's lakes.
                //
                // A pool is flat, so the highlight is taken against the geometric
                // normal - using the relief-perturbed normal would scatter the
                // glint across the terrain roughness and destroy the effect.
                float3 poolN = normalize(lerp(N, geoN, pool));
                float3 H = normalize(L + V);
                float specular = pow(saturate(dot(poolN, H)), _PoolGloss);

                // Schlick: reflectance climbs steeply at grazing angles, which is
                // why pools near the limb flare and ones under the sub-stellar
                // point stay subtle.
                float fresnel = _PoolF0 + ((1.0 - _PoolF0) * pow(1.0 - saturate(dot(poolN, V)), 5.0));

                color += _PoolSpecColor.rgb * sun * specular * fresnel * pool * saturate(dot(poolN, L)) * _PoolIntensity;

                // ---- Twilight ----------------------------------------------
                // Just past the geometric terminator the ground is still lit by
                // the dense atmosphere overhead. A thick sulfur haze scatters a
                // lot, so this band is wide and distinctly olive.
                float twilight = smoothstep(-0.32, 0.14, ndlGeo) * (1.0 - saturate(ndlGeo * 3.0));
                color += albedo * _TwilightColor.rgb * twilight * _TwilightIntensity;

                color += albedo * _NightAmbient;

                // ---- Rift glow ---------------------------------------------
                // Emissive, so it survives the night side untouched by N.L -
                // which is the whole point: the rifts are the only thing you
                // see on the dark limb.
                float crack = CrackMask(dir, elev, faultField);
                float3 hot = lerp(_MagmaColor.rgb, float3(1.0, 0.80, 0.42), pow(crack, 4.0));
                color += hot * crack * _MagmaIntensity;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
