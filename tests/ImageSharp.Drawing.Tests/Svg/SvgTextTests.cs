// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Text;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests of SVG text: the parsing of text content, attributes, and properties, white space processing, font matching,
/// the stages of the layout pipeline, and the limits.
/// </summary>
public class SvgTextTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\" font-family=\"Open Sans\" font-size=\"20\">";

    [Fact]
    public void FontFamily_ParsesNamesQuotesAndGenericFamilies()
    {
        SvgTextStyle text = ComputeText("<text id=\"t\" font-family=\" 'My Font', Arial   Black , serif,&quot;sans-serif&quot;\">a</text>", "t");

        Assert.Equal(4, text.FontFamily.Length);
        Assert.Equal("My Font", text.FontFamily[0].Name);
        Assert.Equal("Arial Black", text.FontFamily[1].Name);
        Assert.Equal(SvgGenericFontFamily.Serif, text.FontFamily[2].Generic);

        // A quoted generic keyword names a family.
        Assert.Equal("sans-serif", text.FontFamily[3].Name);
        Assert.Equal(SvgGenericFontFamily.None, text.FontFamily[3].Generic);
    }

    [Theory]
    [InlineData("1Font")]
    [InlineData("A, ,B")]
    [InlineData("inherit Font")]
    [InlineData("'unterminated")]
    public void FontFamily_InvalidValueIsDropped(string value)
    {
        SvgTextStyle text = ComputeText($"<g font-family=\"Parent\"><text id=\"t\" font-family=\"{value}\">a</text></g>", "t");

        Assert.Equal("Parent", Assert.Single(text.FontFamily).Name);
    }

    [Fact]
    public void FontShorthand_SetsTheLonghandsAndResetsTheOthers()
    {
        SvgElementTree tree = Read("<text id=\"t\" font-kerning=\"none\" style=\"font: italic small-caps bold condensed 30px/2 'Open Sans', serif\">a</text>");
        SvgResolver resolver = new(tree, new SvgLoadOptions());
        SvgComputedStyle style = resolver.ComputeDocumentStyle(Index(tree, "t"));

        Assert.Equal(30F, style.FontSize);
        Assert.Equal(SvgFontStyle.Italic, style.Text.FontStyle);
        Assert.Equal(SvgFontVariant.SmallCaps, style.Text.FontVariant);
        Assert.Equal(700F, style.Text.FontWeight);
        Assert.Equal(75F, style.Text.FontStretch);
        Assert.Equal("Open Sans", style.Text.FontFamily[0].Name);
        Assert.Equal(SvgGenericFontFamily.Serif, style.Text.FontFamily[1].Generic);

        // The shorthand resets font-kerning, and the style attribute wins over the presentation attribute.
        Assert.Equal(SvgFontKerning.Auto, style.Text.FontKerning);
    }

    [Fact]
    public void TextDecoration_IsAShorthandInAttributesAndStyleSheets()
    {
        SvgTextStyle attribute = ComputeText("<text id=\"t\" text-decoration=\"underline overline\">a</text>", "t");
        SvgTextStyle css = ComputeText("<text id=\"t\" style=\"text-decoration: line-through wavy red\">a</text>", "t");

        Assert.Equal(SvgTextDecorationLine.Underline | SvgTextDecorationLine.Overline, attribute.TextDecorationLine);
        Assert.Equal(SvgTextDecorationLine.LineThrough, css.TextDecorationLine);
        Assert.Equal(SvgTextDecorationStyle.Wavy, css.TextDecorationStyle);
        Assert.Equal(SvgPaintKind.Color, css.TextDecorationColor.Kind);
        Assert.Equal(Color.Red, css.TextDecorationColor.Color);
    }

    [Theory]
    [InlineData(300F, "bolder", 400F)]
    [InlineData(400F, "bolder", 700F)]
    [InlineData(600F, "bolder", 900F)]
    [InlineData(900F, "bolder", 900F)]
    [InlineData(500F, "lighter", 100F)]
    [InlineData(700F, "lighter", 400F)]
    [InlineData(900F, "lighter", 700F)]
    public void FontWeight_RelativeKeywordsFollowTheCssTable(float parent, string keyword, float expected)
    {
        SvgTextStyle text = ComputeText($"<g font-weight=\"{parent}\"><text id=\"t\" font-weight=\"{keyword}\">a</text></g>", "t");

        Assert.Equal(expected, text.FontWeight);
    }

    [Fact]
    public void Spacing_EmComputesAtTheSpecifyingElementAndInheritsAsPixels()
    {
        SvgTextStyle text = ComputeText(
            "<text font-size=\"10\" letter-spacing=\"0.5em\" word-spacing=\"10%\"><tspan id=\"t\" font-size=\"40\">a</tspan></text>",
            "t");

        Assert.Equal(new SvgLength(5F, SvgLengthUnit.Px), text.LetterSpacing);
        Assert.Equal(new SvgLength(10F, SvgLengthUnit.Percent), text.WordSpacing);
    }

    [Fact]
    public void TextProperties_InheritExceptTheNonInheritedOnes()
    {
        SvgTextStyle text = ComputeText(
            "<text text-anchor=\"middle\" baseline-shift=\"super\" alignment-baseline=\"hanging\" unicode-bidi=\"embed\""
            + " dominant-baseline=\"central\" writing-mode=\"tb\"><tspan id=\"t\">a</tspan></text>",
            "t");

        Assert.Equal(SvgTextAnchor.Middle, text.TextAnchor);
        Assert.Equal(SvgDominantBaseline.Central, text.DominantBaseline);
        Assert.Equal(SvgWritingMode.VerticalRl, text.WritingMode);
        Assert.Equal(SvgBaselineShiftKind.Baseline, text.BaselineShiftKind);
        Assert.Equal(SvgAlignmentBaseline.Auto, text.AlignmentBaseline);
        Assert.Equal(SvgUnicodeBidi.Normal, text.UnicodeBidi);
    }

    [Fact]
    public void XmlSpace_MapsToWhiteSpaceAndTheWhiteSpaceAttributeWins()
    {
        Assert.Equal(SvgWhiteSpace.Pre, ComputeText("<text id=\"t\" xml:space=\"preserve\">a</text>", "t").WhiteSpace);
        Assert.Equal(SvgWhiteSpace.Nowrap, ComputeText("<text id=\"t\" xml:space=\"default\">a</text>", "t").WhiteSpace);
        Assert.Equal(SvgWhiteSpace.PreLine, ComputeText("<text id=\"t\" xml:space=\"preserve\" white-space=\"pre-line\">a</text>", "t").WhiteSpace);

        // Chromium maps xml:space only on text content elements.
        Assert.Equal(SvgWhiteSpace.Normal, ComputeText("<g xml:space=\"preserve\"><text id=\"t\">a</text></g>", "t").WhiteSpace);
    }

    [Fact]
    public void Attributes_ParsePositionListsAndDropInvalidLists()
    {
        SvgElementTree tree = Read("<text id=\"t\" x=\"1, 2 3%\" dy=\"1em\" rotate=\"10 abc\" textLength=\"-5\" lengthAdjust=\"spacingAndGlyphs\">a</text>");
        ref readonly SvgTextAttributes attributes = ref tree.GetText(Index(tree, "t"));

        Assert.Equal(3, tree.GetTextLengths(attributes.X).Length);
        Assert.Equal(new SvgLength(3F, SvgLengthUnit.Percent), tree.GetTextLengths(attributes.X)[2]);
        Assert.Equal(new SvgLength(1F, SvgLengthUnit.Em), Assert.Single(tree.GetTextLengths(attributes.Dy).ToArray()));
        Assert.Equal(0, attributes.Rotate.Count);
        Assert.False(attributes.TextLength.IsSpecified);
        Assert.Equal(SvgLengthAdjust.SpacingAndGlyphs, attributes.LengthAdjust);
    }

    [Fact]
    public void Attributes_ParseTextPathAndTref()
    {
        SvgElementTree tree = Read(
            "<path id=\"p\" d=\"M0 0 L100 0\"/><text><textPath id=\"tp\" href=\"#p\" startOffset=\"10%\" method=\"stretch\""
            + " spacing=\"auto\" side=\"right\" path=\"M0 0 L 50 50\">a</textPath><tref id=\"r\" xlink:href=\"#p\" "
            + "xmlns:xlink=\"http://www.w3.org/1999/xlink\"/></text>");
        ref readonly SvgTextPathAttributes path = ref tree.GetTextPath(Index(tree, "tp"));

        Assert.Equal("p", path.Href);
        Assert.Equal(new SvgLength(10F, SvgLengthUnit.Percent), path.StartOffset);
        Assert.Equal(SvgTextPathMethod.Stretch, path.Method);
        Assert.Equal(SvgTextPathSpacing.Auto, path.Spacing);
        Assert.Equal(SvgTextPathSide.Right, path.Side);
        Assert.NotNull(path.Path);
        Assert.Equal("p", tree.GetTrefReference(Index(tree, "r")));
    }

    [Theory]
    [InlineData("<text id=\"t\">  a  <tspan> b </tspan>\n\t c  </text>", "a b c")]
    [InlineData("<text id=\"t\">a<tspan>  </tspan>b</text>", "a b")]
    [InlineData("<text id=\"t\" xml:space=\"preserve\"> a\n\tb </text>", " a  b ")]
    [InlineData("<text id=\"t\" xml:space=\"preserve\"> a <tspan xml:space=\"default\">  b  </tspan> c</text>", " a  b  c")]
    [InlineData("<text id=\"t\">&#48;<![CDATA[<b>]]>&amp;</text>", "0<b>&")]
    [InlineData("<text id=\"t\">a<tspan display=\"none\">x</tspan>b<tspan systemLanguage=\"xx\">y</tspan>c<g>z</g></text>", "abc")]
    [InlineData("<text id=\"t\">a<tref xlink:href=\"#t\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"/>b</text>", "ab")]
    public void Characters_ProcessWhiteSpaceAndSkipChildrenThatDoNotRender(string content, string expected)
        => Assert.Equal(expected, BuildContent(content, "t").Text);

    [Fact]
    public void Characters_NestedListsOverwriteOnlyTheCharactersTheyCover()
    {
        SvgTextContent content = BuildContent("<text id=\"t\" x=\"1 2 3 4\" rotate=\"5 6\">a<tspan x=\"9\" rotate=\"7\">bc</tspan>de</text>", "t");
        SvgTextCharacter[] c = content.Characters;

        Assert.Equal([1F, 9F, 3F, 4F], [c[0].X, c[1].X, c[2].X, c[3].X]);
        Assert.True(float.IsNaN(c[4].X));

        // The tspan repeats its last angle; after it, the text continues its own list, which repeats 6.
        Assert.Equal([5F, 7F, 7F, 6F, 6F], [c[0].Rotate, c[1].Rotate, c[2].Rotate, c[3].Rotate, c[4].Rotate]);
        Assert.True(c[1].HasOwnPosition);
        Assert.False(c[4].HasOwnPosition);
    }

    [Fact]
    public void Characters_AddressSurrogatePairsAsOneCharacter()
    {
        SvgTextContent content = BuildContent("<text id=\"t\" x=\"1 2 3\">&#x1F600;ab</text>", "t");

        Assert.Equal(4, content.Text.Length);
        Assert.Equal([1F, 2F, 3F], [content.Characters[0].X, content.Characters[2].X, content.Characters[3].X]);
        Assert.False(content.Characters[1].IsAddressable);
    }

    [Fact]
    public void FontMatching_FollowsTheDocumentedOrder()
    {
        SvgLoadOptions options = SvgTextRenderTests.Options;
        options.DefaultFontFamily = "serif";
        options.GenericFontFamilies = new SvgGenericFontFamilies { Serif = ["Missing", "Open Sans"], SansSerif = ["Missing"] };
        SvgFontMatcher matcher = new(options);

        // A generic name takes its first family that the collection has.
        Assert.Equal("Open Sans", Chain(matcher, new SvgFontFamilyName(SvgGenericFontFamily.Serif)).Primary!.Family.Name);

        // An unknown family and a generic name without a family fall back to the default family.
        Assert.Equal("Open Sans", Chain(matcher, new SvgFontFamilyName("Nope"), new SvgFontFamilyName(SvgGenericFontFamily.SansSerif)).Primary!.Family.Name);

        // Without a matching default family, the first family of the collection by name is used.
        options.DefaultFontFamily = "Nothing";
        Assert.Equal("Open Sans", Chain(new SvgFontMatcher(options), new SvgFontFamilyName("Nope")).Primary!.Family.Name);
    }

    [Fact]
    public void FontMatching_EmptyCollectionHasNoFontAndTextDrawsNothing()
    {
        SvgLoadOptions options = new() { FontCollection = new FontCollection() };
        Assert.Null(Chain(new SvgFontMatcher(options), new SvgFontFamilyName("Open Sans")).Primary);

        SvgDocument document = SvgDocument.Parse(SvgOpen + "<text x=\"10\" y=\"50\">Text</text></svg>", options);
        Assert.True(document.Nodes.IsEmpty);
    }

    [Fact]
    public void FontMatching_WeightAndWidthScoresFollowTheCssSearchOrder()
    {
        // A request of 400 tries 500 before the lighter weights, and those before the heavier weights above 500.
        Assert.True(SvgFontMatcher.WeightScore(400F, 400F) < SvgFontMatcher.WeightScore(500F, 400F));
        Assert.True(SvgFontMatcher.WeightScore(500F, 400F) < SvgFontMatcher.WeightScore(300F, 400F));
        Assert.True(SvgFontMatcher.WeightScore(300F, 400F) < SvgFontMatcher.WeightScore(600F, 400F));

        // Above 500 the heavier weights come first; below 400 the lighter ones.
        Assert.True(SvgFontMatcher.WeightScore(900F, 600F) < SvgFontMatcher.WeightScore(500F, 600F));
        Assert.True(SvgFontMatcher.WeightScore(100F, 300F) < SvgFontMatcher.WeightScore(400F, 300F));

        // A narrow request prefers narrower widths; a wide request wider ones.
        Assert.True(SvgFontMatcher.StretchScore(62.5F, 75F) < SvgFontMatcher.StretchScore(87.5F, 75F));
        Assert.True(SvgFontMatcher.StretchScore(150F, 125F) < SvgFontMatcher.StretchScore(112.5F, 125F));
    }

    [Fact]
    public void Layout_AppliesSpacingShiftsAndChunks()
    {
        SvgTextLayout plain = Layout("<text id=\"t\" x=\"10\" y=\"50\">ab c</text>", "t");
        SvgTextLayout spaced = Layout("<text id=\"t\" x=\"10\" y=\"50\" letter-spacing=\"2\" word-spacing=\"5\" dy=\"0 3\">ab c</text>", "t");

        Assert.Equal(10F, plain.Clusters[0].X);
        Assert.True(plain.Clusters[0].StartsChunk);
        Assert.False(plain.Clusters[1].StartsChunk);

        // Letter spacing follows every cluster; word spacing follows the space; dy shifts the pen from then on.
        float advanceA = plain.Clusters[0].Advance;
        Assert.Equal(10F + advanceA + 2F, spaced.Clusters[1].X, 3);
        Assert.Equal(spaced.Clusters[2].X + spaced.Clusters[2].Advance + 2F + 5F, spaced.Clusters[3].X, 3);
        Assert.Equal(53F, spaced.Clusters[1].Y);
        Assert.Equal(53F, spaced.Clusters[3].Y);
    }

    [Theory]
    [InlineData("start", 0F)]
    [InlineData("middle", 0.5F)]
    [InlineData("end", 1F)]
    public void Layout_AnchorsEachChunk(string anchor, float fraction)
    {
        SvgTextLayout layout = Layout($"<text id=\"t\" x=\"100 150\" y=\"50\" text-anchor=\"{anchor}\">AB</text>", "t");

        // Each character starts a chunk of its own, so each shifts by its own advance.
        Assert.Equal(100F - (fraction * layout.Clusters[0].Width), layout.Clusters[0].X, 3);
        Assert.Equal(150F - (fraction * layout.Clusters[1].Width), layout.Clusters[1].X, 3);
    }

    [Fact]
    public void Layout_BreaksLigaturesWhereAPositionFallsInside()
    {
        SvgTextLayout joined = Layout("<text id=\"t\">ffi</text>", "t");
        SvgTextLayout broken = Layout("<text id=\"t\" dx=\"0 0 1\">ffi</text>", "t");

        // Without positions every character keeps its own cluster or joins a ligature; a position on the last
        // character gives it a cluster of its own.
        Assert.True(joined.Clusters.Length <= 3);
        Assert.Equal(2, broken.Clusters[^1].CharStart);
    }

    [Fact]
    public void Layout_BoundingBoxIsTheUnionOfTheGlyphCells()
    {
        SvgTextLayout layout = Layout("<text id=\"t\" x=\"10\" y=\"50\" font-size=\"20\">Ab</text>", "t");
        Font font = layout.Clusters[0].Font!;
        float scale = 20F / font.FontMetrics.UnitsPerEm;
        float ascent = font.FontMetrics.HorizontalMetrics.Ascender * scale;
        float descent = -font.FontMetrics.HorizontalMetrics.Descender * scale;
        RectangleF box = layout.Bounds!.Value;

        Assert.Equal(10F, box.X, 3);
        Assert.Equal(50F - ascent, box.Y, 3);
        Assert.Equal(layout.Clusters[0].Width + layout.Clusters[1].Width, box.Width, 3);
        Assert.Equal(ascent + descent, box.Height, 3);
    }

    [Fact]
    public void Limits_TextCharactersAreBoundedInTheDocument()
    {
        SvgLoadOptions options = SvgTextRenderTests.Options;
        options.Limits = new SvgLimits { MaxTextCharacters = 10 };

        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(SvgOpen + "<text>0123456789A</text></svg>", options));
        _ = SvgDocument.Parse(SvgOpen + "<text>0123456789</text></svg>", options);
    }

    [Fact]
    public void Limits_TextCharactersCountPerUseInstance()
    {
        SvgLoadOptions options = SvgTextRenderTests.Options;
        options.Limits = new SvgLimits { MaxTextCharacters = 100 };
        StringBuilder svg = new(SvgOpen);
        svg.Append("<defs><text id=\"t\">0123456789</text></defs>");
        for (int i = 0; i < 11; i++)
        {
            svg.Append("<use href=\"#t\"/>");
        }

        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(svg.Append("</svg>").ToString(), options));
    }

    [Fact]
    public void Limits_AUseStormOfTextStopsAtALimit()
    {
        // Each level references the previous one ten times: 10^6 instances of one character without the limits.
        StringBuilder svg = new(SvgOpen);
        svg.Append("<defs><text id=\"l0\">x</text>");
        for (int level = 1; level <= 6; level++)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<g id=\"l{level}\">");
            for (int i = 0; i < 10; i++)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<use href=\"#l{level - 1}\"/>");
            }

            svg.Append("</g>");
        }

        svg.Append("</defs><use href=\"#l6\"/></svg>");
        SvgLoadOptions options = SvgTextRenderTests.Options;
        options.Limits = new SvgLimits { MaxTextCharacters = 50_000 };

        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(svg.ToString(), options));
    }

    [Fact]
    public void Robustness_LongTextAndHugePositionListsLoad()
    {
        string longText = new('a', 50_000);
        SvgDocument document = SvgDocument.Parse(SvgOpen + $"<text x=\"0\" y=\"20\">{longText}</text></svg>", SvgTextRenderTests.Options);
        Assert.False(document.Nodes.IsEmpty);

        // A list far longer than the text costs only its parse.
        StringBuilder list = new();
        for (int i = 0; i < 200_000; i++)
        {
            list.Append(i % 100).Append(' ');
        }

        document = SvgDocument.Parse(SvgOpen + $"<text x=\"{list}\" y=\"20\">abc</text></svg>", SvgTextRenderTests.Options);
        Assert.False(document.Nodes.IsEmpty);
    }

    [Fact]
    public void Robustness_MissingFamilyUsesTheDefaultFamily()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "<text x=\"10\" y=\"50\" font-family=\"No Such Font, monospace\">Text</text></svg>",
            SvgTextRenderTests.Options);

        Assert.False(document.Nodes.IsEmpty);
    }

    [Fact]
    public void Robustness_ConcurrentDrawsOfOneDocumentAreEqual()
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/text-paint.svg"), SvgTextRenderTests.Options);
        Image<Rgba32>[] images = new Image<Rgba32>[8];
        Parallel.For(0, images.Length, i =>
        {
            Image<Rgba32> image = new(200, 200);
            image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 200, 200))));
            images[i] = image;
        });

        try
        {
            for (int i = 1; i < images.Length; i++)
            {
                ImageComparer.Exact.VerifySimilarity(images[0], images[i]);
            }
        }
        finally
        {
            foreach (Image<Rgba32> image in images)
            {
                image.Dispose();
            }
        }
    }

    private static SvgFontChain Chain(SvgFontMatcher matcher, params SvgFontFamilyName[] families)
    {
        SvgTextStyle style = SvgTextStyle.Initial.Clone();
        style.FontFamily = families;
        return matcher.GetChain(style);
    }

    private static SvgTextStyle ComputeText(string content, string id)
    {
        SvgElementTree tree = Read(content);
        SvgResolver resolver = new(tree, new SvgLoadOptions());
        return resolver.ComputeDocumentStyle(Index(tree, id)).Text;
    }

    private static SvgTextContent BuildContent(string content, string id)
    {
        SvgElementTree tree = Read(content);
        SvgResolver resolver = new(tree, SvgTextRenderTests.Options);
        int index = Index(tree, id);
        SvgComputedStyle style = resolver.ComputeDocumentStyle(index);
        return SvgTextCharacters.Build(resolver, index, in style, new SizeF(200F, 200F));
    }

    private static SvgTextLayout Layout(string content, string id)
    {
        SvgLoadOptions options = SvgTextRenderTests.Options;
        SvgElementTree tree = Read(content);
        SvgResolver resolver = new(tree, options);
        int index = Index(tree, id);
        SvgComputedStyle style = resolver.ComputeDocumentStyle(index);
        return SvgTextLayout.Create(resolver, index, in style, new SizeF(200F, 200F), new SvgFontMatcher(options));
    }

    private static int Index(SvgElementTree tree, string id)
    {
        Assert.True(tree.TryGetElementById(id, out int index));
        return index;
    }

    private static SvgElementTree Read(string content) => SvgReader.Read(SvgOpen + content + "</svg>", SvgReaderOptions.Default);
}
