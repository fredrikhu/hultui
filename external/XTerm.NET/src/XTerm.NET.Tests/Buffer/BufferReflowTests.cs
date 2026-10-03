using XTerm.Buffer;

namespace XTerm.Tests.Buffer;

public class BufferReflowTests
{
    [Fact]
    public void ReflowSmallerGetNewLineLengths_SmallLineWithWideCharacters()
    {
        var line = new BufferLine(4);
        SetCell(line, 0, "汉", 2);
        SetCell(line, 1, "", 0);
        SetCell(line, 2, "语", 2);
        SetCell(line, 3, "", 0);

        Assert.Equal("汉语", line.TranslateToString(trimRight: true));
        Assert.Equal(new[] { 2, 2 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 4, 3));
        Assert.Equal(new[] { 2, 2 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 4, 2));
    }

    [Fact]
    public void ReflowSmallerGetNewLineLengths_LargeLineWithWideCharacters()
    {
        var line = new BufferLine(12);
        for (var i = 0; i < 12; i += 4)
        {
            SetCell(line, i, "汉", 2);
            SetCell(line, i + 2, "语", 2);
        }
        for (var i = 1; i < 12; i += 2)
        {
            SetCell(line, i, "", 0);
        }

        Assert.Equal("汉语汉语汉语", line.TranslateToString());
        Assert.Equal(new[] { 10, 2 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 12, 11));
        Assert.Equal(new[] { 10, 2 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 12, 10));
        Assert.Equal(new[] { 8, 4 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 12, 9));
        Assert.Equal(new[] { 8, 4 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 12, 8));
        Assert.Equal(new[] { 6, 6 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 12, 7));
        Assert.Equal(new[] { 6, 6 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 12, 6));
        Assert.Equal(new[] { 4, 4, 4 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 12, 5));
        Assert.Equal(new[] { 4, 4, 4 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 12, 4));
        Assert.Equal(new[] { 2, 2, 2, 2, 2, 2 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 12, 3));
        Assert.Equal(new[] { 2, 2, 2, 2, 2, 2 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 12, 2));
    }

    [Fact]
    public void ReflowSmallerGetNewLineLengths_MixedWideAndSingleCharacters()
    {
        var line = new BufferLine(6);
        SetCell(line, 0, "a", 1);
        SetCell(line, 1, "汉", 2);
        SetCell(line, 2, "", 0);
        SetCell(line, 3, "语", 2);
        SetCell(line, 4, "", 0);
        SetCell(line, 5, "b", 1);

        Assert.Equal("a汉语b", line.TranslateToString());
        Assert.Equal(new[] { 5, 1 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 6, 5));
        Assert.Equal(new[] { 3, 3 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 6, 4));
        Assert.Equal(new[] { 3, 3 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 6, 3));
        Assert.Equal(new[] { 1, 2, 2, 1 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 6, 2));
    }

    [Fact]
    public void ReflowSmallerGetNewLineLengths_WrappedLineWithWideAndSingleCharacters()
    {
        var line1 = new BufferLine(6);
        SetCell(line1, 0, "a", 1);
        SetCell(line1, 1, "汉", 2);
        SetCell(line1, 2, "", 0);
        SetCell(line1, 3, "语", 2);
        SetCell(line1, 4, "", 0);
        SetCell(line1, 5, "b", 1);

        var line2 = new BufferLine(6) { IsWrapped = true };
        SetCell(line2, 0, "a", 1);
        SetCell(line2, 1, "汉", 2);
        SetCell(line2, 2, "", 0);
        SetCell(line2, 3, "语", 2);
        SetCell(line2, 4, "", 0);
        SetCell(line2, 5, "b", 1);

        Assert.Equal(new[] { 5, 4, 3 }, BufferReflow.ReflowSmallerGetNewLineLengths([line1, line2], 6, 5));
        Assert.Equal(new[] { 3, 4, 4, 1 }, BufferReflow.ReflowSmallerGetNewLineLengths([line1, line2], 6, 4));
        Assert.Equal(new[] { 3, 3, 3, 3 }, BufferReflow.ReflowSmallerGetNewLineLengths([line1, line2], 6, 3));
        Assert.Equal(new[] { 1, 2, 2, 2, 2, 2, 1 }, BufferReflow.ReflowSmallerGetNewLineLengths([line1, line2], 6, 2));
    }

    [Fact]
    public void ReflowSmallerGetNewLineLengths_LinesEndingInNullSpace()
    {
        var line = new BufferLine(5);
        SetCell(line, 0, "汉", 2);
        SetCell(line, 1, "", 0);
        SetCell(line, 2, "语", 2);
        SetCell(line, 3, "", 0);
        var empty = BufferCell.Empty;
        line.SetCell(4, ref empty);

        Assert.Equal("汉语", line.TranslateToString(trimRight: true));
        Assert.Equal(new[] { 2, 2 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 4, 3));
        Assert.Equal(new[] { 2, 2 }, BufferReflow.ReflowSmallerGetNewLineLengths([line], 4, 2));
    }

    private static void SetCell(BufferLine line, int col, string content, int width = 1)
    {
        var cell = content == "" ? BufferCell.Empty : new BufferCell(content, width, AttributeData.Default);
        line.SetCell(col, ref cell);
    }
}
