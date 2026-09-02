using System.Net;
using System.Net.Sockets;

namespace SurvivalBackend.Options;

public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>
    /// IP-адреса или CIDR-диапазоны (например "10.0.0.5" или "10.0.0.0/24") reverse-proxy/LB,
    /// которым разрешено выставлять X-Forwarded-For / X-Forwarded-Proto.
    /// Пусто по умолчанию: если перед бэкендом нет прокси (или это ещё не решено),
    /// forwarded-заголовки не обрабатываются вовсе, и везде используется настоящий IP сокета.
    /// </summary>
    public List<string> TrustedNetworks { get; set; } = [];

    public static bool TryParseNetwork(string value, out IPAddress prefix, out int prefixLength)
    {
        var parts = value.Split('/', 2);
        if (parts.Length == 2
            && IPAddress.TryParse(parts[0], out var parsedPrefix)
            && int.TryParse(parts[1], out var parsedLength))
        {
            prefix = parsedPrefix;
            prefixLength = parsedLength;
            return true;
        }

        if (IPAddress.TryParse(value, out var address))
        {
            prefix = address;
            prefixLength = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
            return true;
        }

        prefix = IPAddress.None;
        prefixLength = 0;
        return false;
    }
}
