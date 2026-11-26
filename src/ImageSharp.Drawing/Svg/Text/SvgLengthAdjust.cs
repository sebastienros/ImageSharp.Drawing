// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>lengthAdjust</c> attribute of <c>text</c> and <c>tspan</c>.
/// </summary>
internal enum SvgLengthAdjust : byte
{
    /// <summary>
    /// <c>spacing</c>, the default: only the advances change.
    /// </summary>
    Spacing,

    /// <summary>
    /// <c>spacingAndGlyphs</c>: the glyphs also scale along the inline direction.
    /// </summary>
    SpacingAndGlyphs
}
