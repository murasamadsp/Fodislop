#nullable enable

namespace Fodinae.Networking;

/// <summary>
/// Narrow connection-status state published by StatusProcessor and read by
/// any presentation that needs it (currently the FPS/ping overlay).
///
/// Network code writes this model instead of calling into UI singletons;
/// UI reads it instead of being mutated from the networking layer.
/// </summary>
public sealed class NetworkStatusModel
{
    public int PingMs { get; private set; }

    public int OnlinePlayers { get; private set; }

    public int OnlineProgrammator { get; private set; }

    public void SetPing(int pingMs)
    {
        PingMs = pingMs;
    }

    public void SetOnline(int players, int programmator)
    {
        OnlinePlayers = players;
        OnlineProgrammator = programmator;
    }
}
