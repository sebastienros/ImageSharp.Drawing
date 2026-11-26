// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One typographic character of laid out text: a run of characters that renders as a unit, with its glyphs. Stage 3
/// creates the clusters; stages 4 to 6 set and change their position.
/// </summary>
/// <remarks>
/// <para>
/// A cluster is at least one grapheme. It holds several graphemes only when the font forms a ligature and no character
/// after the first grapheme has its own position value (and <c>letter-spacing</c> is zero), so position values never
/// fall inside a cluster except on code points of the first grapheme, whose values other than the first are ignored, as
/// SVG 2 defines for typographic characters.
/// </para>
/// <para>
/// The position fields are in user units of the <c>text</c> element: <see cref="X"/> and <see cref="Y"/> are the pen
/// position of the cluster on the alphabetic baseline, <see cref="Rotation"/> turns the glyphs about it, and
/// <see cref="ScaleX"/> scales them along the inline direction about it. The glyphs of the cluster follow each other
/// from the pen position by their advances.
/// </para>
/// </remarks>
internal struct SvgTextCluster
{
    /// <summary>
    /// Gets or sets the index of the first character in <see cref="SvgTextContent.Text"/>.
    /// </summary>
    public int CharStart { readonly get; set; }

    /// <summary>
    /// Gets or sets the index after the last character.
    /// </summary>
    public int CharEnd { readonly get; set; }

    /// <summary>
    /// Gets or sets the index of the first glyph in the glyph list of the layout.
    /// </summary>
    public int GlyphStart { readonly get; set; }

    /// <summary>
    /// Gets or sets the number of glyphs. Zero for a cluster without glyphs, such as text for which no font exists.
    /// </summary>
    public int GlyphCount { readonly get; set; }

    /// <summary>
    /// Gets or sets the sum of the glyph advances, before <c>letter-spacing</c> and <c>word-spacing</c>.
    /// </summary>
    public float Advance { readonly get; set; }

    /// <summary>
    /// Gets or sets the font of the glyphs, or <see langword="null"/> when no font exists. Its metrics give the cell of
    /// the cluster for the bounding box.
    /// </summary>
    public Font? Font { readonly get; set; }

    /// <summary>
    /// Gets or sets the span of the first character.
    /// </summary>
    public int Span { readonly get; set; }

    /// <summary>
    /// Gets or sets the horizontal pen position. Stage 4 sets it.
    /// </summary>
    public float X { readonly get; set; }

    /// <summary>
    /// Gets or sets the vertical pen position, on the alphabetic baseline. Stage 4 sets it.
    /// </summary>
    public float Y { readonly get; set; }

    /// <summary>
    /// Gets or sets the rotation in degrees, clockwise, about the pen position.
    /// </summary>
    public float Rotation { readonly get; set; }

    /// <summary>
    /// Gets or sets the scale along the inline direction about the pen position. It is 1 unless package T2 applies
    /// <c>lengthAdjust="spacingAndGlyphs"</c>.
    /// </summary>
    public float ScaleX { readonly get; set; }

    /// <summary>
    /// Gets or sets the advance that moved the pen after the cluster: <see cref="Advance"/> with <c>letter-spacing</c>
    /// and <c>word-spacing</c>. Stage 4 sets it; text anchoring measures chunks with it.
    /// </summary>
    public float Width { readonly get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cluster does not render because its element is not visible. A hidden
    /// cluster still takes its space and counts for the bounding box.
    /// </summary>
    public bool Hidden { readonly get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cluster is left out of the layout: it neither renders nor counts for
    /// the bounding box. Stage 6 sets it for clusters on a <c>textPath</c> until package T2 places them, and T2 for
    /// clusters that fall outside their path.
    /// </summary>
    public bool Skipped { readonly get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cluster starts a text chunk: its first character has an absolute
    /// position in <c>x</c> or <c>y</c>, or it is the first cluster.
    /// </summary>
    public bool StartsChunk { readonly get; set; }
}
