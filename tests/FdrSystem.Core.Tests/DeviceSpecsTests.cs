using FdrSystem.Core.Specs;
using Xunit;

namespace FdrSystem.Core.Tests;

public class DeviceSpecsTests
{
    [Fact]
    public void Get_ReturnsKnownIphoneSpec()
    {
        var spec = DeviceSpecs.Get("iPhone13,2");

        Assert.NotNull(spec);
        Assert.Equal("iPhone 12", spec!.Name);
        Assert.Equal("Apple A14 Bionic", spec.Chip);
        Assert.Equal(4, spec.RamGb);
        Assert.Equal("6,1″", spec.Screen);
        Assert.Equal(2020, spec.Year);
    }

    [Fact]
    public void Get_ReturnsKnownNewerIphoneSpec()
    {
        var spec = DeviceSpecs.Get("iPhone15,4");

        Assert.NotNull(spec);
        Assert.Equal("iPhone 15", spec!.Name);
        Assert.Equal("Apple A16 Bionic", spec.Chip);
        Assert.Equal(48, int.Parse(spec.Camera.Replace(" MP", "")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("iPhone99,9")]
    public void Get_ReturnsNullForUnknownOrEmpty(string? productType) =>
        Assert.Null(DeviceSpecs.Get(productType));
}
