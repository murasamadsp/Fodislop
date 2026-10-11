#nullable enable

using Kern.Core.Interfaces;
using Kern.Networking.Auth;
using UnityEngine;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Networking.Processors;

public sealed class AuthTokenProcessor
{
    private readonly ILocalPlayerState _localPlayer;
    private readonly IGameTokenStore _tokens;
    public AuthTokenProcessor(ILocalPlayerState localPlayer, IGameTokenStore tokens)
    {
        _localPlayer = localPlayer;
        _tokens = tokens;
    }

    private bool _emptyAuthTokenWarningLogged;

    public void Process(AuthTokenPacket packet)
    {
        string newToken = packet.Token;
        if (string.IsNullOrEmpty(newToken))
        {
            if (!_emptyAuthTokenWarningLogged)
            {
                Debug.LogWarning("[Auth] Server returned an empty authentication token.");
                _emptyAuthTokenWarningLogged = true;
            }

            return;
        }

        _emptyAuthTokenWarningLogged = false;
        _tokens.Save(newToken);
        _localPlayer.SetAuthenticated(true);
    }
}
