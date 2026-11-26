// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// The font-relative <c>ch</c> unit and the math functions of CSS Values and Units Level 4.
/// </summary>
public class SvgCssUnitTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"400\" height=\"200\"";

    // CSS Values 4: ch is the advance of "0" in the first available font. Chromium rounds the length to whole pixels
    // (10ch of Open Sans at 20px renders 110px wide); this renderer uses the advance as the specification defines it.
    [Theory]
    [InlineData("width=\"10ch\"", "")]
    [InlineData("width=\"1\"", "width: calc(5ch + 5ch)")]
    public void Ch_IsTheAdvanceOfZeroInTheFont(string attributes, string style)
    {
        FontCollection fonts = new();
        FontFamily family = fonts.Add(TestFontUtilities.GetPath("OpenSans-Regular.ttf"));
        Assert.True(family.CreateFont(20F).FontMetrics.TryGetGlyphMetrics(
            new CodePoint('0'),
            TextAttributes.None,
            TextDecorations.None,
            LayoutMode.HorizontalTopBottom,
            ColorFontSupport.None,
            default,
            out FontGlyphMetrics zero));
        float expected = 10F * 20F * zero.AdvanceWidth / zero.UnitsPerEm;

        SvgDocument document = SvgDocument.Parse(
            SvgOpen + $"><rect {attributes} height=\"10\" font-family=\"Open Sans\" font-size=\"20\" style=\"{style}\"/></svg>",
            new SvgLoadOptions { FontCollection = fonts });

        Assert.Equal(expected, document.Nodes[0].Path!.Bounds.Width, 3);
    }

    // Without a font, and for the font size itself, ch falls back to half the font size; ch in font-size refers to the
    // font of the parent.
    [Fact]
    public void Ch_FallsBackToHalfTheFontSizeWithoutAFont()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + "><rect width=\"10ch\" height=\"10\" font-size=\"20\"/></svg>",
            new SvgLoadOptions { FontCollection = new FontCollection() });

        Assert.Equal(100F, document.Nodes[0].Path!.Bounds.Width, 3);
    }

    // CSS Values 4 section 10: the result of each case, as the Chromium oracle renders it for a rect width.
    [Theory]
    [InlineData("calc(50px + 50px)", 100F)]
    [InlineData("calc(10px * 2)", 20F)]
    [InlineData("calc(2 * 10px)", 20F)]
    [InlineData("calc(200px / 2)", 100F)]
    [InlineData("calc(calc(calc(25px * 2) * 2))", 100F)]
    [InlineData("min(100px, 200px)", 100F)]
    [InlineData("max(5px, 20px, 10px)", 20F)]
    [InlineData("clamp(10px, 100px, 300px)", 100F)]
    [InlineData("clamp(10px, 5px, 300px)", 10F)]
    [InlineData("calc(25% - 10px)", 90F)]
    [InlineData("calc(1em + 4px)", 14F)]
    [InlineData("min(25%, 2em)", 20F)]
    [InlineData("clamp(10px, 25%, 90px)", 90F)]
    [InlineData("calc((10px + 10px) * 2)", 40F)]
    [InlineData("calc(1in - 90px)", 6F)]
    [InlineData("calc(10px * pi / pi)", 10F)]
    [InlineData("CALC(10px)", 10F)]
    [InlineData("calc(10px", 10F)]
    public void MathFunctions_ResolveLengths(string value, float expected)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + $"><rect height=\"10\" font-size=\"10\" style=\"width: {value}\"/></svg>");
        Assert.Equal(expected, document.Nodes[0].Path!.Bounds.Width, 3);

        // The geometry attribute takes the same value.
        document = SvgDocument.Parse(SvgOpen + $"><rect height=\"10\" font-size=\"10\" width=\"{value}\"/></svg>");
        Assert.Equal(expected, document.Nodes[0].Path!.Bounds.Width, 3);
    }

    // A negative result is clamped to the range of the property, zero for width, instead of making the value invalid.
    [Fact]
    public void MathFunctions_ClampToTheRangeOfTheProperty()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen
            + "><rect width=\"50\" height=\"10\" style=\"width: calc(10px - 20px)\"/>"
            + "<rect width=\"50\" height=\"10\" style=\"width: calc(10% - 50px)\"/></svg>");

        Assert.Empty(document.Nodes.ToArray());
    }

    // Invalid math functions: the declaration is dropped and the attribute applies, as in Chromium. A division by zero is
    // not among them: CSS Values 4 makes it infinite and clamps it to the range of the property, which Chromium renders as
    // the largest width it supports; this renderer drops such a declaration (not tested here).
    [Theory]
    [InlineData("calc(10px+10px)")]
    [InlineData("calc(10px -10px)")]
    [InlineData("calc(10px * 10px)")]
    [InlineData("calc(10px / 2px)")]
    [InlineData("calc(10px + 2)")]
    [InlineData("calc(10deg)")]
    [InlineData("clamp(1px, 2px)")]
    [InlineData("min()")]
    [InlineData("calc()")]
    [InlineData("calc(10px) 5px")]
    [InlineData("foo(10px)")]
    public void MathFunctions_RejectInvalidValues(string value)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + $"><rect width=\"50\" height=\"10\" style=\"width: {value}\"/></svg>");
        Assert.Equal(50F, document.Nodes[0].Path!.Bounds.Width, 3);
    }

    [Theory]
    [InlineData("calc(0.25 * 2)", 0.5F)]
    [InlineData("calc(50%)", 0.5F)]
    [InlineData("min(1, 0.5)", 0.5F)]
    [InlineData("calc(2)", 1F)]
    public void MathFunctions_ResolveNumbers(string value, float expected)
    {
        SvgSpecifiedStyle style = default;
        Assert.True(new SvgPropertyParser().TryParse(SvgPropertyId.Opacity, value, ref style));
        Assert.Equal(expected, style.Opacity, 4);
    }

    // Each limit makes the value invalid, so a hostile value costs bounded work.
    [Fact]
    public void MathFunctions_BoundNestingAndSize()
    {
        string nested = new string('(', SvgCalc.MaxDepth - 1) + "10px" + new string(')', SvgCalc.MaxDepth - 1);
        Assert.True(SvgCalc.TryParseLength($"calc({nested})", false, out SvgLength length));
        Assert.Equal(10F, length.Value);

        string deep = new string('(', SvgCalc.MaxDepth + 1) + "10px" + new string(')', SvgCalc.MaxDepth + 1);
        Assert.False(SvgCalc.TryParseLength($"calc({deep})", false, out _));

        string many = "calc(" + string.Join(" + ", Enumerable.Repeat("1px", SvgCalc.MaxTerms)) + ")";
        Assert.False(SvgCalc.TryParseLength(many, false, out _));
    }

    // A math function that mixes units needs the table of the document being read; outside a read it is invalid.
    [Fact]
    public void MathFunctions_MixedUnitsNeedTheDocumentTable()
    {
        Assert.False(SvgCalc.TryParseLength("calc(50% - 10px)", false, out _));

        SvgCalcTable table = new();
        SvgCalcTable previous = SvgCalcTable.Enter(table);
        try
        {
            Assert.True(SvgCalc.TryParseLength("calc(50% - 10px)", false, out SvgLength mixed));
            Assert.Equal(SvgLengthUnit.Calc, mixed.Unit);
            Assert.True(table.Get(mixed)!.HasPercentage);
            Assert.Equal(40F, table.Get(mixed)!.Evaluate(100F, 16F, new SvgUnitBasis(96F, 16F, new SizeF(300F, 150F))), 4);
        }
        finally
        {
            SvgCalcTable.Exit(previous);
        }
    }

    // A math function in the radii of a basic shape is one value, also with spaces and a slash inside it, as the Chromium
    // oracle reads it. Split at its spaces, the radii were invalid and the whole clip-path was dropped.
    [Theory]
    [InlineData("inset(4px round calc(4px * 2))")]
    [InlineData("xywh(4px 4px 32px 32px round calc(4px + 4px) / min(8px, 20%))")]
    [InlineData("rect(4px 36px 36px 4px round max(2px, 8px))")]
    public void MathFunctionsInBasicShapeRadii_KeepTheShape(string shape)
    {
        SvgDocument document = SvgDocument.Parse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40\" height=\"40\">"
            + $"<rect width=\"40\" height=\"40\" fill=\"green\" style=\"clip-path: {shape}\"/></svg>");
        using Image<Rgba32> image = document.Render<Rgba32>();

        // The inset clips the margin, and the radius of 8 also clips the corner of the inset rectangle.
        Assert.Equal(0, image[1, 20].A);
        Assert.Equal(0, image[5, 5].A);
        Assert.Equal(255, image[20, 20].A);
        Assert.Equal(255, image[5, 20].A);
    }

    // stroke-dasharray takes math functions as list entries, in the attribute and in CSS, as the Chromium oracle renders
    // them. Without them the list was invalid and the line drew solid.
    [Theory]
    [InlineData("stroke-dasharray=\"calc(5px + 5px) 10\"", "")]
    [InlineData("", "stroke-dasharray: calc(5px + 5px), min(10px, 50%)")]
    [InlineData("", "--d: 10px; stroke-dasharray: calc(var(--d) * 1) max(var(--d), 1px)")]
    public void MathFunctionsInDashArrays_AreLengths(string attributes, string style)
    {
        SvgDocument document = SvgDocument.Parse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"10\">"
            + $"<line x1=\"0\" y1=\"5\" x2=\"100\" y2=\"5\" stroke=\"green\" stroke-width=\"4\" {attributes} style=\"{style}\"/></svg>");
        using Image<Rgba32> image = document.Render<Rgba32>();

        Assert.Equal(255, image[5, 5].A);
        Assert.Equal(0, image[15, 5].A);
        Assert.Equal(255, image[25, 5].A);
        Assert.Equal(0, image[35, 5].A);
    }
}
