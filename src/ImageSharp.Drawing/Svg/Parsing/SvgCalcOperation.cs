// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One operation of the postfix program of an <see cref="SvgCalcExpression"/>.
/// </summary>
internal readonly struct SvgCalcOperation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCalcOperation"/> struct.
    /// </summary>
    /// <param name="code">The operation.</param>
    /// <param name="slot">The unit slot of a value, or the argument count of <c>min()</c> and <c>max()</c>.</param>
    /// <param name="value">The number of a value.</param>
    public SvgCalcOperation(SvgCalcOperationCode code, int slot, float value)
    {
        this.Code = code;
        this.Slot = slot;
        this.Value = value;
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    public SvgCalcOperationCode Code { get; }

    /// <summary>
    /// Gets, for <see cref="SvgCalcOperationCode.Value"/>, the unit slot: an <see cref="SvgLengthUnit"/> value, or
    /// <see cref="SvgCalc.AngleSlot"/> for an angle in degrees; for <see cref="SvgCalcOperationCode.Min"/> and
    /// <see cref="SvgCalcOperationCode.Max"/>, the number of arguments.
    /// </summary>
    public int Slot { get; }

    /// <summary>
    /// Gets the number of a <see cref="SvgCalcOperationCode.Value"/>.
    /// </summary>
    public float Value { get; }
}
