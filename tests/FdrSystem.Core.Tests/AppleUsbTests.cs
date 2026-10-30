using FdrSystem.Core.Usb;
using Xunit;

namespace FdrSystem.Core.Tests;

public class AppleUsbTests
{
    [Theory]
    [InlineData(0x1222, "DFU")]
    [InlineData(0x1227, "Recovery")]
    [InlineData(0x1281, "Recovery")]
    [InlineData(0x1234, "Normal")]
    [InlineData(0x0000, "Normal")]
    public void ModeOf_MapsAppleProductIds(ushort pid, string expected) =>
        Assert.Equal(expected, AppleUsb.ModeOf(pid));

    [Fact]
    public void FormatUdid_InsertsHyphenAfterEightHexDigits() =>
        Assert.Equal("aabbccdd-eeff001122334455", AppleUsb.FormatUdid("aabbccddeeff001122334455"));

    [Fact]
    public void FormatUdid_LeavesOtherValuesUntouched()
    {
        Assert.Null(AppleUsb.FormatUdid(null));
        Assert.Equal("ABC", AppleUsb.FormatUdid("ABC"));
        Assert.Equal("nao-hexadecimal-12345678", AppleUsb.FormatUdid("nao-hexadecimal-12345678"));
    }

    [Theory]
    [InlineData("aabbccddeeff001122334455", true)]
    [InlineData("aabbccdd-eeff001122334455", true)]
    [InlineData("AABBCCDDEEFF001122334455", true)]
    [InlineData("aabbccdd-eeff00112233445", false)]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzz", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeUdid_Validates24HexDigits(string? value, bool expected) =>
        Assert.Equal(expected, AppleUsb.LooksLikeUdid(value));

    [Fact]
    public void ParseRecoveryIds_ExtractsAllFields()
    {
        const string text =
            "CPID:8101 BDID:0C ECID:1A2B3C4D SRTG:[iBoot-11881.0.194] SRNM:[F17G12345678]";

        var ids = AppleUsb.ParseRecoveryIds(text);

        Assert.Equal("8101", ids.Cpid);
        Assert.Equal("0C", ids.Bdid);
        Assert.Equal("1A2B3C4D", ids.EcidHex);
        Assert.Equal(439041101UL.ToString(), ids.EcidDec);
        Assert.Equal("iBoot-11881.0.194", ids.Srtg);
        Assert.Equal("F17G12345678", ids.Serial);
    }

    [Fact]
    public void ParseRecoveryIds_AcceptsWindowsInstanceIdFormat()
    {
        const string text = "USB\\VID_05AC&PID_1227\\CPID:8010 BDID:08 ECID:1234ABCD0001 SRTG:[iBoot-1.0]";

        var ids = AppleUsb.ParseRecoveryIds(text);

        Assert.Equal("8010", ids.Cpid);
        Assert.Equal("08", ids.Bdid);
        Assert.Equal("1234ABCD0001", ids.EcidHex);
        Assert.Equal("iBoot-1.0", ids.Srtg);
        Assert.Null(ids.Serial);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sem identificadores da Apple")]
    public void ParseRecoveryIds_ReturnsEmptyWhenMarkersAreMissing(string? text)
    {
        var ids = AppleUsb.ParseRecoveryIds(text);

        Assert.Null(ids.EcidHex);
        Assert.Null(ids.EcidDec);
        Assert.Null(ids.Cpid);
        Assert.Null(ids.Bdid);
        Assert.Null(ids.Serial);
        Assert.Null(ids.Srtg);
    }
}
