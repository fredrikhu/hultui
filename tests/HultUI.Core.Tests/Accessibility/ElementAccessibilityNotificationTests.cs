using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

public class ElementAccessibilityNotificationTests
{
    private sealed class RecordingObserver : IElementPropertyObserver
    {
        public List<(Element Element, string Property)> Changes { get; } = [];

        public void OnElementPropertyChanged(Element element, string propertyName)
            => Changes.Add((element, propertyName));
    }

    [Fact]
    public void TextElement_ContentChange_NotifiesName()
    {
        var text = TextElement.New("Before");
        var observer = new RecordingObserver();
        text.AccessibilityObserver = observer;

        text.Content = "After";

        Assert.Contains(observer.Changes,
            c => ReferenceEquals(c.Element, text) && c.Property == AccessibilityProperties.Name);
    }

    [Fact]
    public void TextElement_SameContent_DoesNotNotify()
    {
        var text = TextElement.New("Same");
        var observer = new RecordingObserver();
        text.AccessibilityObserver = observer;

        text.Content = "Same";

        Assert.Empty(observer.Changes);
    }

    [Fact]
    public void ChildTextChange_ReachesObserverOnAncestorRoot()
    {
        // The observer is installed on the root; descendants must walk up to it.
        var root = BoxElement.New();
        var text = TextElement.New("Before");
        root.AddChild(text);

        var observer = new RecordingObserver();
        root.AccessibilityObserver = observer;

        text.Content = "After";

        Assert.Contains(observer.Changes, c => ReferenceEquals(c.Element, text));
    }
}
