using System.Net;
using System.Net.Sockets;

namespace NauAssist.Backend.Features.Web;

/// <summary>
/// SSRF-Schutz für den Fetch von user-/LLM-gelieferten URLs. Chat-Tools und Watcher fetchen
/// Ziel-URLs serverseitig — ohne Schutz könnte ein (ggf. prompt-injizierter) Aufruf interne
/// Dienste (Cloud-Metadaten <c>169.254.169.254</c>, <c>localhost</c>, RFC1918) erreichen.
///
/// Der eigentliche IP-Block läuft als <see cref="SocketsHttpHandler.ConnectCallback"/>:
/// die Auflösung+Prüfung passiert unmittelbar vor dem Connect (kein DNS-Rebinding-TOCTOU),
/// und jeder Redirect-Hop läuft erneut durch den Callback. Der SearXNG-Such-Client nutzt
/// diesen Handler bewusst NICHT — SearXNG ist ein gewollt interner Dienst.
/// </summary>
internal static class SsrfGuard
{
    /// <summary>Erlaubt nur absolute http(s)-URLs. Frühe Validierung beim Anlegen eines Jobs.</summary>
    public static bool IsAllowedUrl(string url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        uri = parsed;
        return true;
    }

    /// <summary>True, wenn die Adresse intern/privat/nicht-routbar ist und nicht angefragt werden darf.</summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        // IPv4-mapped (::ffff:w.x.y.z) UND IPv4-kompatible (::w.x.y.z, RFC 4291) IPv6-Adressen
        // auf den IPv4-Pfad ziehen — sonst würde z.B. http://[::10.0.0.1] als routbares IPv6
        // durchgehen, das der OS-Stack beim Connect auf eine interne IPv4 mappt.
        var ip = NormalizeEmbeddedIPv4(address);

        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] switch
            {
                0 => true,                                  // 0.0.0.0/8
                10 => true,                                 // 10.0.0.0/8 (privat)
                127 => true,                                // 127.0.0.0/8 (loopback)
                169 when b[1] == 254 => true,               // 169.254.0.0/16 (link-local, Cloud-Metadaten)
                172 when b[1] is >= 16 and <= 31 => true,   // 172.16.0.0/12 (privat)
                192 when b[1] == 168 => true,               // 192.168.0.0/16 (privat)
                100 when b[1] is >= 64 and <= 127 => true,  // 100.64.0.0/10 (CGNAT)
                >= 224 => true,                             // 224.0.0.0/4 Multicast + 240.0.0.0/4 reserviert
                _ => false,
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;         // fc00::/7 (unique local)
            return false;
        }

        return true; // unbekannte Adressfamilie ⇒ sicherheitshalber blockieren
    }

    /// <summary>Zieht in IPv6 eingebettete IPv4-Adressen (mapped <c>::ffff:</c> und compatible <c>::</c>) auf IPv4.</summary>
    private static IPAddress NormalizeEmbeddedIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address;
        if (address.IsIPv4MappedToIPv6) return address.MapToIPv4();

        var b = address.GetAddressBytes(); // 16 Bytes
        for (var i = 0; i < 12; i++)
        {
            if (b[i] != 0) return address; // kein ::w.x.y.z
        }

        return new IPAddress(new[] { b[12], b[13], b[14], b[15] });
    }

    /// <summary>HTTP-Handler, der Verbindungen zu internen/privaten Adressen verweigert (auch bei Redirects).</summary>
    public static HttpMessageHandler CreateGuardedHandler() => new SocketsHttpHandler
    {
        ConnectCallback = async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var port = context.DnsEndPoint.Port;

            var addresses = IPAddress.TryParse(host, out var literal)
                ? new[] { literal }
                : await Dns.GetHostAddressesAsync(host, ct);

            var target = addresses.FirstOrDefault(a => !IsBlockedAddress(a))
                ?? throw new HttpRequestException(
                    $"Ziel-Host '{host}' verweist nur auf blockierte (interne/private) Adressen — SSRF-Schutz.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}
