#nullable enable

using Fodinae.Core.Interfaces;
using Fodinae.Networking.Auth;
using UnityEngine;
using MinesServer.Networking.Server.Packets.Connection;

namespace Fodinae.Networking.Processors;

/// <summary>
/// Persists the server-issued authentication token and authorizes the game UI.
/// An empty token is a rejected authentication response, not a client
/// invariant failure: the auth window/reconnect flow stays alive without
/// tripping the editor fail-fast logger.
/// </summary>
public sealed class AuthTokenProcessor
{
    private readonly ILocalPlayerState _localPlayer;
    private bool _emptyAuthTokenWarningLogged;

    public AuthTokenProcessor(ILocalPlayerState localPlayer)
    {
        _localPlayer = localPlayer;
    }

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
        AuthTokenManager.SaveToken(newToken);
        _localPlayer.SetAuthenticated(true);
    }
}
