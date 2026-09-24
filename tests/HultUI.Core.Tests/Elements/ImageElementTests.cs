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

    [Theory]
    [InlineData(800, 400, null, 500f, null, 500, 250)]     // too wide: scaled to the width offered
    [InlineData(300, 150, null, 500f, null, 300, 150)]     // narrower: never scaled up
    [InlineData(800, 400, 600f, 1000f, null, 600, 300)]    // asks for 600: gets 600, aspect kept
    [InlineData(800, 400, 600f, 450f, null, 450, 225)]     // asks for 600, offered 450
    [InlineData(400, 1600, null, 500f, 480f, 120, 480)]    // too tall: narrows to keep the ratio
    [InlineData(801, 401, null, 500f, null, 500, 250)]     // whole pixels, rounded down
    public void ScaledSize_FitsTheWidthAndHeightKeepingTheRatio(
        float iw, float ih, float? natural, float available, float? maxHeight, float w, float h)
    {
        var size = ImageElement.ScaledSize(new Size(iw, ih), natural, available, maxHeight);
        Assert.Equal((w, h), (size.Width, size.Height));
    }

    [Fact]
    public void MeasureContent_ScalesDownToTheWidthOffered_WhenAskedTo()
    {
        var img = ImageElement.New(new StubImageSource(new Size(800, 400)));
        Assert.Equal(new Size(800, 400), img.MeasureContent(500, 500, new StubMeasureContext()));   // opt-in only

        img.ScaleDownToFit = true;
        img.MaxImageHeight = 200;
        Assert.Equal(new Size(400, 200), img.MeasureContent(500, 500, new StubMeasureContext()));
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
    public void PatchFrom_CopiesTheScalingProperties()
    {
        var existing = ImageElement.New(new StubImageSource(new Size(100, 50)));
        var incoming = ImageElement.New(new StubImageSource(new Size(100, 50)));
        incoming.ScaleDownToFit = true;
        incoming.NaturalWidth = 60;
        incoming.MaxImageHeight = 40;

        existing.PatchFrom(incoming);

        Assert.True(existing.ScaleDownToFit);
        Assert.Equal(60f, existing.NaturalWidth);
        Assert.Equal(40f, existing.MaxImageHeight);
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

    [Fact]
    public void ACroppedImageMeasuresAsTheCropAndDrawsThatRectangle()
    {
        // A sprite sheet: twenty 320x180 frames in one image. The element is one frame.
        var sheet = new StubImageSource(new Size(1600, 720));
        var img = ImageElement.New(sheet);
        img.SourceRect = new Rect(320, 180, 320, 180);

        var container = BoxElement.New();
        container.Flex.AlignItems = AlignItems.FlexStart;
        container.AddChild(img);
        new LayoutEngine(new StubMeasureContext()).PerformLayout(container, 500, 500);

        Assert.Equal(320, img.Layout.Width);
        Assert.Equal(180, img.Layout.Height);

        var ctx = new CropRecordingContext();
        img.Render(ctx);

        Assert.Equal(new Rect(320, 180, 320, 180), ctx.LastSource);
        Assert.Equal(img.Layout.AbsoluteContentBox, ctx.LastDest);
    }

    [Fact]
    public void WithoutACropTheWholeImageIsDrawnUnderItsFit()
    {
        var img = ImageElement.New(new StubImageSource(new Size(100, 50)));
        img.Fit = ImageFit.Cover;

        var ctx = new CropRecordingContext();
        img.Render(ctx);

        Assert.Null(ctx.LastSource);
        Assert.Equal(ImageFit.Cover, ctx.LastFit);
    }

    /// <summary>Records which of the two image draws an element asked for.</summary>
    private sealed class CropRecordingContext : IRenderContext
    {
        public Rect? LastSource { get; private set; }
        public Rect LastDest { get; private set; }
        public ImageFit? LastFit { get; private set; }

        public void DrawImage(IImageSource source, Rect destRect, ImageFit fit)
        {
            LastDest = destRect;
            LastFit = fit;
        }

        public void DrawImage(IImageSource source, Rect destRect, Rect sourceRect)
        {
            LastDest = destRect;
            LastSource = sourceRect;
        }

        public void DrawRect(Rect rect, Color fill) { }
        public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth) { }
        public void DrawText(string text, Rect bounds, Core.Styling.ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines) { }
        public void DrawLine(Point from, Point to, Color color, float strokeWidth) { }
        public Size MeasureText(string text, Core.Styling.ResolvedInheritedStyle style) => Size.Zero;
        public IReadOnlyList<TextLineBox> LayoutTextLines(string text, Core.Styling.ResolvedInheritedStyle style, float maxWidth, int? maxLines) => [];
        public void PushClip(Rect clipRect) { }
        public void PushRoundedClip(Rect clipRect, CornerRadius radius) { }
        public void PushTranslate(float x, float y) { }
        public void PushOpacity(float opacity) { }
        public void Pop() { }
        public ICanvasDrawingContext CreateCanvasContext(Rect bounds) => throw new NotSupportedException();
        public void BeginFrame(float width, float height, float scaleFactor, Color? clearColor = null) { }
        public void EndFrame() { }
    }

    private sealed class StubImageSource(Size intrinsicSize, bool isLoaded = true) : IImageSource
    {
        public Size IntrinsicSize => intrinsicSize;
        public bool IsLoaded => isLoaded;
        public string? AlternateText => null;
    }
}
