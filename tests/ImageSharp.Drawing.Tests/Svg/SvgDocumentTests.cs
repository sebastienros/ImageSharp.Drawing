// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

public class SvgDocumentTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"";

    [Theory]
    [InlineData(" width=\"120\" height=\"80\"", 120, 80)]
    [InlineData(" width=\"1in\" height=\"72pt\"", 96, 96)]
    [InlineData(" viewBox=\"0 0 40 30\"", 40, 30)]
    [InlineData(" width=\"80\" viewBox=\"0 0 40 30\"", 80, 60)]
    [InlineData(" height=\"60\" viewBox=\"0 0 40 30\"", 80, 60)]
    [InlineData(" width=\"50%\" height=\"100%\" viewBox=\"0 0 40 30\"", 40, 30)]
    [InlineData("", 300, 150)]
    [InlineData(" width=\"100\"", 100, 150)]
    public void Size_FollowsWidthHeightAndViewBox(string attributes, float width, float height)
    {
        // SVG_IMPLEMENTATION.md 5.2: absolute width and height win, a missing or percentage side follows the view box
        // aspect ratio, and the browser fallback is 300 x 150.
        SvgDocument document = SvgDocument.Parse(SvgOpen + attributes + "/>");

        Assert.Equal(width, document.Size.Width, 3);
        Assert.Equal(height, document.Size.Height, 3);
    }

    [Fact]
    public void ViewBox_IsTheRootViewBox()
    {
        Assert.Equal(new RectangleF(-5, 10, 40, 30), SvgDocument.Parse(SvgOpen + " viewBox=\"-5 10 40 30\"/>").ViewBox);
        Assert.Null(SvgDocument.Parse(SvgOpen + " width=\"10\" height=\"10\"/>").ViewBox);
    }

    [Fact]
    public void Dpi_ConvertsAbsoluteUnits()
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + " width=\"1in\" height=\"2.54cm\"/>", new SvgLoadOptions { Dpi = 300 });

        Assert.Equal(300, document.Size.Width, 3);
        Assert.Equal(300, document.Size.Height, 3);
    }

    [Fact]
    public void Load_ReadsStreamsFilesAndText()
    {
        string path = TestFile.GetInputFileFullPath("Svg/Features/shapes.svg");
        string text = File.ReadAllText(path);
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(text));

        SvgDocument fromFile = SvgDocument.Load(path);
        SvgDocument fromStream = SvgDocument.Load(stream);
        SvgDocument fromText = SvgDocument.Parse(text);

        Assert.True(stream.CanRead);
        Assert.Equal(fromFile.Size, fromStream.Size);
        Assert.Equal(fromFile.Size, fromText.Size);
        Assert.Equal(fromFile.Nodes.Length, fromStream.Nodes.Length);
        Assert.Equal(fromFile.Nodes.Length, fromText.Nodes.Length);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect></svg>")]
    [InlineData("<svg/>")]
    [InlineData("<g xmlns=\"http://www.w3.org/2000/svg\"/>")]
    public void Parse_InvalidDocumentThrows(string svg)
        => Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(svg));

    [Fact]
    public void Parse_InvalidAttributeValuesDoNotThrow()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + " width=\"abc\" viewBox=\"0 0 x 1\"><rect width=\"-5\" height=\"z\" fill=\"bogus\" transform=\"skew(\"/>"
            + "<path d=\"M 10 10 L\"/><circle r=\"NaN\"/><use href=\"#nothing\"/></svg>");

        Assert.Equal(new SizeF(300, 150), document.Size);
        Assert.True(document.Nodes.IsEmpty);
    }

    [Fact]
    public void Load_ThrowsForNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => SvgDocument.Load((Stream)null!));
        Assert.Throws<ArgumentNullException>(() => SvgDocument.Load((string)null!));
        Assert.Throws<ArgumentNullException>(() => SvgDocument.Load(Stream.Null, null!));
        Assert.Throws<ArgumentNullException>(() => SvgDocument.Parse(SvgOpen + "/>", null!));
    }

    [Fact]
    public void Options_RejectInvalidValues()
    {
        SvgLoadOptions options = new();
        SvgLimits limits = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Dpi = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Dpi = float.NaN);
        Assert.Throws<ArgumentNullException>(() => options.Limits = null!);
        Assert.Throws<ArgumentOutOfRangeException>(() => limits.MaxElements = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => limits.MaxNodes = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => limits.MaxNestingDepth = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => limits.MaxCharactersFromEntities = 0);
    }

    [Fact]
    public void Limits_MaxElementsApplies()
    {
        SvgLoadOptions options = new() { Limits = new SvgLimits { MaxElements = 3 } };

        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(SvgOpen + "><g/><g/><g/></svg>", options));
    }

    [Fact]
    public void Limits_MaxNodesStopsExponentialUseExpansion()
    {
        // Each level uses the previous level ten times, so twelve levels expand to 10^12 instances.
        StringBuilder svg = new(SvgOpen + "><defs><rect id=\"l0\" width=\"1\" height=\"1\"/>");
        for (int level = 1; level <= 12; level++)
        {
            svg.Append("<g id=\"l").Append(level).Append("\">");
            for (int i = 0; i < 10; i++)
            {
                svg.Append("<use href=\"#l").Append(level - 1).Append("\"/>");
            }

            svg.Append("</g>");
        }

        svg.Append("</defs><use href=\"#l12\"/></svg>");

        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(svg.ToString()));

        // Three uses of three uses of a group of two shapes visit more than 20 element instances.
        string small = SvgOpen + "><defs><g id=\"a\"><rect width=\"1\" height=\"1\"/><rect width=\"1\" height=\"1\"/></g>"
            + "<g id=\"b\"><use href=\"#a\"/><use href=\"#a\"/><use href=\"#a\"/></g></defs>"
            + "<use href=\"#b\"/><use href=\"#b\"/><use href=\"#b\"/></svg>";
        SvgLoadOptions options = new() { Limits = new SvgLimits { MaxNodes = 20 } };

        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(small, options));
    }

    [Fact]
    public void Limits_MaxNestingDepthCountsUseReferences()
    {
        // Each use references a group that holds the next use, so the document is shallow but its expansion is deep.
        StringBuilder svg = new(SvgOpen + "><defs>");
        for (int i = 0; i < 40; i++)
        {
            svg.Append("<g id=\"g").Append(i).Append("\"><use href=\"#g").Append(i + 1).Append("\"/></g>");
        }

        svg.Append("<rect id=\"g40\" width=\"1\" height=\"1\"/></defs><use href=\"#g0\"/></svg>");

        Assert.Equal(1, SvgDocument.Parse(svg.ToString()).Nodes.Length);

        SvgLoadOptions options = new() { Limits = new SvgLimits { MaxNestingDepth = 30 } };
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(svg.ToString(), options));
    }

    [Theory]
    [InlineData("<g id=\"a\"><use href=\"#a\"/></g>")]
    [InlineData("<use id=\"a\" href=\"#a\"/>")]
    [InlineData("<defs><g id=\"a\"><use href=\"#b\"/></g><g id=\"b\"><use href=\"#a\"/></g></defs><use href=\"#a\"/>")]
    [InlineData("<defs><linearGradient id=\"a\" href=\"#b\"/><linearGradient id=\"b\" xlink:href=\"#a\"/></defs>"
        + "<rect width=\"5\" height=\"5\" fill=\"url(#a)\"/>")]
    public void Parse_ReferenceCyclesRenderNothing(string content)
    {
        // A use cycle and a gradient href cycle are errors that render nothing; they must not hang or throw.
        SvgDocument document = SvgDocument.Parse(SvgOpen + ">" + content + "</svg>");

        Assert.True(document.Nodes.IsEmpty);
    }

    [Fact]
    public void Opacity_OnASingleShapeGroupUsesNoLayer()
    {
        // SVG_IMPLEMENTATION.md 5.7: opacity on a shape with only a fill, or on a group with one such shape, multiplies
        // the paint alpha instead of opening a layer.
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "><g opacity=\"0.5\"><rect width=\"5\" height=\"5\" fill=\"#ff0000\" opacity=\"0.5\"/></g></svg>");

        SvgRenderNode node = Assert.Single(document.Nodes.ToArray());
        Assert.Equal(Color.Red.WithAlpha(0.25F), Assert.IsType<SolidBrush>(node.Fill).Color);
    }

    [Fact]
    public void Opacity_OnOverlappingContentUsesOneBoundedLayer()
    {
        // A group of two shapes and a shape with fill and stroke need a layer bounded to their paint bounds, stroke
        // included. A group around a single layer folds its opacity into that layer.
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "><g opacity=\"0.5\"><g opacity=\"0.5\"><rect width=\"5\" height=\"5\"/><rect x=\"10\" width=\"5\" height=\"5\"/></g></g>"
            + "<rect y=\"20\" width=\"10\" height=\"10\" stroke=\"#000\" stroke-width=\"4\" opacity=\"0.5\"/></svg>");

        SvgRenderNode[] nodes = document.Nodes.ToArray();
        Assert.Equal(
            [SvgRenderNodeKind.BeginLayer, SvgRenderNodeKind.Draw, SvgRenderNodeKind.Draw, SvgRenderNodeKind.EndLayer,
             SvgRenderNodeKind.BeginLayer, SvgRenderNodeKind.Draw, SvgRenderNodeKind.EndLayer],
            nodes.Select(x => x.Kind));
        Assert.Equal(0.25F, nodes[0].Opacity, 5);
        Assert.Equal(3, nodes[0].End);
        Assert.Equal(new RectangleF(0, 0, 15, 5), nodes[0].Bounds);
        Assert.Equal(RectangleF.FromLTRB(-8, 12, 18, 38), nodes[4].Bounds);
    }

    [Fact]
    public void Geometry_IsSharedBetweenUseInstances()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "><defs><path id=\"p\" d=\"M0 0 H5 V5 Z\"/><rect id=\"r\" width=\"5\" height=\"5\"/></defs>"
            + "<use href=\"#p\" fill=\"#ff0000\"/><use href=\"#p\" x=\"10\" fill=\"#00ff00\"/>"
            + "<use href=\"#r\" fill=\"#ff0000\"/><use href=\"#r\" y=\"10\" fill=\"#ff0000\"/></svg>");

        SvgRenderNode[] nodes = document.Nodes.ToArray();
        Assert.Equal(4, nodes.Length);
        Assert.Same(nodes[0].Path, nodes[1].Path);
        Assert.Same(nodes[2].Path, nodes[3].Path);
        Assert.Same(nodes[2].Fill, nodes[3].Fill);
        Assert.NotSame(nodes[0].Fill, nodes[1].Fill);
        Assert.NotEqual(nodes[2].TransformIndex, nodes[3].TransformIndex);
    }

    [Theory]
    [WithBlankImage(60, 40, PixelTypes.Rgba32)]
    public void CurrentColor_OptionIsTheInitialColor<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + " width=\"60\" height=\"40\"><rect width=\"30\" height=\"40\" fill=\"currentColor\"/>"
            + "<rect x=\"30\" width=\"30\" height=\"40\" fill=\"currentColor\" color=\"#00ff00\"/></svg>",
            new SvgLoadOptions { CurrentColor = Color.Blue });

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));

        Assert.Equal(Color.Blue.ToPixel<TPixel>(), image[15, 20]);
        Assert.Equal(Color.Lime.ToPixel<TPixel>(), image[45, 20]);
    }

    // A gradient in bounding box units cannot map onto a bounding box without width or height, such as the box of a
    // vertical line. SVG ignores the gradient: the paint uses its fallback, or paints nothing without one, as in browsers
    // and resvg. resvg test suite: painting/stroke/gradient-with-objectBoundingBox-and-fallback-on-lines.svg.
    [Theory]
    [WithBlankImage(40, 40, PixelTypes.Rgba32)]
    public void Paint_BoundingBoxGradientOnALineUsesTheFallback<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + " width=\"40\" height=\"40\">"
            + "<linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></linearGradient>"
            + "<line x1=\"10\" y1=\"0\" x2=\"10\" y2=\"40\" stroke=\"url(#g) #00ff00\" stroke-width=\"4\"/>"
            + "<line x1=\"30\" y1=\"0\" x2=\"30\" y2=\"40\" stroke=\"url(#g)\" stroke-width=\"4\"/></svg>");

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));

        Assert.Equal(Color.Lime.ToPixel<TPixel>(), image[10, 20]);
        Assert.Equal(Color.Transparent.ToPixel<TPixel>(), image[30, 20]);
    }

    [Theory]
    [WithBlankImage(300, 300, PixelTypes.Rgba32)]
    public void DrawSvg_ConcurrentRendersOfOneDocumentMatch<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // A loaded document is shared state: renders on several threads at once must equal a render on one thread.
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath(TestImages.Svg.StyledLandscape));
        RectangleF destination = new(0, 0, 300, 300);

        using Image<TPixel> expected = provider.GetImage();
        expected.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, destination)));

        Image<TPixel>[] images = new Image<TPixel>[8];
        try
        {
            Parallel.For(
                0,
                images.Length,
                new ParallelOptions { MaxDegreeOfParallelism = images.Length },
                i =>
                {
                    Image<TPixel> image = provider.GetImage();
                    image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, destination)));
                    images[i] = image;
                });

            foreach (Image<TPixel> image in images)
            {
                ImageComparer.Exact.VerifySimilarity(expected, image);
            }
        }
        finally
        {
            foreach (Image<TPixel> image in images)
            {
                image?.Dispose();
            }
        }
    }

    [Theory]
    [WithBlankImage(20, 20, PixelTypes.Rgba32)]
    public void DrawSvg_EmptyDestinationDrawsNothing<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + " viewBox=\"0 0 1 1\"><rect width=\"1\" height=\"1\"/></svg>");

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(x => x.Paint(canvas =>
        {
            canvas.DrawSvg(document, new RectangleF(5, 5, 0, 10));
            canvas.DrawSvg(document, new RectangleF(5, 5, 10, -1));
        }));

        Assert.Equal(Color.Transparent.ToPixel<TPixel>(), image[7, 7]);
        Assert.Throws<ArgumentNullException>(() => image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(null!, PointF.Empty))));
    }

    [Fact]
    public void Options_Phase2Defaults()
    {
        SvgLoadOptions options = new();
        SvgLimits limits = new();

        Assert.Null(options.UserStyleSheet);
        Assert.Equal<string>(["en"], options.Languages);
        Assert.Null(options.ResourceBaseDirectory);
        Assert.Equal(64L * 1024 * 1024, limits.MaxImagePixels);
        Assert.Equal(4096 * 4096, limits.MaxPatternTilePixels);
    }

    [Fact]
    public void Options_RejectInvalidPhase2Values()
    {
        SvgLoadOptions options = new();
        SvgLimits limits = new();

        Assert.Throws<ArgumentNullException>(() => options.Languages = null!);
        Assert.Throws<ArgumentException>(() => options.Languages = ["en", null!]);
        Assert.Throws<ArgumentException>(() => options.Languages = [string.Empty]);
        Assert.Throws<ArgumentException>(() => options.ResourceBaseDirectory = string.Empty);
        Assert.Throws<ArgumentException>(() => options.ResourceBaseDirectory = "  ");
        Assert.Throws<ArgumentOutOfRangeException>(() => limits.MaxImagePixels = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => limits.MaxPatternTilePixels = -1);

        // A failed set keeps the previous value.
        Assert.Equal<string>(["en"], options.Languages);
        Assert.Null(options.ResourceBaseDirectory);
    }

    [Fact]
    public void Options_AcceptValidPhase2Values()
    {
        List<string> languages = ["de-CH", "fr"];
        SvgLoadOptions options = new()
        {
            UserStyleSheet = "rect { fill: red }",
            Languages = languages,
            ResourceBaseDirectory = "images",
        };

        // The options keep a copy, so a later change to the source list has no effect.
        languages.Add("it");

        Assert.Equal("rect { fill: red }", options.UserStyleSheet);
        Assert.Equal<string>(["de-CH", "fr"], options.Languages);
        Assert.Equal("images", options.ResourceBaseDirectory);

        options.Languages = [];
        options.ResourceBaseDirectory = null;
        options.UserStyleSheet = string.Empty;
        Assert.Empty(options.Languages);
        Assert.Null(options.ResourceBaseDirectory);
    }

    // Loading from a file must never enable reading other files.
    [Fact]
    public void Load_FromAPathDoesNotSetTheResourceBaseDirectory()
    {
        SvgLoadOptions options = new();

        _ = SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/shapes.svg"), options);

        Assert.Null(options.ResourceBaseDirectory);
    }

    // CSS Values 4: rem is the root font size, Q a quarter-millimeter, ch half the font size without font metrics, and the
    // viewport units refer to the initial containing block, which is the intrinsic size of a standalone document.
    [Theory]
    [InlineData("width=\"2rem\" height=\"1rem\"", 40F, 20F)]
    [InlineData("width=\"101.6Q\" height=\"50.8Q\"", 96F, 48F)]
    [InlineData("width=\"3ch\" height=\"1ch\"", 15F, 5F)]
    [InlineData("width=\"10vw\" height=\"10vh\"", 40F, 20F)]
    [InlineData("width=\"10vmin\" height=\"10vmax\"", 20F, 40F)]
    public void Units_ResolveTheCssUnits(string attributes, float width, float height)
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + $" width=\"400\" height=\"200\" font-size=\"20\"><rect {attributes} font-size=\"10\"/></svg>");

        RectangleF bounds = document.Nodes[0].Path!.Bounds;
        Assert.Equal(width, bounds.Width, 3);
        Assert.Equal(height, bounds.Height, 3);
    }

    // rem in the font size of the root element refers to the initial font size of 16px.
    [Fact]
    public void Units_RemOnTheRootFontSizeRefersToTheInitialFontSize()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + " width=\"1rem\" height=\"100\" font-size=\"2rem\"><rect width=\"1rem\" height=\"1rem\"/></svg>");

        Assert.Equal(32F, document.Size.Width, 3);
        Assert.Equal(32F, document.Nodes[0].Path!.Bounds.Width, 3);
    }

    // Without the encoding provider, the XML reader rejects the encoding name before it reads the root element.
    [Fact]
    public void Load_ReadsALegacyEncodingThatTheXmlDeclarationNames()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("<?xml version=\"1.0\" encoding=\"windows-1251\"?>" + SvgOpen + " width=\"7\" height=\"5\"/>");
        using MemoryStream stream = new(bytes);

        SvgDocument document = SvgDocument.Load(stream);

        Assert.Equal(new SizeF(7, 5), document.Size);
    }

    // Chromium resolves the percentages of transform-origin on a gradient in bounding box units against the viewport, the
    // view-box reference box: in a 200 x 200 viewport, 50% and 100px give the same render, which differs from 0.5.
    [Fact]
    public void TransformOrigin_OfABoundingBoxGradientResolvesPercentagesAgainstTheViewport()
    {
        static Matrix4x4 GradientTransform(string origin)
        {
            SvgDocument document = SvgDocument.Parse(
                SvgOpen + $" width=\"200\" height=\"200\"><radialGradient id=\"g\" gradientTransform=\"scale(2)\" transform-origin=\"{origin}\">"
                + "<stop offset=\"0\" stop-color=\"black\"/><stop offset=\"1\" stop-color=\"green\"/></radialGradient>"
                + "<rect x=\"20\" y=\"20\" width=\"160\" height=\"160\" fill=\"url(#g)\"/></svg>");

            return ((RadialGradientBrush)document.Nodes[0].Fill!).GradientTransform;
        }

        Assert.Equal(GradientTransform("100px 100px"), GradientTransform("50% 50%"));
        Assert.Equal(GradientTransform("100 100"), GradientTransform("center"));
        Assert.NotEqual(GradientTransform("0.5 0.5"), GradientTransform("50% 50%"));
    }
}
