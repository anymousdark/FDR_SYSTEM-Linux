using FdrSystem.Core.Setup;
using Xunit;

namespace FdrSystem.Core.Tests;

public class SetupEnvironmentTests
{
    [Theory]
    [InlineData("ayhcer", "ayhcer")]
    [InlineData("user-name_1.2", "user-name_1.2")]
    [InlineData("_svc", "_svc")]
    [InlineData("  root  ", "root")]
    public void SafeUser_AcceptsPosixNames(string input, string expected) =>
        Assert.Equal(expected, SetupEnvironment.SafeUser(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1abc")]
    [InlineData(".hidden")]
    [InlineData("has space")]
    [InlineData("a/b")]
    [InlineData("user;rm")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void SafeUser_RejectsUnsafeOrInvalidNames(string? input) =>
        Assert.Null(SetupEnvironment.SafeUser(input));
}
