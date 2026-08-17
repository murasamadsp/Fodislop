#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.World.Terrain
{
    public class TerrainMeshBuilder
    {
        private TerrainVertex[] _vertexBuffer = Array.Empty<TerrainVertex>();
        private float _cellSize;
        public TerrainVertex[] VertexBuffer => _vertexBuffer;

        private int[] _bgAtlasIndices = Array.Empty<int>();
        private int[] _fgAtlasIndices = Array.Empty<int>();

        public void EnsureCapacity(int meshWidth, int meshHeight, float cellSize)
        {
            _cellSize = cellSize;
            int quadCount = meshWidth * meshHeight * 2;
            int vertCount = quadCount * 4;

            if (_vertexBuffer == null || _vertexBuffer.Length != vertCount)
            {
                _vertexBuffer = new TerrainVertex[vertCount];
            }

            int singleLayerQuads = meshWidth * meshHeight;
            if (_bgAtlasIndices.Length != singleLayerQuads)
            {
                _bgAtlasIndices = new int[singleLayerQuads];
                _fgAtlasIndices = new int[singleLayerQuads];
            }
        }

        public void BuildFull(TerrainCellCache cellCache, TerrainPrecalculator precalc, BackgroundFloodFill bgFloodFill,
            int minX, int minY, int meshWidth, int meshHeight, int worldWidth, int worldHeight,
            List<TextureAtlas> atlases, List<int>[] subMeshIndices, bool useColorLod,
            MapManager mapManager, ITextureService textureManager)
        {
            if (atlases == null || atlases.Count == 0 || subMeshIndices == null || subMeshIndices.Length == 0)
            {
                return;
            }

            EnsureCapacity(meshWidth, meshHeight, _cellSize);

            System.Threading.Tasks.Parallel.For(0, meshWidth, x =>
            {
                int gridX = minX + x;
                for (int y = 0; y < meshHeight; y++)
                {
                    int unityY = minY + y;
                    int quadIdx = (x * meshHeight) + y;
                    int baseIdx = quadIdx * 8;
                    _bgAtlasIndices[quadIdx] = FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, true, baseIdx, atlases, useColorLod, mapManager, textureManager);
                    _fgAtlasIndices[quadIdx] = FillQuadData(x, y, gridX, unityY, cellCache, precalc, bgFloodFill, worldWidth, worldHeight, false, baseIdx + 4, atlases, useColorLod, mapManager, textureManager);
                }
            });

            // Ultra-fast flat index collection without dictionary or cache lookups
            int totalQuads = meshWidth * meshHeight;
            for (int i = 0; i < totalQuads; i++)
            {
                int bgAtlas = _bgAtlasIndices[i];
                if (bgAtlas >= 0 && bgAtlas < subMeshIndices.Length)
                {
                    var bgList = subMeshIndices[bgAtlas];
                    int baseIdx = i * 8;
                    bgList.Add(baseIdx + 0);
                    bgList.Add(baseIdx + 3);
                    bgList.Add(baseIdx + 2);
                    bgList.Add(baseIdx + 2);
                    bgList.Add(baseIdx + 1);
                    bgList.Add(baseIdx + 0);
                }

                int fgAtlas = _fgAtlasIndices[i];
                if (fgAtlas >= 0 && fgAtlas < subMeshIndices.Length)
                {
                    var fgList = subMeshIndices[fgAtlas];
                    int fgIdx = (i * 8) + 4;
                    fgList.Add(fgIdx + 0);
                    fgList.Add(fgIdx + 3);
                    fgList.Add(fgIdx + 2);
                    fgList.Add(fgIdx + 2);
                    fgList.Add(fgIdx + 1);
                    fgList.Add(fgIdx + 0);
                }
            }
        }

        private int FillQuadData(int x, int y, int gridX, int unityY, TerrainCellCache cellCache, TerrainPrecalculator precalc, BackgroundFloodFill bgFloodFill,
            int worldWidth, int worldHeight, bool isBackground, int vIdx, List<TextureAtlas> atlases, bool useColorLod,
            MapManager mapManager, ITextureService textureManager)
        {
            if (unityY < 0 || unityY >= worldHeight || gridX < 0 || gridX >= worldWidth)
            {
                return -1;
            }

            int cx = x + 1;
            int cy = y + 1;
            int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);

            CachedCellData ccd = cellCache.GetCellData(cx, cy);
            CellType cellFgType = ccd.Type;
            if (ccd.State != TerrainCellState.Loaded)
            {
                return -1;
            }

            CellType cellType = isBackground ? bgFloodFill.Buffer[x, y] : cellFgType;
            bool isSameCell = !isBackground || cellType == cellFgType;
            if (isBackground && (cellType == cellFgType || cellType == CellType.Unloaded))
            {
                return -1;
            }

            Vector4 atlasRect;
            float uvTileSize;
            CellAnimationType animType;
            float animSpeed;
            int animFrames;
            float frameHeight;
            bool hasTileGroup;
            CellConfigProperties props;
            Color minimapColor;
            int atlasIndex;

            if (isSameCell)
            {
                atlasRect = ccd.AtlasRect;
                uvTileSize = ccd.UVTileSize;
                animType = ccd.Animation;
                animSpeed = ccd.AnimationSpeed;
                animFrames = ccd.AnimationFrameCount;
                frameHeight = ccd.FrameHeightTiles;
                hasTileGroup = ccd.HasTileGroup;
                props = ccd.Properties;
                minimapColor = ccd.MinimapColor;
                atlasIndex = ccd.AtlasIndex;
            }
            else
            {
                var meta = cellCache.GetMetadata(cellType, mapManager, textureManager, atlases);
                atlasRect = meta.AtlasRect;
                uvTileSize = meta.UVTileSize;
                animType = meta.Animation;
                animSpeed = meta.AnimationSpeed;
                animFrames = meta.AnimationFrameCount;
                frameHeight = meta.FrameHeightTiles;
                hasTileGroup = meta.HasTileGroup;
                props = meta.Properties;
                minimapColor = meta.MinimapColor;
                atlasIndex = meta.AtlasIndex;
            }

            if (atlasIndex < 0 || atlasIndex >= atlases.Count)
            {
                atlasIndex = 0;
            }

            bool hasTexture = atlasRect.z > 0f && atlasRect.w > 0f && uvTileSize > 0f;
            if (!hasTexture)
            {
                atlasRect = Vector4.zero;
                uvTileSize = atlases.Count > 0 ? (1f / atlases[0].Size) : 0f;
                animType = CellAnimationType.None;
                animSpeed = 0f;
                animFrames = 1;
                frameHeight = 1f;
            }

            float zOffset = isBackground ? 0.1f : 0.0f;
            float lx = x * _cellSize;
            float ly = y * _cellSize;

            Vector3 off00 = precalc.GridVertexOffsets[x, y];
            Vector3 off10 = precalc.GridVertexOffsets[x + 1, y];
            Vector3 off01 = precalc.GridVertexOffsets[x, y + 1];
            Vector3 off11 = precalc.GridVertexOffsets[x + 1, y + 1];

            bool isAnchored = off00 != Vector3.zero || off10 != Vector3.zero || off01 != Vector3.zero || off11 != Vector3.zero;
            float anchorFlag = isAnchored ? 1f : 0f;
            Vector2 anchor0 = new Vector2(off00.x, off00.y);
            Vector2 anchor1 = new Vector2(1f + off10.x, off10.y);
            Vector2 anchor2 = new Vector2(1f + off11.x, 1f + off11.y);
            Vector2 anchor3 = new Vector2(off01.x, 1f + off01.y);

            _vertexBuffer[vIdx + 0].Position = new Vector3(lx, ly, zOffset) + off00;
            _vertexBuffer[vIdx + 1].Position = new Vector3(lx + _cellSize, ly, zOffset) + off10;
            _vertexBuffer[vIdx + 2].Position = new Vector3(lx + _cellSize, ly + _cellSize, zOffset) + off11;
            _vertexBuffer[vIdx + 3].Position = new Vector3(lx, ly + _cellSize, zOffset) + off01;

            Vector2 uv0 = new Vector2(0, 0);
            Vector2 uv1 = new Vector2(1, 0);
            Vector2 uv2 = new Vector2(1, 1);
            Vector2 uv3 = new Vector2(0, 1);

            int descriptor = isSameCell ? precalc.CellTilingDescriptors[x, y] : 0;
            float packedW = hasTileGroup ? 1f : 0f;

            if (hasTileGroup && descriptor != 0)
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

            bool useFallback = useColorLod || atlasRect.z < 0.0001f;
            Color color = useFallback ? minimapColor : Color.white;
            if (atlasRect.z < 0.0001f)
            {
                color.a = 1f;
            }

            float animOffset = 0f;
            if (!useFallback && animType == CellAnimationType.Blinking)
            {
                uint seed = (uint)((gridX * 374761397) + (serverY * 668265263));
                seed = (seed ^ (seed >> 13)) * 1274126177;
                seed = seed ^ (seed >> 16);
                animOffset = (seed % 6283) / 1000f;
            }

            bool isPhysicalMass =
                !isBackground &&
                cellFgType != CellType.Empty &&
                ((props & CellConfigProperties.DropsShadow) != 0 ||
                 (props & CellConfigProperties.Passable) == 0);
            Vector4 animDataVec = new(
                (float)animType,
                animSpeed,
                animOffset,
                isPhysicalMass ? 1f : 0f);
            Vector4 tileSizeVec = new Vector4(uvTileSize, uvTileSize, (float)animFrames, frameHeight);
            Vector4 worldPosVec = new Vector4(gridX, serverY, descriptor & 0x1F, packedW);

            bool isRelief = isSameCell && precalc.CellIsRelief[x, y];
            byte reliefMask = isSameCell ? precalc.CellReliefMasks[x, y] : (byte)0;

            float sv00 = precalc.GridShadowValues[x, y];
            float sv10 = precalc.GridShadowValues[x + 1, y];
            float sv11 = precalc.GridShadowValues[x + 1, y + 1];
            float sv01 = precalc.GridShadowValues[x, y + 1];

            _vertexBuffer[vIdx].Color = color;
            _vertexBuffer[vIdx].UV1 = atlasRect;
            _vertexBuffer[vIdx].UV2 = tileSizeVec;
            _vertexBuffer[vIdx].UV3 = worldPosVec;
            _vertexBuffer[vIdx].UV4 = animDataVec;
            bool isGlowing = (props & CellConfigProperties.Glowing) != 0;

            Color materialColor = minimapColor;
            Color32 materialColor32 = materialColor;
            int packedLightingColor = materialColor32.r |
                (materialColor32.g << 8) |
                (materialColor32.b << 16);

            _vertexBuffer[vIdx].UV5 = new Vector4(
                anchorFlag,
                isRelief ? reliefMask : sv00,
                anchor0.x,
                anchor0.y);

            _vertexBuffer[vIdx + 1].Color = color;
            _vertexBuffer[vIdx + 1].UV1 = atlasRect;
            _vertexBuffer[vIdx + 1].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 1].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 1].UV4 = animDataVec;
            _vertexBuffer[vIdx + 1].UV5 = new Vector4(
                anchorFlag,
                isRelief ? reliefMask : sv10,
                anchor1.x,
                anchor1.y);

            _vertexBuffer[vIdx + 2].Color = color;
            _vertexBuffer[vIdx + 2].UV1 = atlasRect;
            _vertexBuffer[vIdx + 2].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 2].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 2].UV4 = animDataVec;
            _vertexBuffer[vIdx + 2].UV5 = new Vector4(
                anchorFlag,
                isRelief ? reliefMask : sv11,
                anchor2.x,
                anchor2.y);

            _vertexBuffer[vIdx + 3].Color = color;
            _vertexBuffer[vIdx + 3].UV1 = atlasRect;
            _vertexBuffer[vIdx + 3].UV2 = tileSizeVec;
            _vertexBuffer[vIdx + 3].UV3 = worldPosVec;
            _vertexBuffer[vIdx + 3].UV4 = animDataVec;
            _vertexBuffer[vIdx + 3].UV5 = new Vector4(
                anchorFlag,
                isRelief ? reliefMask : sv01,
                anchor3.x,
                anchor3.y);

            float glowFlags = 0f;
            if (isGlowing)
            {
                glowFlags += 1f;
            }

            if (!isBackground && MapManager.IsRoundableLoose(cellFgType))
            {
                glowFlags += 2f;
            }

            float visualBlendMask = isSameCell ? precalc.CellVisualBlendMasks[x, y] : 0f;
            byte solidConnectivityMask = isSameCell
                ? precalc.CellSolidBoundaryMasks[x, y]
                : (byte)0;
            float solidBoundaryMask = solidConnectivityMask & 15;
            float solidDiagonalMask = solidConnectivityMask >> 4;
            bool hasRoundedPhysicalContour =
                !isBackground && MapManager.IsRoundableLoose(cellFgType);
            float emissionPower = isGlowing
                ? Mathf.Max(1f / byte.MaxValue, materialColor.a)
                : 0f;
            float packedLightingFlags = solidBoundaryMask +
                (isGlowing ? 16f : 0f) +
                (hasRoundedPhysicalContour ? 32f : 0f) +
                (isPhysicalMass ? 64f : 0f) +
                (emissionPower * 0.25f);
            Vector4 glowVec = new Vector4(
                packedLightingColor,
                packedLightingFlags,
                glowFlags + (solidDiagonalMask * 4f),
                visualBlendMask);
            _vertexBuffer[vIdx + 0].UV6 = glowVec;
            _vertexBuffer[vIdx + 1].UV6 = glowVec;
            _vertexBuffer[vIdx + 2].UV6 = glowVec;
            _vertexBuffer[vIdx + 3].UV6 = glowVec;

            return atlasIndex;
        }
    }
}
