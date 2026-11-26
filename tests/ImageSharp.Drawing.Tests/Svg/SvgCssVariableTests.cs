// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// <c>var()</c> substitution at computed-value time (CSS Custom Properties Level 1). Each case was checked with the
/// Chromium oracle (tests/SvgTools/render-svg.js) on the same markup.
/// </summary>
public class SvgCssVariableTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\">";

    private static readonly Rgba32 Green = new(0, 128, 0, 255);

    [Theory]
    [InlineData("<rect id=\"r\" fill=\"red\" style=\"--c: green; fill: var(--c)\"/>")]
    [InlineData("<rect id=\"r\" fill=\"red\" style=\"fill: var(--missing, green)\"/>")]
    [InlineData("<rect id=\"r\" fill=\"red\" style=\"--g: green; fill: var(--missing, var(--g))\"/>")]
    [InlineData("<rect id=\"r\" fill=\"red\" style=\"fill: var(--missing, rgb(0, 128, 0))\"/>")]
    [InlineData("<g style=\"--c: green\"><rect id=\"r\" fill=\"red\" style=\"fill: var(--c)\"/></g>")]
    [InlineData("<rect id=\"r\" fill=\"var(--c)\" style=\"--c: green\"/>")]
    [InlineData("<rect id=\"r\" fill=\"red\" style=\"--k: green; fill: VAR( --k )\"/>")]
    [InlineData("<rect id=\"r\" fill=\"red\" style=\"--g: 128; fill: rgb(0 var(--g) 0)\"/>")]
    [InlineData("<rect id=\"r\" fill=\"red\" style=\"--a: green; --b: var(--a); --c: var(--b); fill: var(--c)\"/>")]
    [InlineData("<g style=\"--c: red\"><rect id=\"r\" fill=\"red\" style=\"--c: initial; fill: var(--c, green)\"/></g>")]
    [InlineData("<rect id=\"r\" fill=\"red\" style=\"--c:; fill: var(--c) green\"/>")]
    public void Var_SubstitutesValuesAndFallbacks(string content)
        => Assert.Equal(Green, Compute(content, "r").Fill.Color.ToPixel<Rgba32>());

    // A custom property computes where it is declared: the child redeclaring --b does not change --a.
    [Fact]
    public void Var_CustomPropertySubstitutesWhereItIsDeclared()
    {
        const string content = """
            <g style="--a: var(--b); --b: green"><g style="--b: red"><rect id="r" style="fill: var(--a)"/></g></g>
            """;

        Assert.Equal(Green, Compute(content, "r").Fill.Color.ToPixel<Rgba32>());
    }

    // Invalid at computed-value time makes the property unset: an inherited property takes the parent value, another
    // property its initial value, whatever the attribute says.
    [Theory]
    [InlineData("--a: var(--b); --b: var(--a); fill: var(--a)")]
    [InlineData("--c: 10px; fill: var(--c)")]
    [InlineData("fill: var(--nope)")]
    [InlineData("--n: 25; fill: rgb(0 var(--n)5 0)")]
    public void Var_InvalidAtComputedValueTimeIsUnset(string style)
    {
        string content = $"<g fill=\"green\"><rect id=\"r\" fill=\"red\" opacity=\"0.5\" style=\"{style}\"/></g>";
        Assert.Equal(Green, Compute(content, "r").Fill.Color.ToPixel<Rgba32>());

        SvgComputedStyle opacity = Compute("<rect id=\"r\" opacity=\"0.5\" style=\"--c: abc; opacity: var(--c)\"/>", "r");
        Assert.Equal(1F, opacity.Opacity);
    }

    // A cycle makes each member the guaranteed-invalid value, so a reference takes its fallback.
    [Fact]
    public void Var_CycleMembersTakeTheirFallback()
    {
        SvgComputedStyle style = Compute("<rect id=\"r\" fill=\"red\" style=\"--a: var(--a); --b: var(--x, var(--b)); fill: var(--b, green)\"/>", "r");
        Assert.Equal(Green, style.Fill.Color.ToPixel<Rgba32>());
        Assert.True(style.CustomProperties!.TryGetValue("--a", out string a));
        Assert.Null(a);
    }

    // A malformed var() is invalid at parse time: the declaration of next lower precedence applies.
    [Fact]
    public void Var_MalformedReferenceKeepsTheLowerDeclaration()
        => Assert.Equal(Green, Compute("<rect id=\"r\" fill=\"green\" style=\"fill: var(c)\"/>", "r").Fill.Color.ToPixel<Rgba32>());

    // A shorthand with var() sets each longhand; a later longhand declaration without var() keeps its value.
    [Fact]
    public void Var_InShorthands()
    {
        const string content = """
            <path id="m" d="M0 0 L10 0" style="--m: url(#a); marker: var(--m)"/>
            <text id="f" style="--f: 30px serif; font: var(--f); font-size: 12px">a</text>
            <text id="i" font-size="20" style="font: var(--x) bogus">a</text>
            """;

        SvgComputedStyle marker = Compute(content, "m");
        Assert.Equal("a", marker.MarkerStart.Id);
        Assert.Equal("a", marker.MarkerEnd.Id);

        Assert.Equal(12F, Compute(content, "f").FontSize);

        // Invalid at computed-value time: font-size is unset, which inherits the 16px of the root, not the attribute.
        Assert.Equal(16F, Compute(content, "i").FontSize);
    }

    // A substituted font-size applies to the em lengths of the same element.
    [Fact]
    public void Var_FontSizeAppliesToEmLengths()
    {
        SvgComputedStyle style = Compute("<rect id=\"r\" style=\"--fs: 10px; font-size: var(--fs); stroke-width: 2em\"/>", "r");
        Assert.Equal(10F, style.FontSize);
        Assert.Equal(20F, style.StrokeWidth.Value);
    }

    // Geometry and the CSS transform from var(), which the reader cannot fold.
    [Fact]
    public void Var_InGeometryAndTransform()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "<rect height=\"10\" style=\"--w: 30px; width: var(--w); --t: 5px; transform: translate(var(--t), 0px)\"/></svg>");

        RectangleF bounds = document.Nodes[0].Path!.Bounds;
        Assert.Equal(30F, bounds.Width, 3);
        Assert.Equal(new System.Numerics.Vector2(5F, 0F), document.Transforms[document.Nodes[0].TransformIndex].Translation);
    }

    // A chain that doubles its length at each step stops at the substitution limit, which makes the reference invalid,
    // so its fallback applies.
    [Fact]
    public void Var_DoublingChainStopsAtTheLimit()
    {
        System.Text.StringBuilder style = new("--v0: abcdefgh;");
        for (int i = 1; i <= 24; i++)
        {
            style.Append(CultureInfo.InvariantCulture, $" --v{i}: var(--v{i - 1}) var(--v{i - 1});");
        }

        style.Append(" fill: var(--v24, green)");
        SvgComputedStyle computed = Compute($"<rect id=\"r\" fill=\"red\" style=\"{style}\"/>", "r");

        Assert.Equal(Green, computed.Fill.Color.ToPixel<Rgba32>());
        Assert.True(computed.CustomProperties!.TryGetValue("--v10", out string short10));
        Assert.Equal(8 << 10, short10.Replace(" ", string.Empty).Length);
        Assert.True(computed.CustomProperties.TryGetValue("--v24", out string long24));
        Assert.Null(long24);
    }

    private static SvgComputedStyle Compute(string content, string id)
    {
        SvgElementTree tree = SvgReader.Read(SvgOpen + content + "</svg>", SvgReaderOptions.Default);
        SvgResolver resolver = new(tree, new SvgLoadOptions());
        Assert.True(tree.TryGetElementById(id, out int index));
        return resolver.ComputeDocumentStyle(index);
    }
}
