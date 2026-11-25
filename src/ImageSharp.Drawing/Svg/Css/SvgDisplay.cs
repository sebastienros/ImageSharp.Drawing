// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified value of the <c>display</c> property. SVG rendering only distinguishes <c>none</c> from every other
/// value, so each valid CSS display keyword other than <c>none</c> maps to <see cref="Inline"/>.
/// </summary>
internal enum SvgDisplay : byte
{
    /// <summary>
    /// The element renders. This is the initial value.
    /// </summary>
    Inline,

    /// <summary>
    /// The element and its subtree do not render.
    /// </summary>
    None
}
