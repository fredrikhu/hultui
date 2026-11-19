using HultUI.Core.Builders;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="HeadlessWindow.TextShaperCalls"/>: the strings the window's text pipeline handed its
/// shaper, read before and after a change (docs/plans/19-run-shaping.md, item 4). New words are
/// shaped once; frames with nothing new, and new labels of words seen before, shape none.
/// </summary>
public class TextShaperCallsTests
{
    [Fact]
    public void NewWordsAreShapedOnce_AndTextTheCachesHoldIsNot()
    {
        using var session = HeadlessSession.Create(400, 200);
        var column = Flex.Column();
        session.Window.SetRoot(column);
        session.PumpFrames(2);

        long before = session.Window.TextShaperCalls;
        column.ReplaceChildren(TextB.New("zebra quokka narwhal").FontSize(13f));
        session.PumpFrames(2);
        long shaped = session.Window.TextShaperCalls - before;
        Assert.True(shaped >= 3, $"{shaped} strings shaped for three new words");

        before = session.Window.TextShaperCalls;
        session.PumpFrames(2);
        column.ReplaceChildren(TextB.New("narwhal zebra quokka").FontSize(13f), TextB.New("quokka").FontSize(13f));
        session.PumpFrames(2);
        Assert.Equal(before, session.Window.TextShaperCalls);
    }
}
