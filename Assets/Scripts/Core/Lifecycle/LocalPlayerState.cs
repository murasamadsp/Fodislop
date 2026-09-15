#nullable enable

using System;
using Fodinae.Core.Interfaces;

namespace Fodinae.Core.Lifecycle;

public sealed class LocalPlayerState : ILocalPlayerState
{
    public ILocalPlayer? Current { get; private set; }

    public bool IsAuthenticated { get; private set; }

    public event Action<ILocalPlayer?>? Changed;

    public void Publish(ILocalPlayer player)
    {
        if (ReferenceEquals(Current, player))
        {
            return;
        }

        Current = player;
        Changed?.Invoke(player);
    }

    public void SetAuthenticated(bool authenticated)
    {
        IsAuthenticated = authenticated;
    }

    public void Clear(ILocalPlayer player)
    {
        if (!ReferenceEquals(Current, player))
        {
            return;
        }

        Current = null;
        Changed?.Invoke(null);
    }
}
