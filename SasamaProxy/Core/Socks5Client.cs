using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SasamaProxy.Core
{
    /// <summary>
    /// Minimal SOCKS5 client (RFC 1928), no-auth only, IPv4 CONNECT only -
    /// that is all V2Ray's local "socks" inbound needs from us. This is used
    /// for two things: (1) the relay uses it to open the real, proxied
    /// connection to the game/browser's true destination once a redirected
    /// connection lands on us, and (2) the latency check uses it to measure
    /// a real proxied round trip rather than a bare TCP connect.
    /// </summary>
    public static class Socks5Client
    {
        public static async Task<bool> ConnectAsync(NetworkStream stream, byte[] destIpv4, ushort destPort, CancellationToken token)
        {
            try
            {
                byte[] greeting = { 0x05, 0x01, 0x00 }; // ver 5, 1 method, no-auth
                await stream.WriteAsync(greeting, 0, greeting.Length, token).ConfigureAwait(false);

                byte[] greetingReply = new byte[2];
                if (!await ReadExactAsync(stream, greetingReply, 2, token).ConfigureAwait(false)) return false;
                if (greetingReply[0] != 0x05 || greetingReply[1] != 0x00) return false; // proxy demands auth we don't support

                byte[] req = new byte[10];
                req[0] = 0x05; req[1] = 0x01; req[2] = 0x00; req[3] = 0x01; // CONNECT, IPv4
                Array.Copy(destIpv4, 0, req, 4, 4);
                req[8] = (byte)(destPort >> 8);
                req[9] = (byte)(destPort & 0xFF);
                await stream.WriteAsync(req, 0, req.Length, token).ConfigureAwait(false);

                byte[] head = new byte[4];
                if (!await ReadExactAsync(stream, head, 4, token).ConfigureAwait(false)) return false;
                if (head[1] != 0x00) return false; // REP != succeeded

                int addrLen;
                switch (head[3])
                {
                    case 0x01: addrLen = 4; break;
                    case 0x04: addrLen = 16; break;
                    case 0x03:
                        byte[] lenBuf = new byte[1];
                        if (!await ReadExactAsync(stream, lenBuf, 1, token).ConfigureAwait(false)) return false;
                        addrLen = lenBuf[0];
                        break;
                    default:
                        return false;
                }

                byte[] rest = new byte[addrLen + 2]; // bound address + bound port, we don't need the values
                if (!await ReadExactAsync(stream, rest, rest.Length, token).ConfigureAwait(false)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
        {
            int total = 0;
            while (total < count)
            {
                int n = await stream.ReadAsync(buffer, total, count - total, token).ConfigureAwait(false);
                if (n <= 0) return false;
                total += n;
            }
            return true;
        }
    }
}
