// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A CSS token as a range of the source text, so tokenizing allocates nothing.
/// </summary>
internal readonly struct SvgCssToken
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssToken"/> struct.
    /// </summary>
    /// <param name="kind">The token kind.</param>
    /// <param name="start">The index of the first character of the token.</param>
    /// <param name="end">The index after the last character of the token.</param>
    /// <param name="valueStart">The index of the first character of the token value.</param>
    /// <param name="valueEnd">The index after the last character of the token value.</param>
    /// <param name="hasEscape">Whether the value contains escape sequences.</param>
    /// <param name="isIdHash">Whether a hash token value would start an identifier.</param>
    public SvgCssToken(SvgCssTokenKind kind, int start, int end, int valueStart, int valueEnd, bool hasEscape, bool isIdHash)
    {
        this.Kind = kind;
        this.Start = start;
        this.End = end;
        this.ValueStart = valueStart;
        this.ValueEnd = valueEnd;
        this.HasEscape = hasEscape;
        this.IsIdHash = isIdHash;
    }

    /// <summary>
    /// Gets the token kind.
    /// </summary>
    public SvgCssTokenKind Kind { get; }

    /// <summary>
    /// Gets the index of the first character of the token.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the index after the last character of the token.
    /// </summary>
    public int End { get; }

    /// <summary>
    /// Gets the index of the first character of the value: the name of an identifier, function, at-keyword, or hash
    /// without its sigils; the content of a string or URL without quotes; the unit of a dimension; or the character of a
    /// delimiter.
    /// </summary>
    public int ValueStart { get; }

    /// <summary>
    /// Gets the index after the last character of the value.
    /// </summary>
    public int ValueEnd { get; }

    /// <summary>
    /// Gets a value indicating whether the value contains escape sequences, so its raw characters differ from its meaning.
    /// </summary>
    public bool HasEscape { get; }

    /// <summary>
    /// Gets a value indicating whether a hash token has the "id" type flag, which an id selector requires.
    /// </summary>
    public bool IsIdHash { get; }

    /// <summary>
    /// Gets the raw value characters.
    /// </summary>
    /// <param name="source">The tokenized source.</param>
    /// <returns>The value characters, with escapes undecoded.</returns>
    public ReadOnlySpan<char> GetRawValue(ReadOnlySpan<char> source) => source[this.ValueStart..this.ValueEnd];

    /// <summary>
    /// Gets the value as a string, with escape sequences decoded.
    /// </summary>
    /// <param name="source">The tokenized source.</param>
    /// <returns>The decoded value.</returns>
    public string GetValue(ReadOnlySpan<char> source)
        => this.HasEscape ? SvgCssTokenizer.Unescape(this.GetRawValue(source)) : this.GetRawValue(source).ToString();

    /// <summary>
    /// Returns whether the token is a delimiter with the given character.
    /// </summary>
    /// <param name="source">The tokenized source.</param>
    /// <param name="delimiter">The character.</param>
    /// <returns><see langword="true"/> if the token is that delimiter; otherwise, <see langword="false"/>.</returns>
    public bool IsDelimiter(ReadOnlySpan<char> source, char delimiter)
        => this.Kind == SvgCssTokenKind.Delim && source[this.Start] == delimiter;
}
