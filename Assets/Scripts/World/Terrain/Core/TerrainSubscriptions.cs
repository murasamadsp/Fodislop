#nullable enable

using System;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Подписки террейна на источники данных, все четыре в одном месте.
/// </summary>
///
/// Источники подменяются на ходу: хранилище переинициализируется при смене
/// мира, слой клеток появляется после WorldInit, сервис текстур — после
/// загрузки атласа. Каждая подписка поэтому перепривязывается, а не ставится
/// один раз, и снимать её надо ровно с того объекта, на который её повесили.
/// Пока это жило в рендерере, оно занимало четыре поля «на что подписаны» и
/// две почти одинаковые процедуры — подписку и отписку в OnDestroy.
public sealed class TerrainSubscriptions(
    Action<int, int> onCellChanged,
    Action<int, int, int, int> onRegionChanged,
    Action<string, Texture2D> onTextureLoaded,
    Action onWorldDataLoaded,
    Action<int, int, int, int> onChunkLoaded) : IDisposable
{
    private IWorldDataStorage? _storage;
    private ITextureService? _textureService;
    private IMapDataProvider? _mapData;
    private IWorldLayer<CellType>? _cellLayer;

    /// <summary>
    /// Привязать подписки к текущим источникам. Вызывается повторно: что не
    /// изменилось, то не трогается.
    /// </summary>
    public void Bind(
        IWorldDataStorage? storage,
        ITextureService? textureService,
        IMapDataProvider? mapData)
    {
        if (!ReferenceEquals(_storage, storage))
        {
            if (_storage != null)
            {
                _storage.CellChanged -= OnCellChanged;
                _storage.RegionChanged -= OnRegionChanged;
            }

            _storage = storage;
            if (_storage != null)
            {
                _storage.CellChanged += OnCellChanged;
                _storage.RegionChanged += OnRegionChanged;
            }
        }

        if (!ReferenceEquals(_textureService, textureService))
        {
            if (_textureService != null)
            {
                _textureService.OnTextureLoaded -= OnTextureLoaded;
            }

            _textureService = textureService;
            if (_textureService != null)
            {
                _textureService.OnTextureLoaded += OnTextureLoaded;
            }
        }

        if (!ReferenceEquals(_mapData, mapData))
        {
            if (_mapData != null)
            {
                _mapData.OnWorldDataLoaded -= OnWorldDataLoaded;
            }

            _mapData = mapData;
            if (_mapData != null)
            {
                _mapData.OnWorldDataLoaded += OnWorldDataLoaded;
            }
        }

        BindCellLayer(storage?.CellLayer);
    }

    public void Dispose()
    {
        Bind(null, null, null);
    }

    private void BindCellLayer(IWorldLayer<CellType>? cellLayer)
    {
        if (ReferenceEquals(_cellLayer, cellLayer))
        {
            return;
        }

        if (_cellLayer != null)
        {
            _cellLayer.ChunkLoaded -= OnChunkLoaded;
        }

        _cellLayer = cellLayer;
        if (_cellLayer != null)
        {
            _cellLayer.ChunkLoaded += OnChunkLoaded;
        }
    }

    private void OnCellChanged(int serverX, int serverY) => onCellChanged(serverX, serverY);

    private void OnRegionChanged(int serverX, int serverY, int width, int height) =>
        onRegionChanged(serverX, serverY, width, height);

    private void OnTextureLoaded(string filename, Texture2D texture) =>
        onTextureLoaded(filename, texture);

    private void OnWorldDataLoaded() => onWorldDataLoaded();

    private void OnChunkLoaded(int serverX, int serverY, int width, int height) =>
        onChunkLoaded(serverX, serverY, width, height);
}
