// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A math function that mixes units, such as <c>calc(50% - 10px)</c> or <c>min(10em, 100px)</c>, kept as a program until
/// the size that percentages refer to and the font size are known. <see cref="SvgCalc"/> builds it; an
/// <see cref="SvgLength"/> of unit <see cref="SvgLengthUnit.Calc"/> refers to it through the <see cref="SvgCalcTable"/>
/// of the document.
/// </summary>
/// <remarks>
/// The program is in postfix order: each operation pops its operands from a stack of values and pushes its result.
/// Lengths convert to user units when the program runs, so <c>em</c> uses the font size of the element that resolves the
/// value. Instances never change after they are created.
/// </remarks>
internal sealed class SvgCalcExpression
{
    private readonly SvgCalcOperation[] operations;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCalcExpression"/> class.
    /// </summary>
    /// <param name="operations">The program in postfix order, which the instance takes over.</param>
    /// <param name="hasPercentage">Whether the expression contains a percentage.</param>
    /// <param name="clampNonNegative">Whether a negative result becomes zero.</param>
    public SvgCalcExpression(SvgCalcOperation[] operations, bool hasPercentage, bool clampNonNegative)
    {
        this.operations = operations;
        this.HasPercentage = hasPercentage;
        this.ClampNonNegative = clampNonNegative;
    }

    /// <summary>
    /// Gets a value indicating whether the expression contains a percentage, so its value depends on the size that
    /// percentages refer to.
    /// </summary>
    public bool HasPercentage { get; }

    /// <summary>
    /// Gets a value indicating whether a negative result becomes zero. CSS Values 4 clamps the result of a math function
    /// to the range of the property, and the lengths that must not be negative (<c>width</c>, <c>r</c>, ...) have the
    /// range zero and more.
    /// </summary>
    public bool ClampNonNegative { get; }

    /// <summary>
    /// Returns an expression with the same program whose negative results become zero.
    /// </summary>
    /// <returns>The expression; this instance when it already clamps.</returns>
    public SvgCalcExpression WithClampNonNegative()
        => this.ClampNonNegative ? this : new SvgCalcExpression(this.operations, this.HasPercentage, true);

    /// <summary>
    /// Runs the program.
    /// </summary>
    /// <param name="percentBase">The size that 100% refers to.</param>
    /// <param name="fontSize">The font size that <c>em</c> refers to.</param>
    /// <param name="units">The document values that the other units refer to.</param>
    /// <returns>The value in user units; zero when the result is not finite.</returns>
    public float Evaluate(float percentBase, float fontSize, SvgUnitBasis units)
    {
        // The parser bounds the number of operations, so the stack fits on the stack.
        Span<double> stack = stackalloc double[SvgCalc.MaxTerms];
        int count = 0;
        foreach (SvgCalcOperation operation in this.operations)
        {
            switch (operation.Code)
            {
                case SvgCalcOperationCode.Value:
                    stack[count++] = ResolveValue(operation.Value, operation.Slot, percentBase, fontSize, units);
                    break;
                case SvgCalcOperationCode.Add:
                    count--;
                    stack[count - 1] += stack[count];
                    break;
                case SvgCalcOperationCode.Subtract:
                    count--;
                    stack[count - 1] -= stack[count];
                    break;
                case SvgCalcOperationCode.Multiply:
                    count--;
                    stack[count - 1] *= stack[count];
                    break;
                case SvgCalcOperationCode.Divide:
                    count--;
                    stack[count - 1] /= stack[count];
                    break;
                case SvgCalcOperationCode.Min:
                case SvgCalcOperationCode.Max:
                {
                    int first = count - operation.Slot;
                    double result = stack[first];
                    for (int i = first + 1; i < count; i++)
                    {
                        result = operation.Code == SvgCalcOperationCode.Min ? Math.Min(result, stack[i]) : Math.Max(result, stack[i]);
                    }

                    count = first;
                    stack[count++] = result;
                    break;
                }

                default:
                {
                    // clamp(min, value, max) is max(min, min(value, max)): the minimum wins over the maximum.
                    count -= 2;
                    stack[count - 1] = Math.Max(stack[count - 1], Math.Min(stack[count], stack[count + 1]));
                    break;
                }
            }
        }

        double value = stack[0];
        if (!double.IsFinite(value))
        {
            return 0F;
        }

        return this.ClampNonNegative && value < 0 ? 0F : (float)value;
    }

    /// <summary>
    /// Converts one value of the program to user units.
    /// </summary>
    /// <param name="value">The number as written.</param>
    /// <param name="slot">The slot: an <see cref="SvgLengthUnit"/> value, or <see cref="SvgCalc.AngleSlot"/>.</param>
    /// <param name="percentBase">The size that 100% refers to.</param>
    /// <param name="fontSize">The font size that <c>em</c> refers to.</param>
    /// <param name="units">The document values that the other units refer to.</param>
    /// <returns>The value.</returns>
    private static double ResolveValue(float value, int slot, float percentBase, float fontSize, SvgUnitBasis units)
        => slot switch
        {
            (int)SvgLengthUnit.Number or SvgCalc.AngleSlot => value,
            (int)SvgLengthUnit.Percent => (double)value * percentBase / 100D,
            _ => SvgUnits.ToPixels(new SvgLength(value, (SvgLengthUnit)slot), fontSize, units)
        };
}
