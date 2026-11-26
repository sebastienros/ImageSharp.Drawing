// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of <c>font-variant-position</c> (CSS Fonts Level 4), in keyword order.
/// </summary>
internal enum SvgFontVariantPosition : byte
{
    /// <summary>
    /// <c>normal</c>, the initial value.
    /// </summary>
    Normal,

    /// <summary>
    /// <c>sub</c> (<c>subs</c>).
    /// </summary>
    Sub,

    /// <summary>
    /// <c>super</c> (<c>sups</c>).
    /// </summary>
    Super
}
