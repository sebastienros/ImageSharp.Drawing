// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified value of the <c>stroke-linejoin</c> property.
/// </summary>
internal enum SvgStrokeLineJoin : byte
{
    /// <summary>
    /// <c>miter</c>, the initial value.
    /// </summary>
    Miter,

    /// <summary>
    /// <c>miter-clip</c> (SVG 2).
    /// </summary>
    MiterClip,

    /// <summary>
    /// <c>round</c>.
    /// </summary>
    Round,

    /// <summary>
    /// <c>bevel</c>.
    /// </summary>
    Bevel,

    /// <summary>
    /// <c>arcs</c> (SVG 2).
    /// </summary>
    Arcs
}
