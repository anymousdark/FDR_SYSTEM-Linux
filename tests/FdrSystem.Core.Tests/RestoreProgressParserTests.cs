using FdrSystem.Core.Firmware;
using Xunit;

namespace FdrSystem.Core.Tests;

public class RestoreProgressParserTests
{
    [Fact]
    public void NewParser_StartsIdle()
    {
        var parser = new RestoreProgressParser();

        Assert.Equal(0, parser.PrepPercent);
        Assert.Equal(0, parser.InstallPercent);
        Assert.Equal("A iniciar o motor de restauro", parser.Phase);
        Assert.False(parser.HadError);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t ")]
    public void Apply_IgnoresEmptyLines(string? line) =>
        Assert.Null(new RestoreProgressParser().Apply(line));

    [Fact]
    public void Apply_Step0_SearchesForTheDevice()
    {
        var update = new RestoreProgressParser().Apply("progress: 0 0")!;

        Assert.Equal("A procurar o iPhone", update.Phase);
        Assert.Contains("Ligue o iPhone", update.Detail);
        Assert.Equal(0, update.PrepPercent);
        Assert.Equal(0, update.InstallPercent);
    }

    [Fact]
    public void Apply_Step1_FeedsPreparationBar()
    {
        var parser = new RestoreProgressParser();
        var update = parser.Apply("progress: 1 0.5")!;

        Assert.Equal(50, update.PrepPercent);
        Assert.Equal("A preparar o firmware", update.Phase);
        Assert.Contains("extrair", update.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_Step2_FeedsInstallBar()
    {
        var update = new RestoreProgressParser().Apply("progress: 2 0.25")!;

        Assert.Equal(25, update.InstallPercent);
        Assert.Equal("A gravar no iPhone", update.Phase);
    }

    [Theory]
    [InlineData(3, "A verificar")]
    [InlineData(4, "A gravar o firmware")]
    [InlineData(5, "A gravar o modem")]
    [InlineData(6, "A finalizar")]
    [InlineData(7, "A terminar")]
    public void Apply_Steps3To7_FeedInstallBar(int step, string phase)
    {
        var update = new RestoreProgressParser().Apply($"progress: {step} 0.10")!;

        Assert.Equal(10, update.InstallPercent);
        Assert.Equal(phase, update.Phase);
    }

    [Fact]
    public void Apply_NeverLetsProgressGoBackwards()
    {
        var parser = new RestoreProgressParser();

        parser.Apply("progress: 2 0.80");
        var update = parser.Apply("progress: 2 0.20")!;

        Assert.Equal(80, update.InstallPercent);
    }

    [Theory]
    [InlineData("progress: 1 2.0", 100)]
    [InlineData("progress: 1 1.5", 100)]
    [InlineData("progress: 1 0.0", 0)]
    public void Apply_ClampsFractionsTo0To100(string line, double expected)
    {
        var update = new RestoreProgressParser().Apply(line)!;

        Assert.Equal(expected, update.PrepPercent);
    }

    [Fact]
    public void Apply_PreparationFinishedAtStep1_IsReported()
    {
        var update = new RestoreProgressParser().Apply("progress: 1 1")!;

        Assert.Equal("Extração concluída.", update.Detail);
    }

    [Theory]
    [InlineData("IPSW: 100", 100, 0)]
    [InlineData("SHA1: 0.5", 50, 0)]
    [InlineData("Restoring image: 42.5", 0, 42.5)]
    [InlineData("Verifying restore: 0.5", 0, 50)]
    [InlineData("Flashing firmware: 12", 0, 12)]
    [InlineData("Updating baseband: 7", 0, 7)]
    public void Apply_LabelsFeedTheCorrectBar(string line, double prep, double install)
    {
        var update = new RestoreProgressParser().Apply(line)!;

        Assert.Equal(prep, update.PrepPercent);
        Assert.Equal(install, update.InstallPercent);
    }

    [Theory]
    [InlineData("Creating partition map: 1", "A preparar o armazenamento")]
    [InlineData("Creating filesystem: 1", "A preparar o armazenamento")]
    [InlineData("Checking filesystems: 1", "A preparar o armazenamento")]
    [InlineData("Mounting filesystems: 1", "A preparar o armazenamento")]
    [InlineData("Fixing up /var: 1", "A preparar o armazenamento")]
    [InlineData("Rebooting device: 1", "A reiniciar")]
    public void Apply_StorageAndRebootLabels_UpdateOnlyThePhase(string line, string phase)
    {
        var update = new RestoreProgressParser().Apply(line)!;

        Assert.Equal(phase, update.Phase);
        Assert.Equal(0, update.PrepPercent);
        Assert.Equal(0, update.InstallPercent);
    }

    [Fact]
    public void Apply_ErrorLine_MarksFailure()
    {
        var parser = new RestoreProgressParser();
        var update = parser.Apply("ERROR: Unable to connect to device")!;

        Assert.True(parser.HadError);
        Assert.Contains("Unable to connect", update.Detail);
    }

    [Fact]
    public void Apply_UnknownShortLine_BecomesDetail()
    {
        var update = new RestoreProgressParser().Apply("Waiting for the device to enter recovery mode.")!;

        Assert.Equal("Waiting for the device to enter recovery mode.", update.Detail);
    }

    [Fact]
    public void Apply_KeepsRawLineAndTrimsTrailingWhitespace()
    {
        var update = new RestoreProgressParser().Apply("progress: 1 0.1   ")!;

        Assert.Equal("progress: 1 0.1", update.Line);
    }
}
