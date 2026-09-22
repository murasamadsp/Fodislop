Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        // Runtime materials must inject both textures. Neutral shader values
        // deliberately make a missing injection visible instead of rendering
        // an implicit white/gray world.
        [MainTexture] _BaseMap ("Texture Atlas", 2D) = "black" {}
        _PrismaticFlowMap ("X Crystal Phase Vectors", 2D) = "black" {}
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
            #include "Assets/Shaders/TerrainDebugView.hlsl"
            #include "Assets/Shaders/TerrainAtlasSampling.hlsl"
            #include "Assets/Shaders/TerrainSampling.hlsl"
            #include "Assets/Shaders/TerrainContour.hlsl"
            #include "Assets/Shaders/TerrainDecals.hlsl"

            #define EPS 0.0001

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_PrismaticFlowMap);
            SAMPLER(sampler_PrismaticFlowMap);
            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);

            #include "Assets/Shaders/TerrainMaterialCBuffer.hlsl"
            #include "Assets/Shaders/TerrainPassCommon.hlsl"

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


            Varyings vert (TerrainVertexInput input)
            {
                Varyings output = (Varyings)0;
            #if defined(KERN_TERRAIN_CELLS)
                TERRAIN_RESOLVE_CELL_VERTEX(input, output)
                output.worldPosition = TransformObjectToWorld(cell.positionOS);
                return output;
            #else
                TERRAIN_RESOLVE_ATTRIBUTE_VERTEX(input, output)
                output.worldPosition = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            #endif
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

                TerrainSurfaceInputs surface = BuildTerrainSurfaceInputs(
                    input.packedData,
                    input.uv,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.glowData,
                    input.animData.w);
                int animationProfile = surface.animationProfile;
                float applyGeometry = 0.0;
            #if defined(KERN_TERRAIN_CELLS)
                // Geometry belongs to the foreground layer.  Keep the
                // background quad rectangular so it can fill the area exposed
                // by a displaced foreground silhouette.
                applyGeometry = input.isForeground;
            #endif
                float cellCoverage = EvaluateTerrainCellCoverage(
                    surface,
                    TerrainContourAntialiasScale(animationProfile),
                    applyGeometry);

                // Отладка идёт ДО вырезания, а не после.
                //
                // Пока clip стоял первым, вид «Силуэт клетки» не мог показать
                // вырезанный пиксель: его уже не существовало, и до сравнения
                // доживали только те фрагменты, у которых покрытие и так выше
                // половины. Вид заливал кадр бирюзой всегда, независимо от
                // того, работает силуэт или нет, — то есть был не видом, а
                // заливкой. Остальные виды рисуются теперь во весь несущий
                // прямоугольник клетки, и это верно: они показывают термы
                // клетки, а не её видимую форму.
                if (KernTerrainDebugActive())
                {
                    float debugOcclusion = 1.0;
                    #ifdef KERN_WORLD_LIGHTING
                    debugOcclusion = KernTerrainAmbientOcclusionMultiplier(
                        input.glowData.y,
                        input.worldPosition.xy,
                        _WorldLightRect);
                    #endif
                    float debugForeground = 1.0;
                #if defined(KERN_TERRAIN_CELLS)
                    debugForeground = input.isForeground;
                #endif
                    return half4(
                        KernTerrainDebugColor(
                            surface,
                            cellCoverage,
                            debugForeground,
                            input.worldPos.z,
                            debugOcclusion),
                        1.0);
                }

            #if defined(KERN_TERRAIN_CELLS)
                clip(cellCoverage - 0.5);
            #endif

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
                float4 atlasTexelSize = TerrainMaterialAtlasTexelSize(atlasSlot);

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
                float3 flowSample = TerrainResolveFlowSample(
                    animationProfile, animType, input.worldPos, input.packedData, _FlowScale);

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

                // Relief mask остаётся в cell data для диагностики и
                // downstream lighting, но не затемняет альбедо: это создавало
                // видимую рамку вокруг каждой клетки и плиточные щели.
                float3 finalRGB = texColor.rgb;
                finalRGB = AnimateTerrainColor(
                    finalRGB,
                    texColor.rgb,
                    input.uv,
                    TerrainAnimationWorldPosition(input.worldPos, input.packedData),
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

            TEXTURE2D(_PrismaticFlowMap);
            SAMPLER(sampler_PrismaticFlowMap);
            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);

            // Альбедо поля — из атласа тем же UV-конвейером, что видимый пасс.
            TEXTURE2D(_BaseMap);

            #include "Assets/Shaders/TerrainMaterialCBuffer.hlsl"
            #include "Assets/Shaders/TerrainPassCommon.hlsl"

            // Свой набор TEXCOORD: полю материалов не нужна мировая позиция —
            // свет оно не считает, оно его кормит.
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

            MaterialFieldVaryings MaterialFieldVert(TerrainVertexInput input)
            {
                MaterialFieldVaryings output = (MaterialFieldVaryings)0;
            #if defined(KERN_TERRAIN_CELLS)
                TERRAIN_RESOLVE_CELL_VERTEX(input, output)
            #else
                TERRAIN_RESOLVE_ATTRIBUTE_VERTEX(input, output)
            #endif
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

                // Материальное поле читает грубое альбедо для отражённого
                // света; здесь используется обычная линейная выборка.
            #if defined(KERN_TERRAIN_CELLS)
                return TerrainSampleAtlas(atlasSlot, sampler_LinearClamp, finalUV);
            #else
                return SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_LinearClamp, finalUV, 0);
            #endif
            }

            MaterialFieldOutput MaterialFieldFrag(MaterialFieldVaryings input)
            {
                MaterialFieldOutput output;

                // Тот же разбор вершины, что и в экранном проходе: поле
                // материалов обязано нести ровно то альбедо, которое видно,
                // иначе свет отскакивает от цвета, которого в кадре нет.
                TerrainSurfaceInputs surface = BuildTerrainSurfaceInputs(
                    input.packedData,
                    input.uv,
                    input.geometryCornersX,
                    input.geometryCornersY,
                    input.glowData,
                    input.animData.w);

                float isForeground = input.isForeground;
                int albedoAtlasSlot = (int)round(input.atlasIndex);
                float4 atlasTexelSize = TerrainMaterialAtlasTexelSize(albedoAtlasSlot);
                int albedoAnimationType = (int)(input.animData.x + 0.5);
                int albedoAnimationProfile = surface.animationProfile;
                float3 flowSample = TerrainResolveFlowSample(
                    albedoAnimationProfile,
                    albedoAnimationType,
                    input.worldPos,
                    input.packedData,
                    _FlowScale);

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
                float3 surfaceAlbedo = albedoTexel.a >= 0.05
                    ? albedoTexel.rgb
                    : 0.0;
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
                applyGeometry = input.isForeground;
            #endif
                float cellCoverage = EvaluateTerrainCellCoverage(
                    surface,
                    TerrainContourAntialiasScale(albedoAnimationProfile),
                    applyGeometry);
            #if defined(KERN_TERRAIN_CELLS)
                clip(cellCoverage - 0.5);
            #endif
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
                    TerrainAnimationWorldPosition(input.worldPos, input.packedData),
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

                // Маска присутствия материала в поле: прозрачные и фоновые
                // фрагменты не вносят в поле ни альбедо, ни свечения.
                float materialMask = step(0.05, input.color.a) * isForeground;
                output.material = half4(surfaceAlbedo * materialMask, occupancy);
                output.emission = half4(
                    surfaceAlbedo * emissionStrength * materialMask * cellCoverage,
                    emissionStrength * materialMask * cellCoverage);
                return output;
            }
            ENDHLSL
        }
    }
}
