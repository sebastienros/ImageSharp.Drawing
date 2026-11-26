// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests of the visual order of bidirectional SVG text, which the Unicode bidirectional algorithm (UAX #9) gives through
/// the local copy of the SixLabors.Fonts implementation (package T6).
/// </summary>
/// <remarks>
/// The documents write right-to-left letters as ASCII capitals, as UAX #9 does in its examples: before the document loads,
/// each capital becomes a Hebrew letter (A is U+05D0, B is U+05D1, and so on), and the visual string maps them back. The
/// visual string is the text of the clusters ordered by their x position, so it shows where the layout places each
/// character, not the glyph it draws. Each expected string is derived from the rules of UAX #9 in the comment of its
/// case. Open Sans has the Hebrew letters; me_quran has the Arabic ones.
/// </remarks>
public class SvgTextBidiTests
{
    private const string SvgOpen =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"400\" height=\"200\" font-family=\"Open Sans, me_quran\" font-size=\"20\">";

    /// <summary>
    /// The first Hebrew letter, which stands for the capital A.
    /// </summary>
    private const char Alef = 'א';

    /// <summary>
    /// The examples of rule L2 in UAX #9, with the explicit embeddings written as <c>unicode-bidi: embed</c>.
    /// </summary>
    /// <param name="text">The content of the <c>text</c> element.</param>
    /// <param name="expected">The visual string.</param>
    [Theory]

    // Levels 00000000001110: the right-to-left word reverses.
    [InlineData("<text id=\"t\">car means CAR.</text>", "car means RAC.")]

    // An RLE embedding: levels 22211111111111; car (2) reverses at level 2, then the whole embedding at level 1.
    [InlineData("<text id=\"t\"><tspan direction=\"rtl\" unicode-bidi=\"embed\">car MEANS CAR.</tspan></text>", ".RAC SNAEM car")]

    // The period and the closing quotation mark after the embedding lie between sos R and eos L, so N2 gives them the
    // paragraph level 0.
    [InlineData(
        "<text id=\"t\">he said “<tspan direction=\"rtl\" unicode-bidi=\"embed\">car MEANS CAR</tspan>.”</text>",
        "he said “RAC SNAEM car.”")]

    // A right-to-left paragraph with an LRE (level 2) that holds an RLE (level 3, car at 4): the four reversals of L2
    // give the question mark first and the left-to-right quotation in the middle.
    [InlineData(
        "<text id=\"t\" direction=\"rtl\">DID YOU SAY ’<tspan direction=\"ltr\" unicode-bidi=\"embed\">he said “"
            + "<tspan direction=\"rtl\" unicode-bidi=\"embed\">car MEANS CAR</tspan>”</tspan>?</text>",
        "?he said “RAC SNAEM car”’ YAS UOY DID")]
    public void VisualOrder_FollowsTheExamplesOfUax9(string text, string expected)
        => Assert.Equal(expected, Visual(Layout(text)));

    /// <summary>
    /// Paired brackets (rule N0), isolates, overrides, numbers, and the paragraph level.
    /// </summary>
    /// <param name="text">The content of the <c>text</c> element.</param>
    /// <param name="expected">The visual string.</param>
    [Theory]

    // N0 b: the brackets enclose a left-to-right word in a right-to-left paragraph, and the strong type before them is
    // R, so they take the embedding direction R and reverse with the paragraph. The glyphs mirror (see below).
    [InlineData("<text id=\"t\" direction=\"rtl\">AB(cd)EF</text>", "FE)cd(BA")]

    // N0 c 2: right-to-left letters inside brackets in a left-to-right paragraph, with L before them: the brackets
    // stay L.
    [InlineData("<text id=\"t\">ab(CD)ef</text>", "ab(DC)ef")]

    // N0 c 1: with R before them, the brackets take R and reverse with the letters around them.
    [InlineData("<text id=\"t\">AB(CD)ef</text>", ")DC(BAef")]

    // A lone bracket at the end of a right-to-left paragraph lies between L and eos R, so N2 gives it the level 1.
    [InlineData("<text id=\"t\" direction=\"rtl\">abc(</text>", "(abc")]

    // An isolate counts as a neutral for the text around it, so N1 puts the spaces and the isolate between the two
    // Hebrew words at level 1, and the whole line reverses. Without the isolate, the spaces next to the Latin word stay
    // at level 0.
    [InlineData("<text id=\"t\">AB <tspan unicode-bidi=\"isolate\">cd</tspan> EF</text>", "FE cd BA")]
    [InlineData("<text id=\"t\">AB <tspan>cd</tspan> EF</text>", "BA cd FE")]

    // Overrides: a left-to-right override keeps Hebrew letters in logical order; a right-to-left override on the text
    // element reverses Latin letters.
    [InlineData("<text id=\"t\">x<tspan direction=\"ltr\" unicode-bidi=\"bidi-override\">AB</tspan>y</text>", "xABy")]
    [InlineData("<text id=\"t\" direction=\"rtl\" unicode-bidi=\"bidi-override\">abc</text>", "cba")]

    // European numbers in right-to-left text: level 2 (I2), so they keep their order; a period between digits is part
    // of the number (W4).
    [InlineData("<text id=\"t\" direction=\"rtl\">AB 123 CD</text>", "DC 123 BA")]
    [InlineData("<text id=\"t\" direction=\"rtl\">AB 1.5 CD</text>", "DC 1.5 BA")]

    // After a Hebrew letter the percent sign joins the number (W5) and moves with it. After an Arabic letter the digits
    // become Arabic numbers (W2), W5 does not apply, and the percent sign stays at the paragraph level, at the end.
    [InlineData("<text id=\"t\">A 12%</text>", "12% A")]
    [InlineData("<text id=\"t\">ب 12%</text>", "12 ب%")]

    // plaintext takes the paragraph direction from the first strong character (P2, P3), whatever direction says.
    [InlineData("<text id=\"t\" unicode-bidi=\"plaintext\">ABC def</text>", "def CBA")]
    [InlineData("<text id=\"t\" direction=\"rtl\" unicode-bidi=\"plaintext\">abc DEF</text>", "abc FED")]

    // L1: the space at the end of the chunk returns to the paragraph level 0 and stays at the right end; the space
    // between the Hebrew letters stays at level 1.
    [InlineData("<text id=\"t\" xml:space=\"preserve\">a<tspan direction=\"rtl\" unicode-bidi=\"embed\">B C </tspan></text>", "aC B ")]
    public void VisualOrder_FollowsTheRulesOfUax9(string text, string expected)
        => Assert.Equal(expected, Visual(Layout(text)));

    /// <summary>
    /// Characters at an odd level draw their mirrored glyph once (rule L4): the shaper mirrors in a right-to-left run,
    /// and the pipeline does not mirror again. The lone bracket is the case that the workaround of package T4 missed.
    /// </summary>
    /// <param name="text">The content of the <c>text</c> element.</param>
    /// <param name="index">The index of the bracket in the characters.</param>
    /// <param name="mirrored">Whether the bracket is at an odd level.</param>
    [Theory]
    [InlineData("<text id=\"t\" direction=\"rtl\">abc(</text>", 3, true)]
    [InlineData("<text id=\"t\">abc(</text>", 3, false)]
    [InlineData("<text id=\"t\" direction=\"rtl\">AB(cd)EF</text>", 2, true)]
    [InlineData("<text id=\"t\">ab(CD)ef</text>", 2, false)]
    public void Mirroring_RightToLeftBracketsDrawTheirMirrorOnce(string text, int index, bool mirrored)
    {
        SvgTextLayout layout = Layout(text);
        ushort open = GlyphOf(Layout("<text id=\"t\">(</text>"), 0);
        ushort close = GlyphOf(Layout("<text id=\"t\">)</text>"), 0);

        Assert.NotEqual(open, close);
        Assert.Equal(mirrored ? close : open, GlyphOf(layout, index));
    }

    /// <summary>
    /// Embeddings nested deeper than the 125 levels of the algorithm (rule X2 and following) overflow: the characters
    /// keep the deepest valid level, the matching terminators restore the levels, and unmatched terminators have no
    /// effect.
    /// </summary>
    [Fact]
    public void Embeddings_BeyondTheDepthLimitOverflowWithoutFailing()
    {
        // 70 RLE and LRE pairs open 140 levels, deeper than the limit, and 150 PDF close them and 10 more.
        StringBuilder text = new("<text id=\"t\">ab");
        for (int i = 0; i < 70; i++)
        {
            text.Append("‫‪");
        }

        text.Append("CD");
        text.Append('‬', 150);
        text.Append("⁩ef</text>");
        SvgTextLayout layout = Layout(text.ToString());

        // The Hebrew letters lie at an odd level (the last valid RLE gives 125), so they reverse; the Latin letters
        // before and after them lie at level 0 again.
        Assert.Equal("abDCef", string.Concat(Visual(layout).Where(char.IsLetter)));
    }

    /// <summary>
    /// Returns the text of the clusters of a layout ordered by their x position, with Hebrew letters written as
    /// capitals.
    /// </summary>
    /// <param name="layout">The layout of one horizontal text chunk.</param>
    /// <returns>The visual string.</returns>
    private static string Visual(SvgTextLayout layout)
    {
        SvgTextCluster[] clusters = layout.Clusters.ToArray();
        Array.Sort(clusters, static (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.CharStart.CompareTo(b.CharStart));
        StringBuilder visual = new();
        foreach (SvgTextCluster cluster in clusters)
        {
            foreach (char c in layout.Content.Text.AsSpan(cluster.CharStart, cluster.CharEnd - cluster.CharStart))
            {
                visual.Append(c is >= Alef and < (char)(Alef + 26) ? (char)('A' + (c - Alef)) : c);
            }
        }

        return visual.ToString();
    }

    /// <summary>
    /// Returns the first glyph of the cluster that holds a character.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="index">The index of the character.</param>
    /// <returns>The glyph id.</returns>
    private static ushort GlyphOf(SvgTextLayout layout, int index)
    {
        foreach (SvgTextCluster cluster in layout.Clusters)
        {
            if (cluster.CharStart <= index && index < cluster.CharEnd)
            {
                return layout.Glyphs[cluster.GlyphStart].GlyphId;
            }
        }

        throw new InvalidOperationException("No cluster holds the character.");
    }

    /// <summary>
    /// Lays out the <c>text</c> element with id <c>t</c>, after writing each ASCII capital as a Hebrew letter.
    /// </summary>
    /// <param name="content">The content of the document.</param>
    /// <returns>The layout.</returns>
    private static SvgTextLayout Layout(string content)
    {
        StringBuilder hebrew = new(content.Length);
        foreach (char c in content)
        {
            hebrew.Append(c is >= 'A' and <= 'Z' ? (char)(Alef + (c - 'A')) : c);
        }

        SvgLoadOptions options = SvgTextDirectionRenderTests.Options;
        SvgElementTree tree = SvgReader.Read(SvgOpen + hebrew + "</svg>", SvgReaderOptions.Default);
        SvgResolver resolver = new(tree, options);
        Assert.True(tree.TryGetElementById("t", out int index));
        SvgComputedStyle style = resolver.ComputeDocumentStyle(index);
        return SvgTextLayout.Create(resolver, index, in style, new SizeF(400F, 200F), new SvgFontMatcher(options));
    }
}
