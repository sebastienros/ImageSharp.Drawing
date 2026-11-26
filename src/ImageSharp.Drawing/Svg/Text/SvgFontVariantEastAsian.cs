// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of <c>font-variant-east-asian</c> (CSS Fonts Level 4): <c>normal</c> (no flag) or a set of keywords with
/// at most one variant form, at most one width, and <c>ruby</c>.
/// </summary>
[Flags]
internal enum SvgFontVariantEastAsian : ushort
{
    /// <summary>
    /// <c>normal</c>, the initial value.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// <c>jis78</c> (<c>jp78</c>).
    /// </summary>
    Jis78 = 1 << 0,

    /// <summary>
    /// <c>jis83</c> (<c>jp83</c>).
    /// </summary>
    Jis83 = 1 << 1,

    /// <summary>
    /// <c>jis90</c> (<c>jp90</c>).
    /// </summary>
    Jis90 = 1 << 2,

    /// <summary>
    /// <c>jis04</c> (<c>jp04</c>).
    /// </summary>
    Jis04 = 1 << 3,

    /// <summary>
    /// <c>simplified</c> (<c>smpl</c>).
    /// </summary>
    Simplified = 1 << 4,

    /// <summary>
    /// <c>traditional</c> (<c>trad</c>).
    /// </summary>
    Traditional = 1 << 5,

    /// <summary>
    /// <c>full-width</c> (<c>fwid</c>).
    /// </summary>
    FullWidth = 1 << 6,

    /// <summary>
    /// <c>proportional-width</c> (<c>pwid</c>).
    /// </summary>
    ProportionalWidth = 1 << 7,

    /// <summary>
    /// <c>ruby</c> (<c>ruby</c>).
    /// </summary>
    Ruby = 1 << 8
}
