using FdrSystem.Core.Firmware;
using Xunit;

namespace FdrSystem.Core.Tests;

public class DownloadFormatTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1048576L, "1,0 MB")]
    [InlineData(1572864L, "1,5 MB")]
    [InlineData(1073741824L, "1,00 GB")]
    public void Fmt_FormatsBytes(long bytes, string expected) =>
        Assert.Equal(expected, DownloadItem.Fmt(bytes));

    [Theory]
    [InlineData(0, "a medir…")]
    [InlineData(512, "512 B/s")]
    [InlineData(2048, "2 KB/s")]
    [InlineData(1048576, "1,0 MB/s")]
    public void FmtSpeed_FormatsThroughput(double bps, string expected) =>
        Assert.Equal(expected, DownloadItem.FmtSpeed(bps));

    [Fact]
    public void DownloadItem_ExposesPartialAndMetaPaths()
    {
        var item = new DownloadItem { FilePath = "/tmp/iPhone.ipsw" };

        Assert.Equal("/tmp/iPhone.ipsw.download", item.PartialPath);
        Assert.Equal("/tmp/iPhone.ipsw.download.meta", item.MetaPath);
    }

    [Fact]
    public void DownloadItem_ProgressUsesTotalAsMaximum()
    {
        var item = new DownloadItem { Total = 1000, Done = 250 };

        Assert.Equal(1000, item.ProgressMax);
        Assert.Equal(250, item.ProgressValue);
    }

    [Fact]
    public void DownloadItem_ProgressNeverExceedsTotal()
    {
        var item = new DownloadItem { Total = 1000, Done = 1500 };

        Assert.Equal(1000, item.ProgressValue);
    }

    [Theory]
    [InlineData(DownloadState.Queued, "Pausar")]
    [InlineData(DownloadState.Downloading, "Pausar")]
    [InlineData(DownloadState.Paused, "Retomar")]
    [InlineData(DownloadState.Error, "Retomar")]
    [InlineData(DownloadState.Verifying, "A verificar…")]
    public void DownloadItem_PauseLabelFollowsState(DownloadState state, string expected) =>
        Assert.Equal(expected, new DownloadItem { State = state }.PauseLabel);
}
