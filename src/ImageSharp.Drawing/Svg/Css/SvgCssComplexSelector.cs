// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One complex selector of the argument list of a functional pseudo-class: a range of compound selectors and its
/// specificity.
/// </summary>
internal readonly struct SvgCssComplexSelector
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssComplexSelector"/> struct.
    /// </summary>
    /// <param name="compoundStart">The index of the leftmost compound selector in the style sheet table.</param>
    /// <param name="compoundCount">The number of compound selectors.</param>
    /// <param name="specificity">The packed specificity, as <see cref="SvgCssRule.Specificity"/> packs it.</param>
    public SvgCssComplexSelector(int compoundStart, int compoundCount, int specificity)
    {
        this.CompoundStart = compoundStart;
        this.CompoundCount = compoundCount;
        this.Specificity = specificity;
    }

    /// <summary>
    /// Gets the index of the leftmost compound selector in the style sheet table.
    /// </summary>
    public int CompoundStart { get; }

    /// <summary>
    /// Gets the number of compound selectors.
    /// </summary>
    public int CompoundCount { get; }

    /// <summary>
    /// Gets the packed specificity.
    /// </summary>
    public int Specificity { get; }
}
