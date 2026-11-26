// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The type of the result of a math function, as CSS Values 4 computes it from its arguments.
/// </summary>
internal enum SvgCalcKind : byte
{
    /// <summary>
    /// A number.
    /// </summary>
    Number,

    /// <summary>
    /// A percentage.
    /// </summary>
    Percent,

    /// <summary>
    /// A length in one unit.
    /// </summary>
    Length,

    /// <summary>
    /// An angle, in degrees.
    /// </summary>
    Angle,

    /// <summary>
    /// A length or percentage that mixes units, kept as an <see cref="SvgCalcExpression"/>.
    /// </summary>
    Mixed
}
