Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        // Runtime materials must inject both textures. Neutral shader values
        // deliberately make a missing injection visible instead of rendering
        // an implicit white/gray world.
        [MainTexture] _BaseMap ("Texture Atlas", 2D) = "black" {}
        _FlowMap ("Shimmer Flow Map", 2D) = "black" {}
        _TerrainDecalAtlas ("Terrain Decal Atlas", 2D) = "black" {}
        _ShimmerColor ("Shimmer Color", Color) = (0,0,0,0)
        _FlowScale ("Flow Scale", Vector) = (0,0,0,0)
        _ShimmerSpeedScale ("Shimmer Speed Scale", Float) = 0
        _PulseSpeedScale ("Pulse Speed Scale", Float) = 0
        _DebugColor ("Debug Color", Color) = (0,0,0,0)
        [ToggleUI] _DebugMode ("Debug Mode", Float) = 0
        [HideInInspector] _TerrainAtlasIndex ("Terrain Atlas Index", Float) = 0
        [HideInInspector] _TerrainAtlas0 ("Terrain Atlas 0", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas1 ("Terrain Atlas 1", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas2 ("Terrain Atlas 2", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas3 ("Terrain Atlas 3", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas4 ("Terrain Atlas 4", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas5 ("Terrain Atlas 5", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas6 ("Terrain Atlas 6", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas7 ("Terrain Atlas 7", 2D) = "black" {}
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
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ KERN_WORLD_LIGHTING
            #pragma multi_compile_local _ KERN_TERRAIN_CELLS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/TerrainColorAnimation.hlsl"
            #include "TerrainTileAddressing.hlsl"
            #include "Assets/Shaders/TerrainCellData.hlsl"
            #include "Assets/Shaders/TerrainLightingData.hlsl"
            #include "Assets/Shaders/TerrainAmbientOcclusion.hlsl"
            #include "Assets/Shaders/PixelArtFiltering.hlsl"
            #include "Assets/Shaders/WorldLightSampling.hlsl"
            #include "Assets/Shaders/TerrainAtlasSampling.hlsl"
            #include "Assets/Shaders/TerrainSampling.hlsl"
            #include "Assets/Shaders/TerrainContour.hlsl"
            #include "Assets/Shaders/TerrainDecals.hlsl"

            #define EPS 0.0001

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
                nointerpolation float atlasIndex : TEXCOORD8;
                nointerpolation float isForeground : TEXCOORD9;
                nointerpolation float4 geometryCornersX : TEXCOORD10;
                nointerpolation float4 geometryCornersY : TEXCOORD11;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);

            // ВСЁ, ЧТО ЗАВИСИТ ОТ МАТЕРИАЛА, ОБЯЗАНО ЛЕЖАТЬ ЗДЕСЬ.
            //
            // SRP Batcher склеивает вызовы отрисовки только у шейдеров, где ни
            // одно свойство материала не объявлено снаружи UnityPerMaterial.
            // `_BaseMap_TexelSize` Unity заводит сам под текстуру _BaseMap, то
            // есть это свойство материала; стоя снаружи, оно ломало совместимость
            // целиком, и батчер молча выключался на всём террейне — счётчик
            // пакетов показывал ноль при трёх сотнях смен материала.
            CBUFFER_START(UnityPerMaterial)
                float4 _ShimmerColor;
                float4 _FlowScale;
                float _ShimmerSpeedScale;
                float _PulseSpeedScale;
                float4 _DebugColor;
                float _DebugMode;
                float4 _BaseMap_TexelSize;
                float4 _FlowMap_TexelSize;
                float4 _TerrainDecalAtlas_TexelSize;
                float _TerrainAtlasIndex;
                float4 _TerrainAtlas0_TexelSize;
                float4 _TerrainAtlas1_TexelSize;
                float4 _TerrainAtlas2_TexelSize;
                float4 _TerrainAtlas3_TexelSize;
                float4 _TerrainAtlas4_TexelSize;
                float4 _TerrainAtlas5_TexelSize;
                float4 _TerrainAtlas6_TexelSize;
                float4 _TerrainAtlas7_TexelSize;
            CBUFFER_END

            float4 GetAtlasTexelSize(int slot)
            {
            #if defined(KERN_TERRAIN_CELLS)
                return TerrainAtlasTexelSize(
                    slot,
                    _TerrainAtlas0_TexelSize,
                    _TerrainAtlas1_TexelSize,
                    _TerrainAtlas2_TexelSize,
                    _TerrainAtlas3_TexelSize,
                    _TerrainAtlas4_TexelSize,
                    _TerrainAtlas5_TexelSize,
                    _TerrainAtlas6_TexelSize,
                    _TerrainAtlas7_TexelSize);
            #else
                return _BaseMap_TexelSize;
            #endif
            }

            half4 SampleAtlasColor(int slot, float2 uv)
            {
            #if defined(KERN_TERRAIN_CELLS)
                return _PixelArtFiltering < 0.5
                    ? TerrainSampleAtlas(slot, sampler_PointClamp, uv)
                    : TerrainSampleAtlas(slot, sampler_LinearClamp, uv);
            #else
                return _PixelArtFiltering < 0.5
                    ? SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_PointClamp, uv, 0)
                    : SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_LinearClamp, uv, 0);
            #endif
            }

            float MissingTextureHash(float2 position)
            {
                float3 p = frac(float3(position, position.x + position.y) *
                    float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float3 SampleMissingTexture(float2 worldPosition)
            {
                float2 cell = floor(worldPosition);
                float hue = MissingTextureHash(cell);
                float value = lerp(0.35, 0.8, MissingTextureHash(cell + 17.0));
                float saturation = lerp(0.55, 0.9, MissingTextureHash(cell + 43.0));
                return TerrainHSVToRGB(float3(hue, saturation, value));
            }

            float3 SampleFlowMap(float2 worldPos)
            {
                return SAMPLE_TEXTURE2D(
                    _FlowMap,
                    sampler_FlowMap,
                    worldPos / _FlowScale.xy).rgb;
            }

            Varyings vert (Attributes input)
            {
                Varyings output;
            #if defined(KERN_TERRAIN_CELLS)
                TerrainCellVertex cell = LoadTerrainCellVertex(input.positionOS.xyz, input.uv);
                output.positionCS = cell.atlasIndex >= 0.0
                    ? TransformObjectToHClip(cell.positionOS)
                    : TerrainCulledPosition();
                output.atlasIndex = cell.atlasIndex;
                output.uv = cell.uv;
                output.color = cell.color;
                output.subAtlasRect = cell.subAtlasRect;
                output.tileSizeUV = cell.tileSizeUV;
                output.worldPos = cell.worldPos;
                output.worldPosition = TransformObjectToWorld(cell.positionOS);
                output.glowData = cell.glowData;
                output.geometryCornersX = cell.geometryCornersX;
                output.geometryCornersY = cell.geometryCornersY;
                output.animData = cell.animData;
                output.packedData = cell.packedData;
                output.isForeground = cell.layer > 0.5 ? 1.0 : 0.0;
                return output;
            #endif
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.subAtlasRect = input.subAtlasRect;
                output.tileSizeUV = input.tileSizeUV;
                output.worldPos = input.worldPosAttr;
                output.worldPosition = TransformObjectToWorld(input.positionOS.xyz);
                output.glowData = input.glowAttr;
                output.animData = input.animData;
                output.isForeground = input.positionOS.z < 0.05 ? 1.0 : 0.0;
                output.packedData = input.packedData;
                output.geometryCornersX = 0.0;
                output.geometryCornersY = 0.0;
                output.atlasIndex = 0.0;

                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                if (_WorldLightDebugView == 9)
                {
                    float occlusion = KernSampleTerrainAmbientOcclusion(
                        input.worldPosition.xy,
                        _WorldLightRect);
                    return half4(occlusion, occlusion, occlusion, 1.0);
                }

                if (_WorldLightDebugView != 0)
                {
                    return half4(
                        GetWorldLightColor(input.worldPosition.xy).rgb,
                        1.0);
                }

                // TBDR: no discard anywhere in this shader — transparent output instead.
                // discard kills Hidden Surface Removal on Apple GPUs; alpha-0 blending is visually identical.
                if (input.worldPos.w > 1.5) return half4(0.0, 0.0, 0.0, 0.0);
                int animationProfile = (int)(input.animData.w + 0.5);
                float applyGeometry = 0.0;
            #if defined(KERN_TERRAIN_CELLS)
                applyGeometry = 1.0;
            #endif
                float2 contourUV = input.packedData.x > 0.5
                    ? input.packedData.yz
                    : input.uv;
                float cellCoverage = EvaluateTerrainCellCoverage(
                    input.packedData.yz,
                    contourUV,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.packedData.x,
                    input.glowData.z,
                    input.glowData.y,
                    TerrainContourAntialiasScale(animationProfile),
                    applyGeometry);
                if (input.subAtlasRect.z < 0.0001)
                {
                    if (input.color.a < 0.05)
                    {
                        return half4(0.0, 0.0, 0.0, 0.0);
                    }

                    float4 worldLight = GetWorldLightColor(input.worldPosition.xy);
                    float3 diagnosticTexture = SampleMissingTexture(input.worldPos.xy);
                    return half4(
                        diagnosticTexture * worldLight.rgb,
                        input.color.a * cellCoverage);
                }
                if (input.color.a < 0.05) return half4(0.0, 0.0, 0.0, 0.0);

                int atlasSlot = (int)round(input.atlasIndex);
                float4 atlasTexelSize = GetAtlasTexelSize(atlasSlot);

                TerrainTileUvResult tileUV = ResolveTerrainTileUV(
                    input.uv,
                    input.subAtlasRect,
                    input.tileSizeUV,
                    input.worldPos,
                    input.animData,
                    input.packedData,
                    _Time.y,
                    atlasTexelSize.xy);

                if (!tileUV.isValid)
                {
                    float4 worldLight = GetWorldLightColor(input.worldPosition.xy);
                    return half4(0.0, 0.0, 0.0, input.color.a * cellCoverage * worldLight.r);
                }

                int animType = (int)(input.animData.x + 0.5);
                float3 flowSample = 0.0;
                if (TerrainAnimationUsesFlowMap(animType, animationProfile))
                {
                    // Geometric quad coordinates stay continuous when atlas
                    // UVs are rotated or mirrored by terrain autotiling.
                    float2 flowPosition = TerrainFlowSamplePosition(
                        input.worldPos.xy + input.packedData.yz,
                        animationProfile,
                        input.animData.y);
                    flowSample = SampleFlowMap(flowPosition);
                }

                float2 finalUV = AnimateTerrainSampleUV(
                    tileUV.finalUV,
                    input.subAtlasRect,
                    input.tileSizeUV.xy,
                    animationProfile,
                    flowSample);
                finalUV = PixelArtSampleUV(finalUV, atlasTexelSize.zw);
                finalUV = ClampTerrainTileUV(finalUV, tileUV);

                half4 texColor = SampleAtlasColor(atlasSlot, finalUV);
                if (texColor.a < 0.05)
                {
                    return half4(0.0, 0.0, 0.0, 0.0);
                }

                float3 finalRGB = texColor.rgb;
                finalRGB = AnimateTerrainColor(
                    finalRGB,
                    texColor.rgb,
                    input.uv,
                    animType,
                    animationProfile,
                    input.animData.y,
                    input.animData.z,
                    flowSample,
                    input.glowData.x,
                    _ShimmerColor.rgb,
                    _ShimmerSpeedScale,
                    _PulseSpeedScale);
                finalRGB = ApplyTerrainDecal(
                    finalRGB,
                    input.uv,
                    input.glowData.w);

                float finalAlpha = cellCoverage;

                float4 worldLight = GetWorldLightColor(input.worldPosition.xy);
                float3 litRGB = finalRGB * worldLight.rgb;

                #ifdef KERN_WORLD_LIGHTING
                litRGB *= KernTerrainAmbientOcclusionMultiplier(
                    input.glowData.y,
                    input.worldPosition.xy,
                    _WorldLightRect);
                #endif
                if (finalAlpha < 0.99 && finalAlpha > 0.01)
                {
                    litRGB /= max(finalAlpha, 0.15);
                }

                return half4(litRGB, finalAlpha);
            }
            ENDHLSL
        }
        Pass
        {
            Name "LightingMaterialField"
            Tags { "LightMode" = "KernLightingMaterialField" }

            Blend One One
            BlendOp Max
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex MaterialFieldVert
            #pragma fragment MaterialFieldFrag
            #pragma multi_compile_local _ KERN_TERRAIN_CELLS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/TerrainColorAnimation.hlsl"
            #include "Assets/Shaders/TerrainCellData.hlsl"
            #include "TerrainTileAddressing.hlsl"
            #include "Assets/Shaders/TerrainLightingData.hlsl"
            #include "Assets/Shaders/TerrainAtlasSampling.hlsl"
            #include "Assets/Shaders/TerrainSampling.hlsl"
            #include "Assets/Shaders/TerrainContour.hlsl"
            #include "Assets/Shaders/TerrainDecals.hlsl"

            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);

            // Тот же UnityPerMaterial, что и в пассе Universal2D, слово в слово.
            //
            // SRP Batcher требует, чтобы КАЖДЫЙ пасс шейдера объявлял этот
            // блок и объявлял его одинаково. Пасс без блока делает несовместимым
            // весь шейдер целиком, а не только себя, — и батчер выключался на
            // террейне даже после того, как `_BaseMap_TexelSize` переехал
            // внутрь. Здесь ни одно из этих свойств не читается; блок стоит
            // ради совпадения раскладки, и убирать его как «мёртвый» нельзя.
            CBUFFER_START(UnityPerMaterial)
                float4 _ShimmerColor;
                float4 _FlowScale;
                float _ShimmerSpeedScale;
                float _PulseSpeedScale;
                float4 _DebugColor;
                float _DebugMode;
                float4 _BaseMap_TexelSize;
                float4 _FlowMap_TexelSize;
                float4 _TerrainDecalAtlas_TexelSize;
                float _TerrainAtlasIndex;
                float4 _TerrainAtlas0_TexelSize;
                float4 _TerrainAtlas1_TexelSize;
                float4 _TerrainAtlas2_TexelSize;
                float4 _TerrainAtlas3_TexelSize;
                float4 _TerrainAtlas4_TexelSize;
                float4 _TerrainAtlas5_TexelSize;
                float4 _TerrainAtlas6_TexelSize;
                float4 _TerrainAtlas7_TexelSize;
            CBUFFER_END

            // Альбедо поля — из атласа тем же UV-конвейером, что видимый пасс.
            TEXTURE2D(_BaseMap);

            float4 GetFieldAtlasTexelSize(int slot)
            {
            #if defined(KERN_TERRAIN_CELLS)
                return TerrainAtlasTexelSize(
                    slot,
                    _TerrainAtlas0_TexelSize,
                    _TerrainAtlas1_TexelSize,
                    _TerrainAtlas2_TexelSize,
                    _TerrainAtlas3_TexelSize,
                    _TerrainAtlas4_TexelSize,
                    _TerrainAtlas5_TexelSize,
                    _TerrainAtlas6_TexelSize,
                    _TerrainAtlas7_TexelSize);
            #else
                return _BaseMap_TexelSize;
            #endif
            }

            struct MaterialFieldAttributes
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

            struct MaterialFieldVaryings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float4 worldPos     : TEXCOORD1;
                float4 animData     : TEXCOORD2;
                float4 packedData   : TEXCOORD3;
                float4 glowData     : TEXCOORD4;
                nointerpolation float isForeground : TEXCOORD5;
                float4 subAtlasRect : TEXCOORD6;
                float4 tileSizeUV   : TEXCOORD7;
                nointerpolation float atlasIndex : TEXCOORD8;
                nointerpolation float4 geometryCornersX : TEXCOORD9;
                nointerpolation float4 geometryCornersY : TEXCOORD10;
            };

            struct MaterialFieldOutput
            {
                half4 material : SV_Target0;
                half4 emission : SV_Target1;
            };

            MaterialFieldVaryings MaterialFieldVert(MaterialFieldAttributes input)
            {
                MaterialFieldVaryings output;
            #if defined(KERN_TERRAIN_CELLS)
                TerrainCellVertex cell = LoadTerrainCellVertex(input.positionOS.xyz, input.uv);
                output.positionCS = cell.atlasIndex >= 0.0
                    ? TransformObjectToHClip(cell.positionOS)
                    : TerrainCulledPosition();
                output.uv = cell.uv;
                output.color = cell.color;
                output.worldPos = cell.worldPos;
                output.animData = cell.animData;
                output.packedData = cell.packedData;
                output.glowData = cell.glowData;
                output.geometryCornersX = cell.geometryCornersX;
                output.geometryCornersY = cell.geometryCornersY;
                output.isForeground = cell.layer > 0.5 ? 1.0 : 0.0;
                output.subAtlasRect = cell.subAtlasRect;
                output.tileSizeUV = cell.tileSizeUV;
                output.atlasIndex = cell.atlasIndex;
                return output;
            #endif
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                output.worldPos = input.worldPosAttr;
                output.animData = input.animData;
                output.packedData = input.packedData;
                output.geometryCornersX = 0.0;
                output.geometryCornersY = 0.0;
                output.glowData = input.glowAttr;
                output.isForeground = input.positionOS.z < 0.05 ? 1.0 : 0.0;
                output.subAtlasRect = input.subAtlasRect;
                output.tileSizeUV = input.tileSizeUV;
                output.atlasIndex = 0.0;
                return output;
            }

            half4 SampleFieldAlbedoTexel(
                float2 cornerUV,
                float4 subAtlasRect,
                float4 tileSize,
                float4 worldPos,
                float4 animData,
                float4 packedData,
                int atlasSlot,
                float4 atlasTexelSize,
                int animationProfile,
                float3 flowSample)
            {
                TerrainTileUvResult tileUV = ResolveTerrainTileUV(
                    cornerUV,
                    subAtlasRect,
                    tileSize,
                    worldPos,
                    animData,
                    packedData,
                    _Time.y,
                    atlasTexelSize.xy);

                if (!tileUV.isValid)
                {
                    return half4(0.0, 0.0, 0.0, 0.0);
                }

                float2 finalUV = AnimateTerrainSampleUV(
                    tileUV.finalUV,
                    subAtlasRect,
                    tileSize.xy,
                    animationProfile,
                    flowSample);
                finalUV = ClampTerrainTileUV(finalUV, tileUV);

            #if defined(KERN_TERRAIN_CELLS)
                return TerrainSampleAtlas(atlasSlot, sampler_LinearClamp, finalUV);
            #else
                return SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_LinearClamp, finalUV, 0);
            #endif
            }

            MaterialFieldOutput MaterialFieldFrag(MaterialFieldVaryings input)
            {
                MaterialFieldOutput output;
                float isForeground = input.isForeground;
                int albedoAtlasSlot = (int)round(input.atlasIndex);
                float4 atlasTexelSize = GetFieldAtlasTexelSize(albedoAtlasSlot);
                int albedoAnimationType = (int)(input.animData.x + 0.5);
                int albedoAnimationProfile = (int)(input.animData.w + 0.5);
                float3 flowSample = 0.0;
                if (TerrainAnimationUsesFlowMap(
                    albedoAnimationType,
                    albedoAnimationProfile))
                {
                    float2 flowPosition = TerrainFlowSamplePosition(
                        input.worldPos.xy + input.packedData.yz,
                        albedoAnimationProfile,
                        input.animData.y);
                    flowSample = SAMPLE_TEXTURE2D(
                        _FlowMap,
                        sampler_FlowMap,
                        flowPosition / _FlowScale.xy).rgb;
                }

                half4 albedoTexel = SampleFieldAlbedoTexel(
                    input.uv,
                    input.subAtlasRect,
                    input.tileSizeUV,
                    input.worldPos,
                    input.animData,
                    input.packedData,
                    albedoAtlasSlot,
                    atlasTexelSize,
                    albedoAnimationProfile,
                    flowSample);

                // Без фолбеков: нет текселя — нет альбедо. Плоский цвет
                // миникарты сюда больше не попадает ни в каком виде.
                float3 surfaceAlbedo = albedoTexel.a >= 0.05 ? albedoTexel.rgb : 0.0;
                uint lightingFlags = KernTerrainLightingFlags(input.glowData.y);
                float emissionStrength = KernTerrainEmissionStrength(
                    input.glowData.y,
                    lightingFlags);
                bool isPhysicalMass = KernTerrainIsPhysicalMass(lightingFlags);
                // Occupancy — физическая масса переднего плана. isPhysicalMass уже
                // гарантирует !isBackground (фон не получает PhysicalMass),
                // поэтому isForeground здесь избыточен и только добавлял хрупкую
                // зависимость от точности positionOS.z.
                float applyGeometry = 0.0;
            #if defined(KERN_TERRAIN_CELLS)
                applyGeometry = 1.0;
            #endif
                int animationProfile = (int)(input.animData.w + 0.5);
                float2 contourUV = input.packedData.x > 0.5
                    ? input.packedData.yz
                    : input.uv;
                float cellCoverage = EvaluateTerrainCellCoverage(
                    input.packedData.yz,
                    contourUV,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.packedData.x,
                    input.glowData.z,
                    input.glowData.y,
                    TerrainContourAntialiasScale(animationProfile),
                    applyGeometry);
                float occupancy = isPhysicalMass
                    ? TerrainCellOccupancy(cellCoverage)
                    : 0.0;
                // Силуэт блока: occupancy повторяет видимую форму — скругление
                // выше, дырки по альфе текселя здесь. Тот же семпл, что пошёл
                // в альбедо, новой выборки нет. Без этого решётка или тайл
                // с прозрачными местами давили AO тенью как сплошной квадрат.
                occupancy *= albedoTexel.a >= 0.05 ? 1.0 : 0.0;

                surfaceAlbedo = AnimateTerrainColor(
                    surfaceAlbedo,
                    surfaceAlbedo,
                    input.uv,
                    albedoAnimationType,
                    albedoAnimationProfile,
                    input.animData.y,
                    input.animData.z,
                    flowSample,
                    input.glowData.x,
                    _ShimmerColor.rgb,
                    _ShimmerSpeedScale,
                    _PulseSpeedScale);
                surfaceAlbedo = ApplyTerrainDecal(
                    surfaceAlbedo,
                    input.uv,
                    input.glowData.w);

                float surface = step(0.05, input.color.a) * isForeground;
                output.material = half4(surfaceAlbedo * surface, occupancy);
                output.emission = half4(
                    surfaceAlbedo * emissionStrength * surface * cellCoverage,
                    emissionStrength * surface * cellCoverage);
                return output;
            }
            ENDHLSL
        }
    }
}
