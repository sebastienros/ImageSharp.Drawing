// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests the filter primitives that read neighboring pixels or generate noise (<c>feMorphology</c>,
/// <c>feConvolveMatrix</c>, <c>feDisplacementMap</c>, <c>feTurbulence</c>) on small renders whose pixels follow from the
/// formulas of Filter Effects Level 1. The filters use <c>color-interpolation-filters="sRGB"</c> where the values must
/// be exact, so that no color space conversion rounds them.
/// </summary>
public class SvgFilterSpatialTests
{
    private const string SvgOpen =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\">";

    // The user space region of the whole 20 x 20 image.
    private const string Region = "filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\" color-interpolation-filters=\"sRGB\"";

    // A black 5 x 5 square from 10 to 15.
    private const string Square = "<rect x=\"10\" y=\"10\" width=\"5\" height=\"5\" filter=\"url(#f)\"/>";

    [Theory]
    [InlineData("1", 11, 14, 11, 14)]
    [InlineData("1\" operator=\"dilate", 9, 16, 9, 16)]
    [InlineData("2 0\" operator=\"dilate", 8, 17, 10, 15)]
    [InlineData("0.5", 11, 14, 11, 14)]
    [InlineData("0.4", 10, 15, 10, 15)]
    [InlineData("10 -5\" operator=\"dilate", 0, 20, 10, 15)]
    public void Morphology_ErodesAndDilatesByWholePixels(string radius, int left, int right, int top, int bottom)
    {
        // The window is 2 r + 1 pixels, with r rounded to whole pixels (halves up); a negative or zero radius leaves its
        // axis unchanged.
        using Image<Rgba32> image = Render($"<filter id=\"f\" {Region}><feMorphology radius=\"{radius}\"/></filter>" + Square);

        AssertOpaqueSpan(image, left, right, top, bottom);
    }

    [Theory]
    [InlineData("")]
    [InlineData("radius=\"0\"")]
    [InlineData("radius=\"-3\"")]
    [InlineData("radius=\"1 2 3\"")]
    [InlineData("radius=\"0 0\" operator=\"dilate\"")]
    public void Morphology_WithoutAPositiveRadiusPassesItsInputThrough(string attributes)
    {
        using Image<Rgba32> image = Render($"<filter id=\"f\" {Region}><feMorphology {attributes}/></filter>" + Square);

        AssertOpaqueSpan(image, 10, 15, 10, 15);
    }

    [Theory]
    [InlineData("erode", 0, 0, 0)]
    [InlineData("dilate", 255, 0, 255)]
    public void Morphology_WorksPerPremultipliedChannel(string op, byte r, byte g, byte b)
    {
        // Red meets blue at x = 10. A window over both takes the minimum (black) or the maximum (magenta) per channel.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feMorphology operator=\"{op}\" radius=\"1\"/></filter>"
            + "<g filter=\"url(#f)\"><rect width=\"10\" height=\"20\" fill=\"#ff0000\"/><rect x=\"10\" width=\"10\" height=\"20\" fill=\"#0000ff\"/></g>");

        AssertPixel(new Rgba32(r, g, b, 255), image[9, 10]);
        AssertPixel(new Rgba32(r, g, b, 255), image[10, 10]);
        AssertPixel(new Rgba32(255, 0, 0, 255), image[5, 10]);
        AssertPixel(new Rgba32(0, 0, 255, 255), image[15, 10]);
    }

    [Fact]
    public void Morphology_WithAHugeRadiusFinishes()
    {
        // The cost per pixel does not depend on the radius. Erosion leaves nothing; dilation spreads the square over the
        // whole region.
        using Image<Rgba32> eroded = Render($"<filter id=\"f\" {Region}><feMorphology radius=\"1e30\"/></filter>" + Square);
        using Image<Rgba32> dilated = Render($"<filter id=\"f\" {Region}><feMorphology operator=\"dilate\" radius=\"1e30\"/></filter>" + Square);

        Assert.Equal(0, eroded[12, 12].A);
        Assert.Equal(255, dilated[0, 0].A);
        Assert.Equal(255, dilated[19, 19].A);
    }

    [Theory]
    [InlineData("kernelMatrix=\"0 0 0 0 1 0 0 0 0\"", 10, 15)]
    [InlineData("order=\"3 1\" kernelMatrix=\"1 0 0\"", 9, 14)]
    [InlineData("order=\"3 1\" kernelMatrix=\"1 0 0\" targetX=\"0\"", 8, 13)]
    [InlineData("order=\"3.9 1\" kernelMatrix=\"0 0 1\"", 11, 16)]
    public void ConvolveMatrix_RotatesTheKernelAgainstTheImage(string attributes, int left, int right)
    {
        // result(x) = sum source(x - targetX + j) * kernelMatrix[orderX - j - 1]: the first entry of "1 0 0" weighs the
        // pixel orderX - 1 - targetX to the right, so the square moves left by one (two with targetX 0); "0 0 1" moves
        // it right. An order of 3.9 truncates to 3.
        using Image<Rgba32> image = Render($"<filter id=\"f\" {Region}><feConvolveMatrix {attributes}/></filter>" + Square);

        AssertOpaqueSpan(image, left, right, 10, 15);
    }

    [Theory]
    [InlineData("duplicate", 0, 0, 255)]
    [InlineData("wrap", 255, 0, 0)]
    [InlineData("invalid", 0, 0, 255)]
    public void ConvolveMatrix_ExtendsTheImageAtTheSubregionEdges(string edgeMode, byte r, byte g, byte b)
    {
        // A red column at x = 0 and a blue one at x = 19; the output at 19 reads x = 20, beyond the edge.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feConvolveMatrix order=\"3 1\" kernelMatrix=\"1 0 0\" edgeMode=\"{edgeMode}\"/></filter>"
            + "<g filter=\"url(#f)\"><rect width=\"1\" height=\"20\" fill=\"#ff0000\"/><rect x=\"19\" width=\"1\" height=\"20\" fill=\"#0000ff\"/></g>");

        AssertPixel(new Rgba32(r, g, b, 255), image[19, 10]);
        AssertPixel(new Rgba32(0, 0, 255, 255), image[18, 10]);
    }

    [Fact]
    public void ConvolveMatrix_WithEdgeModeNoneReadsTransparentBlackOutsideTheInput()
    {
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feConvolveMatrix order=\"3 1\" kernelMatrix=\"1 0 0\" edgeMode=\"none\"/></filter>"
            + "<rect width=\"20\" height=\"20\" fill=\"#0000ff\" filter=\"url(#f)\"/>");

        Assert.Equal(0, image[19, 10].A);
        Assert.Equal(255, image[18, 10].A);
    }

    [Theory]
    [InlineData("", 255, 128)]
    [InlineData("divisor=\"0\"", 255, 255)]
    [InlineData("divisor=\"9\" bias=\"0.25\"", 255, 255)]
    public void ConvolveMatrix_DividesAndAddsTheBias(string attributes, byte red, byte alpha)
    {
        // A 3 x 3 kernel of 1 over opaque red: the default divisor is the sum, 9, and keeps red; a divisor of 18 (set
        // below for the first case) halves the premultiplied values; a divisor of 0 is the default. The bias adds to
        // every premultiplied channel.
        string divisor = attributes.Length == 0 ? "divisor=\"18\"" : attributes;
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feConvolveMatrix kernelMatrix=\"1 1 1 1 1 1 1 1 1\" {divisor}/></filter>"
            + "<rect width=\"20\" height=\"20\" fill=\"#ff0000\" filter=\"url(#f)\"/>");

        Assert.InRange(image[10, 10].R, red - 1, red);
        Assert.InRange(image[10, 10].A, alpha - 1, alpha);
        if (attributes.Contains("bias"))
        {
            // Premultiplied (1, 0, 0, 1) + 0.25 clamps to (1, 0.25, 0.25, 1).
            AssertPixel(new Rgba32(255, 64, 64, 255), image[10, 10]);
        }
    }

    [Theory]
    [InlineData("false", 100, 50, 0, 255)]
    [InlineData("true", 200, 100, 0, 128)]
    public void ConvolveMatrix_WithPreserveAlphaConvolvesUnpremultipliedColors(string preserveAlpha, byte r, byte g, byte b, byte a)
    {
        // A kernel of 2 at the center with divisor 1 over rgba(100, 50, 0, 0.5): premultiplied, every channel doubles
        // and the straight color stays; with preserveAlpha the straight color doubles and the alpha stays.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feConvolveMatrix kernelMatrix=\"0 0 0 0 2 0 0 0 0\" divisor=\"1\" preserveAlpha=\"{preserveAlpha}\"/></filter>"
            + "<rect width=\"20\" height=\"20\" fill=\"rgb(100, 50, 0)\" fill-opacity=\"0.5\" filter=\"url(#f)\"/>");

        AssertPixel(new Rgba32(r, g, b, a), image[10, 10]);
    }

    [Fact]
    public void ConvolveMatrix_TakesADivisorOfOneForAKernelThatSumsToZero()
    {
        // Eight times 0.1 and one -0.8 sum to 0 as decimals, though not as floats: over a uniform area the result is 0.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feConvolveMatrix kernelMatrix=\"0.1 0.1 0.1 0.1 0.1 0.1 0.1 0.1 -0.8\"/></filter>"
            + "<rect width=\"20\" height=\"20\" fill=\"#ff0000\" filter=\"url(#f)\"/>");

        Assert.Equal(0, image[10, 10].A);
    }

    [Theory]
    [InlineData("kernelMatrix=\"1 1 1 1 1 1 1 1\"")]
    [InlineData("kernelMatrix=\"\"")]
    [InlineData("order=\"3\"")]
    [InlineData("order=\"0\" kernelMatrix=\"1\"")]
    [InlineData("order=\"4 -2\" kernelMatrix=\"1 1 1 1 1 1 1 1\"")]
    [InlineData("kernelMatrix=\"1 1 1 1 1 1 1 1 1\" targetX=\"3\"")]
    [InlineData("kernelMatrix=\"1 1 1 1 1 1 1 1 1\" targetY=\"-1\"")]
    public void ConvolveMatrix_WithAnUnfulfilledPreconditionPassesItsInputThrough(string attributes)
    {
        // Filter Effects Level 1: the primitive acts as a pass-through filter.
        using Image<Rgba32> image = Render($"<filter id=\"f\" {Region}><feConvolveMatrix {attributes}/></filter>" + Square);

        AssertOpaqueSpan(image, 10, 15, 10, 15);
    }

    [Fact]
    public void ConvolveMatrix_AboveTheKernelLimitGivesTransparentBlack()
    {
        string kernel = string.Join(' ', Enumerable.Repeat("1", 17 * 17));
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feFlood flood-color=\"#00ff00\" result=\"green\"/>"
            + $"<feConvolveMatrix in=\"SourceGraphic\" order=\"17\" kernelMatrix=\"{kernel}\" result=\"large\"/>"
            + "<feMerge><feMergeNode in=\"green\"/><feMergeNode in=\"large\"/></feMerge></filter>"
            + Square);

        AssertPixel(new Rgba32(0, 255, 0, 255), image[12, 12]);
    }

    [Theory]
    [InlineData("flood-color=\"#000000\"", "", 4, 8, 8)]
    [InlineData("flood-color=\"rgb(153, 128, 0)\" flood-opacity=\"0.5\"", "xChannelSelector=\"R\" yChannelSelector=\"G\"", 10, 9, 10)]
    [InlineData("flood-color=\"rgb(188, 128, 0)\"", "xChannelSelector=\"R\" yChannelSelector=\"G\"", 10, 8, 10)]
    public void DisplacementMap_MovesByTheUnpremultipliedMap(string flood, string selectors, float scale, int left, int top)
    {
        // P'(x) = P(x + scale (C - 0.5)), the nearest pixel to the position. Alpha 1 moves by half the scale; red 153 at
        // half opacity is 0.6 unpremultiplied and moves 10 * 0.1 = 1; red 188 is 0.737 and moves 2.4, to 2. Green 128
        // stays within half a pixel.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feFlood {flood} result=\"map\"/>"
            + $"<feDisplacementMap in=\"SourceGraphic\" in2=\"map\" scale=\"{scale}\" {selectors}/></filter>"
            + Square);

        AssertOpaqueSpan(image, left, left + 5, top, top + 5);
    }

    [Fact]
    public void DisplacementMap_ReadsTheMapInTheColorSpaceOfThePrimitive()
    {
        // In linearRGB, red 188 is 0.503, which moves 10 * 0.003 by less than half a pixel; in sRGB it moves by 2.
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\">"
            + "<feFlood flood-color=\"rgb(188, 188, 0)\" result=\"map\"/>"
            + "<feDisplacementMap in=\"SourceGraphic\" in2=\"map\" scale=\"10\" xChannelSelector=\"R\" yChannelSelector=\"G\"/></filter>"
            + Square);

        AssertOpaqueSpan(image, 10, 15, 10, 15);
    }

    [Fact]
    public void DisplacementMap_KeepsTheColorsOfItsInput()
    {
        // The primitive works in linearRGB, but the displaced image is not converted, so a dark color comes out exact
        // (through 8-bit linear light it would become 13 instead of 10).
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\">"
            + "<feFlood flood-color=\"rgb(188, 188, 0)\" result=\"map\"/>"
            + "<feDisplacementMap in=\"SourceGraphic\" in2=\"map\" scale=\"10\" xChannelSelector=\"R\" yChannelSelector=\"G\"/></filter>"
            + "<rect width=\"20\" height=\"20\" fill=\"rgb(10, 20, 30)\" filter=\"url(#f)\"/>");

        Assert.Equal(new Rgba32(10, 20, 30, 255), image[10, 10]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("scale=\"0\"")]
    [InlineData("scale=\"1 2\"")]
    public void DisplacementMap_WithoutAScalePassesItsInputThrough(string attributes)
    {
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feFlood result=\"map\"/><feDisplacementMap in=\"SourceGraphic\" in2=\"map\" {attributes}/></filter>" + Square);

        AssertOpaqueSpan(image, 10, 15, 10, 15);
    }

    [Fact]
    public void DisplacementMap_WithAHugeScaleMovesEverythingOut()
    {
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feFlood result=\"map\"/><feDisplacementMap in=\"SourceGraphic\" in2=\"map\" scale=\"1e30\"/></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        Assert.Equal(0, image[10, 10].A);
    }

    [Fact]
    public void Turbulence_RandomNumbersFollowTheReferenceGenerator()
    {
        // Filter Effects: the generator produces 1043618065 as the 10,000th number from the seed 1.
        long value = 1;
        for (int i = 0; i < 10000; i++)
        {
            value = SvgFeTurbulence.Random(value);
        }

        Assert.Equal(1043618065, value);
    }

    [Theory]
    [InlineData(20, "1")]
    [InlineData(40, "4")]
    public void Turbulence_IsZeroAtTheLatticePoints(int size, string frequency)
    {
        // The gradient noise is 0 at integer lattice points, so fractal noise is (0 + 1) / 2 there. Filter pixel i
        // samples the user point of its center plus half a unit: at scale 1 that is i + 1, a lattice point for a
        // frequency of 1; at scale 2 it is i / 2 + 0.75, a lattice point for a frequency of 4.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feTurbulence type=\"fractalNoise\" baseFrequency=\"{frequency}\" numOctaves=\"3\"/></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>",
            size,
            size);

        for (int y = 0; y < size; y += 3)
        {
            for (int x = 0; x < size; x += 3)
            {
                AssertPixel(new Rgba32(128, 128, 128, 128), image[x, y]);
            }
        }
    }

    [Theory]
    [InlineData("type=\"fractalNoise\"", 128)]
    [InlineData("", 0)]
    [InlineData("baseFrequency=\"-0.1\" type=\"fractalNoise\"", 0)]
    [InlineData("baseFrequency=\"0.1\" numOctaves=\"0\"", 0)]
    public void Turbulence_GivesConstantsWithoutFrequencyOrOctaves(string attributes, byte alpha)
    {
        // A frequency of 0 makes the noise 0: transparent for turbulence, half gray at half alpha for fractal noise. A
        // negative frequency is unsupported and gives transparent black; no octave sums nothing.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feTurbulence {attributes}/></filter><rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        Assert.Equal(alpha, image[7, 13].A);
        if (alpha != 0)
        {
            AssertPixel(new Rgba32(128, 128, 128, 128), image[7, 13]);
        }
    }

    [Theory]
    [InlineData("seed=\"0\"", "seed=\"1\"", true)]
    [InlineData("seed=\"1.9\"", "seed=\"1\"", true)]
    [InlineData("seed=\"-1.9\"", "seed=\"-1\"", true)]
    [InlineData("seed=\"2\"", "seed=\"1\"", false)]
    [InlineData("numOctaves=\"40\"", "numOctaves=\"9\"", true)]
    [InlineData("numOctaves=\"1.5\"", "numOctaves=\"1\"", true)]
    [InlineData("type=\"invalid\"", "type=\"turbulence\"", true)]
    public void Turbulence_ReadsItsAttributes(string first, string second, bool same)
    {
        // Seeds truncate toward zero, and the setup maps 0 and 1 to the same generator state; the octaves clamp to 9; an
        // invalid value takes the initial value.
        using Image<Rgba32> a = Render(
            $"<filter id=\"f\" {Region}><feTurbulence baseFrequency=\"0.13\" {first}/></filter><rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");
        using Image<Rgba32> b = Render(
            $"<filter id=\"f\" {Region}><feTurbulence baseFrequency=\"0.13\" {second}/></filter><rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        bool equal = true;
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                equal &= a[x, y] == b[x, y];
            }
        }

        Assert.Equal(same, equal);
    }

    /// <summary>
    /// Renders a 20 x 20 document with the given content into an image of the given size.
    /// </summary>
    private static Image<Rgba32> Render(string content, int width = 20, int height = 20)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + content + "</svg>");
        Image<Rgba32> image = new(width, height);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, width, height))));
        return image;
    }

    /// <summary>
    /// Asserts that the opaque pixels along the middle row and column of a span are exactly the span.
    /// </summary>
    private static void AssertOpaqueSpan(Image<Rgba32> image, int left, int right, int top, int bottom)
    {
        int row = (top + bottom) / 2;
        int column = (left + right) / 2;
        for (int x = 0; x < image.Width; x++)
        {
            Assert.True((image[x, row].A == 255) == (x >= left && x < right), $"column {x} of row {row}: alpha {image[x, row].A}");
        }

        for (int y = 0; y < image.Height; y++)
        {
            Assert.True((image[column, y].A == 255) == (y >= top && y < bottom), $"row {y} of column {column}: alpha {image[column, y].A}");
        }
    }

    /// <summary>
    /// Asserts that a pixel equals the expected one within one 8-bit level per channel.
    /// </summary>
    private static void AssertPixel(Rgba32 expected, Rgba32 actual)
    {
        Assert.InRange(actual.A, expected.A - 1, expected.A + 1);
        Assert.InRange(actual.R, expected.R - 1, expected.R + 1);
        Assert.InRange(actual.G, expected.G - 1, expected.G + 1);
        Assert.InRange(actual.B, expected.B - 1, expected.B + 1);
    }
}
