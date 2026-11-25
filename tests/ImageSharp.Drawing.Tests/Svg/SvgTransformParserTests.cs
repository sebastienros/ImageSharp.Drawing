// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

public class SvgTransformParserTests
{
    // Each case maps a point through the transform as the SVG 1.1 coordinate system chapter defines the functions.
    [Theory]
    [InlineData("translate(10, 20)", 1F, 1F, 11F, 21F)]
    [InlineData("translate(10)", 1F, 1F, 11F, 1F)]
    [InlineData("scale(2)", 1F, 3F, 2F, 6F)]
    [InlineData("scale(2 3)", 1F, 1F, 2F, 3F)]
    [InlineData("rotate(90)", 1F, 0F, 0F, 1F)]
    [InlineData("rotate(90 10 10)", 20F, 10F, 10F, 20F)]
    [InlineData("rotate(-90, 10, 10)", 20F, 10F, 10F, 0F)]
    [InlineData("skewX(45)", 0F, 1F, 1F, 1F)]
    [InlineData("skewY(45)", 1F, 0F, 1F, 1F)]
    [InlineData("matrix(1 2 3 4 5 6)", 1F, 1F, 9F, 12F)]
    [InlineData("matrix(1,2,3,4,5,6)", 2F, 0F, 7F, 10F)]
    public void TryParse_MapsPointsAsSvgDefinesEachFunction(string input, float x, float y, float expectedX, float expectedY)
        => AssertMaps(input, new Vector2(x, y), new Vector2(expectedX, expectedY));

    // A transform list is equivalent to nested groups: the first transform is the outermost, so it applies last.
    [Theory]
    [InlineData("translate(10,0) scale(2)", 1F, 1F, 12F, 2F)]
    [InlineData("scale(2) translate(10,0)", 1F, 1F, 22F, 2F)]
    [InlineData("translate(10,0),scale(2)", 1F, 1F, 12F, 2F)]
    [InlineData("translate(10,0)scale(2)", 1F, 1F, 12F, 2F)]
    [InlineData("translate(100) rotate(90) scale(2)", 1F, 0F, 100F, 2F)]
    [InlineData("  translate ( 1 , 2 )  ", 0F, 0F, 1F, 2F)]
    public void TryParse_AppliesTheFirstTransformOutermost(string input, float x, float y, float expectedX, float expectedY)
        => AssertMaps(input, new Vector2(x, y), new Vector2(expectedX, expectedY));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_EmptyListIsIdentity(string input)
    {
        Assert.True(SvgTransformParser.TryParse(input, out Matrix3x2 value));
        Assert.Equal(Matrix3x2.Identity, value);
    }

    [Theory]
    [InlineData("translate(10,")]
    [InlineData("translate(10,)")]
    [InlineData("translate()")]
    [InlineData("translate(1 2 3)")]
    [InlineData("rotate(1 2)")]
    [InlineData("skewX(1 2)")]
    [InlineData("matrix(1 2 3 4 5)")]
    [InlineData("scale()")]
    [InlineData("foo(1)")]
    [InlineData("Translate(1)")]
    [InlineData("translate(1) x")]
    [InlineData("translate(1),")]
    [InlineData("translate(1),,scale(2)")]
    [InlineData("translate 1")]
    [InlineData("translate(1e39)")]
    [InlineData("translate(1px)")]
    [InlineData("rotate(45deg)")]
    [InlineData("matrix(1 0 0 1 3e38 0) translate(3e38)")]
    public void TryParse_RejectsInvalidLists(string input)
        => Assert.False(SvgTransformParser.TryParse(input, out _));

    // CSS Transforms 1: the CSS grammar takes commas between arguments and units on angles and translations.
    [Theory]
    [InlineData("scale(2)", 1F, 3F, 2F, 6F)]
    [InlineData("scale(2, 3)", 1F, 1F, 2F, 3F)]
    [InlineData("scale(50%)", 2F, 4F, 1F, 2F)]
    [InlineData("scaleX(2)", 1F, 1F, 2F, 1F)]
    [InlineData("scaleY(2)", 1F, 1F, 1F, 2F)]
    [InlineData("translate(10px, 20px)", 1F, 1F, 11F, 21F)]
    [InlineData("translate(10px)", 1F, 1F, 11F, 1F)]
    [InlineData("translate(0, 5px)", 1F, 1F, 1F, 6F)]
    [InlineData("translateX(10px)", 1F, 1F, 11F, 1F)]
    [InlineData("translateY(10px)", 1F, 1F, 1F, 11F)]
    [InlineData("rotate(90deg)", 1F, 0F, 0F, 1F)]
    [InlineData("rotate(0.25turn)", 1F, 0F, 0F, 1F)]
    [InlineData("rotate(100grad)", 1F, 0F, 0F, 1F)]
    [InlineData("rotate(0)", 1F, 0F, 1F, 0F)]
    [InlineData("skewX(45deg)", 0F, 1F, 1F, 1F)]
    [InlineData("skewY(45deg)", 1F, 0F, 1F, 1F)]
    [InlineData("skew(45deg, 45deg)", 1F, 1F, 2F, 2F)]
    [InlineData("skew(45deg)", 1F, 1F, 2F, 1F)]
    [InlineData("matrix(1, 2, 3, 4, 5, 6)", 1F, 1F, 9F, 12F)]
    [InlineData("SCALE(2)", 1F, 1F, 2F, 2F)]
    [InlineData("translate(10px) scale(2)", 1F, 1F, 12F, 2F)]
    [InlineData("translate(10px)scale(2)", 1F, 1F, 12F, 2F)]
    [InlineData("none", 3F, 4F, 3F, 4F)]
    public void TryParseCss_MapsPointsAsCssDefinesEachFunction(string input, float x, float y, float expectedX, float expectedY)
    {
        Assert.True(SvgTransformParser.TryParseCss(input, out Matrix3x2 transform));
        Vector2 actual = Vector2.Transform(new Vector2(x, y), transform);
        Assert.Equal(expectedX, actual.X, 4);
        Assert.Equal(expectedY, actual.Y, 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("rotate(45)")]
    [InlineData("translate(10)")]
    [InlineData("translate(10%)")]
    [InlineData("translate(1em)")]
    [InlineData("translate(10px 20px)")]
    [InlineData("scale(2 3)")]
    [InlineData("scale(2,)")]
    [InlineData("rotate(45deg, 10px, 10px)")]
    [InlineData("rotate3d(1, 0, 0, 45deg)")]
    [InlineData("translateZ(1px)")]
    [InlineData("scale (2)")]
    [InlineData("scale(2), rotate(45deg)")]
    [InlineData("none scale(2)")]
    [InlineData("scale(1e39)")]
    public void TryParseCss_RejectsInvalidValues(string input)
        => Assert.False(SvgTransformParser.TryParseCss(input, out _));

    private static void AssertMaps(string input, Vector2 point, Vector2 expected)
    {
        Assert.True(SvgTransformParser.TryParse(input, out Matrix3x2 transform));
        Vector2 actual = Vector2.Transform(point, transform);
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
    }
}
