// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One glyph of the output of stage 3 of the text layout pipeline (<see cref="SvgTextShaper"/>).
/// </summary>
/// <remarks>
/// Lengths are in user units, with the y axis pointing down: <see cref="Offset"/> places the glyph origin relative to its
/// pen position, and <see cref="Advance"/> moves the pen along the inline direction.
/// </remarks>
internal struct SvgShapedGlyph
{
    /// <summary>
    /// Gets or sets the glyph id in <see cref="Font"/>.
    /// </summary>
    public ushort GlyphId { readonly get; set; }

    /// <summary>
    /// Gets or sets the font at its size, which also selects bold and italic synthesis.
    /// </summary>
    public Font Font { readonly get; set; }

    /// <summary>
    /// Gets or sets the horizontal advance.
    /// </summary>
    public float Advance { readonly get; set; }

    /// <summary>
    /// Gets or sets the offset of the glyph origin from its pen position, y down.
    /// </summary>
    public Vector2 Offset { readonly get; set; }
}
