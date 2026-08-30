#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae;
using Fodinae.Core.Interfaces;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual; // Corrected using directive for ImagePacket
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    public class ImagePacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement? Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not ImagePacket imagePacket)
            {
                return null;
            }

            var element = new VisualElement();
            element.style.width = imagePacket.Width;
            element.style.height = imagePacket.Height;

            var cts = new CancellationTokenSource();
            element.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                cts.Cancel();
                cts.Dispose();
            });

            builder.Operations.Run(
                $"load_packet_image_{imagePacket.URI}",
                supervisorToken => LoadImage(
                    element,
                    imagePacket.URI,
                    builder.AssetLoader,
                    cts.Token,
                    supervisorToken));

            return element;
        }

        private static async UniTask LoadImage(
            VisualElement element,
            string uri,
            IAssetLoader loader,
            CancellationToken elementToken,
            CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                elementToken,
                supervisorToken);
            CancellationToken token = linkedCancellation.Token;
            Texture2D? texture;
            try
            {
                texture = await loader.GetTextureAsync(uri, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // Packet images are optional presentation assets. A missing
                // icon must not become an unhandled UniTaskVoid exception or
                // block the entire packet window.
                Debug.LogWarning(
                    $"[ImagePacketBuilder] Optional image '{uri}' was skipped: {exception.Message}");
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (texture == null)
            {
                Debug.LogWarning(
                    $"[ImagePacketBuilder] Optional image '{uri}' returned no texture; skipped.");
                return;
            }

            if (element != null)
            {
                element.style.backgroundImage = new StyleBackground(texture);
            }
        }
    }
}
