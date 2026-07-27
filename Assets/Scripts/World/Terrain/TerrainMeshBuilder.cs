using System;
using System.Collections.Generic;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game.Managers;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.Scripts.World.Terrain
{
    public class TerrainMeshBuilder
    {
        private TerrainVertex[] _vertexBuffer;
        private float _cellSize;
        private static readonly Vector2[] _localUVsBuffer =
        {
            new(-0.70710678f, -0.70710678f),
            new(0.70710678f, -0.70710678f),
            new(0.70710678f, 0.70710678f),
            new(-0.70710678f, 0.70710678f),
        };
        private static readonly HashSet<CellType> GlowingCellTypes = new() { CellType.Lava };

        public TerrainVertex[] VertexBuffer => _vertexBuffer;

        public void EnsureCapacity(int meshWidth, int meshHeight, float cellSize)
        {
            _cellSize = cellSize;
            int quadCount = meshWidth * meshHeight * 2;
            int vertCount = quadCount * 4;

            if (_vertexBuffer == null || _vertexBuffer.Length != vertCount)
            {
                _vertexBuffer = new TerrainVertex[vertCount];
            }
        }

        public void BuildFull(TerrainCellCache cellCache, TerrainPrecalculator precalc, BackgroundFloodFill bgFloodFill,
            int minX, int minY, int meshWidth, int meshHeight, int worldWidth, int worldHeight,
            List<TextureAtlas> atlases, List<int>[] subMeshIndices, bool useColorLod)
        {
            int vIdx = 0;
            for (int x = 0; x < meshWidth; x++)
            {
                int gridX = minX + x;
                for (int y = 0; y < meshHeight; y++)
                {
                    int unityY = minY + y;
                    FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, true, ref vIdx, atlases, subMeshIndices, useColorLod);
                    FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, false, ref vIdx, atlases, subMeshIndices, useColorLod);
                }
            }
        }

        public void BuildIncremental(TerrainCellCache cellCache, TerrainPrecalculator precalc, BackgroundFloodFill bgFloodFill,
            int minX, int minY, int meshWidth, int meshHeight, int worldWidth, int worldHeight, int dx, int dy,
            List<TextureAtlas> atlases, List<int>[] subMeshIndices, bool useColorLod)
        {
            ScrollVertexBuffer(dx, dy, meshWidth, meshHeight);

            if (dx > 0)
            {
                for (int x = meshWidth - dx; x < meshWidth; x++)
                {
                    int vIdx = (x * meshHeight) * 8;
                    int gridX = minX + x;
                    for (int y = 0; y < meshHeight; y++)
                    {
                        int unityY = minY + y;
                        FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, true, ref vIdx, atlases, subMeshIndices, useColorLod);
                        FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, false, ref vIdx, atlases, subMeshIndices, useColorLod);
                    }
                }
            }
            else if (dx < 0)
            {
                for (int x = 0; x < -dx; x++)
                {
                    int vIdx = (x * meshHeight) * 8;
                    int gridX = minX + x;
                    for (int y = 0; y < meshHeight; y++)
                    {
                        int unityY = minY + y;
                        FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, true, ref vIdx, atlases, subMeshIndices, useColorLod);
                        FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, false, ref vIdx, atlases, subMeshIndices, useColorLod);
                    }
                }
            }

            if (dy > 0)
            {
                for (int y = meshHeight - dy; y < meshHeight; y++)
                {
                    for (int x = 0; x < meshWidth; x++)
                    {
                        int vIdx = ((x * meshHeight) + y) * 8;
                        int gridX = minX + x;
                        int unityY = minY + y;
                        FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, true, ref vIdx, atlases, subMeshIndices, useColorLod);
                        FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, false, ref vIdx, atlases, subMeshIndices, useColorLod);
                    }
                }
            }
            else if (dy < 0)
            {
                for (int y = 0; y < -dy; y++)
                {
                    for (int x = 0; x < meshWidth; x++)
                    {
                        int vIdx = ((x * meshHeight) + y) * 8;
                        int gridX = minX + x;
                        int unityY = minY + y;
                        FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, true, ref vIdx, atlases, subMeshIndices, useColorLod);
                        FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, false, ref vIdx, atlases, subMeshIndices, useColorLod);
                    }
                }
            }
        }

        private void ScrollVertexBuffer(int dx, int dy, int mw, int mh)
        {
            if (dx == 0 && dy == 0)
            {
                return;
            }

            const int stride = 8;
            int rowStride = mh * stride;
            Vector3 posOffset = new Vector3(-dx * _cellSize, -dy * _cellSize, 0);

            if (dx > 0)
            {
                for (int x = 0; x < mw - dx; x++)
                {
                    int srcBase = (x + dx) * rowStride;
                    int dstBase = x * rowStride;
                    if (dy > 0)
                    {
                        for (int y = 0; y < mh - dy; y++)
                        {
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[srcBase + ((y + dy) * stride) + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dstBase + (y * stride) + v] = vert;
                            }
                        }
                    }
                    else if (dy < 0)
                    {
                        for (int y = mh - 1; y >= -dy; y--)
                        {
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[srcBase + ((y + dy) * stride) + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dstBase + (y * stride) + v] = vert;
                            }
                        }
                    }
                    else
                    {
                        for (int y = 0; y < mh; y++)
                        {
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[srcBase + (y * stride) + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dstBase + (y * stride) + v] = vert;
                            }
                        }
                    }
                }
            }
            else if (dx < 0)
            {
                for (int x = mw - 1; x >= -dx; x--)
                {
                    int srcBase = (x + dx) * rowStride;
                    int dstBase = x * rowStride;
                    if (dy > 0)
                    {
                        for (int y = 0; y < mh - dy; y++)
                        {
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[srcBase + ((y + dy) * stride) + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dstBase + (y * stride) + v] = vert;
                            }
                        }
                    }
                    else if (dy < 0)
                    {
                        for (int y = mh - 1; y >= -dy; y--)
                        {
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[srcBase + ((y + dy) * stride) + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dstBase + (y * stride) + v] = vert;
                            }
                        }
                    }
                    else
                    {
                        for (int y = 0; y < mh; y++)
                        {
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[srcBase + (y * stride) + v];
                                vert.Position += posOffset;
                                _vertexBuffer[dstBase + (y * stride) + v] = vert;
                            }
                        }
                    }
                }
            }
            else if (dy != 0)
            {
                for (int x = 0; x < mw; x++)
                {
                    int baseX = x * rowStride;
                    if (dy > 0)
                    {
                        for (int y = 0; y < mh - dy; y++)
                        {
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[baseX + ((y + dy) * stride) + v];
                                vert.Position += posOffset;
                                _vertexBuffer[baseX + (y * stride) + v] = vert;
                            }
                        }
                    }
                    else
                    {
                        for (int y = mh - 1; y >= -dy; y--)
                        {
                            for (int v = 0; v < stride; v++)
                            {
                                TerrainVertex vert = _vertexBuffer[baseX + ((y + dy) * stride) + v];
                                vert.Position += posOffset;
                                _vertexBuffer[baseX + (y * stride) + v] = vert;
                            }
                        }
                    }
                }
            }
        }

        private void FillQuadData(int x, int y, int gridX, int unityY, TerrainCellCache cellCache, TerrainPrecalculator precalc, BackgroundFloodFill bgFloodFill,
            int worldWidth, int worldHeight, bool isBackground, ref int vIdx, List<TextureAtlas> atlases, List<int>[] subMeshIndices, bool useColorLod)
        {
            if (unityY >= worldHeight)
            {
                float posX = x * _cellSize;
                float posY = y * _cellSize;
                _vertexBuffer[vIdx + 0].Position = new Vector3(posX, posY, 0);
                _vertexBuffer[vIdx + 1].Position = new Vector3(posX + _cellSize, posY, 0);
                _vertexBuffer[vIdx + 2].Position = new Vector3(posX + _cellSize, posY + _cellSize, 0);
                _vertexBuffer[vIdx + 3].Position = new Vector3(posX, posY + _cellSize, 0);
                Color clear = Color.clear;
                _vertexBuffer[vIdx + 0].Color = clear;
                _vertexBuffer[vIdx + 1].Color = clear;
                _vertexBuffer[vIdx + 2].Color = clear;
                _vertexBuffer[vIdx + 3].Color = clear;
                Vector4 clearUV3 = new Vector4(x, y, 0, 0);
                _vertexBuffer[vIdx + 0].UV3 = clearUV3;
                _vertexBuffer[vIdx + 1].UV3 = clearUV3;
                _vertexBuffer[vIdx + 2].UV3 = clearUV3;
                _vertexBuffer[vIdx + 3].UV3 = clearUV3;
                Vector4 clearUV6 = Vector4.zero;
                _vertexBuffer[vIdx + 0].UV6 = clearUV6;
                _vertexBuffer[vIdx + 1].UV6 = clearUV6;
                _vertexBuffer[vIdx + 2].UV6 = clearUV6;
                _vertexBuffer[vIdx + 3].UV6 = clearUV6;

                var toSubMesh = subMeshIndices[0];
                toSubMesh.Add(vIdx + 0);
                toSubMesh.Add(vIdx + 3);
                toSubMesh.Add(vIdx + 2);
                toSubMesh.Add(vIdx + 2);
                toSubMesh.Add(vIdx + 1);
                toSubMesh.Add(vIdx + 0);

                vIdx += 4;
                return;
            }

            int cx = x + 1;
            int cy = y + 1;
            int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);

            CachedCellData ccd = cellCache.GetCellData(cx, cy);
            CellType cellFgType = ccd.Type;

            float glowX = 0f, glowY = 0f, glowZ = 0f;
            bool isGlowSource = GlowingCellTypes.Contains(cellFgType);

            if (isGlowSource)
            {
                glowX = gridX + 0.5f;
                glowY = unityY + 0.5f;
                glowZ = 1f;
            }
            else
            {
                for (int dy = -1; dy <= 1 && glowZ == 0f; dy++)
                {
                    for (int dx = -1; dx <= 1 && glowZ == 0f; dx++)
                    {
                        if ((dx != 0 || dy != 0) && GlowingCellTypes.Contains(cellCache.GetCellData(cx + dx, cy + dy).Type))
                        {
                            glowX = gridX + dx + 0.5f;
                            glowY = unityY + dy + 0.5f;
                            glowZ = 1f;
                        }
                    }
                }
            }

            CellType cellType = isBackground ? bgFloodFill.Buffer[x, y] : cellFgType;
            bool isSameCell = !isBackground || cellType == cellFgType;
            if (isBackground && (cellType == cellFgType || cellType == CellType.Unloaded))
            {
                cellType = CellType.Unloaded;
                isSameCell = false;
            }

            CachedCellData localFallback = default;

            // Since we moved this logic out of TerrainRenderer, we need to manually lookup MapManager / Texture Manager if needed.
            // For now, this fallback entry can be calculated within TerrainCellCache
            var mm = ServiceLocator.Resolve<MapManager>();
            var wtm = ServiceLocator.Resolve<ITextureService>() as WorldTextureManager;

            ref CachedCellData data = ref (isSameCell ? ref ccd : ref cellCache.GetNeighborCacheEntryRef(cellType, cx, cy, mm, wtm, atlases, ref localFallback));
            int atlasIndex = data.AtlasIndex;
            if (atlasIndex < 0 || atlasIndex >= subMeshIndices.Length)
            {
                atlasIndex = 0;
            }

            float zOffset = isBackground ? 0.1f : 0.0f;
            float lx = x * _cellSize;
            float ly = y * _cellSize;

            Vector3 off00 = precalc.GridVertexOffsets[x, y];
            Vector3 off10 = precalc.GridVertexOffsets[x + 1, y];
            Vector3 off01 = precalc.GridVertexOffsets[x, y + 1];
            Vector3 off11 = precalc.GridVertexOffsets[x + 1, y + 1];

            _vertexBuffer[vIdx + 0].Position = new Vector3(lx, ly, zOffset) + off00;
            _vertexBuffer[vIdx + 1].Position = new Vector3(lx + _cellSize, ly, zOffset) + off10;
            _vertexBuffer[vIdx + 2].Position = new Vector3(lx + _cellSize, ly + _cellSize, zOffset) + off11;
            _vertexBuffer[vIdx + 3].Position = new Vector3(lx, ly + _cellSize, zOffset) + off01;

            Vector2 uv0 = new Vector2(0, 0);
            Vector2 uv1 = new Vector2(1, 0);
            Vector2 uv2 = new Vector2(1, 1);
            Vector2 uv3 = new Vector2(0, 1);

            int descriptor = isSameCell ? precalc.CellTilingDescriptors[x, y] : 0;
            float packedW = data.HasTileGroup ? 1f : 0f;

            if (data.HasTileGroup && descriptor != 0)
            {
                if ((descriptor & 0x40) != 0)
                {
                    (uv0.x, uv1.x) = (uv1.x, uv0.x);
                    (uv3.x, uv2.x) = (uv2.x, uv3.x);
                }

                if ((descriptor & 0x20) != 0)
                {
                    (uv0.y, uv3.y) = (uv3.y, uv0.y);
                    (uv1.y, uv2.y) = (uv2.y, uv1.y);
                }

                if ((descriptor & 0x80) != 0)
                {
                    Vector2 t = uv0;
                    uv0 = uv1;
                    uv1 = uv2;
                    uv2 = uv3;
                    uv3 = t;
                }
            }

            _vertexBuffer[vIdx + 0].UV0 = uv0;
            _vertexBuffer[vIdx + 1].UV0 = uv1;
            _vertexBuffer[vIdx + 2].UV0 = uv2;
            _vertexBuffer[vIdx + 3].UV0 = uv3;

            Vector4 atlasRect = data.AtlasRect;
            bool useFallback = useColorLod || atlasRect.z < 0.0001f;
            Color color = useFallback ? data.MinimapColor : Color.white;
            float animOffset = 0f;
            if (!useFallback && data.Animation == CellAnimationType.Blinking)
            {
                uint seed = (uint)((gridX * 374761397) + (serverY * 668265263));
                seed = (seed ^ (seed >> 13)) * 1274126177;
                seed = seed ^ (seed >> 16);
                animOffset = (seed % 6283) / 1000f;
            }

            Vector4 animDataVec = new Vector4((float)data.Animation, (float)data.AnimationSpeed, animOffset, 0f);
            Vector4 tileSizeVec = new Vector4(data.UVTileSize, data.UVTileSize, (float)data.AnimationFrameCount, data.FrameHeightTiles);
            Vector4 worldPosVec = new Vector4(gridX, serverY, descriptor & 0x1F, packedW);

            bool isRelief = isSameCell && precalc.CellIsRelief[x, y];
            byte reliefMask = isSameCell ? precalc.CellReliefMasks[x, y] : (byte)0;
            float textureType = isRelief ? 1.0f : 0.0f;

            float sv00 = precalc.GridShadowValues[x, y];
            float sv10 = precalc.GridShadowValues[x + 1, y];
            float sv11 = precalc.GridShadowValues[x + 1, y + 1];
            float sv01 = precalc.GridShadowValues[x, y + 1];

            _vertexBuffer[vIdx].Color = color;
            _vertexBuffer[vIdx].UV1 = atlasRect;
            _vertexBuffer[vIdx].UV2 = tileSizeVec;
            _vertexBuffer[vIdx].UV3 = worldPosVec;
            _vertexBuffer[vIdx].UV4 = animDataVec;
            _vertexBuffer[vIdx].UV5 = new Vector4(textureType, isRelief ? reliefMask : sv00, _localUVsBuffer[0].x, _localUVsBuffer[0].y);

            _vertexBuffer[vIdx + 1].Color = color;
            _vertexBuffer[vIdx + 1].UV1 = atlasRect;
            _vertexBuffer[vIdx + 1].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 1].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 1].UV4 = animDataVec;
            _vertexBuffer[vIdx + 1].UV5 = new Vector4(textureType, isRelief ? reliefMask : sv10, _localUVsBuffer[1].x, _localUVsBuffer[1].y);

            _vertexBuffer[vIdx + 2].Color = color;
            _vertexBuffer[vIdx + 2].UV1 = atlasRect;
            _vertexBuffer[vIdx + 2].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 2].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 2].UV4 = animDataVec;
            _vertexBuffer[vIdx + 2].UV5 = new Vector4(textureType, isRelief ? reliefMask : sv11, _localUVsBuffer[2].x, _localUVsBuffer[2].y);

            _vertexBuffer[vIdx + 3].Color = color;
            _vertexBuffer[vIdx + 3].UV1 = atlasRect;
            _vertexBuffer[vIdx + 3].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 3].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 3].UV4 = animDataVec;
            _vertexBuffer[vIdx + 3].UV5 = new Vector4(textureType, isRelief ? reliefMask : sv01, _localUVsBuffer[3].x, _localUVsBuffer[3].y);

            float glowFlags = 0f;
            if (glowZ > 0.5f)
            {
                glowFlags += 1f;
            }

            if (!isBackground && MapManager.IsRoundableLoose(cellFgType))
            {
                glowFlags += 2f;
            }

            float sameCatMask = isSameCell ? precalc.CellSameCatMasks[x, y] : 0f;
            Vector4 glowVec = new Vector4(glowX, glowY, glowFlags, sameCatMask);
            _vertexBuffer[vIdx + 0].UV6 = glowVec;
            _vertexBuffer[vIdx + 1].UV6 = glowVec;
            _vertexBuffer[vIdx + 2].UV6 = glowVec;
            _vertexBuffer[vIdx + 3].UV6 = glowVec;

            var indices = subMeshIndices[atlasIndex];
            indices.Add(vIdx + 0);
            indices.Add(vIdx + 3);
            indices.Add(vIdx + 2);
            indices.Add(vIdx + 2);
            indices.Add(vIdx + 1);
            indices.Add(vIdx + 0);
            vIdx += 4;
        }
    }
}
