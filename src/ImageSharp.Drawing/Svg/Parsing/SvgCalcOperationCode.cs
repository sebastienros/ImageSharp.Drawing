// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The operations of the program of an <see cref="SvgCalcExpression"/>.
/// </summary>
internal enum SvgCalcOperationCode : byte
{
    /// <summary>
    /// Pushes a number with a unit.
    /// </summary>
    Value,

    /// <summary>
    /// Pops two values and pushes their sum.
    /// </summary>
    Add,

    /// <summary>
    /// Pops two values and pushes the first minus the second.
    /// </summary>
    Subtract,

    /// <summary>
    /// Pops two values and pushes their product.
    /// </summary>
    Multiply,

    /// <summary>
    /// Pops two values and pushes the first divided by the second.
    /// </summary>
    Divide,

    /// <summary>
    /// Pops the arguments of <c>min()</c> and pushes the smallest.
    /// </summary>
    Min,

    /// <summary>
    /// Pops the arguments of <c>max()</c> and pushes the largest.
    /// </summary>
    Max,

    /// <summary>
    /// Pops the three arguments of <c>clamp()</c> and pushes the clamped value.
    /// </summary>
    Clamp
}
