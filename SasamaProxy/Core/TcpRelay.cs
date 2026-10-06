using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SasamaProxy.Native;

namespace SasamaProxy.Core
{
    /// <summary>
    /// This is the piece that makes the NAT redirect actually functional
    /// rather than just a passthrough. WinDivert only changes where a
    /// connection's packets go on the wire; it cannot itself speak the SOCKS5
    /// protocol V2Ray expects on its inbound. So: NatEngine redirects the raw
    /// TCP connection to us, and we are the one who actually dials out to
    /// V2Ray with a proper SOCKS5 CONNECT for the real destination, then
    /// copy bytes both ways. Without this piece the app would just see a
    /// raw TLS/HTTP handshake arrive at V2Ray's SOCKS port and reject it.
    /// </summary>
    public class TcpRelay
    {
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Func<ushort, NatEntry?> _lookup;
        private string _proxyIp;
        private int _proxyPort;
        private IPAddress _localMachineIp = IPAddress.Loopback;

        public ushort Port { get; private set; }

        /// <param name="localMachineIp">
        /// Must be the EXACT same address NatEngine is using as its redirect
        /// target for this session. An earlier version of this file detected
        /// its own copy independently via a `static readonly` field - which
        /// runs once, the first time this class is touched (in practice: at
        /// MainWindow construction, long before Start Proxy is even clicked).
        /// NatEngine detects its own copy fresh every time Start() runs. On a
        /// network that's the same address in practice, but there's no
        /// guarantee, and the security check below requires an EXACT match -
        /// any mismatch would silently reject every single redirected
        /// connection with no obvious cause. Passing one value in from
        /// MainWindow, detected once and shared, removes the risk entirely.
        /// </param>
        public void Start(string proxyIp, int proxyPort, Func<ushort, NatEntry?> lookup, IPAddress localMachineIp)
        {
            _proxyIp = proxyIp;
            _proxyPort = proxyPort;
            _lookup = lookup;
            _localMachineIp = localMachineIp ?? IPAddress.Loopback;

            // Bound to ALL interfaces (0.0.0.0), not just loopback. This is
            // required for the NAT redirect to work at all - see the long
            // comment in NatEngine.cs about WinDivert + 127.0.0.1. Because
            // this opens the port on every interface (including any LAN),
            // HandleClientAsync below explicitly rejects any connection that
            // isn't genuinely from this same machine, so it can't be used as
            // an open relay by anyone else on your network.
            _listener = new TcpListener(IPAddress.Any, 0); // OS picks a free port, avoids any collision
            _listener.Start();
            Port = (ushort)((IPEndPoint)_listener.LocalEndpoint).Port;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            Task.Run(() => AcceptLoopAsync(token));

            Logger.Log($"Relay listening on port {Port} on all interfaces (forwards to {proxyIp}:{proxyPort} via SOCKS5, accepting only from {_localMachineIp}).");
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch
                {
                    break; // listener stopped
                }

                _ = HandleClientAsync(client, token); // fire and forget, one task per connection
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    var remoteEp = client.Client.RemoteEndPoint as IPEndPoint;
                    if (remoteEp == null) return;
                    Logger.Log($"Relay: accepted a redirected connection from port {remoteEp.Port}.");

                    // Security check: this listener is bound to all interfaces
                    // (required for the redirect to work - see Start() above),
                    // so without this check anyone else on the same Wi-Fi/LAN
                    // could connect to this port and tunnel through it. Only a
                    // connection that is genuinely this same machine talking to
                    // itself (which is what a redirected app connection always
                    // looks like) is allowed through.
                    if (!IPAddress.IsLoopback(remoteEp.Address) && !remoteEp.Address.Equals(_localMachineIp))
                    {
                        Logger.Log($"Relay: rejected a connection from {remoteEp.Address} - not this machine (expected {_localMachineIp}).");
                        return;
                    }

                    ushort appLocalPort = (ushort)remoteEp.Port;

                    var entryOpt = _lookup(appLocalPort);
                    if (entryOpt == null)
                    {
                        Logger.Log($"Relay: no NAT record for redirected connection on port {appLocalPort}, dropping.");
                        return;
                    }
                    var entry = entryOpt.Value;

                    using (var upstream = new TcpClient())
                    {
                        var connectTask = upstream.ConnectAsync(_proxyIp, _proxyPort);
                        var winner = await Task.WhenAny(connectTask, Task.Delay(5000, token)).ConfigureAwait(false);
                        if (winner != connectTask)
                        {
                            Logger.Log("Relay: connecting to local proxy timed out.");
                            return;
                        }
                        await connectTask.ConfigureAwait(false); // observe any connect exception

                        var upstreamStream = upstream.GetStream();
                        byte[] destBytes = NetOrder.ValueToBytes(entry.DestAddr);
                        bool ok = await Socks5Client.ConnectAsync(upstreamStream, destBytes, entry.DestPort, token).ConfigureAwait(false);
                        if (!ok)
                        {
                            Logger.Log($"Relay: SOCKS5 handshake to local proxy failed for {destBytes[0]}.{destBytes[1]}.{destBytes[2]}.{destBytes[3]}:{entry.DestPort}.");
                            return;
                        }
                        Logger.Log($"Relay: tunnel established to {destBytes[0]}.{destBytes[1]}.{destBytes[2]}.{destBytes[3]}:{entry.DestPort} via proxy - streaming data now.");

                        var clientStream = client.GetStream();
                        var t1 = clientStream.CopyToAsync(upstreamStream);
                        var t2 = upstreamStream.CopyToAsync(clientStream);
                        await Task.WhenAny(t1, t2).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Relay connection error: " + ex.Message);
                }
            }
        }
    }
}
