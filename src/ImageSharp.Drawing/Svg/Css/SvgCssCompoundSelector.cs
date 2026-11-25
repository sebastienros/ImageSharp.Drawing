// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A compound selector: a range of simple selectors that must all match one element, and the combinator that relates it
/// to the compound on its left.
/// </summary>
internal readonly struct SvgCssCompoundSelector
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssCompoundSelector"/> struct.
    /// </summary>
    /// <param name="simpleStart">The index of the first simple selector in the style sheet table.</param>
    /// <param name="simpleCount">The number of simple selectors.</param>
    /// <param name="combinator">The combinator to the compound on the left.</param>
    public SvgCssCompoundSelector(int simpleStart, int simpleCount, SvgCssCombinator combinator)
    {
        this.SimpleStart = simpleStart;
        this.SimpleCount = simpleCount;
        this.Combinator = combinator;
    }

    /// <summary>
    /// Gets the index of the first simple selector in the style sheet table.
    /// </summary>
    public int SimpleStart { get; }

    /// <summary>
    /// Gets the number of simple selectors.
    /// </summary>
    public int SimpleCount { get; }

    /// <summary>
    /// Gets the combinator to the compound on the left; <see cref="SvgCssCombinator.None"/> for the leftmost compound.
    /// </summary>
    public SvgCssCombinator Combinator { get; }
}
