#nullable enable

using System;
using System.Net;
using System.Net.Sockets;

namespace Fodinae.Networking.Connection
{
    /// <summary>
    /// Вид сетевого транспорта. Dummy — офлайн-заглушка для локального теста,
    /// Tcp — реальный Darkar25 транспорт (MinesServerNetworking).
    /// </summary>
    public enum ConnectionTransportKind
    {
        Dummy,
        Tcp,
    }

    /// <summary>
    /// Чистое решение выбора транспорта и разбора endpoint'а. Не создаёт
    /// соединений и не зависит от Unity runtime — покрывается unit-тестами.
    /// </summary>
    public static class ConnectionTransportConfig
    {
        public const string DefaultServerHost = "127.0.0.1";
        public const int DefaultServerPort = 7777;

        public static ConnectionTransportKind SelectTransport(bool useDummyConnection)
        {
            return useDummyConnection
                ? ConnectionTransportKind.Dummy
                : ConnectionTransportKind.Tcp;
        }

        /// <summary>
        /// Разбирает host:port в <see cref="IPAddress"/>. Пустой host подставляет
        /// <see cref="DefaultServerHost"/>. Возвращает false при невалидном порте
        /// или нерезолвящемся хосте.
        /// </summary>
        public static bool TryResolveEndpoint(
            string? host,
            int port,
            out IPAddress address,
            out int validatedPort)
        {
            address = null!;
            validatedPort = 0;
            if (port <= 0 || port > 65535)
            {
                return false;
            }

            string resolvedHost = string.IsNullOrWhiteSpace(host)
                ? DefaultServerHost
                : host.Trim();
            if (!IPAddress.TryParse(resolvedHost, out address) &&
                !TryResolveHostname(resolvedHost, out address))
            {
                return false;
            }

            validatedPort = port;
            return true;
        }

        private static bool TryResolveHostname(string host, out IPAddress address)
        {
            address = null!;
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(host);
                if (addresses.Length == 0)
                {
                    return false;
                }

                address = addresses[0];
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                // Не-хостнейм строка (пробелы, спецсимволы) — не endpoint.
                return false;
            }
        }
    }
}
