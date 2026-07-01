using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class UpdateServiceTests
{
    [Fact]
    public void ReleaseFeedHealth_reports_missing_assets()
    {
        var health = new ReleaseFeedHealth(
            Ok: false,
            LatestVersion: "0.2.7",
            AppVersion: "0.2.7",
            HasFeed: false,
            HasFullPackage: true,
            Assets: ["AndroidEmulatorPlus-0.2.7-full.nupkg"],
            Missing: ["releases.win.json"],
            Summary: "Feed incomplete — missing: releases.win.json.");

        Assert.False(health.Ok);
        Assert.True(health.HasFullPackage);
        Assert.False(health.HasFeed);
        Assert.Single(health.Missing);
        Assert.Equal("releases.win.json", health.Missing[0]);
    }

    [Fact]
    public void ReleaseFeedHealth_reports_healthy_when_all_present()
    {
        var health = new ReleaseFeedHealth(
            Ok: true,
            LatestVersion: "0.2.7",
            AppVersion: "0.2.7",
            HasFeed: true,
            HasFullPackage: true,
            Assets: ["releases.win.json", "AndroidEmulatorPlus-0.2.7-full.nupkg"],
            Missing: [],
            Summary: "Feed healthy.");

        Assert.True(health.Ok);
        Assert.True(health.HasFeed);
        Assert.True(health.HasFullPackage);
        Assert.Empty(health.Missing);
    }

    [Fact]
    public void ReleaseFeedHealth_version_parity_tracked()
    {
        var current = new ReleaseFeedHealth(
            Ok: true, LatestVersion: "0.2.7", AppVersion: "0.2.7",
            HasFeed: true, HasFullPackage: true,
            Assets: [], Missing: [], Summary: "current");

        var behind = new ReleaseFeedHealth(
            Ok: true, LatestVersion: "0.3.0", AppVersion: "0.2.7",
            HasFeed: true, HasFullPackage: true,
            Assets: [], Missing: [], Summary: "update available");

        Assert.Equal(current.LatestVersion, current.AppVersion);
        Assert.NotEqual(behind.LatestVersion, behind.AppVersion);
    }
}
