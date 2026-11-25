// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests the rules of the compositing features of SVG phase 2 that a reference image does not show well: conditional
/// processing, the render model of clips and masks, reference cycles, limits, and concurrent renders.
/// </summary>
public class SvgCompositingTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\"";

    // SVG 1.1 conditional processing: a listed tag matches the language of the user when one is a prefix of the other at
    // a subtag boundary, ignoring case. An empty list fails.
    [Theory]
    [InlineData("en", "en", true)]
    [InlineData("en", "EN", true)]
    [InlineData("en", "en-GB", true)]
    [InlineData("en-US", "en", true)]
    [InlineData("en-US", "en-GB", false)]
    [InlineData("en", "eng", false)]
    [InlineData("en", "ru, en", true)]
    [InlineData("en", "ru,en", true)]
    [InlineData("en", "ru", false)]
    [InlineData("en", "", false)]
    [InlineData("en", " , ", false)]
    public void SystemLanguage_MatchesTheLanguagesOfTheOptions(string language, string systemLanguage, bool renders)
    {
        SvgLoadOptions options = new() { Languages = [language] };
        SvgDocument document = SvgDocument.Parse(SvgOpen + $"><rect width=\"10\" height=\"10\" systemLanguage=\"{systemLanguage}\"/></svg>", options);

        Assert.Equal(renders, document.Nodes.Length == 1);
    }

    [Fact]
    public void SystemLanguage_WithoutLanguagesMatchesNothing()
    {
        SvgLoadOptions options = new() { Languages = [] };

        Assert.Empty(SvgDocument.Parse(SvgOpen + "><rect width=\"10\" height=\"10\" systemLanguage=\"en\"/></svg>", options).Nodes.ToArray());
    }

    // Chromium supports the XHTML and MathML namespaces as extensions and passes an empty list; requiredFeatures is
    // ignored, as SVG 2 removed it.
    [Theory]
    [InlineData("requiredExtensions=\"\"", true)]
    [InlineData("requiredExtensions=\"http://www.w3.org/1999/xhtml\"", true)]
    [InlineData("requiredExtensions=\"http://www.w3.org/1998/Math/MathML http://www.w3.org/1999/xhtml\"", true)]
    [InlineData("requiredExtensions=\"http://example.org/bogus\"", false)]
    [InlineData("requiredExtensions=\"http://www.w3.org/1999/xhtml http://example.org/bogus\"", false)]
    [InlineData("requiredFeatures=\"http://example.org/bogus\"", true)]
    [InlineData("requiredFeatures=\"\"", true)]
    public void RequiredExtensionsAndFeatures_FollowChromium(string attribute, bool renders)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + $"><rect width=\"10\" height=\"10\" {attribute}/></svg>");

        Assert.Equal(renders, document.Nodes.Length == 1);
    }

    [Fact]
    public void Switch_RendersOnlyTheFirstCandidateWhoseConditionsPass()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "><switch>"
            + "<title>skipped</title>"
            + "<rect width=\"1\" height=\"1\" fill=\"red\" systemLanguage=\"xx\"/>"
            + "<rect width=\"2\" height=\"2\" fill=\"lime\"/>"
            + "<rect width=\"3\" height=\"3\" fill=\"red\"/>"
            + "</switch></svg>");

        SvgRenderNode node = Assert.Single(document.Nodes.ToArray());
        Assert.Equal(new RectangleF(0, 0, 2, 2), node.Bounds);
    }

    // A clip region that is one path with the non-zero rule becomes a canvas clip; a union of children needs a mask.
    [Fact]
    public void ClipPath_SinglePathIsAGeometryClipAndAUnionIsAnAlphaMask()
    {
        SvgDocument geometry = SvgDocument.Parse(
            SvgOpen + "><clipPath id=\"c\"><circle cx=\"10\" cy=\"10\" r=\"5\"/></clipPath><rect width=\"20\" height=\"20\" clip-path=\"url(#c)\"/></svg>");
        Assert.Equal(
            [SvgRenderNodeKind.BeginClip, SvgRenderNodeKind.Draw, SvgRenderNodeKind.EndClip],
            geometry.Nodes.ToArray().Select(n => n.Kind));

        SvgDocument union = SvgDocument.Parse(
            SvgOpen + "><clipPath id=\"c\"><circle cx=\"5\" cy=\"5\" r=\"5\"/><circle cx=\"15\" cy=\"15\" r=\"5\"/></clipPath>"
            + "<rect width=\"20\" height=\"20\" clip-path=\"url(#c)\"/></svg>");
        SvgRenderNode[] nodes = union.Nodes.ToArray();
        Assert.Equal(
            [
                SvgRenderNodeKind.BeginLayer, SvgRenderNodeKind.Draw, SvgRenderNodeKind.BeginMask, SvgRenderNodeKind.Draw,
                SvgRenderNodeKind.Draw, SvgRenderNodeKind.EndMask, SvgRenderNodeKind.EndLayer
            ],
            nodes.Select(n => n.Kind));
        Assert.Equal(SvgMaskMode.Alpha, nodes[2].MaskMode);
        Assert.Equal(nodes[0].Bounds, nodes[2].Bounds);
    }

    // The mask layer has the bounds of the masked layer, cut by the mask region (default -10% to 120% of the box).
    [Fact]
    public void Mask_LayerBoundsAreTheContentCutByTheMaskRegion()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "><mask id=\"m\" x=\"0\" y=\"0\" width=\"0.5\" height=\"1\"><rect width=\"20\" height=\"20\" fill=\"white\"/></mask>"
            + "<rect x=\"2\" y=\"2\" width=\"16\" height=\"16\" mask=\"url(#m)\"/></svg>");
        SvgRenderNode[] nodes = document.Nodes.ToArray();

        Assert.Equal(SvgRenderNodeKind.BeginLayer, nodes[0].Kind);
        Assert.Equal(new RectangleF(2, 2, 8, 16), nodes[0].Bounds);
        SvgRenderNode mask = Assert.Single(nodes, n => n.Kind == SvgRenderNodeKind.BeginMask);
        Assert.Equal(SvgMaskMode.Luminance, mask.MaskMode);
        Assert.Equal(nodes[0].Bounds, mask.Bounds);
    }

    // A clip or mask region without area, or objectBoundingBox units on an element without a box, render nothing.
    [Theory]
    [InlineData("<clipPath id=\"c\"/><rect width=\"10\" height=\"10\" clip-path=\"url(#c)\"/>")]
    [InlineData("<clipPath id=\"c\"><line x2=\"10\" y2=\"10\"/></clipPath><rect width=\"10\" height=\"10\" clip-path=\"url(#c)\"/>")]
    [InlineData("<clipPath id=\"c\" clipPathUnits=\"objectBoundingBox\"><rect width=\"1\" height=\"1\"/></clipPath>"
        + "<line x2=\"10\" stroke=\"black\" clip-path=\"url(#c)\"/>")]
    [InlineData("<mask id=\"m\" width=\"0\"><rect width=\"10\" height=\"10\" fill=\"white\"/></mask><rect width=\"10\" height=\"10\" mask=\"url(#m)\"/>")]
    [InlineData("<mask id=\"m\"><rect width=\"10\" height=\"10\" fill=\"white\"/></mask><line x2=\"10\" stroke=\"black\" mask=\"url(#m)\"/>")]
    public void ClipAndMask_WithoutAreaRenderNothing(string content)
        => Assert.Empty(SvgDocument.Parse(SvgOpen + ">" + content + "</svg>").Nodes.ToArray());

    // Browsers ignore a reference that names nothing, names an element of another kind, or is a cycle.
    [Theory]
    [InlineData("<rect width=\"10\" height=\"10\" clip-path=\"url(#missing)\" mask=\"url(#missing)\"/>")]
    [InlineData("<g id=\"g\"/><rect width=\"10\" height=\"10\" clip-path=\"url(#g)\" mask=\"url(#g)\"/>")]
    [InlineData("<clipPath id=\"c\" clip-path=\"url(#c)\"><rect width=\"10\" height=\"10\"/></clipPath><rect width=\"10\" height=\"10\" clip-path=\"url(#c)\"/>")]
    [InlineData("<mask id=\"m\"><rect width=\"10\" height=\"10\" fill=\"white\" mask=\"url(#m)\"/></mask><rect width=\"10\" height=\"10\" mask=\"url(#m)\"/>")]
    public void InvalidReferences_AreIgnored(string content)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + ">" + content + "</svg>");

        Assert.Contains(document.Nodes.ToArray(), n => n.Kind == SvgRenderNodeKind.Draw && n.Bounds == new RectangleF(0, 0, 10, 10));
    }

    // Cycles through every reference kind that this package follows, alone and mixed with use, load and render.
    [Theory]
    [InlineData("<clipPath id=\"a\"><rect width=\"10\" height=\"10\" clip-path=\"url(#b)\"/></clipPath>"
        + "<clipPath id=\"b\"><rect width=\"10\" height=\"10\" clip-path=\"url(#a)\"/></clipPath><rect width=\"20\" height=\"20\" clip-path=\"url(#a)\"/>")]
    [InlineData("<mask id=\"a\" mask=\"url(#b)\"><rect width=\"10\" height=\"10\" fill=\"white\"/></mask>"
        + "<mask id=\"b\" mask=\"url(#a)\"><rect width=\"10\" height=\"10\" fill=\"white\"/></mask><rect width=\"20\" height=\"20\" mask=\"url(#a)\"/>")]
    [InlineData("<mask id=\"a\"><g id=\"g\" clip-path=\"url(#c)\"><use href=\"#r\"/></g></mask>"
        + "<clipPath id=\"c\"><use href=\"#r\"/></clipPath><rect id=\"r\" width=\"10\" height=\"10\" mask=\"url(#a)\"/>")]
    [InlineData("<mask id=\"a\"><use href=\"#u\"/></mask><use id=\"u\" href=\"#r\" mask=\"url(#a)\"/><rect id=\"r\" width=\"10\" height=\"10\"/>")]
    public void ReferenceCycles_LoadAndRender(string content)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + ">" + content + "</svg>");

        using Image<Rgba32> image = new(20, 20);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));
    }

    [Fact]
    public void Limits_MaxNestingDepthStopsLongClipAndMaskChains()
    {
        StringBuilder clips = new(SvgOpen + ">");
        StringBuilder masks = new(SvgOpen + ">");
        for (int i = 0; i < 40; i++)
        {
            clips.Append("<clipPath id=\"c").Append(i).Append("\" clip-path=\"url(#c").Append(i + 1).Append(")\"><rect width=\"10\" height=\"10\"/></clipPath>");
            masks.Append("<mask id=\"m").Append(i).Append("\" mask=\"url(#m").Append(i + 1).Append(")\"><rect width=\"10\" height=\"10\" fill=\"white\"/></mask>");
        }

        clips.Append("<rect width=\"10\" height=\"10\" clip-path=\"url(#c0)\"/></svg>");
        masks.Append("<rect width=\"10\" height=\"10\" mask=\"url(#m0)\"/></svg>");

        Assert.NotEmpty(SvgDocument.Parse(clips.ToString()).Nodes.ToArray());
        Assert.NotEmpty(SvgDocument.Parse(masks.ToString()).Nodes.ToArray());

        SvgLoadOptions options = new() { Limits = new SvgLimits { MaxNestingDepth = 30 } };
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(clips.ToString(), options));
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(masks.ToString(), options));
    }

    [Fact]
    public void Limits_MaxNodesStopsExponentialClipExpansion()
    {
        // Each clipPath has four children that are clipped by the next clipPath, so twelve levels visit 4^12 children.
        StringBuilder svg = new(SvgOpen + "><clipPath id=\"c12\"><rect width=\"10\" height=\"10\"/></clipPath>");
        for (int level = 11; level >= 0; level--)
        {
            svg.Append("<clipPath id=\"c").Append(level).Append("\">");
            for (int i = 0; i < 4; i++)
            {
                svg.Append("<rect width=\"10\" height=\"10\" clip-path=\"url(#c").Append(level + 1).Append(")\"/>");
            }

            svg.Append("</clipPath>");
        }

        svg.Append("<rect width=\"10\" height=\"10\" clip-path=\"url(#c0)\"/></svg>");

        SvgLoadOptions options = new() { Limits = new SvgLimits { MaxNodes = 100_000 } };
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(svg.ToString(), options));
    }

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32)]
    public void DrawSvg_ConcurrentRendersWithClipsMasksAndBlendingMatch<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument[] documents =
        [
            SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/clip-path.svg")),
            SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/mask.svg")),
            SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/mix-blend-mode.svg")),
        ];

        RectangleF destination = new(0, 0, 200, 200);
        foreach (SvgDocument document in documents)
        {
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
    }

    // The caller's graphics options apply to each draw outside every layer, as in phase 1, and a layer with a blend mode
    // takes the caller's blend percentage. With a caller blend percentage of 0.5 over white: the blue rectangle draws to
    // (127, 127, 255); the yellow circle multiplies with it to (127, 127, 0) and composites at 50% to (127, 127, 127).
    [Fact]
    public void BlendMode_ComposesWithTheCallerBlendPercentage()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "><rect width=\"20\" height=\"20\" fill=\"#0000ff\"/>"
            + "<circle cx=\"10\" cy=\"10\" r=\"8\" fill=\"#ffff00\" style=\"mix-blend-mode: multiply\"/></svg>");

        using Image<Rgba32> image = new(20, 20, Color.White.ToPixel<Rgba32>());
        image.Mutate(x => x.Paint(
            new DrawingOptions { GraphicsOptions = new GraphicsOptions { BlendPercentage = 0.5F } },
            canvas => canvas.DrawSvg(document, PointF.Empty)));

        Rgba32 inside = image[10, 10];
        Rgba32 outside = image[1, 1];
        Assert.InRange(inside.R, 126, 129);
        Assert.InRange(inside.G, 126, 129);
        Assert.InRange(inside.B, 126, 129);
        Assert.InRange(outside.B, 254, 255);
        Assert.InRange(outside.R, 126, 129);
    }

    // Paint order: the stroke paints before the fill, so the fill covers the inner half of the stroke.
    [Fact]
    public void PaintOrder_StrokeFirstLetsTheFillCoverTheInnerStroke()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "><rect x=\"5\" y=\"5\" width=\"10\" height=\"10\" fill=\"#0000ff\" stroke=\"#ff0000\" stroke-width=\"4\" paint-order=\"stroke\"/></svg>");

        using Image<Rgba32> image = new(20, 20);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));

        Assert.Equal(new Rgba32(0, 0, 255, 255), image[6, 10]);
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[4, 10]);
    }
}
