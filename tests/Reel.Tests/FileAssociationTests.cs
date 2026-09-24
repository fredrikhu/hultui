using Reel;
using Xunit;

namespace Reel.Tests;

/// <summary>
/// What the registration would write. The writing itself is four advapi32 calls and needs a
/// real registry to mean anything; what is worth pinning is the plan — the quoting, the
/// extensions, and the keys Windows actually reads when it builds "Open with" and the
/// Default apps page.
/// </summary>
public class FileAssociationTests
{
    private const string Exe = @"C:\Program Files\Reel\Reel.exe";

    private static string? Value(string key, string name) =>
        FileAssociations.Plan(Exe).Where(e => e.Key == key && e.Name == name)
            .Select(e => e.Value).FirstOrDefault();

    [Fact]
    public void TheOpenCommandQuotesBothTheExeAndTheFile()
    {
        var command = Value($@"Software\Classes\{FileAssociations.ProgId}\shell\open\command", "");

        // "C:\Program Files\..." unquoted hands the shell "C:\Program" and the rest as
        // arguments — the oldest bug in file associations.
        Assert.Equal($"\"{Exe}\" \"%1\"", command);
    }

    [Fact]
    public void EveryPlayableExtensionIsOfferedAndClaimedNowhere()
    {
        var plan = FileAssociations.Plan(Exe);

        foreach (var extension in Playlist.Extensions)
        {
            // Listed under OpenWithProgids: an offer, which leaves whatever owns the
            // extension today exactly where it is.
            Assert.Contains(plan, e => e.Key == $@"Software\Classes\{extension}\OpenWithProgids"
                                       && e.Name == FileAssociations.ProgId);
            // And declared as a capability, which is what puts Reel on the Settings page.
            Assert.Contains(plan, e => e.Key.EndsWith(@"Capabilities\FileAssociations", StringComparison.Ordinal)
                                       && e.Name == extension && e.Value == FileAssociations.ProgId);
        }

        // Nothing writes UserChoice or a per-extension default: Windows signs that itself,
        // and an app that forges it is malware by Microsoft's definition.
        Assert.DoesNotContain(plan, e => e.Key.Contains("UserChoice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan, e => e.Key == $@"Software\Classes\{Playlist.Extensions[0]}" && e.Name.Length == 0);
    }

    [Fact]
    public void TheAppIsRegisteredWhereSettingsLooksForIt()
    {
        var plan = FileAssociations.Plan(Exe);

        Assert.Equal(@"Software\Reel\Capabilities",
            Value(@"Software\RegisteredApplications", FileAssociations.ApplicationName));
        Assert.Equal("Reel", Value(@"Software\Reel\Capabilities", "ApplicationName"));
        Assert.All(plan, e => Assert.False(e.Key.StartsWith(@"Software\Classes\CLSID", StringComparison.Ordinal)));
        // Every key is under HKCU — no administrator, nothing machine-wide.
        Assert.All(plan, e => Assert.StartsWith("Software\\", e.Key, StringComparison.Ordinal));
    }

    [Fact]
    public void TheIconAndTheMenuEntryComeFromTheSameExecutable()
    {
        Assert.Equal($"\"{Exe}\",0", Value($@"Software\Classes\{FileAssociations.ProgId}\DefaultIcon", ""));
        Assert.Equal("&Play with Reel", Value($@"Software\Classes\{FileAssociations.ProgId}\shell\open", ""));
    }

    [Fact]
    public void NothingIsRegisteredUntilItIs()
    {
        // The check is about *this* executable: a second copy of Reel elsewhere on disk
        // does not count as this one being registered.
        Assert.False(FileAssociations.IsRegistered(@"C:\nowhere\Reel.exe"));
    }
}
