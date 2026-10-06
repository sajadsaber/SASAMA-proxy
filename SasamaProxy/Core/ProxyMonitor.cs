using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SasamaProxy.Core
{
    /// <summary>
    /// Drives both the online/offline dot + latency number in the UI, and
    /// (via the Healthy flag) whether NatEngine is currently allowed to
    /// redirect traffic at all. There is no such thing as "ping" through a
    /// SOCKS5 proxy (SOCKS doesn't carry ICMP) - what we actually measure is
    /// the time to open a real proxied TCP connection to a well-known,
    /// always-on host (1.1.1.1:443), which is a truer measure of "will my
    /// game/browser traffic actually get through" than a raw ICMP ping
    /// would be anyway.
    /// </summary>
    public class ProxyMonitor
    {
        public event Action<bool, int> StatusChanged; // (healthy, latencyMs or -1)

        private CancellationTokenSource _cts;
        private volatile bool _healthy;
        public bool Healthy => _healthy;

        private Func<string> _getIp;
        private Func<int> _getPort;

        public void Start(Func<string> getIp, Func<int> getPort)
        {
            _getIp = getIp;
            _getPort = getPort;
            _cts = new CancellationTokenSource();
            Task.Run(() => LoopAsync(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _healthy = false;
        }

        private async Task LoopAsync(CancellationToken token)
        {
            bool? lastLogged = null; // null guarantees the very first result always logs, even if it's the same value every time after
            while (!token.IsCancellationRequested)
            {
                int latency = await CheckOnceAsync(_getIp(), _getPort(), token).ConfigureAwait(false);
                _healthy = latency >= 0;
                StatusChanged?.Invoke(_healthy, latency);

                if (_healthy != lastLogged)
                {
                    Logger.Log(_healthy
                        ? $"Local proxy check: online, {latency} ms."
                        : "Local proxy check: offline/unreachable - redirection is paused until this recovers.");
                    lastLogged = _healthy;
                }

                try { await Task.Delay(5000, token).ConfigureAwait(false); }
                catch { break; }
            }
        }

        /// <summary>Runs one check immediately - used by the manual "Ping" button.</summary>
        public async Task<int> CheckOnceAsync(string ip, int port, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using (var tcp = new TcpClient())
                {
                    var connectTask = tcp.ConnectAsync(ip, port);
                    var winner = await Task.WhenAny(connectTask, Task.Delay(3000, token)).ConfigureAwait(false);
                    if (winner != connectTask) return -1;
                    await connectTask.ConfigureAwait(false);

                    var stream = tcp.GetStream();
                    byte[] target = { 1, 1, 1, 1 }; // Cloudflare, stable and fast worldwide
                    bool ok = await Socks5Client.ConnectAsync(stream, target, 443, token).ConfigureAwait(false);
                    if (!ok) return -1;

                    sw.Stop();
                    return (int)sw.ElapsedMilliseconds;
                }
            }
            catch
            {
                return -1;
            }
        }
    }
}
