#nullable enable

namespace Fodinae.Core.Interfaces
{
    /// <summary>
    /// Офлайн-идентичность игрока (имя и т.п.). Реализуется DummyConnection из
    /// симулированной VK-сессии, чтобы мир в офлайн-режиме ощущался как
    /// настоящий; реальные источники идентичности могут реализовать тот же
    /// интерфейс и подхватятся бутстрапом (DummyConnection регистрируется
    /// через AsImplementedInterfaces).
    /// </summary>
    public interface IOfflineIdentityProvider
    {
        /// <summary>Имя игрока в мире (неймплейт, чат, профиль).</summary>
        string PlayerName { get; }
    }

    /// <summary>
    /// Офлайн-статистика игрока (уровень, валюта). Аналог
    /// <see cref="IOfflineIdentityProvider"/>: провайдеры подменяются по
    /// интерфейсу, не трогая DummyConnection.
    /// </summary>
    public interface IOfflineStatsProvider
    {
        long Level { get; }

        long Currency { get; }
    }
}
