using FdrSystem.Core.Devices;
using Xunit;

namespace FdrSystem.Core.Tests;

public class IrecoveryDevicesTests
{
    [Theory]
    [InlineData("8101", "0C", "iPhone13,2", "iPhone 12")]
    [InlineData("8020", "0E", "iPhone11,2", "iPhone XS")]
    [InlineData("8030", "04", "iPhone12,1", "iPhone 11")]
    [InlineData("8140", "08", "iPhone17,3", "iPhone 16")]
    public void TryResolve_MapsCpidAndBdid(string cpid, string bdid, string productType, string name)
    {
        var ok = IrecoveryDevices.TryResolve(cpid, bdid, out var resolvedType, out var resolvedName);

        Assert.True(ok);
        Assert.Equal(productType, resolvedType);
        Assert.Equal(name, resolvedName);
    }

    [Theory]
    [InlineData(null, "0C")]
    [InlineData("8101", null)]
    [InlineData("", "")]
    [InlineData("ZZZZ", "0C")]
    [InlineData("FFFF", "FF")]
    public void TryResolve_ReturnsFalseForUnknownOrInvalid(string? cpid, string? bdid)
    {
        var ok = IrecoveryDevices.TryResolve(cpid, bdid, out var productType, out var name);

        Assert.False(ok);
        Assert.Equal("", productType);
        Assert.Equal("", name);
    }
}
