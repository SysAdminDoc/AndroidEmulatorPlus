using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class SnapshotServiceTests
{
    [Theory]
    [InlineData("default_boot", true)]
    [InlineData("my_snapshot", true)]
    [InlineData("snap-2026-07-01", true)]
    [InlineData("Snap With Spaces", true)]
    [InlineData("snap.v2", true)]
    [InlineData("snap_1", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("snap/../../etc", false)]
    [InlineData("snap;rm -rf", false)]
    [InlineData("snap\ntrick", false)]
    public void IsSafeSnapshotName_validates_names(string name, bool expected)
    {
        Assert.Equal(expected, SnapshotService.IsSafeSnapshotName(name));
    }

    [Fact]
    public void IsSafeSnapshotName_rejects_overly_long_names()
    {
        var longName = new string('a', 129);
        Assert.False(SnapshotService.IsSafeSnapshotName(longName));
    }

    [Fact]
    public void IsSafeSnapshotName_accepts_max_length_name()
    {
        var maxName = new string('a', 128);
        Assert.True(SnapshotService.IsSafeSnapshotName(maxName));
    }

    [Fact]
    public void Snapshot_DefaultBoot_identified_correctly()
    {
        var boot = new Snapshot("default_boot", "/fake", 1024, System.DateTime.Now);
        var other = new Snapshot("my_snap", "/fake", 512, System.DateTime.Now);

        Assert.True(boot.IsDefaultBoot);
        Assert.False(other.IsDefaultBoot);
    }
}
