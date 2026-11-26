// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests of SVG filters with the transforms and scopes around them and with concurrent renders. A flood over the filter
/// region of a square, which is the square grown by the default 10%, must cover what a square of that size covers
/// without a filter, whatever transform, clip, or mask applies around it.
/// </summary>
public class SvgFilterIntegrationTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\">";

    // The filter region of the square from 50 to 150 is 40 to 160; the flood fills it green.
    private const string Flood = "<filter id=\"f\"><feFlood flood-color=\"#008000\"/></filter>";
    private const string Filtered = "<rect x=\"50\" y=\"50\" width=\"100\" height=\"100\" filter=\"url(#f)\"/>";
    private const string Plain = "<rect x=\"40\" y=\"40\" width=\"120\" height=\"120\" fill=\"#008000\"/>";

    [Theory]

    // A rotated clip and a skewed mask of a group around the filtered element, and a clip and a mask on the element.
    [InlineData("<clipPath id=\"c\" transform=\"rotate(25 100 100)\"><rect x=\"60\" y=\"60\" width=\"80\" height=\"80\"/></clipPath>", "clip-path=\"url(#c)\"")]
    [InlineData("<mask id=\"m\" maskUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"200\" height=\"200\"><rect x=\"60\" y=\"60\" width=\"60\" height=\"80\" fill=\"#ffffff\" transform=\"skewX(20)\"/></mask>", "mask=\"url(#m)\"")]
    [InlineData("<clipPath id=\"c\"><circle cx=\"80\" cy=\"80\" r=\"50\"/><circle cx=\"130\" cy=\"130\" r=\"30\"/></clipPath>", "clip-path=\"url(#c)\"")]
    public void Filter_UnderRotatedOrSkewedClipsAndMasks_DrawsInsideTheScope(string definitions, string scope)
    {
        foreach (string transform in new[] { string.Empty, " transform=\"rotate(-15 100 100) skewY(10)\"" })
        {
            using Image<Rgba32> filtered = Render($"{definitions}{Flood}<g{transform} {scope}>{Filtered}</g>");
            using Image<Rgba32> plain = Render($"{definitions}<g{transform} {scope}>{Plain}</g>");

            // The filter result draws through bilinear sampling under the skew; the edges differ by antialiasing only.
            ImageComparer.TolerantPercentage(0.5F).VerifySimilarity(plain, filtered);
        }
    }

    [Fact]
    public void Filter_UnderAProjectiveCallerTransform_FollowsTheTransform()
    {
        // The filter result is an image in the user space of the element; the caller's projective transform maps it as
        // it maps the geometry of the element.
        Matrix4x4 transform = Matrix4x4.Identity;
        transform.M14 = 0.002F;
        transform.M24 = 0.001F;
        SvgDocument filtered = SvgDocument.Parse(SvgOpen + Flood + Filtered + "</svg>");
        SvgDocument plain = SvgDocument.Parse(SvgOpen + Plain + "</svg>");

        using Image<Rgba32> a = new(200, 200);
        a.Mutate(x => x.Paint(new DrawingOptions { Transform = transform }, canvas => canvas.DrawSvg(filtered, PointF.Empty)));
        using Image<Rgba32> b = new(200, 200);
        b.Mutate(x => x.Paint(new DrawingOptions { Transform = transform }, canvas => canvas.DrawSvg(plain, PointF.Empty)));

        Assert.True(a[60, 60].A > 0);
        ImageComparer.TolerantPercentage(0.5F).VerifySimilarity(b, a);
    }

    [Fact]
    public void ConcurrentRenders_OfOneDocumentWithFilters_EqualARenderOnOneThread()
    {
        // The filter graphs and the nested content documents are shared by every render; each render owns its buffers.
        SvgDocument[] documents =
        [
            SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/filter-integration-compositing.svg")),
            SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/filter-integration-content.svg"), SvgTextRenderTests.Options),
            SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/filter-lighting-specular.svg")),
            SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/filter-turbulence.svg")),
            SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/filter-image.svg")),
        ];

        foreach (SvgDocument document in documents)
        {
            using Image<Rgba32> expected = new(200, 200);
            expected.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 200, 200))));

            // Renders at two sizes run at the same time, so the renders also differ in their filter scale.
            Image<Rgba32>[] images = new Image<Rgba32>[16];
            Parallel.For(0, images.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            {
                int size = i % 2 == 0 ? 200 : 300;
                Image<Rgba32> image = new(size, size);
                image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, size, size))));
                images[i] = image;
            });

            using Image<Rgba32> large = new(300, 300);
            large.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 300, 300))));
            for (int i = 0; i < images.Length; i++)
            {
                using Image<Rgba32> image = images[i];
                ImageComparer.Exact.VerifySimilarity(i % 2 == 0 ? expected : large, image);
            }
        }
    }

    private static Image<Rgba32> Render(string content)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + content + "</svg>");
        Image<Rgba32> image = new(200, 200);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 200, 200))));
        return image;
    }
}
