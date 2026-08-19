Shader "Fodinae/UI/Starfield"
{
    // Procedural starfield for the main menu backdrop.
    //
    // Replaces a baked PNG of scattered dots. Baked stars cannot scintillate,
    // and their point spread was a hard 2px disc, which is what made the old
    // background read as noise rather than as a sky.
    //
    // Three things carry the realism here:
    //  - a magnitude distribution biased hard toward faint stars, so the eye
    //    picks out a handful of bright ones against a dense faint field rather
    //    than a uniform sprinkle;
    //  - a point spread with a tight core AND a wide low-amplitude wing, which
    //    is how a real point source lands on any optic - a bare Gaussian looks
    //    like a soft dot, the wing is what makes it look like a star;
    //  - colour drawn from a stellar temperature ramp, so the field is not
    //    monochrome white.
    Properties
    {
        _Density ("Star Density (cells across)", Range(10, 200)) = 68
        _Brightness ("Brightness", Range(0, 4)) = 1.0
        _CoreSize ("Core Size", Range(0.002, 0.08)) = 0.020
        _GlowSize ("Glow Size", Range(0.02, 0.6)) = 0.16
        _TwinkleAmount ("Twinkle Amount", Range(0, 1)) = 0.45
        _TwinkleSpeed ("Twinkle Speed", Range(0, 6)) = 1.4
        _SkyColor ("Deep Sky Color", Color) = (0.012, 0.018, 0.032, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Density;
                float _Brightness;
                float _CoreSize;
                float _GlowSize;
                float _TwinkleAmount;
                float _TwinkleSpeed;
                float4 _SkyColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float2 Hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Rough main-sequence colour ramp, blue-white through to red dwarf.
            float3 StarColor(float t)
            {
                float3 blue = float3(0.72, 0.80, 1.00);
                float3 white = float3(1.00, 0.98, 0.96);
                float3 yellow = float3(1.00, 0.93, 0.78);
                float3 orange = float3(1.00, 0.80, 0.60);
                float3 red = float3(1.00, 0.68, 0.50);

                float3 c = lerp(blue, white, smoothstep(0.00, 0.22, t));
                c = lerp(c, yellow, smoothstep(0.22, 0.48, t));
                c = lerp(c, orange, smoothstep(0.48, 0.75, t));
                c = lerp(c, red, smoothstep(0.75, 1.00, t));
                return c;
            }

            // One layer of stars on a jittered grid. Neighbouring cells are
            // sampled too, so a star near a cell edge is not clipped by it.
            float3 StarLayer(float2 uv, float density, float sizeScale, float seed)
            {
                float2 grid = uv * density;
                float2 cell = floor(grid);
                float2 local = frac(grid);

                float3 accum = 0.0;

                [unroll]
                for (int oy = -1; oy <= 1; oy++)
                {
                    [unroll]
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        float2 offset = float2(ox, oy);
                        float2 id = cell + offset + seed;

                        float2 jitter = Hash22(id);
                        float presence = Hash12(id + 7.13);

                        // Most cells are empty; a sparse field reads far more
                        // like a sky than one star per cell ever does.
                        if (presence < 0.62)
                        {
                            continue;
                        }

                        // Magnitude: pow() with a high exponent leaves only a
                        // few percent of stars visibly bright.
                        float m = Hash12(id + 19.7);
                        float mag = pow(m, 6.0);

                        float2 delta = local - offset - jitter;
                        float dist = length(delta);

                        // Brighter stars present a larger disc, as they do
                        // through any real optic.
                        float radius = sizeScale * (0.55 + (mag * 1.9));

                        float core = exp(-(dist * dist) / max(radius * radius, 1e-6));
                        float wing = radius / (radius + (dist * dist * 42.0));
                        float shape = core + (wing * 0.10);

                        // Scintillation. Two incommensurate rates per star so
                        // the field never visibly loops, and faint stars flicker
                        // proportionally harder - which is what the eye expects.
                        float phase = Hash12(id + 3.77) * 6.2831853;
                        float rate = 0.6 + (Hash12(id + 11.3) * 1.8);
                        float t = _Time.y * _TwinkleSpeed * rate;
                        float flicker = (sin(t + phase) * 0.6) + (sin((t * 1.618) + (phase * 2.1)) * 0.4);
                        float amount = _TwinkleAmount * lerp(1.0, 0.35, mag);
                        float twinkle = 1.0 + (flicker * amount);

                        float3 color = StarColor(Hash12(id + 5.19));
                        accum += color * shape * mag * max(twinkle, 0.0);
                    }
                }

                return accum;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Screen-space UV with the aspect folded in, so stars stay round
                // and the field does not stretch when the window is resized.
                float2 uv = input.positionCS.xy / _ScreenParams.xy;
                uv.x *= _ScreenParams.x / max(_ScreenParams.y, 1.0);

                // Two layers at different scales: a dense faint field plus a
                // sparser bright one. A single layer always reads as a regular
                // lattice no matter how much the cells are jittered.
                float3 stars = StarLayer(uv, _Density, _CoreSize, 0.0);
                stars += StarLayer(uv, _Density * 0.43, _GlowSize * 0.22, 41.7) * 1.6;

                float3 color = _SkyColor.rgb + (stars * _Brightness);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
