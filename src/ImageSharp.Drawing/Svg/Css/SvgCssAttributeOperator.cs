// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The operator of an attribute selector (Selectors Level 4, section 6).
/// </summary>
internal enum SvgCssAttributeOperator : byte
{
    /// <summary>
    /// <c>[name]</c>: the attribute is present.
    /// </summary>
    Presence,

    /// <summary>
    /// <c>[name=value]</c>: the value equals the given value.
    /// </summary>
    Exact,

    /// <summary>
    /// <c>[name~=value]</c>: the value is a whitespace-separated list with an item equal to the given value.
    /// </summary>
    Includes,

    /// <summary>
    /// <c>[name|=value]</c>: the value equals the given value, or starts with it followed by <c>-</c>.
    /// </summary>
    DashMatch,

    /// <summary>
    /// <c>[name^=value]</c>: the value starts with the given value.
    /// </summary>
    Prefix,

    /// <summary>
    /// <c>[name$=value]</c>: the value ends with the given value.
    /// </summary>
    Suffix,

    /// <summary>
    /// <c>[name*=value]</c>: the value contains the given value.
    /// </summary>
    Substring
}
