// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests of SVG 2 and CSS features: CSS basic shapes in <c>clip-path</c>, <c>pathLength</c>, the font feature and font
/// variant properties through shaping, the white space properties, and the <c>:lang()</c> selector. The expected
/// results come from the specifications and from renders of the Chromium oracle in <c>tests/SvgTools</c>; each test says
/// which.
/// </summary>
public class SvgSvg2FeatureTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\">";

    private static readonly Rgba32 Green = new(0, 128, 0, 255);

    // Chromium: a basic shape without a box refers to the stroke box (CSS Masking: border-box, which is the stroke box on
    // SVG elements); content-box and padding-box are the fill box, border-box and margin-box the stroke box.
    [Theory]
    [InlineData("circle()", true)]
    [InlineData("circle() stroke-box", true)]
    [InlineData("circle() border-box", true)]
    [InlineData("circle() margin-box", true)]
    [InlineData("circle() fill-box", false)]
    [InlineData("circle() content-box", false)]
    [InlineData("circle() padding-box", false)]
    public void ClipShape_ReferenceBoxes(string value, bool strokeBox)
    {
        // The rectangle fills 20 to 80 and strokes 10 to 90: the circle of the stroke box has radius 40, the circle of
        // the fill box radius 30. (14, 50) lies in the stroke and only inside the larger circle.
        using Image<Rgba32> image = Render(
            $"<rect x=\"20\" y=\"20\" width=\"60\" height=\"60\" fill=\"green\" stroke=\"green\" stroke-width=\"20\" style=\"clip-path: {value}\"/>");

        Assert.Equal(strokeBox ? Green : default, image[14, 50]);
        Assert.Equal(Green, image[50, 50]);
        Assert.Equal(default, image[19, 19]);
    }

    // Chromium: a stroke that is none adds nothing to the stroke box, a transparent stroke does.
    [Theory]
    [InlineData("none", false)]
    [InlineData("transparent", true)]
    public void ClipShape_StrokeBoxCountsAPaintedStroke(string stroke, bool larger)
    {
        // The circle of the fill box has radius 30 and leaves the corner (22, 22) of the fill out; radius 40 keeps it.
        using Image<Rgba32> image = Render(
            $"<rect x=\"20\" y=\"20\" width=\"60\" height=\"60\" fill=\"green\" stroke=\"{stroke}\" stroke-width=\"20\" style=\"clip-path: circle()\"/>");

        Assert.Equal(larger ? Green : default, image[24, 24]);
    }

    // CSS Masking: view-box is a box at the origin of the coordinate system of the viewBox with its size, as the
    // Chromium oracle renders it: circle(50px) is centered at (100, 100) of the viewBox, which maps to (25, 25).
    [Fact]
    public void ClipShape_ViewBoxStartsAtTheOrigin()
    {
        using Image<Rgba32> image = Render(
            """
            <svg width="100" height="100" viewBox="50 50 200 200">
              <rect x="50" y="50" width="200" height="200" fill="green" style="clip-path: circle(50px) view-box"/>
            </svg>
            """);

        Assert.Equal(Green, image[25, 25]);
        Assert.Equal(default, image[60, 60]);
    }

    // CSS Shapes: insets that add up to more than the box shrink to fit, which leaves no area here; a shape without area
    // clips everything, as in Chromium.
    [Theory]
    [InlineData("inset(40px)")]
    [InlineData("circle(0)")]
    [InlineData("polygon(0 0, 100% 100%)")]
    [InlineData("xywh(0 0 0 10px)")]
    [InlineData("rect(50% auto 40% auto)")]
    public void ClipShape_WithoutAreaRendersNothing(string value)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + $"<rect x=\"20\" y=\"20\" width=\"60\" height=\"60\" style=\"clip-path: {value}\"/></svg>");

        Assert.Empty(document.Nodes.ToArray());
    }

    // Chromium: the fill box of a horizontal line has no height, so a shape on it clips everything; the default stroke box
    // keeps the line.
    [Fact]
    public void ClipShape_LineUsesItsStrokeBox()
    {
        using Image<Rgba32> image = Render(
            """
            <line x1="10" y1="30" x2="90" y2="30" stroke="green" stroke-width="20" style="clip-path: inset(0)"/>
            <line x1="10" y1="70" x2="90" y2="70" stroke="green" stroke-width="20" style="clip-path: inset(0) fill-box"/>
            """);

        Assert.Equal(Green, image[50, 30]);
        Assert.Equal(default, image[50, 70]);
    }

    // A shape that does not depend on the fill rule clips with a geometry clip and needs no layer; evenodd on a polygon
    // needs coverage, which draws into an alpha mask.
    [Fact]
    public void ClipShape_UsesAGeometryClipUnlessTheRuleMatters()
    {
        SvgDocument geometry = SvgDocument.Parse(SvgOpen + "<rect width=\"50\" height=\"50\" style=\"clip-path: polygon(0 0, 100% 0, 0 100%)\"/></svg>");
        Assert.Equal(
            [SvgRenderNodeKind.BeginClip, SvgRenderNodeKind.Draw, SvgRenderNodeKind.EndClip],
            geometry.Nodes.ToArray().Select(n => n.Kind));

        SvgDocument coverage = SvgDocument.Parse(
            SvgOpen + "<rect width=\"50\" height=\"50\" style=\"clip-path: polygon(evenodd, 0 0, 100% 0, 0 100%)\"/></svg>");
        Assert.Equal(
            [
                SvgRenderNodeKind.BeginLayer, SvgRenderNodeKind.Draw, SvgRenderNodeKind.BeginMask, SvgRenderNodeKind.Draw,
                SvgRenderNodeKind.EndMask, SvgRenderNodeKind.EndLayer
            ],
            coverage.Nodes.ToArray().Select(n => n.Kind));
    }

    // The fill rule of polygon() and path(): the inner pentagon of a star is a hole with evenodd only.
    [Theory]
    [InlineData("polygon(evenodd, 50% 0, 79% 90%, 2% 35%, 98% 35%, 21% 90%)", false)]
    [InlineData("polygon(50% 0, 79% 90%, 2% 35%, 98% 35%, 21% 90%)", true)]
    [InlineData("path(evenodd, 'M 50 0 L 79 90 L 2 35 L 98 35 L 21 90 Z')", false)]
    [InlineData("path('M 50 0 L 79 90 L 2 35 L 98 35 L 21 90 Z')", true)]
    public void ClipShape_AppliesTheFillRule(string value, bool center)
    {
        using Image<Rgba32> image = Render($"<rect width=\"100\" height=\"100\" fill=\"green\" style=\"clip-path: {value}\"/>");

        Assert.Equal(center ? Green : default, image[50, 50]);
        Assert.Equal(Green, image[50, 20]);
    }

    // Chromium: a shape on a child of a clipPath refers to the box of the child; a shape on the clipPath element refers to
    // the box of the clipped element, in its user space.
    [Fact]
    public void ClipShape_OnClipPathAndItsChildren()
    {
        using Image<Rgba32> image = Render(
            """
            <clipPath id="child"><rect x="0" y="0" width="40" height="40" style="clip-path: circle()"/></clipPath>
            <clipPath id="self" style="clip-path: circle(closest-side)"><rect width="100" height="100"/></clipPath>
            <rect width="100" height="50" fill="green" clip-path="url(#child)"/>
            <rect x="40" y="50" width="60" height="50" fill="green" clip-path="url(#self)"/>
            """);

        // The child circle: center (20, 20), radius 20. The self circle: center (70, 75), radius 25.
        Assert.Equal(Green, image[20, 20]);
        Assert.Equal(default, image[2, 2]);
        Assert.Equal(default, image[60, 20]);
        Assert.Equal(Green, image[70, 75]);
        Assert.Equal(default, image[42, 52]);
    }

    // Chromium: the outermost svg element has a CSS layout box, so a basic shape on it refers to its viewport, outside
    // the view box transform.
    [Fact]
    public void ClipShape_RootUsesTheViewport()
    {
        SvgDocument document = SvgDocument.Parse(
            """
            <svg xmlns="http://www.w3.org/2000/svg" width="200" height="100" viewBox="0 0 400 200" style="clip-path: circle(closest-side)">
              <rect width="400" height="200" fill="green"/>
            </svg>
            """);
        using Image<Rgba32> image = new(200, 100);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new PointF(0, 0))));

        // The circle of the viewport: center (100, 50), radius 50.
        Assert.Equal(Green, image[100, 50]);
        Assert.Equal(Green, image[55, 50]);
        Assert.Equal(default, image[45, 50]);
        Assert.Equal(default, image[10, 10]);
    }

    // SVG 2 and Chromium: pathLength scales the dash pattern and the dash offset by the computed length divided by
    // pathLength. The path is 180 long, so pathLength 90 doubles the pattern: dashes from 10 to 30, 50 to 70, ...
    [Theory]
    [InlineData("pathLength=\"90\"", "", new[] { 15, 25, 55 }, new[] { 35, 45 })]
    [InlineData("pathLength=\"90\"", "stroke-dashoffset=\"5\"", new[] { 15, 45, 55 }, new[] { 25, 65 })]
    [InlineData("pathLength=\"-5\"", "", new[] { 15, 35 }, new[] { 25, 45 })]
    [InlineData("pathLength=\"0\"", "", new[] { 15, 25, 35, 45 }, new int[0])]
    [InlineData("pathLength=\"1e-30\"", "", new[] { 15, 25, 35, 45 }, new int[0])]
    public void PathLength_ScalesTheDashes(string pathLength, string offset, int[] dashes, int[] gaps)
    {
        using Image<Rgba32> image = Render(
            $"<path d=\"M 10 50 H 190\" stroke=\"green\" stroke-width=\"6\" stroke-dasharray=\"10 10\" {pathLength} {offset}/>",
            200);

        foreach (int x in dashes)
        {
            Assert.Equal(Green, image[x, 50]);
        }

        foreach (int x in gaps)
        {
            Assert.Equal(default, image[x, 50]);
        }
    }

    // SVG 2: a pathLength of zero scales by infinity, but a zero dash stays zero; so a pattern that starts with a zero
    // dash draws nothing with butt caps. The Chromium oracle draws a solid stroke here instead.
    [Fact]
    public void PathLength_ZeroKeepsAZeroDash()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "<path d=\"M 10 50 H 90\" stroke=\"green\" stroke-width=\"6\" stroke-dasharray=\"0 10\" pathLength=\"0\"/></svg>");
        using Image<Rgba32> image = new(100, 100);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new PointF(0, 0))));

        Assert.Equal(default, image[50, 50]);
    }

    // The length that pathLength divides is the length that the dasher walks: a circle of radius 80 with pathLength 100
    // and the pattern 25 75 draws one quarter, which ends at the bottom of the circle, as in the Chromium oracle.
    [Fact]
    public void PathLength_MeasuresCurvesAsTheDasherDoes()
    {
        using Image<Rgba32> image = Render(
            "<circle cx=\"100\" cy=\"100\" r=\"80\" fill=\"none\" stroke=\"green\" stroke-width=\"4\" pathLength=\"100\" stroke-dasharray=\"25 75\"/>",
            200);

        Assert.Equal(Green, image[178, 110]);
        Assert.Equal(Green, image[110, 179]);
        Assert.Equal(default, image[90, 179]);
        Assert.Equal(default, image[22, 100]);
    }

    // SVG 2 and Chromium: on a textPath, pathLength of the referenced path scales a startOffset that is a length; a
    // percentage refers to the computed length.
    [Theory]
    [InlineData("pathLength=\"90\"", "20", 50F)]
    [InlineData("", "20", 30F)]
    [InlineData("pathLength=\"90\"", "50%", 100F)]
    [InlineData("pathLength=\"360\"", "20", 20F)]
    public void PathLength_ScalesTheStartOffsetOfTextPath(string pathLength, string startOffset, float expected)
    {
        SvgTextLayout layout = Layout(
            $"<path id=\"p\" d=\"M 10 50 H 190\" {pathLength}/><text id=\"t\"><textPath href=\"#p\" startOffset=\"{startOffset}\">I</textPath></text>",
            "t");

        Assert.Equal(expected, layout.Clusters[0].X, 2);
    }

    // SVG 2: a pathLength of zero makes a non-zero startOffset infinite, which moves the text off the path, as the
    // Chromium oracle renders it.
    [Fact]
    public void PathLength_ZeroMovesTextOffThePath()
    {
        SvgTextLayout layout = Layout(
            "<path id=\"p\" d=\"M 10 50 H 190\" pathLength=\"0\"/><text id=\"t\"><textPath href=\"#p\" startOffset=\"20\">I</textPath></text>",
            "t");

        Assert.True(layout.Clusters[0].Skipped);
    }

    // CSS Fonts Level 4 resolution: the variant properties, then letter-spacing, then font-feature-settings. Open Sans
    // ligates "fi"; the ligature breaks for liga off, and letter-spacing breaks it unless font-feature-settings turns liga
    // back on.
    [Theory]
    [InlineData("", 1)]
    [InlineData("font-variant-ligatures: none", 2)]
    [InlineData("font-variant-ligatures: no-common-ligatures", 2)]
    [InlineData("font-variant: no-common-ligatures", 2)]
    [InlineData("font-feature-settings: 'liga' 0", 2)]
    [InlineData("font-variant-ligatures: none; font-feature-settings: 'liga' 1", 1)]
    [InlineData("letter-spacing: 1px", 2)]
    [InlineData("letter-spacing: 1px; font-feature-settings: 'liga' 1", 1)]
    public void FontFeatures_ResolveLigatures(string style, int clusters)
    {
        SvgTextLayout layout = Layout($"<text id=\"t\" font-family=\"Open Sans\" style=\"{style}\">fi</text>", "t");

        Assert.Equal(clusters, layout.Clusters.Length);
    }

    // The variant properties and font-feature-settings reach the shaper: Inter has tnum and zero, so the digits change.
    // A later font-feature-settings entry overrides the variant property, and the font shorthand resets both.
    [Theory]
    [InlineData("font-variant-numeric: tabular-nums", "font-feature-settings: 'tnum'", true)]
    [InlineData("font-variant: slashed-zero", "font-feature-settings: 'zero' 1", true)]
    [InlineData("font-variant-numeric: tabular-nums; font-feature-settings: 'tnum' 0", "", true)]
    [InlineData("font-feature-settings: 'tnum'; font: 300 18px Inter", "font: 300 18px Inter", true)]
    [InlineData("font-variant-numeric: tabular-nums", "", false)]
    public void FontFeatures_ReachTheShaper(string first, string second, bool same)
    {
        ushort[] a = GlyphIds($"<text id=\"t\" font-family=\"Inter\" font-weight=\"300\" style=\"{first}\">1110</text>");
        ushort[] b = GlyphIds($"<text id=\"t\" font-family=\"Inter\" font-weight=\"300\" style=\"{second}\">1110</text>");
        float wa = Advance($"<text id=\"t\" font-family=\"Inter\" font-weight=\"300\" style=\"{first}\">1110</text>");
        float wb = Advance($"<text id=\"t\" font-family=\"Inter\" font-weight=\"300\" style=\"{second}\">1110</text>");

        Assert.Equal(same, a.SequenceEqual(b) && Math.Abs(wa - wb) < 0.01F);
    }

    // font-feature-settings turns kerning off and back on in both directions over font-kerning.
    [Fact]
    public void FontFeatures_KernSettingOverridesFontKerning()
    {
        float kerned = Advance("<text id=\"t\" font-family=\"Inter\" font-weight=\"300\">AVAV</text>");
        float off = Advance("<text id=\"t\" font-family=\"Inter\" font-weight=\"300\" style=\"font-feature-settings: 'kern' 0\">AVAV</text>");
        float on = Advance(
            "<text id=\"t\" font-family=\"Inter\" font-weight=\"300\" style=\"font-kerning: none; font-feature-settings: 'kern' 1\">AVAV</text>");

        Assert.True(off > kerned + 1F, $"{off} <= {kerned}");
        Assert.Equal(kerned, on, 3);
    }

    // font-variant-east-asian reaches the shaper: Noto Sans KR has fwid, which gives Latin letters full-width forms.
    [Fact]
    public void FontFeatures_EastAsianWidths()
    {
        float normal = Advance("<text id=\"t\" font-family=\"Noto Sans KR\">Abc</text>");
        float full = Advance("<text id=\"t\" font-family=\"Noto Sans KR\" style=\"font-variant-east-asian: full-width\">Abc</text>");

        // Full-width forms advance by one em each.
        Assert.Equal(60F, full, 2);
        Assert.True(normal < full);
    }

    // The white space processing reads white-space-collapse, the cascade winner of the shorthand and the longhand. As
    // in Chromium, preserve-breaks collapses and break-spaces preserves in one-line SVG text, and a value on an ancestor
    // outside the text does not reach it.
    [Theory]
    [InlineData("<text id=\"t\">[A   B]</text>", "[A B]")]
    [InlineData("<text id=\"t\" style=\"white-space-collapse: preserve\">[A   B]</text>", "[A   B]")]
    [InlineData("<text id=\"t\" style=\"white-space-collapse: break-spaces\">[A   B]</text>", "[A   B]")]
    [InlineData("<text id=\"t\" style=\"white-space-collapse: preserve-breaks\">[A   B]</text>", "[A B]")]
    [InlineData("<text id=\"t\" style=\"white-space: preserve nowrap\">[A   B]</text>", "[A   B]")]
    [InlineData("<text id=\"t\" style=\"white-space: pre; white-space-collapse: collapse\">[A   B]</text>", "[A B]")]
    [InlineData("<text id=\"t\" style=\"white-space-collapse: collapse; white-space: pre\">[A   B]</text>", "[A   B]")]
    [InlineData("<text id=\"t\" xml:space=\"preserve\">[A   B]</text>", "[A   B]")]
    [InlineData("<g style=\"white-space: pre\"><text id=\"t\">[A   B]</text></g>", "[A B]")]
    [InlineData("<g style=\"white-space: pre\"><text id=\"t\" style=\"white-space: inherit\">[A   B]</text></g>", "[A   B]")]
    [InlineData("<text id=\"t\" style=\"white-space: pre\">[A   B]<tspan style=\"white-space-collapse: collapse\">[C   D]</tspan></text>", "[A   B][C D]")]
    public void WhiteSpace_ReadsTheCollapseMode(string content, string expected)
    {
        SvgTextContent text = BuildContent(content, "t");

        Assert.Equal(expected, text.Text);
    }

    // Selectors Level 4: :lang() takes a list of identifiers and strings, matched by extended filtering. The Chromium
    // oracle matches the Level 3 form only; the selectors of that form agree.
    [Theory]
    [InlineData("fr", "fr", true)]
    [InlineData("fr-CA", "fr", true)]
    [InlineData("FR-ca", "fr-CA", true)]
    [InlineData("fr", "fr-CA", false)]
    [InlineData("fr_CA", "fr", false)]
    [InlineData("de-Latn-DE", "de-DE", true)]
    [InlineData("de-Latn-DE", "\"de-*-DE\"", true)]
    [InlineData("de-CH", "\\*-CH", true)]
    [InlineData("de-CH", "\"*-CH\"", true)]
    [InlineData("de-x-DE", "de-DE", false)]
    [InlineData("ja", "en, ja", true)]
    [InlineData("ja", "\"*\"", true)]
    [InlineData("", "\"*\"", false)]
    [InlineData("", "\"\"", true)]
    [InlineData("en", "\"\"", false)]
    public void LangSelector_MatchesByExtendedFiltering(string language, string ranges, bool matches)
    {
        using Image<Rgba32> image = Render(
            $"<style>rect {{ fill: red; }} rect:lang({ranges}) {{ fill: green; }}</style><rect width=\"10\" height=\"10\" lang=\"{language}\"/>");

        Assert.Equal(matches ? Green : new Rgba32(255, 0, 0, 255), image[5, 5]);
    }

    // xml:lang wins over lang on one element and both inherit; an element without a language matches no range.
    [Fact]
    public void LangSelector_ReadsXmlLangLangAndAncestors()
    {
        using Image<Rgba32> image = Render(
            """
            <style>rect { fill: red; } rect:lang(fr) { fill: green; }</style>
            <rect x="0" width="10" height="10" xml:lang="fr" lang="en"/>
            <g lang="fr"><rect x="20" width="10" height="10"/></g>
            <rect x="40" width="10" height="10"/>
            """);

        Assert.Equal(Green, image[5, 5]);
        Assert.Equal(Green, image[25, 5]);
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[45, 5]);
    }

    // SVG 2: systemLanguage tests the languages of the user (SvgLoadOptions.Languages), not lang or xml:lang; the
    // Chromium oracle also ignores them and uses its accept languages.
    [Fact]
    public void SystemLanguage_IgnoresLangAttributes()
    {
        SvgLoadOptions options = new() { Languages = ["de"] };
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "<g lang=\"ru\"><rect width=\"10\" height=\"10\" systemLanguage=\"ru\"/>"
                + "<rect width=\"10\" height=\"10\" systemLanguage=\"de\"/></g></svg>",
            options);

        Assert.Single(document.Nodes.ToArray());
    }

    private static Image<Rgba32> Render(string content, int size = 100)
    {
        SvgDocument document = SvgDocument.Parse(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}\" height=\"{size}\">{content}</svg>");
        Image<Rgba32> image = new(size, size);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new PointF(0, 0))));
        return image;
    }

    private static ushort[] GlyphIds(string content)
    {
        SvgTextLayout layout = Layout(content, "t");
        return [.. layout.Glyphs.ToArray().Select(g => g.GlyphId)];
    }

    private static float Advance(string content)
    {
        SvgTextLayout layout = Layout(content, "t");
        float advance = 0F;
        foreach (SvgTextCluster cluster in layout.Clusters)
        {
            advance += cluster.Advance;
        }

        return advance;
    }

    private static SvgTextLayout Layout(string content, string id)
    {
        SvgLoadOptions options = SvgSvg2RenderTests.TextOptions;
        SvgElementTree tree = Read(content);
        SvgResolver resolver = new(tree, options);
        Assert.True(tree.TryGetElementById(id, out int index));
        SvgComputedStyle style = resolver.ComputeDocumentStyle(index);
        return SvgTextLayout.Create(resolver, index, in style, new SizeF(200F, 200F), new SvgFontMatcher(options));
    }

    private static SvgTextContent BuildContent(string content, string id)
    {
        SvgElementTree tree = Read(content);
        SvgResolver resolver = new(tree, SvgSvg2RenderTests.TextOptions);
        Assert.True(tree.TryGetElementById(id, out int index));
        SvgComputedStyle style = resolver.ComputeDocumentStyle(index);
        return SvgTextCharacters.Build(resolver, index, in style, new SizeF(200F, 200F));
    }

    private static SvgElementTree Read(string content)
        => SvgReader.Read(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\" font-size=\"20\">" + content + "</svg>",
            SvgReaderOptions.Default);
}
