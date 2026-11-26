// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests of SVG text features in combination: baselines, decorations, <c>textLength</c>, and text on a path in vertical
/// and right-to-left text, shaping features with a direction, and the bounding box of vertical text. The expected values
/// come from Chromium renders and <c>getBBox()</c> with the same fonts (Open Sans, Noto Sans KR, me_quran).
/// </summary>
public class SvgTextIntegrationTests
{
    private const string SvgOpen =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"200\" height=\"200\" "
        + "font-family=\"Noto Sans KR\" font-size=\"20\">";

    [Theory]
    [InlineData("tb", -1F)]
    [InlineData("vertical-lr", -1F)]
    [InlineData("sideways-rl", -1F)]
    [InlineData("sideways-lr", 1F)]
    public void VerticalBaselineShift_MovesTheGlyphsAcrossTheLine(string mode, float sign)
    {
        // baseline-shift raises the glyphs toward their top: right for glyphs turned clockwise, left in sideways-lr.
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" x=\"50\" y=\"100\" writing-mode=\"{mode}\">a<tspan baseline-shift=\"8\">b</tspan><tspan baseline-shift=\"-25%\">c</tspan></text>",
            "t");
        SvgTextCluster a = layout.Clusters[0];
        SvgTextCluster b = layout.Clusters[1];
        SvgTextCluster c = layout.Clusters[2];

        Assert.Equal(a.X - (sign * 8F), b.X, 3);
        Assert.Equal(a.X + (sign * 5F), c.X, 3);

        // The pen still advances along y only.
        Assert.Equal(a.Y + a.Width, b.Y, 3);
        Assert.Equal(b.Y + b.Width, c.Y, 3);
    }

    [Fact]
    public void VerticalDominantBaseline_MovesSidewaysAndUprightGlyphsOntoTheBaseline()
    {
        SvgTextLayout layout = Layout("<text id=\"t\" x=\"50\" y=\"10\" writing-mode=\"tb\" dominant-baseline=\"hanging\">a日</text>", "t");
        SvgTextCluster sideways = layout.Clusters[0];
        SvgTextCluster upright = layout.Clusters[1];
        FontMetrics metrics = sideways.Font!.FontMetrics;
        float scale = 20F / metrics.UnitsPerEm;
        float ascent = metrics.HorizontalMetrics.Ascender * scale;
        float descent = -metrics.HorizontalMetrics.Descender * scale;
        float hanging = 0.8F * ascent;
        float central = (ascent - descent) / 2F;

        // A sideways glyph puts its hanging baseline on x. An upright glyph, which shaping centres on x, moves by the
        // distance from the central to the hanging baseline. Both move left, as in Chromium.
        Assert.Equal(90F, sideways.Rotation);
        Assert.Equal(50F - hanging, sideways.X, 3);
        Assert.True(layout.IsUpright(in upright));
        Assert.Equal(50F - (hanging - central), upright.X, 3);
    }

    [Fact]
    public void VerticalAlignmentBaseline_AppliesToTheSpanThatSetsIt()
    {
        SvgTextLayout layout = Layout(
            "<text id=\"t\" x=\"50\" y=\"10\" writing-mode=\"tb\">a<tspan alignment-baseline=\"hanging\">b</tspan>c</text>",
            "t");

        Assert.Equal(layout.Clusters[0].X, layout.Clusters[2].X, 3);
        Assert.True(layout.Clusters[1].X < layout.Clusters[0].X);
    }

    [Fact]
    public void VerticalBoundingBox_IsTheUnionOfCellsAcrossTheLine()
    {
        // Chromium getBBox() with Noto Sans KR: x 8, y 10, width 44, height 90, where it rounds the ascent and the
        // descent (43.44 together) to whole pixels.
        SvgTextLayout layout = Layout("<text id=\"t\" x=\"30\" y=\"10\" writing-mode=\"tb\" font-size=\"30\">日本語</text>", "t");
        FontMetrics metrics = layout.Clusters[0].Font!.FontMetrics;
        float scale = 30F / metrics.UnitsPerEm;
        float height = (metrics.HorizontalMetrics.Ascender - metrics.HorizontalMetrics.Descender) * scale;
        RectangleF box = layout.Bounds!.Value;

        Assert.Equal(30F - (height / 2F), box.X, 3);
        Assert.Equal(10F, box.Y, 3);
        Assert.Equal(height, box.Width, 3);
        Assert.Equal(90F, box.Height, 3);
    }

    [Fact]
    public void VerticalBoundingBox_SidewaysCellsAndUprightCellsShareTheLine()
    {
        // Chromium getBBox(): the same 44 wide column for sideways Latin, upright CJK, and both mixed.
        SvgTextLayout sideways = Layout("<text id=\"t\" x=\"80\" y=\"10\" writing-mode=\"tb\" font-size=\"30\">abc</text>", "t");
        SvgTextLayout mixed = Layout("<text id=\"t\" x=\"80\" y=\"10\" writing-mode=\"tb\" font-size=\"30\">ab日</text>", "t");

        Assert.Equal(sideways.Bounds!.Value.Left, mixed.Bounds!.Value.Left, 3);
        Assert.Equal(sideways.Bounds!.Value.Right, mixed.Bounds!.Value.Right, 3);
    }

    [Fact]
    public void VerticalTextLength_SpacingAndGlyphsScalesUprightGlyphsAlongY()
    {
        SvgTextLayout layout = Layout(
            "<text id=\"t\" x=\"50\" y=\"10\" writing-mode=\"tb\" textLength=\"80\" lengthAdjust=\"spacingAndGlyphs\">日本</text>",
            "t");
        SvgTextCluster first = layout.Clusters[0];
        Matrix3x2 transform = layout.GetGlyphTransform(in first, first.GlyphStart);

        Assert.Equal(2F, first.ScaleX, 3);
        Assert.Equal(1F, transform.M11, 3);
        Assert.Equal(2F, transform.M22, 3);
        Assert.Equal(50F, layout.Clusters[1].Y, 3);
        Assert.Equal(80F, layout.Bounds!.Value.Height, 3);
    }

    [Theory]
    [InlineData("spacing")]
    [InlineData("spacingAndGlyphs")]
    public void RightToLeftRunInLeftToRightText_TextLengthKeepsTheLeftEdgeAtX(string adjust)
    {
        // Chromium keeps the start of the chunk at x: the Arabic word spans 20 to 170.
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" x=\"20\" y=\"100\" font-family=\"me_quran\" textLength=\"150\" lengthAdjust=\"{adjust}\">بسمل</text>",
            "t");
        float left = float.MaxValue;
        float right = float.MinValue;
        foreach (SvgTextCluster cluster in layout.Clusters)
        {
            left = MathF.Min(left, cluster.X);
            right = MathF.Max(right, cluster.X + (cluster.Width * cluster.ScaleX));
        }

        Assert.Equal(20F, left, 2);
        Assert.Equal(170F, right, 2);
    }

    [Theory]
    [InlineData("spacing")]
    [InlineData("spacingAndGlyphs")]
    public void SidewaysLrTextLength_RunsUpwardFromY(string adjust)
    {
        // Chromium: the text runs from y = 180 up to y = 20.
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" x=\"40\" y=\"180\" writing-mode=\"sideways-lr\" textLength=\"160\" lengthAdjust=\"{adjust}\">Side up</text>",
            "t");
        SvgTextCluster last = layout.Clusters[^1];

        Assert.Equal(180F, layout.Clusters[0].Y, 2);
        Assert.Equal(20F, last.Y - layout.GetInlineExtent(in last), 2);
    }

    [Fact]
    public void VerticalTextPath_SidewaysGlyphsKeepTheCentralBaselineAcrossThePath()
    {
        SvgTextLayout layout = Layout(
            "<defs><path id=\"p\" d=\"M30,10 L30,190\"/></defs><text id=\"t\" writing-mode=\"tb\"><textPath xlink:href=\"#p\">a日</textPath></text>",
            "t");
        SvgTextCluster sideways = layout.Clusters[0];
        SvgTextCluster upright = layout.Clusters[1];
        FontMetrics metrics = sideways.Font!.FontMetrics;
        float central = (metrics.HorizontalMetrics.Ascender + metrics.HorizontalMetrics.Descender) * 20F / metrics.UnitsPerEm / 2F;

        // Down a vertical path, the sideways glyph lies as in vertical text without a path: turned clockwise, with its
        // central baseline on the path. The upright glyph stands centred on the path below it, as in Chromium.
        Assert.Equal(90F, sideways.Rotation, 3);
        Assert.Equal(30F - central, sideways.X, 3);
        Assert.Equal(10F, sideways.Y, 3);
        Assert.Equal(0F, upright.Rotation, 3);
        Assert.Equal(30F, upright.X, 3);
        Assert.Equal(10F + sideways.Width, upright.Y, 3);
    }

    [Fact]
    public void Features_DoNotResetTheDirectionOrTheOrientation()
    {
        // font-kerning and font-variant configure the shaping run after the direction and the orientation.
        SvgTextLayout vertical = Layout(
            "<text id=\"t\" x=\"50\" y=\"10\" writing-mode=\"tb\" font-kerning=\"none\" font-variant=\"small-caps\">日本</text>",
            "t");
        Assert.True(vertical.IsUpright(in vertical.Clusters[0]));
        Assert.Equal(vertical.Clusters[0].X, vertical.Clusters[1].X);
        Assert.Equal(30F, vertical.Clusters[1].Y, 3);

        SvgTextLayout plain = Layout("<text id=\"t\" x=\"190\" y=\"50\" direction=\"rtl\" font-family=\"me_quran\">بسم</text>", "t");
        SvgTextLayout styled = Layout(
            "<text id=\"t\" x=\"190\" y=\"50\" direction=\"rtl\" font-family=\"me_quran\" font-kerning=\"none\" font-variant=\"small-caps\">بسم</text>",
            "t");
        Assert.Equal(plain.Glyphs.Length, styled.Glyphs.Length);
        for (int i = 0; i < plain.Glyphs.Length; i++)
        {
            Assert.Equal(plain.Glyphs[i].GlyphId, styled.Glyphs[i].GlyphId);
        }

        // The first character of a right-to-left chunk lies at its right end.
        Assert.True(styled.Clusters[0].X > styled.Clusters[^1].X);
    }

    [Theory]
    [InlineData("underline", -1)]
    [InlineData("overline", 1)]
    public void VerticalDecoration_RunsDownTheColumn(string line, int side)
    {
        SvgDocument document = Parse($"<text x=\"50\" y=\"10\" writing-mode=\"tb\" text-decoration=\"{line}\">日本ab</text>");
        SvgTextLayout layout = Layout("<text id=\"t\" x=\"50\" y=\"10\" writing-mode=\"tb\">日本ab</text>", "t");
        SvgRenderNode band = Assert.Single(document.Nodes.ToArray(), n => n.Path is not null && n.Path.Bounds.Height > n.Path.Bounds.Width * 4);
        RectangleF bounds = band.Path!.Bounds;

        // One band from the top of the first cluster to the end of the last, left of x for an underline and right of x
        // for an overline, as in Chromium.
        Assert.Equal(10F, bounds.Top, 2);
        Assert.Equal(layout.Clusters[^1].Y + layout.Clusters[^1].Width, bounds.Bottom, 2);
        Assert.Equal(side, Math.Sign(bounds.Left + (bounds.Width / 2F) - 50F));
    }

    [Fact]
    public void RightToLeftDecoration_FormsOneBand()
    {
        SvgDocument document = Parse("<text x=\"190\" y=\"50\" direction=\"rtl\" font-family=\"me_quran\" text-decoration=\"underline\">بسم الله</text>");

        // Clusters stay in logical order and run leftward; each touches the left end of the band.
        Assert.IsType<RectanglePolygon>(document.Nodes[0].Path);
    }

    [Fact]
    public void Decoration_GlyphsOfAFallbackFontShareTheLine()
    {
        // The CJK glyph comes from the fallback font; the line keeps the metrics of the primary font, as in Chromium.
        SvgDocument document = Parse("<text x=\"10\" y=\"50\" font-family=\"Open Sans, Noto Sans KR\" text-decoration=\"underline\">a日b</text>");

        Assert.IsType<RectanglePolygon>(document.Nodes[0].Path);
    }

    [Fact]
    public void PathDecoration_DrawsABandPerPlacedClusterOnly()
    {
        SvgDocument document = Parse(
            "<defs><path id=\"p\" d=\"M20,100 Q100,20 180,100\"/></defs>"
            + "<text font-family=\"Open Sans\" text-decoration=\"underline\"><textPath xlink:href=\"#p\">abcdefghijklmnopqrstuvwxyz</textPath></text>");
        SvgTextLayout layout = Layout(
            "<path id=\"p\" d=\"M20,100 Q100,20 180,100\"/>"
            + "<text id=\"t\" font-family=\"Open Sans\"><textPath xlink:href=\"#p\">abcdefghijklmnopqrstuvwxyz</textPath></text>",
            "t");
        int placed = 0;
        foreach (SvgTextCluster cluster in layout.Clusters)
        {
            placed += cluster.Skipped ? 0 : 1;
        }

        ComplexPolygon bands = Assert.IsType<ComplexPolygon>(document.Nodes[0].Path);
        Assert.InRange(placed, 1, layout.Clusters.Length - 1);
        Assert.Equal(placed, bands.Paths.Count());
    }

    private static SvgDocument Parse(string content)
        => SvgDocument.Parse(SvgOpen + content + "</svg>", SvgTextDirectionRenderTests.Options);

    private static SvgTextLayout Layout(string content, string id)
    {
        SvgLoadOptions options = SvgTextDirectionRenderTests.Options;
        SvgElementTree tree = SvgReader.Read(SvgOpen + content + "</svg>", SvgReaderOptions.Default);
        SvgResolver resolver = new(tree, options);
        Assert.True(tree.TryGetElementById(id, out int index));
        SvgComputedStyle style = resolver.ComputeDocumentStyle(index);
        return SvgTextLayout.Create(resolver, index, in style, new SizeF(200F, 200F), new SvgFontMatcher(options));
    }
}
