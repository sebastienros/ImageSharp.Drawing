// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of <c>font-variant-numeric</c> (CSS Fonts Level 4): <c>normal</c> (no flag) or a set of keywords with at
/// most one of each group (figures, spacing, fractions).
/// </summary>
[Flags]
internal enum SvgFontVariantNumeric : byte
{
    /// <summary>
    /// <c>normal</c>, the initial value.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// <c>lining-nums</c> (<c>lnum</c>).
    /// </summary>
    LiningNums = 1 << 0,

    /// <summary>
    /// <c>oldstyle-nums</c> (<c>onum</c>).
    /// </summary>
    OldstyleNums = 1 << 1,

    /// <summary>
    /// <c>proportional-nums</c> (<c>pnum</c>).
    /// </summary>
    ProportionalNums = 1 << 2,

    /// <summary>
    /// <c>tabular-nums</c> (<c>tnum</c>).
    /// </summary>
    TabularNums = 1 << 3,

    /// <summary>
    /// <c>diagonal-fractions</c> (<c>frac</c>).
    /// </summary>
    DiagonalFractions = 1 << 4,

    /// <summary>
    /// <c>stacked-fractions</c> (<c>afrc</c>).
    /// </summary>
    StackedFractions = 1 << 5,

    /// <summary>
    /// <c>ordinal</c> (<c>ordn</c>).
    /// </summary>
    Ordinal = 1 << 6,

    /// <summary>
    /// <c>slashed-zero</c> (<c>zero</c>).
    /// </summary>
    SlashedZero = 1 << 7
}
