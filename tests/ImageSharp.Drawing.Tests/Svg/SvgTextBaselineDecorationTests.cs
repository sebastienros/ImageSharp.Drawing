// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests of the baselines (<c>dominant-baseline</c>, <c>alignment-baseline</c>, <c>baseline-shift</c>), the font
/// features (<c>font-kerning</c>, <c>kerning</c>), and <c>text-decoration</c> of SVG text. The expected baseline offsets
/// are the rules measured in Chromium with the fonts of the resvg suite: the heights derive from the ascent, descent,
/// and x-height of the primary font of the span.
/// </summary>
public class SvgTextBaselineDecorationTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\" font-family=\"Open Sans\" font-size=\"20\">";

    [Theory]
    [InlineData("auto", 0F, 0F, 0F)]
    [InlineData("alphabetic", 0F, 0F, 0F)]
    [InlineData("use-script", 0F, 0F, 0F)]
    [InlineData("hanging", 0.8F, 0F, 0F)]
    [InlineData("mathematical", 0.5F, 0F, 0F)]
    [InlineData("central", 0.5F, -0.5F, 0F)]
    [InlineData("middle", 0F, 0F, 0.5F)]
    [InlineData("text-before-edge", 1F, 0F, 0F)]
    [InlineData("text-after-edge", 0F, -1F, 0F)]
    [InlineData("ideographic", 0F, -1F, 0F)]
    [InlineData("text-top", 0F, 0F, 0F)]
    public void DominantBaseline_MovesTheGlyphsByTheHeightOfTheBaseline(string value, float ascent, float descent, float xHeight)
    {
        SvgTextLayout layout = Layout($"<text id=\"t\" x=\"10\" y=\"50\" dominant-baseline=\"{value}\">x</text>", "t");
        Metrics metrics = new(layout.Clusters[0].Font!);

        float expected = (ascent * metrics.Ascent) + (descent * metrics.Descent) + (xHeight * metrics.XHeight);
        Assert.Equal(50F + expected, layout.Clusters[0].Y, 3);
    }

    [Fact]
    public void AlignmentBaseline_AppliesOnlyToTheCharactersOfTheSpanThatSetsIt()
    {
        SvgTextLayout layout = Layout(
            "<text id=\"t\" y=\"50\">a<tspan alignment-baseline=\"hanging\">b<tspan>c</tspan></tspan></text>",
            "t");
        Metrics metrics = new(layout.Clusters[0].Font!);

        Assert.Equal(50F, layout.Clusters[0].Y, 3);
        Assert.Equal(50F + (0.8F * metrics.Ascent), layout.Clusters[1].Y, 3);

        // Chromium does not apply the alignment of an ancestor span.
        Assert.Equal(50F, layout.Clusters[2].Y, 3);
    }

    [Fact]
    public void AlignmentBaseline_OverridesTheDominantBaselineInTheFontOfTheSpan()
    {
        SvgTextLayout layout = Layout(
            "<text id=\"t\" y=\"50\" dominant-baseline=\"hanging\">a<tspan font-size=\"10\">b</tspan><tspan alignment-baseline=\"alphabetic\">c</tspan></text>",
            "t");

        Assert.Equal(50F + (0.8F * new Metrics(layout.Clusters[0].Font!).Ascent), layout.Clusters[0].Y, 3);
        Assert.Equal(50F + (0.8F * new Metrics(layout.Clusters[1].Font!).Ascent), layout.Clusters[1].Y, 3);
        Assert.Equal(50F, layout.Clusters[2].Y, 3);
    }

    [Fact]
    public void BaselineShift_AddsUpThroughNestedSpansButNotOnTheTextElement()
    {
        SvgTextLayout layout = Layout(
            "<text id=\"t\" y=\"50\" baseline-shift=\"10\">a<tspan baseline-shift=\"20%\">b<tspan baseline-shift=\"baseline\">c"
            + "<tspan baseline-shift=\"5\" font-size=\"40\">d</tspan></tspan></tspan></text>",
            "t");

        // A percentage refers to the font size of the span; positive values move up.
        Assert.Equal(50F, layout.Clusters[0].Y, 3);
        Assert.Equal(46F, layout.Clusters[1].Y, 3);
        Assert.Equal(46F, layout.Clusters[2].Y, 3);
        Assert.Equal(41F, layout.Clusters[3].Y, 3);
    }

    [Fact]
    public void BaselineShift_SubAndSuperMoveByHalfTheFontHeightOfTheSpan()
    {
        SvgTextLayout layout = Layout(
            "<text id=\"t\" y=\"50\"><tspan baseline-shift=\"super\" font-size=\"10\">a</tspan><tspan baseline-shift=\"sub\">b</tspan></text>",
            "t");
        Metrics small = new(layout.Clusters[0].Font!);
        Metrics normal = new(layout.Clusters[1].Font!);

        Assert.Equal(50F - ((small.Ascent + small.Descent) / 2F), layout.Clusters[0].Y, 3);
        Assert.Equal(50F + ((normal.Ascent + normal.Descent) / 2F), layout.Clusters[1].Y, 3);
    }

    [Fact]
    public void Kerning_FontKerningNoneAndTheKerningPropertyTurnKerningOff()
    {
        // Inter has kerning for A and V; the font-kerning presentation attribute does not exist, so the style sets it.
        static float Advance(string attributes)
        {
            SvgTextLayout layout = Layout($"<text id=\"t\" font-family=\"Inter\" {attributes}>AV</text>", "t");
            return layout.Clusters[1].X - layout.Clusters[0].X;
        }

        float kerned = Advance(string.Empty);
        Assert.True(Advance("style=\"font-kerning: none\"") > kerned);
        Assert.Equal(Advance("style=\"font-kerning: none\""), Advance("kerning=\"0\""), 3);
        Assert.Equal(kerned, Advance("style=\"font-kerning: normal\""), 3);
    }

    [Fact]
    public void TextDecoration_UnderlinePaintsBelowTheGlyphsAndLineThroughAbove()
    {
        SvgDocument document = Parse("<text x=\"10\" y=\"50\" font-family=\"Inter\" text-decoration=\"underline line-through\" fill=\"blue\">Ab</text>");
        ReadOnlySpan<SvgRenderNode> nodes = document.Nodes;
        SvgTextLayout layout = Layout("<text id=\"t\" x=\"10\" y=\"50\" font-family=\"Inter\">Ab</text>", "t");
        FontMetrics metrics = layout.Clusters[0].Font!.FontMetrics;
        float scale = 20F / metrics.UnitsPerEm;
        float width = layout.Clusters[0].Width + layout.Clusters[1].Width;

        // The underline is one band along both clusters, centered on the underline position of the font.
        RectangleF underline = nodes[0].Path!.Bounds;
        Assert.Equal(10F, underline.Left, 3);
        Assert.Equal(width, underline.Width, 3);
        Assert.Equal(50F - (metrics.UnderlinePosition * scale), underline.Top + (underline.Height / 2F), 3);
        Assert.Equal(metrics.UnderlineThickness * scale, underline.Height, 3);
        Assert.Equal(Color.Blue, ((SolidBrush)nodes[0].Fill!).Color);

        RectangleF lineThrough = nodes[^1].Path!.Bounds;
        Assert.Equal(50F - (metrics.StrikeoutPosition * scale), lineThrough.Top + (lineThrough.Height / 2F), 3);
        Assert.Equal(metrics.StrikeoutSize * scale, lineThrough.Height, 3);
        Assert.Equal(4, nodes.Length);
    }

    [Fact]
    public void TextDecoration_PaintComesFromTheSpanThatSetsTheLine()
    {
        SvgDocument document = Parse(
            "<text x=\"10\" y=\"50\" fill=\"black\"><tspan text-decoration=\"underline\" fill=\"red\">a<tspan fill=\"blue\">b</tspan></tspan></text>");

        // Each paint group draws its band below its glyph: band, a, band, b.
        ReadOnlySpan<SvgRenderNode> nodes = document.Nodes;
        Assert.Equal(4, nodes.Length);
        Assert.Equal(Color.Red, ((SolidBrush)nodes[0].Fill!).Color);
        Assert.Equal(Color.Red, ((SolidBrush)nodes[2].Fill!).Color);
        Assert.Equal(Color.Blue, ((SolidBrush)nodes[3].Fill!).Color);
    }

    [Fact]
    public void TextDecoration_SetOutsideTheTextTakesThePaintOfTheText()
    {
        SvgDocument document = Parse("<g text-decoration=\"underline\" fill=\"red\"><text x=\"10\" y=\"50\" fill=\"teal\">a</text></g>");

        Assert.Equal(2, document.Nodes.Length);
        Assert.Equal(Color.Teal, ((SolidBrush)document.Nodes[0].Fill!).Color);
    }

    [Fact]
    public void TextDecoration_RotatedAndHiddenClustersBreakTheBand()
    {
        // b and c turn, so each gets a band of its own; a keeps a straight band.
        SvgDocument rotated = Parse("<text x=\"10\" y=\"50\" rotate=\"0 10\" text-decoration=\"underline\">abc</text>");
        ComplexPolygon bands = Assert.IsType<ComplexPolygon>(rotated.Nodes[0].Path);
        Assert.Equal(3, bands.Paths.Count());

        // A hidden character draws no band.
        SvgDocument hidden = Parse("<text x=\"10\" y=\"50\" text-decoration=\"underline\">a<tspan visibility=\"hidden\">b</tspan>c</text>");
        SvgTextLayout layout = Layout("<text id=\"t\" x=\"10\" y=\"50\">abc</text>", "t");
        Assert.Equal(4, hidden.Nodes.Length);
        Assert.Equal(layout.Clusters[2].X, hidden.Nodes[2].Path!.Bounds.Left, 3);
    }

    private static SvgDocument Parse(string content)
        => SvgDocument.Parse(SvgOpen + content + "</svg>", SvgTextDecorationBaselineRenderTests.Options);

    private static SvgTextLayout Layout(string content, string id)
    {
        SvgLoadOptions options = SvgTextDecorationBaselineRenderTests.Options;
        SvgElementTree tree = SvgReader.Read(SvgOpen + content + "</svg>", SvgReaderOptions.Default);
        SvgResolver resolver = new(tree, options);
        Assert.True(tree.TryGetElementById(id, out int index));
        SvgComputedStyle style = resolver.ComputeDocumentStyle(index);
        return SvgTextLayout.Create(resolver, index, in style, new SizeF(200F, 200F), new SvgFontMatcher(options));
    }

    /// <summary>
    /// The vertical metrics of a font in user units.
    /// </summary>
    private readonly struct Metrics
    {
        public Metrics(Font font)
        {
            FontMetrics metrics = font.FontMetrics;
            float scale = font.Size / metrics.UnitsPerEm;
            this.Ascent = metrics.HorizontalMetrics.Ascender * scale;
            this.Descent = -metrics.HorizontalMetrics.Descender * scale;
            this.XHeight = metrics.XHeight * scale;
        }

        public float Ascent { get; }

        public float Descent { get; }

        public float XHeight { get; }
    }
}
