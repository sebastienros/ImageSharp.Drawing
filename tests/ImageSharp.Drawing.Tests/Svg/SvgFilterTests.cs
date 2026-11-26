// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests the parsing of the filter elements and properties, the resolution of the filter graph, and the filter
/// primitives on small renders whose pixels follow from the formulas of Filter Effects Level 1 and Compositing and
/// Blending Level 1.
/// </summary>
public class SvgFilterTests
{
    private const string SvgOpen =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"20\" height=\"20\">";

    [Fact]
    public void Reader_ParsesTheFilterElement()
    {
        SvgElementTree tree = Read(
            "<filter id=\"f\" x=\"1\" y=\"10%\" width=\"-2\" height=\"3em\" filterUnits=\"userSpaceOnUse\""
            + " primitiveUnits=\"objectBoundingBox\" xlink:href=\"#g\"/>");
        ref readonly SvgFilterAttributes filter = ref tree.GetFilter(Index(tree, "f"));

        Assert.Equal(new SvgLength(1F, SvgLengthUnit.Number), filter.X);
        Assert.Equal(new SvgLength(10F, SvgLengthUnit.Percent), filter.Y);
        Assert.Equal(new SvgLength(-2F, SvgLengthUnit.Number), filter.Width);
        Assert.Equal(new SvgLength(3F, SvgLengthUnit.Em), filter.Height);
        Assert.Equal(SvgCoordinateUnits.UserSpaceOnUse, filter.Units);
        Assert.Equal(SvgCoordinateUnits.ObjectBoundingBox, filter.PrimitiveUnits);
        Assert.Equal("g", filter.Href);
    }

    [Fact]
    public void Reader_ParsesPrimitiveAttributes()
    {
        SvgElementTree tree = Read(
            "<filter>"
            + "<feGaussianBlur id=\"b\" in=\" SourceAlpha \" result=\"r\" x=\"5\" width=\"50%\" stdDeviation=\"1, 2\" edgeMode=\"wrap\"/>"
            + "<feColorMatrix id=\"m\" type=\"saturate\" values=\"0.5 x\"/>"
            + "<feSpotLight id=\"l\" x=\"3\" y=\"4\" z=\"5\"/>"
            + "<feImage id=\"i\" href=\" image.png#frag \" preserveAspectRatio=\"xMinYMin slice\"/>"
            + "</filter>");

        int blur = Index(tree, "b");
        ref readonly SvgFilterPrimitiveAttributes attributes = ref tree.GetFilterPrimitive(blur);
        Assert.Equal("SourceAlpha", attributes.In);
        Assert.Equal("r", attributes.Result);
        Assert.Equal(new SvgLength(5F, SvgLengthUnit.Number), attributes.X);
        Assert.Equal(new SvgLength(50F, SvgLengthUnit.Percent), attributes.Width);
        Assert.False(attributes.Y.IsSpecified);

        Assert.True(tree.TryGetFilterValue(blur, SvgAttributeId.StdDeviation, out SvgFilterAttributeValue deviation));
        Assert.True(deviation.IsNumberList);
        Assert.Equal([1F, 2F], tree.GetFilterNumbers(in deviation).ToArray());
        Assert.True(tree.TryGetFilterValue(blur, SvgAttributeId.EdgeMode, out SvgFilterAttributeValue edgeMode));
        Assert.Equal("wrap", edgeMode.Text);
        Assert.False(tree.TryGetFilterValue(blur, SvgAttributeId.Values, out _));

        // The type attribute of a primitive is a value of its own; an invalid number list keeps its text only.
        int matrix = Index(tree, "m");
        Assert.True(tree.TryGetFilterValue(matrix, SvgAttributeId.Type, out SvgFilterAttributeValue type));
        Assert.Equal("saturate", type.Text);
        Assert.True(tree.TryGetFilterValue(matrix, SvgAttributeId.Values, out SvgFilterAttributeValue values));
        Assert.False(values.IsNumberList);
        Assert.Equal(0, values.NumberCount);

        // Light sources have numbers for x and y.
        int light = Index(tree, "l");
        Assert.False(tree.GetFilterPrimitive(light).X.IsSpecified);
        Assert.True(tree.TryGetFilterValue(light, SvgAttributeId.X, out SvgFilterAttributeValue x));
        Assert.Equal([3F], tree.GetFilterNumbers(in x).ToArray());

        // feImage keeps its whole URL, as image does.
        ref readonly SvgFilterPrimitiveAttributes image = ref tree.GetFilterPrimitive(Index(tree, "i"));
        Assert.Equal("image.png#frag", image.Href);
        Assert.True(image.HasPreserveAspectRatio);
    }

    [Theory]
    [InlineData("none", 0)]
    [InlineData("url(#a)", 1)]
    [InlineData("url(#a) url( '#b' )", 2)]
    [InlineData("url(#a)blur(2px)", 2)]
    [InlineData("blur()", 1)]
    [InlineData("blur(0)", 1)]
    [InlineData("hue-rotate(0) hue-rotate(1turn) hue-rotate()", 3)]
    [InlineData("grayscale(50%) sepia(2) OPACITY()", 3)]
    [InlineData("drop-shadow(red 1px 2px)", 1)]
    [InlineData("drop-shadow(1px 2px 3px rgb(1, 2, 3))", 1)]
    [InlineData("drop-shadow(0 0)", 1)]
    public void FilterProperty_AcceptsTheGrammarOfFilterEffects(string value, int count)
    {
        SvgSpecifiedStyle style = default;
        Assert.True(new SvgPropertyParser().TryParse(SvgPropertyId.Filter, value, ref style));
        SvgFilterList list = style.Extended!.Filters!.Filter;
        Assert.Equal(count, list?.Functions.Length ?? 0);
    }

    [Theory]
    [InlineData("blur(-1px)")]
    [InlineData("blur(50%)")]
    [InlineData("blur(4 2)")]
    [InlineData("blur(4)")]
    [InlineData("hue-rotate(45)")]
    [InlineData("grayscale(-1)")]
    [InlineData("drop-shadow(red, 10, 15)")]
    [InlineData("drop-shadow(1px)")]
    [InlineData("drop-shadow()")]
    [InlineData("drop-shadow(1px 2px 3px 4px)")]
    [InlineData("drop-shadow(1px 2px -3px)")]
    [InlineData("drop-shadow(1px red 2px)")]
    [InlineData("grayscale() hue-rotate(random) opacity(0.5)")]
    [InlineData("url(#a), url(#b)")]
    [InlineData("unknown(1)")]
    [InlineData("url(#a")]
    public void FilterProperty_RejectsInvalidValues(string value)
    {
        SvgSpecifiedStyle style = default;
        Assert.False(new SvgPropertyParser().TryParse(SvgPropertyId.Filter, value, ref style));
        Assert.False(style.IsSpecified(SvgPropertyId.Filter));
    }

    [Fact]
    public void FilterProperty_StoresTheArgumentsOfFunctions()
    {
        SvgSpecifiedStyle style = default;
        const string value = "url(#f) grayscale(50%) hue-rotate(0.5turn) drop-shadow(2px 3px 4px blue) brightness()";
        Assert.True(new SvgPropertyParser().TryParse(SvgPropertyId.Filter, value, ref style));
        ReadOnlySpan<SvgFilterFunction> functions = style.Extended!.Filters!.Filter!.Functions;

        Assert.Equal("f", functions[0].Reference.Id);
        Assert.Equal(SvgFilterFunctionKind.Grayscale, functions[1].Kind);
        Assert.Equal(0.5F, functions[1].Amount);
        Assert.Equal(180F, functions[2].Amount, 3);
        Assert.Equal(new SvgLength(2F, SvgLengthUnit.Px), functions[3].OffsetX);
        Assert.Equal(new SvgLength(3F, SvgLengthUnit.Px), functions[3].OffsetY);
        Assert.Equal(new SvgLength(4F, SvgLengthUnit.Px), functions[3].Radius);
        Assert.Equal(SvgPaintKind.Color, functions[3].Color.Kind);
        Assert.Equal(1F, functions[4].Amount);
    }

    [Fact]
    public void FilterProperties_HaveTheirInheritanceAndInitialValues()
    {
        SvgElementTree tree = Read(
            "<g id=\"g\" color-interpolation-filters=\"sRGB\" flood-color=\"red\" flood-opacity=\"0.5\" lighting-color=\"blue\" filter=\"url(#f)\">"
            + "<rect id=\"r\"/>"
            + "<rect id=\"s\" style=\"flood-opacity: 2; flood-color: currentColor; color-interpolation-filters: auto\"/>"
            + "</g>");
        SvgResolver resolver = new(tree, new SvgLoadOptions());

        SvgComputedStyle initial = resolver.ComputeDocumentStyle(0);
        Assert.Null(initial.Filter);
        Assert.Equal(SvgColorInterpolation.LinearRgb, initial.ColorInterpolationFilters);
        Assert.Equal(1F, initial.FilterColors.FloodOpacity);
        Assert.Equal(Color.Black, initial.FilterColors.FloodColor.Color);
        Assert.Equal(Color.White, initial.FilterColors.LightingColor.Color);

        SvgComputedStyle group = resolver.ComputeDocumentStyle(Index(tree, "g"));
        Assert.True(group.Filter!.IsSingleUrl);
        Assert.Equal(0.5F, group.FilterColors.FloodOpacity);

        // color-interpolation-filters inherits; filter, flood-color, flood-opacity, and lighting-color do not.
        SvgComputedStyle child = resolver.ComputeDocumentStyle(Index(tree, "r"));
        Assert.Null(child.Filter);
        Assert.Equal(SvgColorInterpolation.SRgb, child.ColorInterpolationFilters);
        Assert.Equal(1F, child.FilterColors.FloodOpacity);
        Assert.Equal(Color.Black, child.FilterColors.FloodColor.Color);

        SvgComputedStyle styled = resolver.ComputeDocumentStyle(Index(tree, "s"));
        Assert.Equal(1F, styled.FilterColors.FloodOpacity);
        Assert.Equal(SvgPaintKind.CurrentColor, styled.FilterColors.FloodColor.Kind);
        Assert.Equal(SvgColorInterpolation.Auto, styled.ColorInterpolationFilters);
    }

    [Fact]
    public void Graph_ResolvesInputsAndResults()
    {
        SvgFilterGraph graph = GetGraph(
            "<filter id=\"f\">"
            + "<feOffset/>"
            + "<feOffset in=\"SourceAlpha\" result=\"a\"/>"
            + "<feOffset in=\"unknown\"/>"
            + "<feOffset in=\"BackgroundImage\" result=\"a\"/>"
            + "<feComposite in=\"a\" in2=\"SourceGraphic\"/>"
            + "<feMerge><feMergeNode/><feMergeNode in=\"a\"/><feMergeNode in=\"SourceAlpha\"/></feMerge>"
            + "</filter>"
            + "<rect width=\"10\" height=\"10\" filter=\"url(#f)\"/>");

        // A missing input is the previous result, or SourceGraphic first; a name that matches nothing, and the
        // unsupported BackgroundImage, too; a result name refers to the most recent primitive with it.
        SvgFilterPrimitive[] primitives = graph.Primitives;
        Assert.Equal([SvgFilterPrimitive.SourceGraphic], primitives[0].Inputs.ToArray());
        Assert.Equal([SvgFilterPrimitive.SourceAlpha], primitives[1].Inputs.ToArray());
        Assert.Equal([1], primitives[2].Inputs.ToArray());
        Assert.Equal([2], primitives[3].Inputs.ToArray());
        Assert.Equal([3, SvgFilterPrimitive.SourceGraphic], primitives[4].Inputs.ToArray());
        Assert.Equal([4, 3, SvgFilterPrimitive.SourceAlpha], primitives[5].Inputs.ToArray());

        Assert.True(graph.UsesSource);
        Assert.Equal([-1, 2, 3, 5, 5, 6], graph.LastUse);
    }

    [Fact]
    public void Graph_AppliesTheDefaultSubregions()
    {
        SvgFilterGraph graph = GetGraph(
            "<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"100\" height=\"100\">"
            + "<feFlood x=\"10\" y=\"10\" width=\"20\" height=\"20\" result=\"a\"/>"
            + "<feFlood x=\"50\" y=\"40\" width=\"10\" height=\"10\" result=\"b\"/>"
            + "<feComposite in=\"a\" in2=\"b\"/>"
            + "<feComposite in=\"a\" in2=\"b\" x=\"0\" height=\"5\"/>"
            + "<feOffset in=\"SourceGraphic\"/>"
            + "<feFlood width=\"0\"/>"
            + "</filter>"
            + "<rect width=\"10\" height=\"10\" filter=\"url(#f)\"/>");

        SvgFilterPrimitive[] primitives = graph.Primitives;

        // The union of the inputs; each given attribute replaces one value of it; a standard input gives the region.
        Assert.Equal(RectangleF.FromLTRB(10F, 10F, 60F, 50F), primitives[2].Subregion);
        Assert.Equal(new RectangleF(0F, 10F, 50F, 5F), primitives[3].Subregion);
        Assert.Equal(new RectangleF(0F, 0F, 100F, 100F), primitives[4].Subregion);

        // A zero width disables the primitive: its result is transparent.
        Assert.IsType<SvgFeTransparent>(primitives[5]);
    }

    [Fact]
    public void Graph_ReadsTheColorSpaceFromTheFilterElement()
    {
        SvgFilterGraph graph = GetGraph(
            "<filter id=\"f\" color-interpolation-filters=\"sRGB\">"
            + "<feOffset dx=\"1\"/>"
            + "<feOffset dx=\"1\" color-interpolation-filters=\"linearRGB\"/>"
            + "<feOffset dx=\"1\" color-interpolation-filters=\"auto\"/>"
            + "</filter>"
            + "<g color-interpolation-filters=\"linearRGB\"><rect width=\"10\" height=\"10\" filter=\"url(#f)\"/></g>");

        Assert.Equal(SvgColorInterpolation.SRgb, graph.Primitives[0].ColorSpace);
        Assert.Equal(SvgColorInterpolation.LinearRgb, graph.Primitives[1].ColorSpace);
        Assert.Equal(SvgColorInterpolation.SRgb, graph.Primitives[2].ColorSpace);
    }

    [Theory]

    // Filter Effects: a reference that names no filter makes the filter ignored, so the element draws.
    [InlineData("<rect width=\"10\" height=\"10\" filter=\"url(#missing)\"/>", false)]
    [InlineData("<linearGradient id=\"f\"/><rect width=\"10\" height=\"10\" filter=\"url(#f)\"/>", false)]

    // A filter that this build does not render draws the element without it.
    [InlineData("<filter id=\"f\"><feTile/></filter><rect width=\"10\" height=\"10\" filter=\"url(#f)\"/>", false)]
    [InlineData("<rect width=\"10\" height=\"10\" filter=\"blur(2px)\"/>", false)]

    // An element without content still renders through a region in user space.
    [InlineData("<filter id=\"f\" filterUnits=\"userSpaceOnUse\"><feFlood/></filter><g filter=\"url(#f)\"/>", true)]
    public void Resolve_DecidesHowAFilterApplies(string content, bool filtered)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + content + "</svg>");
        SvgRenderNodeKind expected = filtered ? SvgRenderNodeKind.Filter : SvgRenderNodeKind.Draw;
        Assert.Equal(expected, Assert.Single(document.Nodes.ToArray()).Kind);
    }

    [Theory]
    [InlineData("<filter id=\"f\"/><rect width=\"10\" height=\"10\" filter=\"url(#f)\"/>")]
    [InlineData("<filter id=\"f\"><desc/></filter><rect width=\"10\" height=\"10\" filter=\"url(#f)\"/>")]
    [InlineData("<filter id=\"f\" width=\"0\"><feFlood/></filter><rect width=\"10\" height=\"10\" filter=\"url(#f)\"/>")]
    [InlineData("<filter id=\"f\"><feFlood/></filter><g filter=\"url(#f)\"/>")]
    [InlineData("<filter id=\"f\" filterUnits=\"userSpaceOnUse\" primitiveUnits=\"objectBoundingBox\"><feFlood/></filter><g filter=\"url(#f)\"/>")]
    public void Resolve_DoesNotDrawAnElementWhoseFilterHasNoPrimitivesOrNoRegion(string content)
        => Assert.True(SvgDocument.Parse(SvgOpen + content + "</svg>").Nodes.IsEmpty);

    [Fact]
    public void Resolve_BoundsLayersByTheFilterRegion()
    {
        // The blurred square spans 8 to 12; its region in bounding box units spans 4 to 16, so the group layer that the
        // opacity needs covers the region, not only the geometry.
        SvgDocument document = SvgDocument.Parse(
            SvgOpen
            + "<filter id=\"f\" x=\"-1\" y=\"-1\" width=\"3\" height=\"3\"><feGaussianBlur stdDeviation=\"1\"/></filter>"
            + "<g opacity=\"0.5\" transform=\"translate(1 0)\">"
            + "<rect x=\"8\" y=\"8\" width=\"4\" height=\"4\" filter=\"url(#f)\"/>"
            + "<rect x=\"9\" y=\"9\" width=\"1\" height=\"1\"/>"
            + "</g></svg>");

        SvgRenderNode[] nodes = document.Nodes.ToArray();
        Assert.Equal(SvgRenderNodeKind.BeginLayer, nodes[0].Kind);
        Assert.Equal(RectangleF.FromLTRB(5F, 4F, 17F, 16F), nodes[0].Bounds);
        Assert.Equal(SvgRenderNodeKind.Filter, nodes[1].Kind);
        Assert.Equal(RectangleF.FromLTRB(5F, 4F, 17F, 16F), nodes[1].Bounds);

        // The content of the filter node is in the user space of the element, and the node holds its transform.
        SvgFilterNode filter = document.Filters[nodes[1].FilterIndex];
        Assert.Equal(new RectangleF(4F, 4F, 12F, 12F), Assert.Single(filter.Graphs).Region);
        Assert.NotNull(filter.Content);
    }

    [Fact]
    public void Resolve_MultipliesTheOpacityOfAFilteredElementIntoTheResult()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "<filter id=\"f\"><feOffset/></filter>"
            + "<g opacity=\"0.5\"><rect width=\"10\" height=\"10\" opacity=\"0.5\" filter=\"url(#f)\"/></g></svg>");

        SvgRenderNode node = Assert.Single(document.Nodes.ToArray());
        Assert.Equal(SvgRenderNodeKind.Filter, node.Kind);
        Assert.Equal(0.25F, node.Opacity);
    }

    [Fact]
    public void Flood_FillsTheSubregionWithTheFloodColorAndOpacity()
    {
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\" color-interpolation-filters=\"sRGB\">"
            + "<feFlood flood-color=\"#336699\" flood-opacity=\"0.5\" x=\"5\" width=\"10\"/></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertPixel(new Rgba32(51, 102, 153, 128), image[10, 10]);
        Assert.Equal(0, image[4, 10].A);
        Assert.Equal(0, image[15, 10].A);
    }

    [Fact]
    public void Flood_ComesOutExactInLinearRgb()
    {
        // The flood converts to linear light in floating point and back, so even a dark color is exact.
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" x=\"0\" y=\"0\" width=\"1\" height=\"1\"><feFlood flood-color=\"rgb(10, 20, 30)\"/></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertPixel(new Rgba32(10, 20, 30, 255), image[10, 10]);
    }

    [Theory]
    [InlineData("3", "-2", 13, 8, 255)]
    [InlineData("0.5", "0", 10, 10, 128)]
    [InlineData("0.5", "0", 11, 10, 255)]
    [InlineData("0.25", "0.25", 10, 10, 144)]
    public void Offset_MovesTheInputAndInterpolatesFractions(string dx, string dy, int x, int y, int alpha)
    {
        // A black square from 10 to 15. A fraction interpolates between the two nearest pixels on each axis.
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\"><feOffset dx=\"{dx}\" dy=\"{dy}\"/></filter>"
            + "<rect x=\"10\" y=\"10\" width=\"5\" height=\"5\" filter=\"url(#f)\"/>");

        Assert.InRange(image[x, y].A, alpha - 1, alpha + 1);
    }

    [Fact]
    public void Merge_CompositesItsNodesSourceOverInOrder()
    {
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" x=\"0\" y=\"0\" width=\"1\" height=\"1\" color-interpolation-filters=\"sRGB\">"
            + "<feFlood flood-color=\"#0000ff\" result=\"b\"/>"
            + "<feFlood flood-color=\"#ff0000\" flood-opacity=\"0.5\" result=\"r\"/>"
            + "<feMerge><feMergeNode in=\"b\"/><feMergeNode in=\"r\"/></feMerge></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        // r over b: red at half alpha over opaque blue.
        AssertPixel(new Rgba32(128, 0, 127, 255), image[10, 10]);
    }

    public static TheoryData<string, float[]> CompositeOperators { get; } = new()
    {
        // Premultiplied A = (0.8, 0, 0, 0.8), B = (0, 0, 0.4, 0.4); the expected values are the formulas of Filter
        // Effects applied to them: over A + B (1 - a), in A b, out A (1 - b), atop A b + B (1 - a),
        // xor A (1 - b) + B (1 - a), lighter A + B.
        { "over", [0.8F, 0F, 0.08F, 0.88F] },
        { "in", [0.32F, 0F, 0F, 0.32F] },
        { "out", [0.48F, 0F, 0F, 0.48F] },
        { "atop", [0.32F, 0F, 0.08F, 0.4F] },
        { "xor", [0.48F, 0F, 0.08F, 0.56F] },
        { "lighter", [0.8F, 0F, 0.4F, 1F] },
        { "arithmetic\" k1=\"1\" k2=\"0.5\" k3=\"0.5\" k4=\"0.1", [0.5F, 0.1F, 0.3F, 1F] },
    };

    [Theory]
    [MemberData(nameof(CompositeOperators))]
    public void Composite_FollowsTheOperatorFormulas(string op, float[] expected)
    {
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" x=\"0\" y=\"0\" width=\"1\" height=\"1\" color-interpolation-filters=\"sRGB\">"
            + "<feFlood flood-color=\"#ff0000\" flood-opacity=\"0.8\" result=\"a\"/>"
            + "<feFlood flood-color=\"#0000ff\" flood-opacity=\"0.4\" result=\"b\"/>"
            + $"<feComposite in=\"a\" in2=\"b\" operator=\"{op}\"/></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertPremultiplied(expected, image[10, 10]);
    }

    [Theory]
    [InlineData("normal", 255, 128, 0)]
    [InlineData("multiply", 128, 64, 0)]
    [InlineData("screen", 255, 192, 255)]
    [InlineData("darken", 128, 128, 0)]
    [InlineData("difference", 127, 0, 255)]
    [InlineData("invalid", 255, 128, 0)]
    public void Blend_BlendsTheSourceOverTheBackdrop(string mode, byte r, byte g, byte b)
    {
        // Opaque source (255, 128, 0) over opaque backdrop (128, 128, 255): the result is the blend function B(cb, cs).
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" x=\"0\" y=\"0\" width=\"1\" height=\"1\" color-interpolation-filters=\"sRGB\">"
            + "<feFlood flood-color=\"rgb(128, 128, 255)\" result=\"b\"/>"
            + "<feFlood flood-color=\"rgb(255, 128, 0)\" result=\"s\"/>"
            + $"<feBlend in=\"s\" in2=\"b\" mode=\"{mode}\"/></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        AssertPixel(new Rgba32(r, g, b, 255), image[10, 10]);
    }

    [Theory]
    [InlineData("type=\"luminanceToAlpha\"", 0, 0, 0, 54)]
    [InlineData("type=\"saturate\" values=\"0\"", 54, 54, 54, 255)]
    [InlineData("values=\"0 0 0 0 0.5  1 0 0 0 0  0 0 0 0 0  0 0 0 1 0\"", 128, 255, 0, 255)]
    [InlineData("values=\"1 0 0 0 0\"", 255, 0, 0, 255)]
    [InlineData("type=\"hueRotate\"", 255, 0, 0, 255)]
    public void ColorMatrix_TransformsStraightColors(string attributes, byte r, byte g, byte b, byte a)
    {
        using Image<Rgba32> image = Render(
            $"<filter id=\"f\" color-interpolation-filters=\"sRGB\"><feColorMatrix {attributes}/></filter>"
            + "<rect width=\"20\" height=\"20\" fill=\"#ff0000\" filter=\"url(#f)\"/>");

        AssertPixel(new Rgba32(r, g, b, a), image[10, 10]);
    }

    [Fact]
    public void Blur_SpreadsAPointAsAGaussian()
    {
        // Below 2 pixels the blur is a true Gaussian: the center of a blurred point keeps 1 / (2 pi s^2) of it.
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\" color-interpolation-filters=\"sRGB\">"
            + "<feGaussianBlur stdDeviation=\"1\"/></filter>"
            + "<rect x=\"10\" y=\"10\" width=\"1\" height=\"1\" filter=\"url(#f)\"/>");

        Assert.InRange(image[10, 10].A, 40, 41);
        Assert.Equal(image[9, 10].A, image[11, 10].A);
        Assert.Equal(image[10, 9].A, image[10, 11].A);
    }

    [Fact]
    public void Blur_WithBoxesKeepsTheInteriorAndHalvesAnEdge()
    {
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"-40\" y=\"-40\" width=\"100\" height=\"100\" color-interpolation-filters=\"sRGB\">"
            + "<feGaussianBlur stdDeviation=\"3 0\"/></filter>"
            + "<rect x=\"-40\" y=\"0\" width=\"50\" height=\"20\" filter=\"url(#f)\"/>",
            20,
            20);

        // The box size is d = floor(3 * 3 * sqrt(2 pi) / 4 + 0.5) = 6, so the blur reaches 3 d / 2 = 9 pixels: pixel 0 is
        // out of reach of the edge at x = 10 and keeps its value; across the edge the box blurs average to one half.
        Assert.Equal(255, image[0, 10].A);
        Assert.InRange(image[9, 10].A + image[10, 10].A, 250, 260);
    }

    [Fact]
    public void Blur_ZeroOrNegativeDeviationIsAPassThrough()
    {
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\"><feGaussianBlur stdDeviation=\"-2\"/></filter>"
            + "<rect x=\"5\" y=\"5\" width=\"10\" height=\"10\" fill=\"#ff0000\" filter=\"url(#f)\"/>");

        AssertPixel(new Rgba32(255, 0, 0, 255), image[5, 5]);
        Assert.Equal(0, image[4, 5].A);
    }

    [Fact]
    public void FilterRegion_ClipsTheInputOfThePrimitives()
    {
        // Filter Effects: the region is a hard clip on the input. The blur reads transparent pixels beyond the region
        // edge at x = 10, so the pixel there is no longer opaque, although the shape continues.
        using Image<Rgba32> image = Render(
            "<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"10\" height=\"20\" color-interpolation-filters=\"sRGB\">"
            + "<feGaussianBlur stdDeviation=\"2\"/></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/>");

        Assert.True(image[9, 10].A < 200);
        Assert.Equal(0, image[10, 10].A);
    }

    [Fact]
    public void LinearRgb_BlendsBrighterThanSrgb()
    {
        // Half red and half blue, blurred across the boundary: in linear light the mix is brighter.
        string Document(string space)
            => $"<filter id=\"f\" x=\"0\" y=\"0\" width=\"1\" height=\"1\" color-interpolation-filters=\"{space}\">"
            + "<feGaussianBlur stdDeviation=\"4 0\"/></filter>"
            + "<linearGradient id=\"g\"><stop offset=\"0.5\" stop-color=\"#ff0000\"/><stop offset=\"0.5\" stop-color=\"#0000ff\"/></linearGradient>"
            + "<rect width=\"20\" height=\"20\" fill=\"url(#g)\" filter=\"url(#f)\"/>";

        using Image<Rgba32> srgb = Render(Document("sRGB"));
        using Image<Rgba32> linear = Render(Document("linearRGB"));

        Assert.InRange(srgb[10, 10].R, 100, 155);
        Assert.True(linear[10, 10].R > srgb[10, 10].R + 30);
    }

    [Fact]
    public void Filter_FollowsTheScaleOfTheRender()
    {
        // The same document at twice the size: lengths of primitives scale with the user space.
        const string content =
            "<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\" color-interpolation-filters=\"sRGB\">"
            + "<feOffset dx=\"4\"/></filter>"
            + "<rect x=\"2\" y=\"2\" width=\"6\" height=\"6\" filter=\"url(#f)\"/>";

        using Image<Rgba32> small = Render(content, 20, 20);
        using Image<Rgba32> large = Render(content, 40, 40);

        Assert.Equal(255, small[6, 5].A);
        Assert.Equal(0, small[5, 5].A);
        Assert.Equal(255, large[12, 10].A);
        Assert.Equal(0, large[11, 10].A);
    }

    [Fact]
    public void Chain_RunsEachFilterOnTheResultOfThePreviousOne()
    {
        // A filter list applies its entries in order. Two offset filters of one element user space, chained in one node:
        // the square moves by both offsets, and the second filter region clips the result of the first.
        const string region = "filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"20\" height=\"20\"";
        SvgDocument first = SvgDocument.Parse(
            SvgOpen + $"<filter id=\"f\" {region}><feOffset dx=\"5\"/></filter><rect x=\"2\" y=\"2\" width=\"4\" height=\"4\" filter=\"url(#f)\"/></svg>");
        SvgDocument second = SvgDocument.Parse(
            SvgOpen + "<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"12\" height=\"20\"><feOffset dx=\"3\"/></filter>"
            + "<rect width=\"1\" height=\"1\" filter=\"url(#f)\"/></svg>");

        SvgRenderNode[] nodes = first.Nodes.ToArray();
        SvgFilterNode node = first.Filters[nodes[0].FilterIndex];
        SvgFilterNode chain = new(node.Content, [node.Graphs[0], second.Filters[0].Graphs[0]], node.MaxPixels);
        SvgDocument document = new(nodes, first.Transforms.ToArray(), first.Size, first.ViewBox, new RectangleF(0, 0, 20, 20), default, 0, [chain]);

        using Image<Rgba32> image = new(20, 20);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 20, 20))));

        // 2 + 5 + 3 = 10 to 14, cut by the second region at 12.
        Assert.Equal(0, image[9, 3].A);
        Assert.Equal(255, image[10, 3].A);
        Assert.Equal(255, image[11, 3].A);
        Assert.Equal(0, image[12, 3].A);
    }

    [Fact]
    public void MaxFilterPixels_LowersTheResolutionInsteadOfFailing()
    {
        SvgLoadOptions options = new();
        options.Limits.MaxFilterPixels = 64;
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "<filter id=\"f\" x=\"0\" y=\"0\" width=\"1\" height=\"1\"><feFlood flood-color=\"#00ff00\"/></filter>"
            + "<rect width=\"20\" height=\"20\" filter=\"url(#f)\"/></svg>",
            options);

        using Image<Rgba32> image = new(100, 100);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 100, 100))));

        AssertPixel(new Rgba32(0, 255, 0, 255), image[50, 50]);
    }

    [Fact]
    public void ColorConversion_RoundTripsThroughLinearLight()
    {
        // sRGB 128 is linear 0.2158, 55 in 8 bits; the conversion back gives 128 again.
        SvgFilterImage image = new(Configuration.Default.MemoryAllocator, new Rectangle(0, 0, 1, 1), SvgColorInterpolation.SRgb);
        image.Pixels[0] = new Rgba32(128, 255, 0, 255);
        SvgFilterColor.Convert(image, SvgColorInterpolation.LinearRgb);
        Assert.Equal(new Rgba32(55, 255, 0, 255), image.Pixels[0]);
        SvgFilterColor.Convert(image, SvgColorInterpolation.SRgb);
        Assert.Equal(new Rgba32(128, 255, 0, 255), image.Pixels[0]);
        image.Dispose();
    }

    private static Image<Rgba32> Render(string content, int width = 20, int height = 20)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + content + "</svg>");
        Image<Rgba32> image = new(width, height);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, width, height))));
        return image;
    }

    private static SvgFilterGraph GetGraph(string content)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + content + "</svg>");
        SvgRenderNode node = Assert.Single(document.Nodes.ToArray());
        Assert.Equal(SvgRenderNodeKind.Filter, node.Kind);
        return Assert.Single(document.Filters[node.FilterIndex].Graphs);
    }

    private static void AssertPixel(Rgba32 expected, Rgba32 actual)
    {
        Assert.InRange(actual.A, expected.A - 1, expected.A + 1);
        Assert.InRange(actual.R, expected.R - 1, expected.R + 1);
        Assert.InRange(actual.G, expected.G - 1, expected.G + 1);
        Assert.InRange(actual.B, expected.B - 1, expected.B + 1);
    }

    private static void AssertPremultiplied(float[] expected, Rgba32 actual)
    {
        // The canvas stores straight alpha; compare premultiplied values within 8-bit rounding of each step.
        float a = actual.A / 255F;
        Assert.InRange(a, expected[3] - 0.01F, expected[3] + 0.01F);
        Assert.InRange(actual.R / 255F * a, expected[0] - 0.012F, expected[0] + 0.012F);
        Assert.InRange(actual.G / 255F * a, expected[1] - 0.012F, expected[1] + 0.012F);
        Assert.InRange(actual.B / 255F * a, expected[2] - 0.012F, expected[2] + 0.012F);
    }

    private static int Index(SvgElementTree tree, string id)
    {
        Assert.True(tree.TryGetElementById(id, out int index));
        return index;
    }

    private static SvgElementTree Read(string content) => SvgReader.Read(SvgOpen + content + "</svg>", SvgReaderOptions.Default);
}
