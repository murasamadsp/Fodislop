Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture Atlas", 2D) = "white" {}
        _FlowMapRect ("Flow Map Rect", Vector) = (0,0,0,0)
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
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

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

            CBUFFER_START(UnityPerMaterial)
                float4 _FlowMapRect;
                float4 _ShimmerColor;
                float4 _FallbackColor;
                float4 _DebugColor;
                float _DebugMode;
                float _SimpleGraphics;
                float _UseLight2D;
            CBUFFER_END

            float _DarknessFactor;
            sampler2D _WorldLightTexture;
            float4 _WorldLightRect;

            float3 GetTerrariaLightColor(float2 worldPos)
            {
                float3 lightColor = float3(1.0, 1.0, 1.0);
                if (_UseLight2D > 0.5)
                {
                    float2 lightUV = (worldPos - _WorldLightRect.xy) / _WorldLightRect.zw;
                    lightColor = tex2D(_WorldLightTexture, lightUV).rgb;
                }
                return lightColor;
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
                float2 size = float2(12.0, 10.0);
                float2 uv = worldPos / size;

                float2 texelSize = 1.0 / size;
                float2 pixel = uv * size - 0.5;
                float2 f = frac(pixel);
                pixel = floor(pixel);

                float2 uv00 = (pixel + float2(0.5, 0.5)) * texelSize;
                float2 uv10 = (pixel + float2(1.5, 0.5)) * texelSize;
                float2 uv01 = (pixel + float2(0.5, 1.5)) * texelSize;
                float2 uv11 = (pixel + float2(1.5, 1.5)) * texelSize;

                uv00 = frac(uv00);
                uv10 = frac(uv10);
                uv01 = frac(uv01);
                uv11 = frac(uv11);

                float3 s00 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv00 * _FlowMapRect.zw).rgb;
                float3 s10 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv10 * _FlowMapRect.zw).rgb;
                float3 s01 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv01 * _FlowMapRect.zw).rgb;
                float3 s11 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv11 * _FlowMapRect.zw).rgb;

                return lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
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
                // TBDR: no discard anywhere in this shader — transparent output instead.
                // discard kills Hidden Surface Removal on Apple GPUs; alpha-0 blending is visually identical.
                if (input.worldPos.w > 1.5) return half4(0.0, 0.0, 0.0, 0.0);
                if (input.subAtlasRect.z < 0.0001)
                {
                    return input.color;
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
                    return input.color;
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
                float2 quadUV = clamp(input.uv, EPS, 1.0 - EPS);

                float2 finalUV = baseUV + tileOffsetUV + quadUV * availableTileSize;
                finalUV.y += animOffsetUV;

                int animTypeEarly = (int)(input.animData.x + 0.5);
                if (animTypeEarly == 4)
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

            CBUFFER_START(UnityPerMaterial)
                float4 _FlowMapRect;
                float4 _ShimmerColor;
                float4 _FallbackColor;
                float4 _DebugColor;
                float _DebugMode;
                float _SimpleGraphics;
                float _UseLight2D;
            CBUFFER_END

            float _DarknessFactor;
            sampler2D _WorldLightTexture;
            float4 _WorldLightRect;

            float3 GetTerrariaLightColor(float2 worldPos)
            {
                float3 lightColor = float3(1.0, 1.0, 1.0);
                if (_UseLight2D > 0.5)
                {
                    float2 lightUV = (worldPos - _WorldLightRect.xy) / _WorldLightRect.zw;
                    lightColor = tex2D(_WorldLightTexture, lightUV).rgb;
                }
                return lightColor;
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
                float2 size = float2(12.0, 10.0);
                float2 uv = worldPos / size;

                float2 texelSize = 1.0 / size;
                float2 pixel = uv * size - 0.5;
                float2 f = frac(pixel);
                pixel = floor(pixel);

                float2 uv00 = (pixel + float2(0.5, 0.5)) * texelSize;
                float2 uv10 = (pixel + float2(1.5, 0.5)) * texelSize;
                float2 uv01 = (pixel + float2(0.5, 1.5)) * texelSize;
                float2 uv11 = (pixel + float2(1.5, 1.5)) * texelSize;

                uv00 = frac(uv00);
                uv10 = frac(uv10);
                uv01 = frac(uv01);
                uv11 = frac(uv11);

                float3 s00 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv00 * _FlowMapRect.zw).rgb;
                float3 s10 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv10 * _FlowMapRect.zw).rgb;
                float3 s01 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv01 * _FlowMapRect.zw).rgb;
                float3 s11 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, _FlowMapRect.xy + uv11 * _FlowMapRect.zw).rgb;

                return lerp(lerp(s00, s10, f.x), lerp(s01, s11, f.x), f.y);
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
                // TBDR: no discard anywhere in this shader — transparent output instead.
                // discard kills Hidden Surface Removal on Apple GPUs; alpha-0 blending is visually identical.
                if (input.worldPos.w > 1.5) return half4(0.0, 0.0, 0.0, 0.0);
                if (input.subAtlasRect.z < 0.0001)
                {
                    return input.color;
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
                    return input.color;
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
                float2 quadUV = clamp(input.uv, EPS, 1.0 - EPS);

                float2 finalUV = baseUV + tileOffsetUV + quadUV * availableTileSize;
                finalUV.y += animOffsetUV;

                int animTypeEarly = (int)(input.animData.x + 0.5);
                if (animTypeEarly == 4)
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
            Name "OcclusionCoverage"
            Tags { "LightMode" = "FodinaeOcclusionCoverage" }

            Blend One One
            BlendOp Max
            ColorMask R
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex CoverageVert
            #pragma fragment CoverageFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct CoverageAttributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPosAttr : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float4 glowAttr     : TEXCOORD6;
            };

            struct CoverageVaryings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 subAtlasRect : TEXCOORD1;
                float4 tileSizeUV   : TEXCOORD2;
                float4 worldPos     : TEXCOORD3;
                float4 animData     : TEXCOORD4;
                float4 glowData     : TEXCOORD5;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CoverageVaryings CoverageVert(CoverageAttributes input)
            {
                CoverageVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.subAtlasRect = input.subAtlasRect;
                output.tileSizeUV = input.tileSizeUV;
                output.worldPos = input.worldPosAttr;
                output.animData = input.animData;
                output.glowData = input.glowAttr;
                return output;
            }

            half4 CoverageFrag(CoverageVaryings input) : SV_Target
            {
                float castsShadow = step(0.5, input.animData.w);
                if (castsShadow < 0.5 || input.worldPos.w > 1.5 || input.color.a < 0.05)
                {
                    return 0.0;
                }

                if (input.subAtlasRect.z < 0.0001 || input.tileSizeUV.x <= 0.0)
                {
                    return half4(castsShadow * input.color.a, 0.0, 0.0, 1.0);
                }

                float2 baseUV = input.subAtlasRect.xy;
                float2 subAtlasSizeUV = input.subAtlasRect.zw;
                float2 tileSizeUV = input.tileSizeUV.xy;
                float2 tilesCount = max(ceil(subAtlasSizeUV / tileSizeUV - 0.0001), 1.0);
                float2 gridPosition = floor(input.worldPos.xy + 0.001);
                float2 wrapped;
                bool isTiling = fmod(input.worldPos.w, 2.0) > 0.5;
                const float EPS = 0.0001;

                float variantY = fmod(abs(gridPosition.y), tilesCount.y);
                wrapped.y = floor(tilesCount.y - EPS - variantY);
                wrapped.x = isTiling
                    ? floor(input.worldPos.z + EPS)
                    : floor(fmod(abs(gridPosition.x), tilesCount.x) + EPS);
                wrapped = clamp(wrapped, 0.0, tilesCount - 1.0);

                float2 tileOffsetUV = wrapped * tileSizeUV;
                float2 availableTileSize = min(tileSizeUV, subAtlasSizeUV - tileOffsetUV);
                float2 quadUV = clamp(input.uv, EPS, 1.0 - EPS);
                float2 finalUV = baseUV + tileOffsetUV + quadUV * availableTileSize;

                // Coverage deliberately uses animation frame zero. Lighting
                // must not rebuild or shimmer whenever a visual frame changes.
                half textureAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, finalUV).a;
                if (textureAlpha < 0.05)
                {
                    return 0.0;
                }

                // Preserve the actual atlas alpha. A binary cutoff turns
                // translucent details into a solid square in the SDF and
                // prevents them from producing proportional soft shadows.
                float coverage = textureAlpha;
                int flags = int(round(input.glowData.z));
                if ((flags & 2) != 0)
                {
                    int sameMask = int(round(input.glowData.w));
                    float4 bits = frac(sameMask * float4(0.5, 0.25, 0.125, 0.0625));
                    bool4 hasSame = bits >= 0.5;
                    float2 p = input.uv - 0.5;
                    float rTL = (hasSame.x || hasSame.y) ? 0.0 : 0.5;
                    float rTR = (hasSame.x || hasSame.w) ? 0.0 : 0.5;
                    float rBL = (hasSame.z || hasSame.y) ? 0.0 : 0.5;
                    float rBR = (hasSame.z || hasSame.w) ? 0.0 : 0.5;
                    float distanceFromCenter = length(p);
                    float antialias = min(fwidth(distanceFromCenter), 1.0 / 16.0);
                    float roundCoverage = 1.0 - smoothstep(
                        0.51 - antialias,
                        0.51 + antialias,
                        distanceFromCenter);

                    if (rTL < 0.25)
                    {
                        roundCoverage = max(roundCoverage,
                            smoothstep(-antialias, 0.0, -p.x) * smoothstep(-antialias, 0.0, p.y));
                    }
                    if (rTR < 0.25)
                    {
                        roundCoverage = max(roundCoverage,
                            smoothstep(-antialias, 0.0, p.x) * smoothstep(-antialias, 0.0, p.y));
                    }
                    if (rBL < 0.25)
                    {
                        roundCoverage = max(roundCoverage,
                            smoothstep(-antialias, 0.0, -p.x) * smoothstep(-antialias, 0.0, -p.y));
                    }
                    if (rBR < 0.25)
                    {
                        roundCoverage = max(roundCoverage,
                            smoothstep(-antialias, 0.0, p.x) * smoothstep(-antialias, 0.0, -p.y));
                    }

                    float cornerDistance = abs(abs(p.x) - abs(p.y));
                    float cornerExclude = smoothstep(0.4, 0.5, cornerDistance);
                    roundCoverage = lerp(roundCoverage, 1.0, cornerExclude);
                    coverage *= roundCoverage;
                }

                return half4(saturate(coverage * castsShadow * input.color.a), 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }
}
