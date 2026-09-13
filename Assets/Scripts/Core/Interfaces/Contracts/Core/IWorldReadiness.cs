#nullable enable

using System;

namespace Fodinae.Core.Interfaces;
public interface IWorldReadiness
{
    bool IsWorldLoaded { get; }

    void NotifyWorldLoaded();
}
