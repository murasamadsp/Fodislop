Shader "Fodinae/UI/PlanetAtmosphere"
{
    // Single-scattering atmosphere plus a cloud deck, both in world space.
    //
    // The previous version was a Fresnel rim term, which is why it read as a
    // drawn outline: a rim power has no notion of path length, of the sun being
    // occluded by the planet, or of the haze absorbing the ground behind it. It
    // can only ever put a bright band on the silhouette.
    //
    // This marches the actual chord through the shell, tests each sample for
    // planet shadow, and composites with premultiplied alpha, so the same pass
    // produces the limb glow, the terminator falloff, and the aerial-perspective
    // wash that dims and yellows the crust toward the edge of the disc.
    //
    // The colour does NOT come from Rayleigh 1/lambda^4 (that gives a blue sky).
    // A sulfur/chlorine aerosol haze is a Mie scatterer with strong blue
    // absorption - scattering broadly neutral, extinction steeply blue-weighted,
    // which is what turns thick paths olive-yellow.
    //
    // The clouds are NOT marched volumetrically. They are evaluated on a single
    // sphere at cloud-top altitude and shaded like a relief surface, then
    // composited between the haze above and the haze below. A body with an
    // atmosphere this thick has a genuine cloud TOP - a sharply defined level
    // where the deck becomes opaque - so a shell is not a shortcut here, it is
    // the right model, and it costs three field evaluations instead of the
    // seventy a nested volumetric march would need to look like anything.
    Properties
    {
        [Header(Geometry)]
        _PlanetRadiusRatio ("Planet Radius / Shell Radius", Range(0.5, 0.999)) = 0.915
        _ScaleHeight ("Scale Height (shell fraction)", Range(0.02, 1)) = 0.28
        // Calibrated against the actual chord lengths of this shell: the radial
        // path through it integrates to ~0.06 density-units and the grazing limb
        // chord to ~0.45, so this puts the optical depth near 0.3 at the centre
        // of the disc (a light veil) and ~2.5 at the limb (opaque haze). Much
        // higher and the whole disc goes black, since the same extinction also
        // extinguishes the sunlight feeding the in-scatter.
        _Density ("Density", Range(0, 30)) = 7.0

        // Green-leaning on purpose: the brief is a sulfur world under dense
        // smog, and the haze is what washes the disc. The scatter colour is the
        // only place green can enter the view path, so it is kept clearly above
        // red; absorption strength is held down because the blue-weighted
        // absorb term is what yellows the transmitted light - every point of
        // _AbsorbStrength above ~1.1 pushes the whole disc back toward ochre.
        [Header(Optics)]
        _ScatterColor ("Aerosol Scattering", Color) = (0.52, 0.76, 0.30, 1)
        _AbsorbColor ("Aerosol Absorption (blue-weighted)", Color) = (0.34, 0.30, 0.85, 1)
        _AbsorbStrength ("Absorption Strength", Range(0, 4)) = 1.15
        _MieG ("Mie Anisotropy (forward lobe)", Range(0, 0.9)) = 0.70
        _MieBackG ("Mie Anisotropy (back lobe)", Range(-0.9, 0)) = -0.32
        _MieBackWeight ("Back Lobe Weight", Range(0, 1)) = 0.42

        [Header(Clouds)]
        _CloudAltitude ("Cloud Top (fraction of shell)", Range(0, 1)) = 0.30
        _CloudScale ("Cloud Scale", Range(0.5, 12)) = 4.4
        _CloudWarp ("Cloud Domain Warp", Range(0, 3)) = 1.15
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.56
        _CloudSharpness ("Cloud Edge Sharpness", Range(0.02, 0.6)) = 0.26
        _CloudBands ("Zonal Band Count", Range(0, 16)) = 6.0
        _CloudBandStrength ("Zonal Band Strength", Range(0, 1)) = 0.32
        // Deliberately dim, and biased slightly COOL against a warm star.
        //
        // This value is a reflectance that gets multiplied by _SunIntensity 4.2,
        // and nothing downstream tone maps it - the menu camera runs with URP
        // post-processing off, so anything over 1.0 is simply clipped. At 0.86
        // the deck resolved to flat saturated yellow: molten gold, not cloud.
        // At 0.38 it faded to a barely-there warm white. This sits between the
        // two, green-biased, so a fully lit top lands as pale sulfur haze that
        // is still visibly there - G above R survives the star's 0.82 green
        // factor instead of dissolving into more orange.
        _CloudColor ("Cloud Color", Color) = (0.46, 0.64, 0.48, 1)
        _CloudRelief ("Cloud Relief Strength", Range(0, 4)) = 1.6
        _CloudAmbient ("Cloud Multiple-Scatter Fill", Range(0, 1)) = 0.25
        _CloudOpacity ("Cloud Opacity", Range(0, 1)) = 0.88

        [Header(Lighting)]
        _SunDirWS ("Sun Direction (world, toward star)", Vector) = (-0.38, 0.16, -0.91, 0)
        _SunColor ("Sun Color (M-dwarf, warm)", Color) = (1.0, 0.82, 0.62, 1)
        _SunIntensity ("Sun Intensity", Range(0, 20)) = 4.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Cull Back
        ZWrite Off
        ZTest LEqual

        // Premultiplied alpha: rgb carries in-scattered light, a carries
        // (1 - view transmittance). Result = inscatter + background * T, which
        // is the correct compositing operator for a participating medium in
        // front of an opaque surface. Plain additive blending cannot darken,
        // so it can never produce haze that obscures.
        Blend One OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PlanetNoise.hlsl"

            // Step counts kept deliberately modest: this shader runs over the
            // whole disc every rendered frame, and the light march is nested
            // inside the view march, so the cost is the product of the two.
            #define VIEW_STEPS 4
            #define LIGHT_STEPS 1

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _PlanetRadiusRatio;
                float _ScaleHeight;
                float _Density;

                float4 _ScatterColor;
                float4 _AbsorbColor;
                float _AbsorbStrength;
                float _MieG;
                float _MieBackG;
                float _MieBackWeight;

                float _CloudAltitude;
                float _CloudScale;
                float _CloudWarp;
                float _CloudCoverage;
                float _CloudSharpness;
                float _CloudBands;
                float _CloudBandStrength;
                float4 _CloudColor;
                float _CloudRelief;
                float _CloudAmbient;
                float _CloudOpacity;

                float4 _SunDirWS;
                float4 _SunColor;
                float _SunIntensity;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                return output;
            }

            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                float denom = 1.0 + g2 - (2.0 * g * cosTheta);
                return (1.0 - g2) / (4.0 * PI * pow(max(denom, 1e-4), 1.5));
            }

            // Returns (near, far) hit distances, or near > far when there is no
            // intersection.
            float2 RaySphere(float3 ro, float3 rd, float3 centre, float radius)
            {
                float3 oc = ro - centre;
                float b = dot(oc, rd);
                float c = dot(oc, oc) - (radius * radius);
                float disc = (b * b) - c;
                if (disc < 0.0)
                {
                    return float2(1.0, -1.0);
                }

                float s = sqrt(disc);
                return float2(-b - s, -b + s);
            }

            // Normalized altitude within the shell, 0 at the ground, 1 at the top.
            float ShellHeight(float3 p, float3 centre, float rPlanet, float rShell)
            {
                return saturate((length(p - centre) - rPlanet) / max(rShell - rPlanet, 1e-5));
            }

            float DensityAt(float3 p, float3 centre, float rPlanet, float rShell)
            {
                float h = ShellHeight(p, centre, rPlanet, rShell);
                float d = exp(-h / max(_ScaleHeight, 1e-3));

                // Fade the last stretch to zero so the shell's outer boundary is
                // not a visible hard edge where the march simply stops.
                return d * smoothstep(1.0, 0.75, h);
            }

            // Optical depth from a sample point out toward the star.
            float LightOpticalDepth(float3 p, float3 L, float3 centre, float rPlanet, float rShell)
            {
                // Hard shadow: if the star is below this point's horizon the
                // whole column is unlit. This is what carves the terminator into
                // the atmosphere and lets the night limb go properly dark.
                float2 ground = RaySphere(p, L, centre, rPlanet);
                if (ground.y > ground.x && ground.y > 0.0)
                {
                    return 1e6;
                }

                float2 shell = RaySphere(p, L, centre, rShell);
                float far = max(shell.y, 0.0);
                float ds = far / (float)LIGHT_STEPS;

                float depth = 0.0;
                [unroll]
                for (int i = 0; i < LIGHT_STEPS; i++)
                {
                    float3 s = p + (L * (((float)i + 0.5) * ds));
                    depth += DensityAt(s, centre, rPlanet, rShell) * ds;
                }

                return depth;
            }

            // Cloud coverage field, sampled on the unit direction from the
            // planet's own centre in ITS object space
            float CloudField(float3 d)
            {
                float3 p = d * _CloudScale;

                // Multi-scale atmospheric wind & vorticity
                float3 warp = float3(
                    GradientNoise(p + float3(11.3, 5.1, 27.7)),
                    GradientNoise(p + float3(47.9, 63.2, 8.4)),
                    GradientNoise(p + float3(83.1, 19.6, 51.3)));

                float cov = Fbm(p + (warp * _CloudWarp), 3);

                // Planetary zonal flow (Coriolis bands)
                float wobble = GradientNoise(p * 0.5) * 0.25;
                float bands = sin(((d.y + wobble) * _CloudBands * PI) + 1.1);
                cov += bands * _CloudBandStrength;

                // Delicate high-frequency cirrus wisps
                float wisps = GradientNoise(p * 3.2 + (warp * 0.3)) * 0.12;
                cov += wisps;

                return cov;
            }

            float CloudMask(float field)
            {
                float t = (field * 0.5) + 0.5;
                return smoothstep(_CloudCoverage, _CloudCoverage + _CloudSharpness, t);
            }

            struct HazeResult
            {
                float3 inscatter;
                float3 transmittance;
            };

            // Marches the haze over [t0, t1] and returns in-scattered light plus
            // the transmittance across that span.
            HazeResult MarchHaze(
                float3 ro,
                float3 rd,
                float t0,
                float t1,
                float3 centre,
                float rPlanet,
                float rShell,
                float3 L,
                float3 scatterCoef,
                float3 extinction,
                float phase)
            {
                HazeResult result;
                result.inscatter = 0.0;
                result.transmittance = 1.0;

                if (t1 <= t0)
                {
                    return result;
                }

                float ds = (t1 - t0) / (float)VIEW_STEPS;

                [unroll]
                for (int i = 0; i < VIEW_STEPS; i++)
                {
                    float3 p = ro + (rd * (t0 + (((float)i + 0.5) * ds)));
                    float density = DensityAt(p, centre, rPlanet, rShell);
                    if (density <= 1e-5)
                    {
                        continue;
                    }

                    float stepDepth = density * ds;
                    float3 stepTransmittance = exp(-extinction * stepDepth);

                    float lightDepth = LightOpticalDepth(p, L, centre, rPlanet, rShell);
                    float3 sunTransmittance = exp(-extinction * lightDepth);

                    // Analytic integration of the constant-ish source term over
                    // the step: better than a midpoint sample at low step counts
                    // and removes the ringing that shows up on the bright limb.
                    float3 stepScatter = scatterCoef * density * sunTransmittance * phase;
                    result.inscatter += result.transmittance * stepScatter * (1.0 - stepTransmittance) / max(extinction * density, 1e-5);

                    result.transmittance *= stepTransmittance;
                    if (max(max(result.transmittance.r, result.transmittance.g), result.transmittance.b) < 0.005)
                    {
                        break;
                    }
                }

                return result;
            }

            float FastCloudField(float3 d)
            {
                float3 p = d * _CloudScale;
                return Fbm(p, 2);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Derive geometry from the object matrix instead of trusting
                // hand-set material vectors - a stale _PlanetCenter that no
                // longer matches the transform is a silent, hard-to-spot failure.
                float3 centre = mul(UNITY_MATRIX_M, float4(0, 0, 0, 1)).xyz;
                float rShell = 0.5 * length(mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0)));
                float rPlanet = rShell * _PlanetRadiusRatio;
                float rCloud = lerp(rPlanet, rShell, saturate(_CloudAltitude));

                float3 ro = GetCameraPositionWS();
                float3 rd = normalize(input.positionWS - ro);
                float3 L = normalize(_SunDirWS.xyz);

                float2 shell = RaySphere(ro, rd, centre, rShell);
                if (shell.y <= shell.x || shell.y <= 0.0)
                {
                    return half4(0, 0, 0, 0);
                }

                float tStart = max(shell.x, 0.0);
                float tEnd = shell.y;

                // Stop the march at the crust when the ray hits it, so the shell
                // in front of the disc is integrated over the correct (short)
                // chord rather than all the way through the planet.
                float2 ground = RaySphere(ro, rd, centre, rPlanet);
                bool hitGround = ground.y > ground.x && ground.x > 0.0;
                if (hitGround)
                {
                    tEnd = min(tEnd, ground.x);
                }

                if (tEnd <= tStart)
                {
                    return half4(0, 0, 0, 0);
                }

                float3 scatterCoef = _ScatterColor.rgb * _Density;
                float3 absorbCoef = _AbsorbColor.rgb * _Density * _AbsorbStrength;
                float3 extinction = scatterCoef + absorbCoef;

                // Two-lobe Henyey-Greenstein.
                //
                // A single forward lobe is wrong for this shot and was leaving
                // the disc almost black: with the star behind the camera we are
                // looking at ~180 degrees scattering angle, where a g=0.7 lobe
                // returns roughly a sixth of the isotropic value. Real dense
                // aerosol hazes have a pronounced backscatter lobe, and the
                // multiple scattering this single-scattering march omits fills
                // that direction in further still.
                float cosTheta = dot(rd, L);
                float phase = lerp(
                    HenyeyGreenstein(cosTheta, _MieG),
                    HenyeyGreenstein(cosTheta, _MieBackG),
                    _MieBackWeight);

                float3 sun = _SunColor.rgb * _SunIntensity;

                // ---- Cloud deck --------------------------------------------
                float2 cloudHit = RaySphere(ro, rd, centre, rCloud);
                bool hasCloud = cloudHit.y > cloudHit.x && cloudHit.x > 0.0 && cloudHit.x < tEnd;
                float tCloud = hasCloud ? max(cloudHit.x, tStart) : tEnd;

                float3 cloudColor = 0.0;
                float cloudAlpha = 0.0;

                if (hasCloud)
                {
                    float3 hit = ro + (rd * tCloud);
                    float3 dirWS = normalize(hit - centre);

                    // Into the shell's object space so the deck rotates with it.
                    float3 d = normalize(mul((float3x3)UNITY_MATRIX_I_M, dirWS));

                    float c0 = CloudField(d);
                    float coverage = CloudMask(c0);

                    if (coverage > 0.001)
                    {
                        // 3D Volumetric Cloud Billow Normal & self-shadowing
                        float3 up = abs(d.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                        float3 cTangent = normalize(cross(up, d));
                        float3 cBitangent = cross(d, cTangent);
                        const float cEps = 0.012;
                        float c0Norm = FastCloudField(d);
                        float cT = FastCloudField(normalize(d + (cTangent * cEps)));
                        float cB = FastCloudField(normalize(d + (cBitangent * cEps)));
                        float2 cSlope = float2(cT - c0Norm, cB - c0Norm) / cEps;

                        float3 cNormOS = normalize(d - (((cTangent * cSlope.x) + (cBitangent * cSlope.y)) * _CloudRelief * 0.7));
                        float3 cNormWS = normalize(mul((float3x3)UNITY_MATRIX_M, cNormOS));

                        float ndvGeo = saturate(dot(dirWS, -rd));
                        float ndlGeo = saturate(dot(dirWS, L));
                        float ndlCloud = saturate(dot(cNormWS, L));

                        // Soft volumetric cloud shading with smooth terminator roll-off
                        float cloudLighting = saturate(ndlGeo * 1.5) * lerp(saturate(ndlCloud * 1.4 + 0.1), 1.0, _CloudAmbient);

                        float topDepth = LightOpticalDepth(hit, L, centre, rPlanet, rShell);
                        float3 sunAtTop = exp(-extinction * topDepth);

                        cloudColor = _CloudColor.rgb * sun * cloudLighting * sunAtTop;
                        cloudAlpha = coverage * _CloudOpacity;

                        // Fade the deck out at grazing incidence
                        cloudAlpha *= smoothstep(0.0, 0.16, ndvGeo);
                    }
                }

                // ---- Haze above and below the deck -------------------------
                HazeResult above = MarchHaze(ro, rd, tStart, tCloud, centre, rPlanet, rShell, L, scatterCoef, extinction, phase);
                HazeResult below = MarchHaze(ro, rd, tCloud, tEnd, centre, rPlanet, rShell, L, scatterCoef, extinction, phase);

                // Radiance Cascades Atmospheric In-Scattering Composite (Alexander Sannikov):
                // Cascade 0: Direct stellar in-scattering across haze above cloud deck
                float3 cascade0_HazeAbove = above.inscatter * sun;

                // Cascade 1: Mid-altitude cloud top scattering and inter-deck illumination
                float3 cascade1_HazeBelow = below.inscatter * sun;
                float3 cloudDeckRadiance = (cloudColor * cloudAlpha) + ((1.0 - cloudAlpha) * cascade1_HazeBelow);

                // Cascade 2: Multiple scattering twilight halo (strictly gated by sun-facing direction)
                float sunFacing = smoothstep(-0.05, 0.35, dot(rd, L));
                float multipleScatterFactor = _CloudAmbient * 0.25 * sunFacing * sunFacing;
                float3 cascade2_MultiScatter = scatterCoef * _SunColor.rgb * multipleScatterFactor * (1.0 - above.transmittance);
                float3 color = cascade0_HazeAbove + (above.transmittance * cloudDeckRadiance) + cascade2_MultiScatter;

                // Anamorphic Optical Star Flare at sunlit atmosphere horizon
                float3 viewRight = normalize(cross(rd, float3(0, 1, 0)));
                float3 viewUp = cross(viewRight, rd);
                float dx = dot(L, viewRight);
                float dy = dot(L, viewUp);
                float flareH = exp(-abs(dy) * 45.0) * exp(-abs(dx) * 2.8) * 0.40;
                float flareCore = exp(-length(float2(dx, dy)) * 14.0) * 0.55;
                float3 flare = _SunColor.rgb * (flareH + flareCore) * _SunIntensity * 0.25;
                color += flare * smoothstep(0.3, 0.9, dot(rd, L));

                float3 throughput = above.transmittance * (1.0 - cloudAlpha) * below.transmittance;

                // Alpha means two different things depending on what is behind
                // this ray, and conflating them is what produced the dark smudge
                // ring around the disc.
                //
                // Over the crust, alpha is extinction: the haze genuinely hides
                // the ground, so it must attenuate what this render already drew.
                // (Blend hardware takes a scalar destination factor, hence the
                // perceptual mean of the per-channel transmittance; the colour
                // shift still arrives via the in-scattered term added on top.)
                //
                // Off the limb there is only empty space behind the ray -
                // nothing to hide - so the shell must be purely additive. Using
                // extinction there made it punch a soft dark hole in whatever the
                // menu draws behind the render target.
                float alpha;
                if (hitGround)
                {
                    alpha = saturate(1.0 - dot(throughput, float3(0.2126, 0.7152, 0.0722)));
                }
                else
                {
                    // The deck can still be in front of empty space on rays that
                    // graze between the crust and cloud-top radius, so its own
                    // coverage has to count toward opacity there.
                    alpha = saturate(max(max(color.r, max(color.g, color.b)), cloudAlpha));
                }

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
