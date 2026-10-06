using System;
using System.Runtime.InteropServices;

namespace SasamaProxy.Native
{
    // Thin, careful P/Invoke wrapper around WinDivert.dll (v2.2.2).
    // Every struct/offset here was cross-checked against the real
    // include/windivert.h shipped in the official basil00/WinDivert
    // 2.2.2 release, not guessed from memory. If you ever upgrade the
    // WinDivert.dll in Redist\x64, re-check this file against the new
    // windivert.h header before assuming it still matches.

    public enum WinDivertLayer : uint
    {
        Network = 0,
        NetworkForward = 1,
        Flow = 2,
        Socket = 3,
        Reflect = 4
    }

    public static class WinDivertEvent
    {
        public const byte NetworkPacket = 0;
        public const byte FlowEstablished = 1;
        public const byte FlowDeleted = 2;
        public const byte SocketBind = 3;
        public const byte SocketConnect = 4;
        public const byte SocketListen = 5;
        public const byte SocketAccept = 6;
        public const byte SocketClose = 7;
        public const byte ReflectOpen = 8;
        public const byte ReflectClose = 9;
    }

    public static class WinDivertFlag
    {
        public const ulong Sniff = 0x0001;
        public const ulong Drop = 0x0002;
        public const ulong RecvOnly = 0x0004;
        public const ulong SendOnly = 0x0008;
        public const ulong NoInstall = 0x0010;
        public const ulong Fragments = 0x0020;
    }

    /// <summary>
    /// Mirrors WINDIVERT_ADDRESS (80 bytes total: 8 + 4 + 4 + 64-byte union).
    /// The bitfield word and the union are exposed as raw storage plus
    /// helper accessors, rather than trying to get C# to emulate C
    /// bitfields/unions directly - that indirection is exactly the kind
    /// of thing that silently breaks in P/Invoke, so we keep it explicit.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct WINDIVERT_ADDRESS
    {
        public long Timestamp;
        public uint Bits;      // Layer:8, Event:8, Sniffed:1, Outbound:1, Loopback:1, Impostor:1, IPv6:1, IPChecksum:1, TCPChecksum:1, UDPChecksum:1, Reserved1:8
        public uint Reserved2;
        public fixed byte Union[64];

        public byte Layer => (byte)(Bits & 0xFF);
        public byte Event => (byte)((Bits >> 8) & 0xFF);
        public bool Outbound => ((Bits >> 17) & 0x1) != 0;
        public bool Loopback => ((Bits >> 18) & 0x1) != 0;
        public bool Impostor => ((Bits >> 19) & 0x1) != 0;

        // --- WINDIVERT_DATA_FLOW accessors (union offset 0 = struct offset 16) ---
        // Layout: UINT64 EndpointId(0); UINT64 ParentEndpointId(8); UINT32 ProcessId(16);
        //         UINT32 LocalAddr[4](20); UINT32 RemoteAddr[4](36); UINT16 LocalPort(52);
        //         UINT16 RemotePort(54); UINT8 Protocol(56)   -- offsets relative to union start.
        public uint FlowProcessId
        {
            get { fixed (byte* p = Union) { return *(uint*)(p + 16); } }
        }

        // IMPORTANT, confirmed against WinDivert's own official flowtrack.c
        // sample (which prints addr->Flow.LocalPort directly with %u, no
        // conversion): this field is already in HOST byte order, unlike the
        // port fields inside an actual packet's TCP header (which genuinely
        // are network/big-endian, see NetOrder.ReadU16BE below). Do NOT
        // byte-swap this value - an earlier version of this file incorrectly
        // did, which silently made every tracked port number wrong and
        // meant nothing ever actually got redirected, despite tracking
        // appearing to work in the log.
        public ushort FlowLocalPortRaw
        {
            get { fixed (byte* p = Union) { return *(ushort*)(p + 52); } }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NatEntry
    {
        public uint DestAddr;      // value-form (a<<24|b<<16|c<<8|d), NOT raw memory bytes
        public ushort DestPort;    // host order
        public long LastSeenTicks;
    }

    public static class NetOrder
    {
        public static ushort Swap16(ushort v) => (ushort)((v << 8) | (v >> 8));

        public static ushort ReadU16BE(byte[] b, int offset) => (ushort)((b[offset] << 8) | b[offset + 1]);

        public static void WriteU16BE(byte[] b, int offset, ushort value)
        {
            b[offset] = (byte)(value >> 8);
            b[offset + 1] = (byte)(value & 0xFF);
        }

        public static uint ReadU32BE(byte[] b, int offset) =>
            ((uint)b[offset] << 24) | ((uint)b[offset + 1] << 16) | ((uint)b[offset + 2] << 8) | b[offset + 3];

        public static void WriteU32BE(byte[] b, int offset, uint value)
        {
            b[offset] = (byte)(value >> 24);
            b[offset + 1] = (byte)(value >> 16);
            b[offset + 2] = (byte)(value >> 8);
            b[offset + 3] = (byte)value;
        }

        public static byte[] ValueToBytes(uint value) => new byte[]
        {
            (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
        };
    }

    public static class WinDivertNative
    {
        public const long InvalidHandleValue = -1;

        [DllImport("WinDivert.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr WinDivertOpen(string filter, WinDivertLayer layer, short priority, ulong flags);

        [DllImport("WinDivert.dll", SetLastError = true)]
        public static extern bool WinDivertRecv(IntPtr handle, byte[] pPacket, uint packetLen, out uint pRecvLen, ref WINDIVERT_ADDRESS pAddr);

        // Overload for the Flow layer, which carries no packet payload.
        [DllImport("WinDivert.dll", SetLastError = true, EntryPoint = "WinDivertRecv")]
        public static extern bool WinDivertRecvNoPacket(IntPtr handle, IntPtr pPacket, uint packetLen, out uint pRecvLen, ref WINDIVERT_ADDRESS pAddr);

        [DllImport("WinDivert.dll", SetLastError = true)]
        public static extern bool WinDivertSend(IntPtr handle, byte[] pPacket, uint packetLen, out uint pSendLen, ref WINDIVERT_ADDRESS pAddr);

        [DllImport("WinDivert.dll", SetLastError = true)]
        public static extern bool WinDivertClose(IntPtr handle);

        [DllImport("WinDivert.dll", SetLastError = true)]
        public static extern bool WinDivertHelperCalcChecksums(byte[] pPacket, uint packetLen, ref WINDIVERT_ADDRESS pAddr, ulong flags);

        public static bool IsValidHandle(IntPtr h) => h != IntPtr.Zero && h.ToInt64() != InvalidHandleValue;
    }
}
