// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests the lighting filter primitives, <c>feDiffuseLighting</c> and <c>feSpecularLighting</c>, and their light sources
/// on small renders whose pixels follow from the equations and the surface normal kernels of Filter Effects Level 1.
/// </summary>
/// <remarks>
/// The documents draw at one pixel per user unit unless a test says otherwise, so filter pixel (x, y) has its center at
/// user point (x + 0.5, y + 0.5). Most filters run in sRGB, so the lit values are the 8-bit result of the equations.
/// </remarks>
public class SvgFilterLightingTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\" viewBox=\"0 0 20 20\">";

    // A 3 x 3 alpha grid at (5, 5): every pixel of a 3 x 3 filter region is a corner, an edge, or the interior pixel.
    private const string Grid =
        "<g filter=\"url(#f)\">"
        + "<rect x=\"6\" y=\"5\" width=\"1\" height=\"1\" fill-opacity=\"0.2\"/>"
        + "<rect x=\"7\" y=\"5\" width=\"1\" height=\"1\" fill-opacity=\"0.6\"/>"
        + "<rect x=\"5\" y=\"6\" width=\"1\" height=\"1\" fill-opacity=\"0.4\"/>"
        + "<rect x=\"6\" y=\"6\" width=\"1\" height=\"1\" fill-opacity=\"0.6\"/>"
        + "<rect x=\"7\" y=\"6\" width=\"1\" height=\"1\"/>"
        + "<rect x=\"5\" y=\"7\" width=\"1\" height=\"1\" fill-opacity=\"0.2\"/>"
        + "<rect x=\"6\" y=\"7\" width=\"1\" height=\"1\" fill-opacity=\"0.8\"/>"
        + "<rect x=\"7\" y=\"7\" width=\"1\" height=\"1\" fill-opacity=\"0.4\"/>"
        + "</g>";

    private const string GridRegion = "filterUnits=\"userSpaceOnUse\" x=\"5\" y=\"5\" width=\"3\" height=\"3\" color-interpolation-filters=\"sRGB\"";

    private const string Region = "filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\" color-interpolation-filters=\"sRGB\"";

    /// <summary>
    /// Gets the lit values of the alpha grid
    ///   0.0 0.2 0.6
    ///   0.4 0.6 1.0
    ///   0.2 0.8 0.4
    /// with surfaceScale 1, under a distant light at elevation 45 from azimuth 0 (L = (0.7071, 0, 0.7071)) and from
    /// azimuth 90 (L = (0, 0.7071, 0.7071)). With the unnormalized normal (Nx, Ny, 1) the value is
    /// 255 * 0.7071 * (Nx + 1) / Norm(Nx, Ny, 1) for azimuth 0 and the same with Ny for azimuth 90.
    /// The normals come from the kernel tables of the specification, for example:
    /// top left corner, Nx = -2/3 * (-2 * 0 + 2 * 0.2 - 0.4 + 0.6) = -0.4 and Ny = -2/3 * (-2 * 0 - 0.2 + 2 * 0.4 + 0.6) = -0.8;
    /// interior, Nx = -1/4 * (-0 + 0.6 - 2 * 0.4 + 2 * 1 - 0.2 + 0.4) = -0.5 and Ny = -1/4 * (-0 - 2 * 0.2 - 0.6 + 0.2 + 2 * 0.8 + 0.4) = -0.3;
    /// bottom right corner, Nx = -2/3 * (-0.6 + 1 - 2 * 0.8 + 2 * 0.4) = 0.2667 and Ny = -2/3 * (-0.6 - 2 * 1 + 0.8 + 2 * 0.4) = 0.6667.
    /// </summary>
    public static TheoryData<int, int, int, int> GridValues { get; } = new()
    {
        // x, y, azimuth 0, azimuth 90. Corners, edges, and the interior pixel.
        { 0, 0, 81, 27 },   // top left: N = (-0.4, -0.8)
        { 1, 0, 51, 26 },   // top row: N = (-0.6, -0.8)
        { 2, 0, 24, 24 },   // top right: N = (-0.8, -0.8)
        { 0, 1, 59, 99 },   // left column: N = (-0.6, -0.3333)
        { 1, 1, 78, 109 },  // interior: N = (-0.5, -0.3)
        { 2, 1, 100, 156 }, // right column: N = (-0.4, -0.0667)
        { 0, 2, 9, 149 },   // bottom left: N = (-0.9333, 0.1333)
        { 1, 2, 112, 202 }, // bottom row: N = (-0.3333, 0.2)
        { 2, 2, 186, 244 }, // bottom right: N = (0.2667, 0.6667)
    };

    [Theory]
    [MemberData(nameof(GridValues))]
    public void Diffuse_UsesTheInteriorEdgeAndCornerKernelsOfTheSpecification(int x, int y, int azimuth0, int azimuth90)
    {
        using Image<Rgba32> fromX = Render(
            $"<filter id=\"f\" {GridRegion}><feDiffuseLighting><feDistantLight azimuth=\"0\" elevation=\"45\"/></feDiffuseLighting></filter>" + Grid);
        using Image<Rgba32> fromY = Render(
            $"<filter id=\"f\" {GridRegion}><feDiffuseLighting><feDistantLight azimuth=\"90\" elevation=\"45\"/></feDiffuseLighting></filter>" + Grid);

        AssertGray(azimuth0, fromX[5 + x, 5 + y]);
        AssertGray(azimuth90, fromY[5 + x, 5 + y]);
    }

    [Fact]
    public void Diffuse_TreatsTheSubregionAsTheImageEdge()
    {
        // The input is opaque everywhere, also outside the subregion, and the data ends at its edge in the second render.
        // Neither the input beyond the subregion nor the transparent pixels beyond the data reach the edge kernels, so
        // the surface is flat: N.L = sin(45 degrees) = 0.7071, 180.
        const string light = "<feDistantLight azimuth=\"0\" elevation=\"45\"/>";
        using Image<Rgba32> wide = Render(
            $"<filter id=\"f\" {Region}><feDiffuseLighting x=\"5\" y=\"5\" width=\"10\" height=\"10\">{light}</feDiffuseLighting></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");
        using Image<Rgba32> exact = Render(
            $"<filter id=\"f\" {Region}><feDiffuseLighting x=\"5\" y=\"5\" width=\"10\" height=\"10\">{light}</feDiffuseLighting></filter>"
            + "<rect x=\"5\" y=\"5\" width=\"10\" height=\"10\" filter=\"url(#f)\"/>");

        foreach (Image<Rgba32> image in new[] { wide, exact })
        {
            AssertGray(180, image[5, 10]);
            AssertGray(180, image[14, 10]);
            AssertGray(180, image[10, 5]);
            AssertGray(180, image[5, 5]);
            Assert.Equal(0, image[4, 10].A);
            Assert.Equal(0, image[15, 10].A);
        }
    }

    [Fact]
    public void Diffuse_SeesTheEdgeOfTheDataInsideTheImage()
    {
        // A black square inside a larger region: at its left edge the interior kernel sees 0 on the left and 1 on the
        // right, Nx = -1/4 * 4 = -1, so N.L = (-0.7071 + 0.7071) / Norm = 0; at its right edge Nx = 1 and N.L = 1.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feDiffuseLighting><feDistantLight azimuth=\"0\" elevation=\"45\"/></feDiffuseLighting></filter>"
            + "<rect x=\"5\" y=\"5\" width=\"10\" height=\"10\" filter=\"url(#f)\"/>");

        AssertGray(0, image[4, 10]);
        AssertGray(0, image[5, 10]);
        AssertGray(180, image[10, 10]);
        AssertGray(255, image[14, 10]);
        AssertGray(255, image[15, 10]);
        AssertGray(180, image[2, 10]);
    }

    [Fact]
    public void PointLight_LightsFromItsPositionAboveTheSurface()
    {
        // A flat opaque surface at height surfaceScale * 1 = 1, a light at (0, 0, 5): at the center (10.5, 10.5) of pixel
        // (10, 10), L = (-10.5, -10.5, 4) / 15.38, and N.L = 4 / 15.38 = 0.2601, 66; at (2.5, 2.5), N.L = 4 / 5.339, 191.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feDiffuseLighting><fePointLight x=\"0\" y=\"0\" z=\"5\"/></feDiffuseLighting></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertGray(66, image[10, 10]);
        AssertGray(191, image[2, 2]);
    }

    [Fact]
    public void SpotLight_FadesAcrossTheConeEdge()
    {
        // A spot light 9 units above a flat opaque surface (height 1), pointing straight down, with exponent 2 and a cone
        // of 20 degrees (cosine 0.93969). At a horizontal distance d, -L.S = N.L = 9 / Norm(d, 9), and the value is
        // pow(-L.S, 2) * N.L, faded linearly over 0.016 of the cosine inside the cone edge:
        // d = 0: 255; d = 2: -L.S = 0.97619, 237; d = 3: -L.S = 0.94868, inside the band by 0.00899, 0.9 * 0.5619 * 0.94868, 122;
        // d = 4: -L.S = 0.91381, outside the cone, 0.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feDiffuseLighting>"
            + "<feSpotLight x=\"10.5\" y=\"10.5\" z=\"10\" pointsAtX=\"10.5\" pointsAtY=\"10.5\" pointsAtZ=\"0\" specularExponent=\"2\" limitingConeAngle=\"20\"/>"
            + "</feDiffuseLighting></filter><rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertGray(255, image[10, 10]);
        AssertGray(237, image[12, 10]);
        AssertGray(122, image[13, 10]);
        AssertGray(0, image[14, 10]);
        AssertGray(122, image[10, 7]);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-20", 122)]
    public void SpotLight_UsesTheCosineOfTheConeAngle(string angle, int expected)
    {
        // The cone has the same cosine for an angle of either sign. A cone of 0 degrees lets no light out, as the
        // specification defines (Chromium treats 0 as no cone).
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feDiffuseLighting>"
            + $"<feSpotLight x=\"10.5\" y=\"10.5\" z=\"10\" pointsAtX=\"10.5\" pointsAtY=\"10.5\" pointsAtZ=\"0\" specularExponent=\"2\" limitingConeAngle=\"{angle}\"/>"
            + "</feDiffuseLighting></filter><rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertGray(expected, image[13, 10]);
    }

    [Theory]
    [InlineData("-10")]
    [InlineData("0")]
    [InlineData("abc")]
    public void SpotLight_TakesExponentOneForANonPositiveExponent(string exponent)
    {
        // With exponent 1 at d = 3 and no cone: -L.S * N.L = 0.94868 * 0.94868 = 0.9, 230.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feDiffuseLighting>"
            + $"<feSpotLight x=\"10.5\" y=\"10.5\" z=\"10\" pointsAtX=\"10.5\" pointsAtY=\"10.5\" pointsAtZ=\"0\" specularExponent=\"{exponent}\"/>"
            + "</feDiffuseLighting></filter><rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertGray(230, image[13, 10]);
    }

    [Fact]
    public void SpotLight_WithoutDirection_GivesNoLight()
    {
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feDiffuseLighting>"
            + "<feSpotLight x=\"10\" y=\"10\" z=\"5\" pointsAtX=\"10\" pointsAtY=\"10\" pointsAtZ=\"5\"/>"
            + "</feDiffuseLighting></filter><rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertPixel(new Rgba32(0, 0, 0, 255), image[10, 10]);
    }

    [Fact]
    public void Specular_HasTheAlphaOfItsLargestChannel()
    {
        // A flat surface, L = (cos 60, 0, sin 60), H = Normalize(L + (0, 0, 1)) = (0.2588, 0, 0.9659), N.H = 0.9659.
        // S = 0.6 * 0.9659^2 * (1, 0.502, 0) = (0.5598, 0.2810, 0), alpha 0.5598: premultiplied (143, 72, 0, 143), which
        // is the straight color (255, 128, 0) at alpha 143.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feSpecularLighting lighting-color=\"rgb(255, 128, 0)\" specularConstant=\"0.6\" specularExponent=\"2\">"
            + "<feDistantLight azimuth=\"0\" elevation=\"60\"/></feSpecularLighting></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertPixel(new Rgba32(255, 128, 0, 143), image[10, 10]);
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("0.5", "1")]
    [InlineData("1e30", "128")]
    [InlineData("300", "128")]
    public void Specular_ClampsTheExponentToTheRangeOfSvg11(string exponent, string clamped)
    {
        // A point light over a bump gives a different highlight for every exponent.
        const string bump = "<radialGradient id=\"g\" r=\"0.5\"><stop offset=\"0\"/><stop offset=\"1\" stop-opacity=\"0\"/></radialGradient>";
        string filter(string e) => $"<filter id=\"f\" {Region}><feSpecularLighting surfaceScale=\"5\" specularExponent=\"{e}\">"
            + "<fePointLight x=\"8\" y=\"8\" z=\"10\"/></feSpecularLighting></filter><rect width=\"20\" height=\"20\" fill=\"url(#g)\" filter=\"url(#f)\"/>";

        using Image<Rgba32> actual = Render(bump + filter(exponent));
        using Image<Rgba32> expected = Render(bump + filter(clamped));

        AssertSameImage(expected, actual);
    }

    [Fact]
    public void Specular_WithANegativeConstant_IsOpaqueBlack()
    {
        // Filter Effects does not define a negative specularConstant; Chromium gives opaque black, and so does the engine.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feSpecularLighting specularConstant=\"-1\"><feDistantLight elevation=\"90\"/></feSpecularLighting></filter>"
            + "<rect x=\"5\" y=\"5\" width=\"10\" height=\"10\" fill=\"green\" filter=\"url(#f)\"/>");

        AssertPixel(new Rgba32(0, 0, 0, 255), image[1, 1]);
        AssertPixel(new Rgba32(0, 0, 0, 255), image[10, 10]);
    }

    [Fact]
    public void Specular_WithAZeroConstant_IsTransparent()
    {
        // S = 0 * ... = 0 in every channel, so its alpha max(Sr, Sg, Sb) is 0 too.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feSpecularLighting specularConstant=\"0\"><feDistantLight elevation=\"90\"/></feSpecularLighting></filter>"
            + "<rect x=\"5\" y=\"5\" width=\"10\" height=\"10\" fill=\"green\" filter=\"url(#f)\"/>");

        Assert.Equal(0, image[10, 10].A);
    }

    [Fact]
    public void Diffuse_ConvertsTheLightingColorToTheColorSpaceOfThePrimitive()
    {
        // N.L = sin(30 degrees) = 0.5. In sRGB: 160 * 0.5 = 80. In linearRGB: 160 is 0.3515 linear, 90 in 8 bits; half of
        // it is 45, which converts back to sRGB 117.
        string filter(string space) =>
            $"<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\" color-interpolation-filters=\"{space}\">"
            + "<feDiffuseLighting lighting-color=\"rgb(160, 160, 160)\"><feDistantLight azimuth=\"0\" elevation=\"30\"/></feDiffuseLighting></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>";

        using Image<Rgba32> srgb = Render(filter("sRGB"));
        using Image<Rgba32> linear = Render(filter("linearRGB"));

        AssertGray(80, srgb[10, 10]);
        AssertGray(117, linear[10, 10]);
    }

    [Fact]
    public void Lighting_FollowsTheScaleOfTheRender()
    {
        // The same document at one and two pixels per user unit. Point light: at twice the size, pixel (21, 21) has its
        // center at (10.75, 10.75) and N.L = 4 / Norm(10.75, 10.75, 4) = 0.2545, 65, next to 66 at (10.5, 10.5).
        // An alpha ramp from 0 to 1 over 20 user units with surfaceScale 4: Nx = -4 * 2 * 0.05 = -0.4 in user units at
        // both sizes, so a light straight above gives 255 / Norm(-0.4, 0, 1) = 237.
        const string point = $"<filter id=\"f\" {Region}><feDiffuseLighting><fePointLight x=\"0\" y=\"0\" z=\"5\"/></feDiffuseLighting></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>";
        const string ramp = "<linearGradient id=\"g\"><stop offset=\"0\" stop-opacity=\"0\"/><stop offset=\"1\"/></linearGradient>"
            + $"<filter id=\"f\" {Region}><feDiffuseLighting surfaceScale=\"4\"><feDistantLight elevation=\"90\"/></feDiffuseLighting></filter>"
            + "<rect width=\"20\" height=\"20\" fill=\"url(#g)\" filter=\"url(#f)\"/>";

        using Image<Rgba32> point1 = Render(point);
        using Image<Rgba32> point2 = Render(point, 40, 40);
        using Image<Rgba32> ramp1 = Render(ramp);
        using Image<Rgba32> ramp2 = Render(ramp, 40, 40);

        AssertGray(66, point1[10, 10]);
        AssertGray(65, point2[21, 21]);
        Assert.InRange(ramp1[10, 10].R, 235, 239);
        Assert.InRange(ramp2[20, 20].R, 235, 239);
    }

    [Fact]
    public void Lighting_UsesTheMeanScaleForHeightsUnderAScaleThatDiffersPerAxis()
    {
        // Scale (2, 1): z scales by 1.5. The light at user (0, 0, 5) is at filter (0, 0, 7.5), the opaque surface at
        // height 1.5. Pixel (21, 10) has its center at filter (21.5, 10.5): N.L = 6 / Norm(21.5, 10.5, 6) = 0.2433, 62.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feDiffuseLighting><fePointLight x=\"0\" y=\"0\" z=\"5\"/></feDiffuseLighting></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>",
            40,
            20,
            "preserveAspectRatio=\"none\"");

        AssertGray(62, image[21, 10]);
    }

    [Fact]
    public void PointLight_FollowsPrimitiveUnits()
    {
        // primitiveUnits="objectBoundingBox" with the box (0, 0, 20, 10): x and y are fractions of the width and the
        // height; z is a fraction of sqrt((20^2 + 10^2) / 2) = 15.81. The light (0.5, 0.5, 0.3162) is at user (10, 5, 5).
        // Pixel (10, 5) has its center at (10.5, 5.5): N.L = 4 / Norm(0.5, 0.5, 4) = 0.9847, 251; pixel (16, 5):
        // N.L = 4 / Norm(6.5, 0.5, 4) = 0.5228, 133.
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\" primitiveUnits=\"objectBoundingBox\" color-interpolation-filters=\"sRGB\">"
            + "<feDiffuseLighting><fePointLight x=\"0.5\" y=\"0.5\" z=\"0.3162278\"/></feDiffuseLighting></filter>"
            + "<rect width=\"20\" height=\"10\" filter=\"url(#f)\"/>");

        AssertGray(251, image[10, 5]);
        AssertGray(133, image[16, 5]);
    }

    [Fact]
    public void Lighting_WithoutLightSource_IsTransparent()
    {
        // The content model asks for one light source; without one the result is transparent black, as in Chromium, and
        // a light source that is not a direct child does not count. The merge shows the source graphic.
        foreach (string content in new[] { string.Empty, "<g><feDistantLight elevation=\"90\"/></g>" })
        {
            foreach (string primitive in new[] { "feDiffuseLighting", "feSpecularLighting" })
            {
                using Image<Rgba32> image = Render(
                    $"<filter id=\"f\" {Region}><{primitive} result=\"l\">{content}</{primitive}>"
                    + "<feMerge><feMergeNode in=\"SourceGraphic\"/><feMergeNode in=\"l\"/></feMerge></filter>"
                    + "<rect x=\"5\" y=\"5\" width=\"10\" height=\"10\" fill=\"#008000\" filter=\"url(#f)\"/>");

                AssertPixel(new Rgba32(0, 128, 0, 255), image[10, 10]);
                Assert.Equal(0, image[1, 1].A);
            }
        }
    }

    [Fact]
    public void Lighting_UsesTheFirstLightSource()
    {
        // The point light comes first and wins: 66 at pixel (10, 10), as in PointLight_LightsFromItsPositionAboveTheSurface.
        // The distant light straight above would give 255.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" {Region}><feDiffuseLighting><desc/><fePointLight x=\"0\" y=\"0\" z=\"5\"/><feDistantLight elevation=\"90\"/></feDiffuseLighting></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertGray(66, image[10, 10]);
    }

    [Fact]
    public void Lighting_IgnoresKernelUnitLengthAndUsesInitialValuesForInvalidNumbers()
    {
        // kernelUnitLength does not change the result, as in Chromium. Invalid numbers (a unit, two numbers, a keyword)
        // take the initial values surfaceScale 1 and diffuseConstant 1.
        string filter(string attributes) => $"<filter id=\"f\" {GridRegion}><feDiffuseLighting {attributes}>"
            + "<feDistantLight azimuth=\"0\" elevation=\"45\"/></feDiffuseLighting></filter>" + Grid;

        using Image<Rgba32> expected = Render(filter(string.Empty));
        using Image<Rgba32> kernel = Render(filter("kernelUnitLength=\"3 0.5\""));
        using Image<Rgba32> invalid = Render(filter("surfaceScale=\"2px\" diffuseConstant=\"1 2\""));
        using Image<Rgba32> keyword = Render(filter("surfaceScale=\"Infinity\" diffuseConstant=\"none\""));

        AssertSameImage(expected, kernel);
        AssertSameImage(expected, invalid);
        AssertSameImage(expected, keyword);
    }

    [Fact]
    public void Lighting_StaysFiniteForExtremeValues()
    {
        // A huge surfaceScale overflows the normal where the surface slopes: no light there, as in Chromium. The flat
        // parts keep N = (0, 0, 1), 180. A huge constant saturates; a light far below the surface lights nothing.
        const string bump = "<radialGradient id=\"g\" r=\"0.3\"><stop offset=\"0\"/><stop offset=\"1\" stop-opacity=\"0\"/></radialGradient>";
        using Image<Rgba32> scale = Render(
            bump + $"<filter id=\"f\" {Region}><feDiffuseLighting surfaceScale=\"1e30\"><feDistantLight azimuth=\"0\" elevation=\"45\"/></feDiffuseLighting></filter>"
            + "<rect width=\"20\" height=\"20\" fill=\"url(#g)\" filter=\"url(#f)\"/>");
        using Image<Rgba32> constant = Render(
            $"<filter id=\"f\" {Region}><feSpecularLighting specularConstant=\"1e30\" specularExponent=\"1e30\"><feDistantLight elevation=\"60\"/></feSpecularLighting></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");
        using Image<Rgba32> below = Render(
            $"<filter id=\"f\" {Region}><feDiffuseLighting diffuseConstant=\"1e30\"><fePointLight x=\"10\" y=\"10\" z=\"-1e30\"/></feDiffuseLighting></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertGray(180, scale[1, 1]);
        AssertGray(0, scale[12, 10]);
        AssertPixel(new Rgba32(255, 255, 255, 255), constant[10, 10]);
        AssertPixel(new Rgba32(0, 0, 0, 255), below[10, 10]);
    }

    private static Image<Rgba32> Render(string content, int width = 20, int height = 20, string attributes = "")
    {
        string open = attributes.Length == 0 ? SvgOpen : SvgOpen.Replace(">", $" {attributes}>", StringComparison.Ordinal);
        SvgDocument document = SvgDocument.Parse(open + content + "</svg>");
        Image<Rgba32> image = new(width, height);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, width, height))));
        return image;
    }

    private static void AssertGray(int expected, Rgba32 actual)
        => AssertPixel(new Rgba32((byte)expected, (byte)expected, (byte)expected, 255), actual);

    private static void AssertPixel(Rgba32 expected, Rgba32 actual)
    {
        Assert.InRange(actual.A, expected.A - 1, expected.A + 1);
        Assert.InRange(actual.R, expected.R - 1, expected.R + 1);
        Assert.InRange(actual.G, expected.G - 1, expected.G + 1);
        Assert.InRange(actual.B, expected.B - 1, expected.B + 1);
    }

    private static void AssertSameImage(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                Assert.Equal(expected[x, y], actual[x, y]);
            }
        }
    }
}
