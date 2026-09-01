#nullable enable

using System;

namespace Fodinae.Core.Interfaces;

/// <summary>
/// Single typed source for "the locally controlled player".
///
/// Replaces the <c>PlayerMovementController.LocalPlayer</c> static: the static
/// was a hidden global that networking processors, UI controllers and render
/// components all reached into, so nothing owned its lifetime and every
/// consumer silently depended on Unity call order. The state is published by
/// the player controller through DI and cleared on destroy; consumers resolve
/// this interface instead of reaching into a concrete type.
/// </summary>
public interface ILocalPlayerState
{
    /// <summary>The locally controlled player, or null before it is spawned / after teardown.</summary>
    ILocalPlayer? Current { get; }

    /// <summary>Raised whenever the local player is published (non-null) or cleared (null).</summary>
    event Action<ILocalPlayer?>? Changed;

    /// <summary>Publishes the locally controlled player. Idempotent for the same instance.</summary>
    void Publish(ILocalPlayer player);

    /// <summary>Clears the state when the published player is destroyed. Idempotent.</summary>
    void Clear(ILocalPlayer player);

    /// <summary>Publishes whether the local player has completed authentication.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Updates authentication state for presentation consumers.</summary>
    void SetAuthenticated(bool authenticated);
}
