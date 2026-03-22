using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class ScrollView
{
    public static ScrollViewElement New() => ScrollViewElement.New();

    public static ScrollViewElement Vertical() =>
        ScrollViewElement.New();

    public static ScrollViewElement Horizontal()
    {
        var sv = ScrollViewElement.New();
        sv.Box.OverflowX = Layout.Overflow.Scroll;
        sv.Box.OverflowY = Layout.Overflow.Hidden;
        sv.ShowScrollbarX = true;
        sv.ShowScrollbarY = false;
        sv.Flex.Direction = Layout.FlexDirection.Row;
        return sv;
    }
}
