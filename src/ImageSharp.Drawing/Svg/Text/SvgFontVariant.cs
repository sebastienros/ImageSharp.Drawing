// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>font-variant</c> property, as far as it selects capital letter forms. Values of the CSS shorthand without a capital letter keyword parse as <see cref="Normal"/>.
/// </summary>
internal enum SvgFontVariant : byte
{
    /// <summary>
    /// <c>normal</c> or <c>none</c>, the initial value.
    /// </summary>
    Normal,

    /// <summary>
    /// <c>small-caps</c>.
    /// </summary>
    SmallCaps,

    /// <summary>
    /// <c>all-small-caps</c>.
    /// </summary>
    AllSmallCaps,

    /// <summary>
    /// <c>petite-caps</c>.
    /// </summary>
    PetiteCaps,

    /// <summary>
    /// <c>all-petite-caps</c>.
    /// </summary>
    AllPetiteCaps,

    /// <summary>
    /// <c>unicase</c>.
    /// </summary>
    Unicase,

    /// <summary>
    /// <c>titling-caps</c>.
    /// </summary>
    TitlingCaps
}
