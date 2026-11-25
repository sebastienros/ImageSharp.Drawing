// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The combinator between a compound selector and the compound to its left (Selectors Level 4, section 16).
/// </summary>
internal enum SvgCssCombinator : byte
{
    /// <summary>
    /// No combinator: the leftmost compound of a complex selector.
    /// </summary>
    None,

    /// <summary>
    /// The descendant combinator, written as whitespace.
    /// </summary>
    Descendant,

    /// <summary>
    /// The child combinator <c>&gt;</c>.
    /// </summary>
    Child,

    /// <summary>
    /// The next-sibling combinator <c>+</c>: the left compound matches the previous element sibling.
    /// </summary>
    NextSibling,

    /// <summary>
    /// The subsequent-sibling combinator <c>~</c>: the left compound matches any earlier element sibling.
    /// </summary>
    SubsequentSibling
}
