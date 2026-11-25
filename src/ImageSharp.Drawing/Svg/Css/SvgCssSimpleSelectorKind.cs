// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of a simple selector. Phase 1 supports these four. Later phases add attribute selectors, pseudo-classes, and
/// functional pseudo-classes such as <c>:not()</c> as new members, matched by the same compound loop.
/// </summary>
internal enum SvgCssSimpleSelectorKind : byte
{
    /// <summary>
    /// A type selector such as <c>rect</c>. Matches the element local name, case-sensitively as XML names are.
    /// </summary>
    Type,

    /// <summary>
    /// The universal selector <c>*</c>.
    /// </summary>
    Universal,

    /// <summary>
    /// An id selector such as <c>#logo</c>.
    /// </summary>
    Id,

    /// <summary>
    /// A class selector such as <c>.st0</c>.
    /// </summary>
    Class
}
