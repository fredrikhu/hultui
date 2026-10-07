using System.Xml.Linq;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>The platform's generated XML docs: each comment sits on its own member. A member
/// added between a comment and the member it describes takes that comment as a second summary
/// and leaves the other member with none; the compiler does not warn.</summary>
public class DocCommentTests
{
    static XDocument Docs()
    {
        string path = Path.ChangeExtension(typeof(SDL3Window).Assembly.Location, ".xml");
        Assert.True(File.Exists(path), $"No generated XML docs at {path}");
        return XDocument.Load(path);
    }

    // Types are left out: a partial type's files each describe their own part, and the
    // compiler joins those summaries on purpose.
    [Fact]
    public void NoMember_HasTwoSummaries()
    {
        var doubled = Docs().Descendants("member")
            .Where(m => !((string?)m.Attribute("name"))!.StartsWith("T:"))
            .Where(m => m.Elements("summary").Count() > 1)
            .Select(m => (string?)m.Attribute("name"))
            .ToList();
        Assert.Empty(doubled);
    }

    [Fact]
    public void PollAllEvents_HasItsSummary()
    {
        var member = Docs().Descendants("member")
            .Single(m => ((string?)m.Attribute("name"))!.StartsWith("M:HultUI.Platform.SDL3.SDL3Window.PollAllEvents("));
        Assert.Contains("Polls all SDL events", member.Element("summary")!.Value);
    }
}
