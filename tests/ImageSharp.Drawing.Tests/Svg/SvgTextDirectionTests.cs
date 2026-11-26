// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests of vertical text, right-to-left and bidirectional text, cursive scripts, and <c>tref</c> in the SVG text layout
/// pipeline (package T4).
/// </summary>
public class SvgTextDirectionTests
{
    private const string SvgOpen =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"200\" height=\"200\" "
        + "font-family=\"Open Sans\" font-size=\"20\">";

    [Fact]
    public void Vertical_UprightGlyphsAdvanceDownwardByTheirVerticalAdvance()
    {
        SvgTextLayout layout = Layout("<text id=\"t\" x=\"50\" y=\"10\" writing-mode=\"tb\" font-family=\"Noto Sans KR\">日本</text>", "t");
        SvgTextCluster first = layout.Clusters[0];
        SvgTextCluster second = layout.Clusters[1];

        // CJK ideographs stand upright in mixed vertical text. Noto Sans KR gives them a vertical advance of one em.
        Assert.Equal(0F, first.Rotation);
        Assert.Equal(50F, first.X);
        Assert.Equal(10F, first.Y);
        Assert.Equal(20F, first.Width, 3);
        Assert.Equal(50F, second.X);
        Assert.Equal(30F, second.Y, 3);
    }

    [Theory]
    [InlineData("tb")]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public void Vertical_SidewaysGlyphsTurnAndSitOnTheCentralBaseline(string mode)
    {
        // glyph-orientation-vertical has no effect, as in Chromium.
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" x=\"50\" y=\"10\" writing-mode=\"{mode}\" glyph-orientation-vertical=\"0\">ab</text>",
            "t");
        SvgTextCluster first = layout.Clusters[0];
        SvgTextCluster second = layout.Clusters[1];
        FontMetrics metrics = first.Font!.FontMetrics;
        float central = (metrics.HorizontalMetrics.Ascender + metrics.HorizontalMetrics.Descender) * 20F / metrics.UnitsPerEm / 2F;

        // Latin lies sideways, turned clockwise, with the middle of the em box on x, and advances by its horizontal
        // advance along y.
        Assert.Equal(90F, first.Rotation);
        Assert.Equal(50F - central, first.X, 3);
        Assert.Equal(10F, first.Y);
        Assert.Equal(first.X, second.X);
        Assert.Equal(10F + first.Advance, second.Y, 3);
    }

    [Fact]
    public void Vertical_SidewaysModesTurnEveryGlyphOnTheAlphabeticBaseline()
    {
        SvgTextLayout right = Layout("<text id=\"t\" x=\"50\" y=\"100\" writing-mode=\"sideways-rl\" font-family=\"Noto Sans KR\">日a</text>", "t");
        SvgTextLayout left = Layout("<text id=\"t\" x=\"50\" y=\"100\" writing-mode=\"sideways-lr\" font-family=\"Noto Sans KR\">日a</text>", "t");

        Assert.Equal([90F, 90F], [right.Clusters[0].Rotation, right.Clusters[1].Rotation]);
        Assert.Equal([50F, 50F], [right.Clusters[0].X, right.Clusters[1].X]);
        Assert.Equal(100F + right.Clusters[0].Advance, right.Clusters[1].Y, 3);

        // sideways-lr turns counterclockwise and advances upward.
        Assert.Equal([-90F, -90F], [left.Clusters[0].Rotation, left.Clusters[1].Rotation]);
        Assert.Equal(100F - left.Clusters[0].Advance, left.Clusters[1].Y, 3);
    }

    [Theory]
    [InlineData("start", 0F)]
    [InlineData("middle", 0.5F)]
    [InlineData("end", 1F)]
    public void Vertical_TextAnchorShiftsTheChunkAlongY(string anchor, float fraction)
    {
        SvgTextLayout layout = Layout(
            $"<text id=\"t\" x=\"50\" y=\"100\" writing-mode=\"tb\" text-anchor=\"{anchor}\" letter-spacing=\"3\">ab</text>",
            "t");
        float extent = layout.Clusters[0].Width + layout.Clusters[1].Width;

        Assert.Equal(100F - (fraction * extent), layout.Clusters[0].Y, 3);
        Assert.Equal(layout.Clusters[0].Advance + 3F, layout.Clusters[0].Width, 3);
        Assert.Equal(layout.Clusters[0].X, layout.Clusters[1].X);
    }

    [Fact]
    public void Vertical_WritingModeOnATspanHasNoEffect()
    {
        SvgTextLayout layout = Layout("<text id=\"t\" x=\"10\" y=\"50\">a<tspan writing-mode=\"tb\">b</tspan></text>", "t");

        Assert.Equal(0F, layout.Clusters[1].Rotation);
        Assert.Equal(50F, layout.Clusters[1].Y);
        Assert.Equal(10F + layout.Clusters[0].Width, layout.Clusters[1].X, 3);
    }

    [Theory]
    [InlineData("start", 0F)]
    [InlineData("middle", 0.5F)]
    [InlineData("end", 1F)]
    public void RightToLeft_ChunkRunsLeftwardFromItsStart(string anchor, float fraction)
    {
        SvgTextLayout layout = Layout($"<text id=\"t\" x=\"100\" y=\"50\" direction=\"rtl\" text-anchor=\"{anchor}\">ab</text>", "t");
        float a = layout.Clusters[0].Width;
        float b = layout.Clusters[1].Width;

        // Latin letters keep their order inside the right-to-left paragraph; the line ends at x for start and begins
        // there for end.
        Assert.Equal(100F - a - b + (fraction * (a + b)), layout.Clusters[0].X, 3);
        Assert.Equal(layout.Clusters[0].X + a, layout.Clusters[1].X, 3);
    }

    [Fact]
    public void Bidi_ReversesRightToLeftRunsAndAppliesShiftsInLogicalOrder()
    {
        // Open Sans has Hebrew letters, so one font shapes the whole text.
        SvgTextLayout plain = Layout("<text id=\"t\" x=\"10\" y=\"50\">ab אב cd</text>", "t");
        SvgTextLayout shifted = Layout("<text id=\"t\" x=\"10\" y=\"50\" dx=\"0 0 0 5\">ab אב cd</text>", "t");
        Span<SvgTextCluster> clusters = plain.Clusters;

        // Visual order: a b space bet alef space c d.
        Assert.Equal(10F, clusters[0].X);
        Assert.True(clusters[4].X < clusters[3].X);
        Assert.Equal(clusters[2].X + clusters[2].Width, clusters[4].X, 3);
        Assert.Equal(clusters[4].X + clusters[4].Width, clusters[3].X, 3);
        Assert.Equal(clusters[3].X + clusters[3].Width, clusters[5].X, 3);

        // A dx on the alef shifts it and every character after it in logical order, the bet included.
        for (int i = 0; i < clusters.Length; i++)
        {
            Assert.Equal(i >= 3 ? 5F : 0F, shifted.Clusters[i].X - clusters[i].X, 3);
        }
    }

    [Theory]
    [InlineData("bidi-override")]
    [InlineData("isolate-override")]
    public void Bidi_OverrideReversesLatinLetters(string value)
    {
        SvgTextLayout layout = Layout($"<text id=\"t\" x=\"10\" y=\"50\">a<tspan direction=\"rtl\" unicode-bidi=\"{value}\">bcd</tspan>e</text>", "t");
        Span<SvgTextCluster> clusters = layout.Clusters;

        Assert.Equal(10F, clusters[0].X);
        Assert.Equal(clusters[0].X + clusters[0].Width, clusters[3].X, 3);
        Assert.Equal(clusters[3].X + clusters[3].Width, clusters[2].X, 3);
        Assert.Equal(clusters[2].X + clusters[2].Width, clusters[1].X, 3);
        Assert.Equal(clusters[1].X + clusters[1].Width, clusters[4].X, 3);
    }

    [Fact]
    public void Bidi_EachTextChunkIsReorderedOnItsOwn()
    {
        // The second Hebrew letter starts a chunk at an absolute y, so the letters keep their logical order and the
        // second chunk continues from the end of the first.
        SvgTextLayout layout = Layout("<text id=\"t\" x=\"10\" y=\"50 70\">אב</text>", "t");

        Assert.Equal(10F, layout.Clusters[0].X);
        Assert.Equal(10F + layout.Clusters[0].Width, layout.Clusters[1].X, 3);
        Assert.Equal(70F, layout.Clusters[1].Y);
    }

    [Fact]
    public void Cursive_ArabicIsShapedRightToLeftWithJoiningForms()
    {
        SvgTextLayout word = Layout("<text id=\"t\" x=\"10\" y=\"50\" font-family=\"me_quran\">بسم</text>", "t");
        SvgTextLayout letter = Layout("<text id=\"t\" x=\"10\" y=\"50\" font-family=\"me_quran\">ب</text>", "t");
        Span<SvgTextCluster> clusters = word.Clusters;

        // The first letter in logical order is the rightmost, and it takes its initial form, not the isolated one.
        Assert.True(clusters[0].X > clusters[1].X);
        Assert.True(clusters[1].X > clusters[2].X);
        Assert.Equal(10F, clusters[2].X, 3);
        Assert.NotEqual(letter.Glyphs[letter.Clusters[0].GlyphStart].GlyphId, word.Glyphs[clusters[0].GlyphStart].GlyphId);
    }

    [Fact]
    public void Cursive_LetterSpacingDoesNotApplyToArabic()
    {
        SvgTextLayout plain = Layout("<text id=\"t\" font-family=\"Open Sans, me_quran\">aسل</text>", "t");
        SvgTextLayout spaced = Layout("<text id=\"t\" font-family=\"Open Sans, me_quran\" letter-spacing=\"4\">aسل</text>", "t");

        Assert.Equal(plain.Clusters[0].Width + 4F, spaced.Clusters[0].Width, 3);
        Assert.Equal(plain.Clusters[1].Width, spaced.Clusters[1].Width, 3);
        Assert.Equal(plain.Clusters[2].Width, spaced.Clusters[2].Width, 3);
    }

    [Fact]
    public void Tref_AddsTheCharacterDataOfTheReferencedTextWithItsOwnPositions()
    {
        SvgTextLayout layout = Layout(
            "<defs><text id=\"s\">a <tspan>b</tspan></text></defs>"
            + "<text id=\"t\" y=\"50\">c<tref xlink:href=\"#s\" x=\"70\" dy=\"0 4\">ignored</tref></text>",
            "t");

        Assert.Equal("ca b", layout.Content.Text);
        Assert.Equal(70F, layout.Clusters[1].X);
        Assert.Equal(54F, layout.Clusters[2].Y);
    }

    [Theory]
    [InlineData("<defs><text id=\"s\"><tref xlink:href=\"#u\"/></text><text id=\"u\">x</text></defs><text id=\"t\">a<tref xlink:href=\"#s\"/></text>")]
    [InlineData("<defs><rect id=\"s\"/></defs><text id=\"t\">a<tref xlink:href=\"#s\"/></text>")]
    [InlineData("<text id=\"t\">a<tref xlink:href=\"other.svg#s\"/></text>")]
    public void Tref_AddsNothingForNestedNonTextAndExternalReferences(string content)
        => Assert.Equal("a", Layout(content, "t").Content.Text);

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
