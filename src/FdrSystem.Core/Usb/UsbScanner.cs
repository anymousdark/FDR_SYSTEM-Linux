using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace FdrSystem.Core.Usb;

/// <summary>
/// Lista os dispositivos Apple ligados por USB.
/// Linux: lê o sysfs (/sys/bus/usb/devices) — não precisa de drivers nem de
/// permissões especiais para ver o aparelho, só para o usbmuxd o abrir.
/// Windows: mantém o SetupAPI da versão original, para o port poder ser
/// desenvolvido e comparado nesta máquina.
/// </summary>
public static class UsbScanner
{
    public static IReadOnlyList<UsbDevice> FindAppleDevices() =>
        OperatingSystem.IsWindows() ? WindowsPnp.FindAppleDevices() : LinuxSysfs.FindAppleDevices();

    /// <summary>Diagnóstico legível para a página Driver.</summary>
    public static string DescribeEnvironment()
    {
        if (OperatingSystem.IsWindows()) return "Windows — SetupAPI (PnP)";

        var usbmuxd = File.Exists("/var/run/usbmuxd") || File.Exists("/run/usbmuxd");
        return usbmuxd
            ? "Linux — sysfs USB + usbmuxd ativo"
            : "Linux — sysfs USB (usbmuxd não está a correr: instale/ative usbmuxd)";
    }

    private static class LinuxSysfs
    {
        private const string Root = "/sys/bus/usb/devices";

        public static IReadOnlyList<UsbDevice> FindAppleDevices()
        {
            var found = new List<UsbDevice>();
            if (!Directory.Exists(Root)) return found;

            foreach (var dir in Directory.EnumerateDirectories(Root))
            {
                var name = Path.GetFileName(dir);
                if (name.Contains(':')) continue; // interfaces (1-1:1.0)

                var vendor = ReadHex(Path.Combine(dir, "idVendor"));
                if (vendor != AppleUsb.AppleVendorId) continue;

                var product = ReadHex(Path.Combine(dir, "idProduct"));
                found.Add(new UsbDevice(
                    dir,
                    vendor,
                    product,
                    ReadText(Path.Combine(dir, "serial")),
                    ReadText(Path.Combine(dir, "product")),
                    ReadText(Path.Combine(dir, "manufacturer"))));
            }

            return found;
        }

        private static string? ReadText(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var text = File.ReadAllText(path).Trim();
                return text.Length == 0 ? null : text;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static ushort ReadHex(string path)
        {
            var text = ReadText(path);
            return text is not null && ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
                ? value
                : (ushort)0;
        }
    }

    private static class WindowsPnp
    {
        private const int DIGCF_PRESENT = 0x02;
        private const int DIGCF_ALLCLASSES = 0x04;
        private const uint SPDRP_DEVICEDESC = 0x00;
        private const uint SPDRP_FRIENDLYNAME = 0x0C;
        private static readonly IntPtr InvalidHandleValue = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType, byte[]? propertyBuffer, uint propertyBufferSize, out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        public static IReadOnlyList<UsbDevice> FindAppleDevices()
        {
            var found = new List<UsbDevice>();
            var set = SetupDiGetClassDevsW(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == IntPtr.Zero || set == InvalidHandleValue) return found;

            try
            {
                var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                for (uint index = 0; SetupDiEnumDeviceInfo(set, index, ref info); index++)
                {
                    var instanceId = GetInstanceId(set, ref info);
                    if (instanceId is null || !instanceId.StartsWith(@"USB\VID_05AC", StringComparison.OrdinalIgnoreCase)) continue;
                    if (instanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) continue;

                    var parts = instanceId.Split('\\');
                    var serial = parts.Length >= 3 ? parts[2] : null;
                    var pid = ParseProductId(instanceId);

                    found.Add(new UsbDevice(
                        instanceId,
                        AppleUsb.AppleVendorId,
                        pid,
                        serial,
                        GetFriendlyName(set, ref info),
                        null));
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }

            return found;
        }

        private static string? GetInstanceId(IntPtr set, ref SP_DEVINFO_DATA info)
        {
            var buffer = new StringBuilder(512);
            if (!SetupDiGetDeviceInstanceIdW(set, ref info, buffer, buffer.Capacity, out _) || buffer.Length == 0) return null;
            return buffer.ToString();
        }

        private static ushort ParseProductId(string instanceId)
        {
            var marker = instanceId.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
            if (marker < 0 || marker + 8 > instanceId.Length) return 0;
            return ushort.TryParse(instanceId.AsSpan(marker + 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pid)
                ? pid
                : (ushort)0;
        }

        private static string? GetFriendlyName(IntPtr set, ref SP_DEVINFO_DATA info) =>
            ReadStringProperty(set, ref info, SPDRP_FRIENDLYNAME) ?? ReadStringProperty(set, ref info, SPDRP_DEVICEDESC);

        private static string? ReadStringProperty(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
        {
            var buffer = new byte[1024];
            if (!SetupDiGetDeviceRegistryPropertyW(set, ref info, property, out _, buffer, (uint)buffer.Length, out var size) || size < 2) return null;
            var text = Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0').Trim();
            return text.Length == 0 ? null : text;
        }
    }
}
