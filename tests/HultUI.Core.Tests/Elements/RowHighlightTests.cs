using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// <see cref="RowHighlight"/>'s public shape: the one-width form callers already use keeps its
/// constructor, properties and deconstruction (no source or binary break), and the width per side is
/// an init-only <see cref="RowHighlight.BorderThickness"/> that is part of the value (a list compares
/// highlights to skip a redraw).
/// </summary>
public class RowHighlightTests
{
    private static readonly Color Fill = new(40, 90, 200);
    private static readonly Color Accent = new(240, 200, 60);

    [Fact]
    public void TheOneWidthForm_IsAsItWas()
    {
        var type = typeof(RowHighlight);
        Assert.NotNull(type.GetConstructor([typeof(Color), typeof(CornerRadius), typeof(Color), typeof(float)]));
        Assert.Equal(typeof(float), type.GetProperty(nameof(RowHighlight.BorderWidth))!.PropertyType);
        Assert.NotNull(type.GetMethod("Deconstruct",
            [typeof(Color).MakeByRefType(), typeof(CornerRadius).MakeByRefType(), typeof(Color).MakeByRefType(), typeof(float).MakeByRefType()]));
        // No other public constructor: a call that passes `default` for the width still binds.
        Assert.Single(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance),
            c => c.GetParameters().Length == 4);

        var h = new RowHighlight(Fill, new CornerRadius(3), Accent, 1f);
        var (background, radius, border, width) = h;
        Assert.Equal((Fill, new CornerRadius(3), Accent, 1f), (background, radius, border, width));
        Assert.Null(h.BorderThickness);
        Assert.Equal(new RowHighlight(Fill, new CornerRadius(3), Accent, 1f), h);
    }

    [Fact]
    public void BorderThickness_IsAPublicInitOnlyWidthPerSide()
    {
        var property = typeof(RowHighlight).GetProperty(nameof(RowHighlight.BorderThickness))!;
        Assert.Equal(typeof(Thickness?), property.PropertyType);
        Assert.True(property.GetMethod!.IsPublic);
        // init, not set: the highlight stays a value.
        Assert.Contains(property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(),
            m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

        var edge = new RowHighlight(Fill, new CornerRadius(3), Accent) { BorderThickness = new Thickness(2, 0, 0, 0) };
        Assert.Equal(new Thickness(2, 0, 0, 0), edge.BorderThickness);
        Assert.Equal(0f, edge.BorderWidth);
    }

    [Fact]
    public void BorderThickness_IsPartOfTheValue()
    {
        var plain = new RowHighlight(Fill, new CornerRadius(3), Accent);
        var top = plain with { BorderThickness = new Thickness(2, 0, 0, 0) };
        var left = plain with { BorderThickness = new Thickness(0, 0, 0, 2) };

        Assert.NotEqual(plain, top);
        Assert.NotEqual(top, left);
        Assert.Equal(top, new RowHighlight(Fill, new CornerRadius(3), Accent) { BorderThickness = new Thickness(2, 0, 0, 0) });
        Assert.Equal(top.GetHashCode(), (plain with { BorderThickness = new Thickness(2, 0, 0, 0) }).GetHashCode());
    }
}
