using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using HultUI.Core.Platform;
using HultUI.Platform.SDL3;
using Xunit;
using WinFormsClipboard = System.Windows.Forms.Clipboard;

namespace HultUI.UiTests;

/// <summary>
/// Files on the real Windows clipboard: a <c>text/uri-list</c> copied through HultUI's clipboard
/// is the CF_HDROP list Explorer pastes, and files Explorer copies read back as one. WinForms'
/// clipboard stands in for Explorer at the other end: it writes and reads CF_HDROP through OLE,
/// as the shell does, and shares no code with <see cref="WindowsClipboard"/>.
/// </summary>
/// <remarks>
/// These replace what is on the desktop's clipboard, so they run with the opt-in real-window
/// suite and put back any text they found.
/// </remarks>
[Collection("ui")]
public sealed partial class WindowsClipboardFileListTests
{
    private static readonly string[] Paths =
    [
        @"C:\Users\me\My Documents\report 2026.docx",
        @"C:\Users\me\Bilder\Smörgåsbord ünd 日本.png",
        @"\\server\share\Team Folder\notes.txt",
    ];

    private const string UriList =
        "file:///C:/Users/me/My%20Documents/report%202026.docx\r\n" +
        "file:///C:/Users/me/Bilder/Sm%C3%B6rg%C3%A5sbord%20%C3%BCnd%20%E6%97%A5%E6%9C%AC.png\r\n" +
        "file://server/share/Team%20Folder/notes.txt\r\n";

    [UiFact]
    public void AUriListCopiedHere_IsTheFileListExplorerPastes() => OnClipboard(hwnd =>
    {
        // A web address in the list is no file: it is left out of the CF_HDROP.
        var copied = Encoding.UTF8.GetBytes(UriList + "https://example.com/page\r\n");
        Assert.True(WindowsClipboard.TrySetData(hwnd, new Dictionary<string, byte[]>
        {
            [ClipboardFormats.Uris] = copied,
            [ClipboardFormats.PlainText] = Encoding.UTF8.GetBytes("three files"),
        }));

        Assert.True(WinFormsClipboard.ContainsFileDropList());
        Assert.Equal(Paths, WinFormsClipboard.GetFileDropList().Cast<string>());
        Assert.Equal("three files", WinFormsClipboard.GetText());

        Assert.True(WindowsClipboard.HasFormat(ClipboardFormats.Uris));
        Assert.Equal(UriList, Encoding.UTF8.GetString(WindowsClipboard.TryGetData(hwnd, ClipboardFormats.Uris)!));
    });

    [UiFact]
    public void FilesCopiedInExplorer_ReadAsAUriList() => OnClipboard(hwnd =>
    {
        var files = new StringCollection();
        files.AddRange(Paths);
        WinFormsClipboard.SetFileDropList(files);

        Assert.True(WindowsClipboard.HasFormat(ClipboardFormats.Uris));
        Assert.Equal(UriList, Encoding.UTF8.GetString(WindowsClipboard.TryGetData(hwnd, ClipboardFormats.Uris)!));
    });

    [UiFact]
    public void TextAlone_IsNoFileList() => OnClipboard(hwnd =>
    {
        WinFormsClipboard.SetText("not a file");

        Assert.False(WindowsClipboard.HasFormat(ClipboardFormats.Uris));
        Assert.Null(WindowsClipboard.TryGetData(hwnd, ClipboardFormats.Uris));
    });

    /// <summary>
    /// Runs the test on an STA thread (WinForms' clipboard is OLE's) with a message-only window
    /// to own the clipboard: opened without a window, <c>SetClipboardData</c> fails.
    /// </summary>
    private static void OnClipboard(Action<nint> body, [CallerMemberName] string test = "")
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            string? saved = null;
            nint hwnd = 0;
            try
            {
                using var guard = ForegroundGuard.Begin(member: test);
                if (WinFormsClipboard.ContainsText()) saved = WinFormsClipboard.GetText();
                hwnd = CreateWindowExW(0, "STATIC", null, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);
                Assert.NotEqual(0, hwnd);
                body(hwnd);
            }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            finally
            {
                // Best effort: a clipboard listener (history, a manager) may still hold the
                // clipboard after the test's last change, and a throw here ends the test host.
                try
                {
                    if (saved is not null) WinFormsClipboard.SetText(saved);   // retries itself
                    else if (hwnd != 0) WindowsClipboard.Clear(hwnd);         // so does this
                }
                catch (ExternalException) { }
                if (hwnd != 0) DestroyWindow(hwnd);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private const nint HWND_MESSAGE = -3;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string? windowName,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);
}
