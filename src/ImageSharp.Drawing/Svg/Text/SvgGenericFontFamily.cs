// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A CSS generic font family name in a <c>font-family</c> list.
/// </summary>
internal enum SvgGenericFontFamily : byte
{
    /// <summary>
    /// Not a generic name: the entry names a family.
    /// </summary>
    None,

    /// <summary>
    /// <c>serif</c>.
    /// </summary>
    Serif,

    /// <summary>
    /// <c>sans-serif</c>.
    /// </summary>
    SansSerif,

    /// <summary>
    /// <c>monospace</c>.
    /// </summary>
    Monospace,

    /// <summary>
    /// <c>cursive</c>.
    /// </summary>
    Cursive,

    /// <summary>
    /// <c>fantasy</c>.
    /// </summary>
    Fantasy
}
