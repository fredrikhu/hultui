using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// What an input is told to be on a rebuild, it becomes — masking and highlight spans included.
///
/// <para>Reconciliation keeps the live element and patches it from the freshly built one, so
/// anything <c>PatchFrom</c> forgets keeps the value the element was born with. Every test here
/// asserts a change, because asserting that a masked field <i>stays</i> masked passes with the
/// copy deleted: the live element was masked already.</para>
/// </summary>
public class TextInputRebuildTests
{
    /// <summary>
    /// The one that types a password in clear. Unkeyed siblings are matched by position and
    /// type, so when the username field goes away the live username input — unmasked — is the
    /// element that becomes the password field. It has to take the mask with the text.
    /// </summary>
    [Fact]
    public void AFieldThatTakesThePasswordsPlaceIsMasked()
    {
        using var session = HeadlessSession.Create(300, 160);

        var form = new SignInForm();
        var root = form.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(3);

        var username = Inputs(root)[0];
        Assert.False(username.Masked);

        form.AskUsername = false;
        form.Rebuild();
        session.PumpFrames(3);

        // Patched rather than replaced — otherwise this would be asserting on the incoming
        // element, which was built masked and passes whatever PatchFrom does.
        var survivor = Assert.Single(Inputs(root));
        Assert.Same(username, survivor);
        Assert.Equal("hunter2", survivor.Text);
        Assert.True(survivor.Masked);
    }

    /// <summary>The lesser direction: a show-password toggle that the field ignores.</summary>
    [Fact]
    public void RevealingThePasswordUnmasksTheField()
    {
        using var session = HeadlessSession.Create(300, 160);

        var form = new SignInForm { AskUsername = false };
        var root = form.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(3);

        var password = Assert.Single(Inputs(root));
        Assert.True(password.Masked);

        form.Reveal = true;
        form.Rebuild();
        session.PumpFrames(3);

        Assert.Same(password, Assert.Single(Inputs(root)));
        Assert.False(password.Masked);
    }

    /// <summary>Highlight spans describe the text of this build. Ones left over from the last
    /// paint colour ranges that no longer mean anything — or run past the end of the value.</summary>
    [Fact]
    public void SpansFollowARebuildThatChangesThem()
    {
        using var session = HeadlessSession.Create(300, 160);

        var host = new HighlightedInput { Spans = [new TextSpan(0, 3, Color.Red)] };
        var root = host.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(3);

        var live = Assert.Single(Inputs(root));

        IReadOnlyList<TextSpan> next = [new TextSpan(4, 3, Color.Blue)];
        host.Spans = next;
        host.Rebuild();
        session.PumpFrames(3);

        Assert.Same(live, Assert.Single(Inputs(root)));
        Assert.Same(next, live.Spans);

        host.Spans = null;
        host.Rebuild();
        session.PumpFrames(3);

        Assert.Null(live.Spans);
    }

    /// <summary>A real component, because only reconciliation runs <c>PatchFrom</c> — see
    /// <see cref="ChromelessInputTests"/>.</summary>
    private sealed class SignInForm : Component
    {
        public bool AskUsername = true;
        public bool Reveal;

        public override Element Build()
        {
            var form = Box.New().Padding(10f);
            if (AskUsername) form.AddChild(TextInput.New("fredrik"));
            var password = TextInput.New("hunter2");
            password.Masked = !Reveal;
            form.AddChild(password);
            return form;
        }

        public void Rebuild() => MarkDirty();
    }

    private sealed class HighlightedInput : Component
    {
        public IReadOnlyList<TextSpan>? Spans;

        public override Element Build() =>
            Box.New().Padding(10f).Children(TextInput.New("abc def").Spans(Spans));

        public void Rebuild() => MarkDirty();
    }

    private static List<TextInputElement> Inputs(Element root)
    {
        var found = new List<TextInputElement>();
        Collect(root);
        return found;

        void Collect(Element element)
        {
            if (element is TextInputElement input) found.Add(input);
            foreach (var child in element.Children) Collect(child);
        }
    }
}
