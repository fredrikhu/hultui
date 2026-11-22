using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A control written outside the framework that takes the keyboard, such as a remote desktop
/// view, arms the platform's text input while it has focus by saying so, as a text box does.
/// Without it a real window gives its keys no text, and on Windows no AltGr either.
/// <see cref="KeyboardViewElement"/> is written as a control library writes it; a library
/// outside the framework's friend assemblies overrides with <c>protected override</c>.
/// </summary>
public class ThirdPartyTextInputTests
{
    private sealed class KeyboardViewElement : BoxElement
    {
        public KeyboardViewElement()
        {
            IsFocusable = true;
        }

        protected internal override bool WantsTextInput => true;
    }

    [Fact]
    public void AControlThatWantsTextInput_ArmsItWhileItHasFocus()
    {
        using var session = HeadlessSession.Create(400, 200);
        var view = new KeyboardViewElement();
        view.Width(Dimension.Px(200)).Height(Dimension.Px(100));
        var other = new BoxElement { IsFocusable = true };
        other.Width(Dimension.Px(50)).Height(Dimension.Px(50));
        session.Window.SetRoot(Flex.Row().Children(view, other));
        session.PumpFrame();

        session.Window.Click(view);
        Assert.True(session.Window.TextInputHost.IsTextInputActive);

        session.Window.Click(other);
        Assert.False(session.Window.TextInputHost.IsTextInputActive);
    }
}
