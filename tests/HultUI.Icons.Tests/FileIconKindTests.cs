using HultUI.Icons;
using HultUI.Syntax;
using Xunit;

namespace HultUI.Icons.Tests;

/// <summary>
/// A file list asks <see cref="FileIcons.KindFor"/> for every row it shows. The first code file
/// used to build TextMate's registry on the UI thread to answer it — 4.6–5.3 MB and 7–10 ms in
/// one frame of Vesper's first code folder (plan 17).
/// </summary>
public class FileIconKindTests
{
    [Fact]
    public void AskingForAFileIcon_DoesNotLoadTheSyntaxRegistry()
    {
        int before = SyntaxHighlighter.RegistryUsesOnThisThread;

        Assert.Equal(FileIconKind.Code, FileIcons.KindFor("Program.cs"));
        Assert.Equal(FileIconKind.Code, FileIcons.KindFor("SETTINGS.JSON"));
        Assert.Equal(FileIconKind.Code, FileIcons.KindFor("Dockerfile"));
        Assert.Equal(FileIconKind.File, FileIcons.KindFor("notes.txt"));
        Assert.Equal(FileIconKind.File, FileIcons.KindFor("Cargo.toml"));
        Assert.Equal(FileIconKind.Image, FileIcons.KindFor("photo.png"));
        Assert.Equal(FileIconKind.Project, FileIcons.KindFor("App.csproj"));
        Assert.Equal(FileIconKind.Solution, FileIcons.KindFor("App.slnx"));
        Assert.Equal(FileIconKind.Folder, FileIcons.KindFor("src", isDir: true));

        Assert.Equal(before, SyntaxHighlighter.RegistryUsesOnThisThread);
    }
}
