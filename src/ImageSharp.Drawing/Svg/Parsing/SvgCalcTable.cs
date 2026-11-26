// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The math functions of one document that mix units (<see cref="SvgCalcExpression"/>). A length of unit
/// <see cref="SvgLengthUnit.Calc"/> holds the index of its expression here.
/// </summary>
/// <remarks>
/// <para>
/// Lengths are parsed by static methods (<see cref="SvgLength.TryParse(ReadOnlySpan{char}, out SvgLength)"/>) from many
/// places of the reader, so the table of the document being read is the ambient <see cref="Current"/> of the reading
/// thread, which <see cref="Enter"/> sets for the duration of a read. Outside a read there is no table, and a math function
/// that mixes units is invalid; one with a single unit still parses into a plain length.
/// </para>
/// <para>
/// The element tree keeps the table after the read; it does not change after that, so concurrent reads are safe. The
/// resolve stage reaches it through <see cref="SvgStyleContext"/>.
/// </para>
/// </remarks>
internal sealed class SvgCalcTable
{
    /// <summary>
    /// The largest number of expressions in one document. A math function that mixes units beyond it is invalid, which
    /// bounds the memory a hostile document can take with them.
    /// </summary>
    public const int MaxExpressions = 65536;

    [ThreadStatic]
    private static SvgCalcTable? current;

    // Created on the first expression, so documents without one allocate nothing more.
    private List<SvgCalcExpression>? expressions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCalcTable"/> class.
    /// </summary>
    /// <param name="negativeIndices">
    /// Whether the indices of this table are negative (-1 for the first expression), so that its lengths and those of
    /// another table can be told apart: the resolve stage keeps the math functions of substituted <c>var()</c> values
    /// apart from those of the document, whose table does not change after the read.
    /// </param>
    public SvgCalcTable(bool negativeIndices = false) => this.NegativeIndices = negativeIndices;

    /// <summary>
    /// Gets a value indicating whether the indices of this table are negative.
    /// </summary>
    public bool NegativeIndices { get; }

    /// <summary>
    /// Gets the table of the document that the current thread reads, or <see langword="null"/> outside a read.
    /// </summary>
    public static SvgCalcTable? Current => current;

    /// <summary>
    /// Gets the number of expressions.
    /// </summary>
    public int Count => this.expressions?.Count ?? 0;

    /// <summary>
    /// Makes a table the ambient table of the current thread. Pass the result to <see cref="Exit"/> when the read ends.
    /// </summary>
    /// <param name="table">The table of the document being read.</param>
    /// <returns>The previous ambient table, which a nested read restores.</returns>
    public static SvgCalcTable? Enter(SvgCalcTable table)
    {
        SvgCalcTable? previous = current;
        current = table;
        return previous;
    }

    /// <summary>
    /// Restores the ambient table that was current before <see cref="Enter"/>.
    /// </summary>
    /// <param name="previous">The result of <see cref="Enter"/>.</param>
    public static void Exit(SvgCalcTable? previous) => current = previous;

    /// <summary>
    /// Adds an expression.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="index">When this method returns <see langword="true"/>, contains the index of the expression.</param>
    /// <returns><see langword="true"/> if the expression was added; <see langword="false"/> when the table is full.</returns>
    public bool TryAdd(SvgCalcExpression expression, out int index)
    {
        this.expressions ??= [];
        index = this.expressions.Count;
        if (index >= MaxExpressions)
        {
            return false;
        }

        this.expressions.Add(expression);
        index = this.NegativeIndices ? -index - 1 : index;
        return true;
    }

    /// <summary>
    /// Returns the expression of a length of unit <see cref="SvgLengthUnit.Calc"/>.
    /// </summary>
    /// <param name="value">The length.</param>
    /// <returns>The expression, or <see langword="null"/> when the length does not belong to this table.</returns>
    public SvgCalcExpression? Get(SvgLength value)
    {
        int index = (int)value.Value;
        index = this.NegativeIndices ? -index - 1 : index;
        return this.expressions is not null && index >= 0 && index < this.expressions.Count ? this.expressions[index] : null;
    }
}
