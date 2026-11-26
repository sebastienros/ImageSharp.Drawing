// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified value of one custom property declaration (<c>--name: value</c>): its token text, or a CSS-wide keyword.
/// </summary>
internal readonly struct SvgCustomPropertyValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCustomPropertyValue"/> struct.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="text">The token text for <see cref="SvgCustomPropertyKind.Text"/>; otherwise empty.</param>
    public SvgCustomPropertyValue(SvgCustomPropertyKind kind, string text)
    {
        this.Kind = kind;
        this.Text = text;
    }

    /// <summary>
    /// Gets the kind of the value.
    /// </summary>
    public SvgCustomPropertyKind Kind { get; }

    /// <summary>
    /// Gets the token text of the value, without leading and trailing whitespace and without <c>!important</c>, as
    /// written: comments and escapes are kept for the tokenizer of the substitution. It can be empty, which CSS allows.
    /// Empty for a keyword.
    /// </summary>
    public string Text { get; }
}
