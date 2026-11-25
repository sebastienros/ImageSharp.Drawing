// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

public class SvgNumberParserTests
{
    // SVG and CSS numbers end where the next character cannot continue them, so packed input splits into numbers.
    [Theory]
    [InlineData("10-20", 10F, 2)]
    [InlineData("1.5.5", 1.5F, 3)]
    [InlineData(".5.5", 0.5F, 2)]
    [InlineData("1e2e3", 100F, 3)]
    [InlineData("1E-2x", 0.01F, 4)]
    [InlineData("-.5e1", -5F, 5)]
    [InlineData("+.5", 0.5F, 3)]
    [InlineData("1em", 1F, 1)]
    [InlineData("1e", 1F, 1)]
    [InlineData("1e+", 1F, 1)]
    [InlineData("2e+1,", 20F, 4)]
    [InlineData("007", 7F, 3)]
    [InlineData("3.4e38", 3.4e38F, 6)]
    public void TryReadNumber_StopsAtTheEndOfTheNumber(string input, float expected, int expectedLength)
    {
        Assert.True(SvgNumberParser.TryReadNumber(input, false, out float value, out int length));
        Assert.Equal(expected, value);
        Assert.Equal(expectedLength, length);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("e5")]
    [InlineData(" 1")]
    [InlineData("+-1")]
    [InlineData("1e39")]
    [InlineData("-1e39")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void TryReadNumber_RejectsInvalidOrNonFiniteNumbers(string input)
        => Assert.False(SvgNumberParser.TryReadNumber(input, false, out _, out _));

    [Fact]
    public void TryReadNumber_TrailingPointIsPartOfThePathNumberGrammarOnly()
    {
        // SVG path data allows "5." as a number. CSS numbers need a digit after the point.
        Assert.True(SvgNumberParser.TryReadNumber("5.", true, out float pathValue, out int pathLength));
        Assert.Equal(5F, pathValue);
        Assert.Equal(2, pathLength);

        Assert.True(SvgNumberParser.TryReadNumber("5.", false, out float cssValue, out int cssLength));
        Assert.Equal(5F, cssValue);
        Assert.Equal(1, cssLength);
    }

    [Theory]
    [InlineData(" 12.5 ", 12.5F)]
    [InlineData("\t-3\n", -3F)]
    [InlineData("1e2", 100F)]
    public void TryParseNumber_AcceptsOneNumberWithSurroundingWhitespace(string input, float expected)
    {
        Assert.True(SvgNumberParser.TryParseNumber(input, out float value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("1 2")]
    [InlineData("1px")]
    [InlineData("")]
    [InlineData("10-20")]
    public void TryParseNumber_RejectsAnythingButOneNumber(string input)
        => Assert.False(SvgNumberParser.TryParseNumber(input, out _));

    [Theory]
    [InlineData("0.5", 0.5F)]
    [InlineData("50%", 0.5F)]
    [InlineData("1.5", 1F)]
    [InlineData("-1", 0F)]
    [InlineData("150%", 1F)]
    [InlineData(" 25% ", 0.25F)]
    public void TryParseClampedFraction_ReadsNumbersAndPercentagesClampedToTheUnitRange(string input, float expected)
    {
        Assert.True(SvgNumberParser.TryParseClampedFraction(input, out float value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("50 %")]
    [InlineData("50%%")]
    [InlineData("0.5px")]
    public void TryParseClampedFraction_RejectsOtherValues(string input)
        => Assert.False(SvgNumberParser.TryParseClampedFraction(input, out _));

    [Theory]
    [InlineData("0 0 100 50", 0F, 0F, 100F, 50F)]
    [InlineData("0,0,100,50", 0F, 0F, 100F, 50F)]
    [InlineData(" -10 , -20 30 40 ", -10F, -20F, 30F, 40F)]
    [InlineData("0 0 0 10", 0F, 0F, 0F, 10F)]
    public void TryParseViewBox_ReadsFourNumbers(string input, float x, float y, float width, float height)
    {
        Assert.True(SvgNumberParser.TryParseViewBox(input, out RectangleF value));
        Assert.Equal(new RectangleF(x, y, width, height), value);
    }

    // SVG 2: a negative width or height is an error that invalidates the attribute.
    [Theory]
    [InlineData("0 0 -1 10")]
    [InlineData("0 0 10 -1")]
    [InlineData("0 0 100")]
    [InlineData("0 0 100 50 1")]
    [InlineData("0 0 100 50,")]
    [InlineData("0,,0 100 50")]
    [InlineData("a b c d")]
    [InlineData("")]
    public void TryParseViewBox_RejectsInvalidValues(string input)
        => Assert.False(SvgNumberParser.TryParseViewBox(input, out _));

    // CSS Values 4: 1turn = 360deg = 400grad = 2π rad. SVG attributes take a number without a unit as degrees.
    [Theory]
    [InlineData("30", 30F)]
    [InlineData("-45", -45F)]
    [InlineData("30deg", 30F)]
    [InlineData("40grad", 36F)]
    [InlineData("0.25turn", 90F)]
    [InlineData("1.5rad", 85.943665F)]
    [InlineData(" 1.5RAD ", 85.943665F)]
    [InlineData("9999", 9999F)]
    public void TryParseAngle_ReadsUnitsAndUnitlessDegrees(string input, float expected)
    {
        Assert.True(SvgNumberParser.TryParseAngle(input, out float degrees));
        Assert.Equal(expected, degrees, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("deg")]
    [InlineData("30 deg")]
    [InlineData("30px")]
    [InlineData("30%")]
    [InlineData("30degx")]
    public void TryParseAngle_RejectsOtherValues(string input)
        => Assert.False(SvgNumberParser.TryParseAngle(input, out _));

    // CSS allows an angle without a unit only for zero.
    [Fact]
    public void TryReadAngle_CssFormRequiresAUnitUnlessZero()
    {
        Assert.False(SvgNumberParser.TryReadAngle("45", false, out _, out _));
        Assert.True(SvgNumberParser.TryReadAngle("0", false, out float zero, out int length));
        Assert.Equal(0F, zero);
        Assert.Equal(1, length);
    }

    [Fact]
    public void TryParsePoints_ReadsCoordinatePairs()
    {
        List<PointF> points = [];

        Assert.True(SvgNumberParser.TryParsePoints(" 10,20 30 40,50-60 ", points));
        Assert.Equal([new PointF(10, 20), new PointF(30, 40), new PointF(50, -60)], points);
    }

    [Fact]
    public void TryParsePoints_DropsAnOddFinalValue()
    {
        List<PointF> points = [];

        Assert.False(SvgNumberParser.TryParsePoints("10,20 30,40 50", points));
        Assert.Equal([new PointF(10, 20), new PointF(30, 40)], points);
    }

    [Fact]
    public void TryParsePoints_KeepsThePairsBeforeTheFirstError()
    {
        List<PointF> points = [];

        Assert.False(SvgNumberParser.TryParsePoints("10,20 30,40 50,x 70,80", points));
        Assert.Equal([new PointF(10, 20), new PointF(30, 40)], points);
    }
}
