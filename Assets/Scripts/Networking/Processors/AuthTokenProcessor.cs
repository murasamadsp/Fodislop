#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.Networking.Auth;
using UnityEngine;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Processors;

public sealed class AuthTokenProcessor(ILocalPlayerState localPlayer, IGameTokenStore tokens)
{
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
        tokens.Save(newToken);
        localPlayer.SetAuthenticated(true);
    }
}
