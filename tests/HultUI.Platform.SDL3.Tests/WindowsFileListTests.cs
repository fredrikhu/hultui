using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Xunit;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// The file list Windows applications pass each other (CF_HDROP) and its <c>text/uri-list</c>
/// form, which is how HultUI's clipboard hands Explorer's copied files to an app and takes an
/// app's back. The real clipboard is in <c>WindowsClipboardFileListTests</c> (UiTests); these
/// need no clipboard, so they run in every Windows suite.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileListTests
{
    private static readonly string[] Paths =
    [
        @"C:\Users\me\My Documents\report 2026.docx",
        @"C:\Users\me\Bilder\Smörgåsbord ünd 日本.png",
        @"C:\data\100% #1;a+b.txt",
        @"\\server\share\Team Folder\notes.txt",
        @"C:\Users\me\A folder\",
    ];

    private const string UriList =
        "file:///C:/Users/me/My%20Documents/report%202026.docx\r\n" +
        "file:///C:/Users/me/Bilder/Sm%C3%B6rg%C3%A5sbord%20%C3%BCnd%20%E6%97%A5%E6%9C%AC.png\r\n" +
        "file:///C:/data/100%25%20%231;a+b.txt\r\n" +
        "file://server/share/Team%20Folder/notes.txt\r\n" +
        "file:///C:/Users/me/A%20folder/\r\n";

    [WindowsFact("CF_HDROP is a Windows format")]
    public void Paths_BecomeOnePercentEncodedFileUriPerCrlfLine()
    {
        Assert.Equal(UriList, Encoding.UTF8.GetString(WindowsFileList.ToUriList(Paths)));
    }

    [WindowsFact("CF_HDROP is a Windows format")]
    public void AUriList_GivesBackThePaths()
    {
        Assert.Equal(Paths, WindowsFileList.FromUriList(Encoding.UTF8.GetBytes(UriList)));
    }

    [WindowsFact("CF_HDROP is a Windows format")]
    public void AUriList_KeepsOnlyWindowsFiles()
    {
        // LF line ends as well as CRLF; comments, other schemes and a Linux path left out.
        var list = "# copied from somewhere\n" +
                   "https://example.com/a.txt\n" +
                   "file:///home/me/a.txt\n" +
                   "\n" +
                   "file:///C:/a%20b.txt\n" +
                   "file://server/share/c.txt";

        Assert.Equal([@"C:\a b.txt", @"\\server\share\c.txt"],
            WindowsFileList.FromUriList(Encoding.UTF8.GetBytes(list)));
    }

    [WindowsFact("CF_HDROP is a Windows format")]
    public void ABlock_ReadsBackItsPaths()
    {
        nint hdrop = WindowsFileList.Allocate(Paths);
        Assert.NotEqual(0, hdrop);
        try
        {
            Assert.Equal(Paths, WindowsFileList.Read(hdrop));
        }
        finally { GlobalFree(hdrop); }
    }

    [WindowsFact("CF_HDROP is a Windows format")]
    public void NoPaths_IsNoBlock()
    {
        Assert.Equal(0, WindowsFileList.Allocate([]));
    }

    [WindowsFact("CF_HDROP is a Windows format")]
    public void AnAnsiBlock_ReadsBackItsPaths()
    {
        // fWide = 0: the names in the ANSI code page, as older applications write them.
        var names = Encoding.ASCII.GetBytes("C:\\old app\\a.txt\0\\\\server\\share\\b.txt\0\0");
        const int header = 20;
        nint hdrop = Marshal.AllocHGlobal(header + names.Length);
        try
        {
            Marshal.Copy(new byte[header], 0, hdrop, header);
            Marshal.WriteInt32(hdrop, 0, header);            // pFiles; fWide (offset 16) stays 0
            Marshal.Copy(names, 0, hdrop + header, names.Length);

            Assert.Equal([@"C:\old app\a.txt", @"\\server\share\b.txt"], WindowsFileList.Read(hdrop));
        }
        finally { Marshal.FreeHGlobal(hdrop); }
    }

    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint hMem);
}
