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

    /// <summary>
    /// The <c>mask-type</c> keywords in <see cref="SvgMaskType"/> order.
    /// </summary>
    private static readonly string[] MaskTypeKeywords = ["luminance", "alpha"];

    /// <summary>
    /// The <c>mix-blend-mode</c> keywords in <see cref="SvgBlendMode"/> order.
    /// </summary>
    private static readonly string[] BlendModeKeywords =
    [
        "normal", "multiply", "screen", "overlay", "darken", "lighten", "color-dodge", "color-burn", "hard-light",
        "soft-light", "difference", "exclusion", "hue", "saturation", "color", "luminosity"
    ];

    /// <summary>
    /// The <c>isolation</c> keywords in <see cref="SvgIsolation"/> order.
    /// </summary>
    private static readonly string[] IsolationKeywords = ["auto", "isolate"];

    /// <summary>
    /// The <c>vector-effect</c> keywords in <see cref="SvgVectorEffect"/> order.
    /// </summary>
    private static readonly string[] VectorEffectKeywords = ["none", "non-scaling-stroke"];

    /// <summary>
    /// The <c>overflow</c> keywords in <see cref="SvgOverflow"/> order.
    /// </summary>
    private static readonly string[] OverflowKeywords = ["visible", "hidden", "scroll", "auto"];

    /// <summary>
    /// The <c>image-rendering</c> keywords in <see cref="SvgImageRendering"/> order.
    /// </summary>
    private static readonly string[] ImageRenderingKeywords =
        ["auto", "optimizeQuality", "optimizeSpeed", "pixelated", "crisp-edges", "smooth", "high-quality"];

    /// <summary>
    /// The <c>shape-rendering</c> keywords in <see cref="SvgShapeRendering"/> order.
    /// </summary>
    private static readonly string[] ShapeRenderingKeywords = ["auto", "optimizeSpeed", "crispEdges", "geometricPrecision"];

    /// <summary>
    /// The <c>color-interpolation</c> keywords in <see cref="SvgColorInterpolation"/> order.
    /// </summary>
    private static readonly string[] ColorInterpolationKeywords = ["sRGB", "auto", "linearRGB"];

    private readonly SvgColorCache colors = new();

    /// <summary>
    /// Gets the dash length table. <see cref="SvgDashArray"/> values index into it.
    /// </summary>
    public List<SvgLength> DashLengths { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the parser has seen a declaration of the CSS <c>transform</c> property, valid or
    /// not. When it has not, the reader skips the pass that applies the property to the transform table.
    /// </summary>
    public bool HasTransformDeclarations { get; private set; }

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
        this.HasTransformDeclarations |= property == SvgPropertyId.Transform;

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
                if (!TryParseExtended(property, source, ref style))
                {
                    return false;
                }

                break;
        }

        style.MarkValue(property);
        return true;
    }

    /// <summary>
    /// Returns whether a CSS declaration name is the <c>marker</c> shorthand. The shorthand exists only in CSS; SVG does
    /// not define a <c>marker</c> presentation attribute.
    /// </summary>
    /// <param name="name">The declaration name.</param>
    /// <returns><see langword="true"/> if the name is <c>marker</c>, ignoring ASCII case; otherwise, <see langword="false"/>.</returns>
    public static bool IsMarkerShorthand(ReadOnlySpan<char> name) => name.Equals("marker", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses the value of the CSS <c>marker</c> shorthand and stores it in <c>marker-start</c>, <c>marker-mid</c>, and
    /// <c>marker-end</c>, as CSS expands a shorthand into its longhands. An invalid value leaves the style unchanged.
    /// </summary>
    /// <param name="source">The value characters, without <c>!important</c>.</param>
    /// <param name="style">The style that receives the values.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    public bool TryParseMarkerShorthand(ReadOnlySpan<char> source, ref SvgSpecifiedStyle style)
    {
        // The value has the grammar of one longhand, so parsing it once validates it for all three. The CSS-wide
        // keywords apply to each longhand.
        SvgSpecifiedStyle parsed = default;
        if (!this.TryParse(SvgPropertyId.MarkerStart, source, ref parsed))
        {
            return false;
        }

        _ = this.TryParse(SvgPropertyId.MarkerStart, source, ref style);
        _ = this.TryParse(SvgPropertyId.MarkerMid, source, ref style);
        _ = this.TryParse(SvgPropertyId.MarkerEnd, source, ref style);
        return true;
    }

    /// <summary>
    /// Parses the value of a property from <see cref="SvgPropertyId.ClipPath"/> on and stores it in the extension of the
    /// style. The caller marks the property.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="source">The trimmed value, which is not a CSS-wide keyword.</param>
    /// <param name="style">The style that receives the value.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseExtended(SvgPropertyId property, ReadOnlySpan<char> source, ref SvgSpecifiedStyle style)
    {
        // Parse first and create the extension only for a valid value, so an invalid declaration allocates nothing.
        int keyword;
        switch (property)
        {
            case SvgPropertyId.ClipPath:
            case SvgPropertyId.Mask:
            case SvgPropertyId.MarkerStart:
            case SvgPropertyId.MarkerMid:
            case SvgPropertyId.MarkerEnd:
                if (!SvgReference.TryParse(source, out SvgReference reference))
                {
                    return false;
                }

                SetReference(style.GetOrCreateExtended(), property, reference);
                return true;
            case SvgPropertyId.ClipRule:
                if (!TryParseKeyword(source, FillRuleKeywords, out keyword))
                {
                    return false;
                }

                style.GetOrCreateExtended().ClipRule = (SvgFillRule)keyword;
                return true;
            case SvgPropertyId.MaskType:
                if (!TryParseKeyword(source, MaskTypeKeywords, out keyword))
                {
                    return false;
                }

                style.GetOrCreateExtended().MaskType = (SvgMaskType)keyword;
                return true;
            case SvgPropertyId.MixBlendMode:
                if (!TryParseKeyword(source, BlendModeKeywords, out keyword))
                {
                    return false;
                }

                style.GetOrCreateExtended().MixBlendMode = (SvgBlendMode)keyword;
                return true;
            case SvgPropertyId.Isolation:
                if (!TryParseKeyword(source, IsolationKeywords, out keyword))
                {
                    return false;
                }

                style.GetOrCreateExtended().Isolation = (SvgIsolation)keyword;
                return true;
            case SvgPropertyId.PaintOrder:
                if (!TryParsePaintOrder(source, out SvgPaintOrder paintOrder))
                {
                    return false;
                }

                style.GetOrCreateExtended().PaintOrder = paintOrder;
                return true;
            case SvgPropertyId.VectorEffect:
                if (!TryParseKeyword(source, VectorEffectKeywords, out keyword))
                {
                    return false;
                }

                style.GetOrCreateExtended().VectorEffect = (SvgVectorEffect)keyword;
                return true;
            case SvgPropertyId.Overflow:
                if (!TryParseKeyword(source, OverflowKeywords, out keyword))
                {
                    return false;
                }

                style.GetOrCreateExtended().Overflow = (SvgOverflow)keyword;
                return true;
            case SvgPropertyId.ImageRendering:
                if (!TryParseKeyword(source, ImageRenderingKeywords, out keyword))
                {
                    return false;
                }

                style.GetOrCreateExtended().ImageRendering = (SvgImageRendering)keyword;
                return true;
            case SvgPropertyId.ShapeRendering:
                if (!TryParseKeyword(source, ShapeRenderingKeywords, out keyword))
                {
                    return false;
                }

                style.GetOrCreateExtended().ShapeRendering = (SvgShapeRendering)keyword;
                return true;
            case SvgPropertyId.ColorInterpolation:
                if (!TryParseKeyword(source, ColorInterpolationKeywords, out keyword))
                {
                    return false;
                }

                style.GetOrCreateExtended().ColorInterpolation = (SvgColorInterpolation)keyword;
                return true;
            case SvgPropertyId.TransformOrigin:
                if (!TryParseTransformOrigin(source, out SvgLength originX, out SvgLength originY))
                {
                    return false;
                }

                SvgExtendedStyle extended = style.GetOrCreateExtended();
                extended.TransformOriginX = originX;
                extended.TransformOriginY = originY;
                return true;
            case SvgPropertyId.Transform:
                if (!SvgTransformParser.TryParseCss(source, out System.Numerics.Matrix3x2 transform))
                {
                    return false;
                }

                style.GetOrCreateExtended().Transform = transform;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Stores a reference value in the field of its property.
    /// </summary>
    /// <param name="extended">The extension that receives the value.</param>
    /// <param name="property">One of the five reference properties.</param>
    /// <param name="value">The reference.</param>
    private static void SetReference(SvgExtendedStyle extended, SvgPropertyId property, SvgReference value)
    {
        switch (property)
        {
            case SvgPropertyId.ClipPath:
                extended.ClipPath = value;
                break;
            case SvgPropertyId.Mask:
                extended.Mask = value;
                break;
            case SvgPropertyId.MarkerStart:
                extended.MarkerStart = value;
                break;
            case SvgPropertyId.MarkerMid:
                extended.MarkerMid = value;
                break;
            default:
                extended.MarkerEnd = value;
                break;
        }
    }

    /// <summary>
    /// Parses a <c>paint-order</c> value: <c>normal</c>, or one to three of <c>fill</c>, <c>stroke</c>, and
    /// <c>markers</c>, each at most once, in any order.
    /// </summary>
    /// <remarks>
    /// The layers that the value omits follow the listed ones in the order fill, stroke, markers. The order is built as
    /// three 2-bit layer codes (fill 0, stroke 1, markers 2) and then mapped to the enum member.
    /// </remarks>
    /// <param name="source">The trimmed value.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the order.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParsePaintOrder(ReadOnlySpan<char> source, out SvgPaintOrder value)
    {
        value = SvgPaintOrder.FillStrokeMarkers;
        if (source.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Span<int> order = stackalloc int[3];
        int count = 0;
        int seen = 0;
        ReadOnlySpan<char> rest = source;
        while (!rest.IsEmpty)
        {
            int length = 0;
            while (length < rest.Length && !SvgNumberParser.IsWhitespace(rest[length]))
            {
                length++;
            }

            ReadOnlySpan<char> word = rest[..length];
            rest = SvgNumberParser.SkipWhitespace(rest[length..]);
            int layer;
            if (word.Equals("fill", StringComparison.OrdinalIgnoreCase))
            {
                layer = 0;
            }
            else if (word.Equals("stroke", StringComparison.OrdinalIgnoreCase))
            {
                layer = 1;
            }
            else if (word.Equals("markers", StringComparison.OrdinalIgnoreCase))
            {
                layer = 2;
            }
            else
            {
                return false;
            }

            // A layer listed twice makes the value invalid.
            if ((seen & (1 << layer)) != 0)
            {
                return false;
            }

            seen |= 1 << layer;
            order[count++] = layer;
        }

        for (int layer = 0; layer < 3 && count < 3; layer++)
        {
            if ((seen & (1 << layer)) == 0)
            {
                order[count++] = layer;
            }
        }

        value = (order[0], order[1]) switch
        {
            (0, 1) => SvgPaintOrder.FillStrokeMarkers,
            (0, _) => SvgPaintOrder.FillMarkersStroke,
            (1, 0) => SvgPaintOrder.StrokeFillMarkers,
            (1, _) => SvgPaintOrder.StrokeMarkersFill,
            (_, 0) => SvgPaintOrder.MarkersFillStroke,
            _ => SvgPaintOrder.MarkersStrokeFill
        };

        return true;
    }

    /// <summary>
    /// Parses a <c>transform-origin</c> value: one, two, or three components, as CSS Transforms Level 1 defines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One component is a horizontal keyword or a length, with the vertical position at the center, or a vertical
    /// keyword with the horizontal position at the center. With two components, two keywords can come in either order,
    /// and otherwise the first is horizontal and the second vertical. A third component is the z position, a length that
    /// is not a percentage; it has no effect in two dimensions and is checked and dropped.
    /// </para>
    /// <para>
    /// Keywords become percentages: left and top 0%, center 50%, right and bottom 100%. As for other SVG lengths, a
    /// number without a unit is accepted as user units.
    /// </para>
    /// </remarks>
    /// <param name="source">The trimmed value.</param>
    /// <param name="x">When this method returns <see langword="true"/>, contains the horizontal position.</param>
    /// <param name="y">When this method returns <see langword="true"/>, contains the vertical position.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseTransformOrigin(ReadOnlySpan<char> source, out SvgLength x, out SvgLength y)
    {
        SvgLength center = new(50F, SvgLengthUnit.Percent);
        x = center;
        y = center;

        // Each component is a keyword or a length. Keyword codes: 1 left, 2 right, 3 top, 4 bottom, 5 center; 0 is a length.
        Span<int> keywords = stackalloc int[3];
        Span<SvgLength> lengths = stackalloc SvgLength[3];
        int count = 0;
        ReadOnlySpan<char> rest = source;
        while (!rest.IsEmpty)
        {
            if (count == 3)
            {
                return false;
            }

            int length = 0;
            while (length < rest.Length && !SvgNumberParser.IsWhitespace(rest[length]))
            {
                length++;
            }

            ReadOnlySpan<char> word = rest[..length];
            rest = SvgNumberParser.SkipWhitespace(rest[length..]);
            keywords[count] = word switch
            {
                _ when word.Equals("left", StringComparison.OrdinalIgnoreCase) => 1,
                _ when word.Equals("right", StringComparison.OrdinalIgnoreCase) => 2,
                _ when word.Equals("top", StringComparison.OrdinalIgnoreCase) => 3,
                _ when word.Equals("bottom", StringComparison.OrdinalIgnoreCase) => 4,
                _ when word.Equals("center", StringComparison.OrdinalIgnoreCase) => 5,
                _ => 0
            };

            if (keywords[count] == 0 && !SvgLength.TryParse(word, out lengths[count]))
            {
                return false;
            }

            count++;
        }

        if (count == 0)
        {
            return false;
        }

        // The z component must be a length that is not a percentage.
        if (count == 3 && (keywords[2] != 0 || lengths[2].Unit == SvgLengthUnit.Percent))
        {
            return false;
        }

        if (count == 1)
        {
            switch (keywords[0])
            {
                case 0:
                    x = lengths[0];
                    return true;
                case 3:
                case 4:
                    y = KeywordPosition(keywords[0]);
                    return true;
                default:
                    x = KeywordPosition(keywords[0]);
                    return true;
            }
        }

        int first = keywords[0];
        int second = keywords[1];

        // Two keywords may come in either order: a vertical keyword first, or a horizontal keyword second, swaps them.
        if (first != 0 && second != 0 && (first is 3 or 4 || second is 1 or 2))
        {
            (first, second) = (second, first);
        }

        // The first component is horizontal and the second vertical.
        if (first is 3 or 4 || second is 1 or 2)
        {
            return false;
        }

        x = first == 0 ? lengths[0] : KeywordPosition(first);
        y = second == 0 ? lengths[1] : KeywordPosition(second);
        return true;
    }

    /// <summary>
    /// Returns the percentage of a <c>transform-origin</c> keyword.
    /// </summary>
    /// <param name="keyword">The keyword code: 1 left, 2 right, 3 top, 4 bottom, 5 center.</param>
    /// <returns>The position as a percentage.</returns>
    private static SvgLength KeywordPosition(int keyword)
        => new(
            keyword switch
            {
                1 or 3 => 0F,
                2 or 4 => 100F,
                _ => 50F
            },
            SvgLengthUnit.Percent);

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
            ["clip-path"] = SvgPropertyId.ClipPath,
            ["clip-rule"] = SvgPropertyId.ClipRule,
            ["mask"] = SvgPropertyId.Mask,
            ["mask-type"] = SvgPropertyId.MaskType,
            ["mix-blend-mode"] = SvgPropertyId.MixBlendMode,
            ["isolation"] = SvgPropertyId.Isolation,
            ["paint-order"] = SvgPropertyId.PaintOrder,
            ["vector-effect"] = SvgPropertyId.VectorEffect,
            ["overflow"] = SvgPropertyId.Overflow,
            ["marker-start"] = SvgPropertyId.MarkerStart,
            ["marker-mid"] = SvgPropertyId.MarkerMid,
            ["marker-end"] = SvgPropertyId.MarkerEnd,
            ["image-rendering"] = SvgPropertyId.ImageRendering,
            ["shape-rendering"] = SvgPropertyId.ShapeRendering,
            ["color-interpolation"] = SvgPropertyId.ColorInterpolation,
            ["transform-origin"] = SvgPropertyId.TransformOrigin,
            [SvgNames.Transform] = SvgPropertyId.Transform,
        };
}
