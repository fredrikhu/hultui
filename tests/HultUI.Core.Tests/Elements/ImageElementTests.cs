using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class ImageElementTests
{
    [Fact]
    public void New_CreatesWithSource()
    {
        var source = new StubImageSource(new Size(100, 50));
        var img = ImageElement.New(source);

        Assert.Equal(source, img.Source);
        Assert.Equal(ImageFit.Contain, img.Fit);
    }

    [Fact]
    public void MeasureContent_ReturnsIntrinsicSize()
    {
        var source = new StubImageSource(new Size(200, 150));
        var img = ImageElement.New(source);

        var size = img.MeasureContent(500, 500, new StubMeasureContext());

        Assert.Equal(200, size.Width);
        Assert.Equal(150, size.Height);
    }

    [Fact]
    public void MeasureContent_ReturnsZero_WhenNoSource()
    {
        var img = ImageElement.New();

        var size = img.MeasureContent(500, 500, new StubMeasureContext());

        Assert.Equal(0, size.Width);
        Assert.Equal(0, size.Height);
    }

    [Fact]
    public void MeasureContent_ReturnsZero_WhenNotLoaded()
    {
        var source = new StubImageSource(new Size(100, 50), isLoaded: false);
        var img = ImageElement.New(source);

        var size = img.MeasureContent(500, 500, new StubMeasureContext());

        Assert.Equal(0, size.Width);
        Assert.Equal(0, size.Height);
    }

    [Fact]
    public void PatchFrom_CopiesImageProperties()
    {
        var source1 = new StubImageSource(new Size(100, 50));
        var source2 = new StubImageSource(new Size(200, 100));

        var existing = ImageElement.New(source1);
        existing.Fit = ImageFit.Contain;

        var incoming = ImageElement.New(source2);
        incoming.Fit = ImageFit.Cover;

        existing.PatchFrom(incoming);

        Assert.Equal(source2, existing.Source);
        Assert.Equal(ImageFit.Cover, existing.Fit);
    }

    [Fact]
    public void Layout_UsesIntrinsicSize_WhenAutoSized()
    {
        var source = new StubImageSource(new Size(120, 80));
        var img = ImageElement.New(source);

        // Wrap in a container because PerformLayout forces root to available size when auto
        // Use FlexStart alignment so the image isn't stretched to the container's cross size
        var container = BoxElement.New();
        container.Flex.AlignItems = AlignItems.FlexStart;
        container.AddChild(img);

        var layout = new LayoutEngine(new StubMeasureContext());
        layout.PerformLayout(container, 500, 500);

        Assert.Equal(120, img.Layout.Width);
        Assert.Equal(80, img.Layout.Height);
    }

    private sealed class StubImageSource(Size intrinsicSize, bool isLoaded = true) : IImageSource
    {
        public Size IntrinsicSize => intrinsicSize;
        public bool IsLoaded => isLoaded;
        public string? AlternateText => null;
    }
}
