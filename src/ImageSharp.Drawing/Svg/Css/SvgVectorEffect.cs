// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>vector-effect</c> property. SVG 2 lists more values; browsers support only these two, so the others are invalid.
/// </summary>
internal enum SvgVectorEffect : byte
{
    /// <summary>
    /// <c>none</c>, the initial value.
    /// </summary>
    None,

    /// <summary>
    /// <c>non-scaling-stroke</c>: the stroke width is measured in the host coordinate space, not in user units.
    /// </summary>
    NonScalingStroke
}
