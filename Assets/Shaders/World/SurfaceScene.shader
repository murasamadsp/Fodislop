Shader "Kern/Surface Scene"
{
    Properties
    {
        _BaseMap ("Albedo or cutout", 2D) = "black" {}
        _LayerKind ("Sky / satellite / ridge / rock", Float) = 0
        _ZWrite ("Depth write", Float) = 0
        _Fog ("Atmosphere path", Float) = 0
        _Emission ("Crack radiance in paper white units", Float) = 0
        _Density ("Atmosphere density", Float) = 0
        _Seed ("Static scene seed", Float) = 0
        _SurfaceY ("Surface altitude in cells", Float) = 0
        _GroundBandHeight ("Reserved gameplay surface height in cells", Float) = 0
        _WorldWidth ("World width in cells", Float) = 0
        _CameraX ("Camera position in cells", Float) = 0
        _AtmosphereColor ("Atmosphere linear color", Color) = (0,0,0,1)
        _RockColor ("Rock linear color", Color) = (0,0,0,1)
        _CrackColor ("Crack linear color", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Universal2D"
            Tags { "LightMode"="Universal2D" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WorldRenderGrid.hlsl"
            #include "SurfaceGeology.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float _LayerKind, _Fog, _Emission, _Density, _Seed, _SurfaceY, _WorldWidth, _CameraX;
                float _GroundBandHeight;
                float4 _AtmosphereColor, _RockColor, _CrackColor;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = KernWorldGridVertex(TransformObjectToWorld(input.positionOS));
                output.positionCS = KernWorldGridClipPosition(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 samplePosition)
            {
                float3 p = frac(float3(samplePosition.xyx) * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise(float2 samplePosition)
            {
                float2 cell = floor(samplePosition);
                float2 uv = frac(samplePosition);
                uv = uv * uv * (3.0 - 2.0 * uv);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1,0)), uv.x),
                    lerp(Hash(cell + float2(0,1)), Hash(cell + 1), uv.x), uv.y);
            }

            float Clouds(float2 samplePosition)
            {
                float value = Noise(samplePosition) * 0.55;
                value += Noise(samplePosition * 2.13 + 13.0) * 0.25;
                value += Noise(samplePosition * 4.61 + 29.0) * 0.13;
                value += Noise(samplePosition * 9.17 + 51.0) * 0.07;
                return value;
            }

            float3 Sky(float2 position, float celestial)
            {
                float height = position.y - _SurfaceY;
                float2 sky = float2(position.x - _CameraX, height);
                float cloud = Clouds(float2(position.x, height) * float2(0.095, 0.15) + _Seed * 0.017);
                float cloudShadow = smoothstep(0.37, 0.62, cloud);
                float cloudEdge = exp(-abs(cloud - 0.46) * 34.0);
                float cloudRelief = saturate((ddx(cloud) - ddy(cloud)) * 90.0 + 0.5);
                float horizon = exp(-height * 0.028);
                float3 color = _AtmosphereColor.rgb * lerp(0.34, 1.16, horizon);
                color *= lerp(1.05, 0.15, cloudShadow);
                float sunProximity = exp(-length(sky - float2(-12, 22)) * 0.045);
                color += _AtmosphereColor.rgb * cloudEdge * cloudRelief * sunProximity * 0.65;
                float2 delta = sky - float2(-12, 22);
                float distanceToStar = length(delta);
                float transmission = exp(-_Density * (1.8 + cloudShadow * 3.0));
                float disk = 1.0 - smoothstep(0.33, 0.52, distanceToStar);
                float halo = exp(-distanceToStar * 0.4) * 0.26;
                color += float3(12.0, 2.4, 0.2) * (disk + halo) * transmission * celestial;
                float2 stars = sky * 3.0;
                float star = step(0.9965, Hash(floor(stars) + _Seed));
                star *= pow(saturate(1.0 - length(frac(stars) - 0.5) * 2.0), 8.0);
                color += celestial * star * float3(0.30, 0.34, 0.30) * exp(-_Density * (5.0 + cloudShadow * 8.0));
                return color;
            }

            float4 Basalt(float2 worldUV)
            {
                return KernSampleSurfaceBasalt(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap),
                    worldUV, _Seed, _Fog * 4.0, false);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float height = input.positionWS.y - _SurfaceY;
                // The original gameplay surface owns its complete transition band.
                // Decorative depth and albedo must never enter its transparent edges.
                clip(height - (_LayerKind > 2.5 ? _GroundBandHeight : 0.0));
                if (_LayerKind < 0.5)
                {
                    return float4(Sky(input.positionWS.xy, 1.0), 1);
                }

                float2 uv = input.uv;
                if (_LayerKind > 2.5)
                {
                    uv *= 0.25;
                }
                float4 sampleColor = _LayerKind > 2.5 ? Basalt(uv) : SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                float3 color;
                float alpha = sampleColor.a;
                if (_LayerKind > 2.5)
                {
                    if (_LayerKind > 3.5)
                    {
                        clip(input.positionWS.x);
                        clip(_WorldWidth - input.positionWS.x);
                    }
                    float3 normal = normalize(input.normalWS);
                    float lighting = 0.12 + max(0.0, dot(normal, normalize(float3(-0.5, 0.72, -0.48)))) * 1.7;
                    float tintMaximum = max(0.0001, max(_RockColor.r, max(_RockColor.g, _RockColor.b)));
                    float3 rockTint = _LayerKind > 3.5 ? float3(0.72, 0.26, 0.12) : float3(0.24, 0.28, 0.19);
                    float3 albedo = KernWeatheredSurfaceAlbedoTint(sampleColor.rgb * (_RockColor.rgb / tintMaximum),
                        input.positionWS.xy, _SurfaceY, rockTint);
                    // Broad mineral patches and discontinuous strata expose the shape
                    // before fine albedo detail. All factors are reflectance masks.
                    float2 geological = input.uv + _Seed * 0.013;
                    float mineral = lerp(0.45, 1.0, Noise(geological * 0.19));
                    float strata = Noise(float2(geological.x * 0.4, geological.y * 2.4)
                        + Noise(geological * 0.15) * 3.5);
                    float fracture = lerp(0.55, 1.0, smoothstep(0.035, 0.22, strata));
                    float topFace = smoothstep(0.45, 0.85, normal.y);
                    float foregroundRelief = _LayerKind > 3.5 ? lerp(0.85, 2.8, topFace) : 1.0;
                    color = albedo * lighting * mineral * fracture * 1.65 * foregroundRelief;
                    float2 rock = input.uv;
                    float cell = floor(rock.x * 0.35);
                    float vein = abs(sin(rock.x * 2.7 + Noise(rock * 1.7) * 4.0));
                    float crack = (1.0 - smoothstep(0.015, 0.065, vein)) * step(0.97, Hash(float2(cell, _Seed)));
                    crack *= exp(-max(0.0, height - _GroundBandHeight) * 0.9);
                    color += _CrackColor.rgb * _Emission * crack;
                    alpha = 1.0;
                }
                else
                {
                    color = sampleColor.rgb * (_LayerKind < 1.5 ? 0.24 : 0.6);
                    if (_LayerKind > 1.5)
                    {
                        alpha *= smoothstep(0.0, 0.045, uv.x) * smoothstep(0.0, 0.045, 1.0 - uv.x);
                    }
                }
                float valleyMist = exp(-max(0.0, height - _GroundBandHeight) * 0.24);
                float mistBreakup = Noise(input.positionWS.xy * float2(0.12, 0.32) + _Seed);
                float fog = saturate(_Fog + valleyMist * mistBreakup * _Density * 0.18);
                color = lerp(color, (Sky(input.positionWS.xy, 0.0) * 0.65 + _AtmosphereColor.rgb * 0.16), fog);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
