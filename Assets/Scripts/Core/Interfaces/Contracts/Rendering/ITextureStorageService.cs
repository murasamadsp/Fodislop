#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Kern.Core.Interfaces;
public interface ITextureStorageService
{
    bool HasTexture(string filename);
    UniTask<Texture2D?> GetTextureAsync(
        string filename,
        CancellationToken cancellationToken = default);
    UniTask<byte[]?> GetTextureData(string filename, CancellationToken cancellationToken = default);
    UniTask PreloadTexturesAsync(
        IReadOnlyList<string> filenames,
        CancellationToken cancellationToken = default);
    event Action<string> OnTextureLoaded;
}
