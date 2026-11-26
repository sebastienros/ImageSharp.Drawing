// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Numerics;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests of SVG text on a path (<c>textPath</c>) and of <c>textLength</c> with <c>lengthAdjust</c>. The expected
/// positions follow SVG 2 and, where SVG leaves a choice, the rendered output of Chromium, which the project follows: a
/// cluster sits on the path at the middle of its advance and turns with the path.
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgTextPathTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\" font-family=\"Open Sans\" font-size=\"20\">";

    // An upward vertical line of length 100 from (20, 120), and a horizontal line of length 150 from (10, 50).
    private const string UpPath = "<path id=\"up\" d=\"M 20 120 L 20 20\"/>";
    private const string FlatPath = "<path id=\"flat\" d=\"M 10 50 L 160 50\"/>";

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextPathTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgTextPathTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TextPath_PlacesEachClusterByTheMiddleOfItsAdvanceAndTurnsItWithThePath()
    {
        SvgTextLayout layout = Layout(UpPath + "<text id=\"t\"><textPath href=\"#up\">ab</textPath></text>", "t");
        SvgTextCluster a = layout.Clusters[0];
        SvgTextCluster b = layout.Clusters[1];

        // The path runs upward, so the pen of the first cluster is at the start point and the glyphs turn by -90
        // degrees; the second cluster starts one advance further along the path.
        Assert.Equal(20F, a.X, 3);
        Assert.Equal(120F, a.Y, 3);
        Assert.Equal(-90F, a.Rotation, 3);
        Assert.Equal(20F, b.X, 3);
        Assert.Equal(120F - a.Width, b.Y, 3);
        Assert.False(a.Skipped);
    }

    [Theory]
    [InlineData("50%", 50F)]
    [InlineData("30", 30F)]
    [InlineData("1em", 20F)]
    public void TextPath_StartOffsetIsALengthOrAPercentageOfThePathLength(string offset, float distance)
    {
        SvgTextLayout layout = Layout(UpPath + $"<text id=\"t\"><textPath href=\"#up\" startOffset=\"{offset}\">a</textPath></text>", "t");

        Assert.Equal(120F - distance, layout.Clusters[0].Y, 3);
    }

    [Fact]
    public void TextPath_ClustersWhoseMiddleIsOffThePathAreLeftOut()
    {
        // The path is 150 long. Text far longer than the path, and text moved before its start, is left out where the
        // middle of a cluster leaves the path; a closed path does not wrap, as in Chromium.
        SvgTextLayout layout = Layout(
            FlatPath + "<path id=\"loop\" d=\"M 10 100 L 60 100 L 60 150 Z\"/>"
            + $"<text id=\"t\"><textPath href=\"#flat\" startOffset=\"-15\">{new string('i', 67)}</textPath></text>",
            "t");

        float x = -15F;
        foreach (SvgTextCluster cluster in layout.Clusters)
        {
            float middle = x + (cluster.Width / 2F);
            Assert.Equal(middle < 0F || middle > 150F, cluster.Skipped);
            x += cluster.Width;
        }

        Assert.True(x > 165F);
        Assert.False(Layout(FlatPath + "<text id=\"t\"><textPath href=\"#flat\" startOffset=\"9999\">a</textPath></text>", "t").Bounds.HasValue);
    }

    [Fact]
    public void TextPath_ClosedPathDoesNotWrap()
    {
        // The closed triangle is 50 + 50 + 70.7 long.
        SvgTextLayout layout = Layout(
            "<path id=\"loop\" d=\"M 10 100 L 60 100 L 60 150 Z\"/><text id=\"t\"><textPath href=\"#loop\" startOffset=\"160\">abc</textPath></text>",
            "t");

        float length = 100F + (50F * MathF.Sqrt(2F));
        Assert.Equal(160F + (layout.Clusters[0].Width / 2F) > length, layout.Clusters[0].Skipped);
        Assert.True(layout.Clusters[2].Skipped);
    }

    [Fact]
    public void TextPath_TextAfterThePathContinuesAtItsEndPoint()
    {
        SvgTextLayout layout = Layout(
            FlatPath + "<text id=\"t\" x=\"5\" y=\"5\"><textPath href=\"#flat\">a</textPath>bc<tspan x=\"30\" y=\"180\">d</tspan></text>",
            "t");

        // The text after the path keeps its own advances from the end point; an absolute position ends that.
        Assert.Equal(160F, layout.Clusters[1].X, 3);
        Assert.Equal(50F, layout.Clusters[1].Y, 3);
        Assert.Equal(160F + layout.Clusters[1].Width, layout.Clusters[2].X, 3);
        Assert.Equal(30F, layout.Clusters[3].X, 3);
        Assert.Equal(180F, layout.Clusters[3].Y, 3);
    }

    [Theory]
    [InlineData("href=\"#missing\"")]
    [InlineData("href=\"#box\"")]
    [InlineData("href=\"#t\"")]
    [InlineData("")]
    public void TextPath_WithoutAPathElementLeavesItsTextOut(string reference)
    {
        // Chromium accepts only a path element: a basic shape, a missing element, and no reference give no path.
        SvgTextLayout layout = Layout(
            "<rect id=\"box\" x=\"10\" y=\"10\" width=\"100\" height=\"100\"/>"
            + $"<text id=\"t\" x=\"10\" y=\"150\">a<textPath {reference}>bc</textPath></text>",
            "t");

        Assert.False(layout.Clusters[0].Skipped);
        Assert.True(layout.Clusters[1].Skipped);
        Assert.True(layout.Clusters[2].Skipped);
    }

    [Fact]
    public void TextPath_PathAttributeWinsOverTheReferenceUnlessItIsEmpty()
    {
        SvgTextLayout withPath = Layout(FlatPath + "<text id=\"t\"><textPath href=\"#flat\" path=\"M 30 80 L 130 80\">a</textPath></text>", "t");
        SvgTextLayout invalidPath = Layout(FlatPath + "<text id=\"t\"><textPath href=\"#flat\" path=\"q\">a</textPath></text>", "t");

        Assert.Equal(new Vector2(30F, 80F), new Vector2(withPath.Clusters[0].X, withPath.Clusters[0].Y));
        Assert.Equal(new Vector2(10F, 50F), new Vector2(invalidPath.Clusters[0].X, invalidPath.Clusters[0].Y));
    }

    [Fact]
    public void TextPath_TransformOfTheReferencedPathAppliesButNotThatOfItsAncestors()
    {
        SvgTextLayout layout = Layout(
            "<g transform=\"translate(0 100)\"><path id=\"p\" transform=\"translate(5 7)\" d=\"M 10 50 L 160 50\"/></g>"
            + "<text id=\"t\"><textPath href=\"#p\">a</textPath></text>",
            "t");

        Assert.Equal(15F, layout.Clusters[0].X, 3);
        Assert.Equal(57F, layout.Clusters[0].Y, 3);
    }

    [Fact]
    public void TextPath_SideRightReversesThePath()
    {
        SvgTextLayout layout = Layout(FlatPath + "<text id=\"t\"><textPath href=\"#flat\" side=\"right\">a</textPath></text>", "t");

        // The text starts at the end point and runs back, upside down.
        Assert.Equal(160F, layout.Clusters[0].X, 3);
        Assert.Equal(50F, layout.Clusters[0].Y, 3);
        Assert.Equal(180F, layout.Clusters[0].Rotation, 3);
    }

    [Fact]
    public void TextPath_MethodAndSpacingHaveNoEffect()
    {
        SvgTextLayout plain = Layout(FlatPath + "<text id=\"t\"><textPath href=\"#flat\">ab</textPath></text>", "t");
        SvgTextLayout other = Layout(FlatPath + "<text id=\"t\"><textPath href=\"#flat\" method=\"stretch\" spacing=\"auto\">ab</textPath></text>", "t");

        Assert.Equal(plain.Clusters[1].X, other.Clusters[1].X);
        Assert.Equal(plain.Clusters[1].Y, other.Clusters[1].Y);
    }

    [Fact]
    public void TextPath_RotateTurnsTheClusterAboutItsPointOnThePath()
    {
        SvgTextLayout layout = Layout(FlatPath + "<text id=\"t\" rotate=\"90\"><textPath href=\"#flat\">a</textPath></text>", "t");
        SvgTextCluster a = layout.Clusters[0];

        // The middle of the advance stays on the path at (10 + w / 2, 50); the pen lies half an advance back from it in
        // the turned direction.
        float half = a.Width / 2F;
        Assert.Equal(90F, a.Rotation, 3);
        Assert.Equal(10F + half, a.X, 3);
        Assert.Equal(50F - half, a.Y, 3);
    }

    [Fact]
    public void TextPath_ShiftsMoveAlongAndAcrossThePathAndYIsIgnored()
    {
        SvgTextLayout layout = Layout(
            UpPath + "<text id=\"t\" x=\"3\" y=\"90\" dx=\"7\" dy=\"4\"><textPath href=\"#up\">ab</textPath></text>",
            "t");
        SvgTextCluster a = layout.Clusters[0];

        // x and dx move along the path (upward); dy moves across it, which is toward +x for a path turned by -90 degrees.
        // The y position of the text element does not apply inside the path.
        Assert.Equal(20F + 4F, a.X, 3);
        Assert.Equal(120F - 10F, a.Y, 3);
    }

    [Fact]
    public void TextPath_RotateTurnsTheShiftAcrossThePathWithTheGlyph()
    {
        SvgTextLayout layout = Layout(
            FlatPath + "<text id=\"t\" rotate=\"90\" dy=\"-10\"><textPath href=\"#flat\" startOffset=\"50\">a</textPath></text>",
            "t");
        SvgTextCluster a = layout.Clusters[0];

        // Chromium: the middle of the advance stays at (60 + w / 2, 50), and the offset of the pen from it, half an
        // advance back and 10 up, turns by 90 degrees with the glyph.
        float half = a.Width / 2F;
        Assert.Equal(90F, a.Rotation, 3);
        Assert.Equal(60F + half + 10F, a.X, 3);
        Assert.Equal(50F - half, a.Y, 3);
    }

    [Fact]
    public void TextPath_TextAfterThePathCancelsTheShiftOfItsFirstCluster()
    {
        SvgTextLayout layout = Layout(
            FlatPath + "<text id=\"t\"><textPath href=\"#flat\">a</textPath><tspan dy=\"10\">b</tspan>c</text>",
            "t");

        // Chromium: the first cluster after the path lies on the end point despite its dy, as SVG 2 defines path_end.
        Assert.Equal(160F, layout.Clusters[1].X, 3);
        Assert.Equal(50F, layout.Clusters[1].Y, 3);
        Assert.Equal(160F + layout.Clusters[1].Width, layout.Clusters[2].X, 3);
        Assert.Equal(50F, layout.Clusters[2].Y, 3);
    }

    [Theory]
    [InlineData("start", 1F)]
    [InlineData("middle", 0.5F)]
    [InlineData("end", 0F)]
    public void TextPath_TextAfterATextPathWithoutAPathMovesByItsOwnAdvances(string anchor, float share)
    {
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" x=\"100\" y=\"100\" text-anchor=\"{anchor}\">"
            + "Some<textPath>long</textPath><tspan dy=\"40\">text</tspan></text>",
            "t");
        Span<SvgTextCluster> clusters = layout.Clusters;
        float path = clusters[4].Width + clusters[5].Width + clusters[6].Width + clusters[7].Width;
        float after = clusters[8].Width + clusters[9].Width + clusters[10].Width + clusters[11].Width;

        // Chromium: the hidden path text keeps its advances on the line that restarts at (0, 0) with the path. The text
        // after it, anchored as a chunk of its own, then moves right by its own advances; its dy applies.
        Assert.True(clusters[4].Skipped);
        Assert.Equal(path + (share * after), clusters[8].X, 3);
        Assert.Equal(40F, clusters[8].Y, 3);
    }

    [Fact]
    public void TextPath_AnchorMeasuresThePathTextWithoutTheTextAfterIt()
    {
        SvgTextLayout layout = Layout(
            FlatPath + "<text id=\"t\" text-anchor=\"middle\"><textPath href=\"#flat\" startOffset=\"50%\">ab</textPath>cd</text>",
            "t");
        SvgTextCluster a = layout.Clusters[0];
        SvgTextCluster b = layout.Clusters[1];

        // Chromium: "ab" centres on the start offset, 75 along the path; "cd" starts at the end point.
        Assert.Equal(10F + 75F - ((a.Width + b.Width) / 2F), a.X, 3);
        Assert.Equal(160F, layout.Clusters[2].X, 3);
    }

    [Fact]
    public void TextPath_TspanPositionInsideThePathStartsAChunk()
    {
        SvgTextLayout layout = Layout(
            FlatPath + "<text id=\"t\" text-anchor=\"middle\"><textPath href=\"#flat\" startOffset=\"50%\">"
            + "ab<tspan x=\"20\" y=\"70\">c</tspan></textPath></text>",
            "t");
        SvgTextCluster a = layout.Clusters[0];
        SvgTextCluster b = layout.Clusters[1];
        SvgTextCluster c = layout.Clusters[2];

        // The first chunk "ab" centres on the start offset (75 along the path); "c" starts a chunk at x = 20 along the
        // path from the offset, centred there; its y is ignored.
        float ab = a.Width + b.Width;
        Assert.Equal(10F + 75F - (ab / 2F), a.X, 3);
        Assert.Equal(10F + 75F + 20F - (c.Width / 2F), c.X, 3);
        Assert.Equal(50F, c.Y, 3);
    }

    [Fact]
    public void TextPath_PlacementDoesNotDependOnTheSizeOfTheUserUnits()
    {
        // The same curve and text in units 100 times smaller: the path flattens relative to the font size, so the
        // placement scales with the units.
        SvgTextLayout large = Layout(
            "<path id=\"p\" d=\"M 20 73 C 35 108 85 108 100 73 C 115 38 165 38 180 73\"/><text id=\"t\" font-size=\"24\" dy=\"15\"><textPath href=\"#p\">Some long text</textPath></text>",
            "t");
        SvgTextLayout small = Layout(
            "<path id=\"p\" d=\"M 0.20 0.73 C 0.35 1.08 0.85 1.08 1.00 0.73 C 1.15 0.38 1.65 0.38 1.80 0.73\"/><text id=\"t\" font-size=\"0.24\" dy=\"0.15\"><textPath href=\"#p\">Some long text</textPath></text>",
            "t");

        for (int i = 0; i < large.Clusters.Length; i++)
        {
            Assert.Equal(large.Clusters[i].Skipped, small.Clusters[i].Skipped);
            Assert.Equal(large.Clusters[i].X, small.Clusters[i].X * 100F, 1);
            Assert.Equal(large.Clusters[i].Y, small.Clusters[i].Y * 100F, 1);
            Assert.Equal(large.Clusters[i].Rotation, small.Clusters[i].Rotation, 1);
        }
    }

    [Fact]
    public void TextPath_BoundingBoxAndClipFollowThePlacedGlyphs()
    {
        SvgTextLayout layout = Layout(UpPath + "<text id=\"t\"><textPath href=\"#up\">abc</textPath></text>", "t");
        RectangleF box = layout.Bounds!.Value;
        float advance = layout.Clusters[0].Width + layout.Clusters[1].Width + layout.Clusters[2].Width;

        // The cells turn with the path: the text runs from y = 120 upward, and the ascent lies on the left of the path.
        Assert.Equal(120F, box.Bottom, 3);
        Assert.Equal(advance, box.Height, 3);
        Assert.True(box.Left < 20F && box.Right > 20F);

        SvgDocument document = SvgDocument.Parse(
            SvgOpen + UpPath + "<clipPath id=\"c\"><text><textPath href=\"#up\">abc</textPath></text></clipPath>"
            + "<rect width=\"200\" height=\"200\" clip-path=\"url(#c)\"/></svg>",
            SvgTextRenderTests.Options);
        using Image<Rgba32> image = new(200, 200);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 200, 200))));

        // The clipped rectangle shows only along the path.
        Assert.Equal(0, image[150, 60].A);
        Assert.True(HasInk(image, new Rectangle(5, (int)box.Top, 20, (int)box.Height)));
    }

    [Fact]
    public void TextLength_SpacingSpreadsTheDifferenceOverTheGaps()
    {
        SvgTextLayout plain = Layout("<text id=\"t\" x=\"10\" y=\"50\">abc</text>", "t");
        float natural = plain.Clusters[0].Width + plain.Clusters[1].Width + plain.Clusters[2].Width;
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" x=\"10\" y=\"50\" textLength=\"{(natural + 30F).ToString(CultureInfo.InvariantCulture)}\">abc</text>",
            "t");

        // Three clusters have two gaps of 15; the first cluster keeps its position and the last moves by the difference.
        Assert.Equal(10F, layout.Clusters[0].X, 3);
        Assert.Equal(plain.Clusters[1].X + 15F, layout.Clusters[1].X, 2);
        Assert.Equal(plain.Clusters[2].X + 30F, layout.Clusters[2].X, 2);
        Assert.Equal(natural + 30F, layout.Bounds!.Value.Width, 2);
    }

    [Fact]
    public void TextLength_OnATspanMovesTheTextAfterIt()
    {
        SvgTextLayout plain = Layout("<text id=\"t\" x=\"10\" y=\"50\">a<tspan>bc</tspan>d</text>", "t");
        string length = (plain.Clusters[1].Width + plain.Clusters[2].Width + 10F).ToString(CultureInfo.InvariantCulture);
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" x=\"10\" y=\"50\">a<tspan textLength=\"{length}\">bc</tspan>d<tspan x=\"10\" y=\"90\">e</tspan></text>",
            "t");

        // One gap takes the difference of 10. As in Chromium, the text after the element up to the next text chunk moves
        // by the shift after its last cluster, which counts that cluster too: 20.
        Assert.Equal(plain.Clusters[1].X, layout.Clusters[1].X, 3);
        Assert.Equal(plain.Clusters[2].X + 10F, layout.Clusters[2].X, 2);
        Assert.Equal(plain.Clusters[3].X + 20F, layout.Clusters[3].X, 2);
        Assert.Equal(10F, layout.Clusters[4].X, 3);
    }

    [Fact]
    public void TextLength_TextAfterTheElementMovesUpToTheNextChunkOnEitherAxis()
    {
        SvgTextLayout plain = Layout("<text id=\"t\" x=\"10\" y=\"50\">a<tspan>bc</tspan>d<tspan y=\"90\">e</tspan></text>", "t");
        string length = (plain.Clusters[1].Width + plain.Clusters[2].Width + 10F).ToString(CultureInfo.InvariantCulture);
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" x=\"10\" y=\"50\">a<tspan textLength=\"{length}\">bc</tspan>d<tspan y=\"90\">e</tspan></text>",
            "t");

        // Chromium: a chunk that only y starts also ends the shift of the text after the element.
        Assert.Equal(plain.Clusters[3].X + 20F, layout.Clusters[3].X, 2);
        Assert.Equal(plain.Clusters[4].X, layout.Clusters[4].X, 3);
    }

    [Fact]
    public void TextLength_LaterChunksStartWhereTheAdvancesBeforeThemEnd()
    {
        SvgTextLayout plain = Layout("<text id=\"t\"><tspan x=\"10\" y=\"50\">ab</tspan>c<tspan x=\"10\" y=\"90\">de</tspan></text>", "t");
        float natural = 0F;
        foreach (SvgTextCluster cluster in plain.Clusters)
        {
            natural += cluster.Width;
        }

        string inner = (plain.Clusters[0].Width + plain.Clusters[1].Width + 4F).ToString(CultureInfo.InvariantCulture);
        string outer = (natural + 90F).ToString(CultureInfo.InvariantCulture);
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" textLength=\"{outer}\"><tspan x=\"10\" y=\"50\" textLength=\"{inner}\">ab</tspan>c<tspan x=\"10\" y=\"90\">de</tspan></text>",
            "t");

        // The inner element spreads 4 and moves "c" by 8. On the virtual line of the text element the second chunk
        // starts where the advances of "abc" end, without those shifts, as in Chromium (the resvg test
        // text/textLength/on-text-and-tspan), so the outer difference is 90 over three gaps of 30. The second chunk
        // keeps its start at x = 10.
        Assert.Equal(10F, layout.Clusters[3].X, 3);
        Assert.Equal(plain.Clusters[4].X + 30F, layout.Clusters[4].X, 2);
        Assert.Equal(plain.Clusters[1].X + 4F + 30F, layout.Clusters[1].X, 2);
        Assert.Equal(plain.Clusters[2].X + 8F + 30F, layout.Clusters[2].X, 2);
    }

    [Fact]
    public void TextLength_SpacingAndGlyphsScalesPositionsGlyphsAndAdvances()
    {
        SvgTextLayout plain = Layout("<text id=\"t\" x=\"10\" y=\"50\">ab</text>", "t");
        float natural = plain.Clusters[0].Width + plain.Clusters[1].Width;
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" x=\"10\" y=\"50\" textLength=\"{(natural * 2F).ToString(CultureInfo.InvariantCulture)}\" lengthAdjust=\"spacingAndGlyphs\" text-anchor=\"end\">ab</text>",
            "t");

        // Everything doubles about the start; the end anchor then measures the scaled advances.
        Assert.Equal(2F, layout.Clusters[0].ScaleX, 3);
        Assert.Equal(2F, layout.Clusters[1].ScaleX, 3);
        Assert.Equal(10F - (natural * 2F), layout.Clusters[0].X, 2);
        Assert.Equal(10F - (natural * 2F) + (plain.Clusters[0].Width * 2F), layout.Clusters[1].X, 2);
        Assert.Equal(natural * 2F, layout.Bounds!.Value.Width, 2);
    }

    [Fact]
    public void TextLength_DescendantsResolveFirstAndCountAsOneUnit()
    {
        SvgTextLayout plain = Layout("<text id=\"t\" x=\"10\" y=\"50\">a<tspan>bc</tspan></text>", "t");
        float a = plain.Clusters[0].Width;
        float bc = plain.Clusters[1].Width + plain.Clusters[2].Width;
        string inner = (bc + 10F).ToString(CultureInfo.InvariantCulture);
        string outer = (a + bc + 30F).ToString(CultureInfo.InvariantCulture);
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" x=\"10\" y=\"50\" textLength=\"{outer}\">a<tspan textLength=\"{inner}\">bc</tspan></text>",
            "t");

        // The tspan spreads 10 over its one gap first. The text then has one cluster of its own and one resolved tspan,
        // so one gap of 20. Chromium adds a share after the first cluster of the resolved tspan, as SVG 2 writes the
        // step, so the second cluster of the tspan moves by two shares on top of its own 10.
        Assert.Equal(10F, layout.Clusters[0].X, 3);
        Assert.Equal(plain.Clusters[1].X + 20F, layout.Clusters[1].X, 2);
        Assert.Equal(plain.Clusters[2].X + 10F + 40F, layout.Clusters[2].X, 2);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-50")]
    [InlineData("abc")]
    public void TextLength_ZeroNegativeAndInvalidValuesHaveNoEffect(string value)
    {
        SvgTextLayout plain = Layout("<text id=\"t\" x=\"10\" y=\"50\">abc</text>", "t");
        SvgTextLayout layout = Layout($"<text id=\"t\" x=\"10\" y=\"50\" textLength=\"{value}\">abc</text>", "t");

        Assert.Equal(plain.Clusters[2].X, layout.Clusters[2].X);
    }

    [Fact]
    public void TextLength_PercentageRefersToTheViewportWidth()
    {
        SvgTextLayout layout = Layout("<text id=\"t\" x=\"10\" y=\"50\" textLength=\"50%\" lengthAdjust=\"spacingAndGlyphs\">ab</text>", "t");

        Assert.Equal(100F, layout.Bounds!.Value.Width, 2);
    }

    [Fact]
    public void TextLength_OnATextPathFitsTheTextAlongThePath()
    {
        SvgTextLayout layout = Layout(
            FlatPath + "<text id=\"t\"><textPath href=\"#flat\" textLength=\"150\" lengthAdjust=\"spacingAndGlyphs\">ab</textPath></text>",
            "t");
        SvgTextCluster b = layout.Clusters[1];

        // The scaled text fills the 150 long path: the last cluster ends at its end point.
        Assert.Equal(160F, b.X + (b.Width * b.ScaleX), 2);
        Assert.Equal(50F, b.Y, 3);
    }

    [Fact]
    public void Robustness_LongPathsLongTextAndHugeValuesFinishInBoundedTime()
    {
        // A path of n segments with n / 5 characters on it costs a binary search per cluster, and many text elements on
        // the same path measure it once per load: ten times the input costs about ten times the time (SvgGrowth).
        static string PathData(int segments)
        {
            StringBuilder data = new("M 0 100");
            for (int i = 1; i <= segments; i++)
            {
                data.Append(CultureInfo.InvariantCulture, $" L {i * 0.01F} {100 + ((i % 2) * 0.5F)}");
            }

            return data.ToString();
        }

        static void LongText(int segments)
        {
            string text = new('a', segments / 5);
            SvgTextLayout layout = Layout($"<path id=\"p\" d=\"{PathData(segments)}\"/><text id=\"t\"><textPath href=\"#p\">{text}</textPath></text>", "t");
            Assert.Equal(segments / 5, layout.Clusters.Length);
            Assert.False(layout.Clusters[0].Skipped);
            Assert.True(layout.Clusters[^1].Skipped);
        }

        static void ManyTexts(int segments)
        {
            StringBuilder many = new(SvgOpen);
            many.Append(CultureInfo.InvariantCulture, $"<defs><path id=\"p\" d=\"{PathData(segments)}\"/></defs>");
            for (int i = 0; i < segments / 50; i++)
            {
                many.Append("<text><textPath href=\"#p\">ab</textPath></text>");
            }

            Assert.False(SvgDocument.Parse(many.Append("</svg>").ToString(), SvgTextRenderTests.Options).Nodes.IsEmpty);
        }

        TimeSpan small = SvgGrowth.MeasureWarm(this.output, "10,000 segments, 2,000 characters, 200 texts", () =>
        {
            LongText(10_000);
            ManyTexts(10_000);
        });
        TimeSpan large = SvgGrowth.Measure(this.output, "100,000 segments, 20,000 characters, 2,000 texts", () =>
        {
            LongText(100_000);
            ManyTexts(100_000);
        });
        SvgGrowth.AssertLinear(small, large, 10);

        // Huge offsets and lengths, a zero length path, and a hostile curve size load and leave out what does not fit.
        string[] documents =
        [
            FlatPath + "<text id=\"t\"><textPath href=\"#flat\" startOffset=\"1e38\">abc</textPath></text>",
            FlatPath + "<text id=\"t\"><textPath href=\"#flat\" startOffset=\"-1e38\">abc</textPath></text>",
            "<text id=\"t\" x=\"10\" y=\"50\" textLength=\"3e38\">abc</text>",
            "<text id=\"t\" x=\"10\" y=\"50\" textLength=\"3e38\" lengthAdjust=\"spacingAndGlyphs\">abc</text>",
            FlatPath + "<text id=\"t\"><textPath href=\"#flat\" textLength=\"3e38\">abc</textPath></text>",
            "<defs><path id=\"p\" d=\"M 10 10 L 10 10\"/></defs><text id=\"t\"><textPath href=\"#p\">abc</textPath></text>",
            "<defs><path id=\"p\" d=\"M 0 0 C 1e30 0 0 1e30 1e30 1e30\"/></defs><text id=\"t\" font-size=\"0.001\"><textPath href=\"#p\">abc</textPath></text>",
        ];

        foreach (string document in documents)
        {
            SvgDocument parsed = SvgDocument.Parse(SvgOpen + document + "</svg>", SvgTextRenderTests.Options);
            using Image<Rgba32> image = new(50, 50);
            image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(parsed, new RectangleF(0, 0, 50, 50))));
        }
    }

    private static bool HasInk(Image<Rgba32> image, Rectangle area)
    {
        for (int y = Math.Max(area.Top, 0); y < Math.Min(area.Bottom, image.Height); y++)
        {
            for (int x = Math.Max(area.Left, 0); x < Math.Min(area.Right, image.Width); x++)
            {
                if (image[x, y].A > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static SvgTextLayout Layout(string content, string id)
    {
        SvgLoadOptions options = SvgTextRenderTests.Options;
        SvgElementTree tree = SvgReader.Read(SvgOpen + content + "</svg>", SvgReaderOptions.Default);
        SvgResolver resolver = new(tree, options);
        Assert.True(tree.TryGetElementById(id, out int index));
        SvgComputedStyle style = resolver.ComputeDocumentStyle(index);
        return SvgTextLayout.Create(resolver, index, in style, new SizeF(200F, 200F), new SvgFontMatcher(options));
    }
}
