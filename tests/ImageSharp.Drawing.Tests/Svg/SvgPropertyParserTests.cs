// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

public class SvgPropertyParserTests
{
    private readonly SvgPropertyParser parser = new();

    [Theory]
    [InlineData((int)SvgPropertyId.Fill)]
    [InlineData((int)SvgPropertyId.Opacity)]
    [InlineData((int)SvgPropertyId.StopColor)]
    public void TryParse_Inherit_SetsTheInheritBit(int id)
    {
        SvgPropertyId property = (SvgPropertyId)id;
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(property, " INHERIT ", ref style));
        Assert.True(style.IsSpecified(property));
        Assert.True(style.IsInherit(property));
        Assert.False(style.HasValue(property));
    }

    [Fact]
    public void TryParse_Initial_SetsTheInitialBit()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.StrokeWidth, "initial", ref style));
        Assert.True(style.IsInitial(SvgPropertyId.StrokeWidth));
        Assert.False(style.IsInherit(SvgPropertyId.StrokeWidth));
    }

    // CSS Cascade: unset acts as inherit on an inherited property and as initial otherwise.
    [Fact]
    public void TryParse_Unset_DependsOnInheritance()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.Fill, "unset", ref style));
        Assert.True(this.parser.TryParse(SvgPropertyId.Opacity, "unset", ref style));
        Assert.True(style.IsInherit(SvgPropertyId.Fill));
        Assert.True(style.IsInitial(SvgPropertyId.Opacity));
    }

    [Fact]
    public void TryParse_ValueAfterKeyword_ClearsTheKeyword()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.Fill, "inherit", ref style));
        Assert.True(this.parser.TryParse(SvgPropertyId.Fill, "red", ref style));
        Assert.True(style.HasValue(SvgPropertyId.Fill));
        Assert.False(style.IsInherit(SvgPropertyId.Fill));
    }

    // CSS Color Level 4: currentColor as the value of color is treated as inherit.
    [Fact]
    public void TryParse_ColorCurrentColor_IsInherit()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.Color, "currentColor", ref style));
        Assert.True(style.IsInherit(SvgPropertyId.Color));
    }

    [Fact]
    public void TryParse_StopColor_AcceptsCurrentColor()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.StopColor, "currentColor", ref style));
        Assert.Equal(SvgPaintKind.CurrentColor, style.StopColor.Kind);
        Assert.False(this.parser.TryParse(SvgPropertyId.StopColor, "url(#g)", ref style));
    }

    [Theory]
    [InlineData((int)SvgPropertyId.Opacity, "0.25", 0.25F)]
    [InlineData((int)SvgPropertyId.FillOpacity, "50%", 0.5F)]
    [InlineData((int)SvgPropertyId.StrokeOpacity, "2", 1F)]
    [InlineData((int)SvgPropertyId.StopOpacity, "-1", 0F)]
    public void TryParse_Opacity_ClampsToTheUnitRange(int id, string input, float expected)
    {
        SvgPropertyId property = (SvgPropertyId)id;
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(property, input, ref style));
        float actual = property switch
        {
            SvgPropertyId.Opacity => style.Opacity,
            SvgPropertyId.FillOpacity => style.FillOpacity,
            SvgPropertyId.StrokeOpacity => style.StrokeOpacity,
            _ => style.StopOpacity
        };

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("none", (int)SvgDisplay.None)]
    [InlineData("inline", (int)SvgDisplay.Inline)]
    [InlineData("BLOCK", (int)SvgDisplay.Inline)]
    [InlineData("inline-block", (int)SvgDisplay.Inline)]
    public void TryParse_Display_DistinguishesNone(string input, int expected)
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.Display, input, ref style));
        Assert.Equal((SvgDisplay)expected, style.Display);
    }

    [Fact]
    public void TryParse_Keywords_MapToTheirValues()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.Visibility, "hidden", ref style));
        Assert.True(this.parser.TryParse(SvgPropertyId.FillRule, "evenodd", ref style));
        Assert.True(this.parser.TryParse(SvgPropertyId.StrokeLineCap, "square", ref style));
        Assert.True(this.parser.TryParse(SvgPropertyId.StrokeLineJoin, "miter-clip", ref style));

        Assert.Equal(SvgVisibility.Hidden, style.Visibility);
        Assert.Equal(SvgFillRule.EvenOdd, style.FillRule);
        Assert.Equal(SvgStrokeLineCap.Square, style.StrokeLineCap);
        Assert.Equal(SvgStrokeLineJoin.MiterClip, style.StrokeLineJoin);
    }

    [Fact]
    public void TryParse_StrokeDashArray_StoresTheListInTheDashTable()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.StrokeDashArray, "5, 10 15%", ref style));
        SvgDashArray dashes = style.StrokeDashArray;

        Assert.False(dashes.IsNone);
        Assert.Equal(
            [new SvgLength(5, SvgLengthUnit.Number), new SvgLength(10, SvgLengthUnit.Number), new SvgLength(15, SvgLengthUnit.Percent)],
            this.parser.DashLengths.GetRange(dashes.Start, dashes.Count));

        Assert.True(this.parser.TryParse(SvgPropertyId.StrokeDashArray, "none", ref style));
        Assert.True(style.StrokeDashArray.IsNone);
    }

    [Fact]
    public void TryParse_FontSize_ReadsLengthsAndKeywords()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.FontSize, "medium", ref style));
        Assert.Equal(new SvgLength(16, SvgLengthUnit.Px), style.FontSize);

        Assert.True(this.parser.TryParse(SvgPropertyId.FontSize, "xx-large", ref style));
        Assert.Equal(new SvgLength(32, SvgLengthUnit.Px), style.FontSize);

        Assert.True(this.parser.TryParse(SvgPropertyId.FontSize, "larger", ref style));
        Assert.Equal(new SvgLength(120, SvgLengthUnit.Percent), style.FontSize);

        Assert.True(this.parser.TryParse(SvgPropertyId.FontSize, "2em", ref style));
        Assert.Equal(new SvgLength(2, SvgLengthUnit.Em), style.FontSize);
    }

    // An invalid value is dropped: the declaration does not change a value parsed before it.
    [Theory]
    [InlineData((int)SvgPropertyId.Fill, "bogus")]
    [InlineData((int)SvgPropertyId.Fill, "red !important")]
    [InlineData((int)SvgPropertyId.Color, "url(#a)")]
    [InlineData((int)SvgPropertyId.Display, "hidden")]
    [InlineData((int)SvgPropertyId.Visibility, "none")]
    [InlineData((int)SvgPropertyId.Opacity, "0.5px")]
    [InlineData((int)SvgPropertyId.FillRule, "non-zero")]
    [InlineData((int)SvgPropertyId.StrokeWidth, "-1")]
    [InlineData((int)SvgPropertyId.StrokeWidth, "thick")]
    [InlineData((int)SvgPropertyId.StrokeLineCap, "rounded")]
    [InlineData((int)SvgPropertyId.StrokeLineJoin, "mitre")]
    [InlineData((int)SvgPropertyId.StrokeMiterLimit, "0.5")]
    [InlineData((int)SvgPropertyId.StrokeMiterLimit, "4px")]
    [InlineData((int)SvgPropertyId.StrokeDashArray, "5 -1")]
    [InlineData((int)SvgPropertyId.StrokeDashArray, "")]
    [InlineData((int)SvgPropertyId.StrokeDashOffset, "1 2")]
    [InlineData((int)SvgPropertyId.FontSize, "-2px")]
    [InlineData((int)SvgPropertyId.FontSize, "huge")]
    [InlineData((int)SvgPropertyId.StopOpacity, "")]
    public void TryParse_InvalidValue_LeavesTheStyleUnchanged(int id, string input)
    {
        SvgPropertyId property = (SvgPropertyId)id;
        SvgSpecifiedStyle style = default;

        Assert.False(this.parser.TryParse(property, input, ref style));
        Assert.True(style.IsEmpty);
    }

    [Fact]
    public void TryParse_InvalidValue_KeepsTheEarlierValue()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.Fill, "#00f", ref style));
        Assert.False(this.parser.TryParse(SvgPropertyId.Fill, "bogus", ref style));
        Assert.Equal(new Rgba32(0, 0, 255, 255), style.Fill.Color.ToPixel<Rgba32>());
    }

    [Theory]
    [InlineData("fill", (int)SvgPropertyId.Fill)]
    [InlineData("FILL", (int)SvgPropertyId.Fill)]
    [InlineData("Stroke-Width", (int)SvgPropertyId.StrokeWidth)]
    [InlineData("stop-color", (int)SvgPropertyId.StopColor)]
    public void TryGetProperty_IgnoresAsciiCase(string name, int expected)
    {
        Assert.True(SvgPropertyParser.TryGetProperty(name, out SvgPropertyId property));
        Assert.Equal((SvgPropertyId)expected, property);
    }

    [Fact]
    public void Apply_OverridesOnlyTheSpecifiedProperties()
    {
        SvgSpecifiedStyle lower = default;
        SvgSpecifiedStyle higher = default;
        Assert.True(this.parser.TryParse(SvgPropertyId.Fill, "red", ref lower));
        Assert.True(this.parser.TryParse(SvgPropertyId.Stroke, "red", ref lower));
        Assert.True(this.parser.TryParse(SvgPropertyId.Stroke, "inherit", ref higher));
        Assert.True(this.parser.TryParse(SvgPropertyId.Opacity, "0.5", ref higher));

        lower.Apply(in higher);

        Assert.True(lower.HasValue(SvgPropertyId.Fill));
        Assert.Equal(new Rgba32(255, 0, 0, 255), lower.Fill.Color.ToPixel<Rgba32>());
        Assert.True(lower.IsInherit(SvgPropertyId.Stroke));
        Assert.Equal(0.5F, lower.Opacity);
    }
}
