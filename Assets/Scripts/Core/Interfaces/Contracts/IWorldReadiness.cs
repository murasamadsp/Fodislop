#nullable enable

using System;

namespace Fodinae.Core.Interfaces
{
    /// <summary>
    /// Прослойка готовности мира, позволяющая Networking/session-коду
    /// зависеть от интерфейса, а не от конкретного GameManager.
    /// Реализуется GameManager — единым источником публикации world-ready.
    /// </summary>
    public interface IWorldReadiness
    {
        /// <summary>Стала ли текущая мировая сессия полностью готовой к геймплею.</summary>
        bool IsWorldLoaded { get; }

        /// <summary>Уведомляет о готовности мировой сессии (callback от загрузки мира).</summary>
        void NotifyWorldLoaded();
    }
}
