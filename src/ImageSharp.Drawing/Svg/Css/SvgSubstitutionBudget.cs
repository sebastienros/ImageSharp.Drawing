// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The number of characters that <c>var()</c> substitution may still produce for one document.
/// </summary>
/// <remarks>
/// Each substituted value is bounded by <see cref="SvgVariableSubstitution.MaxValueLength"/>, but a document can declare
/// many custom properties and the resolve stage computes the style of each <c>use</c> instance again. The budget bounds
/// the total: once it is spent, every further substitution fails, which makes its declaration invalid at computed-value
/// time. It is not thread-safe; the resolve stage is single-threaded.
/// </remarks>
internal sealed class SvgSubstitutionBudget
{
    /// <summary>
    /// The number of characters that the substitutions of one document may produce in total.
    /// </summary>
    public const long MaxCharacters = 16L * 1024 * 1024;

    private long remaining = MaxCharacters;

    /// <summary>
    /// Charges characters to the budget.
    /// </summary>
    /// <param name="count">The number of characters.</param>
    /// <returns><see langword="true"/> if the budget covered them; otherwise, <see langword="false"/>.</returns>
    public bool TryCharge(int count)
    {
        if (count > this.remaining)
        {
            this.remaining = 0;
            return false;
        }

        this.remaining -= count;
        return true;
    }
}
