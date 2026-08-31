#nullable enable

using MinesServer.Networking.Shared;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyConnectionSession
{
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    public int LifecycleVersion { get; private set; }

    public bool TryBeginConnect(out int lifecycleVersion)
    {
        if (Status != ConnectionStatus.Disconnected)
        {
            lifecycleVersion = LifecycleVersion;
            return false;
        }

        Status = ConnectionStatus.Connecting;
        lifecycleVersion = ++LifecycleVersion;
        return true;
    }

    public bool TryCompleteConnect(int lifecycleVersion)
    {
        if (Status != ConnectionStatus.Connecting || lifecycleVersion != LifecycleVersion)
        {
            return false;
        }

        Status = ConnectionStatus.Connected;
        return true;
    }

    public bool TryBeginDisconnect(out int lifecycleVersion)
    {
        if (Status == ConnectionStatus.Disconnected)
        {
            lifecycleVersion = LifecycleVersion;
            return false;
        }

        lifecycleVersion = ++LifecycleVersion;
        Status = ConnectionStatus.Disconnecting;
        return true;
    }

    public bool TryCompleteDisconnect(int lifecycleVersion)
    {
        if (Status != ConnectionStatus.Disconnecting || lifecycleVersion != LifecycleVersion)
        {
            return false;
        }

        Status = ConnectionStatus.Disconnected;
        return true;
    }

    public bool IsAlive(int lifecycleVersion)
    {
        return Status == ConnectionStatus.Connected &&
            lifecycleVersion == LifecycleVersion;
    }

    public void Stop()
    {
        LifecycleVersion++;
        Status = ConnectionStatus.Disconnected;
    }
}
