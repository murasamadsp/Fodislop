#nullable enable

using Kern.Core;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Применение пользовательских настроек террейна к окну и материалам.
/// </summary>
///
/// Настройки приходят одним ClientConfig, а расходятся по трём адресатам:
/// тумблер искажения живёт в конвейере, кайма и вид поверхности — в материалах.
/// Renderer здесь только повод: он держит
/// конфиг, а раскладку по адресатам делает этот тип.
public sealed class TerrainClientConfigApplier
{
    private readonly TerrainWindow _window;

    public TerrainClientConfigApplier(TerrainWindow window) => _window = window;

    public void Apply(ClientConfig config)
    {
        bool enableDistortion = config.Terrain.EnableDistortion;
        if (_window.Driver.Pipeline.EnableDistortion != enableDistortion)
        {
            // Предрасчёт может принадлежать фоновому шагу: флаг ляжет в
            // следующий шаг, и тот соберёт окно целиком.
            _window.RequestDistortion(enableDistortion);
        }

        // The selected rim state is bound with the other material parameters.
        bool enableRim = config.Terrain.EnableRim;

        _window.Driver.Presentation.ApplyClientConfig(config);
        Debug.Log(
            $"[TerrainRenderer] ApplyClientConfig: distortion={enableDistortion}, " +
            $"rim={enableRim}");
    }
}
