// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>writing-mode</c> property. The SVG 1.1 keywords map to the CSS Writing Modes values as CSS Writing Modes Level 3 defines: <c>lr</c>, <c>lr-tb</c>, <c>rl</c>, and <c>rl-tb</c> to <c>horizontal-tb</c>; <c>tb</c> and <c>tb-rl</c> to <c>vertical-rl</c>.
/// </summary>
internal enum SvgWritingMode : byte
{
    /// <summary>
    /// <c>horizontal-tb</c>, the initial value.
    /// </summary>
    HorizontalTb,

    /// <summary>
    /// <c>vertical-rl</c>.
    /// </summary>
    VerticalRl,

    /// <summary>
    /// <c>vertical-lr</c>.
    /// </summary>
    VerticalLr,

    /// <summary>
    /// <c>sideways-rl</c>.
    /// </summary>
    SidewaysRl,

    /// <summary>
    /// <c>sideways-lr</c>.
    /// </summary>
    SidewaysLr
}
