using System.Globalization;

namespace FdrSystem.Core.Usb;

/// <summary>
/// Regras comuns aos dois sistemas: modo do aparelho, formato do UDID e
/// leitura dos identificadores que o USB expõe em recovery/DFU
/// (CPID/BDID/ECID/SRTG aparecem na string de série).
/// </summary>
public static class AppleUsb
{
    public const ushort AppleVendorId = 0x05AC;

    public static string ModeOf(ushort pid) => pid switch
    {
        0x1222 => "DFU",
        0x1227 or 0x1281 => "Recovery",
        _ => "Normal",
    };

    /// <summary>UDID em formato Apple (XXXXXXXX-XXXXXXXXXXXXXXXX).</summary>
    public static string? FormatUdid(string? serial) =>
        serial is { Length: 24 } && serial.All(Uri.IsHexDigit) ? serial[..8] + "-" + serial[8..] : serial;

    public static bool LooksLikeUdid(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var hex = value.Replace("-", string.Empty);
        return hex.Length == 24 && hex.All(Uri.IsHexDigit);
    }

    public sealed record RecoveryIds(
        string? EcidHex,
        string? EcidDec,
        string? Serial,
        string? Cpid,
        string? Bdid,
        string? Srtg);

    /// <summary>
    /// Extrai CPID/BDID/ECID/SRNM/SRTG de uma string que os contenha, seja o
    /// InstanceId do Windows (USB\VID_05AC&amp;PID_1227\CPID:8010 BDID:0D...) ou
    /// o ficheiro serial do sysfs no Linux.
    /// </summary>
    public static RecoveryIds ParseRecoveryIds(string? text)
    {
        text ??= string.Empty;

        var ecid = TakeHex(text, "ECID:");
        var cpid = TakeHex(text, "CPID:");
        var bdid = TakeHex(text, "BDID:");

        string? serial = null, srtg = null;

        var s = text.IndexOf("SRNM:[", StringComparison.OrdinalIgnoreCase);
        if (s >= 0)
        {
            var end = text.IndexOf(']', s + 6);
            if (end > s + 6) serial = text.Substring(s + 6, end - s - 6);
        }

        var g = text.IndexOf("SRTG:", StringComparison.OrdinalIgnoreCase);
        if (g >= 0)
        {
            var start = g + 5;
            if (start < text.Length && text[start] == '[') start++;
            var stop = start;
            while (stop < text.Length && text[stop] != '_' && text[stop] != ' ' && text[stop] != ']') stop++;
            if (stop > start) srtg = text[start..stop];
        }

        string? dec = null;
        if (ecid is not null)
        {
            try { dec = Convert.ToUInt64(ecid, 16).ToString(CultureInfo.InvariantCulture); }
            catch (FormatException) { }
            catch (OverflowException) { }
        }

        return new RecoveryIds(ecid, dec, serial, cpid, bdid, srtg);
    }

    private static string? TakeHex(string text, string marker)
    {
        var i = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var hex = new string(text.Skip(i + marker.Length).TakeWhile(Uri.IsHexDigit).ToArray());
        return hex.Length > 0 ? hex : null;
    }
}
