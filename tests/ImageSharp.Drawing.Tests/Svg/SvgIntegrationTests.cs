// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Numerics;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests for the phase 2 SVG features in combination: clips, masks, patterns, markers, images, blend modes, <c>use</c>,
/// and CSS selectors that set the compositing and marker properties. The reference images were accepted only after a
/// comparison with the Chromium render oracle in <c>tests/SvgTools</c> at the same size.
/// </summary>
[GroupOutput("Svg")]
public class SvgIntegrationTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"";

    /// <summary>
    /// A data URI of a PNG image of 16 x 12 opaque pixels.
    /// </summary>
    private const string PngDataUri = "data:image/png;base64,"
        + "iVBORw0KGgoAAAANSUhEUgAAABAAAAAMCAYAAABr5z2BAAABI0lEQVR42hXQoXEDMQBE0WshyPxQkIlakEsIC1YDBmpBM65A1FAd"
        + "eNRAgIAbOGymFpR34JEFH+y2bd/ri51A3P7WD4lMsVUanWE7mCy27SLATiBeBEhkiq3S6AzbwWRdzsBVgJ1AvAqQyBRbpdEZtoPJ"
        + "up6BmwA7gXgTIJEptkqjM2wHk3U7A79/64udQPz9Xj8kMsVWaXSG7WCy2La7ADuBeBcgkSm2SqMzbAeTdT8DDwF2AvEhQCJTbJVG"
        + "Z9gOJutxBp4C7ATiU4BEptgqjc6wHUzW8wy8nMhOIL6cSCJTbJVGZ9gOJut1nvgWYCcQ3wIkMsVWaXSG7WCy3mfgI8BOIH4ESGSK"
        + "rdLoDNvBZH3OwBJgJxCXAIlMsVUanWE7mCz+AYlX2EAgS/7zAAAAAElFTkSuQmCC";

    /// <summary>
    /// The cross-feature files in <c>tests/Images/Input/Svg/Features</c>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "integration-mask-pattern")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "integration-marker-clip")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "integration-image-mask")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "integration-pattern-content")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "integration-use")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "integration-css")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "integration-nesting")]
    [WithBlankImage(400, 400, PixelTypes.Rgba32, "integration-mask-pattern")]
    public void DrawSvg_Integration<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"));
        using Image<TPixel> image = provider.GetImage();
        string output = $"{name}-{image.Width}";
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, image.Width, image.Height))));
        image.DebugSave(provider, output, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.01F),
            provider,
            output,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    /// <summary>
    /// Many threads draw the same documents at once, at more output sizes than the pattern tile cache keeps, so tiles
    /// render, get evicted, and render again while other threads sample them; texture levels and decoded images are
    /// shared too. Every concurrent render must equal the render of the same size on one thread.
    /// </summary>
    [Fact]
    public void DrawSvg_ConcurrentRendersOfAllFeaturesMatch()
    {
        string[] names =
        [
            "integration-mask-pattern", "integration-marker-clip", "integration-image-mask", "integration-pattern-content",
            "integration-use", "integration-css", "integration-nesting", "pattern", "mask", "marker", "image", "clip-path",
        ];

        // Seven sizes, more than the four tile renders that a pattern caches, plus a rotated canvas transform.
        (float Size, float Angle)[] variants = [(64, 0), (100, 0), (137, 0), (173, 0), (200, 0), (251, 0), (160, 30)];
        foreach (string name in names)
        {
            SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"));
            Image<Rgba32>[] expected = new Image<Rgba32>[variants.Length];
            Image<Rgba32>[] actual = new Image<Rgba32>[variants.Length * 3];
            try
            {
                for (int i = 0; i < variants.Length; i++)
                {
                    expected[i] = Render(document, variants[i].Size, variants[i].Angle);
                }

                // Interleave the variants so that threads ask for different tile sizes at the same time.
                Parallel.For(
                    0,
                    actual.Length,
                    new ParallelOptions { MaxDegreeOfParallelism = 8 },
                    i => actual[i] = Render(document, variants[i % variants.Length].Size, variants[i % variants.Length].Angle));

                for (int i = 0; i < actual.Length; i++)
                {
                    ImageComparer.Exact.VerifySimilarity(expected[i % variants.Length], actual[i]);
                }
            }
            finally
            {
                foreach (Image<Rgba32> image in expected.Concat(actual))
                {
                    image?.Dispose();
                }
            }
        }

        static Image<Rgba32> Render(SvgDocument document, float size, float angle)
        {
            int pixels = (int)size;
            Image<Rgba32> image = new(pixels, pixels);
            DrawingOptions options = new()
            {
                Transform = new Matrix4x4(Matrix3x2.CreateRotation(angle * MathF.PI / 180F, new Vector2(size / 2F)))
            };

            image.Mutate(x => x.Paint(options, canvas => canvas.DrawSvg(document, new RectangleF(0, 0, size, size))));
            return image;
        }
    }

    /// <summary>
    /// A user style sheet sets <c>clip-path</c>, <c>mask</c>, and the marker properties at user origin: below author
    /// declarations, presentation attributes included, for normal declarations, and above them for <c>!important</c>.
    /// The result equals a document that sets the winning values directly.
    /// </summary>
    [Fact]
    public void DrawSvg_UserStyleSheetSetsCompositingAndMarkers()
    {
        const string defs = "<defs><clipPath id=\"c\"><circle cx=\"20\" cy=\"20\" r=\"15\"/></clipPath>"
            + "<mask id=\"k\"><rect x=\"40\" width=\"20\" height=\"40\" fill=\"#fff\"/></mask>"
            + "<marker id=\"m\" markerWidth=\"6\" markerHeight=\"6\" refX=\"3\" refY=\"3\" markerUnits=\"userSpaceOnUse\">"
            + "<rect width=\"6\" height=\"6\" fill=\"#d62728\"/></marker></defs>";

        const string userSheet = "rect[data-clip] { clip-path: url(#c); } path { marker: url(#m); } .k { mask: url(#k) !important; }"
            + " .plain { clip-path: url(#c); }";

        SvgDocument styled = SvgDocument.Parse(
            SvgOpen + " width=\"100\" height=\"100\"><style>.k { mask: none; }</style>" + defs
            + "<rect data-clip=\"\" width=\"40\" height=\"40\" fill=\"#1f77b4\"/>"
            + "<rect class=\"k\" x=\"30\" width=\"40\" height=\"40\" fill=\"#ff7f0e\"/>"
            + "<rect class=\"plain\" clip-path=\"none\" y=\"50\" width=\"40\" height=\"40\" fill=\"#2ca02c\"/>"
            + "<path d=\"M50 60 L70 80 L90 60\" fill=\"none\" stroke=\"#000\"/></svg>",
            new SvgLoadOptions { UserStyleSheet = userSheet });

        SvgDocument direct = SvgDocument.Parse(
            SvgOpen + " width=\"100\" height=\"100\">" + defs
            + "<rect clip-path=\"url(#c)\" width=\"40\" height=\"40\" fill=\"#1f77b4\"/>"
            + "<rect mask=\"url(#k)\" x=\"30\" width=\"40\" height=\"40\" fill=\"#ff7f0e\"/>"
            + "<rect y=\"50\" width=\"40\" height=\"40\" fill=\"#2ca02c\"/>"
            + "<path d=\"M50 60 L70 80 L90 60\" fill=\"none\" stroke=\"#000\" marker-start=\"url(#m)\" marker-mid=\"url(#m)\""
            + " marker-end=\"url(#m)\"/></svg>");

        using Image<Rgba32> expected = new(100, 100);
        using Image<Rgba32> actual = new(100, 100);
        expected.Mutate(x => x.Paint(canvas => canvas.DrawSvg(direct, PointF.Empty)));
        actual.Mutate(x => x.Paint(canvas => canvas.DrawSvg(styled, PointF.Empty)));

        // The comparison is only meaningful if each feature drew: the clip cuts the corner of the first rectangle, the
        // mask keeps only part of the second, the third rectangle is not clipped, and the marker paints at the middle
        // vertex.
        Assert.Equal(0, expected[2, 2].A);
        Assert.Equal(0, expected[65, 5].A);
        Assert.Equal(255, expected[2, 52].A);
        Assert.Equal(0xd6, expected[70, 80].R);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    /// <summary>
    /// Nested patterns, each a tile that the next pattern fills, stay bounded: the tiles of deeply nested levels get a
    /// smaller pixel budget, so the render finishes and the innermost color still shows.
    /// </summary>
    [Fact]
    public void DrawSvg_DeeplyNestedPatternsRender()
    {
        StringBuilder svg = new(SvgOpen + " width=\"100\" height=\"100\">");
        const int depth = 100;
        for (int i = 0; i < depth; i++)
        {
            string fill = i + 1 < depth ? $"url(#p{i + 1})" : "#ff0000";
            svg.Append(CultureInfo.InvariantCulture, $"<pattern id=\"p{i}\" patternUnits=\"userSpaceOnUse\" width=\"100\" height=\"100\">")
                .Append(CultureInfo.InvariantCulture, $"<rect width=\"100\" height=\"100\" fill=\"{fill}\"/></pattern>");
        }

        svg.Append("<rect width=\"100\" height=\"100\" fill=\"url(#p0)\"/></svg>");
        SvgDocument document = SvgDocument.Parse(svg.ToString());

        using Image<Rgba32> image = new(400, 400);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 400, 400))));
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[200, 200]);
    }

    /// <summary>
    /// Gets pattern tiles whose device size is extreme: much longer than wide, tiny over a huge area, or scaled to a huge
    /// size on one axis and to almost nothing on the other.
    /// </summary>
    public static TheoryData<string> ExtremePatternTiles { get; } = new()
    {
        "<pattern id=\"p\" patternUnits=\"userSpaceOnUse\" width=\"1e9\" height=\"0.0001\">"
            + "<rect width=\"1e9\" height=\"0.00005\" fill=\"#0f0\"/></pattern><rect width=\"100\" height=\"100\" fill=\"url(#p)\"/>",
        "<pattern id=\"p\" patternUnits=\"userSpaceOnUse\" width=\"0.0001\" height=\"1e9\">"
            + "<rect width=\"0.00005\" height=\"1e9\" fill=\"#0f0\"/></pattern><rect width=\"100\" height=\"100\" fill=\"url(#p)\"/>",
        "<pattern id=\"p\" patternUnits=\"userSpaceOnUse\" width=\"0.001\" height=\"0.001\">"
            + "<rect width=\"0.0005\" height=\"0.001\" fill=\"#0f0\"/></pattern>"
            + "<rect x=\"-1e6\" y=\"-1e6\" width=\"2e6\" height=\"2e6\" fill=\"url(#p)\"/>",
        "<pattern id=\"p\" patternUnits=\"userSpaceOnUse\" width=\"10\" height=\"10\" patternTransform=\"scale(1e20 1e-20)\">"
            + "<rect width=\"5\" height=\"10\" fill=\"#0f0\"/></pattern><rect width=\"100\" height=\"100\" fill=\"url(#p)\"/>",
    };

    /// <summary>
    /// A pattern tile of extreme device size draws at a lower resolution instead of failing the draw.
    /// </summary>
    /// <param name="body">The document content.</param>
    [Theory]
    [MemberData(nameof(ExtremePatternTiles))]
    public void DrawSvg_ExtremePatternTileDraws(string body)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + " width=\"100\" height=\"100\">" + body + "</svg>");
        using Image<Rgba32> image = new(200, 200);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 200, 200))));
    }

    /// <summary>
    /// Every marker instance counts against <see cref="SvgLimits.MaxNodes"/>, directly and through <c>use</c> elements in
    /// marker content, so a path with many vertices, or markers that nest through <c>use</c>, cannot multiply the work
    /// without bound.
    /// </summary>
    [Fact]
    public void DrawSvg_MarkerInstancesCountAgainstTheNodeLimit()
    {
        const string marker = "<marker id=\"m\" markerWidth=\"2\" markerHeight=\"2\" markerUnits=\"userSpaceOnUse\">"
            + "<rect width=\"1\" height=\"1\"/></marker>";
        string points = string.Join(' ', Enumerable.Range(0, 5000).Select(i => $"{i % 100},{i / 100}"));
        string many = SvgOpen + " width=\"100\" height=\"100\">" + marker
            + $"<polyline points=\"{points}\" fill=\"none\" stroke=\"#000\" marker-mid=\"url(#m)\"/></svg>";

        SvgLoadOptions limited = new() { Limits = new SvgLimits { MaxNodes = 1000 } };
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(many, limited));

        // Within the default limits the same document renders.
        SvgDocument document = SvgDocument.Parse(many);
        using Image<Rgba32> image = new(100, 100);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));

        // Each marker holds a use of a path whose ten vertices carry the marker of the next level: 10^6 instances.
        StringBuilder nested = new(SvgOpen + " width=\"100\" height=\"100\"><defs>");
        for (int i = 0; i < 6; i++)
        {
            string next = i < 5 ? $"url(#m{i + 1})" : "none";
            nested.Append(CultureInfo.InvariantCulture, $"<path id=\"l{i}\" d=\"M0 0 L1 1 L2 0 L3 1 L4 0 L5 1 L6 0 L7 1 L8 0 L9 1\"")
                .Append(CultureInfo.InvariantCulture, $" stroke=\"#000\" marker-mid=\"{next}\"/>")
                .Append(CultureInfo.InvariantCulture, $"<marker id=\"m{i}\" markerWidth=\"9\" markerHeight=\"9\" overflow=\"visible\">")
                .Append(CultureInfo.InvariantCulture, $"<use xlink:href=\"#l{i}\"/></marker>");
        }

        nested.Append("</defs><use xlink:href=\"#l0\"/></svg>");
        SvgLoadOptions nodeLimit = new() { Limits = new SvgLimits { MaxNodes = 20_000 } };
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(nested.ToString(), nodeLimit));
    }

    /// <summary>
    /// Masks and clips nest up to the depth limit and render. A chain of <c>mask</c> properties on <c>mask</c> elements,
    /// or of <c>clip-path</c> properties on <c>clipPath</c> elements, longer than <see cref="SvgLimits.MaxNestingDepth"/>
    /// fails the load instead of recursing without bound.
    /// </summary>
    [Fact]
    public void DrawSvg_NestedMasksAndClipsAreBounded()
    {
        StringBuilder defs = new();
        StringBuilder groups = new();
        const int depth = 100;
        for (int i = 0; i < depth; i++)
        {
            defs.Append(CultureInfo.InvariantCulture, $"<mask id=\"k{i}\"><rect width=\"100\" height=\"100\" fill=\"#fff\"/></mask>")
                .Append(CultureInfo.InvariantCulture, $"<clipPath id=\"c{i}\"><rect width=\"100\" height=\"100\"/>")
                .Append("<circle cx=\"50\" cy=\"50\" r=\"60\"/></clipPath>");
            groups.Append(CultureInfo.InvariantCulture, $"<g mask=\"url(#k{i})\" clip-path=\"url(#c{i})\">");
        }

        SvgDocument document = SvgDocument.Parse(SvgOpen + " width=\"100\" height=\"100\"><defs>" + defs + "</defs>" + groups
            + "<rect width=\"100\" height=\"100\" fill=\"#0000ff\"/>" + string.Concat(Enumerable.Repeat("</g>", depth)) + "</svg>");

        using Image<Rgba32> image = new(100, 100);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));
        Assert.Equal(new Rgba32(0, 0, 255, 255), image[50, 50]);

        string masks = string.Concat(Enumerable.Range(0, 600)
            .Select(i => $"<mask id=\"k{i}\" mask=\"url(#k{i + 1})\"><rect width=\"100\" height=\"100\" fill=\"#fff\"/></mask>"));
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(
            SvgOpen + " width=\"100\" height=\"100\"><defs>" + masks + "</defs><rect width=\"100\" height=\"100\" mask=\"url(#k0)\"/></svg>"));

        string clips = string.Concat(Enumerable.Range(0, 600)
            .Select(i => $"<clipPath id=\"c{i}\" clip-path=\"url(#c{i + 1})\"><rect width=\"100\" height=\"100\"/></clipPath>"));
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(
            SvgOpen + " width=\"100\" height=\"100\"><defs>" + clips + "</defs><rect width=\"100\" height=\"100\" clip-path=\"url(#c0)\"/></svg>"));
    }

    /// <summary>
    /// An image that many <c>use</c> elements repeat decodes once, so its pixels count once against
    /// <see cref="SvgLimits.MaxImagePixels"/>; the instances count against <see cref="SvgLimits.MaxNodes"/>.
    /// </summary>
    [Fact]
    public void DrawSvg_ImageUseStormIsBounded()
    {
        StringBuilder svg = new(SvgOpen + " width=\"100\" height=\"100\"><defs>");
        svg.Append(CultureInfo.InvariantCulture, $"<image id=\"i0\" width=\"16\" height=\"12\" xlink:href=\"{PngDataUri}\"/>");
        for (int level = 1; level <= 4; level++)
        {
            string target = level == 1 ? "i0" : $"g{level - 1}";
            svg.Append(CultureInfo.InvariantCulture, $"<g id=\"g{level}\">");
            for (int k = 0; k < 10; k++)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<use xlink:href=\"#{target}\" x=\"{k * 2}\" y=\"{k * 2}\"/>");
            }

            svg.Append("</g>");
        }

        svg.Append("</defs><use xlink:href=\"#g4\"/></svg>");
        string text = svg.ToString();

        // 10^4 instances of one 16 x 12 image within a budget of exactly one decode.
        SvgDocument document = SvgDocument.Parse(text, new SvgLoadOptions { Limits = new SvgLimits { MaxImagePixels = 16 * 12 } });
        using Image<Rgba32> image = new(100, 100);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));
        Assert.Equal(255, image[50, 50].A);

        SvgLoadOptions nodeLimit = new() { Limits = new SvgLimits { MaxNodes = 5000 } };
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(text, nodeLimit));
    }

    /// <summary>
    /// The SVG 2 keywords of <c>refX</c> (<c>left</c>, <c>center</c>, <c>right</c>) and <c>refY</c> (<c>top</c>,
    /// <c>center</c>, <c>bottom</c>) are not supported by Chromium, which treats them as invalid values, so the reference
    /// point stays at the origin. This renderer matches the Chromium oracle.
    /// </summary>
    /// <param name="refX">The <c>refX</c> value.</param>
    /// <param name="refY">The <c>refY</c> value.</param>
    [Theory]
    [InlineData("left", "top")]
    [InlineData("center", "center")]
    [InlineData("right", "bottom")]
    public void DrawSvg_MarkerReferenceKeywordsActAsZero(string refX, string refY)
    {
        static Image<Rgba32> Render(string x, string y)
        {
            SvgDocument document = SvgDocument.Parse(SvgOpen + " width=\"100\" height=\"100\">"
                + $"<marker id=\"m\" viewBox=\"0 0 10 10\" markerWidth=\"20\" markerHeight=\"20\" markerUnits=\"userSpaceOnUse\" refX=\"{x}\" refY=\"{y}\">"
                + "<rect width=\"10\" height=\"10\" fill=\"#1f77b4\"/></marker>"
                + "<path d=\"M50 50 L80 50\" stroke=\"#000\" marker-start=\"url(#m)\"/></svg>");
            Image<Rgba32> image = new(100, 100);
            image.Mutate(c => c.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));
            return image;
        }

        using Image<Rgba32> expected = Render("0", "0");
        using Image<Rgba32> actual = Render(refX, refY);

        // With the reference point at the origin, the marker covers the area below and to the right of the vertex.
        Assert.Equal(255, expected[60, 60].A);
        Assert.Equal(0, expected[45, 45].A);
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }
}
