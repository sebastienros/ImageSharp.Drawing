// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Parses SVG numbers, number lists, and the values built from them. Every method reads a
/// <see cref="ReadOnlySpan{T}"/>, uses the invariant culture, never throws, and reports failure with a
/// <see cref="bool"/>.
/// </summary>
internal static class SvgNumberParser
{
    /// <summary>
    /// Returns the length of the number token at the start of <paramref name="source"/>, or zero when the source does
    /// not start with a number.
    /// </summary>
    /// <remarks>
    /// The grammar is <c>sign? (digits ('.' digits?)? | '.' digits) (('e' | 'E') sign? digits)?</c>. The scan stops at the
    /// first character that cannot continue the number, so tightly packed input such as <c>10-20</c>, <c>1.5.5</c>, and
    /// <c>1e2e3</c> splits into separate numbers. An exponent marker is part of the number only when digits follow it, so
    /// <c>1em</c> is the number <c>1</c> followed by the unit <c>em</c>.
    /// </remarks>
    /// <param name="source">The characters to scan.</param>
    /// <param name="allowTrailingPoint">
    /// Whether a decimal point without following digits ends the number, as in <c>5.</c>. SVG path data allows it. CSS
    /// numbers do not.
    /// </param>
    /// <returns>The number of characters in the number token.</returns>
    public static int ScanNumber(ReadOnlySpan<char> source, bool allowTrailingPoint)
    {
        int i = 0;
        if (i < source.Length && source[i] is '+' or '-')
        {
            i++;
        }

        int integerStart = i;
        while (i < source.Length && char.IsAsciiDigit(source[i]))
        {
            i++;
        }

        int digits = i - integerStart;
        if (i < source.Length && source[i] == '.')
        {
            int fractionEnd = i + 1;
            while (fractionEnd < source.Length && char.IsAsciiDigit(source[fractionEnd]))
            {
                fractionEnd++;
            }

            int fractionDigits = fractionEnd - i - 1;

            // A second decimal point starts a new number, so ".5" after "1.5" is not consumed here.
            if (fractionDigits > 0 || (digits > 0 && allowTrailingPoint))
            {
                i = fractionEnd;
                digits += fractionDigits;
            }
        }

        if (digits == 0)
        {
            return 0;
        }

        if (i < source.Length && source[i] is 'e' or 'E')
        {
            int exponent = i + 1;
            if (exponent < source.Length && source[exponent] is '+' or '-')
            {
                exponent++;
            }

            int exponentStart = exponent;
            while (exponent < source.Length && char.IsAsciiDigit(source[exponent]))
            {
                exponent++;
            }

            if (exponent > exponentStart)
            {
                i = exponent;
            }
        }

        return i;
    }

    /// <summary>
    /// Reads the number at the start of <paramref name="source"/>. Leading separators are not skipped.
    /// </summary>
    /// <param name="source">The characters to read.</param>
    /// <param name="allowTrailingPoint">Whether a decimal point without following digits ends the number.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the finite number.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the number of characters read.</param>
    /// <returns><see langword="true"/> if a finite number was read; otherwise, <see langword="false"/>.</returns>
    public static bool TryReadNumber(ReadOnlySpan<char> source, bool allowTrailingPoint, out float value, out int length)
    {
        length = ScanNumber(source, allowTrailingPoint);

        // The scan has already matched the grammar, so the conversion only fails on overflow to infinity.
        if (length > 0
            && float.TryParse(source[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && float.IsFinite(value))
        {
            return true;
        }

        value = default;
        length = 0;
        return false;
    }

    /// <summary>
    /// Parses a value that is exactly one number, with optional surrounding whitespace.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the number.</param>
    /// <returns><see langword="true"/> if the value is one finite number; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseNumber(ReadOnlySpan<char> source, out float value)
    {
        source = Trim(source);
        return TryReadNumber(source, false, out value, out int length) && length == source.Length;
    }

    /// <summary>
    /// Parses a number or a percentage and returns it as a fraction, so <c>50%</c> and <c>0.5</c> both give 0.5. The result
    /// is clamped to the range 0 to 1, as CSS does for opacity values and SVG does for gradient stop offsets.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the clamped fraction.</param>
    /// <returns><see langword="true"/> if the value is a number or a percentage; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseClampedFraction(ReadOnlySpan<char> source, out float value)
    {
        source = Trim(source);
        if (!TryReadNumber(source, false, out value, out int length))
        {
            return false;
        }

        if (length == source.Length - 1 && source[^1] == '%')
        {
            value /= 100F;
        }
        else if (length != source.Length)
        {
            value = default;
            return false;
        }

        value = Math.Clamp(value, 0F, 1F);
        return true;
    }

    /// <summary>
    /// Parses a <c>viewBox</c> value: four numbers for min-x, min-y, width, and height, separated by whitespace and an
    /// optional comma.
    /// </summary>
    /// <remarks>
    /// SVG 2 makes a negative width or height an error that invalidates the attribute, so this method fails for it. A zero
    /// width or height is valid; SVG 2 says that it disables rendering of the element, and the resolve stage applies that.
    /// </remarks>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the view box rectangle.</param>
    /// <returns><see langword="true"/> if the value is a valid view box; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseViewBox(ReadOnlySpan<char> source, out RectangleF value)
    {
        value = default;
        Span<float> numbers = stackalloc float[4];
        source = SkipWhitespace(source);
        for (int i = 0; i < numbers.Length; i++)
        {
            if (i > 0 && !TrySkipCommaWhitespace(ref source))
            {
                return false;
            }

            if (!TryReadNumber(source, false, out numbers[i], out int length))
            {
                return false;
            }

            source = source[length..];
        }

        if (!SkipWhitespace(source).IsEmpty || numbers[2] < 0 || numbers[3] < 0)
        {
            return false;
        }

        value = new RectangleF(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    /// <summary>
    /// Parses the <c>points</c> attribute of <c>polyline</c> and <c>polygon</c> and appends the points to
    /// <paramref name="points"/>.
    /// </summary>
    /// <remarks>
    /// SVG 2 renders a polyline or polygon up to the first error in its points, like path data. This method reads numbers
    /// until the first one that does not parse, pairs them, and drops a final odd value.
    /// </remarks>
    /// <param name="source">The characters to parse.</param>
    /// <param name="points">The list that receives the points.</param>
    /// <returns><see langword="true"/> if the whole value parsed without error; otherwise, <see langword="false"/>.</returns>
    public static bool TryParsePoints(ReadOnlySpan<char> source, List<PointF> points)
    {
        source = SkipWhitespace(source);
        bool first = true;
        bool hasX = false;
        float x = 0;
        while (!source.IsEmpty)
        {
            if (!first && !TrySkipCommaWhitespace(ref source))
            {
                return false;
            }

            if (!TryReadNumber(source, false, out float number, out int length))
            {
                return false;
            }

            source = SkipWhitespace(source[length..]);
            first = false;
            if (hasX)
            {
                points.Add(new PointF(x, number));
                hasX = false;
            }
            else
            {
                x = number;
                hasX = true;
            }
        }

        return !hasX;
    }

    /// <summary>
    /// Returns whether the character is SVG and CSS whitespace: space, tab, line feed, carriage return, or form feed.
    /// </summary>
    /// <param name="c">The character to test.</param>
    /// <returns><see langword="true"/> if the character is whitespace; otherwise, <see langword="false"/>.</returns>
    public static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    /// <summary>
    /// Removes leading whitespace.
    /// </summary>
    /// <param name="source">The characters to trim.</param>
    /// <returns>The characters after the leading whitespace.</returns>
    public static ReadOnlySpan<char> SkipWhitespace(ReadOnlySpan<char> source)
    {
        int i = 0;
        while (i < source.Length && IsWhitespace(source[i]))
        {
            i++;
        }

        return source[i..];
    }

    /// <summary>
    /// Removes leading and trailing whitespace.
    /// </summary>
    /// <param name="source">The characters to trim.</param>
    /// <returns>The characters without surrounding whitespace.</returns>
    public static ReadOnlySpan<char> Trim(ReadOnlySpan<char> source)
    {
        source = SkipWhitespace(source);
        int end = source.Length;
        while (end > 0 && IsWhitespace(source[end - 1]))
        {
            end--;
        }

        return source[..end];
    }

    /// <summary>
    /// Skips the separator between two list values: whitespace, then an optional comma and more whitespace. The
    /// separator may be empty, because SVG lists such as <c>10-20</c> need none between values that do not run together.
    /// </summary>
    /// <param name="source">The remaining characters; advanced past the separator on success.</param>
    /// <returns><see langword="true"/> if a value can follow; <see langword="false"/> if the input ends after a comma.</returns>
    public static bool TrySkipCommaWhitespace(ref ReadOnlySpan<char> source)
    {
        source = SkipWhitespace(source);
        if (!source.IsEmpty && source[0] == ',')
        {
            source = SkipWhitespace(source[1..]);
            return !source.IsEmpty;
        }

        return true;
    }
}
