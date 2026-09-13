#nullable enable

using System;

namespace Fodinae.Core.Interfaces;

public interface ILocalPlayerState
{
    ILocalPlayer? Current { get; }

    event Action<ILocalPlayer?>? Changed;

    void Publish(ILocalPlayer player);

    void Clear(ILocalPlayer player);

    bool IsAuthenticated { get; }

    void SetAuthenticated(bool authenticated);
}
