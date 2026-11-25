// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests the full selector set through <see cref="SvgReader"/>: combinators, attribute selectors, the structural
/// pseudo-classes, the logical pseudo-classes, specificity, invalid selectors, and the cost bounds of matching. The
/// expectations follow Selectors Level 4 and CSS Syntax Level 3; where those leave the outcome open, they follow Chromium,
/// checked with tests/SvgTools/render-svg.js.
/// </summary>
public class SvgSelectorTests
{
    // Children of the root: the style element, then g#p. Children of g#p: a, b, c, d, e (positions 1 to 5).
    private const string Structure =
        """
        <g id="p" class="grp">
          <rect id="a" class="x" fill="red" data-k="one two"/>
          <circle id="b" lang="en-US"/>
          <rect id="c" class="x y" stroke="Blue"/>
          <rect id="d"/>
          <g id="e"><rect id="f"/></g>
        </g>
        """;

    private static readonly string[] Ids = ["p", "a", "b", "c", "d", "e", "f"];

    [Theory]
    [InlineData("rect + circle", "b")]
    [InlineData("rect + rect", "d")]
    [InlineData("circle + rect", "c")]
    [InlineData("rect ~ rect", "c d")]
    [InlineData("circle ~ *", "c d e")]
    [InlineData("#a ~ g rect", "f")]
    [InlineData("#a + circle + rect", "c")]
    [InlineData("#a+circle+rect", "c")]
    [InlineData("g > rect", "a c d f")]
    [InlineData("#p > rect", "a c d")]
    [InlineData("svg > g rect", "a c d f")]
    [InlineData(".x ~ .x", "c")]
    [InlineData(".y ~ #d", "d")]
    [InlineData("#d ~ rect", "")]
    [InlineData("rect ~ g > rect", "f")]
    [InlineData("style + g", "p")]
    [InlineData("style ~ g", "p")]
    [InlineData("circle ~ rect + rect", "d")]
    [InlineData("#a ~ * ~ #d", "d")]
    public void Combinators_MatchTheirElements(string selector, string expected)
        => AssertMatches(selector, expected);

    [Theory]
    [InlineData("[fill]", "a")]
    [InlineData("[fill=\"red\"]", "a")]
    [InlineData("[fill=red]", "a")]
    [InlineData("[ fill = \"red\" ]", "a")]
    [InlineData("[fill=\"RED\"]", "")]
    [InlineData("[fill=\"RED\" i]", "a")]
    [InlineData("[fill=\"RED\" I]", "a")]
    [InlineData("[fill=\"red\" s]", "a")]
    [InlineData("[FILL]", "")]
    [InlineData("[stroke=\"blue\"]", "")]
    [InlineData("[stroke=\"blue\" i]", "c")]
    [InlineData("[class~=\"y\"]", "c")]
    [InlineData("[class~=x]", "a c")]
    [InlineData("[class~=\"\"]", "")]
    [InlineData("[class~=\"x y\"]", "")]
    [InlineData("[lang|=\"en\"]", "b")]
    [InlineData("[lang|=\"en-US\"]", "b")]
    [InlineData("[lang|=\"e\"]", "")]
    [InlineData("[id^=\"p\"]", "p")]
    [InlineData("[id^=\"\"]", "")]
    [InlineData("[data-k$=\"two\"]", "a")]
    [InlineData("[data-k$=\"TWO\" i]", "a")]
    [InlineData("[data-k*=\"e t\"]", "a")]
    [InlineData("[data-k*=\"\"]", "")]
    [InlineData("[data-k]", "a")]
    [InlineData("[id]", "p a b c d e f")]
    [InlineData("rect[id=\"c\"]", "c")]
    [InlineData("[|id=\"c\"]", "c")]
    [InlineData("[*|id=\"c\"]", "c")]
    [InlineData("[class=\"x y\"][stroke]", "c")]
    public void AttributeSelectors_MatchRawValues(string selector, string expected)
        => AssertMatches(selector, expected);

    [Theory]
    [InlineData("rect:first-child", "a f")]
    [InlineData(":last-child", "p e f")]
    [InlineData(":only-child", "f")]
    [InlineData("rect:first-of-type", "a f")]
    [InlineData("rect:last-of-type", "d f")]
    [InlineData("circle:only-of-type", "b")]
    [InlineData("g:only-of-type", "p e")]
    [InlineData(":nth-child(2)", "p b")]
    [InlineData(":nth-child(odd)", "a c e f")]
    [InlineData(":nth-child(2n+3)", "c e")]
    [InlineData(":nth-child(-n+2)", "p a b f")]
    [InlineData(":nth-last-child(1)", "p e f")]
    [InlineData(":nth-last-child(2n)", "b d")]
    [InlineData("rect:nth-of-type(2)", "c")]
    [InlineData("rect:nth-last-of-type(1)", "d f")]
    [InlineData(":nth-child(2 of .x)", "c")]
    [InlineData(":nth-child(1 of rect)", "a f")]
    [InlineData(":nth-last-child(1 of rect)", "d f")]
    [InlineData(":nth-child(2 of #p > *)", "b")]
    [InlineData(":root > g", "p")]
    [InlineData(":scope > g", "p")]
    [InlineData("rect:defined", "a c d f")]
    [InlineData("svg:first-child > g", "p")]
    public void StructuralPseudoClasses_MatchTheirElements(string selector, string expected)
        => AssertMatches(selector, expected);

    // Positions of the children of g#p: a = 1, b = 2, c = 3, d = 4, e = 5.
    [Theory]
    [InlineData("2n+1", "a c e")]
    [InlineData(" 2n + 1 ", "a c e")]
    [InlineData("+2n+1", "a c e")]
    [InlineData("-n+3", "a b c")]
    [InlineData("-n+ 3", "a b c")]
    [InlineData("n-3", "a b c d e")]
    [InlineData("n -1", "a b c d e")]
    [InlineData("3n-1", "b e")]
    [InlineData("3n- 1", "b e")]
    [InlineData("3n - 1", "b e")]
    [InlineData("-2n+5", "a c e")]
    [InlineData("0n+2", "b")]
    [InlineData("even", "b d")]
    [InlineData("EVEN", "b d")]
    [InlineData("odd", "a c e")]
    [InlineData("5", "e")]
    [InlineData("+5", "e")]
    [InlineData("0", "")]
    [InlineData("n", "a b c d e")]
    [InlineData("+n", "a b c d e")]
    [InlineData("-n-1", "")]
    [InlineData("99999999999n+1", "")]
    [InlineData("1073741823n+1", "a")]
    [InlineData("1073741824n+1", "")]
    [InlineData("n-1073741824", "a b c d e")]
    [InlineData("n-1073741825", "")]
    public void NthChild_ParsesAnPlusB(string expression, string expected)
        => AssertMatches($"#p > :nth-child({expression})", expected);

    [Theory]
    [InlineData(":not(rect)", "p b e")]
    [InlineData("rect:not(.x)", "d f")]
    [InlineData(":not(.x, circle, g)", "d f")]
    [InlineData(":is(circle, #d)", "b d")]
    [InlineData(":where(circle, #d)", "b d")]
    [InlineData(":is(g > rect)", "a c d f")]
    [InlineData(":not(#p > *)", "p f")]
    [InlineData(":is(#a ~ *) rect", "f")]
    [InlineData(":not(:not(rect))", "a c d f")]
    [InlineData(":is(:frobnicate, circle)", "b")]
    [InlineData(":where(::before, circle)", "b")]
    [InlineData(":is()", "")]
    [InlineData(":is(#e :is(rect))", "f")]
    [InlineData("#a:hover", "")]
    [InlineData("#a:visited, #a:link, #a:focus", "")]
    [InlineData("#a::before", "")]
    [InlineData("#a:not(:hover)", "a")]
    [InlineData("#a::before, #b", "b")]
    [InlineData("#a:before, #b", "b")]
    [InlineData("#a::-webkit-anything, #b", "b")]
    [InlineData(":is(:is(:is(:is(:is(:is(:is(:is(circle))))))))", "b")]
    public void LogicalAndStatePseudoClasses_MatchTheirElements(string selector, string expected)
        => AssertMatches(selector, expected);

    // One invalid selector drops the whole rule. The parser stays in sync, so the next rule applies.
    [Theory]
    [InlineData("rect:frobnicate")]
    [InlineData("rect, :frobnicate")]
    [InlineData(":not(:frobnicate)")]
    [InlineData(":not()")]
    [InlineData("rect:has(circle)")]
    [InlineData("rect:lang(en)")]
    [InlineData("[svg|x]")]
    [InlineData("[x=\"a\" q]")]
    [InlineData("[x=]")]
    [InlineData("[x~ =\"a\"]")]
    [InlineData("[x=\"a\" i i]")]
    [InlineData("[]")]
    [InlineData(":nth-child()")]
    [InlineData(":nth-child(2 of)")]
    [InlineData(":nth-of-type(1 of rect)")]
    [InlineData(":nth-child(n+)")]
    [InlineData(":nth-child(2.5)")]
    [InlineData(":nth-child(+ n)")]
    [InlineData(":nth-child(2 n)")]
    [InlineData(":nth-child(n--1)")]
    [InlineData(":nth-child(1e1)")]
    [InlineData("::before rect")]
    [InlineData("::before > rect")]
    [InlineData("rect::frob")]
    [InlineData("::before::after")]
    [InlineData("#a::before:hover")]
    [InlineData("#a::before:not(:hover)")]
    [InlineData(":not(::before)")]
    [InlineData("|rect")]
    [InlineData("rect||circle")]
    [InlineData("rect ~")]
    [InlineData("+ rect")]
    [InlineData("rect + + circle")]
    [InlineData(":is(:is(:is(:is(:is(:is(:is(:is(:is(circle)))))))))")]
    [InlineData(":not(:not(:not(:not(:not(:not(:not(:not(:not(rect)))))))))")]
    public void InvalidSelector_DropsTheRule(string selector)
    {
        SvgElementTree tree = Read($$"""<style>{{selector}}, rect { stroke-width: 9 } rect { opacity: 0.5 }</style>""" + Structure);

        foreach (string id in Ids)
        {
            Assert.False(StyleOf(tree, id).IsSpecified(SvgPropertyId.StrokeWidth), id);
        }

        Assert.Equal(0.5F, StyleOf(tree, "a").Opacity);
    }

    // Selectors Level 4, section 17: ids, then classes, attributes, and pseudo-classes, then types and pseudo-elements.
    // :is(), :not(), and :nth-child(... of S) add their most specific argument; :where() adds nothing.
    [Theory]
    [InlineData(":is(#a, .b) c", 1, 0, 1)]
    [InlineData(":where(#a, .b) c", 0, 0, 1)]
    [InlineData(":not(.a.b, d)", 0, 2, 0)]
    [InlineData(":nth-child(2 of #a, .b)", 1, 1, 0)]
    [InlineData(":nth-child(2)", 0, 1, 0)]
    [InlineData("[x]", 0, 1, 0)]
    [InlineData("*", 0, 0, 0)]
    [InlineData("rect::before", 0, 0, 2)]
    [InlineData(":root", 0, 1, 0)]
    [InlineData("a:hover", 0, 1, 1)]
    [InlineData(":is()", 0, 0, 0)]
    [InlineData(":is(:frobnicate, #a)", 1, 0, 0)]
    [InlineData("g + rect ~ #x", 1, 0, 2)]
    public void Specificity_FollowsSelectorsLevel4(string selector, int ids, int classes, int types)
    {
        SvgStyleSheet sheet = new();
        SvgCssParser.ParseStyleSheet(selector + " { fill: red }", sheet, new SvgPropertyParser(), new SvgStringPool());
        sheet.Complete();

        Assert.Equal(1, sheet.Count);
        Assert.Equal((ids << 16) | (classes << 8) | types, sheet.Rules[0].Specificity);
    }

    [Fact]
    public void Specificity_OfIsDecidesTheCascade()
    {
        SvgElementTree tree = Read("<style>:is(#a, .z) { fill: blue } rect.x.x.x { fill: red }</style>" + Structure);

        Assert.Equal(Color.Blue, StyleOf(tree, "a").Fill.Color);
    }

    // Chromium: an element with whitespace text is not empty; a comment is not content.
    [Fact]
    public void Empty_CountsWhitespaceTextAsContent()
    {
        SvgElementTree tree = Read(
            """
            <style>:empty { fill: blue }</style>
            <g id="space"> </g>
            <g id="none"/>
            <g id="comment"><!-- c --></g>
            <desc id="cdata"><![CDATA[ ]]></desc>
            <g id="child"><g id="leaf"></g></g>
            <text id="text">a</text>
            """);

        Assert.False(StyleOf(tree, "space").IsSpecified(SvgPropertyId.Fill));
        Assert.True(StyleOf(tree, "none").IsSpecified(SvgPropertyId.Fill));
        Assert.True(StyleOf(tree, "comment").IsSpecified(SvgPropertyId.Fill));
        Assert.False(StyleOf(tree, "cdata").IsSpecified(SvgPropertyId.Fill));
        Assert.False(StyleOf(tree, "child").IsSpecified(SvgPropertyId.Fill));
        Assert.True(StyleOf(tree, "leaf").IsSpecified(SvgPropertyId.Fill));
        Assert.False(StyleOf(tree, "text").IsSpecified(SvgPropertyId.Fill));
    }

    // A long subsequent-sibling scan is memoized, so n siblings cost O(n), not O(n²).
    [Fact]
    public void Matching_WideSiblingListsFinish()
    {
        StringBuilder svg = new("<style>rect ~ g { fill: red } g ~ g ~ circle { fill: red } :nth-child(2n of g) { stroke: blue }</style>");
        for (int i = 0; i < 100_000; i++)
        {
            svg.Append("<g/>");
        }

        svg.Append("<circle id=\"last\"/>");
        SvgElementTree tree = Read(svg.ToString());

        Assert.True(StyleOf(tree, "last").IsSpecified(SvgPropertyId.Fill));
        Assert.False(tree.GetStyle(5).IsSpecified(SvgPropertyId.Fill));
        Assert.True(tree.GetStyle(3).IsSpecified(SvgPropertyId.Stroke));
        Assert.False(tree.GetStyle(2).IsSpecified(SvgPropertyId.Stroke));
    }

    // Nested functional pseudo-classes with combinators are memoized per element, so the cost does not grow with the
    // product of the tree depths that each level walks.
    [Fact]
    public void Matching_NestedComplexArgumentsOverDeepTreesFinish()
    {
        string open = string.Concat(Enumerable.Repeat("<g>", 400));
        string close = string.Concat(Enumerable.Repeat("</g>", 400));
        SvgElementTree tree = Read(
            "<style>:is(:is(:is(:is(x g) g) g) g) rect { fill: red } :is(:is(:is(g g) g) g) rect { stroke: blue }</style>"
            + $"{open}<rect id=\"r\"/>{close}");

        Assert.False(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Fill));
        Assert.True(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Stroke));
    }

    // Many rules with long descendant chains over a deep tree: each placement fails globally once the root is reached.
    [Fact]
    public void Matching_ManyRulesOverADeepTreeFinish()
    {
        StringBuilder css = new("<style>");
        for (int i = 0; i < 2_000; i++)
        {
            css.Append(string.Join(" ", Enumerable.Repeat("g", 30))).Append(" x").Append(i).Append(" rect, ");
        }

        css.Append("never { fill: red }</style>");
        string open = string.Concat(Enumerable.Repeat("<g>", 300));
        string close = string.Concat(Enumerable.Repeat("</g>", 300));
        SvgElementTree tree = Read($"{css}{open}<rect id=\"r\"/>{close}");

        Assert.False(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Fill));
    }

    // Deep nesting is rejected without deep recursion.
    [Fact]
    public void Parsing_DeepNestingDoesNotOverflowTheStack()
    {
        string selector = string.Concat(Enumerable.Repeat(":not(", 100_000)) + "x" + new string(')', 100_000);
        string media = string.Concat(Enumerable.Repeat("@media screen {", 10_000)) + "rect { fill: red }" + new string('}', 10_000);
        string query = "@media " + string.Concat(Enumerable.Repeat("(", 100_000)) + new string(')', 100_000) + " { rect { fill: red } }";
        SvgElementTree tree = Read($"<style>{selector} {{ fill: red }} {media} {query} rect {{ stroke: blue }}</style><rect id=\"r\"/>");

        Assert.False(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Fill));
        Assert.True(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Stroke));
    }

    // The deepest recursion that a valid selector allows: 64 compounds at each of 8 nesting levels, matched against a deep
    // tree, on a thread with a 1 MB stack.
    [Fact]
    public void Matching_DeepestValidSelectorFitsASmallStack()
    {
        string level = string.Join(" ", Enumerable.Repeat("g", 63));
        string selector = "rect";
        for (int i = 0; i < 8; i++)
        {
            selector = $":is({level} {selector})";
        }

        string open = string.Concat(Enumerable.Repeat("<g>", 500));
        string close = string.Concat(Enumerable.Repeat("</g>", 500));
        string svg = $"<style>{selector} {{ fill: blue }}</style>{open}<rect id=\"r\"/>{close}";
        SvgElementTree? tree = null;
        Thread thread = new(() => tree = Read(svg), 1024 * 1024);
        thread.Start();
        thread.Join();

        Assert.NotNull(tree);
        Assert.True(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Fill));
    }

    private static void AssertMatches(string selector, string expected)
    {
        SvgElementTree tree = Read($"<style>{selector} {{ fill: blue }}</style>{Structure}");
        string[] matched = Ids.Where(id => StyleOf(tree, id).Fill.Kind == SvgPaintKind.Color && StyleOf(tree, id).Fill.Color == Color.Blue).ToArray();

        Assert.Equal(expected, string.Join(" ", matched));
    }

    private static ref readonly SvgSpecifiedStyle StyleOf(SvgElementTree tree, string id)
    {
        Assert.True(tree.TryGetElementById(id, out int index));
        return ref tree.GetStyle(index);
    }

    private static SvgElementTree Read(string content)
        => SvgReader.Read($"<svg xmlns=\"http://www.w3.org/2000/svg\">{content}</svg>", SvgReaderOptions.Default);
}
