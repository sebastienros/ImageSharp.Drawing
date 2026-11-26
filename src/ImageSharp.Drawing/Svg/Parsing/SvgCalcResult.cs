// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of a math function after parsing: a number, a percentage, a length in one unit, or an angle, each folded
/// to one number; or an expression that mixes units.
/// </summary>
internal readonly struct SvgCalcResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCalcResult"/> struct.
    /// </summary>
    /// <param name="kind">The type of the result.</param>
    /// <param name="value">The folded number, percentage, length, or angle in degrees.</param>
    /// <param name="unit">The unit of a <see cref="SvgCalcKind.Length"/>.</param>
    /// <param name="expression">The expression of a <see cref="SvgCalcKind.Mixed"/> result.</param>
    public SvgCalcResult(SvgCalcKind kind, double value, SvgLengthUnit unit, SvgCalcExpression? expression)
    {
        this.Kind = kind;
        this.Value = value;
        this.Unit = unit;
        this.Expression = expression;
    }

    /// <summary>
    /// Gets the type of the result.
    /// </summary>
    public SvgCalcKind Kind { get; }

    /// <summary>
    /// Gets the folded value: the number, the percentage (50 for 50%), the length in <see cref="Unit"/>, or the angle in
    /// degrees. Zero for <see cref="SvgCalcKind.Mixed"/>.
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// Gets the unit of a <see cref="SvgCalcKind.Length"/>.
    /// </summary>
    public SvgLengthUnit Unit { get; }

    /// <summary>
    /// Gets the expression of a <see cref="SvgCalcKind.Mixed"/> result; otherwise <see langword="null"/>.
    /// </summary>
    public SvgCalcExpression? Expression { get; }
}
