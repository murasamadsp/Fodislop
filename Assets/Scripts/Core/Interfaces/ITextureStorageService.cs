#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Fodinae.Core.Interfaces
{
    public interface ITextureStorageService
    {
        bool HasTexture(string filename);
        UniTask<byte[]?> GetTextureData(string filename, CancellationToken cancellationToken = default);
        event Action<string> OnTextureLoaded;
    }
}
