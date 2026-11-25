// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One complex selector of a style rule with the declarations of the rule. A rule with a selector list becomes one entry
/// per selector, because each selector has its own specificity; the entries share the declaration blocks.
/// </summary>
internal readonly struct SvgCssRule
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssRule"/> struct.
    /// </summary>
    /// <param name="compoundStart">The index of the leftmost compound selector in the style sheet table.</param>
    /// <param name="compoundCount">The number of compound selectors.</param>
    /// <param name="specificity">The packed specificity.</param>
    /// <param name="order">The position of the rule in the style sheets, in document order.</param>
    /// <param name="normalStyle">The index of the normal declarations in the style table, or -1.</param>
    /// <param name="importantStyle">The index of the <c>!important</c> declarations in the style table, or -1.</param>
    /// <param name="isUser">Whether the rule comes from the user style sheet.</param>
    /// <param name="media">The index of the media condition of the rule in the style sheet, or -1 for none.</param>
    public SvgCssRule(
        int compoundStart,
        int compoundCount,
        int specificity,
        int order,
        int normalStyle,
        int importantStyle,
        bool isUser = false,
        int media = -1)
    {
        this.CompoundStart = compoundStart;
        this.CompoundCount = compoundCount;
        this.Specificity = specificity;
        this.Order = order;
        this.NormalStyle = normalStyle;
        this.ImportantStyle = importantStyle;
        this.IsUser = isUser;
        this.Media = media;
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
    /// Gets the specificity packed as <c>(ids &lt;&lt; 16) | (classes &lt;&lt; 8) | types</c>, each count saturated at
    /// 255, so comparing the integers compares the specificities.
    /// </summary>
    public int Specificity { get; }

    /// <summary>
    /// Gets the position of the rule in the style sheets, in document order. It breaks specificity ties.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Gets the index of the normal declarations in the style table, or -1 when there are none.
    /// </summary>
    public int NormalStyle { get; }

    /// <summary>
    /// Gets the index of the <c>!important</c> declarations in the style table, or -1 when there are none.
    /// </summary>
    public int ImportantStyle { get; }

    /// <summary>
    /// Gets a value indicating whether the rule comes from the user style sheet. User rules have the user origin of
    /// CSS Cascade Level 4: their normal declarations rank below every author declaration, presentation attributes
    /// included, and their <c>!important</c> declarations rank above every author declaration.
    /// </summary>
    public bool IsUser { get; }

    /// <summary>
    /// Gets the index of the media condition that the rule depends on, or -1 when it applies on every medium.
    /// </summary>
    public int Media { get; }
}
