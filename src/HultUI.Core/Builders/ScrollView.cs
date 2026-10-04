using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class ScrollView
{
    public static ScrollViewElement New([OriginFile] string? file = null, [OriginLine] int line = 0) => ScrollViewElement.New(file, line);

    public static ScrollViewElement Vertical([OriginFile] string? file = null, [OriginLine] int line = 0) =>
        ScrollViewElement.New(file, line);

    public static ScrollViewElement Horizontal([OriginFile] string? file = null, [OriginLine] int line = 0)
    {
        var sv = ScrollViewElement.New(file, line);
        sv.Box.OverflowX = Layout.Overflow.Scroll;
        sv.Box.OverflowY = Layout.Overflow.Hidden;
        sv.ShowScrollbarX = true;
        sv.ShowScrollbarY = false;
        sv.VerticalWheelScrollsSideways = true;
        sv.Flex.Direction = Layout.FlexDirection.Row;
        return sv;
    }
}
