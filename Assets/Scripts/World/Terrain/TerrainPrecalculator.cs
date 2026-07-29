#nullable enable

using Fodinae.Game.Managers;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.World.Terrain
{
    public class TerrainPrecalculator
    {
        public Vector3[,] GridVertexOffsets;
        public float[,] GridShadowValues;
        public int[,] CellTilingDescriptors;
        public byte[,] CellReliefMasks;
        public bool[,] CellIsRelief;
        public byte[,] CellSameCatMasks;

        public void EnsureCapacity(int meshWidth, int meshHeight)
        {
            if (GridVertexOffsets == null || GridVertexOffsets.GetLength(0) != meshWidth + 1 || GridVertexOffsets.GetLength(1) != meshHeight + 1)
            {
                GridVertexOffsets = new Vector3[meshWidth + 1, meshHeight + 1];
                GridShadowValues = new float[meshWidth + 1, meshHeight + 1];
                CellTilingDescriptors = new int[meshWidth, meshHeight];
                CellReliefMasks = new byte[meshWidth, meshHeight];
                CellIsRelief = new bool[meshWidth, meshHeight];
                CellSameCatMasks = new byte[meshWidth, meshHeight];
            }
        }

        public void PrecalculateFull(TerrainCellCache cellCache, int meshWidth, int meshHeight, MapManager mm)
        {
            int gw = meshWidth + 1;
            int gh = meshHeight + 1;
            for (int x = 0; x < gw; x++)
            {
                for (int y = 0; y < gh; y++)
                {
                    CalculateVertexNode(cellCache, x, y);
                }
            }

            for (int x = 0; x < meshWidth; x++)
            {
                for (int y = 0; y < meshHeight; y++)
                {
                    CalculateCellNode(cellCache, x, y);
                }
            }
        }

        public void PrecalculateIncremental(TerrainCellCache cellCache, int meshWidth, int meshHeight, int dx, int dy, MapManager mm)
        {
            int gw = meshWidth + 1;
            int gh = meshHeight + 1;

            TerrainCellCache.Scroll2DArray(GridVertexOffsets, gw, gh, dx, dy);
            TerrainCellCache.Scroll2DArray(GridShadowValues, gw, gh, dx, dy);
            TerrainCellCache.Scroll2DArray(CellTilingDescriptors, meshWidth, meshHeight, dx, dy);
            TerrainCellCache.Scroll2DArray(CellReliefMasks, meshWidth, meshHeight, dx, dy);
            TerrainCellCache.Scroll2DArray(CellIsRelief, meshWidth, meshHeight, dx, dy);
            TerrainCellCache.Scroll2DArray(CellSameCatMasks, meshWidth, meshHeight, dx, dy);

            int vxStart = 0, vxLen = 0, vyStart = 0, vyLen = 0;
            if (dx > 0)
            {
                vxStart = gw - dx;
                vxLen = dx;
            }
            else if (dx < 0)
            {
                vxStart = 0;
                vxLen = -dx;
            }

            if (dy > 0)
            {
                vyStart = gh - dy;
                vyLen = dy;
            }
            else if (dy < 0)
            {
                vyStart = 0;
                vyLen = -dy;
            }

            if (vxLen > 0 || vyLen > 0)
            {
                if (vxLen > 0)
                {
                    for (int x = vxStart; x < vxStart + vxLen; x++)
                    {
                        for (int y = 0; y < gh; y++)
                        {
                            CalculateVertexNode(cellCache, x, y);
                        }
                    }
                }

                if (vyLen > 0 && vxLen < gw)
                {
                    int xStart = 0, xEnd = gw;
                    if (vxLen > 0)
                    {
                        if (dx > 0)
                        {
                            xEnd = vxStart;
                        }
                        else
                        {
                            xStart = vxLen;
                        }
                    }

                    if (xStart < xEnd)
                    {
                        for (int y = vyStart; y < vyStart + vyLen; y++)
                        {
                            for (int x = xStart; x < xEnd; x++)
                            {
                                CalculateVertexNode(cellCache, x, y);
                            }
                        }
                    }
                }
            }

            int cxStart = 0, cxLen = 0, cyStart = 0, cyLen = 0;
            if (dx > 0)
            {
                cxStart = meshWidth - dx;
                cxLen = dx;
            }
            else if (dx < 0)
            {
                cxStart = 0;
                cxLen = -dx;
            }

            if (dy > 0)
            {
                cyStart = meshHeight - dy;
                cyLen = dy;
            }
            else if (dy < 0)
            {
                cyStart = 0;
                cyLen = -dy;
            }

            if (cxLen > 0 || cyLen > 0)
            {
                if (cxLen > 0)
                {
                    for (int x = cxStart; x < cxStart + cxLen; x++)
                    {
                        for (int y = 0; y < meshHeight; y++)
                        {
                            CalculateCellNode(cellCache, x, y);
                        }
                    }
                }

                if (cyLen > 0 && cxLen < meshWidth)
                {
                    int xStart = 0, xEnd = meshWidth;
                    if (cxLen > 0)
                    {
                        if (dx > 0)
                        {
                            xEnd = cxStart;
                        }
                        else
                        {
                            xStart = cxLen;
                        }
                    }

                    if (xStart < xEnd)
                    {
                        for (int y = cyStart; y < cyStart + cyLen; y++)
                        {
                            for (int x = xStart; x < xEnd; x++)
                            {
                                CalculateCellNode(cellCache, x, y);
                            }
                        }
                    }
                }
            }
        }

        private void CalculateVertexNode(TerrainCellCache cellCache, int x, int y)
        {
            int cx = x + 1;
            int cy = y + 1;
            var tl = cellCache.GetCellData(x, cy);
            var tr = cellCache.GetCellData(cx, cy);
            var bl = cellCache.GetCellData(x, y);
            var br = cellCache.GetCellData(cx, y);

            if (tl.Distortion == CellDistortionType.Block || tr.Distortion == CellDistortionType.Block ||
                bl.Distortion == CellDistortionType.Block || br.Distortion == CellDistortionType.Block)
            {
                GridVertexOffsets[x, y] = Vector3.zero;
            }
            else
            {
                int xSign = 0, ySign = 0;
                if (bl.Distortion == CellDistortionType.Cause)
                {
                    xSign -= 1;
                    ySign += 1;
                }

                if (br.Distortion == CellDistortionType.Cause)
                {
                    xSign += 1;
                    ySign += 1;
                }

                if (tl.Distortion == CellDistortionType.Cause)
                {
                    xSign -= 1;
                    ySign -= 1;
                }

                if (tr.Distortion == CellDistortionType.Cause)
                {
                    xSign += 1;
                    ySign -= 1;
                }

                if (xSign == 0 && ySign == 0)
                {
                    GridVertexOffsets[x, y] = Vector3.zero;
                }
                else
                {
                    uint seed = (uint)(((cellCache.CacheMinX + cx) * 374761397) + ((cellCache.CacheMinY + cy) * 668265263));
                    seed = (seed ^ (seed >> 13)) * 1274126177;
                    seed = seed ^ (seed >> 16);
                    float r = ((seed % 4) + 1) * 0.0625f;
                    uint seed2 = seed * 2654435761u;
                    float ry = ((seed2 % 4) + 1) * 0.0625f;
                    GridVertexOffsets[x, y] = new Vector3(xSign > 0 ? r : (xSign < 0 ? -r : 0), ySign > 0 ? ry : (ySign < 0 ? -ry : 0), 0);
                }
            }

            bool hasC = (bl.Properties & CellConfigProperties.DropsShadow) != 0 || (br.Properties & CellConfigProperties.DropsShadow) != 0 ||
                        (tl.Properties & CellConfigProperties.DropsShadow) != 0 || (tr.Properties & CellConfigProperties.DropsShadow) != 0;
            bool hasR = (bl.Properties & CellConfigProperties.ReceivesShadow) != 0 || (br.Properties & CellConfigProperties.ReceivesShadow) != 0 ||
                        (tl.Properties & CellConfigProperties.ReceivesShadow) != 0 || (tr.Properties & CellConfigProperties.ReceivesShadow) != 0;
            GridShadowValues[x, y] = (hasC && hasR) ? 0.7f : 0.0f;
        }

        private void CalculateCellNode(TerrainCellCache cellCache, int x, int y)
        {
            int cx = x + 1;
            int cy = y + 1;
            var data = cellCache.GetCellData(cx, cy);

            if (data.HasTileGroup)
            {
                byte m = 0;
                if (cellCache.GetCellData(cx - 1, cy).HasTileGroup && cellCache.GetCellData(cx - 1, cy).TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 0;
                }

                if (cellCache.GetCellData(cx - 1, cy - 1).HasTileGroup && cellCache.GetCellData(cx - 1, cy - 1).TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 1;
                }

                if (cellCache.GetCellData(cx, cy - 1).HasTileGroup && cellCache.GetCellData(cx, cy - 1).TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 2;
                }

                if (cellCache.GetCellData(cx + 1, cy - 1).HasTileGroup && cellCache.GetCellData(cx + 1, cy - 1).TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 3;
                }

                if (cellCache.GetCellData(cx + 1, cy).HasTileGroup && cellCache.GetCellData(cx + 1, cy).TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 4;
                }

                if (cellCache.GetCellData(cx + 1, cy + 1).HasTileGroup && cellCache.GetCellData(cx + 1, cy + 1).TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 5;
                }

                if (cellCache.GetCellData(cx, cy + 1).HasTileGroup && cellCache.GetCellData(cx, cy + 1).TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 6;
                }

                if (cellCache.GetCellData(cx - 1, cy + 1).HasTileGroup && cellCache.GetCellData(cx - 1, cy + 1).TileGroupId == data.TileGroupId)
                {
                    m |= 1 << 7;
                }

                CellTilingDescriptors[x, y] = TileBitmaskConverter.GetDescriptor(m);
            }
            else
            {
                CellTilingDescriptors[x, y] = 0;
            }

            byte rm = 0;
            bool isR = false;
            if (cellCache.GetCellData(cx, cy + 1).ReliefGroup >= data.ReliefGroup)
            {
                rm |= 1;
            }
            else
            {
                isR = true;
            }

            if (cellCache.GetCellData(cx - 1, cy).ReliefGroup >= data.ReliefGroup)
            {
                rm |= 2;
            }
            else
            {
                isR = true;
            }

            if (cellCache.GetCellData(cx, cy - 1).ReliefGroup >= data.ReliefGroup)
            {
                rm |= 4;
            }
            else
            {
                isR = true;
            }

            if (cellCache.GetCellData(cx + 1, cy).ReliefGroup >= data.ReliefGroup)
            {
                rm |= 8;
            }
            else
            {
                isR = true;
            }

            CellReliefMasks[x, y] = rm;
            CellIsRelief[x, y] = isR;

            byte sm = 0;
            if (MapManager.IsRoundableLoose(cellCache.GetCellData(cx, cy + 1).Type))
            {
                sm |= 1;
            }

            if (MapManager.IsRoundableLoose(cellCache.GetCellData(cx - 1, cy).Type))
            {
                sm |= 2;
            }

            int bt = (int)cellCache.GetCellData(cx, cy - 1).Type;
            if (MapManager.IsRoundableLoose((CellType)bt) || (bt < 32 || bt > 35))
            {
                sm |= 4;
            }

            if (MapManager.IsRoundableLoose(cellCache.GetCellData(cx + 1, cy).Type))
            {
                sm |= 8;
            }

            CellSameCatMasks[x, y] = sm;
        }
    }
}
