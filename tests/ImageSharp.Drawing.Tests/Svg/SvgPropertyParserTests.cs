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

    [Theory]
    [InlineData("clip-path", (int)SvgPropertyId.ClipPath)]
    [InlineData("clip-rule", (int)SvgPropertyId.ClipRule)]
    [InlineData("mask", (int)SvgPropertyId.Mask)]
    [InlineData("mask-type", (int)SvgPropertyId.MaskType)]
    [InlineData("mix-blend-mode", (int)SvgPropertyId.MixBlendMode)]
    [InlineData("isolation", (int)SvgPropertyId.Isolation)]
    [InlineData("paint-order", (int)SvgPropertyId.PaintOrder)]
    [InlineData("vector-effect", (int)SvgPropertyId.VectorEffect)]
    [InlineData("overflow", (int)SvgPropertyId.Overflow)]
    [InlineData("marker-start", (int)SvgPropertyId.MarkerStart)]
    [InlineData("marker-mid", (int)SvgPropertyId.MarkerMid)]
    [InlineData("marker-end", (int)SvgPropertyId.MarkerEnd)]
    [InlineData("image-rendering", (int)SvgPropertyId.ImageRendering)]
    [InlineData("shape-rendering", (int)SvgPropertyId.ShapeRendering)]
    [InlineData("color-interpolation", (int)SvgPropertyId.ColorInterpolation)]
    [InlineData("transform-origin", (int)SvgPropertyId.TransformOrigin)]
    [InlineData("Transform", (int)SvgPropertyId.Transform)]
    public void TryGetProperty_KnowsThePhase2Properties(string name, int expected)
    {
        Assert.True(SvgPropertyParser.TryGetProperty(name, out SvgPropertyId property));
        Assert.Equal((SvgPropertyId)expected, property);
    }

    // marker is a CSS shorthand only, so it has no property of its own.
    [Fact]
    public void TryGetProperty_MarkerIsAShorthand()
    {
        Assert.False(SvgPropertyParser.TryGetProperty("marker", out _));
        Assert.True(SvgPropertyParser.IsMarkerShorthand("MARKER"));
    }

    // The inheritance column of the phase 2 property table.
    [Theory]
    [InlineData((int)SvgPropertyId.ClipPath, false)]
    [InlineData((int)SvgPropertyId.ClipRule, true)]
    [InlineData((int)SvgPropertyId.Mask, false)]
    [InlineData((int)SvgPropertyId.MaskType, false)]
    [InlineData((int)SvgPropertyId.MixBlendMode, false)]
    [InlineData((int)SvgPropertyId.Isolation, false)]
    [InlineData((int)SvgPropertyId.PaintOrder, true)]
    [InlineData((int)SvgPropertyId.VectorEffect, false)]
    [InlineData((int)SvgPropertyId.Overflow, false)]
    [InlineData((int)SvgPropertyId.MarkerStart, true)]
    [InlineData((int)SvgPropertyId.MarkerMid, true)]
    [InlineData((int)SvgPropertyId.MarkerEnd, true)]
    [InlineData((int)SvgPropertyId.ImageRendering, true)]
    [InlineData((int)SvgPropertyId.ShapeRendering, true)]
    [InlineData((int)SvgPropertyId.ColorInterpolation, true)]
    [InlineData((int)SvgPropertyId.TransformOrigin, false)]
    [InlineData((int)SvgPropertyId.Transform, false)]
    public void IsInherited_MatchesThePropertyTable(int property, bool inherited)
        => Assert.Equal(inherited, SvgSpecifiedStyle.IsInherited((SvgPropertyId)property));

    [Theory]
    [InlineData((int)SvgPropertyId.ClipPath)]
    [InlineData((int)SvgPropertyId.Mask)]
    [InlineData((int)SvgPropertyId.MarkerStart)]
    [InlineData((int)SvgPropertyId.MarkerMid)]
    [InlineData((int)SvgPropertyId.MarkerEnd)]
    public void TryParse_ReadsReferences(int id)
    {
        SvgPropertyId property = (SvgPropertyId)id;
        SvgSpecifiedStyle none = default;
        SvgSpecifiedStyle local = default;
        SvgSpecifiedStyle external = default;

        Assert.True(this.parser.TryParse(property, "None", ref none));
        Assert.True(this.parser.TryParse(property, " url( '#m' ) ", ref local));
        Assert.True(this.parser.TryParse(property, "url(other.svg#m)", ref external));

        Assert.True(ReferenceOf(property, none).IsNone);
        Assert.Equal("m", ReferenceOf(property, local).Id);
        Assert.False(ReferenceOf(property, external).IsNone);
        Assert.Null(ReferenceOf(property, external).Id);
    }

    [Theory]
    [InlineData("url(#m) none")]
    [InlineData("url(#m")]
    [InlineData("url()")]
    [InlineData("#m")]
    [InlineData("auto")]
    [InlineData("")]
    public void TryParse_RejectsInvalidReferences(string input)
    {
        SvgSpecifiedStyle style = default;

        Assert.False(this.parser.TryParse(SvgPropertyId.ClipPath, input, ref style));
        Assert.True(style.IsEmpty);

        // An invalid value does not create the extension.
        Assert.Null(style.Extended);
    }

    [Theory]
    [InlineData((int)SvgPropertyId.ClipRule, "evenodd", (int)SvgFillRule.EvenOdd)]
    [InlineData((int)SvgPropertyId.MaskType, "alpha", (int)SvgMaskType.Alpha)]
    [InlineData((int)SvgPropertyId.MaskType, "luminance", (int)SvgMaskType.Luminance)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "normal", (int)SvgBlendMode.Normal)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "multiply", (int)SvgBlendMode.Multiply)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "screen", (int)SvgBlendMode.Screen)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "overlay", (int)SvgBlendMode.Overlay)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "darken", (int)SvgBlendMode.Darken)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "lighten", (int)SvgBlendMode.Lighten)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "color-dodge", (int)SvgBlendMode.ColorDodge)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "color-burn", (int)SvgBlendMode.ColorBurn)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "hard-light", (int)SvgBlendMode.HardLight)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "soft-light", (int)SvgBlendMode.SoftLight)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "difference", (int)SvgBlendMode.Difference)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "exclusion", (int)SvgBlendMode.Exclusion)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "hue", (int)SvgBlendMode.Hue)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "saturation", (int)SvgBlendMode.Saturation)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "color", (int)SvgBlendMode.Color)]
    [InlineData((int)SvgPropertyId.MixBlendMode, "LUMINOSITY", (int)SvgBlendMode.Luminosity)]
    [InlineData((int)SvgPropertyId.Isolation, "isolate", (int)SvgIsolation.Isolate)]
    [InlineData((int)SvgPropertyId.VectorEffect, "non-scaling-stroke", (int)SvgVectorEffect.NonScalingStroke)]
    [InlineData((int)SvgPropertyId.Overflow, "hidden", (int)SvgOverflow.Hidden)]
    [InlineData((int)SvgPropertyId.Overflow, "scroll", (int)SvgOverflow.Scroll)]
    [InlineData((int)SvgPropertyId.Overflow, "auto", (int)SvgOverflow.Auto)]
    [InlineData((int)SvgPropertyId.Overflow, "visible", (int)SvgOverflow.Visible)]
    [InlineData((int)SvgPropertyId.ImageRendering, "optimizeSpeed", (int)SvgImageRendering.OptimizeSpeed)]
    [InlineData((int)SvgPropertyId.ImageRendering, "optimizeQuality", (int)SvgImageRendering.OptimizeQuality)]
    [InlineData((int)SvgPropertyId.ImageRendering, "pixelated", (int)SvgImageRendering.Pixelated)]
    [InlineData((int)SvgPropertyId.ImageRendering, "crisp-edges", (int)SvgImageRendering.CrispEdges)]
    [InlineData((int)SvgPropertyId.ImageRendering, "smooth", (int)SvgImageRendering.Smooth)]
    [InlineData((int)SvgPropertyId.ImageRendering, "high-quality", (int)SvgImageRendering.HighQuality)]
    [InlineData((int)SvgPropertyId.ShapeRendering, "crispEdges", (int)SvgShapeRendering.CrispEdges)]
    [InlineData((int)SvgPropertyId.ShapeRendering, "optimizeSpeed", (int)SvgShapeRendering.OptimizeSpeed)]
    [InlineData((int)SvgPropertyId.ShapeRendering, "geometricPrecision", (int)SvgShapeRendering.GeometricPrecision)]
    [InlineData((int)SvgPropertyId.ColorInterpolation, "linearRGB", (int)SvgColorInterpolation.LinearRgb)]
    [InlineData((int)SvgPropertyId.ColorInterpolation, "sRGB", (int)SvgColorInterpolation.SRgb)]
    [InlineData((int)SvgPropertyId.ColorInterpolation, "auto", (int)SvgColorInterpolation.Auto)]
    public void TryParse_ReadsKeywordProperties(int id, string input, int expected)
    {
        SvgPropertyId property = (SvgPropertyId)id;
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(property, input, ref style));
        Assert.True(style.HasValue(property));
        Assert.Equal(expected, KeywordOf(property, style));
    }

    [Theory]
    [InlineData((int)SvgPropertyId.ClipRule, "even-odd")]
    [InlineData((int)SvgPropertyId.MaskType, "luminosity")]
    [InlineData((int)SvgPropertyId.MixBlendMode, "plus-lighter")]
    [InlineData((int)SvgPropertyId.MixBlendMode, "multiply screen")]
    [InlineData((int)SvgPropertyId.Isolation, "none")]
    [InlineData((int)SvgPropertyId.VectorEffect, "non-scaling-size")]
    [InlineData((int)SvgPropertyId.Overflow, "hidden visible")]
    [InlineData((int)SvgPropertyId.ImageRendering, "nearest")]
    [InlineData((int)SvgPropertyId.ShapeRendering, "crisp-edges")]
    [InlineData((int)SvgPropertyId.ColorInterpolation, "linear")]
    public void TryParse_RejectsInvalidKeywords(int id, string input)
    {
        SvgSpecifiedStyle style = default;

        Assert.False(this.parser.TryParse((SvgPropertyId)id, input, ref style));
        Assert.True(style.IsEmpty);
    }

    // CSS Painting API: omitted layers follow the listed ones in the order fill, stroke, markers.
    [Theory]
    [InlineData("normal", (int)SvgPaintOrder.FillStrokeMarkers)]
    [InlineData("fill", (int)SvgPaintOrder.FillStrokeMarkers)]
    [InlineData("stroke", (int)SvgPaintOrder.StrokeFillMarkers)]
    [InlineData("markers", (int)SvgPaintOrder.MarkersFillStroke)]
    [InlineData("fill markers", (int)SvgPaintOrder.FillMarkersStroke)]
    [InlineData("markers stroke", (int)SvgPaintOrder.MarkersStrokeFill)]
    [InlineData("stroke markers", (int)SvgPaintOrder.StrokeMarkersFill)]
    [InlineData("stroke markers fill", (int)SvgPaintOrder.StrokeMarkersFill)]
    [InlineData(" Stroke  FILL ", (int)SvgPaintOrder.StrokeFillMarkers)]
    public void TryParse_ReadsPaintOrder(string input, int expected)
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.PaintOrder, input, ref style));
        Assert.Equal((SvgPaintOrder)expected, style.Extended!.PaintOrder);
    }

    [Theory]
    [InlineData("stroke stroke")]
    [InlineData("invalid")]
    [InlineData("stroke invalid")]
    [InlineData("stroke markers fill qwe")]
    [InlineData("normal stroke")]
    [InlineData("fill, stroke")]
    public void TryParse_RejectsInvalidPaintOrder(string input)
    {
        SvgSpecifiedStyle style = default;

        Assert.False(this.parser.TryParse(SvgPropertyId.PaintOrder, input, ref style));
    }

    // CSS Transforms 1, transform-origin: keywords map to percentages; one value centers the other axis; two keywords may
    // come in either order; a third component is a z length.
    [Theory]
    [InlineData("center", 50F, (int)SvgLengthUnit.Percent, 50F, (int)SvgLengthUnit.Percent)]
    [InlineData("left", 0F, (int)SvgLengthUnit.Percent, 50F, (int)SvgLengthUnit.Percent)]
    [InlineData("right", 100F, (int)SvgLengthUnit.Percent, 50F, (int)SvgLengthUnit.Percent)]
    [InlineData("top", 50F, (int)SvgLengthUnit.Percent, 0F, (int)SvgLengthUnit.Percent)]
    [InlineData("bottom", 50F, (int)SvgLengthUnit.Percent, 100F, (int)SvgLengthUnit.Percent)]
    [InlineData("10px", 10F, (int)SvgLengthUnit.Px, 50F, (int)SvgLengthUnit.Percent)]
    [InlineData("10px 20%", 10F, (int)SvgLengthUnit.Px, 20F, (int)SvgLengthUnit.Percent)]
    [InlineData("25% top", 25F, (int)SvgLengthUnit.Percent, 0F, (int)SvgLengthUnit.Percent)]
    [InlineData("left 2em", 0F, (int)SvgLengthUnit.Percent, 2F, (int)SvgLengthUnit.Em)]
    [InlineData("top left", 0F, (int)SvgLengthUnit.Percent, 0F, (int)SvgLengthUnit.Percent)]
    [InlineData("bottom right", 100F, (int)SvgLengthUnit.Percent, 100F, (int)SvgLengthUnit.Percent)]
    [InlineData("center bottom", 50F, (int)SvgLengthUnit.Percent, 100F, (int)SvgLengthUnit.Percent)]
    [InlineData("top center", 50F, (int)SvgLengthUnit.Percent, 0F, (int)SvgLengthUnit.Percent)]
    [InlineData("0.5 0.5", 0.5F, (int)SvgLengthUnit.Number, 0.5F, (int)SvgLengthUnit.Number)]
    [InlineData("10px 20px 30px", 10F, (int)SvgLengthUnit.Px, 20F, (int)SvgLengthUnit.Px)]
    [InlineData(" RIGHT  Top ", 100F, (int)SvgLengthUnit.Percent, 0F, (int)SvgLengthUnit.Percent)]
    public void TryParse_ReadsTransformOrigin(string input, float x, int xUnit, float y, int yUnit)
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.TransformOrigin, input, ref style));
        Assert.Equal(new SvgLength(x, (SvgLengthUnit)xUnit), style.Extended!.TransformOriginX);
        Assert.Equal(new SvgLength(y, (SvgLengthUnit)yUnit), style.Extended.TransformOriginY);
    }

    [Theory]
    [InlineData("top 25%")]
    [InlineData("left right")]
    [InlineData("top bottom")]
    [InlineData("10px left")]
    [InlineData("10px 20px 30%")]
    [InlineData("10px 20px left")]
    [InlineData("1px 2px 3px 4px")]
    [InlineData("10px, 20px")]
    [InlineData("middle")]
    public void TryParse_RejectsInvalidTransformOrigin(string input)
    {
        SvgSpecifiedStyle style = default;

        Assert.False(this.parser.TryParse(SvgPropertyId.TransformOrigin, input, ref style));
    }

    [Fact]
    public void TryParse_ReadsTheCssTransformProperty()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.Transform, "scale(2)", ref style));
        Assert.Equal(System.Numerics.Matrix3x2.CreateScale(2F), style.Extended!.Transform);
        Assert.True(this.parser.HasTransformDeclarations);
        Assert.False(this.parser.TryParse(SvgPropertyId.Transform, "rotate(45)", ref style));
    }

    [Fact]
    public void TryParseMarkerShorthand_SetsTheThreeLonghands()
    {
        SvgSpecifiedStyle style = default;
        SvgSpecifiedStyle keyword = default;

        Assert.True(this.parser.TryParseMarkerShorthand("url(#m)", ref style));
        Assert.True(this.parser.TryParseMarkerShorthand("inherit", ref keyword));
        Assert.False(this.parser.TryParseMarkerShorthand("url(#m) url(#n)", ref keyword));

        Assert.Equal("m", style.Extended!.MarkerStart.Id);
        Assert.Equal("m", style.Extended.MarkerMid.Id);
        Assert.Equal("m", style.Extended.MarkerEnd.Id);
        Assert.True(keyword.IsInherit(SvgPropertyId.MarkerStart));
        Assert.True(keyword.IsInherit(SvgPropertyId.MarkerMid));
        Assert.True(keyword.IsInherit(SvgPropertyId.MarkerEnd));
    }

    // A style stored in a table can be the source of many cascades, so applying it must not change any extension.
    [Fact]
    public void Apply_MergesExtendedValuesWithoutChangingEitherStyle()
    {
        SvgSpecifiedStyle lower = default;
        SvgSpecifiedStyle higher = default;
        Assert.True(this.parser.TryParse(SvgPropertyId.ClipPath, "url(#a)", ref lower));
        Assert.True(this.parser.TryParse(SvgPropertyId.Mask, "url(#b)", ref lower));
        Assert.True(this.parser.TryParse(SvgPropertyId.Mask, "url(#c)", ref higher));
        Assert.True(this.parser.TryParse(SvgPropertyId.PaintOrder, "stroke", ref higher));
        SvgExtendedStyle lowerExtension = lower.Extended!;
        SvgExtendedStyle higherExtension = higher.Extended!;

        lower.Apply(in higher);

        Assert.Equal("a", lower.Extended!.ClipPath.Id);
        Assert.Equal("c", lower.Extended.Mask.Id);
        Assert.Equal(SvgPaintOrder.StrokeFillMarkers, lower.Extended.PaintOrder);
        Assert.Equal("b", lowerExtension.Mask.Id);
        Assert.Equal(SvgPaintOrder.FillStrokeMarkers, lowerExtension.PaintOrder);
        Assert.Null(higherExtension.ClipPath.Id);
    }

    private static SvgReference ReferenceOf(SvgPropertyId property, in SvgSpecifiedStyle style)
        => property switch
        {
            SvgPropertyId.ClipPath => style.Extended!.ClipPath,
            SvgPropertyId.Mask => style.Extended!.Mask,
            SvgPropertyId.MarkerStart => style.Extended!.MarkerStart,
            SvgPropertyId.MarkerMid => style.Extended!.MarkerMid,
            _ => style.Extended!.MarkerEnd
        };

    private static int KeywordOf(SvgPropertyId property, in SvgSpecifiedStyle style)
        => property switch
        {
            SvgPropertyId.ClipRule => (int)style.Extended!.ClipRule,
            SvgPropertyId.MaskType => (int)style.Extended!.MaskType,
            SvgPropertyId.MixBlendMode => (int)style.Extended!.MixBlendMode,
            SvgPropertyId.Isolation => (int)style.Extended!.Isolation,
            SvgPropertyId.VectorEffect => (int)style.Extended!.VectorEffect,
            SvgPropertyId.Overflow => (int)style.Extended!.Overflow,
            SvgPropertyId.ImageRendering => (int)style.Extended!.ImageRendering,
            SvgPropertyId.ShapeRendering => (int)style.Extended!.ShapeRendering,
            _ => (int)style.Extended!.ColorInterpolation
        };
}
