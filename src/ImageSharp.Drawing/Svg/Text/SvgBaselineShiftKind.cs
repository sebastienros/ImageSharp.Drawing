// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of a <c>baseline-shift</c> value. A length or percentage keeps its amount in <see cref="SvgTextStyle.BaselineShift"/>.
/// </summary>
internal enum SvgBaselineShiftKind : byte
{
    /// <summary>
    /// <c>baseline</c>, the initial value: no shift.
    /// </summary>
    Baseline,

    /// <summary>
    /// <c>sub</c>.
    /// </summary>
    Sub,

    /// <summary>
    /// <c>super</c>.
    /// </summary>
    Super,

    /// <summary>
    /// A length, or a percentage of the line height, in <see cref="SvgTextStyle.BaselineShift"/>.
    /// </summary>
    Length
}
