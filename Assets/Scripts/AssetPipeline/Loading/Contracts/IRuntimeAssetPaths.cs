#nullable enable

using Cysharp.Threading.Tasks;

namespace Fodinae.Core.Interfaces;
public interface IRuntimeAssetPaths
{
    string BundledTexturesRoot { get; }
    string PersistentTexturesRoot { get; }
    UniTask EnsureReadyAsync();
    string? FindBundledTextureFile(string relativePath);
    string? FindTextureFile(string relativePath);
}
