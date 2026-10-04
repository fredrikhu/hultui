using System.Text.Json.Serialization;
using HultUI.Core.Storage;
using Xunit;

namespace HultUI.Core.Tests.Storage;

public sealed class SampleState
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SampleState))]
internal partial class StorageTestJsonContext : JsonSerializerContext;

/// <summary>
/// <see cref="JsonFile{T}"/>: what an app's state file survives. The cases are the ones that cost
/// people their settings before — a truncated write read back as a first run, a hand edit gone
/// wrong, a crash between the swap's two renames.
/// </summary>
public class JsonFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("jsonfile-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private JsonFile<SampleState> File(string name = "state.json") =>
        new(_dir, name, StorageTestJsonContext.Default.SampleState);

    private string PathOf(string name) => System.IO.Path.Combine(_dir, name);

    [Fact]
    public void WhatIsSavedIsWhatLoads()
    {
        Assert.True(File().Save(new SampleState { Name = "first", Count = 3 }));

        var loaded = File().Load();

        Assert.Equal("first", loaded!.Name);
        Assert.Equal(3, loaded.Count);
    }

    [Fact]
    public void NothingStoredLoadsAsNull() => Assert.Null(File().Load());

    [Fact]
    public void TheFirstSaveCreatesTheFolder()
    {
        var nested = new JsonFile<SampleState>(System.IO.Path.Combine(_dir, "a", "b", "state.json"),
            StorageTestJsonContext.Default.SampleState);

        Assert.True(nested.Save(new SampleState { Name = "deep" }));
        Assert.Equal("deep", nested.Load()!.Name);
    }

    [Fact]
    public void AnOverwriteKeepsThePreviousContentsAsABackup()
    {
        File().Save(new SampleState { Name = "first" });
        File().Save(new SampleState { Name = "second" });

        Assert.Equal("second", File().Load()!.Name);
        Assert.Equal("first", File("state.json.bak").Load()!.Name);
    }

    [Fact]
    public void AnUnreadableFileIsReadFromTheBackup()
    {
        File().Save(new SampleState { Name = "first" });
        File().Save(new SampleState { Name = "second" });
        System.IO.File.WriteAllText(PathOf("state.json"), "{ this is not json");

        Assert.Equal("first", File().Load()!.Name);
    }

    /// <summary>The moment between the swap's two renames: the old file has become the backup
    /// and the new one is not in place yet. What is there is the backup, and it is the answer.</summary>
    [Fact]
    public void AMissingFileWithABackupLoadsTheBackup()
    {
        File().Save(new SampleState { Name = "first" });
        File().Save(new SampleState { Name = "second" });
        System.IO.File.Delete(PathOf("state.json"));

        Assert.Equal("first", File().Load()!.Name);
    }

    [Fact]
    public void NeitherReadableLoadsAsNull()
    {
        System.IO.File.WriteAllText(PathOf("state.json"), "nope");
        System.IO.File.WriteAllText(PathOf("state.json.bak"), "nor this");

        Assert.Null(File().Load());
    }

    [Fact]
    public void TheStagingFileDoesNotSurviveASave()
    {
        File().Save(new SampleState { Name = "first" });
        File().Save(new SampleState { Name = "second" });

        Assert.False(System.IO.File.Exists(PathOf("state.json.tmp")));
    }

    /// <summary>A write that cannot happen says so, leaves the file as it was, and leaves no
    /// half-written staging file behind to be mistaken for anything.</summary>
    [Fact]
    public void AFailedSaveLeavesTheFileAsItWas()
    {
        File().Save(new SampleState { Name = "kept" });
        using (new FileStream(PathOf("state.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(File().Save(new SampleState { Name = "lost" }));
        }

        Assert.Equal("kept", File().Load()!.Name);
        Assert.False(System.IO.File.Exists(PathOf("state.json.tmp")));
    }
}

/// <summary><see cref="AppFolders"/>: the folder is always a full path, whatever the environment
/// leaves out.</summary>
public class AppFoldersTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\" : "/";
    private static string Full(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    [Fact]
    public void ConfigPrefersXdgConfigHomeOnEveryPlatform() =>
        Assert.Equal(Full("xdg", "App"), AppFolders.ResolveConfig("App", Full("xdg"), Full("roaming"), Full("home")));

    [Fact]
    public void ConfigThenTheRoamingFolderThenDotConfig()
    {
        Assert.Equal(Full("roaming", "App"), AppFolders.ResolveConfig("App", null, Full("roaming"), Full("home")));
        Assert.Equal(Full("home", ".config", "App"), AppFolders.ResolveConfig("App", null, "", Full("home")));
    }

    /// <summary>The fresh-Linux-account bug: .NET answers "" (or a relative path) for a folder
    /// that does not exist yet, and "" combined with the app's name is a path relative to
    /// wherever the app was started.</summary>
    [Fact]
    public void ARelativeOrEmptyAnswerIsSkipped()
    {
        Assert.Equal(Full("home", ".config", "App"), AppFolders.ResolveConfig("App", "relative", "", Full("home")));
        var nowhere = AppFolders.ResolveConfig("App", null, null, null);
        Assert.True(System.IO.Path.IsPathFullyQualified(nowhere), nowhere);
    }

    [Fact]
    public void DataUsesXdgDataHomeOnlyOffWindows()
    {
        Assert.Equal(Full("xdg", "App"), AppFolders.ResolveData("App", Full("xdg"), Full("local"), Full("home"), isWindows: false));
        Assert.Equal(Full("local", "App"), AppFolders.ResolveData("App", Full("xdg"), Full("local"), Full("home"), isWindows: true));
        Assert.Equal(Full("home", ".local", "share", "App"), AppFolders.ResolveData("App", null, "", Full("home"), isWindows: false));
    }
}
