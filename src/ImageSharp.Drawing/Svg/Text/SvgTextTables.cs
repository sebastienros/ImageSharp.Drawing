// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The tables that the reader fills for text elements: the attributes of <c>text</c>, <c>tspan</c>, <c>textPath</c>, and
/// <c>tref</c>, the position lists, and the character data inside <c>text</c> elements.
/// </summary>
/// <remarks>
/// The reader creates one instance on the first text content element of a document, so documents without text allocate
/// nothing for it. The element tree takes the instance over; it does not change after the reader returns.
/// </remarks>
internal sealed class SvgTextTables
{
    /// <summary>
    /// Gets the attributes of <c>text</c> and <c>tspan</c> elements.
    /// </summary>
    public List<SvgTextAttributes> TextAttributes { get; } = [];

    /// <summary>
    /// Gets the attributes of <c>textPath</c> elements.
    /// </summary>
    public List<SvgTextPathAttributes> TextPaths { get; } = [];

    /// <summary>
    /// Gets the referenced ids of <c>tref</c> elements, or <see langword="null"/> for a missing or external reference.
    /// </summary>
    public List<string?> TrefReferences { get; } = [];

    /// <summary>
    /// Gets the lengths of the <c>x</c>, <c>y</c>, <c>dx</c>, and <c>dy</c> lists.
    /// </summary>
    public List<SvgLength> Lengths { get; } = [];

    /// <summary>
    /// Gets the angles of the <c>rotate</c> lists, in degrees.
    /// </summary>
    public List<float> Rotations { get; } = [];

    /// <summary>
    /// Gets the character data nodes inside <c>text</c> elements, in document order.
    /// </summary>
    public List<SvgTextNode> Nodes { get; } = [];

    /// <summary>
    /// Gets the characters that <see cref="Nodes"/> index into. Only the first <see cref="Length"/> are used.
    /// </summary>
    public char[] Characters { get; private set; } = [];

    /// <summary>
    /// Gets the number of characters in <see cref="Characters"/>.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// Appends characters to the character table, growing it by doubling.
    /// </summary>
    /// <param name="value">The characters.</param>
    /// <returns>The index of the first appended character.</returns>
    public int AppendText(string value)
    {
        int start = this.Length;
        int required = start + value.Length;
        if (required > this.Characters.Length)
        {
            char[] larger = new char[Math.Max(required, Math.Max(256, this.Characters.Length * 2))];
            this.Characters.AsSpan(0, start).CopyTo(larger);
            this.Characters = larger;
        }

        value.CopyTo(this.Characters.AsSpan(start));
        this.Length = required;
        return start;
    }

    /// <summary>
    /// Parses a list of lengths separated by whitespace and an optional comma into the length table. An invalid list
    /// adds nothing and gives the empty range, as an invalid attribute value is unspecified.
    /// </summary>
    /// <param name="value">The attribute value.</param>
    /// <returns>The range of the lengths.</returns>
    public SvgListRange ParseLengthList(string value)
    {
        List<SvgLength> values = this.Lengths;
        int start = values.Count;
        ReadOnlySpan<char> source = SvgNumberParser.SkipWhitespace(value);
        while (!source.IsEmpty)
        {
            if ((values.Count > start && !SvgNumberParser.TrySkipCommaWhitespace(ref source))
                || !SvgLength.TryRead(source, out SvgLength length, out int read))
            {
                values.RemoveRange(start, values.Count - start);
                return default;
            }

            values.Add(length);
            source = SvgNumberParser.SkipWhitespace(source[read..]);
        }

        return new SvgListRange(start, values.Count - start);
    }

    /// <summary>
    /// Parses a <c>rotate</c> list of numbers into the rotation table. An invalid list adds nothing and gives the empty
    /// range.
    /// </summary>
    /// <param name="value">The attribute value.</param>
    /// <returns>The range of the angles.</returns>
    public SvgListRange ParseRotateList(string value)
    {
        List<float> values = this.Rotations;
        int start = values.Count;
        ReadOnlySpan<char> source = SvgNumberParser.SkipWhitespace(value);
        while (!source.IsEmpty)
        {
            if ((values.Count > start && !SvgNumberParser.TrySkipCommaWhitespace(ref source))
                || !SvgNumberParser.TryReadNumber(source, false, out float angle, out int read))
            {
                values.RemoveRange(start, values.Count - start);
                return default;
            }

            values.Add(angle);
            source = SvgNumberParser.SkipWhitespace(source[read..]);
        }

        return new SvgListRange(start, values.Count - start);
    }
}
