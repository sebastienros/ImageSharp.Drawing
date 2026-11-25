// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Parses style property values from presentation attributes, <c>style</c> attributes, and style sheets into
/// <see cref="SvgSpecifiedStyle"/>.
/// </summary>
/// <remarks>
/// One parser serves one document read. It owns the color cache and the dash length table that
/// <see cref="SvgDashArray"/> values point into, so values parsed from attributes and from style sheets share one table.
/// It is not thread-safe.
/// </remarks>
internal sealed class SvgPropertyParser
{
    private static readonly Dictionary<string, SvgPropertyId> PropertiesByName = CreatePropertiesByName();
    private static readonly Dictionary<string, SvgPropertyId>.AlternateLookup<ReadOnlySpan<char>> PropertyLookup =
        PropertiesByName.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>
    /// The <c>visibility</c> keywords in <see cref="SvgVisibility"/> order.
    /// </summary>
    private static readonly string[] VisibilityKeywords = ["visible", "hidden", "collapse"];

    /// <summary>
    /// The <c>fill-rule</c> keywords in <see cref="SvgFillRule"/> order.
    /// </summary>
    private static readonly string[] FillRuleKeywords = ["nonzero", "evenodd"];

    /// <summary>
    /// The <c>stroke-linecap</c> keywords in <see cref="SvgStrokeLineCap"/> order.
    /// </summary>
    private static readonly string[] LineCapKeywords = ["butt", "round", "square"];

    /// <summary>
    /// The <c>stroke-linejoin</c> keywords in <see cref="SvgStrokeLineJoin"/> order.
    /// </summary>
    private static readonly string[] LineJoinKeywords = ["miter", "miter-clip", "round", "bevel", "arcs"];

    /// <summary>
    /// The <c>display</c> keywords other than <c>none</c>, from CSS 2 and CSS Display Level 3. They all render an SVG
    /// element the same way.
    /// </summary>
    private static readonly string[] DisplayKeywords =
    [
        "inline", "block", "list-item", "run-in", "compact", "marker", "table", "inline-table", "table-row-group",
        "table-header-group", "table-footer-group", "table-row", "table-column-group", "table-column", "table-cell",
        "table-caption", "inline-block", "flex", "inline-flex", "grid", "inline-grid", "flow-root", "contents"
    ];

    /// <summary>
    /// The absolute-size keywords of <c>font-size</c>, in the order of <see cref="AbsoluteSizeScales"/>.
    /// </summary>
    private static readonly string[] AbsoluteSizeKeywords =
        ["xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large"];

    /// <summary>
    /// The scale of each absolute-size keyword relative to <c>medium</c>.
    /// </summary>
    private static readonly float[] AbsoluteSizeScales = [3F / 5F, 3F / 4F, 8F / 9F, 1F, 6F / 5F, 3F / 2F, 2F, 3F];

    private readonly SvgColorCache colors = new();

    /// <summary>
    /// Gets the dash length table. <see cref="SvgDashArray"/> values index into it.
    /// </summary>
    public List<SvgLength> DashLengths { get; } = [];

    /// <summary>
    /// Gets the property names in their canonical lowercase form, for pre-seeding the XML name table.
    /// </summary>
    public static IEnumerable<string> PropertyNames => PropertiesByName.Keys;

    /// <summary>
    /// Looks up a property by its CSS name. CSS property names are ASCII case-insensitive.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="property">When this method returns <see langword="true"/>, contains the property.</param>
    /// <returns><see langword="true"/> if the property is known; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetProperty(ReadOnlySpan<char> name, out SvgPropertyId property)
        => PropertyLookup.TryGetValue(name, out property);

    /// <summary>
    /// Parses a property value and stores it in <paramref name="style"/>. An invalid value leaves the style unchanged, as
    /// CSS drops an invalid declaration and SVG treats an invalid presentation attribute as unspecified.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="source">The value characters, without <c>!important</c>.</param>
    /// <param name="style">The style that receives the value.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    public bool TryParse(SvgPropertyId property, ReadOnlySpan<char> source, ref SvgSpecifiedStyle style)
    {
        source = SvgNumberParser.Trim(source);

        // The CSS-wide keywords apply to every property.
        if (source.Equals("inherit", StringComparison.OrdinalIgnoreCase))
        {
            style.MarkInherit(property);
            return true;
        }

        if (source.Equals("initial", StringComparison.OrdinalIgnoreCase))
        {
            style.MarkInitial(property);
            return true;
        }

        if (source.Equals("unset", StringComparison.OrdinalIgnoreCase))
        {
            if (SvgSpecifiedStyle.IsInherited(property))
            {
                style.MarkInherit(property);
            }
            else
            {
                style.MarkInitial(property);
            }

            return true;
        }

        switch (property)
        {
            case SvgPropertyId.Color:
                // CSS Color Level 4: currentColor as the value of color is treated as inherit.
                if (source.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
                {
                    style.MarkInherit(property);
                    return true;
                }

                if (!SvgColorParser.TryParse(source, this.colors, out Color color))
                {
                    return false;
                }

                style.Color = color;
                break;
            case SvgPropertyId.Display:
                if (!TryParseDisplay(source, out SvgDisplay display))
                {
                    return false;
                }

                style.Display = display;
                break;
            case SvgPropertyId.Visibility:
                if (!TryParseKeyword(source, VisibilityKeywords, out int visibility))
                {
                    return false;
                }

                style.Visibility = (SvgVisibility)visibility;
                break;
            case SvgPropertyId.Opacity:
            case SvgPropertyId.FillOpacity:
            case SvgPropertyId.StrokeOpacity:
            case SvgPropertyId.StopOpacity:
                if (!SvgNumberParser.TryParseClampedFraction(source, out float opacity))
                {
                    return false;
                }

                SetOpacity(ref style, property, opacity);
                break;
            case SvgPropertyId.Fill:
            case SvgPropertyId.Stroke:
                if (!SvgPaint.TryParse(source, this.colors, out SvgPaint paint))
                {
                    return false;
                }

                if (property == SvgPropertyId.Fill)
                {
                    style.Fill = paint;
                }
                else
                {
                    style.Stroke = paint;
                }

                break;
            case SvgPropertyId.FillRule:
                if (!TryParseKeyword(source, FillRuleKeywords, out int fillRule))
                {
                    return false;
                }

                style.FillRule = (SvgFillRule)fillRule;
                break;
            case SvgPropertyId.StrokeWidth:
                if (!SvgLength.TryParseNonNegative(source, out SvgLength width))
                {
                    return false;
                }

                style.StrokeWidth = width;
                break;
            case SvgPropertyId.StrokeLineCap:
                if (!TryParseKeyword(source, LineCapKeywords, out int lineCap))
                {
                    return false;
                }

                style.StrokeLineCap = (SvgStrokeLineCap)lineCap;
                break;
            case SvgPropertyId.StrokeLineJoin:
                if (!TryParseKeyword(source, LineJoinKeywords, out int lineJoin))
                {
                    return false;
                }

                style.StrokeLineJoin = (SvgStrokeLineJoin)lineJoin;
                break;
            case SvgPropertyId.StrokeMiterLimit:
                // SVG requires a miter limit of at least 1; any other value is an error.
                if (!SvgNumberParser.TryParseNumber(source, out float miterLimit) || miterLimit < 1F)
                {
                    return false;
                }

                style.StrokeMiterLimit = miterLimit;
                break;
            case SvgPropertyId.StrokeDashArray:
                if (source.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    style.StrokeDashArray = default;
                    break;
                }

                // SVG makes a list with a negative value invalid, so the declaration is dropped.
                int start = this.DashLengths.Count;
                if (!SvgLength.TryParseNonNegativeList(source, this.DashLengths))
                {
                    return false;
                }

                style.StrokeDashArray = new SvgDashArray(start, this.DashLengths.Count - start);
                break;
            case SvgPropertyId.StrokeDashOffset:
                if (!SvgLength.TryParse(source, out SvgLength offset))
                {
                    return false;
                }

                style.StrokeDashOffset = offset;
                break;
            case SvgPropertyId.FontSize:
                if (!TryParseFontSize(source, out SvgLength fontSize))
                {
                    return false;
                }

                style.FontSize = fontSize;
                break;
            case SvgPropertyId.StopColor:
                if (!SvgPaint.TryParseColorOrCurrentColor(source, this.colors, out SvgPaint stopColor))
                {
                    return false;
                }

                style.StopColor = stopColor;
                break;
            default:
                return false;
        }

        style.MarkValue(property);
        return true;
    }

    /// <summary>
    /// Stores an opacity value in the field of its property.
    /// </summary>
    /// <param name="style">The style that receives the value.</param>
    /// <param name="property">One of the four opacity properties.</param>
    /// <param name="value">The clamped opacity.</param>
    private static void SetOpacity(ref SvgSpecifiedStyle style, SvgPropertyId property, float value)
    {
        switch (property)
        {
            case SvgPropertyId.Opacity:
                style.Opacity = value;
                break;
            case SvgPropertyId.FillOpacity:
                style.FillOpacity = value;
                break;
            case SvgPropertyId.StrokeOpacity:
                style.StrokeOpacity = value;
                break;
            default:
                style.StopOpacity = value;
                break;
        }
    }

    /// <summary>
    /// Parses a <c>display</c> value.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the value.</param>
    /// <returns><see langword="true"/> if the value is a known display keyword; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseDisplay(ReadOnlySpan<char> source, out SvgDisplay value)
    {
        if (source.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            value = SvgDisplay.None;
            return true;
        }

        value = SvgDisplay.Inline;
        return TryParseKeyword(source, DisplayKeywords, out _);
    }

    /// <summary>
    /// Parses a <c>font-size</c> value: a non-negative length or percentage, an absolute-size keyword, or a relative-size
    /// keyword.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the size.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseFontSize(ReadOnlySpan<char> source, out SvgLength value)
    {
        if (SvgLength.TryParseNonNegative(source, out value))
        {
            return true;
        }

        // CSS Fonts Level 4, section 2.5: the absolute-size keywords scale the medium size of 16px by
        // 3/5, 3/4, 8/9, 1, 6/5, 3/2, 2, and 3.
        if (TryParseKeyword(source, AbsoluteSizeKeywords, out int index))
        {
            value = new SvgLength(16F * AbsoluteSizeScales[index], SvgLengthUnit.Px);
            return true;
        }

        // The relative-size keywords scale the parent font size. CSS leaves the ratio to the user agent; 1.2 is the
        // common choice, so they are stored as percentages of the parent size.
        if (source.Equals("larger", StringComparison.OrdinalIgnoreCase))
        {
            value = new SvgLength(120F, SvgLengthUnit.Percent);
            return true;
        }

        if (source.Equals("smaller", StringComparison.OrdinalIgnoreCase))
        {
            value = new SvgLength(100F / 1.2F, SvgLengthUnit.Percent);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Matches a value against a keyword list, ignoring ASCII case.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="keywords">The keywords, in the order of the enum they map to.</param>
    /// <param name="index">When this method returns <see langword="true"/>, contains the index of the keyword.</param>
    /// <returns><see langword="true"/> if the value is one of the keywords; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseKeyword(ReadOnlySpan<char> source, string[] keywords, out int index)
    {
        for (index = 0; index < keywords.Length; index++)
        {
            if (source.Equals(keywords[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        index = 0;
        return false;
    }

    /// <summary>
    /// Creates the case-insensitive map from property name to property.
    /// </summary>
    /// <returns>The map.</returns>
    private static Dictionary<string, SvgPropertyId> CreatePropertiesByName()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [SvgNames.Color] = SvgPropertyId.Color,
            [SvgNames.Display] = SvgPropertyId.Display,
            [SvgNames.Visibility] = SvgPropertyId.Visibility,
            [SvgNames.Opacity] = SvgPropertyId.Opacity,
            [SvgNames.Fill] = SvgPropertyId.Fill,
            [SvgNames.FillOpacity] = SvgPropertyId.FillOpacity,
            [SvgNames.FillRule] = SvgPropertyId.FillRule,
            [SvgNames.Stroke] = SvgPropertyId.Stroke,
            [SvgNames.StrokeOpacity] = SvgPropertyId.StrokeOpacity,
            [SvgNames.StrokeWidth] = SvgPropertyId.StrokeWidth,
            [SvgNames.StrokeLineCap] = SvgPropertyId.StrokeLineCap,
            [SvgNames.StrokeLineJoin] = SvgPropertyId.StrokeLineJoin,
            [SvgNames.StrokeMiterLimit] = SvgPropertyId.StrokeMiterLimit,
            [SvgNames.StrokeDashArray] = SvgPropertyId.StrokeDashArray,
            [SvgNames.StrokeDashOffset] = SvgPropertyId.StrokeDashOffset,
            [SvgNames.FontSize] = SvgPropertyId.FontSize,
            [SvgNames.StopColor] = SvgPropertyId.StopColor,
            [SvgNames.StopOpacity] = SvgPropertyId.StopOpacity,
        };
}
