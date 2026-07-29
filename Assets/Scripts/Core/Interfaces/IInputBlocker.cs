#nullable enable

namespace Fodinae.Core.Interfaces
{
    public interface IInputBlocker
    {
        bool IsInputBlocked { get; }
        string? TopWindowTag { get; }
    }
}
