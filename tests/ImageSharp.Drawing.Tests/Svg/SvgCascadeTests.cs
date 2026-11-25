// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests the style sheet parser, selector matching, and the cascade through <see cref="SvgReader"/>. Each expectation
/// follows CSS Cascade Level 4, Selectors Level 3, CSS Syntax Level 3, and the SVG 2 rule that presentation attributes
/// precede every author style sheet rule.
/// </summary>
public class SvgCascadeTests
{
    private static readonly Rgba32 Red = new(255, 0, 0, 255);
    private static readonly Rgba32 Green = new(0, 128, 0, 255);
    private static readonly Rgba32 Blue = new(0, 0, 255, 255);
    private static readonly Rgba32 Yellow = new(255, 255, 0, 255);

    [Fact]
    public void Cascade_AppliesTheFiveLevelsInOrder()
    {
        // Each property is decided at a different level.
        SvgElementTree tree = Read(
            """
            <style>
              rect { fill: green; stroke: green; stroke-opacity: 0.1 !important; opacity: 0.1 !important }
            </style>
            <rect id="r" fill="red" stroke="red" stroke-width="3" stroke-opacity="0.9" opacity="0.9"
                  style="stroke: yellow; stroke-opacity: 0.5; opacity: 0.2 !important"/>
            """);

        ref readonly SvgSpecifiedStyle style = ref StyleOf(tree, "r");

        // 1. Presentation attribute, not overridden.
        Assert.Equal(new SvgLength(3, SvgLengthUnit.Number), style.StrokeWidth);

        // 2. A normal rule overrides a presentation attribute.
        Assert.Equal(Green, style.Fill.Color.ToPixel<Rgba32>());

        // 3. The normal style attribute overrides a normal rule.
        Assert.Equal(Yellow, style.Stroke.Color.ToPixel<Rgba32>());

        // 4. An important rule overrides the normal style attribute.
        Assert.Equal(0.1F, style.StrokeOpacity);

        // 5. The important style attribute overrides an important rule.
        Assert.Equal(0.2F, style.Opacity);
    }

    [Fact]
    public void Cascade_UniversalRuleOverridesPresentationAttribute()
    {
        SvgElementTree tree = Read("""<style>* { fill: blue }</style><rect id="r" fill="red"/>""");

        Assert.Equal(Blue, StyleOf(tree, "r").Fill.Color.ToPixel<Rgba32>());
    }

    [Theory]
    [InlineData("rect { fill: red } .c { fill: blue }")]
    [InlineData(".c { fill: blue } rect { fill: red }")]
    [InlineData("#r { fill: blue } .c.d { fill: red }")]
    [InlineData(".c.d { fill: red } #r { fill: blue }")]
    [InlineData("svg rect { fill: red } g .c { fill: blue }")]
    [InlineData(".c { fill: red } svg g rect.c#r { fill: blue }")]
    public void Cascade_HigherSpecificityWins(string css)
    {
        SvgElementTree tree = Read($"""<style>{css}</style><g><rect id="r" class="c d"/></g>""");

        Assert.Equal(Blue, StyleOf(tree, "r").Fill.Color.ToPixel<Rgba32>());
    }

    [Theory]
    [InlineData(".a { fill: red } .b { fill: blue }", "a b")]
    [InlineData(".b { fill: red } .a { fill: blue }", "a b")]
    [InlineData(".b { fill: red } .a { fill: blue }", "b a")]
    public void Cascade_LaterRuleWinsForEqualSpecificity(string css, string classes)
    {
        SvgElementTree tree = Read($"""<style>{css}</style><rect id="r" class="{classes}"/>""");

        Assert.Equal(Blue, StyleOf(tree, "r").Fill.Color.ToPixel<Rgba32>());
    }

    [Fact]
    public void Cascade_OrderContinuesAcrossStyleElementsAndAppliesToEarlierElements()
    {
        SvgElementTree tree = Read(
            """
            <style>rect { fill: red }</style>
            <rect id="r"/>
            <style>rect { fill: blue }</style>
            """);

        Assert.Equal(Blue, StyleOf(tree, "r").Fill.Color.ToPixel<Rgba32>());
    }

    [Fact]
    public void Cascade_ImportantRuleSpecificityOrdersImportantDeclarations()
    {
        SvgElementTree tree = Read(
            """
            <style>#r { fill: blue !important } rect { fill: red !important }</style>
            <rect id="r" style="fill: green"/>
            """);

        Assert.Equal(Blue, StyleOf(tree, "r").Fill.Color.ToPixel<Rgba32>());
    }

    [Theory]
    [InlineData("fill: red !important; fill: blue", 255, 0, 0)]
    [InlineData("fill: red; fill: blue", 0, 0, 255)]
    [InlineData("fill: blue; fill: bogus", 0, 0, 255)]
    [InlineData("fill: blue ! IMPORTANT ; fill: red", 0, 0, 255)]
    [InlineData("FILL: blue", 0, 0, 255)]
    [InlineData("fill:blue;;;", 0, 0, 255)]
    [InlineData("unknown: red; fill: blue", 0, 0, 255)]
    [InlineData("fill red; fill: blue", 0, 0, 255)]
    [InlineData("fill: /* red */ blue", 0, 0, 255)]
    [InlineData("fill: url(#a) blue", 0, 0, 255)]
    public void InlineStyle_DeclarationsFollowCssRules(string declarations, byte r, byte g, byte b)
    {
        SvgElementTree tree = Read($"""<rect id="r" style="{declarations}"/>""");
        SvgPaint fill = StyleOf(tree, "r").Fill;

        Assert.Equal(new Rgba32(r, g, b, 255), fill.Color.ToPixel<Rgba32>());
    }

    [Fact]
    public void PresentationAttribute_WithImportantIsInvalid()
    {
        SvgElementTree tree = Read("""<rect id="r" fill="red !important"/>""");

        Assert.False(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Fill));
    }

    [Theory]
    [InlineData("rect { fill: blue }")]
    [InlineData("svg rect { fill: blue }")]
    [InlineData("svg g rect { fill: blue }")]
    [InlineData("g > rect { fill: blue }")]
    [InlineData("svg > g > g > rect { fill: blue }")]
    [InlineData("svg > * > * > rect { fill: blue }")]
    [InlineData("svg rect.c { fill: blue }")]
    [InlineData("#g1 .c { fill: blue }")]
    [InlineData("*.c#r { fill: blue }")]
    [InlineData("circle, rect { fill: blue }")]
    [InlineData("svg  >  g   rect { fill: blue }")]
    [InlineData("g g rect { fill: blue }")]
    [InlineData("svg g g rect { fill: blue }")]
    public void Selectors_MatchTheElement(string css)
    {
        SvgElementTree tree = Read($"""<style>{css}</style><g id="g1"><g><rect id="r" class="c"/></g></g>""");

        Assert.Equal(Blue, StyleOf(tree, "r").Fill.Color.ToPixel<Rgba32>());
    }

    [Theory]
    [InlineData("svg > rect { fill: blue }")]
    [InlineData("svg > g > rect { fill: blue }")]
    [InlineData("svg > * > rect { fill: blue }")]
    [InlineData("g > g > g rect { fill: blue }")]
    [InlineData("rect g { fill: blue }")]
    [InlineData("RECT { fill: blue }")]
    [InlineData(".C { fill: blue }")]
    [InlineData(".c.d { fill: blue }")]
    [InlineData("#R { fill: blue }")]
    [InlineData("circle { fill: blue }")]
    [InlineData("g1 rect { fill: blue }")]
    public void Selectors_DoNotMatchTheElement(string css)
    {
        SvgElementTree tree = Read($"""<style>{css}</style><g id="g1"><g><rect id="r" class="c"/></g></g>""");

        Assert.False(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Fill));
    }

    // A selector list with any invalid or unsupported selector drops the whole rule.
    [Theory]
    [InlineData("rect, a:hover { fill: red }")]
    [InlineData("rect[x] { fill: red }")]
    [InlineData("rect + rect { fill: red }")]
    [InlineData("rect ~ rect { fill: red }")]
    [InlineData("svg|rect { fill: red }")]
    [InlineData("rect, { fill: red }")]
    [InlineData(", rect { fill: red }")]
    [InlineData("> rect { fill: red }")]
    [InlineData("g > { fill: red }")]
    [InlineData("g > > rect { fill: red }")]
    [InlineData("rect* { fill: red }")]
    [InlineData(". c { fill: red }")]
    [InlineData("#1 { fill: red }")]
    [InlineData("rect::before { fill: red }")]
    [InlineData(":not(circle) { fill: red }")]
    public void Selectors_InvalidListDropsTheRule(string css)
    {
        SvgElementTree tree = Read($$"""<style>{{css}} rect { stroke: blue }</style><rect id="r" class="c"/>""");

        ref readonly SvgSpecifiedStyle style = ref StyleOf(tree, "r");
        Assert.False(style.IsSpecified(SvgPropertyId.Fill));

        // The parser stays in sync: the next rule still applies.
        Assert.Equal(Blue, style.Stroke.Color.ToPixel<Rgba32>());
    }

    [Theory]
    [InlineData("@media print { rect { fill: red } } rect { fill: blue }")]
    [InlineData("@import url(other.css); rect { fill: blue }")]
    [InlineData("@import 'other.css'; rect { fill: blue }")]
    [InlineData("@font-face { font-family: x; src: url(a.woff) } rect { fill: blue }")]
    [InlineData("@media screen { @supports (display: grid) { rect { fill: red } } } rect { fill: blue }")]
    [InlineData("/* rect { fill: red } */ rect { fill: blue }")]
    [InlineData("<!-- rect { fill: blue } -->")]
    [InlineData("rect { font-family: \"}\"; fill: blue }")]
    [InlineData("rect { x: [ } ]; fill: blue }")]
    [InlineData("rect { x: fn( } ); fill: blue }")]
    [InlineData("rect { fill: blue")]
    [InlineData("rect { fill: blue } @unterminated { rect { fill: red }")]
    [InlineData("r\\65 ct { fill: blue }")]
    public void StyleSheet_SkipsWhatItDoesNotUseAndStaysInSync(string css)
    {
        SvgElementTree tree = Read($"<style><![CDATA[{css}]]></style><rect id=\"r\"/>");

        Assert.Equal(Blue, StyleOf(tree, "r").Fill.Color.ToPixel<Rgba32>());
    }

    [Fact]
    public void StyleSheet_StrayClosingBraceStartsARuleThatSwallowsTheNextOne()
    {
        // CSS Syntax 5.4.1: at the top level a "}" starts a qualified rule. Its prelude "} rect" is not a valid selector,
        // so the rule with the blue fill is dropped.
        SvgElementTree tree = Read("""<style>rect { fill: red } } rect { fill: blue }</style><rect id="r"/>""");

        Assert.Equal(Red, StyleOf(tree, "r").Fill.Color.ToPixel<Rgba32>());
    }

    [Fact]
    public void StyleSheet_ReadsTextAndCdataSections()
    {
        SvgElementTree tree = Read(
            """
            <style>
              rect { fill: blue }
              <![CDATA[ circle > x, rect { stroke: blue } ]]>
            </style>
            <rect id="r"/>
            """);

        ref readonly SvgSpecifiedStyle style = ref StyleOf(tree, "r");
        Assert.Equal(Blue, style.Fill.Color.ToPixel<Rgba32>());
        Assert.Equal(Blue, style.Stroke.Color.ToPixel<Rgba32>());
    }

    [Theory]
    [InlineData("text/css")]
    [InlineData("TEXT/CSS")]
    [InlineData("")]
    public void StyleSheet_AppliesForCssType(string type)
    {
        SvgElementTree tree = Read($$"""<style type="{{type}}">rect { fill: blue }</style><rect id="r"/>""");

        Assert.Equal(Blue, StyleOf(tree, "r").Fill.Color.ToPixel<Rgba32>());
    }

    [Fact]
    public void StyleSheet_IgnoresOtherTypes()
    {
        SvgElementTree tree = Read("""<style type="text/less">rect { fill: blue }</style><rect id="r"/>""");

        Assert.False(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Fill));
        Assert.Equal(0, tree.StyleRuleCount);
    }

    [Fact]
    public void StyleSheet_SelectorListCreatesOneRulePerSelector()
    {
        SvgElementTree tree = Read("<style>rect, circle, #a .b g { fill: blue }</style>");

        Assert.Equal(3, tree.StyleRuleCount);
    }

    [Fact]
    public void StyleSheet_SpecificityCountsIdsClassesAndTypes()
    {
        SvgStyleSheet sheet = new();
        SvgCssParser.ParseStyleSheet("#a .b c.d > * { fill: red }", sheet, new SvgPropertyParser(), new SvgStringPool());
        sheet.Complete();

        // Selectors Level 3, section 9: one id, two classes, and one type; the universal selector counts nothing.
        Assert.Equal((1 << 16) | (2 << 8) | 1, sheet.Rules[0].Specificity);
    }

    [Fact]
    public void Cascade_WithoutRulesMergesOwnDeclarations()
    {
        SvgElementTree tree = Read("""<rect id="r" fill="red" stroke="red" style="fill: blue"/><rect id="empty"/>""");

        ref readonly SvgSpecifiedStyle style = ref StyleOf(tree, "r");
        Assert.Equal(0, tree.StyleRuleCount);
        Assert.Equal(Blue, style.Fill.Color.ToPixel<Rgba32>());
        Assert.Equal(Red, style.Stroke.Color.ToPixel<Rgba32>());
        Assert.True(StyleOf(tree, "empty").IsEmpty);
    }

    [Fact]
    public void Cascade_DoesNotApplyInheritance()
    {
        SvgElementTree tree = Read("""<style>g { fill: blue }</style><g fill="red"><rect id="r"/></g>""");

        Assert.False(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Fill));
    }

    [Fact]
    public void Cascade_KeepsInheritKeywords()
    {
        SvgElementTree tree = Read("""<style>rect { fill: inherit }</style><rect id="r" fill="red"/>""");

        ref readonly SvgSpecifiedStyle style = ref StyleOf(tree, "r");
        Assert.True(style.IsInherit(SvgPropertyId.Fill));
        Assert.False(style.HasValue(SvgPropertyId.Fill));
    }

    [Fact]
    public void Selectors_MatchUnknownElementsByType()
    {
        SvgElementTree tree = Read("""<style>blah { fill: blue }</style><blah id="u"/>""");

        Assert.True(tree.TryGetElementById("u", out int index));
        Assert.Equal(SvgElementKind.Unknown, tree[index].Kind);
        Assert.Equal(Blue, tree.GetStyle(index).Fill.Color.ToPixel<Rgba32>());
    }

    [Fact]
    public void Selectors_DeepDescendantChainsFinish()
    {
        // A selector that cannot match, over a deep tree, must not backtrack exponentially.
        string selector = string.Join(" ", Enumerable.Repeat("g", 40)) + " x";
        string open = string.Concat(Enumerable.Repeat("<g>", 200));
        string close = string.Concat(Enumerable.Repeat("</g>", 200));
        SvgElementTree tree = Read($"<style>{selector} {{ fill: red }}</style>{open}<rect id=\"r\"/>{close}");

        Assert.False(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Fill));
    }

    // The marker shorthand sets marker-start, marker-mid, and marker-end, from a style sheet and from a style attribute.
    [Fact]
    public void Cascade_MarkerShorthandSetsTheThreeLonghands()
    {
        SvgElementTree tree = Read(
            """
            <style>#a { marker: url(#m) } #b { MARKER: url(#m) !important }</style>
            <path id="a" marker-end="url(#x)"/>
            <path id="b" style="marker-start: url(#n)"/>
            <path id="c" style="marker: url(#n); marker-mid: none"/>
            """);

        SvgExtendedStyle a = StyleOf(tree, "a").Extended!;
        Assert.Equal("m", a.MarkerStart.Id);
        Assert.Equal("m", a.MarkerMid.Id);

        // A rule overrides the presentation attribute.
        Assert.Equal("m", a.MarkerEnd.Id);

        // An important rule overrides the normal style attribute.
        Assert.Equal("m", StyleOf(tree, "b").Extended!.MarkerStart.Id);

        SvgExtendedStyle c = StyleOf(tree, "c").Extended!;
        Assert.Equal("n", c.MarkerStart.Id);
        Assert.True(c.MarkerMid.IsNone);
        Assert.Equal("n", c.MarkerEnd.Id);
    }

    // One rule styles many elements, so the cascade of one element must not change the values that another element sees.
    [Fact]
    public void Cascade_SharedRuleExtensionsStayUnchanged()
    {
        SvgElementTree tree = Read(
            """
            <style>rect { clip-path: url(#a); mask: url(#b) }</style>
            <rect id="first" style="mask: url(#c)"/>
            <rect id="second"/>
            """);

        Assert.Equal("c", StyleOf(tree, "first").Extended!.Mask.Id);
        Assert.Equal("a", StyleOf(tree, "first").Extended!.ClipPath.Id);
        Assert.Equal("b", StyleOf(tree, "second").Extended!.Mask.Id);
        Assert.Equal("a", StyleOf(tree, "second").Extended!.ClipPath.Id);
    }

    // SVG 2: the transform attribute is the presentation attribute of the transform property, so CSS wins over it.
    [Fact]
    public void Cascade_CssTransformReplacesTheTransformAttribute()
    {
        SvgElementTree tree = Read(
            """
            <style>#a { transform: scale(2) } #b { transform: none }</style>
            <g id="g" transform="translate(5)">
              <rect id="a" transform="translate(1)"/>
              <rect id="b" transform="translate(1)"/>
              <rect id="c" style="transform: rotate(90deg)"/>
              <rect id="d" style="transform: inherit"/>
              <rect id="e" style="transform: rotate(90)" transform="translate(3)"/>
            </g>
            """);

        Assert.True(tree.TryGetTransform(Index(tree, "a"), out Matrix3x2 a));
        Assert.Equal(Matrix3x2.CreateScale(2F), a);
        Assert.False(tree.TryGetTransform(Index(tree, "b"), out _));
        Assert.True(tree.TryGetTransform(Index(tree, "c"), out Matrix3x2 c));
        Assert.Equal(1F, Vector2.Transform(Vector2.UnitX, c).Y, 5);
        Assert.True(tree.TryGetTransform(Index(tree, "d"), out Matrix3x2 d));
        Assert.Equal(Matrix3x2.CreateTranslation(5, 0), d);

        // An invalid CSS value is dropped, so the attribute applies.
        Assert.True(tree.TryGetTransform(Index(tree, "e"), out Matrix3x2 e));
        Assert.Equal(Matrix3x2.CreateTranslation(3, 0), e);
    }

    private static ref readonly SvgSpecifiedStyle StyleOf(SvgElementTree tree, string id)
    {
        Assert.True(tree.TryGetElementById(id, out int index));
        return ref tree.GetStyle(index);
    }

    private static SvgElementTree Read(string content)
        => SvgReader.Read($"<svg xmlns=\"http://www.w3.org/2000/svg\">{content}</svg>", SvgReaderOptions.Default);

    private static int Index(SvgElementTree tree, string id)
    {
        Assert.True(tree.TryGetElementById(id, out int index));
        return index;
    }
}
