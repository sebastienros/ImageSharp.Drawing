// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.Fonts.Tables.AdvancedTypographic;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests the parsing, the cascade, and the computed values of the properties whose ids start at 64 (the geometry
/// properties, <c>transform-box</c>, the font feature properties, and <c>white-space-collapse</c>), of the CSS basic
/// shapes of <c>clip-path</c>, of custom properties and <c>var()</c> declarations, and of the <c>pathLength</c>,
/// <c>lang</c>, and <c>xml:lang</c> attributes. Expectations follow CSS Cascade Level 4, CSS Custom Properties Level 1,
/// CSS Masking Level 1, CSS Shapes Level 1 and 2, CSS Fonts Level 4, CSS Text Level 4, SVG 2, and, where those leave a
/// choice, what the Chromium oracle renders.
/// </summary>
public class SvgStylePropertyTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\">";

    private readonly SvgPropertyParser parser = new();

    [Fact]
    public void PropertySet_HoldsPropertiesInBothWords()
    {
        SvgPropertySet set = SvgPropertySet.Of(SvgPropertyId.Fill) | SvgPropertySet.Of(SvgPropertyId.WhiteSpaceCollapse);

        Assert.True(set.Contains(SvgPropertyId.Fill));
        Assert.True(set.Contains(SvgPropertyId.WhiteSpaceCollapse));
        Assert.False(set.Contains(SvgPropertyId.X));
        Assert.Equal(SvgPropertyId.Fill, SvgPropertySet.TakeFirst(ref set));
        Assert.Equal(SvgPropertyId.WhiteSpaceCollapse, SvgPropertySet.TakeFirst(ref set));
        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void HighProperties_HaveTheirInheritance()
    {
        Assert.False(SvgSpecifiedStyle.IsInherited(SvgPropertyId.X));
        Assert.False(SvgSpecifiedStyle.IsInherited(SvgPropertyId.D));
        Assert.False(SvgSpecifiedStyle.IsInherited(SvgPropertyId.TransformBox));
        Assert.True(SvgSpecifiedStyle.IsInherited(SvgPropertyId.FontFeatureSettings));
        Assert.True(SvgSpecifiedStyle.IsInherited(SvgPropertyId.FontVariantCaps));
        Assert.True(SvgSpecifiedStyle.IsInherited(SvgPropertyId.WhiteSpaceCollapse));
    }

    [Theory]
    [InlineData("x", "10px", 10F, (int)SvgLengthUnit.Px)]
    [InlineData("y", "-5", -5F, (int)SvgLengthUnit.Number)]
    [InlineData("cx", "50%", 50F, (int)SvgLengthUnit.Percent)]
    [InlineData("r", "2em", 2F, (int)SvgLengthUnit.Em)]
    [InlineData("width", "0", 0F, (int)SvgLengthUnit.Number)]
    [InlineData("rx", "3mm", 3F, (int)SvgLengthUnit.Mm)]
    public void Geometry_ParsesLengths(string name, string value, float expected, int unit)
    {
        Assert.True(SvgPropertyParser.TryGetProperty(name, out SvgPropertyId property));
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(property, value, ref style));
        Assert.True(style.HasValue(property));
        Assert.Equal(new SvgLength(expected, (SvgLengthUnit)unit), style.Extended!.Geometry!.GetLength(property));
    }

    // CSS: r, width, height, rx, and ry are non-negative; width, height, rx, and ry take auto, which r does not.
    [Theory]
    [InlineData("r", "-1px", false)]
    [InlineData("r", "auto", false)]
    [InlineData("width", "-1", false)]
    [InlineData("width", "auto", true)]
    [InlineData("ry", "auto", true)]
    [InlineData("x", "auto", false)]
    [InlineData("x", "10px 5px", false)]
    [InlineData("cy", "red", false)]
    public void Geometry_ValidatesValues(string name, string value, bool valid)
    {
        Assert.True(SvgPropertyParser.TryGetProperty(name, out SvgPropertyId property));
        SvgSpecifiedStyle style = default;

        Assert.Equal(valid, this.parser.TryParse(property, value, ref style));
        Assert.Equal(valid, style.IsSpecified(property));
        if (!valid)
        {
            // An invalid declaration allocates nothing.
            Assert.Null(style.Extended);
        }
    }

    [Fact]
    public void Geometry_D_ParsesNoneAndPath()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.D, "path('M 0 0 h 10 v 10 z')", ref style));
        Assert.NotNull(style.Extended!.Geometry!.D);
        Assert.Equal(new RectangleF(0, 0, 10, 10), style.Extended.Geometry.D!.Bounds);

        Assert.True(this.parser.TryParse(SvgPropertyId.D, "none", ref style));
        Assert.Null(style.Extended.Geometry.D);
    }

    // Chromium drops a path() whose string has an error anywhere, where the d attribute renders up to the error.
    [Theory]
    [InlineData("path('M 0 0 L')")]
    [InlineData("path('L 10 10')")]
    [InlineData("path(M 0 0)")]
    [InlineData("path('M 0 0'")]
    [InlineData("path('M 0 0' 'M 1 1')")]
    [InlineData("url(#p)")]
    public void Geometry_D_RejectsInvalidPath(string value)
    {
        SvgSpecifiedStyle style = default;

        Assert.False(this.parser.TryParse(SvgPropertyId.D, value, ref style));
    }

    [Theory]
    [InlineData("fill-box", (int)SvgTransformBox.FillBox)]
    [InlineData("VIEW-BOX", (int)SvgTransformBox.ViewBox)]
    [InlineData("content-box", (int)SvgTransformBox.ContentBox)]
    public void TransformBox_ParsesKeywords(string value, int expected)
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.TransformBox, value, ref style));
        Assert.Equal((SvgTransformBox)expected, style.Extended!.Geometry!.TransformBox);
        Assert.False(this.parser.TryParse(SvgPropertyId.TransformBox, "margin-box", ref style));
    }

    // The attribute stays the fast path: x on a rect is the attribute record, and only CSS sets the property.
    [Fact]
    public void Geometry_AttributeIsNotTheProperty()
    {
        SvgElementTree tree = Read("""<rect id="r" x="5" width="10" height="10"/><rect id="s" transform-box="fill-box"/>""");

        Assert.False(tree.GetStyle(Index(tree, "r")).IsSpecified(SvgPropertyId.X));
        Assert.Equal(new SvgLength(5, SvgLengthUnit.Number), tree.GetRect(Index(tree, "r")).X);

        // Chromium has no presentation attribute for transform-box.
        Assert.True(tree.GetStyle(Index(tree, "s")).IsEmpty);
    }

    [Fact]
    public void Geometry_CascadesAndComputes()
    {
        const string content = """
            <style>#r { x: 1px; width: 50%; } rect { y: 7px }</style>
            <g id="g" style="font-size: 10px">
              <rect id="r" x="30" style="x: 2em; rx: auto"/>
              <rect id="i" style="x: inherit; y: inherit; height: initial"/>
            </g>
            """;

        SvgComputedStyle r = Compute(content, "r");
        SvgGeometryStyle geometry = r.Geometry;

        // The style attribute beats the rule, which beats the attribute; em computes to pixels at the element.
        Assert.Equal(new SvgLength(20, SvgLengthUnit.Px), geometry.X);
        Assert.Equal(new SvgLength(50, SvgLengthUnit.Percent), geometry.Width);
        Assert.Equal(new SvgLength(7, SvgLengthUnit.Px), geometry.Y);
        Assert.False(geometry.Rx.IsSpecified);
        Assert.True(geometry.FromStyle.Contains(SvgPropertyId.X));
        Assert.True(geometry.FromStyle.Contains(SvgPropertyId.Rx));
        Assert.False(geometry.FromStyle.Contains(SvgPropertyId.Height));

        // The parent g specifies no geometry, so inherit takes the attribute of the parent, which only the resolve stage
        // reads; initial counts as a declaration and beats the attribute.
        SvgGeometryStyle inherited = Compute(content, "i").Geometry;
        Assert.True(inherited.FromParentAttribute.Contains(SvgPropertyId.X));
        Assert.False(inherited.FromStyle.Contains(SvgPropertyId.X));

        // The style attribute (y: inherit) beats the rule y: 7px.
        Assert.True(inherited.FromParentAttribute.Contains(SvgPropertyId.Y));
        Assert.True(inherited.FromStyle.Contains(SvgPropertyId.Height));
        Assert.False(inherited.Height.IsSpecified);

        // Geometry does not inherit, and an element without geometry declarations shares the initial values.
        Assert.Same(SvgGeometryStyle.Initial, Compute(content, "g").Geometry);
    }

    [Fact]
    public void Geometry_InheritCopiesAParentDeclaration()
    {
        SvgComputedStyle style = Compute(
            """<g style="cx: 12px; transform-box: fill-box"><circle id="c" style="cx: inherit; transform-box: inherit"/></g>""",
            "c");

        Assert.Equal(new SvgLength(12, SvgLengthUnit.Px), style.Geometry.Cx);
        Assert.True(style.Geometry.FromStyle.Contains(SvgPropertyId.Cx));
        Assert.Equal(SvgTransformBox.FillBox, style.Geometry.TransformBox);
        Assert.Equal(SvgTransformBox.ViewBox, SvgGeometryStyle.Initial.TransformBox);
    }

    [Theory]
    [InlineData("inset(10px)", (int)SvgBasicShapeKind.Inset, (int)SvgGeometryBox.Unspecified)]
    [InlineData("inset(1px 2px 3px 4px round 5px) stroke-box", (int)SvgBasicShapeKind.Inset, (int)SvgGeometryBox.StrokeBox)]
    [InlineData("view-box circle(10px at 20px 30px)", (int)SvgBasicShapeKind.Circle, (int)SvgGeometryBox.ViewBox)]
    [InlineData("circle()", (int)SvgBasicShapeKind.Circle, (int)SvgGeometryBox.Unspecified)]
    [InlineData("ellipse(closest-side farthest-side at left top)", (int)SvgBasicShapeKind.Ellipse, (int)SvgGeometryBox.Unspecified)]
    [InlineData("polygon(evenodd, 0 0, 10px 0, 10px 10px)", (int)SvgBasicShapeKind.Polygon, (int)SvgGeometryBox.Unspecified)]
    [InlineData("path(evenodd, 'M0 0 h10 v10 z') fill-box", (int)SvgBasicShapeKind.Path, (int)SvgGeometryBox.FillBox)]
    [InlineData("xywh(1px 2px 30% 40%)", (int)SvgBasicShapeKind.Xywh, (int)SvgGeometryBox.Unspecified)]
    [InlineData("rect(auto 10px 20px auto round 1px / 2px)", (int)SvgBasicShapeKind.Rect, (int)SvgGeometryBox.Unspecified)]
    [InlineData("margin-box", (int)SvgBasicShapeKind.None, (int)SvgGeometryBox.MarginBox)]
    public void ClipPath_ParsesBasicShapes(string value, int kind, int box)
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.ClipPath, value, ref style));
        SvgReference clip = style.Extended!.ClipPath;
        Assert.False(clip.IsNone);
        Assert.Null(clip.Id);
        Assert.Equal((SvgBasicShapeKind)kind, clip.Shape!.Kind);
        Assert.Equal((SvgGeometryBox)box, clip.Shape.Box);
    }

    [Theory]
    [InlineData("inset()")]
    [InlineData("inset(1px 2px 3px 4px 5px)")]
    [InlineData("inset(1px round)")]
    [InlineData("circle(-1px)")]
    [InlineData("circle(1px 2px)")]
    [InlineData("ellipse(1px)")]
    [InlineData("circle(at)")]
    [InlineData("circle(at left right)")]
    [InlineData("polygon()")]
    [InlineData("polygon(nonzero)")]
    [InlineData("polygon(0 0 0)")]
    [InlineData("xywh(0 0 -1px 2px)")]
    [InlineData("rect(0 0 0)")]
    [InlineData("path('M 0 0 L')")]
    [InlineData("fill-box stroke-box")]
    [InlineData("circle() inset(1px)")]
    [InlineData("shape(from 0 0, hline to 10px)")]
    public void ClipPath_RejectsInvalidShapes(string value)
    {
        SvgSpecifiedStyle style = default;

        Assert.False(this.parser.TryParse(SvgPropertyId.ClipPath, value, ref style));
        Assert.False(style.IsSpecified(SvgPropertyId.ClipPath));
    }

    [Fact]
    public void ClipPath_ShapeHoldsItsParameters()
    {
        SvgSpecifiedStyle style = default;
        Assert.True(this.parser.TryParse(SvgPropertyId.ClipPath, "inset(1px 2px round 3px 4px / 5px)", ref style));
        SvgClipShape inset = style.Extended!.ClipPath.Shape!;

        // Omitted sides and corners follow the box rule: top, right, bottom, left.
        Assert.Equal([Px(1), Px(2), Px(1), Px(2)], inset.Lengths);
        Assert.Equal([Px(3), Px(4), Px(3), Px(4), Px(5), Px(5), Px(5), Px(5)], inset.Radii!);

        Assert.True(this.parser.TryParse(SvgPropertyId.ClipPath, "circle(farthest-side at right 10px bottom 20%)", ref style));
        SvgClipShape circle = style.Extended!.ClipPath.Shape!;
        Assert.Equal(SvgShapeRadiusKind.FarthestSide, circle.RadiusXKind);
        Assert.Equal(Px(10), circle.Position.X);
        Assert.True(circle.Position.XFromEnd);
        Assert.Equal(new SvgLength(20, SvgLengthUnit.Percent), circle.Position.Y);
        Assert.True(circle.Position.YFromEnd);

        Assert.True(this.parser.TryParse(SvgPropertyId.ClipPath, "ellipse(1px 2px at top)", ref style));
        SvgClipShape ellipse = style.Extended!.ClipPath.Shape!;
        Assert.Equal([Px(1), Px(2)], ellipse.Lengths);
        Assert.Equal(new SvgLength(50, SvgLengthUnit.Percent), ellipse.Position.X);
        Assert.Equal(new SvgLength(0, SvgLengthUnit.Percent), ellipse.Position.Y);

        Assert.True(this.parser.TryParse(SvgPropertyId.ClipPath, "polygon(1px 2px, 3% 4%)", ref style));
        SvgClipShape polygon = style.Extended!.ClipPath.Shape!;
        Assert.Equal(SvgFillRule.NonZero, polygon.FillRule);
        Assert.Equal([Px(1), Px(2), new SvgLength(3, SvgLengthUnit.Percent), new SvgLength(4, SvgLengthUnit.Percent)], polygon.Lengths);
    }

    // Chromium accepts basic shapes in the clip-path presentation attribute, and the shape clips: a circle of radius 5
    // at the center of the rectangle.
    [Fact]
    public void ClipPath_ShapeAttributeIsStoredAndClips()
    {
        const string content = """<rect id="r" width="200" height="100" fill="green" clip-path="circle(5px)"/>""";
        SvgComputedStyle style = Compute(content, "r");
        Assert.Equal(SvgBasicShapeKind.Circle, style.ClipPath.Shape!.Kind);

        using Image<Rgba32> image = Render(content);
        Assert.Equal(default, image[2, 2]);
        Assert.Equal(new Rgba32(0, 128, 0, 255), image[100, 50]);
    }

    [Theory]
    [InlineData("normal", 0)]
    [InlineData("'liga' 0, 'smcp', \"ss01\" 3, 'kern' on, 'dlig' off", 5)]
    public void FontFeatureSettings_ParsesEntries(string value, int count)
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.FontFeatureSettings, value, ref style));
        SvgFontFeatureSetting[] settings = style.Extended!.Text!.FontFeatureSettings;
        Assert.Equal(count, settings.Length);
        if (count > 0)
        {
            Assert.Equal(Tag.Parse("liga"), settings[0].Tag);
            Assert.Equal(0, settings[0].Value);
            Assert.Equal(1, settings[1].Value);
            Assert.Equal(3, settings[2].Value);
            Assert.Equal(1, settings[3].Value);
            Assert.Equal(0, settings[4].Value);
        }
    }

    [Theory]
    [InlineData("'lig' 1")]
    [InlineData("'ligas'")]
    [InlineData("liga")]
    [InlineData("'liga' -1")]
    [InlineData("'liga' 1.5")]
    [InlineData("'liga',")]
    [InlineData("'liéa'")]
    public void FontFeatureSettings_RejectsInvalidEntries(string value)
    {
        SvgSpecifiedStyle style = default;

        Assert.False(this.parser.TryParse(SvgPropertyId.FontFeatureSettings, value, ref style));
    }

    [Theory]
    [InlineData((int)SvgPropertyId.FontVariantLigatures, "none", true)]
    [InlineData((int)SvgPropertyId.FontVariantLigatures, "no-common-ligatures discretionary-ligatures", true)]
    [InlineData((int)SvgPropertyId.FontVariantLigatures, "common-ligatures no-common-ligatures", false)]
    [InlineData((int)SvgPropertyId.FontVariantLigatures, "none contextual", false)]
    [InlineData((int)SvgPropertyId.FontVariantNumeric, "oldstyle-nums tabular-nums diagonal-fractions ordinal slashed-zero", true)]
    [InlineData((int)SvgPropertyId.FontVariantNumeric, "lining-nums oldstyle-nums", false)]
    [InlineData((int)SvgPropertyId.FontVariantNumeric, "ordinal ordinal", false)]
    [InlineData((int)SvgPropertyId.FontVariantEastAsian, "jis78 full-width ruby", true)]
    [InlineData((int)SvgPropertyId.FontVariantEastAsian, "jis78 simplified", false)]
    [InlineData((int)SvgPropertyId.FontVariantCaps, "all-small-caps", true)]
    [InlineData((int)SvgPropertyId.FontVariantCaps, "small-caps unicase", false)]
    [InlineData((int)SvgPropertyId.FontVariantPosition, "super", true)]
    [InlineData((int)SvgPropertyId.FontVariantPosition, "sub super", false)]
    [InlineData((int)SvgPropertyId.WhiteSpaceCollapse, "preserve-breaks", true)]
    [InlineData((int)SvgPropertyId.WhiteSpaceCollapse, "preserve-spaces", false)]
    [InlineData((int)SvgPropertyId.WhiteSpaceCollapse, "discard", false)]
    public void FontVariantAndWhiteSpaceCollapse_FollowTheirGrammar(int property, string value, bool valid)
    {
        SvgSpecifiedStyle style = default;

        Assert.Equal(valid, this.parser.TryParse((SvgPropertyId)property, value, ref style));
    }

    [Fact]
    public void FontVariantLonghands_StoreTheirValues()
    {
        SvgSpecifiedStyle style = default;
        Assert.True(this.parser.TryParse(SvgPropertyId.FontVariantLigatures, "no-common-ligatures contextual", ref style));
        Assert.True(this.parser.TryParse(SvgPropertyId.FontVariantNumeric, "slashed-zero oldstyle-nums", ref style));
        Assert.True(this.parser.TryParse(SvgPropertyId.FontVariantEastAsian, "proportional-width jis04", ref style));
        Assert.True(this.parser.TryParse(SvgPropertyId.FontVariantCaps, "petite-caps", ref style));
        Assert.True(this.parser.TryParse(SvgPropertyId.FontVariantPosition, "sub", ref style));
        SvgTextStyle text = style.Extended!.Text!;

        Assert.Equal(SvgFontVariantLigatures.NoCommonLigatures | SvgFontVariantLigatures.Contextual, text.FontVariantLigatures);
        Assert.Equal(SvgFontVariantNumeric.SlashedZero | SvgFontVariantNumeric.OldstyleNums, text.FontVariantNumeric);
        Assert.Equal(SvgFontVariantEastAsian.ProportionalWidth | SvgFontVariantEastAsian.Jis04, text.FontVariantEastAsian);
        Assert.Equal(SvgFontVariant.PetiteCaps, text.FontVariantCaps);
        Assert.Equal(SvgFontVariantPosition.Sub, text.FontVariantPosition);

        // The longhands are new properties: the value that the text layout reads today stays unspecified.
        Assert.False(style.IsSpecified(SvgPropertyId.FontVariant));
    }

    // CSS Fonts Level 4: font-variant is the shorthand of the longhands. The value that the text layout reads today
    // (font-variant) is set as before.
    [Fact]
    public void FontVariant_SetsTheLonghands()
    {
        SvgSpecifiedStyle style = default;
        Assert.True(this.parser.TryParse(SvgPropertyId.FontVariant, "small-caps oldstyle-nums no-contextual super", ref style));
        SvgTextStyle text = style.Extended!.Text!;

        Assert.Equal(SvgFontVariant.SmallCaps, text.FontVariant);
        Assert.Equal(SvgFontVariant.SmallCaps, text.FontVariantCaps);
        Assert.Equal(SvgFontVariantNumeric.OldstyleNums, text.FontVariantNumeric);
        Assert.Equal(SvgFontVariantLigatures.NoContextual, text.FontVariantLigatures);
        Assert.Equal(SvgFontVariantPosition.Super, text.FontVariantPosition);
        Assert.True(style.HasValue(SvgPropertyId.FontVariantEastAsian));

        Assert.True(this.parser.TryParse(SvgPropertyId.FontVariant, "none", ref style));
        Assert.Equal(SvgFontVariantLigatures.None, style.Extended!.Text!.FontVariantLigatures);
        Assert.Equal(SvgFontVariant.Normal, style.Extended.Text.FontVariantCaps);

        // A CSS-wide keyword reaches every longhand.
        Assert.True(this.parser.TryParse(SvgPropertyId.FontVariant, "inherit", ref style));
        Assert.True(style.IsInherit(SvgPropertyId.FontVariant));
        Assert.True(style.IsInherit(SvgPropertyId.FontVariantNumeric));
    }

    // A value that the older lenient rule accepts but the shorthand grammar does not keeps its effect on font-variant and
    // leaves the longhands alone, so existing documents render as before.
    [Fact]
    public void FontVariant_LenientValueLeavesTheLonghands()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.FontVariant, "small-caps lining-nums oldstyle-nums", ref style));
        Assert.Equal(SvgFontVariant.SmallCaps, style.Extended!.Text!.FontVariant);
        Assert.False(style.IsSpecified(SvgPropertyId.FontVariantCaps));
        Assert.False(style.IsSpecified(SvgPropertyId.FontVariantNumeric));
    }

    // Chromium: the font shorthand resets font-feature-settings and the font-variant longhands; small-caps in it sets
    // font-variant-caps.
    [Fact]
    public void FontShorthand_ResetsTheFeatureProperties()
    {
        SvgComputedStyle style = Compute(
            """<g style="font-feature-settings: 'smcp'; font-variant-numeric: oldstyle-nums"><text id="t" style="font-variant-position: sub; font: small-caps 12px serif">a</text></g>""",
            "t");

        Assert.Empty(style.Text.FontFeatureSettings);
        Assert.Equal(SvgFontVariantNumeric.Normal, style.Text.FontVariantNumeric);
        Assert.Equal(SvgFontVariantPosition.Normal, style.Text.FontVariantPosition);
        Assert.Equal(SvgFontVariant.SmallCaps, style.Text.FontVariantCaps);
    }

    [Fact]
    public void FontFeatureProperties_InheritAndHaveNoPresentationAttribute()
    {
        const string content = """
            <g style="font-feature-settings: 'tnum'; font-variant-ligatures: none; white-space-collapse: preserve">
              <text id="t">a</text>
            </g>
            <text style="white-space-collapse: preserve">a<tspan id="s">b</tspan></text>
            <text id="a" font-feature-settings="'smcp'" font-variant-caps="small-caps" white-space-collapse="preserve">a</text>
            """;

        SvgTextStyle inherited = Compute(content, "t").Text;
        Assert.Equal(Tag.Parse("tnum"), Assert.Single(inherited.FontFeatureSettings).Tag);
        Assert.Equal(SvgFontVariantLigatures.None, inherited.FontVariantLigatures);

        // white-space-collapse inherits inside text, but a text element starts from the initial value, as the user agent
        // style sheet of the Chromium oracle sets it on text: the value of the g does not reach the text.
        Assert.Equal(SvgWhiteSpaceCollapse.Collapse, inherited.WhiteSpaceCollapse);
        Assert.Equal(SvgWhiteSpaceCollapse.Preserve, Compute(content, "s").Text.WhiteSpaceCollapse);

        // Chromium ignores these attributes.
        SvgTextStyle attributes = Compute(content, "a").Text;
        Assert.Same(SvgTextStyle.Initial, attributes);
    }

    // The legacy keywords set white-space as before and white-space-collapse too; the CSS Text Level 4 shorthand forms
    // set only white-space-collapse, which the text layout reads.
    [Theory]
    [InlineData("pre", true, (int)SvgWhiteSpace.Pre, (int)SvgWhiteSpaceCollapse.Preserve)]
    [InlineData("nowrap", true, (int)SvgWhiteSpace.Nowrap, (int)SvgWhiteSpaceCollapse.Collapse)]
    [InlineData("pre-line", true, (int)SvgWhiteSpace.PreLine, (int)SvgWhiteSpaceCollapse.PreserveBreaks)]
    [InlineData("break-spaces", true, (int)SvgWhiteSpace.BreakSpaces, (int)SvgWhiteSpaceCollapse.BreakSpaces)]
    [InlineData("preserve nowrap", false, (int)SvgWhiteSpace.Normal, (int)SvgWhiteSpaceCollapse.Preserve)]
    [InlineData("wrap preserve-breaks", false, (int)SvgWhiteSpace.Normal, (int)SvgWhiteSpaceCollapse.PreserveBreaks)]
    [InlineData("collapse", false, (int)SvgWhiteSpace.Normal, (int)SvgWhiteSpaceCollapse.Collapse)]
    [InlineData("wrap", false, (int)SvgWhiteSpace.Normal, (int)SvgWhiteSpaceCollapse.Collapse)]
    public void WhiteSpace_SetsTheCollapseMode(string value, bool legacy, int whiteSpace, int collapse)
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParse(SvgPropertyId.WhiteSpace, value, ref style));
        Assert.Equal(legacy, style.IsSpecified(SvgPropertyId.WhiteSpace));
        Assert.Equal((SvgWhiteSpace)whiteSpace, style.Extended!.Text!.WhiteSpace);
        Assert.Equal((SvgWhiteSpaceCollapse)collapse, style.Extended.Text.WhiteSpaceCollapse);
        Assert.True(style.HasValue(SvgPropertyId.WhiteSpaceCollapse));
    }

    [Theory]
    [InlineData("preserve preserve")]
    [InlineData("nowrap wrap")]
    [InlineData("preserve-spaces")]
    [InlineData("pre nowrap")]
    public void WhiteSpace_RejectsInvalidForms(string value)
    {
        SvgSpecifiedStyle style = default;

        Assert.False(this.parser.TryParse(SvgPropertyId.WhiteSpace, value, ref style));
        Assert.True(style.IsEmpty);
    }

    // The cascade decides between white-space and white-space-collapse by order, and xml:space still maps to white-space.
    [Fact]
    public void WhiteSpaceCollapse_CascadesWithTheShorthand()
    {
        const string content = """
            <text id="a" style="white-space: pre; white-space-collapse: collapse">a</text>
            <text id="b" style="white-space-collapse: collapse; white-space: pre-wrap">a</text>
            <text id="c" xml:space="preserve">a</text>
            """;

        SvgTextStyle a = Compute(content, "a").Text;
        Assert.Equal(SvgWhiteSpaceCollapse.Collapse, a.WhiteSpaceCollapse);
        Assert.Equal(SvgWhiteSpace.Pre, a.WhiteSpace);
        Assert.Equal(SvgWhiteSpaceCollapse.Preserve, Compute(content, "b").Text.WhiteSpaceCollapse);
        Assert.Equal(SvgWhiteSpaceCollapse.Preserve, Compute(content, "c").Text.WhiteSpaceCollapse);
    }

    [Fact]
    public void CustomProperties_CascadeAndInherit()
    {
        const string content = """
            <style>g { --a: rule; --b: rule } #r { --b: rule2 !important }</style>
            <g id="g" style="--a: style; --c:  spaced  value ; --empty:;">
              <rect id="r" style="--b: style; --d: initial; --A: case"/>
              <rect id="u" style="--a: unset; --c: inherit"/>
            </g>
            """;

        SvgComputedStyle g = Compute(content, "g");
        Assert.True(g.CustomProperties!.TryGetValue("--a", out string? a));
        Assert.Equal("style", a);
        Assert.True(g.CustomProperties.TryGetValue("--c", out string? c));
        Assert.Equal("spaced  value", c);
        Assert.True(g.CustomProperties.TryGetValue("--empty", out string? empty));
        Assert.Equal(string.Empty, empty);

        SvgCustomProperties r = Compute(content, "r").CustomProperties!;

        // Inherited from g; !important rule beats the style attribute; initial is the guaranteed-invalid value; names
        // are case-sensitive.
        Assert.True(r.TryGetValue("--a", out a));
        Assert.Equal("style", a);
        Assert.True(r.TryGetValue("--b", out string? b));
        Assert.Equal("rule2", b);
        Assert.True(r.TryGetValue("--d", out string? d));
        Assert.Null(d);
        Assert.True(r.TryGetValue("--A", out string? upper));
        Assert.Equal("case", upper);
        Assert.False(r.TryGetValue("--missing", out _));

        // unset and inherit take the parent value; an element that changes nothing shares the map of its parent.
        SvgElementTree tree = Read(content);
        SvgResolver resolver = new(tree, new SvgLoadOptions());
        SvgComputedStyle parent = resolver.ComputeDocumentStyle(Index(tree, "g"));
        SvgComputedStyle unset = resolver.ComputeChildStyle(Index(tree, "u"), in parent);
        Assert.Same(parent.CustomProperties, unset.CustomProperties);
    }

    // A declaration with var() is valid until substitution, which happens at computed-value time: the specified style
    // keeps the pending text over the value of next lower precedence, and the computed style has the substituted value.
    [Fact]
    public void VarDeclaration_IsPendingUntilComputed()
    {
        const string content = """
            <style>#r { fill: blue }</style>
            <rect id="r" fill="red" style="--c: green; fill: var(--c); stroke: VAR(--c, black)"/>
            <rect id="s" style="fill: var(--c); fill: yellow"/>
            """;

        SvgElementTree tree = Read(content);
        ref readonly SvgSpecifiedStyle r = ref tree.GetStyle(Index(tree, "r"));
        Assert.True(r.IsPending(SvgPropertyId.Fill));
        Assert.True(r.IsPending(SvgPropertyId.Stroke));
        Assert.True(r.Extended!.Variables!.TryGetPending(SvgPropertyId.Fill, out SvgPendingValue fill));
        Assert.Equal("var(--c)", fill.Text);
        Assert.Null(fill.Shorthand);
        Assert.True(r.HasValue(SvgPropertyId.Fill));
        Assert.Equal(new Rgba32(0, 0, 255, 255), r.Fill.Color.ToPixel<Rgba32>());
        Assert.Equal(new Rgba32(0, 128, 0, 255), Compute(content, "r").Fill.Color.ToPixel<Rgba32>());

        // A later declaration without var() wins.
        Assert.False(tree.GetStyle(Index(tree, "s")).IsPending(SvgPropertyId.Fill));
    }

    // Chromium accepts var() in presentation attributes, the geometry attributes included, but not in transform and d.
    [Fact]
    public void VarDeclaration_InPresentationAttributes()
    {
        SvgElementTree tree = Read("""<rect id="r" fill="var(--c)" x="var(--x)" width="10" height="10" transform="var(--t)"/>""");
        ref readonly SvgSpecifiedStyle style = ref tree.GetStyle(Index(tree, "r"));

        Assert.True(style.IsPending(SvgPropertyId.Fill));
        Assert.True(style.IsPending(SvgPropertyId.X));
        Assert.False(style.IsPending(SvgPropertyId.Width));
        Assert.False(style.IsSpecified(SvgPropertyId.Transform));
        Assert.False(tree.GetRect(Index(tree, "r")).X.IsSpecified);
    }

    [Fact]
    public void VarDeclaration_CascadeOrder()
    {
        const string content = """
            <style>#a { fill: var(--rule) } #b { fill: var(--rule) !important }</style>
            <rect id="a" fill="var(--attribute)" style="fill: red"/>
            <rect id="b" style="fill: var(--style)"/>
            <rect id="c" fill="red" style="fill: var(--style)"/>
            """;

        SvgElementTree tree = Read(content);

        // The style attribute without var() beats the rule and the attribute with var().
        Assert.False(tree.GetStyle(Index(tree, "a")).IsPending(SvgPropertyId.Fill));
        Assert.True(tree.GetStyle(Index(tree, "a")).HasValue(SvgPropertyId.Fill));

        // The important rule beats the normal style attribute.
        Assert.True(tree.GetStyle(Index(tree, "b")).Extended!.Variables!.TryGetPending(SvgPropertyId.Fill, out SvgPendingValue b));
        Assert.Equal("var(--rule)", b.Text);

        // The style attribute beats the presentation attribute, whose value stays underneath.
        ref readonly SvgSpecifiedStyle c = ref tree.GetStyle(Index(tree, "c"));
        Assert.True(c.IsPending(SvgPropertyId.Fill));
        Assert.True(c.HasValue(SvgPropertyId.Fill));
    }

    // CSS: every longhand of a shorthand with var() gets the text of the shorthand.
    [Fact]
    public void VarDeclaration_InShorthands()
    {
        SvgSpecifiedStyle style = default;

        Assert.True(this.parser.TryParseShorthand(SvgShorthand.Marker, "var(--m)", ref style));
        Assert.True(this.parser.TryParse(SvgPropertyId.FontVariant, "var(--v)", ref style));
        Assert.True(this.parser.TryParse(SvgPropertyId.WhiteSpace, "var(--w) nowrap", ref style));
        SvgVariableStyle variables = style.Extended!.Variables!;

        Assert.True(variables.TryGetPending(SvgPropertyId.MarkerMid, out SvgPendingValue marker));
        Assert.Equal(SvgShorthand.Marker, marker.Shorthand);
        Assert.True(variables.TryGetPending(SvgPropertyId.FontVariantNumeric, out SvgPendingValue numeric));
        Assert.Equal(SvgShorthand.FontVariant, numeric.Shorthand);
        Assert.True(variables.TryGetPending(SvgPropertyId.FontVariant, out SvgPendingValue variant));
        Assert.Null(variant.Shorthand);
        Assert.True(variables.TryGetPending(SvgPropertyId.WhiteSpaceCollapse, out SvgPendingValue collapse));
        Assert.Equal("var(--w) nowrap", collapse.Text);

        // A function whose name only ends in var is not var().
        Assert.False(SvgPropertyParser.ContainsVar("myvar(--x)"));
        Assert.True(SvgPropertyParser.ContainsVar("rgb(var(--r) 0 0)"));
    }

    [Fact]
    public void PathLength_IsStoredForPathsAndBasicShapes()
    {
        SvgElementTree tree = Read(
            """
            <path id="p" d="M0 0 h10" pathLength="100"/>
            <rect id="r" width="1" height="1" pathLength="-2"/>
            <circle id="c" r="1" pathLength="abc"/>
            <g id="g" pathLength="5"/>
            """);

        Assert.True(tree.TryGetPathLength(Index(tree, "p"), out float path));
        Assert.Equal(100F, path);
        Assert.True(tree.TryGetPathLength(Index(tree, "r"), out float negative));
        Assert.Equal(-2F, negative);
        Assert.False(tree.TryGetPathLength(Index(tree, "c"), out _));
        Assert.False(tree.TryGetPathLength(Index(tree, "g"), out _));
    }

    // Chromium matches :lang() against xml:lang before lang on each element, inherited from the ancestors.
    [Fact]
    public void Language_ComesFromXmlLangThenLang()
    {
        SvgElementTree tree = Read(
            """
            <g id="a" lang="de" xml:lang="fr"><rect id="b"/></g>
            <g id="c" lang="en-GB"><g id="d" xml:lang=""><rect id="e"/></g></g>
            <rect id="f"/>
            """);

        Assert.Equal("fr", tree.GetLanguage(Index(tree, "a")));
        Assert.Equal("fr", tree.GetLanguage(Index(tree, "b")));
        Assert.Equal("en-GB", tree.GetLanguage(Index(tree, "c")));
        Assert.Equal(string.Empty, tree.GetLanguage(Index(tree, "e")));
        Assert.Null(tree.GetLanguage(Index(tree, "f")));
    }

    // The properties that need no extension still need none: a document with only inline properties keeps the style
    // table at its old size (264 bytes per entry, see SvgSpecifiedStyle) and allocates no extension.
    [Fact]
    public void InlineProperties_NeedNoExtension()
    {
        SvgElementTree tree = Read("""<rect id="r" fill="red" stroke="blue" stroke-width="2" opacity="0.5"/>""");

        Assert.Null(tree.GetStyle(Index(tree, "r")).Extended);
        Assert.True(Unsafe.SizeOf<SvgSpecifiedStyle>() <= 264, $"{Unsafe.SizeOf<SvgSpecifiedStyle>()} bytes");
    }

    private static SvgLength Px(float value) => new(value, SvgLengthUnit.Px);

    private static SvgComputedStyle Compute(string content, string id)
    {
        SvgElementTree tree = Read(content);
        SvgResolver resolver = new(tree, new SvgLoadOptions());
        return resolver.ComputeDocumentStyle(Index(tree, id));
    }

    private static Image<Rgba32> Render(string content)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + content + "</svg>");
        Image<Rgba32> image = new(200, 100);
        image.Mutate(context => context.Paint(canvas => canvas.DrawSvg(document, new PointF(0, 0))));
        return image;
    }

    private static int Index(SvgElementTree tree, string id)
    {
        Assert.True(tree.TryGetElementById(id, out int index));
        return index;
    }

    private static SvgElementTree Read(string content) => SvgReader.Read(SvgOpen + content + "</svg>", SvgReaderOptions.Default);
}
