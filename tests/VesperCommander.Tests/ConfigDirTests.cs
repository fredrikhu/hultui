using System;
using System.IO;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// <see cref="ConfigDir"/> — where settings, session and bookmarks live.
///
/// <para>The bug this guards: on a fresh Linux account .NET answers "" for
/// <c>LocalApplicationData</c> (no <c>~/.local/share</c> yet), and the folder became the
/// relative <c>VesperCommander</c>, so settings went to whatever directory Vesper was started
/// in. The decision is tested through <see cref="ConfigDir.Resolve(string?, string?, string?, string?, bool)"/>
/// with the environment's values passed in, so the Linux cases run on Windows too. The paths
/// are built under the temp folder because a full path looks different on each OS.</para>
/// </summary>
public class ConfigDirTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "vesper-configdir");
    private static readonly string Xdg = Path.Combine(Root, "xdg-data");
    private static readonly string Local = Path.Combine(Root, "local-app-data");
    private static readonly string Home = Path.Combine(Root, "home");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_override_wins_over_everything(bool isWindows)
    {
        var over = Path.Combine(Root, "override");
        Assert.Equal(over, ConfigDir.Resolve(over, Xdg, Local, Home, isWindows));
    }

    [Fact]
    public void A_relative_override_is_made_absolute()
    {
        var path = ConfigDir.Resolve("vesper-relative", Xdg, Local, Home, isWindows: false);
        Assert.True(Path.IsPathFullyQualified(path), path);
        Assert.Equal(Path.GetFullPath("vesper-relative"), path);
    }

    [Fact]
    public void The_test_runs_own_override_reaches_Path()
        // AssemblyInfo's module initializer points every test at a scratch folder.
        => Assert.Equal(Path.Combine(Path.GetTempPath(), "vesper-tests-config"), ConfigDir.Path);

    [Fact]
    public void XDG_DATA_HOME_is_honoured_off_Windows()
        => Assert.Equal(Path.Combine(Xdg, "VesperCommander"), ConfigDir.Resolve(null, Xdg, Local, Home, isWindows: false));

    [Fact]
    public void XDG_DATA_HOME_is_ignored_on_Windows()
        => Assert.Equal(Path.Combine(Local, "VesperCommander"), ConfigDir.Resolve(null, Xdg, Local, Home, isWindows: true));

    [Fact]
    public void A_relative_XDG_DATA_HOME_is_ignored()
        // The XDG spec: a relative path in these variables is invalid and must be ignored.
        => Assert.Equal(Path.Combine(Local, "VesperCommander"), ConfigDir.Resolve(null, "relative/xdg", Local, Home, isWindows: false));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_empty_special_folder_falls_back_to_local_share_under_home(bool isWindows)
        => Assert.Equal(Path.Combine(Home, ".local", "share", "VesperCommander"),
                        ConfigDir.Resolve(null, null, "", Home, isWindows));

    [Fact]
    public void A_relative_special_folder_is_skipped_like_an_empty_one()
        => Assert.Equal(Path.Combine(Home, ".local", "share", "VesperCommander"),
                        ConfigDir.Resolve(null, null, "local-app-data", Home, isWindows: false));

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData("rel/xdg", "rel/local", "rel/home")]
    [InlineData(null, "", "rel/home")]
    [InlineData("rel/xdg", null, null)]
    public void The_result_is_always_a_full_path(string? xdg, string? local, string? home)
    {
        foreach (var isWindows in new[] { false, true })
        {
            var path = ConfigDir.Resolve(null, xdg, local, home, isWindows);
            Assert.True(Path.IsPathFullyQualified(path), path);
            Assert.Equal("VesperCommander", Path.GetFileName(path));
        }
    }

    [Fact]
    public void The_real_default_is_a_full_path_and_unchanged_on_Windows()
    {
        Assert.True(Path.IsPathFullyQualified(ConfigDir.Default), ConfigDir.Default);
        if (OperatingSystem.IsWindows())
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VesperCommander"),
                         ConfigDir.Default);
    }
}
