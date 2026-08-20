#nullable enable

using VContainer;

namespace Fodinae.Core.DI
{
    /// <summary>
    /// DI-управляемый доступ к «текущему контейнеру сессии». Bootstrap-скоуп
    /// переживает смену сцен, а мирные сервисы (GameManager, MapManager, ...) живут
    /// в дочернем скоупе сессии, который пересоздаётся при каждом входе в мир.
    /// Bootstrap-уровневые компоненты (ConnectionManager) и код вне DI-графа
    /// (MainMenu) резолвят через этот хелдер, а не через глобальный статический
    /// локатор: хелдер регистрируется в Bootstrap-скоупе и инжектится, а его
    /// Current переключается на контейнер сессии при сборке игрового скоупа.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public sealed class SessionContainer : ISessionContainer
    {
        [UnityEngine.Scripting.Preserve]
        [Inject]
        public SessionContainer()
        {
        }

        public IObjectResolver? Current { get; private set; }

        public void Set(IObjectResolver resolver)
        {
            Current = resolver ?? throw new System.ArgumentNullException(nameof(resolver));
        }

        public T? TryResolve<T>()
            where T : class
        {
            if (Current == null)
            {
                return null;
            }

            return Current.TryResolve(typeof(T), out object? resolved) && resolved is T value
                ? value
                : null;
        }

        public T Resolve<T>()
            where T : class
        {
            if (Current == null)
            {
                throw new System.InvalidOperationException(
                    "No active session container; resolve was requested before any scope was built.");
            }

            return Current.Resolve<T>();
        }
    }

    public interface ISessionContainer
    {
        IObjectResolver? Current { get; }

        void Set(IObjectResolver resolver);

        T? TryResolve<T>()
            where T : class;

        T Resolve<T>()
            where T : class;
    }

    /// <summary>
    /// Ambient access for code that lives outside the DI graph — runtime
    /// AddComponent'd views (ProgrammatorGrid) and execute-always UI (MainMenu)
    /// never get [Inject] fields filled. Routes through the pre-existing
    /// <see cref="BootstrapLifetimeScope.Instance"/> singleton, so no new static
    /// state is introduced; returns null when no scope is built yet.
    /// </summary>
    public static class SessionAccess
    {
        public static ISessionContainer? Resolve()
        {
            BootstrapLifetimeScope? bootstrap = BootstrapLifetimeScope.Instance;
            if (bootstrap == null || bootstrap.Container == null)
            {
                return null;
            }

            bootstrap.Container.TryResolve(out ISessionContainer session);
            return session;
        }
    }
}
