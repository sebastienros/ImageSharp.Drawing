// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

public class SvgPreserveAspectRatioTests
{
    [Theory]
    [InlineData("none", (int)SvgAlign.None, false)]
    [InlineData("xMinYMin", (int)SvgAlign.XMinYMin, false)]
    [InlineData("xMidYMin", (int)SvgAlign.XMidYMin, false)]
    [InlineData("xMaxYMin", (int)SvgAlign.XMaxYMin, false)]
    [InlineData("xMinYMid", (int)SvgAlign.XMinYMid, false)]
    [InlineData("xMidYMid", (int)SvgAlign.XMidYMid, false)]
    [InlineData("xMaxYMid", (int)SvgAlign.XMaxYMid, false)]
    [InlineData("xMinYMax", (int)SvgAlign.XMinYMax, false)]
    [InlineData("xMidYMax", (int)SvgAlign.XMidYMax, false)]
    [InlineData("xMaxYMax", (int)SvgAlign.XMaxYMax, false)]
    [InlineData("xMinYMin meet", (int)SvgAlign.XMinYMin, false)]
    [InlineData("xMaxYMax slice", (int)SvgAlign.XMaxYMax, true)]
    [InlineData("  xMidYMid   slice ", (int)SvgAlign.XMidYMid, true)]
    [InlineData("defer xMinYMax slice", (int)SvgAlign.XMinYMax, true)]
    [InlineData("none slice", (int)SvgAlign.None, true)]
    public void TryParse_ReadsAlignAndMeetOrSlice(string input, int align, bool slice)
    {
        Assert.True(SvgPreserveAspectRatio.TryParse(input, out SvgPreserveAspectRatio value));
        Assert.Equal((SvgAlign)align, value.Align);
        Assert.Equal(slice, value.Slice);
    }

    [Fact]
    public void Default_IsXMidYMidMeet()
    {
        SvgPreserveAspectRatio value = default;

        Assert.Equal(SvgAlign.XMidYMid, value.Align);
        Assert.False(value.Slice);
    }

    [Theory]
    [InlineData("")]
    [InlineData("meet")]
    [InlineData("XMIDYMID")]
    [InlineData("xMidYMid foo")]
    [InlineData("xMidYMid meet slice")]
    [InlineData("xMidYMid,slice")]
    [InlineData("xMidYMidslice")]
    [InlineData("defer")]
    public void TryParse_RejectsInvalidValues(string input)
        => Assert.False(SvgPreserveAspectRatio.TryParse(input, out _));
}
