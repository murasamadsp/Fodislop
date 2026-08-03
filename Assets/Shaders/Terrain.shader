Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture Atlas", 2D) = "white" {}
        _FlowMap ("Shimmer Flow Map", 2D) = "gray" {}
        _ShimmerColor ("Shimmer Color", Color) = (1,1,1,1)
        _FallbackColor ("Fallback Color", Color) = (1, 1, 0, 1)
        _DebugColor ("Debug Color", Color) = (1, 0, 1, 1)
        [ToggleUI] _DebugMode ("Debug Mode", Float) = 0
        [ToggleUI] _UseLight2D ("Use Light2D", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPosAttr : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float4 packedData   : TEXCOORD5;
                float4 glowAttr     : TEXCOORD6;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPos     : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float4 packedData   : TEXCOORD5;
                float3 worldPosition : TEXCOORD6;
                float4 glowData     : TEXCOORD7;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShimmerColor;
                float4 _FallbackColor;
                float4 _DebugColor;
                float _DebugMode;
                float _UseLight2D;
            CBUFFER_END

            Texture2D<float4> _WorldLightTexture;
            SamplerState sampler_WorldLightTexture;
            float4 _WorldLightRect;
            float4 _WorldLightTextureSize;
            int _WorldLightDebugView;

            float3 GetTerrariaLightColor(float2 worldPos)
            {
                if (_UseLight2D <= 0.5)
                {
                    return float3(1.0, 1.0, 1.0);
                }

                float2 lightUV = (worldPos - _WorldLightRect.xy) / _WorldLightRect.zw;
                if (_WorldLightDebugView != 0)
                {
                    int2 debugPixel = clamp(
                        int2(lightUV * _WorldLightTextureSize.xy),
                        int2(0, 0),
                        int2(_WorldLightTextureSize.xy) - 1);
                    return _WorldLightTexture.Load(int3(debugPixel, 0)).rgb;
                }

                return _WorldLightTexture.Sample(
                    sampler_WorldLightTexture,
                    lightUV).rgb;
            }

            float3 RgbToHsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 HsvToRgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
            }

            float3 SampleFlowMap(float2 worldPos)
            {
                return SAMPLE_TEXTURE2D(_FlowMap, sampler_FlowMap, worldPos / float2(12.0, 10.0)).rgb;
            }

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.subAtlasRect = input.subAtlasRect;
                output.tileSizeUV = input.tileSizeUV;
                output.worldPos = input.worldPosAttr;
                output.worldPosition = TransformObjectToWorld(input.positionOS.xyz);
                output.glowData = input.glowAttr;
                output.animData = input.animData;
                output.packedData = input.packedData;

                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                if (_WorldLightDebugView != 0)
                {
                    return half4(
                        GetTerrariaLightColor(input.worldPosition.xy),
                        1.0);
                }

                // TBDR: no discard anywhere in this shader — transparent output instead.
                // discard kills Hidden Surface Removal on Apple GPUs; alpha-0 blending is visually identical.
                if (input.worldPos.w > 1.5) return half4(0.0, 0.0, 0.0, 0.0);
                if (input.subAtlasRect.z < 0.0001)
                {
                    if (input.color.a < 0.05)
                    {
                        return half4(0.0, 0.0, 0.0, 0.0);
                    }

                    float3 worldLight = GetTerrariaLightColor(input.worldPosition.xy);
                    return half4(input.color.rgb * worldLight, input.color.a);
                }
                if (input.color.a < 0.05) return half4(0.0, 0.0, 0.0, 0.0);

                if (_DebugMode > 0.5)
                {
                    return half4(_DebugColor.rgb, 1.0);
                }

                float2 baseUV = input.subAtlasRect.xy;
                float2 subAtlasSizeUV = input.subAtlasRect.zw;
                float2 tileSizeUV = input.tileSizeUV.xy;

                if (subAtlasSizeUV.x <= 0 || tileSizeUV.x <= 0)
                {
                    if (input.color.a < 0.05) return half4(0.0, 0.0, 0.0, 0.0);
                    float3 worldLight = GetTerrariaLightColor(input.worldPosition.xy);
                    return half4(input.color.rgb * worldLight, input.color.a);
                }

                float frameCount = input.tileSizeUV.z;
                float frameHeightTiles = input.tileSizeUV.w;
                float animOffsetUV = 0;

                if (frameCount > 1.5)
                {
                    float speed = input.animData.y;
                    if (speed <= 0) speed = 5;
                    float frameIndex = floor(fmod(_Time.y * speed, frameCount));
                    animOffsetUV = frameIndex * frameHeightTiles * tileSizeUV.y;
                }

                float2 tilesCount = ceil(subAtlasSizeUV / tileSizeUV - 0.0001);
                tilesCount = max(tilesCount, 1.0);

                float2 gPos = floor(input.worldPos.xy + 0.001);
                float2 wrapped;
                bool isTiling = fmod(input.worldPos.w, 2.0) > 0.5;

                const float EPS = 0.0001;
                float variantY = fmod(abs(gPos.y), tilesCount.y);
                wrapped.y = floor(tilesCount.y - EPS - variantY);

                if (isTiling)
                {
                    wrapped.x = floor(input.worldPos.z + EPS);
                }
                else
                {
                    wrapped.x = floor(fmod(abs(gPos.x), tilesCount.x) + EPS);
                }

                wrapped = clamp(wrapped, 0.0, tilesCount - 1.0);
                float2 tileOffsetUV = wrapped * tileSizeUV;
                float2 availableTileSize = min(tileSizeUV, subAtlasSizeUV - tileOffsetUV);
                float2 quadUV = input.uv;
                int animTypeEarly = (int)(input.animData.x + 0.5);
                bool isScrollAnimated = animTypeEarly == 4;
                if (input.packedData.x > 0.5)
                {
                    float2 anchoredUV = input.packedData.zw;
                    float2 stepUV = float2(0.0, 0.0);
                    stepUV.x = anchoredUV.x >= 1.0 ? 1.0 : (anchoredUV.x <= 0.0 ? -1.0 : 0.0);
                    stepUV.y = anchoredUV.y >= 1.0 ? -1.0 : (anchoredUV.y <= 0.0 ? 1.0 : 0.0);
                    bool outsideX = stepUV.x != 0.0;
                    bool outsideY = stepUV.y != 0.0;
                    if (isScrollAnimated)
                    {
                        quadUV.x = outsideX ? frac(anchoredUV.x) : anchoredUV.x;
                        quadUV.y = anchoredUV.y;
                    }
                    else
                    {
                        quadUV = (outsideX || outsideY) ? frac(anchoredUV) : anchoredUV;
                    }

                    if (outsideX || outsideY)
                    {
                        float2 stepPos = gPos + stepUV;
                        float2 wrappedStep;
                        float stepVariantY = fmod(abs(stepPos.y), tilesCount.y);
                        wrappedStep.y = floor(tilesCount.y - EPS - stepVariantY);
                        if (isTiling)
                        {
                            wrappedStep.x = floor(input.worldPos.z + stepUV.x + EPS);
                        }
                        else
                        {
                            wrappedStep.x = floor(fmod(abs(stepPos.x), tilesCount.x) + EPS);
                        }

                        wrappedStep = clamp(wrappedStep, 0.0, tilesCount - 1.0);
                        if (isScrollAnimated)
                        {
                            wrappedStep.y = wrapped.y;
                        }

                        tileOffsetUV = wrappedStep * tileSizeUV;
                        availableTileSize = min(tileSizeUV, subAtlasSizeUV - tileOffsetUV);
                    }
                }

                quadUV.x = clamp(quadUV.x, EPS, 1.0 - EPS);
                if (!isScrollAnimated || input.packedData.x <= 0.5)
                {
                    quadUV.y = clamp(quadUV.y, EPS, 1.0 - EPS);
                }

                float2 finalUV = baseUV + tileOffsetUV + quadUV * availableTileSize;
                finalUV.y += animOffsetUV;

                if (isScrollAnimated)
                {
                    float speed = input.animData.y;
                    if (speed <= 0) speed = 5;
                    float scrollUV = fmod(_Time.y * speed * tileSizeUV.y * 0.05, subAtlasSizeUV.y);
                    finalUV.y = baseUV.y + fmod(finalUV.y - baseUV.y + scrollUV + subAtlasSizeUV.y, subAtlasSizeUV.y);
                }

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, finalUV);

                if (texColor.a < 0.05)
                {
                    return half4(0.0, 0.0, 0.0, 0.0);
                }

                float3 finalRgb = texColor.rgb;
                int animType = (int)(input.animData.x + 0.5);
                float speed = input.animData.y;
                float offset = input.animData.z;

                // Relief connectivity remains mesh metadata, but it must not
                // paint synthetic triangular shadows over the source texture.
                // All terrain darkening comes from the world light texture.

                if (animType == 1) // Blinking
                {
                    float pulse = 0.5 + 0.5 * sin(_Time.y * speed * 0.5 + offset);
                    finalRgb *= pulse;
                }
                else if (animType == 2) // Shimmer
                {
                    float2 pixelWorldPos = input.worldPos.xy + input.uv;
                    float3 flowSample = SampleFlowMap(pixelWorldPos);

                    float3 flowHsv = RgbToHsv(flowSample);
                    float hueAngle = flowHsv.x * 6.28318548;
                    float chroma = max(flowSample.r, max(flowSample.g, flowSample.b)) - min(flowSample.r, min(flowSample.g, flowSample.b));

                    float wave = sin(-(hueAngle + _Time.y * speed * 0.05));
                    wave = (wave + 1.0) * 0.5;
                    float waveCubed = wave * wave * wave;

                    float luminance = dot(texColor.rgb, float3(0.299, 0.587, 0.114));
                    float invLum = 1.0 - luminance;
                    float lumMask = 1.0 - invLum * invLum * invLum;

                    float factor = waveCubed * lumMask * chroma;

                    finalRgb = lerp(finalRgb, _ShimmerColor.rgb, factor);
                }
                else if (animType == 3) // Rainbow
                {
                    float3 hsv = RgbToHsv(finalRgb);
                    hsv.x = frac(hsv.x + _Time.y * (speed / 255.0));
                    finalRgb = HsvToRgb(hsv);
                }

                float finalAlpha = 1.0;
                float4 glowFlags = input.glowData;
                int iflags = int(round(glowFlags.z));
                bool isRoundable = (iflags & 2) != 0;
                if (isRoundable)
                {
                    int sameMask = int(round(glowFlags.w));
                    float4 bits = frac(sameMask * float4(0.5, 0.25, 0.125, 0.0625));
                    bool4 hasSame = bits >= 0.5;
                    float2 p = input.uv - 0.5;
                    float rTL = (hasSame.x || hasSame.y) ? 0.0 : 0.5;
                    float rTR = (hasSame.x || hasSame.w) ? 0.0 : 0.5;
                    float rBL = (hasSame.z || hasSame.y) ? 0.0 : 0.5;
                    float rBR = (hasSame.z || hasSame.w) ? 0.0 : 0.5;
                    float dist = length(p);
                    float aa = min(fwidth(dist), 1.0 / 16.0);
                    float alpha = 1.0 - smoothstep(0.51 - aa, 0.51 + aa, dist);
                    if (rTL < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, -p.x) * smoothstep(-aa, 0.0, p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rTR < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, p.x) * smoothstep(-aa, 0.0, p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rBL < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, -p.x) * smoothstep(-aa, 0.0, -p.y);
                        alpha = max(alpha, fill);
                    }
                    if (rBR < 0.25)
                    {
                        float fill = smoothstep(-aa, 0.0, p.x) * smoothstep(-aa, 0.0, -p.y);
                        alpha = max(alpha, fill);
                    }
                    float cornerDist = abs(abs(p.x) - abs(p.y));
                    float cornerExclude = smoothstep(0.4, 0.5, cornerDist);
                    alpha = lerp(alpha, 1.0, cornerExclude);
                    finalAlpha *= alpha;
                }
                float3 lightColor = GetTerrariaLightColor(input.worldPosition.xy);
                float3 litRgb = finalRgb * lightColor;
                if (finalAlpha < 0.99 && finalAlpha > 0.01)
                {
                    litRgb /= max(finalAlpha, 0.15);
                }

                return half4(litRgb, finalAlpha);
            }
            ENDHLSL
        }
        Pass
        {
            Name "LightingMaterialField"
            Tags { "LightMode" = "FodinaeLightingMaterialField" }

            Blend One One
            BlendOp Max
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex MaterialFieldVert
            #pragma fragment MaterialFieldFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct MaterialFieldAttributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 worldPosAttr : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float4 glowAttr     : TEXCOORD6;
            };

            struct MaterialFieldVaryings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 worldPos     : TEXCOORD1;
                float4 animData     : TEXCOORD2;
                float4 glowData     : TEXCOORD3;
                nointerpolation float isForeground : TEXCOORD4;
            };

            struct MaterialFieldOutput
            {
                half4 material : SV_Target0;
                half4 emission : SV_Target1;
            };

            MaterialFieldVaryings MaterialFieldVert(MaterialFieldAttributes input)
            {
                MaterialFieldVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.worldPos = input.worldPosAttr;
                output.animData = input.animData;
                output.glowData = input.glowAttr;
                output.isForeground = input.positionOS.z < 0.05 ? 1.0 : 0.0;
                return output;
            }

            float PhysicalContour(
                float2 uv,
                int solidBoundaryMask,
                int solidDiagonalMask)
            {
                float4 connected = frac(solidBoundaryMask * float4(0.5, 0.25, 0.125, 0.0625));
                bool top = connected.x >= 0.5;
                bool left = connected.y >= 0.5;
                bool bottom = connected.z >= 0.5;
                bool right = connected.w >= 0.5;
                float2 p = uv - 0.5;
                float antialias = min(fwidth(length(p)), 1.0 / 16.0);
                float contour = 1.0 - smoothstep(0.5 - antialias, 0.5 + antialias, length(p));
                contour = (top || left) && p.x <= 0.0 && p.y >= 0.0 ? 1.0 : contour;
                contour = (top || right) && p.x >= 0.0 && p.y >= 0.0 ? 1.0 : contour;
                contour = (bottom || left) && p.x <= 0.0 && p.y <= 0.0 ? 1.0 : contour;
                contour = (bottom || right) && p.x >= 0.0 && p.y <= 0.0 ? 1.0 : contour;
                float4 diagonal = frac(
                    solidDiagonalMask * float4(0.5, 0.25, 0.125, 0.0625));
                contour = diagonal.x >= 0.5 && p.x <= 0.0 && p.y >= 0.0
                    ? 1.0
                    : contour;
                contour = diagonal.y >= 0.5 && p.x >= 0.0 && p.y >= 0.0
                    ? 1.0
                    : contour;
                contour = diagonal.z >= 0.5 && p.x <= 0.0 && p.y <= 0.0
                    ? 1.0
                    : contour;
                contour = diagonal.w >= 0.5 && p.x >= 0.0 && p.y <= 0.0
                    ? 1.0
                    : contour;
                return contour;
            }

            MaterialFieldOutput MaterialFieldFrag(MaterialFieldVaryings input)
            {
                MaterialFieldOutput output;
                float isForeground = input.isForeground;
                uint packedColor = (uint)round(input.glowData.x);
                float3 surfaceAlbedo = float3(
                    packedColor & 255u,
                    (packedColor >> 8) & 255u,
                    (packedColor >> 16) & 255u) / 255.0;
                uint lightingFlags = (uint)floor(input.glowData.y + 0.0001);
                int solidBoundaryMask = int(lightingFlags & 15u);
                int solidDiagonalMask = (int(round(input.glowData.z)) >> 2) & 15;
                float emissionStrength = (lightingFlags & 16u) != 0u
                    ? saturate(frac(input.glowData.y) * 4.0)
                    : 0.0;
                bool hasRoundedPhysicalContour = (lightingFlags & 32u) != 0u;
                float occupancy = step(0.5, input.animData.w) * isForeground;
                occupancy *= hasRoundedPhysicalContour
                    ? PhysicalContour(
                        input.uv,
                        solidBoundaryMask,
                        solidDiagonalMask)
                    : 1.0;
                float surface = step(0.05, input.color.a) * isForeground;
                output.material = half4(surfaceAlbedo * surface, occupancy);
                output.emission = half4(
                    surfaceAlbedo * emissionStrength * surface,
                    emissionStrength * surface);
                return output;
            }
            ENDHLSL
        }
    }
}
