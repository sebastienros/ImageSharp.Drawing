// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of <c>font-variant-ligatures</c> (CSS Fonts Level 4): <c>normal</c> (no flag), <c>none</c>, or a set of
/// keywords with at most one of each pair.
/// </summary>
[Flags]
internal enum SvgFontVariantLigatures : ushort
{
    /// <summary>
    /// <c>normal</c>, the initial value: the default ligatures of the font and the shaper.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// <c>none</c>: every ligature and contextual alternate off. It stands alone.
    /// </summary>
    None = 1 << 0,

    /// <summary>
    /// <c>common-ligatures</c> (<c>liga</c>, <c>clig</c> on).
    /// </summary>
    CommonLigatures = 1 << 1,

    /// <summary>
    /// <c>no-common-ligatures</c> (<c>liga</c>, <c>clig</c> off).
    /// </summary>
    NoCommonLigatures = 1 << 2,

    /// <summary>
    /// <c>discretionary-ligatures</c> (<c>dlig</c> on).
    /// </summary>
    DiscretionaryLigatures = 1 << 3,

    /// <summary>
    /// <c>no-discretionary-ligatures</c> (<c>dlig</c> off).
    /// </summary>
    NoDiscretionaryLigatures = 1 << 4,

    /// <summary>
    /// <c>historical-ligatures</c> (<c>hlig</c> on).
    /// </summary>
    HistoricalLigatures = 1 << 5,

    /// <summary>
    /// <c>no-historical-ligatures</c> (<c>hlig</c> off).
    /// </summary>
    NoHistoricalLigatures = 1 << 6,

    /// <summary>
    /// <c>contextual</c> (<c>calt</c> on).
    /// </summary>
    Contextual = 1 << 7,

    /// <summary>
    /// <c>no-contextual</c> (<c>calt</c> off).
    /// </summary>
    NoContextual = 1 << 8
}
