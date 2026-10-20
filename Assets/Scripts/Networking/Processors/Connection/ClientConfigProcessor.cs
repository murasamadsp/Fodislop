#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Information;

namespace Kern.Networking.Processors;

public sealed class ClientConfigProcessor : IPacketProcessor<ClientConfigPacket>
{
    private readonly ITextureStorageService _textureStorage;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IAudioSystem? _audio;
    private readonly IClientConfigManager? _clientConfig;
    public ClientConfigProcessor(
    ITextureStorageService textureStorage,
    IAsyncOperationSupervisor operations,
    IAudioSystem? audio = null,
    IClientConfigManager? clientConfig = null)
    {
        _textureStorage = textureStorage;
        _operations = operations;
        _audio = audio;
        _clientConfig = clientConfig;
    }

    public void Process(ClientConfigPacket packet)
    {
        // Громкость принадлежит локальным настройкам пользователя.
        // Серверные значения не должны перезаписывать их при каждом входе.
        if (_audio != null && _clientConfig != null)
        {
            foreach (AudioBusRegistry.BusBinding binding in AudioBusRegistry.Buses)
            {
                _audio.SetBusVolume(binding.Bus, binding.Read(_clientConfig.Config.Audio));
            }
        }

        if (packet.UnrenderedTextures != null && packet.UnrenderedTextures.Count > 0)
        {
            _operations.Run(
                "preload_textures",
                cancellationToken => _textureStorage.PreloadTexturesAsync(packet.UnrenderedTextures, cancellationToken));
        }
    }
}
