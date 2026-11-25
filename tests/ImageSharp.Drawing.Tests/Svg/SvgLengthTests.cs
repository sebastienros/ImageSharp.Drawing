// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

public class SvgLengthTests
{
    [Theory]
    [InlineData("12", 12F, (int)SvgLengthUnit.Number)]
    [InlineData("12px", 12F, (int)SvgLengthUnit.Px)]
    [InlineData("12pt", 12F, (int)SvgLengthUnit.Pt)]
    [InlineData("12pc", 12F, (int)SvgLengthUnit.Pc)]
    [InlineData("12mm", 12F, (int)SvgLengthUnit.Mm)]
    [InlineData("12cm", 12F, (int)SvgLengthUnit.Cm)]
    [InlineData("12in", 12F, (int)SvgLengthUnit.In)]
    [InlineData("12em", 12F, (int)SvgLengthUnit.Em)]
    [InlineData("12ex", 12F, (int)SvgLengthUnit.Ex)]
    [InlineData("12%", 12F, (int)SvgLengthUnit.Percent)]
    [InlineData("12PX", 12F, (int)SvgLengthUnit.Px)]
    [InlineData("1.5Em", 1.5F, (int)SvgLengthUnit.Em)]
    [InlineData("-5mm", -5F, (int)SvgLengthUnit.Mm)]
    [InlineData("1e2px", 100F, (int)SvgLengthUnit.Px)]
    [InlineData("1e1em", 10F, (int)SvgLengthUnit.Em)]
    [InlineData(" .5in ", 0.5F, (int)SvgLengthUnit.In)]
    public void TryParse_KeepsValueAndUnit(string input, float value, int unit)
    {
        Assert.True(SvgLength.TryParse(input, out SvgLength length));
        Assert.Equal(value, length.Value);
        Assert.Equal((SvgLengthUnit)unit, length.Unit);
        Assert.True(length.IsSpecified);
    }

    [Theory]
    [InlineData("")]
    [InlineData("px")]
    [InlineData("10 px")]
    [InlineData("10%%")]
    [InlineData("10xx")]
    [InlineData("10p")]
    [InlineData("10pxx")]
    [InlineData("10 20")]
    [InlineData("1e39px")]
    [InlineData("auto")]
    public void TryParse_RejectsInvalidLengths(string input)
    {
        Assert.False(SvgLength.TryParse(input, out SvgLength length));
        Assert.False(length.IsSpecified);
    }

    [Fact]
    public void Default_IsUnspecified() => Assert.False(default(SvgLength).IsSpecified);

    [Fact]
    public void TryParseNonNegative_RejectsNegativeLengths()
    {
        Assert.True(SvgLength.TryParseNonNegative("0", out _));
        Assert.False(SvgLength.TryParseNonNegative("-1px", out SvgLength length));
        Assert.False(length.IsSpecified);
    }

    [Fact]
    public void TryParseNonNegativeList_ReadsCommaAndWhitespaceSeparatedLengths()
    {
        List<SvgLength> values = [new SvgLength(1, SvgLengthUnit.Px)];

        Assert.True(SvgLength.TryParseNonNegativeList("5, 10% 2em", values));
        Assert.Equal(
            [
                new SvgLength(1, SvgLengthUnit.Px),
                new SvgLength(5, SvgLengthUnit.Number),
                new SvgLength(10, SvgLengthUnit.Percent),
                new SvgLength(2, SvgLengthUnit.Em)
            ],
            values);
    }

    [Theory]
    [InlineData("5 -1")]
    [InlineData("5,,1")]
    [InlineData("5,")]
    [InlineData("5 x")]
    [InlineData("")]
    public void TryParseNonNegativeList_RejectsInvalidListsWithoutAppending(string input)
    {
        List<SvgLength> values = [];

        Assert.False(SvgLength.TryParseNonNegativeList(input, values));
        Assert.Empty(values);
    }
}
