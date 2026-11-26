// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The document state that computed values need beyond the specified styles: the parser and the budget of
/// <c>var()</c> substitution, the math functions that mix units, and the font metrics of the <c>ch</c> unit. One instance
/// serves one resolve; <see cref="SvgUnitBasis.Context"/> carries it to every place that computes lengths.
/// </summary>
/// <remarks>
/// <para>
/// Values that substitution produces are parsed again at computed-value time. Their dash lengths and math functions go
/// to tables of this context, whose references are negative (<see cref="SvgDashArray.Start"/>, and
/// <see cref="SvgCalcTable.NegativeIndices"/>), so that the tables of the element tree stay unchanged after the read.
/// </para>
/// <para>
/// Everything here is created on first use, so a document without custom properties, mixed math functions, or
/// <c>ch</c> pays only for the instance. It is not thread-safe; the resolve stage is single-threaded.
/// </para>
/// </remarks>
internal sealed class SvgStyleContext
{
    private readonly SvgCalcTable? documentCalc;
    private readonly SvgLoadOptions options;
    private SvgPropertyParser? parser;
    private SvgCalcTable? substitutionCalc;
    private SvgFontMatcher? fonts;
    private Dictionary<SvgFontChain, float>? zeroAdvances;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgStyleContext"/> class.
    /// </summary>
    /// <param name="documentCalc">The math functions of the document, from the element tree, or <see langword="null"/>.</param>
    /// <param name="options">The load options, which name the fonts.</param>
    public SvgStyleContext(SvgCalcTable? documentCalc, SvgLoadOptions options)
    {
        this.documentCalc = documentCalc;
        this.options = options;
    }

    /// <summary>
    /// Gets the substitution budget of the document.
    /// </summary>
    public SvgSubstitutionBudget Budget { get; } = new();

    /// <summary>
    /// Gets the parser for substituted values, with its own color cache and dash length table.
    /// </summary>
    public SvgPropertyParser Parser => this.parser ??= new SvgPropertyParser();

    /// <summary>
    /// Gets the table for the math functions of substituted values.
    /// </summary>
    public SvgCalcTable SubstitutionCalc => this.substitutionCalc ??= new SvgCalcTable(true);

    /// <summary>
    /// Returns the expression of a length of unit <see cref="SvgLengthUnit.Calc"/>.
    /// </summary>
    /// <param name="value">The length.</param>
    /// <returns>The expression, or <see langword="null"/> when no table has it.</returns>
    public SvgCalcExpression? GetCalc(SvgLength value)
        => value.Value < 0F ? this.substitutionCalc?.Get(value) : this.documentCalc?.Get(value);

    /// <summary>
    /// Converts a dash array that the parser of this context produced into the form that the resolve stage reads from
    /// this context: a negative start, so it does not index the dash length table of the element tree.
    /// </summary>
    /// <param name="value">The dash array from <see cref="Parser"/>.</param>
    /// <returns>The dash array with its start moved below zero; <c>none</c> stays as it is.</returns>
    public static SvgDashArray ToContextDashArray(SvgDashArray value)
        => value.IsNone ? value : new SvgDashArray(-value.Start - 1, value.Count);

    /// <summary>
    /// Gets the lengths of a dash array whose start is negative, which substitution produced.
    /// </summary>
    /// <param name="value">The dash array.</param>
    /// <returns>The lengths.</returns>
    public ReadOnlySpan<SvgLength> GetDashArray(SvgDashArray value)
        => CollectionsMarshal.AsSpan(this.Parser.DashLengths).Slice(-value.Start - 1, value.Count);

    /// <summary>
    /// Returns the advance of "0" in the first available font of a text style, in ems, which is the <c>ch</c> unit of
    /// CSS Values 4. Results are cached per font.
    /// </summary>
    /// <param name="text">The computed text style.</param>
    /// <returns>The advance in ems; 0.5 when no font is available or the font has no "0".</returns>
    public float GetZeroAdvance(SvgTextStyle text)
    {
        this.fonts ??= new SvgFontMatcher(this.options);
        SvgFontChain chain = this.fonts.GetChain(text);
        this.zeroAdvances ??= [];
        if (this.zeroAdvances.TryGetValue(chain, out float advance))
        {
            return advance;
        }

        advance = 0.5F;
        if (chain.Primary is SvgFontFace face
            && face.HasGlyph('0')
            && face.Metrics.TryGetGlyphMetrics(
                new CodePoint('0'),
                TextAttributes.None,
                TextDecorations.None,
                LayoutMode.HorizontalTopBottom,
                ColorFontSupport.None,
                default,
                out FontGlyphMetrics? metrics)
            && metrics is not null
            && metrics.UnitsPerEm > 0)
        {
            advance = metrics.AdvanceWidth / (float)metrics.UnitsPerEm;
        }

        this.zeroAdvances.Add(chain, advance);
        return advance;
    }
}
