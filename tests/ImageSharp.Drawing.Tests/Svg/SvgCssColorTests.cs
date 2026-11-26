// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// The color functions of CSS Color Levels 4 and 5. Every expected color is what the Chromium oracle
/// (tests/SvgTools/render-svg.js) paints for the value in <c>fill</c>, as an attribute and in CSS alike, read back from
/// the rendered PNG; values outside the sRGB gamut show that Chromium clips each channel. The one exception, rec2020,
/// says why it follows the specification instead.
/// </summary>
public class SvgCssColorTests
{
    [Theory]
    [InlineData("hwb(120 0% 50%)", 0, 128, 0, 255)]
    [InlineData("hwb(120deg 0% 50%)", 0, 128, 0, 255)]
    [InlineData("hwb(200 30% 40% / 0.5)", 78, 128, 153, 128)]
    [InlineData("hwb(0 60% 60%)", 128, 128, 128, 255)]
    [InlineData("lab(46.28 -47.55 48.59)", 0, 128, 0, 255)]
    [InlineData("lab(50 20 -30)", 133, 108, 170, 255)]
    [InlineData("lab(50% 50% -50%)", 176, 67, 228, 255)]
    [InlineData("lab(100 0 0)", 255, 255, 255, 255)]
    [InlineData("lab(-10 0 0)", 0, 0, 0, 255)]
    [InlineData("lab(46 -47 48 / 0.5)", 4, 128, 2, 128)]
    [InlineData("lch(46.28 67.98 134.39)", 0, 128, 0, 255)]
    [InlineData("lch(50 30 none)", 166, 98, 120, 255)]
    [InlineData("lch(50 -10 30)", 119, 119, 119, 255)]
    [InlineData("oklab(0.5199 -0.1405 0.1076)", 0, 128, 0, 255)]
    [InlineData("oklab(0.7 0.1 0.1)", 229, 127, 78, 255)]
    [InlineData("oklch(0.5199 0.1770 142.5)", 0, 128, 0, 255)]
    [InlineData("oklch(0.52 0.18 142.5deg)", 0, 128, 0, 255)]
    [InlineData("oklch(60% 50% 300)", 149, 91, 227, 255)]
    [InlineData("color(srgb 0 0.502 0)", 0, 128, 0, 255)]
    [InlineData("color(srgb 50% 25% 0% / 50%)", 128, 64, 0, 128)]
    [InlineData("color(srgb none 0.5 0)", 0, 128, 0, 255)]
    [InlineData("color(srgb-linear 0 0.216 0)", 0, 128, 0, 255)]
    [InlineData("color(srgb-linear 0.5 0.2 0.8)", 188, 124, 231, 255)]
    [InlineData("color(display-p3 0.3 0.6 0.9)", 36, 155, 236, 255)]
    [InlineData("color(xyz 0.2 0.3 0.4)", 0, 167, 164, 255)]
    [InlineData("color(xyz-d65 0.2 0.3 0.4)", 0, 167, 164, 255)]
    [InlineData("color(xyz-d50 0.2 0.3 0.4)", 0, 168, 189, 255)]
    [InlineData("color(a98-rgb 0.5 0.2 0.8)", 147, 48, 209, 255)]
    [InlineData("color(prophoto-rgb 0.5 0.2 0.8)", 157, 1, 227, 255)]
    [InlineData("rgb(none 128 none)", 0, 128, 0, 255)]
    [InlineData("hsl(none 100% 50%)", 255, 0, 0, 255)]
    public void TryParse_ReadsCssColor4Functions(string input, int r, int g, int b, int a)
        => AssertColor(input, r, g, b, a);

    // CSS Color 4 section 10.8 gives rec2020 the gamma 2.4 transfer function of BT.1886, which the sample code of section
    // 19 uses. The Chromium oracle still uses the older piecewise BT.2020 curve and paints 160, 44, 219 here; this
    // renderer follows the specification, whose sample code gives 139, 0, 211.
    [Fact]
    public void TryParse_Rec2020UsesTheTransferFunctionOfTheSpecification()
        => AssertColor("color(rec2020 0.5 0.2 0.8)", 139, 0, 211, 255);

    // Colors outside the sRGB gamut: Chromium clips each channel of the sRGB result (CSS Color 4 section 14 recommends a
    // chroma reduction instead; the oracle does not apply it).
    [Theory]
    [InlineData("lab(50 100 0)", 255, 0, 124, 255)]
    [InlineData("lch(60 120 30)", 255, 0, 50, 255)]
    [InlineData("oklab(70% 50% -50%)", 221, 77, 255, 255)]
    [InlineData("oklch(0.7 0.3 30)", 255, 0, 0, 255)]
    [InlineData("oklch(0.9 0.4 200)", 0, 255, 255, 255)]
    [InlineData("color(srgb 1.2 -0.1 0.5)", 255, 0, 128, 255)]
    [InlineData("color(display-p3 0 0.5 0)", 0, 130, 0, 255)]
    [InlineData("color(display-p3 1 0 0)", 255, 0, 0, 255)]
    [InlineData("color(display-p3 0 1 0)", 0, 255, 0, 255)]
    public void TryParse_ClipsColorsOutsideTheSrgbGamut(string input, int r, int g, int b, int a)
        => AssertColor(input, r, g, b, a);

    [Theory]
    [InlineData("color-mix(in srgb, #00ff00 50%, #000000)", 0, 128, 0, 255)]
    [InlineData("color-mix(in srgb, red, blue)", 128, 0, 128, 255)]
    [InlineData("color-mix(in oklab, red 30%, blue)", 93, 75, 200, 255)]
    [InlineData("color-mix(in lch, red, blue)", 245, 0, 134, 255)]
    [InlineData("color-mix(in hsl, red, blue)", 255, 0, 255, 255)]
    [InlineData("color-mix(in srgb, red 20%, blue 20%)", 128, 0, 128, 102)]
    [InlineData("color-mix(in srgb-linear, red, blue)", 188, 0, 188, 255)]
    [InlineData("color-mix(in oklch longer hue, red, blue)", 0, 147, 0, 255)]
    [InlineData("color-mix(red, blue)", 140, 83, 162, 255)]
    [InlineData("color-mix(in srgb, red 20%, blue)", 51, 0, 204, 255)]
    [InlineData("rgb(from green r g b)", 0, 128, 0, 255)]
    [InlineData("rgb(from green calc(r + 100) g b / 0.5)", 100, 128, 0, 128)]
    [InlineData("hsl(from red calc(h + 120) s l)", 0, 255, 0, 255)]
    [InlineData("oklch(from blue l c calc(h + 90))", 192, 0, 97, 255)]
    [InlineData("light-dark(green, red)", 0, 128, 0, 255)]
    public void TryParse_ReadsCssColor5Functions(string input, int r, int g, int b, int a)
        => AssertColor(input, r, g, b, a);

    [Theory]
    [InlineData("rgb(calc(0) calc(64 * 2) 0)", 0, 128, 0, 255)]
    [InlineData("rgb(calc(50% + 10%) 0 0)", 153, 0, 0, 255)]
    [InlineData("hsl(calc(60 * 2) 100% 25%)", 0, 128, 0, 255)]
    [InlineData("rgb(0 128 0 / calc(1 / 2))", 0, 128, 0, 128)]
    public void TryParse_ReadsMathFunctionsInChannels(string input, int r, int g, int b, int a)
        => AssertColor(input, r, g, b, a);

    // A missing alpha is zero, and two 0% colors mix into a transparent color (CSS Values 5 mix normalization).
    [Theory]
    [InlineData("rgb(0 128 0 / none)")]
    [InlineData("color-mix(in srgb, red 0%, blue 0%)")]
    public void TryParse_TransparentResults(string input)
    {
        Assert.True(SvgColorParser.TryParse(input, null, out Color color));
        Assert.Equal(0, color.ToPixel<Rgba32>().A);
    }

    // The oracle drops each of these (the CSS declaration is dropped, the attribute paints black).
    [Theory]
    [InlineData("hwb(120, 0%, 50%)")]
    [InlineData("lab(1, 2, 3)")]
    [InlineData("color(srgb 0 0.5)")]
    [InlineData("color(display-p3 0 0.5 0 0.1)")]
    [InlineData("rgb(0,128,0/50%)")]
    [InlineData("rgb(0 128 0, 0.5)")]
    [InlineData("rgb(none, 128, 0)")]
    [InlineData("color(foo 1 2 3)")]
    [InlineData("lab(50deg 0 0)")]
    [InlineData("lch(50 30 30%)")]
    [InlineData("color-mix(in srgb, red)")]
    [InlineData("color-mix(in srgb, red, blue, lime)")]
    [InlineData("color-mix(in srgb, red 120%, blue)")]
    [InlineData("color-mix(in foo, red, blue)")]
    [InlineData("color-mix(in srgb longer hue, red, blue)")]
    [InlineData("rgb(from nothing r g b)")]
    [InlineData("rgb(from red x g b)")]
    [InlineData("light-dark(red)")]
    public void TryParse_RejectsInvalidColorFunctions(string input)
        => Assert.False(SvgColorParser.TryParse(input, null, out _));

    // Nesting is bounded, so a hostile value cannot exhaust the stack.
    [Fact]
    public void TryParse_BoundsTheNestingOfColorFunctions()
    {
        string shallow = Nest(SvgColorParser.MaxNesting - 1);
        string deep = Nest(10_000);

        Assert.True(SvgColorParser.TryParse(shallow, null, out _));
        Assert.False(SvgColorParser.TryParse(deep, null, out _));

        static string Nest(int depth)
        {
            string value = "red";
            for (int i = 0; i < depth; i++)
            {
                value = $"color-mix(in srgb, {value}, blue)";
            }

            return value;
        }
    }

    private static void AssertColor(string input, int r, int g, int b, int a)
    {
        Assert.True(SvgColorParser.TryParse(input, null, out Color color), input);
        Rgba32 actual = color.ToPixel<Rgba32>();

        // The oracle rounds its 8-bit output; one level of difference is rounding.
        Assert.True(
            Math.Abs(actual.R - r) <= 1 && Math.Abs(actual.G - g) <= 1 && Math.Abs(actual.B - b) <= 1 && Math.Abs(actual.A - a) <= 1,
            $"{input}: expected {r},{g},{b},{a}, got {actual.R},{actual.G},{actual.B},{actual.A}");
    }
}
