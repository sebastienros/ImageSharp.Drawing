// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

public class SvgPaintTests
{
    [Theory]
    [InlineData("none", (int)SvgPaintKind.None)]
    [InlineData(" NONE ", (int)SvgPaintKind.None)]
    [InlineData("currentColor", (int)SvgPaintKind.CurrentColor)]
    [InlineData("currentcolor", (int)SvgPaintKind.CurrentColor)]
    [InlineData("red", (int)SvgPaintKind.Color)]
    [InlineData("#00ff00", (int)SvgPaintKind.Color)]
    [InlineData("rgb(0 0 255)", (int)SvgPaintKind.Color)]
    [InlineData("context-fill", (int)SvgPaintKind.ContextFill)]
    [InlineData(" Context-Stroke ", (int)SvgPaintKind.ContextStroke)]
    public void TryParse_ReadsSimplePaints(string input, int kind)
    {
        Assert.True(SvgPaint.TryParse(input, null, out SvgPaint paint));
        Assert.Equal((SvgPaintKind)kind, paint.Kind);
    }

    [Fact]
    public void TryParse_ReadsTheColor()
    {
        Assert.True(SvgPaint.TryParse("#0f0", null, out SvgPaint paint));
        Assert.Equal(new Rgba32(0, 255, 0, 255), paint.Color.ToPixel<Rgba32>());
    }

    [Theory]
    [InlineData("url(#g)", "g")]
    [InlineData("url( #g )", "g")]
    [InlineData("url('#g')", "g")]
    [InlineData("url(\"#my-gradient\")", "my-gradient")]
    [InlineData("URL(#g)", "g")]
    public void TryParse_ReadsFragmentReferences(string input, string id)
    {
        Assert.True(SvgPaint.TryParse(input, null, out SvgPaint paint));
        Assert.Equal(SvgPaintKind.Url, paint.Kind);
        Assert.Equal(id, paint.Reference);
        Assert.Equal(SvgPaintKind.None, paint.Fallback);
    }

    [Fact]
    public void TryParse_ExternalReferenceHasNoLocalId()
    {
        Assert.True(SvgPaint.TryParse("url(other.svg#g)", null, out SvgPaint paint));
        Assert.Equal(SvgPaintKind.Url, paint.Kind);
        Assert.Null(paint.Reference);
    }

    [Fact]
    public void TryParse_ReadsColorFallback()
    {
        Assert.True(SvgPaint.TryParse("url(#g) #00f", null, out SvgPaint paint));
        Assert.Equal(SvgPaintKind.Url, paint.Kind);
        Assert.Equal("g", paint.Reference);
        Assert.Equal(SvgPaintKind.Color, paint.Fallback);
        Assert.Equal(new Rgba32(0, 0, 255, 255), paint.Color.ToPixel<Rgba32>());
    }

    [Theory]
    [InlineData("url(#g) none", (int)SvgPaintKind.None)]
    [InlineData("url(#g) currentColor", (int)SvgPaintKind.CurrentColor)]
    public void TryParse_ReadsKeywordFallbacks(string input, int fallback)
    {
        Assert.True(SvgPaint.TryParse(input, null, out SvgPaint paint));
        Assert.Equal((SvgPaintKind)fallback, paint.Fallback);
    }

    [Theory]
    [InlineData("")]
    [InlineData("url(#g")]
    [InlineData("url()")]
    [InlineData("url('#g)")]
    [InlineData("url(#g) foo")]
    [InlineData("url(#g) url(#h)")]
    [InlineData("url(#g) context-fill")]
    [InlineData("context-fill red")]
    [InlineData("red !important")]
    [InlineData("inherit")]
    public void TryParse_RejectsInvalidPaints(string input)
        => Assert.False(SvgPaint.TryParse(input, null, out _));
}
