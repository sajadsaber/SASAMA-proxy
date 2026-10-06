using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using SasamaProxy.Native;

namespace SasamaProxy.Core
{
    /// <summary>
    /// This is the piece that actually redirects traffic. Design, in short:
    ///
    ///  - One WinDivert handle at the SOCKET layer tells us, the instant a
    ///    process CALLS connect() - before any packet is even sent, whether
    ///    that connection will succeed or not - which process owns it
    ///    (ProcessId) and which local port it is using.
    ///    IMPORTANT: an earlier version of this file used the FLOW layer's
    ///    FLOW_ESTABLISHED event instead. That was a real bug: FLOW_ESTABLISHED
    ///    only fires once a TCP connection is already fully connected - which
    ///    is exactly backwards for a censorship-bypass tool, since a blocked
    ///    destination by definition never reaches "established" on its own,
    ///    so tracking would never even learn about it in time to redirect it.
    ///    SOCKET_CONNECT fires at connect()-call time regardless of whether
    ///    the direct connection would succeed, which is what we actually need.
    ///    The Network layer cannot see process IDs at all, so this Socket-layer
    ///    tap is the only way to learn which process a port belongs to.
    ///
    ///  - One WinDivert handle at the NETWORK layer sees every TCP packet
    ///    entering or leaving the machine. For packets whose local port is
    ///    one we're tracking (from the Socket layer above):
    ///      * outbound, non-loopback packet (app -> real internet host):
    ///        rewrite the destination to this machine's own LAN IP : <relay
    ///        port> and record the real destination in a NAT table keyed by
    ///        local port.
    ///      * inbound, loopback packet (relay -> app): rewrite the source
    ///        back to the real remote host/port so the app's own TCP stack
    ///        accepts it as if it came from where it originally asked.
    ///    Everything else is re-sent completely unmodified.
    ///
    ///  - Every packet is sent back out in a `finally` block, so a bug or
    ///    exception in the matching logic can never leave a packet "stuck"
    ///    (which is exactly the internet-blackout failure mode described in
    ///    the spec). Worst case on a bug here is a dropped or unmodified
    ///    packet, never a hung driver holding all system traffic.
    ///
    /// UDP is intentionally NOT handled here - see the README for why.
    /// </summary>
    public class NatEngine
    {
        private IntPtr _flowHandle = IntPtr.Zero; // name kept for a minimal diff; now opened at the SOCKET layer, see class remarks
        private IntPtr _networkHandle = IntPtr.Zero;
        private Thread _flowThread;
        private Thread _networkThread;
        private Thread _cleanupThread;
        private volatile bool _running;


        // localPort -> owning PID, populated by the Flow-layer thread.
        private readonly ConcurrentDictionary<ushort, uint> _trackedPorts = new ConcurrentDictionary<ushort, uint>();

        // localPort -> original destination, populated by the Network-layer thread.
        private readonly ConcurrentDictionary<ushort, NatEntry> _natTable = new ConcurrentDictionary<ushort, NatEntry>();

        private HashSet<string> _trackedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _namesLock = new object();

        private ushort _relayPort;

        // IMPORTANT: this must be the machine's real local network IP, NOT
        // 127.0.0.1. WinDivert cannot reliably re-inject a real outbound
        // packet retargeted at the literal loopback address - this is a
        // known, documented WinDivert limitation (see
        // github.com/basil00/WinDivert/issues/82), not something specific
        // to this app. Redirecting to the machine's own real LAN IP instead
        // works, because Windows treats any self-to-self traffic as
        // loopback internally regardless of which address is used.
        private uint _relayAddr = 0x7F000001u;

        private static string FormatIp(uint v) => $"{(v >> 24) & 0xFF}.{(v >> 16) & 0xFF}.{(v >> 8) & 0xFF}.{v & 0xFF}";

        /// <summary>Set by MainWindow to reflect ProxyMonitor's current health.</summary>
        public Func<bool> IsProxyHealthy = () => false;

        public bool IsRunning => _running;

        /// <summary>Update the live set of process names to redirect. Safe to call while running.</summary>
        public void SetTrackedProcessNames(IEnumerable<string> names)
        {
            lock (_namesLock)
            {
                _trackedProcessNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            }
        }

        private bool IsTrackedName(string name)
        {
            lock (_namesLock)
            {
                return name != null && _trackedProcessNames.Contains(name);
            }
        }

        public NatEntry? LookupNatEntry(ushort appLocalPort)
        {
            if (_natTable.TryGetValue(appLocalPort, out var entry))
                return entry;
            return null;
        }

        public void Start(IEnumerable<string> trackedProcessNames, ushort relayPort, IPAddress relayAddr)
        {
            if (_running) return;
            SetTrackedProcessNames(trackedProcessNames);
            _relayPort = relayPort;
            byte[] b = (relayAddr ?? IPAddress.Loopback).GetAddressBytes();
            _relayAddr = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
            Logger.Log($"Redirect target: {FormatIp(_relayAddr)}:{relayPort} (this machine's own LAN IP - required by a WinDivert quirk, not a mistake; see README section on this).");

            _flowHandle = WinDivertNative.WinDivertOpen("tcp", WinDivertLayer.Socket, 0, WinDivertFlag.Sniff | WinDivertFlag.RecvOnly);
            if (!WinDivertNative.IsValidHandle(_flowHandle))
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Could not open WinDivert SOCKET handle (Win32 error {err}). Are you running as Administrator?");
            }

            _networkHandle = WinDivertNative.WinDivertOpen("tcp", WinDivertLayer.Network, 0, 0);
            if (!WinDivertNative.IsValidHandle(_networkHandle))
            {
                int err = Marshal.GetLastWin32Error();
                SafeClose(ref _flowHandle);
                throw new InvalidOperationException($"Could not open WinDivert NETWORK handle (Win32 error {err}).");
            }

            _running = true;
            _flowThread = new Thread(FlowLoop) { IsBackground = true, Name = "SasamaProxy-Flow" };
            _networkThread = new Thread(NetworkLoop) { IsBackground = true, Name = "SasamaProxy-Network" };
            _cleanupThread = new Thread(CleanupLoop) { IsBackground = true, Name = "SasamaProxy-Cleanup" };
            _flowThread.Start();
            _networkThread.Start();
            _cleanupThread.Start();

            Logger.Log("NAT engine started (relay port " + relayPort + ").");
        }

        /// <summary>Graceful stop: closes handles and waits briefly for the loops to exit.</summary>
        public void Stop()
        {
            if (!_running && _flowHandle == IntPtr.Zero && _networkHandle == IntPtr.Zero) return;
            _running = false;
            SafeClose(ref _flowHandle);
            SafeClose(ref _networkHandle);
            _flowThread?.Join(2000);
            _networkThread?.Join(2000);
            _cleanupThread?.Join(500);
            _trackedPorts.Clear();
            _natTable.Clear();
            Logger.Log("NAT engine stopped.");
        }

        /// <summary>
        /// The "highly visible emergency stop" required by spec: force-closes
        /// the WinDivert handles immediately from any thread, any state, no
        /// waiting. Safe to call even if the engine is already stopped or is
        /// in a weird half-started state.
        /// </summary>
        public void EmergencyStop()
        {
            _running = false;
            SafeClose(ref _flowHandle);
            SafeClose(ref _networkHandle);
            Logger.Log("EMERGENCY STOP triggered - WinDivert handles force-closed.");
        }

        private static void SafeClose(ref IntPtr handle)
        {
            var h = handle;
            handle = IntPtr.Zero;
            if (WinDivertNative.IsValidHandle(h))
            {
                try { WinDivertNative.WinDivertClose(h); } catch { /* never let cleanup throw */ }
            }
        }

        // ---------------- Flow layer: PID <-> local port bookkeeping ----------------

        private void FlowLoop()
        {
            var addr = new WINDIVERT_ADDRESS();
            while (_running)
            {
                bool ok;
                try
                {
                    ok = WinDivertNative.WinDivertRecvNoPacket(_flowHandle, IntPtr.Zero, 0, out _, ref addr);
                }
                catch (Exception ex)
                {
                    Logger.Log("Flow loop fatal error: " + ex.Message);
                    break;
                }

                if (!ok)
                {
                    if (!_running) break;
                    continue; // transient recv error, try again
                }

                try
                {
                    ushort localPort = addr.FlowLocalPortRaw; // already host-order, see WinDivertInterop.cs remarks
                    uint pid = addr.FlowProcessId;

                    if (addr.Event == WinDivertEvent.SocketConnect)
                    {
                        string name = TryGetProcessName(pid);
                        if (IsTrackedName(name))
                        {
                            _trackedPorts[localPort] = pid;
                            Logger.Log($"Tracking new connection: {name} (pid {pid}) on local port {localPort}");
                        }
                    }
                    else if (addr.Event == WinDivertEvent.SocketClose)
                    {
                        _trackedPorts.TryRemove(localPort, out _);
                        _natTable.TryRemove(localPort, out _);
                    }
                }
                catch
                {
                    // Malformed/unexpected event - ignore and keep going, this thread
                    // never modifies or blocks any traffic so there's nothing to fail open on.
                }
            }
        }

        private static string TryGetProcessName(uint pid)
        {
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                    return p.ProcessName + ".exe";
            }
            catch
            {
                return null; // process already exited - known WinDivert race, see docs
            }
        }

        // ---------------- Network layer: the actual NAT ----------------

        private void NetworkLoop()
        {
            byte[] packet = new byte[65535];
            var addr = new WINDIVERT_ADDRESS();

            while (_running)
            {
                uint recvLen;
                bool ok;
                try
                {
                    ok = WinDivertNative.WinDivertRecv(_networkHandle, packet, (uint)packet.Length, out recvLen, ref addr);
                }
                catch (Exception ex)
                {
                    Logger.Log("Network loop fatal error: " + ex.Message);
                    break;
                }

                if (!ok)
                {
                    if (!_running) break;
                    continue;
                }

                bool modified = false;
                try
                {
                    modified = ProcessPacket(packet, recvLen, ref addr);
                }
                catch (Exception ex)
                {
                    // Fail OPEN: whatever happens in the matching logic, the packet
                    // below still gets sent. Worst case here is a packet that fails
                    // to get redirected once; it is never left stuck.
                    Logger.Log("Packet processing error (packet forwarded as-is): " + ex.Message);
                    modified = false;
                }
                finally
                {
                    try
                    {
                        if (modified)
                            WinDivertNative.WinDivertHelperCalcChecksums(packet, recvLen, ref addr, 0);

                        bool sent = WinDivertNative.WinDivertSend(_networkHandle, packet, recvLen, out _, ref addr);
                        if (!sent)
                        {
                            int err = Marshal.GetLastWin32Error();
                            Interlocked.Increment(ref _sendFailureCount);
                            // Only log occasionally - a burst of these would otherwise flood
                            // the log faster than it's readable.
                            if (_sendFailureCount <= 20 || _sendFailureCount % 200 == 0)
                                Logger.Log($"WinDivertSend failed (Win32 error {err}), packet dropped. [{_sendFailureCount} total this session]");
                        }
                    }
                    catch
                    {
                        // If even the send throws (e.g. handle just got closed by
                        // Stop()/EmergencyStop() on another thread) there is nothing
                        // more we can safely do with this packet - drop it and move on.
                    }
                }
            }
        }

        private int _sendFailureCount;

        /// <summary>Returns true if the packet buffer was modified and needs a checksum recalc.</summary>
        private bool ProcessPacket(byte[] packet, uint len, ref WINDIVERT_ADDRESS addr)
        {
            if (len < 20) return false;
            if ((packet[0] >> 4) != 4) return false; // IPv4 only for now, see README
            int ipHeaderLen = (packet[0] & 0x0F) * 4;
            if (ipHeaderLen < 20 || len < ipHeaderLen + 20) return false;
            if (packet[9] != 6) return false; // not TCP

            int tcpOffset = ipHeaderLen;
            ushort srcPort = NetOrder.ReadU16BE(packet, tcpOffset + 0);
            ushort dstPort = NetOrder.ReadU16BE(packet, tcpOffset + 2);

            if (addr.Outbound && !addr.Loopback)
            {
                // Forward leg: app -> real destination. Redirect only if this local
                // port belongs to a tracked process AND the local proxy is currently
                // reachable. If the proxy is down we deliberately do nothing here,
                // which lets the connection attempt its real destination directly
                // instead of black-holing into a dead relay.
                if (!_trackedPorts.ContainsKey(srcPort)) return false;
                if (!IsProxyHealthy()) return false;

                uint origDest = NetOrder.ReadU32BE(packet, 16);
                ushort origDestPort = dstPort;

                bool isNewFlow = !_natTable.ContainsKey(srcPort);
                _natTable[srcPort] = new NatEntry
                {
                    DestAddr = origDest,
                    DestPort = origDestPort,
                    LastSeenTicks = DateTime.UtcNow.Ticks
                };

                if (isNewFlow)
                    Logger.Log($"Redirecting port {srcPort} -> was going to {NetOrder.ValueToBytes(origDest)[0]}.{NetOrder.ValueToBytes(origDest)[1]}.{NetOrder.ValueToBytes(origDest)[2]}.{NetOrder.ValueToBytes(origDest)[3]}:{origDestPort}, now going to relay.");

                NetOrder.WriteU32BE(packet, 16, _relayAddr);
                NetOrder.WriteU16BE(packet, tcpOffset + 2, _relayPort);
                return true;
            }

            if (!addr.Outbound && addr.Loopback)
            {
                // Return leg: relay -> app. dstPort here is the app's own local port,
                // which is exactly our NAT table key.
                if (_natTable.TryGetValue(dstPort, out var entry))
                {
                    NetOrder.WriteU32BE(packet, 12, entry.DestAddr);
                    NetOrder.WriteU16BE(packet, tcpOffset + 0, entry.DestPort);
                    entry.LastSeenTicks = DateTime.UtcNow.Ticks;
                    _natTable[dstPort] = entry;
                    return true;
                }
                return false;
            }

            // outbound+loopback (the relay's own leg to us, or to V2Ray) and
            // inbound+!loopback (ordinary traffic from the real internet) are
            // never touched.
            return false;
        }

        private void CleanupLoop()
        {
            // Belt-and-suspenders: FLOW_DELETED should remove entries already,
            // but if one is ever missed this sweeps it out after a few minutes
            // of inactivity so the tables never grow forever during a long session.
            while (_running)
            {
                try
                {
                    var cutoff = DateTime.UtcNow.AddMinutes(-3).Ticks;
                    foreach (var kv in _natTable)
                    {
                        if (kv.Value.LastSeenTicks < cutoff)
                            _natTable.TryRemove(kv.Key, out _);
                    }
                }
                catch { /* never let housekeeping crash the engine */ }

                for (int i = 0; i < 30 && _running; i++)
                    Thread.Sleep(1000);
            }
        }
    }
}
