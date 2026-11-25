// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A <c>preserveAspectRatio</c> value. <c>default</c> is the initial value, <c>xMidYMid meet</c>.
/// </summary>
internal readonly struct SvgPreserveAspectRatio
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgPreserveAspectRatio"/> struct.
    /// </summary>
    /// <param name="align">The alignment.</param>
    /// <param name="slice">
    /// <see langword="true"/> for <c>slice</c>, which covers the viewport; <see langword="false"/> for <c>meet</c>, which
    /// fits inside it.
    /// </param>
    public SvgPreserveAspectRatio(SvgAlign align, bool slice)
    {
        this.Align = align;
        this.Slice = slice;
    }

    /// <summary>
    /// Gets the alignment.
    /// </summary>
    public SvgAlign Align { get; }

    /// <summary>
    /// Gets a value indicating whether the value is <c>slice</c> rather than <c>meet</c>. It has no effect when
    /// <see cref="Align"/> is <see cref="SvgAlign.None"/>.
    /// </summary>
    public bool Slice { get; }

    /// <summary>
    /// Parses <c>[defer] &lt;align&gt; [meet | slice]</c>. Keywords are case-sensitive, as in the SVG grammar. The SVG 1.1
    /// <c>defer</c> keyword is accepted and ignored; it only affects <c>image</c> elements and SVG 2 removed it.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the value.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> source, out SvgPreserveAspectRatio value)
    {
        value = default;
        source = SvgNumberParser.Trim(source);
        ReadOnlySpan<char> word = ReadWord(ref source);
        if (word.SequenceEqual("defer"))
        {
            word = ReadWord(ref source);
        }

        SvgAlign align;
        switch (word)
        {
            case "none":
                align = SvgAlign.None;
                break;
            case "xMinYMin":
                align = SvgAlign.XMinYMin;
                break;
            case "xMidYMin":
                align = SvgAlign.XMidYMin;
                break;
            case "xMaxYMin":
                align = SvgAlign.XMaxYMin;
                break;
            case "xMinYMid":
                align = SvgAlign.XMinYMid;
                break;
            case "xMidYMid":
                align = SvgAlign.XMidYMid;
                break;
            case "xMaxYMid":
                align = SvgAlign.XMaxYMid;
                break;
            case "xMinYMax":
                align = SvgAlign.XMinYMax;
                break;
            case "xMidYMax":
                align = SvgAlign.XMidYMax;
                break;
            case "xMaxYMax":
                align = SvgAlign.XMaxYMax;
                break;
            default:
                return false;
        }

        bool slice = false;
        word = ReadWord(ref source);
        if (word.SequenceEqual("slice"))
        {
            slice = true;
        }
        else if (!word.IsEmpty && !word.SequenceEqual("meet"))
        {
            return false;
        }

        if (!source.IsEmpty)
        {
            return false;
        }

        value = new SvgPreserveAspectRatio(align, slice);
        return true;
    }

    /// <summary>
    /// Reads the next whitespace-separated word.
    /// </summary>
    /// <param name="source">The remaining characters; advanced past the word and the whitespace after it.</param>
    /// <returns>The word, or an empty span at the end of the input.</returns>
    private static ReadOnlySpan<char> ReadWord(ref ReadOnlySpan<char> source)
    {
        int length = 0;
        while (length < source.Length && !SvgNumberParser.IsWhitespace(source[length]))
        {
            length++;
        }

        ReadOnlySpan<char> word = source[..length];
        source = SvgNumberParser.SkipWhitespace(source[length..]);
        return word;
    }
}
