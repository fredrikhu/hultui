using HultUI.Core.Platform;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The framework-side contract for the dialogs and the rich clipboard. The SDL
/// implementations of both talk to the OS and are verified by using them; what is pinned
/// here is the shape app code writes against, and the headless behaviour every app test
/// now depends on.
/// </summary>
public class FileDialogsAndClipboardTests
{
    [Fact]
    public async Task UnscriptedDialog_ReadsAsCancelled()
    {
        using var session = HeadlessSession.Create();

        // Cancellation is the shape app code must handle anyway, so it is the default
        // rather than an exception a test has to opt out of.
        Assert.Null(await session.Window.FileDialogs.OpenFolderAsync());
        Assert.Null(await session.Window.FileDialogs.SaveFileAsync("out.txt"));
        Assert.Empty(await session.Window.FileDialogs.OpenFilesAsync());
    }

    [Fact]
    public async Task ScriptedResults_AreHandedOutInOrder()
    {
        using var session = HeadlessSession.Create();
        var dialogs = session.Window.FileDialogs;
        dialogs.EnqueueResult(@"C:\first");
        dialogs.EnqueueCancel();
        dialogs.EnqueueResult(@"C:\third");

        Assert.Equal(@"C:\first", await dialogs.OpenFolderAsync());
        Assert.Null(await dialogs.OpenFolderAsync());
        Assert.Equal(@"C:\third", await dialogs.OpenFolderAsync());

        // Past the end of the queue is cancelled, not an error.
        Assert.Null(await dialogs.OpenFolderAsync());
        Assert.Equal(4, dialogs.OpenCount);
    }

    [Fact]
    public async Task TheRequestIsRecorded_SoTestsCanAssertWhatWasAskedFor()
    {
        using var session = HeadlessSession.Create();
        var dialogs = session.Window.FileDialogs;
        dialogs.EnqueueResult(@"C:\out\report.patch");

        await dialogs.SaveFileAsync("report.patch", @"C:\repo",
            [new FileDialogFilter("Patch files", "patch")]);

        var request = Assert.IsType<FileDialogRequest>(dialogs.LastRequest);
        Assert.Equal(FileDialogKind.SaveFile, request.Kind);
        Assert.Equal("report.patch", request.SuggestedFileName);
        Assert.Equal(@"C:\repo", request.InitialDirectory);
        Assert.Equal("Patch files", Assert.Single(request.Filters).Label);
    }

    [Fact]
    public async Task Unavailable_ResolvesToCancelled_WithoutConsumingTheQueue()
    {
        using var session = HeadlessSession.Create();
        var dialogs = session.Window.FileDialogs;
        dialogs.EnqueueResult(@"C:\somewhere");
        dialogs.IsAvailable = false;

        Assert.Null(await dialogs.OpenFolderAsync());

        // The scripted answer survives, so a test can flip availability back and use it.
        dialogs.IsAvailable = true;
        Assert.Equal(@"C:\somewhere", await dialogs.OpenFolderAsync());
    }

    [Fact]
    public async Task MultiSelect_ReturnsEveryPath()
    {
        using var session = HeadlessSession.Create();
        session.Window.FileDialogs.EnqueueResult(@"C:\a.txt", @"C:\b.txt");

        var picked = await session.Window.FileDialogs.OpenFilesAsync();

        Assert.Equal([@"C:\a.txt", @"C:\b.txt"], picked);
    }

    [Fact]
    public void Clipboard_OffersEveryFormatOfOneSelection()
    {
        using var session = HeadlessSession.Create();
        var clipboard = session.Window.Clipboard;

        clipboard.SetData(new Dictionary<string, byte[]>
        {
            [ClipboardFormats.Html] = "<b>diff</b>"u8.ToArray(),
            [ClipboardFormats.PlainText] = "diff"u8.ToArray(),
        });

        Assert.True(clipboard.HasFormat(ClipboardFormats.Html));
        Assert.Equal("<b>diff</b>"u8.ToArray(), clipboard.GetData(ClipboardFormats.Html));

        // The plain-text alternative is the same selection, so it reads back through the
        // text API too — that is the whole point of offering both.
        Assert.Equal("diff", clipboard.GetText());
    }

    [Fact]
    public void TakingTheClipboardAsText_DropsTheOtherFormats()
    {
        using var session = HeadlessSession.Create();
        var clipboard = session.Window.Clipboard;
        clipboard.SetData(new Dictionary<string, byte[]>
        {
            [ClipboardFormats.Html] = "<b>old</b>"u8.ToArray(),
        });

        clipboard.SetText("plain");

        // An owner offers one selection; a second offer replaces the first rather than
        // adding to it, so a stale HTML alternative can never be pasted.
        Assert.False(clipboard.HasFormat(ClipboardFormats.Html));
        Assert.Equal("plain", clipboard.GetText());
    }

    [Fact]
    public void UnofferedFormat_ReadsAsAbsent()
    {
        using var session = HeadlessSession.Create();
        var clipboard = session.Window.Clipboard;
        clipboard.SetText("just text");

        Assert.False(clipboard.HasFormat(ClipboardFormats.Png));
        Assert.Null(clipboard.GetData(ClipboardFormats.Png));

        clipboard.Clear();
        Assert.Null(clipboard.GetText());
        Assert.False(clipboard.HasFormat(ClipboardFormats.PlainText));
    }

    [Fact]
    public void SetData_CopiesTheBytes()
    {
        using var session = HeadlessSession.Create();
        var clipboard = session.Window.Clipboard;
        var buffer = new byte[] { 1, 2, 3 };

        clipboard.SetData(new Dictionary<string, byte[]> { [ClipboardFormats.Png] = buffer });
        buffer[0] = 99;

        // A caller reusing its buffer must not be able to change what a later paste yields.
        Assert.Equal([1, 2, 3], clipboard.GetData(ClipboardFormats.Png));
    }
}
