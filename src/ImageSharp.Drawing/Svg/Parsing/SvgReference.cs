// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A specified <c>none | url(...)</c> value, as used by <c>clip-path</c>, <c>mask</c>, <c>marker-start</c>,
/// <c>marker-mid</c>, and <c>marker-end</c>. <c>default(SvgReference)</c> is <c>none</c>.
/// </summary>
/// <remarks>
/// The value has one field, so a computed style can hold several references cheaply. A <c>url(...)</c> that is not a
/// same-document fragment is stored as an empty string: it can never name an element, because the reader adds no empty
/// id to the id index. Such a reference behaves like a fragment that names no element.
/// </remarks>
internal readonly struct SvgReference
{
    private readonly string? value;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgReference"/> struct.
    /// </summary>
    /// <param name="value">
    /// <see langword="null"/> for <c>none</c>, an empty string for a URL that is not a same-document fragment, otherwise
    /// the fragment id without the leading <c>#</c>.
    /// </param>
    private SvgReference(string? value) => this.value = value;

    /// <summary>
    /// Gets a value indicating whether the value is <c>none</c>.
    /// </summary>
    public bool IsNone => this.value is null;

    /// <summary>
    /// Gets the id of the referenced element without the leading <c>#</c>, or <see langword="null"/> for <c>none</c> and
    /// for a URL that is not a same-document fragment.
    /// </summary>
    public string? Id => string.IsNullOrEmpty(this.value) ? null : this.value;

    /// <summary>
    /// Creates a <c>url(...)</c> reference.
    /// </summary>
    /// <param name="id">The fragment id without the leading <c>#</c>, or <see langword="null"/> when the URL is not a same-document fragment.</param>
    /// <returns>The reference.</returns>
    public static SvgReference FromUrl(string? id) => new(id ?? string.Empty);

    /// <summary>
    /// Parses <c>none</c> or <c>url(...)</c>. <c>none</c> is ASCII case-insensitive. Nothing may follow the closing
    /// parenthesis.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the reference.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> source, out SvgReference value)
    {
        source = SvgNumberParser.Trim(source);
        value = default;
        if (source.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // The first closing parenthesis ends the URL, as for paint, and it must end the value.
        if (source.Length <= 4 || !source[..4].Equals("url(", StringComparison.OrdinalIgnoreCase) || source.IndexOf(')') != source.Length - 1)
        {
            return false;
        }

        string? id = SvgPaint.ParseUrlReference(source[4..^1], out bool valid);
        if (!valid)
        {
            return false;
        }

        value = FromUrl(id);
        return true;
    }
}
