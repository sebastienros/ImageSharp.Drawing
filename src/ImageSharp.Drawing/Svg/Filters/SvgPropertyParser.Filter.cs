// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The filter properties: <c>filter</c>, <c>color-interpolation-filters</c>, <c>flood-color</c>, <c>flood-opacity</c>,
/// and <c>lighting-color</c>.
/// </content>
internal sealed partial class SvgPropertyParser
{
    /// <summary>
    /// The names of the filter functions in <see cref="SvgFilterFunctionKind"/> order, from <see cref="SvgFilterFunctionKind.Blur"/>.
    /// </summary>
    private static readonly string[] FilterFunctionNames =
        ["blur", "brightness", "contrast", "drop-shadow", "grayscale", "hue-rotate", "invert", "opacity", "saturate", "sepia"];

    /// <summary>
    /// Parses the value of a filter property and stores it in the filter values of the style. The caller marks the
    /// property.
    /// </summary>
    /// <param name="property">A property for which <see cref="SvgFilterStyle.IsFilterProperty(SvgPropertyId)"/> is true.</param>
    /// <param name="source">The trimmed value, which is not a CSS-wide keyword.</param>
    /// <param name="style">The style that receives the value.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseFilter(SvgPropertyId property, ReadOnlySpan<char> source, ref SvgSpecifiedStyle style)
    {
        // Parse first and create the values only for a valid value, so an invalid declaration allocates nothing.
        switch (property)
        {
            case SvgPropertyId.Filter:
                if (!this.TryParseFilterList(source, out SvgFilterList? list))
                {
                    return false;
                }

                style.GetOrCreateExtended().GetOrCreateFilters().Filter = list;
                return true;
            case SvgPropertyId.ColorInterpolationFilters:
                if (!TryParseKeyword(source, ColorInterpolationKeywords, out int keyword))
                {
                    return false;
                }

                style.GetOrCreateExtended().GetOrCreateFilters().ColorInterpolationFilters = (SvgColorInterpolation)keyword;
                return true;
            case SvgPropertyId.FloodOpacity:
                if (!SvgNumberParser.TryParseClampedFraction(source, out float opacity))
                {
                    return false;
                }

                style.GetOrCreateExtended().GetOrCreateFilters().FloodOpacity = opacity;
                return true;
            default:
                if (!SvgPaint.TryParseColorOrCurrentColor(source, this.colors, out SvgPaint color))
                {
                    return false;
                }

                SvgFilterStyle filters = style.GetOrCreateExtended().GetOrCreateFilters();
                if (property == SvgPropertyId.FloodColor)
                {
                    filters.FloodColor = color;
                }
                else
                {
                    filters.LightingColor = color;
                }

                return true;
        }
    }

    /// <summary>
    /// Parses a <c>filter</c> value: <c>none</c>, or a list of <c>url()</c> references and filter functions separated by
    /// whitespace, as Filter Effects Level 1 defines it. One invalid entry makes the whole value invalid.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="list">When this method returns <see langword="true"/>, contains the list, or <see langword="null"/> for <c>none</c>.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseFilterList(ReadOnlySpan<char> source, out SvgFilterList? list)
    {
        list = null;
        if (source.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        List<SvgFilterFunction> functions = [];
        ReadOnlySpan<char> rest = source;
        while (!rest.IsEmpty)
        {
            // An entry is a name followed by a parenthesized argument list. The arguments of drop-shadow() can hold
            // color functions, so the closing parenthesis is found by depth; a url() ends at its first closing
            // parenthesis, as for paint.
            int open = rest.IndexOf('(');
            if (open <= 0)
            {
                return false;
            }

            ReadOnlySpan<char> name = rest[..open];
            int close;
            if (name.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                close = rest.IndexOf(')');
            }
            else
            {
                close = -1;
                int depth = 0;
                for (int i = open; i < rest.Length; i++)
                {
                    if (rest[i] == '(')
                    {
                        depth++;
                    }
                    else if (rest[i] == ')' && --depth == 0)
                    {
                        close = i;
                        break;
                    }
                }
            }

            if (close < 0 || !this.TryParseFilterFunction(name, rest[..(close + 1)], rest[(open + 1)..close], out SvgFilterFunction function))
            {
                return false;
            }

            functions.Add(function);
            rest = rest[(close + 1)..];

            // Entries are separated by whitespace only, which may be empty after a closing parenthesis.
            rest = SvgNumberParser.SkipWhitespace(rest);
        }

        if (functions.Count == 0)
        {
            return false;
        }

        list = new SvgFilterList([.. functions]);
        return true;
    }

    /// <summary>
    /// Parses one entry of a <c>filter</c> value.
    /// </summary>
    /// <param name="name">The name before the opening parenthesis.</param>
    /// <param name="entry">The whole entry, from the name to the closing parenthesis.</param>
    /// <param name="arguments">The characters between the parentheses.</param>
    /// <param name="function">When this method returns <see langword="true"/>, contains the entry.</param>
    /// <returns><see langword="true"/> if the entry is valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseFilterFunction(ReadOnlySpan<char> name, ReadOnlySpan<char> entry, ReadOnlySpan<char> arguments, out SvgFilterFunction function)
    {
        function = default;
        SvgPaint currentColor = new(SvgPaintKind.CurrentColor, default, null, SvgPaintKind.None);
        if (name.Equals("url", StringComparison.OrdinalIgnoreCase))
        {
            if (!SvgReference.TryParse(entry, out SvgReference reference))
            {
                return false;
            }

            function = new SvgFilterFunction(SvgFilterFunctionKind.Url, reference, 0F, default, default, default, currentColor);
            return true;
        }

        int index = -1;
        for (int i = 0; i < FilterFunctionNames.Length; i++)
        {
            if (name.Equals(FilterFunctionNames[i], StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        SvgFilterFunctionKind kind = (SvgFilterFunctionKind)(index + (int)SvgFilterFunctionKind.Blur);
        arguments = SvgNumberParser.Trim(arguments);
        switch (kind)
        {
            case SvgFilterFunctionKind.Blur:
            {
                // blur( <length>? ): a non-negative length without percentages; the default is 0.
                SvgLength radius = new(0F, SvgLengthUnit.Px);
                if (!arguments.IsEmpty && (!TryParseFilterLength(arguments, out radius) || radius.Value < 0F))
                {
                    return false;
                }

                function = new SvgFilterFunction(kind, default, 0F, default, default, radius, currentColor);
                return true;
            }

            case SvgFilterFunctionKind.HueRotate:
            {
                // hue-rotate( [ <angle> | <zero> ]? ): the default is 0deg.
                float degrees = 0F;
                if (!arguments.IsEmpty
                    && (!SvgNumberParser.TryReadAngle(arguments, false, out degrees, out int length) || length != arguments.Length))
                {
                    return false;
                }

                function = new SvgFilterFunction(kind, default, degrees, default, default, default, currentColor);
                return true;
            }

            case SvgFilterFunctionKind.DropShadow:
                return this.TryParseDropShadow(arguments, out function);

            default:
            {
                // <number-percentage>?: a non-negative number or percentage; the default is 1.
                float amount = 1F;
                if (!arguments.IsEmpty)
                {
                    if (!SvgNumberParser.TryReadNumber(arguments, false, out amount, out int length))
                    {
                        return false;
                    }

                    if (length == arguments.Length - 1 && arguments[^1] == '%')
                    {
                        amount /= 100F;
                    }
                    else if (length != arguments.Length)
                    {
                        return false;
                    }

                    if (amount < 0F)
                    {
                        return false;
                    }
                }

                function = new SvgFilterFunction(kind, default, amount, default, default, default, currentColor);
                return true;
            }
        }
    }

    /// <summary>
    /// Parses the arguments of <c>drop-shadow()</c>: <c>[ &lt;color&gt;? &amp;&amp; &lt;length&gt;{2,3} ]</c>, where the
    /// third length, the blur radius, must not be negative. The color defaults to <c>currentColor</c>.
    /// </summary>
    /// <param name="arguments">The trimmed arguments.</param>
    /// <param name="function">When this method returns <see langword="true"/>, contains the entry.</param>
    /// <returns><see langword="true"/> if the arguments are valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseDropShadow(ReadOnlySpan<char> arguments, out SvgFilterFunction function)
    {
        function = default;
        Span<SvgLength> lengths = stackalloc SvgLength[3];
        int lengthCount = 0;
        SvgPaint color = new(SvgPaintKind.CurrentColor, default, null, SvgPaintKind.None);
        bool hasColor = false;
        bool colorAfterLengths = false;

        ReadOnlySpan<char> rest = arguments;
        while (!rest.IsEmpty)
        {
            // Tokens are separated by whitespace outside parentheses, so rgb( 1, 2, 3 ) stays one token.
            int end = 0;
            int depth = 0;
            while (end < rest.Length && (depth > 0 || !SvgNumberParser.IsWhitespace(rest[end])))
            {
                depth += rest[end] == '(' ? 1 : rest[end] == ')' ? -1 : 0;
                end++;
            }

            ReadOnlySpan<char> token = rest[..end];
            rest = SvgNumberParser.SkipWhitespace(rest[end..]);
            if (TryParseFilterLength(token, out SvgLength length))
            {
                // The lengths are one group: a length after a color that followed lengths is invalid.
                if (lengthCount == 3 || colorAfterLengths)
                {
                    return false;
                }

                lengths[lengthCount++] = length;
                continue;
            }

            if (hasColor || !SvgPaint.TryParseColorOrCurrentColor(token, this.colors, out color))
            {
                return false;
            }

            hasColor = true;
            colorAfterLengths = lengthCount > 0;
        }

        if (lengthCount < 2 || (lengthCount == 3 && lengths[2].Value < 0F))
        {
            return false;
        }

        SvgLength radius = lengthCount == 3 ? lengths[2] : new SvgLength(0F, SvgLengthUnit.Px);
        function = new SvgFilterFunction(SvgFilterFunctionKind.DropShadow, default, 0F, lengths[0], lengths[1], radius, color);
        return true;
    }

    /// <summary>
    /// Parses a CSS length for a filter function: a number with a length unit, or a unitless zero. Percentages are not
    /// lengths here.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the length.</param>
    /// <returns><see langword="true"/> if the value is such a length; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseFilterLength(ReadOnlySpan<char> source, out SvgLength length)
    {
        if (!SvgLength.TryParse(source, out length) || length.Unit == SvgLengthUnit.Percent)
        {
            return false;
        }

        if (length.Unit == SvgLengthUnit.Number)
        {
            if (length.Value != 0F)
            {
                return false;
            }

            length = new SvgLength(0F, SvgLengthUnit.Px);
        }

        return true;
    }
}
