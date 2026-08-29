#nullable enable

using System;
using Fodinae.Core.Interfaces;
using Fodinae.UI.Builders;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    public class PacketUIBuilder
    {
        private readonly IAssetLoader _assetLoader;
        private readonly PacketUIBuilderFactory _builderFactory = new();

        public PacketUIBuilder(IAssetLoader assetLoader)
        {
            _assetLoader = assetLoader ?? throw new ArgumentNullException(nameof(assetLoader));
        }

        internal IAssetLoader AssetLoader => _assetLoader;

        public VisualElement? Build(IGUIComponentPacket packet)
        {
            var builder = _builderFactory.CreateBuilder(packet);
            VisualElement? element;

            if (builder != null)
            {
                element = builder.Build(packet, this);
            }
            else
            {
                element = new Label($"[Unimplemented: {packet.GetType().Name}]");
                element.style.backgroundColor = Color.magenta;
            }

            StyleApplicator.ApplyStyles(element!, packet);
            ApplyAttachedProperties(element!, packet);

            element!.userData = packet;

            return element;
        }

        private static void ApplyAttachedProperties(VisualElement element, IGUIComponentPacket packet)
        {
            if (packet.AttachedProperties == null || packet.AttachedProperties.Length == 0)
            {
                return;
            }

            foreach (var prop in packet.AttachedProperties)
            {
                // Протокольные числа всегда в инвариантной культуре (точка как
                // десятичный разделитель) — на Windows с региональными RU/DE/TR
                // float.TryParse по текущей культуре молча теряет геометрию окон.
                if (prop.Key == "Canvas.X" && float.TryParse(
                        prop.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float left))
                {
                    element.style.position = Position.Absolute;
                    element.style.left = left;
                }

                if (prop.Key == "Canvas.Y" && float.TryParse(
                        prop.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float top))
                {
                    element.style.position = Position.Absolute;
                    element.style.top = top;
                }

                if (prop.Key == "Canvas.Width" && float.TryParse(
                        prop.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float width))
                {
                    element.style.position = Position.Absolute;
                    element.style.width = width;
                }

                if (prop.Key == "Canvas.Height" && float.TryParse(
                        prop.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float height))
                {
                    element.style.position = Position.Absolute;
                    element.style.height = height;
                }
            }
        }
    }
}
