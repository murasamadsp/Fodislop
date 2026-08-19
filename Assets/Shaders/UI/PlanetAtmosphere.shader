Shader "Fodinae/UI/PlanetAtmosphere"
{
    // Single-scattering atmosphere, ray-marched in world space.
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
        _Density ("Density", Range(0, 30)) = 6.0

        [Header(Optics)]
        _ScatterColor ("Aerosol Scattering", Color) = (0.50, 0.66, 0.30, 1)
        _AbsorbColor ("Aerosol Absorption (blue-weighted)", Color) = (0.34, 0.30, 0.85, 1)
        _AbsorbStrength ("Absorption Strength", Range(0, 4)) = 1.35
        _MieG ("Mie Anisotropy (forward lobe)", Range(0, 0.9)) = 0.70
        _MieBackG ("Mie Anisotropy (back lobe)", Range(-0.9, 0)) = -0.32
        _MieBackWeight ("Back Lobe Weight", Range(0, 1)) = 0.42

        [Header(Lighting)]
        _SunDirWS ("Sun Direction (world, toward star)", Vector) = (-0.38, 0.16, -0.91, 0)
        _SunColor ("Sun Color (M-dwarf, warm)", Color) = (1.0, 0.82, 0.62, 1)
        _SunIntensity ("Sun Intensity", Range(0, 20)) = 4.2

        [Header(Detail)]
        _TurbulenceScale ("Turbulence Scale", Range(0, 20)) = 5.0
        _TurbulenceStrength ("Turbulence Strength", Range(0, 1)) = 0.38
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
            #define VIEW_STEPS 18
            #define LIGHT_STEPS 4

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

                float4 _SunDirWS;
                float4 _SunColor;
                float _SunIntensity;

                float _TurbulenceScale;
                float _TurbulenceStrength;
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

            // withTurbulence is off for the light march: the aerosol banding is
            // a look detail on what the viewer sees directly, and evaluating an
            // fBm at every sample of the *nested* light loop multiplied the
            // shader cost several times over for no visible gain.
            float DensityAt(float3 p, float3 centre, float rPlanet, float rShell, bool withTurbulence)
            {
                float h = ShellHeight(p, centre, rPlanet, rShell);
                float d = exp(-h / max(_ScaleHeight, 1e-3));

                // Fade the last stretch to zero so the shell's outer boundary is
                // not a visible hard edge where the march simply stops.
                d *= smoothstep(1.0, 0.75, h);

                if (withTurbulence && _TurbulenceStrength > 0.0)
                {
                    // Churning aerosol banding. Sampled on the direction from the
                    // planet centre so it convects around the body rather than
                    // sliding through it as a static 3D field.
                    float3 dir = normalize(p - centre);
                    float n = Fbm((dir * _TurbulenceScale) + float3(0.0, h * 3.0, 0.0), 3);
                    d *= lerp(1.0, saturate((n * 0.5) + 0.5) * 2.0, _TurbulenceStrength);
                }

                return d;
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
                [loop]
                for (int i = 0; i < LIGHT_STEPS; i++)
                {
                    float3 s = p + (L * (((float)i + 0.5) * ds));
                    depth += DensityAt(s, centre, rPlanet, rShell, false) * ds;
                }

                return depth;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Derive geometry from the object matrix instead of trusting
                // hand-set material vectors - a stale _PlanetCenter that no
                // longer matches the transform is a silent, hard-to-spot failure.
                float3 centre = mul(UNITY_MATRIX_M, float4(0, 0, 0, 1)).xyz;
                float rShell = 0.5 * length(mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0)));
                float rPlanet = rShell * _PlanetRadiusRatio;

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

                float ds = (tEnd - tStart) / (float)VIEW_STEPS;
                float3 inscatter = 0.0;
                float3 viewTransmittance = 1.0;

                [loop]
                for (int i = 0; i < VIEW_STEPS; i++)
                {
                    float3 p = ro + (rd * (tStart + (((float)i + 0.5) * ds)));
                    float density = DensityAt(p, centre, rPlanet, rShell, true);
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
                    inscatter += viewTransmittance * stepScatter * (1.0 - stepTransmittance) / max(extinction * density, 1e-5);

                    viewTransmittance *= stepTransmittance;
                }

                float3 color = inscatter * _SunColor.rgb * _SunIntensity;

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
                    alpha = saturate(1.0 - dot(viewTransmittance, float3(0.2126, 0.7152, 0.0722)));
                }
                else
                {
                    alpha = saturate(max(color.r, max(color.g, color.b)));
                }

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
