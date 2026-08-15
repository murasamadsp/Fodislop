#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Fodinae.Core.Interfaces
{
    public interface IAssetLoader
    {
        UniTask<string> GetAssetPathAsync(
            string filename,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = ProjectRuntimeContracts.AssetRequestTimeoutSeconds);
        UniTask<Texture2D?> GetTextureAsync(string filename, CancellationToken cancellationToken = default);
    }
}
