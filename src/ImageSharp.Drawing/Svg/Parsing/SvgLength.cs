// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// An SVG length as written in the document: a number and its unit. <c>default(SvgLength)</c> means that the length is
/// not specified, so typed attribute fields need no separate "specified" flag.
/// </summary>
internal readonly struct SvgLength
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgLength"/> struct.
    /// </summary>
    /// <param name="value">The number. For <see cref="SvgLengthUnit.Percent"/> this is the percentage, not a fraction.</param>
    /// <param name="unit">The unit.</param>
    public SvgLength(float value, SvgLengthUnit unit)
    {
        this.Value = value;
        this.Unit = unit;
    }

    /// <summary>
    /// Gets the number. For <see cref="SvgLengthUnit.Percent"/> this is the percentage, not a fraction.
    /// </summary>
    public float Value { get; }

    /// <summary>
    /// Gets the unit.
    /// </summary>
    public SvgLengthUnit Unit { get; }

    /// <summary>
    /// Gets a value indicating whether the length is specified.
    /// </summary>
    public bool IsSpecified => this.Unit != SvgLengthUnit.Unspecified;

    /// <summary>
    /// Parses a length: a number followed directly by an optional unit, with optional surrounding whitespace. Units are
    /// ASCII case-insensitive, as in CSS. The units are those of <see cref="SvgLengthUnit"/>: the SVG 1.1 units and
    /// the CSS units <c>ch</c>, <c>rem</c>, <c>Q</c>, <c>vw</c>, <c>vh</c>, <c>vmin</c>, and <c>vmax</c>.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the length.</param>
    /// <returns><see langword="true"/> if the value is a length; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> source, out SvgLength value)
    {
        source = SvgNumberParser.Trim(source);
        if (TryRead(source, out value, out int length) && length == source.Length)
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Parses a length that must not be negative. SVG makes a negative width, height, or radius an error, and an error
    /// makes the attribute unspecified.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the length.</param>
    /// <returns><see langword="true"/> if the value is a length of zero or more; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseNonNegative(ReadOnlySpan<char> source, out SvgLength value)
    {
        if (TryParse(source, out value) && value.Value >= 0)
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads the length at the start of <paramref name="source"/>. Leading whitespace is not skipped.
    /// </summary>
    /// <param name="source">The characters to read.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the length.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the number of characters read.</param>
    /// <returns><see langword="true"/> if a length was read; otherwise, <see langword="false"/>.</returns>
    public static bool TryRead(ReadOnlySpan<char> source, out SvgLength value, out int length)
    {
        if (!SvgNumberParser.TryReadNumber(source, false, out float number, out length))
        {
            value = default;
            return false;
        }

        // The unit is the run of letters, or one '%', directly after the number. A unit after whitespace is not part of
        // the length, so "10 px" fails in the caller, which expects the input to end.
        ReadOnlySpan<char> rest = source[length..];
        int unitLength = 0;
        if (!rest.IsEmpty && rest[0] == '%')
        {
            unitLength = 1;
        }
        else
        {
            while (unitLength < rest.Length && char.IsAsciiLetter(rest[unitLength]))
            {
                unitLength++;
            }
        }

        if (!TryGetUnit(rest[..unitLength], out SvgLengthUnit unit))
        {
            value = default;
            length = 0;
            return false;
        }

        value = new SvgLength(number, unit);
        length += unitLength;
        return true;
    }

    /// <summary>
    /// Parses a list of non-negative lengths separated by whitespace and an optional comma, as used by
    /// <c>stroke-dasharray</c>, and appends them to <paramref name="values"/>. Nothing is appended on failure.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="values">The list that receives the lengths.</param>
    /// <returns><see langword="true"/> if the value is a non-empty list of valid lengths; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseNonNegativeList(ReadOnlySpan<char> source, List<SvgLength> values)
    {
        int start = values.Count;
        source = SvgNumberParser.SkipWhitespace(source);
        while (!source.IsEmpty)
        {
            // A trailing comma fails the separator, and a value that is not a non-negative length fails the list.
            if ((values.Count > start && !SvgNumberParser.TrySkipCommaWhitespace(ref source))
                || !TryRead(source, out SvgLength value, out int length)
                || value.Value < 0)
            {
                values.RemoveRange(start, values.Count - start);
                return false;
            }

            values.Add(value);
            source = SvgNumberParser.SkipWhitespace(source[length..]);
        }

        return values.Count > start;
    }

    /// <summary>
    /// Maps a unit suffix to its <see cref="SvgLengthUnit"/>.
    /// </summary>
    /// <param name="unit">The unit characters; empty for a unitless number.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the unit.</param>
    /// <returns><see langword="true"/> if the unit is known; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetUnit(ReadOnlySpan<char> unit, out SvgLengthUnit value)
    {
        value = unit.Length switch
        {
            0 => SvgLengthUnit.Number,
            1 when unit[0] == '%' => SvgLengthUnit.Percent,
            1 when unit[0] is 'q' or 'Q' => SvgLengthUnit.Q,
            2 => (char.ToLowerInvariant(unit[0]), char.ToLowerInvariant(unit[1])) switch
            {
                ('p', 'x') => SvgLengthUnit.Px,
                ('p', 't') => SvgLengthUnit.Pt,
                ('p', 'c') => SvgLengthUnit.Pc,
                ('m', 'm') => SvgLengthUnit.Mm,
                ('c', 'm') => SvgLengthUnit.Cm,
                ('i', 'n') => SvgLengthUnit.In,
                ('e', 'm') => SvgLengthUnit.Em,
                ('e', 'x') => SvgLengthUnit.Ex,
                ('c', 'h') => SvgLengthUnit.Ch,
                ('v', 'w') => SvgLengthUnit.Vw,
                ('v', 'h') => SvgLengthUnit.Vh,
                _ => SvgLengthUnit.Unspecified
            },
            3 when unit.Equals("rem", StringComparison.OrdinalIgnoreCase) => SvgLengthUnit.Rem,
            4 when unit.Equals("vmin", StringComparison.OrdinalIgnoreCase) => SvgLengthUnit.Vmin,
            4 when unit.Equals("vmax", StringComparison.OrdinalIgnoreCase) => SvgLengthUnit.Vmax,
            _ => SvgLengthUnit.Unspecified
        };

        return value != SvgLengthUnit.Unspecified;
    }
}
