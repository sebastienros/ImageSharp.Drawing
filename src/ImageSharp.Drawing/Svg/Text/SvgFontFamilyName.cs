// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One entry of a <c>font-family</c> list: a family name, or a CSS generic family.
/// </summary>
internal readonly struct SvgFontFamilyName
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFontFamilyName"/> struct for a family name.
    /// </summary>
    /// <param name="name">The family name, as written, with quotes removed and identifier runs joined by one space.</param>
    public SvgFontFamilyName(string name)
    {
        this.Name = name;
        this.Generic = SvgGenericFontFamily.None;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFontFamilyName"/> struct for a generic family.
    /// </summary>
    /// <param name="generic">The generic family.</param>
    public SvgFontFamilyName(SvgGenericFontFamily generic)
    {
        this.Name = null;
        this.Generic = generic;
    }

    /// <summary>
    /// Gets the family name, or <see langword="null"/> for a generic family.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the generic family, or <see cref="SvgGenericFontFamily.None"/> when the entry names a family.
    /// </summary>
    public SvgGenericFontFamily Generic { get; }
}
