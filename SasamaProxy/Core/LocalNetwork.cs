using System.Net;
using System.Net.Sockets;

namespace SasamaProxy.Core
{
    /// <summary>
    /// One shared detection routine, called exactly once per Start Proxy
    /// click, from MainWindow, with the result handed to both NatEngine and
    /// TcpRelay. An earlier version had NatEngine and TcpRelay each detect
    /// their own copy of "this machine's LAN IP" independently, at different
    /// times (one at app startup, one fresh per Start click) - functionally
    /// almost always the same address in practice, but with no guarantee,
    /// and TcpRelay's security check requires an EXACT match against
    /// whatever NatEngine used as the redirect target. Any mismatch there
    /// would silently reject every redirected connection with no obvious
    /// cause. Sharing one value removes that risk entirely.
    /// </summary>
    public static class LocalNetwork
    {
        public static IPAddress DetectLocalIPv4()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("8.8.8.8", 65530); // UDP "connect" sends nothing, just picks a route
                    return (socket.LocalEndPoint as IPEndPoint)?.Address ?? IPAddress.Loopback;
                }
            }
            catch
            {
                return IPAddress.Loopback;
            }
        }
    }
}
