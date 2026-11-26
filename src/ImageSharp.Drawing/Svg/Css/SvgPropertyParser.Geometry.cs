// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The geometry properties, <c>transform-box</c>, and the CSS basic shapes of <c>clip-path</c>.
/// </content>
internal sealed partial class SvgPropertyParser
{
    /// <summary>
    /// The <c>transform-box</c> keywords in <see cref="SvgTransformBox"/> order.
    /// </summary>
    private static readonly string[] TransformBoxKeywords = ["content-box", "border-box", "fill-box", "stroke-box", "view-box"];

    /// <summary>
    /// The <c>&lt;geometry-box&gt;</c> keywords in <see cref="SvgGeometryBox"/> order, from index 1.
    /// </summary>
    private static readonly string[] GeometryBoxKeywords =
        [string.Empty, "fill-box", "stroke-box", "view-box", "content-box", "padding-box", "border-box", "margin-box"];

    /// <summary>
    /// Parses the value of a geometry property or of <c>transform-box</c> and stores it in the geometry values of the
    /// style. The caller marks the property.
    /// </summary>
    /// <remarks>
    /// <c>x</c>, <c>y</c>, <c>cx</c>, and <c>cy</c> take any length or percentage; <c>r</c> takes a non-negative one;
    /// <c>width</c>, <c>height</c>, <c>rx</c>, and <c>ry</c> take a non-negative one or <c>auto</c>. A number without a unit
    /// is a length in user units, which Chromium accepts in these properties. <c>d</c> takes <c>none</c> or
    /// <c>path(&lt;string&gt;)</c>.
    /// </remarks>
    /// <param name="property">A property for which <see cref="SvgGeometryStyle.IsGeometryProperty(SvgPropertyId)"/> is true.</param>
    /// <param name="source">The trimmed value, which is not a CSS-wide keyword.</param>
    /// <param name="style">The style that receives the value.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseGeometry(SvgPropertyId property, ReadOnlySpan<char> source, ref SvgSpecifiedStyle style)
    {
        // Parse first and create the geometry values only for a valid value, so an invalid declaration allocates nothing.
        switch (property)
        {
            case SvgPropertyId.X:
            case SvgPropertyId.Y:
            case SvgPropertyId.Cx:
            case SvgPropertyId.Cy:
                if (!SvgLength.TryParse(source, out SvgLength position))
                {
                    return false;
                }

                style.GetOrCreateExtended().GetOrCreateGeometry().SetLength(property, position);
                return true;
            case SvgPropertyId.R:
                if (!SvgLength.TryParseNonNegative(source, out SvgLength radius))
                {
                    return false;
                }

                style.GetOrCreateExtended().GetOrCreateGeometry().SetLength(property, radius);
                return true;
            case SvgPropertyId.Width:
            case SvgPropertyId.Height:
            case SvgPropertyId.Rx:
            case SvgPropertyId.Ry:
                // auto is stored as an unspecified length.
                SvgLength size = default;
                if (!source.Equals("auto", StringComparison.OrdinalIgnoreCase) && !SvgLength.TryParseNonNegative(source, out size))
                {
                    return false;
                }

                style.GetOrCreateExtended().GetOrCreateGeometry().SetLength(property, size);
                return true;
            case SvgPropertyId.D:
                // The d property takes path(<string>) without a fill rule; Chromium drops path(evenodd, ...) here.
                IPath? path = null;
                if (!source.Equals("none", StringComparison.OrdinalIgnoreCase)
                    && (!TryParsePathFunction(source, out _, out path) || SvgNumberParser.Trim(source[5..])[0] is not ('"' or '\'')))
                {
                    return false;
                }

                style.GetOrCreateExtended().GetOrCreateGeometry().D = path;
                return true;
            case SvgPropertyId.TransformBox:
                if (!TryParseKeyword(source, TransformBoxKeywords, out int box))
                {
                    return false;
                }

                style.GetOrCreateExtended().GetOrCreateGeometry().TransformBox = (SvgTransformBox)box;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Parses a <c>clip-path</c> value that is not <c>none</c> or a URL: <c>&lt;basic-shape&gt; || &lt;geometry-box&gt;</c>
    /// (CSS Masking Level 1), with the basic shapes <c>inset()</c>, <c>circle()</c>, <c>ellipse()</c>,
    /// <c>polygon()</c>, and <c>path()</c> of CSS Shapes Level 1 and <c>xywh()</c> and <c>rect()</c> of Level 2, which
    /// Chromium accepts. <c>shape()</c>, which Chromium also accepts, is not parsed.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="shape">When this method returns <see langword="true"/>, contains the shape.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    internal static bool TryParseClipShape(ReadOnlySpan<char> source, out SvgClipShape? shape)
    {
        shape = null;
        SvgGeometryBox box = SvgGeometryBox.Unspecified;
        ReadOnlySpan<char> function = default;
        ReadOnlySpan<char> rest = source;
        while (!rest.IsEmpty)
        {
            ReadOnlySpan<char> word = ReadWord(rest, out int length);
            rest = SvgNumberParser.SkipWhitespace(rest[length..]);
            if (TryParseKeyword(word, GeometryBoxKeywords, out int keyword) && keyword > 0)
            {
                if (box != SvgGeometryBox.Unspecified)
                {
                    return false;
                }

                box = (SvgGeometryBox)keyword;
            }
            else if (function.IsEmpty && word.IndexOf('(') > 0)
            {
                function = word;
            }
            else
            {
                return false;
            }
        }

        if (function.IsEmpty)
        {
            if (box == SvgGeometryBox.Unspecified)
            {
                return false;
            }

            shape = new SvgClipShape(SvgBasicShapeKind.None, box, []);
            return true;
        }

        if (function[^1] != ')')
        {
            return false;
        }

        int open = function.IndexOf('(');
        ReadOnlySpan<char> name = function[..open];
        ReadOnlySpan<char> arguments = SvgNumberParser.Trim(function[(open + 1)..^1]);
        if (name.Equals("inset", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRectangleShape(SvgBasicShapeKind.Inset, arguments, box, out shape);
        }

        if (name.Equals("xywh", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRectangleShape(SvgBasicShapeKind.Xywh, arguments, box, out shape);
        }

        if (name.Equals("rect", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRectangleShape(SvgBasicShapeKind.Rect, arguments, box, out shape);
        }

        if (name.Equals("circle", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseEllipseShape(SvgBasicShapeKind.Circle, arguments, box, out shape);
        }

        if (name.Equals("ellipse", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseEllipseShape(SvgBasicShapeKind.Ellipse, arguments, box, out shape);
        }

        if (name.Equals("polygon", StringComparison.OrdinalIgnoreCase))
        {
            return TryParsePolygonShape(arguments, box, out shape);
        }

        if (name.Equals("path", StringComparison.OrdinalIgnoreCase) && TryParsePathFunction(function, out SvgFillRule rule, out IPath? path))
        {
            shape = new SvgClipShape(SvgBasicShapeKind.Path, box, []) { Path = path, FillRule = rule };
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses <c>path([&lt;fill-rule&gt; ,]? &lt;string&gt;)</c>. The string must be complete path data: an error
    /// anywhere makes the value invalid, as Chromium treats <c>path()</c>.
    /// </summary>
    /// <param name="source">The function, from its name to its closing parenthesis.</param>
    /// <param name="rule">When this method returns <see langword="true"/>, contains the fill rule; <c>nonzero</c> when omitted.</param>
    /// <param name="path">When this method returns <see langword="true"/>, contains the path.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParsePathFunction(ReadOnlySpan<char> source, out SvgFillRule rule, out IPath? path)
    {
        rule = SvgFillRule.NonZero;
        path = null;
        if (source.Length < 7 || !source[..5].Equals("path(", StringComparison.OrdinalIgnoreCase) || source[^1] != ')')
        {
            return false;
        }

        ReadOnlySpan<char> arguments = SvgNumberParser.Trim(source[5..^1]);
        if (arguments.IsEmpty)
        {
            return false;
        }

        // An optional fill rule and a comma come before the string.
        if (arguments[0] is not ('"' or '\''))
        {
            int comma = arguments.IndexOf(',');
            if (comma < 0 || !TryParseKeyword(SvgNumberParser.Trim(arguments[..comma]), FillRuleKeywords, out int fillRule))
            {
                return false;
            }

            rule = (SvgFillRule)fillRule;
            arguments = SvgNumberParser.Trim(arguments[(comma + 1)..]);
        }

        // The rest is one quoted string; the CSS parser has decoded its escapes.
        if (arguments.Length < 2 || arguments[0] is not ('"' or '\'') || arguments[^1] != arguments[0])
        {
            return false;
        }

        ReadOnlySpan<char> data = arguments[1..^1];
        if (data.IndexOf(arguments[0]) >= 0 || !Path.TryParseSvgPathPrefix(data, out IPath parsed))
        {
            return false;
        }

        path = parsed;
        return true;
    }

    /// <summary>
    /// Parses the arguments of <c>inset()</c>, <c>xywh()</c>, or <c>rect()</c>: four lengths (fewer for
    /// <c>inset()</c>), then an optional <c>round</c> with corner radii.
    /// </summary>
    /// <param name="kind">The shape kind.</param>
    /// <param name="arguments">The trimmed arguments.</param>
    /// <param name="box">The geometry box of the value.</param>
    /// <param name="shape">When this method returns <see langword="true"/>, contains the shape.</param>
    /// <returns><see langword="true"/> if the arguments are valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseRectangleShape(SvgBasicShapeKind kind, ReadOnlySpan<char> arguments, SvgGeometryBox box, out SvgClipShape? shape)
    {
        shape = null;
        Span<SvgLength> values = stackalloc SvgLength[4];
        int count = 0;
        SvgLength[]? radii = null;
        ReadOnlySpan<char> rest = arguments;
        while (!rest.IsEmpty)
        {
            ReadOnlySpan<char> word = ReadWord(rest, out int length);
            rest = SvgNumberParser.SkipWhitespace(rest[length..]);
            if (word.Equals("round", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseBorderRadius(rest, out radii))
                {
                    return false;
                }

                break;
            }

            if (count == 4)
            {
                return false;
            }

            // rect() takes auto for any edge; xywh() takes non-negative sizes for its last two values.
            SvgLength value = default;
            bool valid = (kind == SvgBasicShapeKind.Rect && word.Equals("auto", StringComparison.OrdinalIgnoreCase))
                || (kind == SvgBasicShapeKind.Xywh && count >= 2 ? SvgLength.TryParseNonNegative(word, out value) : SvgLength.TryParse(word, out value));
            if (!valid)
            {
                return false;
            }

            values[count++] = value;
        }

        if (count == 0 || (kind != SvgBasicShapeKind.Inset && count != 4))
        {
            return false;
        }

        // inset() fills omitted sides as the CSS box edges do: top, right, bottom, left.
        SvgLength[] lengths = new SvgLength[4];
        FillBoxValues(values[..count], lengths);
        shape = new SvgClipShape(kind, box, lengths) { Radii = radii };
        return true;
    }

    /// <summary>
    /// Parses a <c>border-radius</c> value after <c>round</c>: one to four non-negative lengths, optionally a slash and one
    /// to four more for the vertical radii.
    /// </summary>
    /// <param name="source">The rest of the arguments after <c>round</c>.</param>
    /// <param name="radii">When this method returns <see langword="true"/>, contains the eight radii; see <see cref="SvgClipShape.Radii"/>.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseBorderRadius(ReadOnlySpan<char> source, out SvgLength[]? radii)
    {
        radii = null;
        Span<SvgLength> horizontal = stackalloc SvgLength[4];
        Span<SvgLength> vertical = stackalloc SvgLength[4];
        int horizontalCount = 0;
        int verticalCount = -1;
        ReadOnlySpan<char> rest = source;
        while (!rest.IsEmpty)
        {
            // A slash may stand alone or touch a length.
            if (rest[0] == '/')
            {
                if (verticalCount >= 0 || horizontalCount == 0)
                {
                    return false;
                }

                verticalCount = 0;
                rest = SvgNumberParser.SkipWhitespace(rest[1..]);
                continue;
            }

            // A math function is one value: its spaces and slashes are inside its parentheses.
            int length = 0;
            int depth = 0;
            while (length < rest.Length && (depth > 0 || (rest[length] != '/' && !SvgNumberParser.IsWhitespace(rest[length]))))
            {
                if (rest[length] == '(')
                {
                    depth++;
                }
                else if (rest[length] == ')')
                {
                    depth--;
                }

                length++;
            }

            if (!SvgLength.TryParseNonNegative(rest[..length], out SvgLength value))
            {
                return false;
            }

            if (verticalCount < 0)
            {
                if (horizontalCount == 4)
                {
                    return false;
                }

                horizontal[horizontalCount++] = value;
            }
            else
            {
                if (verticalCount == 4)
                {
                    return false;
                }

                vertical[verticalCount++] = value;
            }

            rest = SvgNumberParser.SkipWhitespace(rest[length..]);
        }

        if (horizontalCount == 0 || verticalCount == 0)
        {
            return false;
        }

        radii = new SvgLength[8];
        FillBoxValues(horizontal[..horizontalCount], radii.AsSpan(0, 4));
        FillBoxValues(verticalCount < 0 ? horizontal[..horizontalCount] : vertical[..verticalCount], radii.AsSpan(4, 4));
        return true;
    }

    /// <summary>
    /// Expands one to four values to four with the CSS rule of box edges and corners: one value for all four, two for
    /// the first and third and the second and fourth, three for the first, the second and fourth, and the third.
    /// </summary>
    /// <param name="values">One to four values.</param>
    /// <param name="result">The four values.</param>
    private static void FillBoxValues(ReadOnlySpan<SvgLength> values, Span<SvgLength> result)
    {
        result[0] = values[0];
        result[1] = values.Length > 1 ? values[1] : values[0];
        result[2] = values.Length > 2 ? values[2] : values[0];
        result[3] = values.Length > 3 ? values[3] : result[1];
    }

    /// <summary>
    /// Parses the arguments of <c>circle()</c> (<c>&lt;shape-radius&gt;? [at &lt;position&gt;]?</c>) or of
    /// <c>ellipse()</c> (<c>[&lt;shape-radius&gt;{2}]? [at &lt;position&gt;]?</c>).
    /// </summary>
    /// <param name="kind"><see cref="SvgBasicShapeKind.Circle"/> or <see cref="SvgBasicShapeKind.Ellipse"/>.</param>
    /// <param name="arguments">The trimmed arguments.</param>
    /// <param name="box">The geometry box of the value.</param>
    /// <param name="shape">When this method returns <see langword="true"/>, contains the shape.</param>
    /// <returns><see langword="true"/> if the arguments are valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseEllipseShape(SvgBasicShapeKind kind, ReadOnlySpan<char> arguments, SvgGeometryBox box, out SvgClipShape? shape)
    {
        shape = null;
        int radiusCount = kind == SvgBasicShapeKind.Circle ? 1 : 2;
        SvgLength[] lengths = new SvgLength[radiusCount];
        Span<SvgShapeRadiusKind> kinds = stackalloc SvgShapeRadiusKind[2];
        int count = 0;
        SvgShapePosition position = SvgShapePosition.Center;
        ReadOnlySpan<char> rest = arguments;
        while (!rest.IsEmpty)
        {
            ReadOnlySpan<char> word = ReadWord(rest, out int length);
            rest = SvgNumberParser.SkipWhitespace(rest[length..]);
            if (word.Equals("at", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParsePosition(rest, out position))
                {
                    return false;
                }

                rest = [];
                break;
            }

            if (count == radiusCount)
            {
                return false;
            }

            if (word.Equals("closest-side", StringComparison.OrdinalIgnoreCase))
            {
                kinds[count] = SvgShapeRadiusKind.ClosestSide;
            }
            else if (word.Equals("farthest-side", StringComparison.OrdinalIgnoreCase))
            {
                kinds[count] = SvgShapeRadiusKind.FarthestSide;
            }
            else if (SvgLength.TryParseNonNegative(word, out lengths[count]))
            {
                kinds[count] = SvgShapeRadiusKind.Length;
            }
            else
            {
                return false;
            }

            count++;
        }

        // An ellipse takes both radii or neither.
        if (count != 0 && count != radiusCount)
        {
            return false;
        }

        shape = new SvgClipShape(kind, box, lengths)
        {
            RadiusXKind = kinds[0],
            RadiusYKind = kinds[1],
            Position = position,
        };

        return true;
    }

    /// <summary>
    /// Parses a CSS <c>&lt;position&gt;</c>: one or two values, each a keyword or a length, or keywords with offsets, such
    /// as <c>right 10px bottom 20%</c>.
    /// </summary>
    /// <param name="source">The position text, without leading whitespace.</param>
    /// <param name="position">When this method returns <see langword="true"/>, contains the position.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParsePosition(ReadOnlySpan<char> source, out SvgShapePosition position)
    {
        position = SvgShapePosition.Center;

        // Keyword codes: 1 left, 2 right, 3 top, 4 bottom, 5 center; 0 is a length.
        Span<int> keywords = stackalloc int[4];
        Span<SvgLength> lengths = stackalloc SvgLength[4];
        int count = 0;
        ReadOnlySpan<char> rest = source;
        while (!rest.IsEmpty)
        {
            if (count == 4)
            {
                return false;
            }

            ReadOnlySpan<char> word = ReadWord(rest, out int length);
            rest = SvgNumberParser.SkipWhitespace(rest[length..]);
            keywords[count] = PositionKeyword(word);
            if (keywords[count] == 0 && !SvgLength.TryParse(word, out lengths[count]))
            {
                return false;
            }

            count++;
        }

        SvgLength center = new(50F, SvgLengthUnit.Percent);
        if (count == 1)
        {
            int keyword = keywords[0];
            position = keyword is 3 or 4
                ? new SvgShapePosition(center, false, KeywordPosition(keyword), false)
                : new SvgShapePosition(keyword == 0 ? lengths[0] : KeywordPosition(keyword), false, center, false);
            return true;
        }

        if (count == 2)
        {
            int first = keywords[0];
            int second = keywords[1];

            // Two keywords may come in either order; otherwise the first value is horizontal.
            if (first != 0 && second != 0 && (first is 3 or 4 || second is 1 or 2))
            {
                (first, second) = (second, first);
                (lengths[0], lengths[1]) = (lengths[1], lengths[0]);
            }

            if (first is 3 or 4 || second is 1 or 2)
            {
                return false;
            }

            position = new SvgShapePosition(
                first == 0 ? lengths[0] : KeywordPosition(first),
                false,
                second == 0 ? lengths[1] : KeywordPosition(second),
                false);
            return true;
        }

        // Three or four values: two groups, each a keyword with an optional offset from its edge; center takes no offset.
        Span<int> groupKeywords = stackalloc int[2];
        Span<SvgLength> groupValues = stackalloc SvgLength[2];
        Span<bool> groupFromEnd = stackalloc bool[2];
        int groups = 0;
        for (int i = 0; i < count; i++)
        {
            int keyword = keywords[i];
            bool hasOffset = i + 1 < count && keywords[i + 1] == 0;
            if (keyword == 0 || groups == 2 || (keyword == 5 && hasOffset))
            {
                return false;
            }

            groupKeywords[groups] = keyword;
            groupValues[groups] = hasOffset ? lengths[i + 1] : KeywordPosition(keyword);
            groupFromEnd[groups] = hasOffset && keyword is 2 or 4;
            groups++;
            if (hasOffset)
            {
                i++;
            }
        }

        if (groups != 2)
        {
            return false;
        }

        // The group with a horizontal keyword, or a center beside a vertical keyword, is the horizontal one.
        int horizontalGroup = groupKeywords[0] is 1 or 2 || (groupKeywords[0] == 5 && groupKeywords[1] is 3 or 4) ? 0 : 1;
        int verticalGroup = 1 - horizontalGroup;
        if (groupKeywords[horizontalGroup] is 3 or 4 || groupKeywords[verticalGroup] is 1 or 2)
        {
            return false;
        }

        position = new SvgShapePosition(
            groupValues[horizontalGroup],
            groupFromEnd[horizontalGroup],
            groupValues[verticalGroup],
            groupFromEnd[verticalGroup]);
        return true;
    }

    /// <summary>
    /// Returns the code of a position keyword.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns>1 left, 2 right, 3 top, 4 bottom, 5 center, or 0 when the word is not a keyword.</returns>
    private static int PositionKeyword(ReadOnlySpan<char> word)
        => word switch
        {
            _ when word.Equals("left", StringComparison.OrdinalIgnoreCase) => 1,
            _ when word.Equals("right", StringComparison.OrdinalIgnoreCase) => 2,
            _ when word.Equals("top", StringComparison.OrdinalIgnoreCase) => 3,
            _ when word.Equals("bottom", StringComparison.OrdinalIgnoreCase) => 4,
            _ when word.Equals("center", StringComparison.OrdinalIgnoreCase) => 5,
            _ => 0
        };

    /// <summary>
    /// Parses the arguments of <c>polygon()</c>: <c>[&lt;fill-rule&gt; ,]? [&lt;length-percentage&gt;
    /// &lt;length-percentage&gt;]#</c>.
    /// </summary>
    /// <param name="arguments">The trimmed arguments.</param>
    /// <param name="box">The geometry box of the value.</param>
    /// <param name="shape">When this method returns <see langword="true"/>, contains the shape.</param>
    /// <returns><see langword="true"/> if the arguments are valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParsePolygonShape(ReadOnlySpan<char> arguments, SvgGeometryBox box, out SvgClipShape? shape)
    {
        shape = null;
        SvgFillRule rule = SvgFillRule.NonZero;
        List<SvgLength> points = [];
        ReadOnlySpan<char> rest = arguments;
        bool first = true;
        while (true)
        {
            int comma = rest.IndexOf(',');
            ReadOnlySpan<char> entry = SvgNumberParser.Trim(comma < 0 ? rest : rest[..comma]);
            if (first && TryParseKeyword(entry, FillRuleKeywords, out int fillRule))
            {
                // A fill rule needs a vertex after it.
                if (comma < 0)
                {
                    return false;
                }

                rule = (SvgFillRule)fillRule;
            }
            else
            {
                ReadOnlySpan<char> xText = ReadWord(entry, out int length);
                ReadOnlySpan<char> yText = SvgNumberParser.SkipWhitespace(entry[length..]);
                if (!SvgLength.TryParse(xText, out SvgLength x) || !SvgLength.TryParse(yText, out SvgLength y))
                {
                    return false;
                }

                points.Add(x);
                points.Add(y);
            }

            first = false;
            if (comma < 0)
            {
                break;
            }

            rest = rest[(comma + 1)..];
        }

        shape = new SvgClipShape(SvgBasicShapeKind.Polygon, box, [.. points]) { FillRule = rule };
        return true;
    }
}
