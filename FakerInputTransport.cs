using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ClickyBot;

internal sealed class FakerInputTransport : IFakerInputTransport
{
    private SafeFileHandle? _control;

    internal FakerInputTransport()
    {
        var apiChecked = false;
        try
        {
            foreach (var path in DevicePaths())
            {
                var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                var keep = false;
                try
                {
                    if (handle.IsInvalid) continue;
                    var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
                    if (!HidD_GetAttributes(handle, ref attributes) || attributes.Vendor != 0xFE0F || attributes.Product != 0x00FF) continue;
                    if (!HidD_GetPreparsedData(handle, out var data)) continue;
                    HidCaps caps;
                    try { if (HidP_GetCaps(data, out caps) != 0x00110000) continue; }
                    finally { HidD_FreePreparsedData(data); }
                    if (caps.UsagePage != 0xFF00) continue;
                    if (caps.Usage == 1 && caps.OutputLength == 65 && _control is null)
                    {
                        _control = handle;
                        keep = true;
                    }
                    else if (caps.Usage == 2 && caps.FeatureLength == 65 && caps.OutputLength == 65)
                    {
                        var feature = new byte[65]; feature[0] = 0x42;
                        if (!HidD_GetFeature(handle, feature, feature.Length)) throw DriverError("read the driver API version");
                        if (BitConverter.ToUInt32(feature, 4) != 1) throw new InvalidOperationException("The installed FakerInput driver uses an unsupported API version.");
                        var check = new byte[65]; check[0] = 0x41; check[4] = 1;
                        if (!HidD_SetOutputReport(handle, check, check.Length)) throw DriverError("verify the driver API");
                        apiChecked = true;
                    }
                }
                finally { if (!keep) handle.Dispose(); }
                if (_control is not null && apiChecked) break;
            }
            if (_control is null || !apiChecked)
                throw new InvalidOperationException("FakerInput keyboard input requires the installed FakerInput v1 driver. Its keyboard control interface was not found. No software-input fallback was sent.");
        }
        catch { Dispose(); throw; }
    }

    public void Write(byte[] report)
    {
        if (_control is null || _control.IsInvalid || _control.IsClosed) throw new InvalidOperationException("The FakerInput driver connection is closed.");
        if (report.Length != 65) throw new InvalidOperationException("Invalid FakerInput keyboard report length.");
        if (!HidD_SetOutputReport(_control, report, report.Length)) throw DriverError("send a keyboard report");
    }

    public void Dispose() { _control?.Dispose(); _control = null; }

    private static InvalidOperationException DriverError(string action)
        => new($"FakerInput could not {action} (Windows error {Marshal.GetLastWin32Error()}). No software-input fallback was sent.");

    private static IEnumerable<string> DevicePaths()
    {
        HidD_GetHidGuid(out var guid);
        var devices = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (devices == new IntPtr(-1)) throw DriverError("enumerate HID devices");
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new DeviceInterface { Size = Marshal.SizeOf<DeviceInterface>() };
                if (!SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref guid, index, ref info))
                {
                    if (Marshal.GetLastWin32Error() == 259) yield break;
                    throw DriverError("enumerate a HID interface");
                }
                SetupDiGetDeviceInterfaceDetail(devices, ref info, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                if (size < 6 || size > 65536) throw new InvalidOperationException("Invalid HID device path size.");
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(devices, ref info, detail, size, out _, IntPtr.Zero)) throw DriverError("read a HID device path");
                    var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (!string.IsNullOrWhiteSpace(path)) yield return path;
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devices); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterface { public int Size; public Guid Class; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct HidAttributes { public int Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] private struct HidCaps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort Links, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int length);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetOutputReport(SafeFileHandle handle, byte[] report, int length);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr window, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr devices, IntPtr data, ref Guid guid, uint index, ref DeviceInterface info);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr devices, ref DeviceInterface info, IntPtr detail, uint size, out uint required, IntPtr data);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr devices);
}
