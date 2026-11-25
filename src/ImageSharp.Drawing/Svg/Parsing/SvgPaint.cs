// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A specified SVG paint value, used by <c>fill</c>, <c>stroke</c>, and <c>stop-color</c>.
/// </summary>
internal readonly struct SvgPaint
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgPaint"/> struct.
    /// </summary>
    /// <param name="kind">The paint kind.</param>
    /// <param name="color">The color for <see cref="SvgPaintKind.Color"/>, or the fallback color for a reference.</param>
    /// <param name="reference">The referenced element id for <see cref="SvgPaintKind.Url"/>.</param>
    /// <param name="fallback">The fallback kind for <see cref="SvgPaintKind.Url"/>.</param>
    public SvgPaint(SvgPaintKind kind, Color color, string? reference, SvgPaintKind fallback)
    {
        this.Kind = kind;
        this.Color = color;
        this.Reference = reference;
        this.Fallback = fallback;
    }

    /// <summary>
    /// Gets the paint kind.
    /// </summary>
    public SvgPaintKind Kind { get; }

    /// <summary>
    /// Gets the color. For <see cref="SvgPaintKind.Color"/> it is the paint. For <see cref="SvgPaintKind.Url"/> with a
    /// <see cref="Fallback"/> of <see cref="SvgPaintKind.Color"/> it is the fallback color.
    /// </summary>
    public Color Color { get; }

    /// <summary>
    /// Gets the id of the referenced element, without the leading <c>#</c>, for <see cref="SvgPaintKind.Url"/>. It is
    /// <see langword="null"/> when the URL is not a same-document fragment, because phase 1 does not load external
    /// resources; such a reference never resolves.
    /// </summary>
    public string? Reference { get; }

    /// <summary>
    /// Gets the fallback paint for <see cref="SvgPaintKind.Url"/>: <see cref="SvgPaintKind.None"/> when no fallback is
    /// written, or <c>none</c>, <see cref="SvgPaintKind.CurrentColor"/>, or <see cref="SvgPaintKind.Color"/>. SVG paints
    /// nothing when the reference does not resolve and no fallback is written, so an absent fallback and an explicit
    /// <c>none</c> behave the same.
    /// </summary>
    public SvgPaintKind Fallback { get; }

    /// <summary>
    /// Parses a paint: <c>none</c>, <c>currentColor</c>, a color, <c>context-fill</c>, <c>context-stroke</c>, or
    /// <c>url(...)</c> followed by an optional fallback. The fallback cannot be a context keyword.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="cache">The color cache, or <see langword="null"/>.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the paint.</param>
    /// <returns><see langword="true"/> if the value is a paint; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> source, SvgColorCache? cache, out SvgPaint value)
    {
        source = SvgNumberParser.Trim(source);
        if (source.Length > 4 && source[..4].Equals("url(", StringComparison.OrdinalIgnoreCase))
        {
            int close = source.IndexOf(')');
            if (close < 0)
            {
                value = default;
                return false;
            }

            string? reference = ParseUrlReference(source[4..close], out bool valid);
            if (!valid)
            {
                value = default;
                return false;
            }

            ReadOnlySpan<char> fallbackText = SvgNumberParser.Trim(source[(close + 1)..]);
            if (fallbackText.IsEmpty)
            {
                value = new SvgPaint(SvgPaintKind.Url, default, reference, SvgPaintKind.None);
                return true;
            }

            if (!TryParseSimple(fallbackText, cache, out SvgPaint fallback))
            {
                value = default;
                return false;
            }

            value = new SvgPaint(SvgPaintKind.Url, fallback.Color, reference, fallback.Kind);
            return true;
        }

        // SVG 2: the context keywords take the fill or stroke of the context element, such as the shape that a marker
        // decorates. They are ASCII case-insensitive, as CSS keywords are.
        if (source.Equals("context-fill", StringComparison.OrdinalIgnoreCase))
        {
            value = new SvgPaint(SvgPaintKind.ContextFill, default, null, SvgPaintKind.None);
            return true;
        }

        if (source.Equals("context-stroke", StringComparison.OrdinalIgnoreCase))
        {
            value = new SvgPaint(SvgPaintKind.ContextStroke, default, null, SvgPaintKind.None);
            return true;
        }

        return TryParseSimple(source, cache, out value);
    }

    /// <summary>
    /// Parses a color value that also accepts <c>currentColor</c>, as <c>stop-color</c> does.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="cache">The color cache, or <see langword="null"/>.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, contains a paint of kind <see cref="SvgPaintKind.Color"/> or
    /// <see cref="SvgPaintKind.CurrentColor"/>.
    /// </param>
    /// <returns><see langword="true"/> if the value is a color or <c>currentColor</c>; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseColorOrCurrentColor(ReadOnlySpan<char> source, SvgColorCache? cache, out SvgPaint value)
    {
        source = SvgNumberParser.Trim(source);
        if (source.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
        {
            value = new SvgPaint(SvgPaintKind.CurrentColor, default, null, SvgPaintKind.None);
            return true;
        }

        if (SvgColorParser.TryParse(source, cache, out Color color))
        {
            value = new SvgPaint(SvgPaintKind.Color, color, null, SvgPaintKind.None);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Parses <c>none</c>, <c>currentColor</c>, or a color.
    /// </summary>
    /// <param name="source">The trimmed characters to parse.</param>
    /// <param name="cache">The color cache, or <see langword="null"/>.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the paint.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseSimple(ReadOnlySpan<char> source, SvgColorCache? cache, out SvgPaint value)
    {
        if (source.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            value = default;
            return true;
        }

        return TryParseColorOrCurrentColor(source, cache, out value);
    }

    /// <summary>
    /// Reads the content of <c>url(...)</c>: an optionally quoted URL with optional surrounding whitespace.
    /// </summary>
    /// <param name="content">The characters between the parentheses.</param>
    /// <param name="valid">When this method returns, indicates whether the content is well formed.</param>
    /// <returns>The fragment id for a same-document reference; otherwise, <see langword="null"/>.</returns>
    internal static string? ParseUrlReference(ReadOnlySpan<char> content, out bool valid)
    {
        content = SvgNumberParser.Trim(content);
        if (content.Length >= 2 && content[0] is '"' or '\'')
        {
            if (content[^1] != content[0])
            {
                valid = false;
                return null;
            }

            content = content[1..^1];
        }

        valid = !content.IsEmpty;

        // Only a fragment such as "#gradient" names an element in this document.
        return content.Length > 1 && content[0] == '#' ? content[1..].ToString() : null;
    }
}
