namespace ClickyBot;

internal interface IFakerInputTransport : IDisposable
{
    void Write(byte[] report);
}

internal sealed class FakerInputKeyboard
{
    internal static FakerInputKeyboard Shared { get; set; } = new(() => new FakerInputTransport());
    private readonly object _sync = new();
    private readonly Func<IFakerInputTransport> _connect;
    private IFakerInputTransport? _transport;
    private HashSet<ushort> _held = [];
    private byte _mouseButtons;

    internal FakerInputKeyboard(Func<IFakerInputTransport> connect) => _connect = connect;

    internal void CheckAvailable()
    {
        lock (_sync) _transport ??= _connect();
    }

    internal void SendKey(ushort key, bool down)
    {
        lock (_sync)
        {
            _ = HidKey(key); // Reject unsupported keys before opening or writing.
            if (_held.Contains(key) == down) return;
            var next = new HashSet<ushort>(_held);
            if (down) next.Add(key); else next.Remove(key);
            Write(BuildReport(next));
            _held = next;
        }
    }

    internal bool ReleaseAllHeldInputs()
    {
        lock (_sync)
        {
            try
            {
                if (_held.Count > 0) { Write(BuildReport([])); _held.Clear(); }
                if (_mouseButtons != 0) { Write(BuildMouseReport(0, 0, 0)); _mouseButtons = 0; }
                _transport?.Dispose();
                _transport = null;
                return true;
            }
            catch (InvalidOperationException)
            {
                // Retain held-key state so a later stop/release can retry.
                return false;
            }
        }
    }

    internal void SendMouseButton(MouseButtonType button, bool down)
    {
        lock (_sync)
        {
            var bit = button switch { MouseButtonType.Left => 1, MouseButtonType.Right => 2, MouseButtonType.Middle => 4, _ => throw new InvalidOperationException("Unsupported mouse button.") };
            var next = (byte)(down ? _mouseButtons | bit : _mouseButtons & ~bit);
            if (next == _mouseButtons) return;
            Write(BuildMouseReport(next, 0, 0));
            _mouseButtons = next;
        }
    }

    internal void MoveMouseRelative(int x, int y)
    {
        lock (_sync) Write(BuildMouseReport(_mouseButtons, x, y));
    }

    internal static byte[] BuildMouseReport(byte buttons, int x, int y)
    {
        if (buttons > 31 || x is < -32767 or > 32767 || y is < -32767 or > 32767)
            throw new InvalidOperationException("FakerInput relative mouse report is out of range.");
        // Official API v1 relative mouse structure: ID, buttons, signed X/Y,
        // vertical/horizontal wheels. This is separate from keyboard state.
        var report = new byte[65];
        report[0] = 0x40; report[1] = 8; report[2] = 3; report[3] = buttons;
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(4, 2), (short)x);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(6, 2), (short)y);
        return report;
    }

    private void Write(byte[] report)
    {
        try { (_transport ??= _connect()).Write(report); }
        catch
        {
            _transport?.Dispose();
            _transport = null;
            throw;
        }
    }

    internal static byte[] BuildReport(IEnumerable<ushort> keys)
    {
        // FakerInput API v1: 65-byte control report, followed by a 9-byte
        // boot-keyboard report. See FAKERINPUT-NOTICE.txt for attribution.
        var report = new byte[65];
        report[0] = 0x40; report[1] = 9; report[2] = 1;
        var usages = new SortedSet<byte>();
        foreach (var key in keys)
        {
            var (usage, modifier) = HidKey(key);
            report[3] |= modifier;
            if (usage != 0) usages.Add(usage);
        }
        if (usages.Count > 6) throw new InvalidOperationException("FakerInput supports up to six held non-modifier keys.");
        var index = 5;
        foreach (var usage in usages) report[index++] = usage;
        return report;
    }

    private static (byte Usage, byte Modifier) HidKey(ushort key)
    {
        if (key is >= 0x41 and <= 0x5A) return ((byte)(key - 0x41 + 4), 0);
        if (key is >= 0x31 and <= 0x39) return ((byte)(key - 0x31 + 0x1E), 0);
        if (key is >= 0x70 and <= 0x7B) return ((byte)(key - 0x70 + 0x3A), 0);
        if (key is >= 0x61 and <= 0x69) return ((byte)(key - 0x61 + 0x59), 0);
        return key switch
        {
            0x10 or 0xA0 => (0, 2), 0xA1 => (0, 32),
            0x11 or 0xA2 => (0, 1), 0xA3 => (0, 16),
            0x12 or 0xA4 => (0, 4), 0xA5 => (0, 64),
            0x5B => (0, 8), 0x5C => (0, 128),
            0x30 => (0x27, 0), 0x0D => (0x28, 0), 0x1B => (0x29, 0),
            0x08 => (0x2A, 0), 0x09 => (0x2B, 0), 0x20 => (0x2C, 0),
            0xBD => (0x2D, 0), 0xBB => (0x2E, 0), 0xDB => (0x2F, 0),
            0xDD => (0x30, 0), 0xDC => (0x31, 0), 0xBA => (0x33, 0),
            0xDE => (0x34, 0), 0xC0 => (0x35, 0), 0xBC => (0x36, 0),
            0xBE => (0x37, 0), 0xBF => (0x38, 0), 0x14 => (0x39, 0),
            0x2C => (0x46, 0), 0x91 => (0x47, 0), 0x13 => (0x48, 0),
            0x2D => (0x49, 0), 0x24 => (0x4A, 0), 0x21 => (0x4B, 0),
            0x2E => (0x4C, 0), 0x23 => (0x4D, 0), 0x22 => (0x4E, 0),
            0x27 => (0x4F, 0), 0x25 => (0x50, 0), 0x28 => (0x51, 0), 0x26 => (0x52, 0),
            0x90 => (0x53, 0), 0x6F => (0x54, 0), 0x6A => (0x55, 0),
            0x6D => (0x56, 0), 0x6B => (0x57, 0), 0x60 => (0x62, 0),
            0x6E => (0x63, 0), 0xE2 => (0x64, 0), 0x5D => (0x65, 0),
            _ => throw new InvalidOperationException($"FakerInput does not support keyboard key 0x{key:X2}.")
        };
    }
}
