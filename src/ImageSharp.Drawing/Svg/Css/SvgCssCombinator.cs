// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The combinator between a compound selector and the compound to its left. Later phases add the sibling combinators.
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
    Child
}
