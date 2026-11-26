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
    [InlineData("translate(calc(5px + 5px), calc(2 * 10px))", 1F, 1F, 11F, 21F)]
    [InlineData("rotate(calc(45deg + 45deg))", 1F, 0F, 0F, 1F)]
    [InlineData("scale(calc(1 + 1))", 1F, 3F, 2F, 6F)]
    public void TryParseCss_MapsPointsAsCssDefinesEachFunction(string input, float x, float y, float expectedX, float expectedY)
    {
        Assert.True(SvgCssTransform.TryParse(input, out SvgCssTransform transform));
        Assert.True(transform.IsStatic);
        Vector2 actual = Vector2.Transform(new Vector2(x, y), transform.Matrix);
        Assert.Equal(expectedX, actual.X, 4);
        Assert.Equal(expectedY, actual.Y, 4);
    }

    // CSS Transforms 2: Chromium composes the 3D functions and draws SVG elements with the result flattened to the plane.
    // The oracle shows translate3d, translateZ, rotateZ, rotate3d about z, scale3d, scaleZ, matrix3d, and perspective
    // alone acting as their 2D part, and rotateX(60deg) halving y.
    [Theory]
    [InlineData("translate3d(10px, 20px, 30px)", 1F, 1F, 11F, 21F)]
    [InlineData("translateZ(10px) translateX(10px)", 1F, 1F, 11F, 1F)]
    [InlineData("rotateZ(90deg)", 1F, 0F, 0F, 1F)]
    [InlineData("rotate3d(0, 0, 1, 90deg)", 1F, 0F, 0F, 1F)]
    [InlineData("rotate3d(0, 0, 0, 90deg)", 1F, 2F, 1F, 2F)]
    [InlineData("rotateX(60deg)", 2F, 4F, 2F, 2F)]
    [InlineData("rotateY(60deg)", 4F, 2F, 2F, 2F)]
    [InlineData("scale3d(2, 3, 4)", 1F, 1F, 2F, 3F)]
    [InlineData("scaleZ(2)", 1F, 1F, 1F, 1F)]
    [InlineData("matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,0, 100,5,0,1)", 1F, 1F, 101F, 6F)]
    [InlineData("perspective(100px) translateX(10px)", 1F, 1F, 11F, 1F)]
    [InlineData("perspective(none)", 1F, 1F, 1F, 1F)]
    public void TryParseCss_FlattensThreeDimensionalFunctions(string input, float x, float y, float expectedX, float expectedY)
    {
        Assert.True(SvgCssTransform.TryParse(input, out SvgCssTransform transform));
        Vector2 actual = Vector2.Transform(new Vector2(x, y), transform.Matrix);
        Assert.Equal(expectedX, actual.X, 4);
        Assert.Equal(expectedY, actual.Y, 4);
    }

    // Percentages refer to the reference box, and units other than px to the font size and the resolution, so such a
    // value resolves when the element does.
    [Theory]
    [InlineData("translate(50%, 25%)", 200F, 100F, 16F, 100F, 25F)]
    [InlineData("translateX(10%)", 200F, 100F, 16F, 20F, 0F)]
    [InlineData("translate(2em)", 0F, 0F, 10F, 20F, 0F)]
    [InlineData("translate(1in, 0)", 0F, 0F, 16F, 96F, 0F)]
    [InlineData("translate(25.4mm)", 0F, 0F, 16F, 96F, 0F)]
    [InlineData("translate(calc(2em + 5px))", 0F, 0F, 10F, 25F, 0F)]
    public void TryParseCss_ResolvesRelativeArgumentsLater(string input, float boxWidth, float boxHeight, float fontSize, float expectedX, float expectedY)
    {
        // A math function that mixes units goes to the table of the document being read.
        SvgCalcTable table = new();
        SvgCalcTable previous = SvgCalcTable.Enter(table);
        SvgCssTransform transform;
        try
        {
            Assert.True(SvgCssTransform.TryParse(input, out transform));
        }
        finally
        {
            SvgCalcTable.Exit(previous);
        }

        Assert.False(transform.IsStatic);
        SvgUnitBasis units = new(96F, 16F, new SizeF(300F, 150F), new SvgStyleContext(table, new SvgLoadOptions()));
        Matrix3x2 matrix = transform.Resolve(new SizeF(boxWidth, boxHeight), fontSize, units);
        Vector2 actual = Vector2.Transform(Vector2.Zero, matrix);
        Assert.Equal(expectedX, actual.X, 3);
        Assert.Equal(expectedY, actual.Y, 3);
    }

    // The oracle rejects a unitless non-zero angle or length, arguments without commas, and unknown functions.
    [Theory]
    [InlineData("")]
    [InlineData("rotate(45)")]
    [InlineData("translate(10)")]
    [InlineData("translate(10px 20px)")]
    [InlineData("scale(2 3)")]
    [InlineData("scale(2,)")]
    [InlineData("rotate(45deg, 10px, 10px)")]
    [InlineData("translateZ(10%)")]
    [InlineData("rotate3d(1, 0, 0)")]
    [InlineData("scale (2)")]
    [InlineData("scale(2), rotate(45deg)")]
    [InlineData("none scale(2)")]
    [InlineData("scale(1e39)")]
    [InlineData("translate(calc(10px+5px))")]
    [InlineData("translate(calc(10px * 10px))")]
    [InlineData("rotate(calc(45deg + 10px))")]
    [InlineData("foo(1px)")]
    public void TryParseCss_RejectsInvalidValues(string input)
        => Assert.False(SvgCssTransform.TryParse(input, out _));

    private static void AssertMaps(string input, Vector2 point, Vector2 expected)
    {
        Assert.True(SvgTransformParser.TryParse(input, out Matrix3x2 transform));
        Vector2 actual = Vector2.Transform(point, transform);
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
    }
}
