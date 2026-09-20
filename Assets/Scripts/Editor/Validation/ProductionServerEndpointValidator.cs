#nullable enable

using System;
using System.Net;
using Kern.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Kern.Editor.Validation;

public sealed class ProductionServerEndpointValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform is not (BuildTarget.StandaloneWindows64 or BuildTarget.StandaloneOSX))
        {
            return;
        }

        string host = ProjectRuntimeContracts.ClientConfiguration.DefaultServerHost.Trim();
        int port = ProjectRuntimeContracts.ClientConfiguration.DefaultServerPort;

        if (ProjectRuntimeContracts.ClientConfiguration.DefaultUseDummyConnection)
        {
            throw new BuildFailedException(
                "Alpha release build requires the real server transport. " +
                "DefaultUseDummyConnection must be false.");
        }

        if (!IsProductionHost(host) || port is < 1 or > 65535)
        {
            throw new BuildFailedException(
                $"Alpha release build requires a public ServerHost/ServerPort; " +
                $"current endpoint is '{host}:{port}'.");
        }
    }

    private static bool IsProductionHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host) ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(host, out IPAddress? address) || !IPAddress.IsLoopback(address);
    }
}
