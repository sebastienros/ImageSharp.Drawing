// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.Fonts.Tables.AdvancedTypographic;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One face of the font collection, with the properties that CSS font matching compares.
/// </summary>
/// <remarks>
/// A face is reached through a SixLabors.Fonts family and one of its style slots, because that is how the public API
/// creates a <see cref="Font"/>. <see cref="Weight"/> is the OS/2 weight class, <see cref="Italic"/> the italic or oblique
/// flag of the face, and <see cref="Width"/> the OS/2 width class as a percentage.
/// </remarks>
internal sealed class SvgFontFace
{
    private static readonly Tag GlyfTag = Tag.Parse("glyf");
    private static readonly Tag CffTag = Tag.Parse("CFF ");
    private static readonly Tag Cff2Tag = Tag.Parse("CFF2");

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFontFace"/> class.
    /// </summary>
    /// <param name="family">The family that holds the face.</param>
    /// <param name="style">The style slot of the face in <paramref name="family"/>.</param>
    /// <param name="metrics">The metrics of the face.</param>
    /// <param name="width">The width of the face as a percentage of the normal width.</param>
    public SvgFontFace(FontFamily family, FontStyle style, FontMetrics metrics, float width)
    {
        this.Family = family;
        this.Style = style;
        this.Metrics = metrics;
        this.Weight = (float)metrics.Description.Weight;
        this.Italic = (metrics.Description.Style & FontStyle.Italic) != 0;
        this.Width = width;

        // A font at an arbitrary size answers coverage questions, which do not depend on the size.
        this.CoverageFont = family.CreateFont(16F, style);

        // Bitmap fonts (CBDT, sbix) have no outline table, and glyph outlines are all that SVG text draws.
        this.HasOutlines = metrics.TryGetTableData(GlyfTag, out _) || metrics.TryGetTableData(CffTag, out _) || metrics.TryGetTableData(Cff2Tag, out _);
    }

    /// <summary>
    /// Gets a value indicating whether the face has glyph outlines: a <c>glyf</c>, <c>CFF</c>, or <c>CFF2</c> table.
    /// </summary>
    public bool HasOutlines { get; }

    /// <summary>
    /// Gets the family that holds the face.
    /// </summary>
    public FontFamily Family { get; }

    /// <summary>
    /// Gets the style slot of the face in <see cref="Family"/>.
    /// </summary>
    public FontStyle Style { get; }

    /// <summary>
    /// Gets the metrics of the face.
    /// </summary>
    public FontMetrics Metrics { get; }

    /// <summary>
    /// Gets the weight of the face, from 1 to 1000.
    /// </summary>
    public float Weight { get; }

    /// <summary>
    /// Gets a value indicating whether the face is italic or oblique.
    /// </summary>
    public bool Italic { get; }

    /// <summary>
    /// Gets the width of the face as a percentage of the normal width.
    /// </summary>
    public float Width { get; }

    /// <summary>
    /// Gets a font of the face that answers whether the face has a glyph for a character.
    /// </summary>
    public Font CoverageFont { get; }

    /// <summary>
    /// Returns whether the face has a glyph for a code point.
    /// </summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> if the face maps the code point to a glyph; otherwise, <see langword="false"/>.</returns>
    public bool HasGlyph(int codePoint)
        => this.CoverageFont.TryGetGlyphId(new SixLabors.Fonts.Unicode.CodePoint(codePoint), out ushort glyph) && glyph != 0;
}
