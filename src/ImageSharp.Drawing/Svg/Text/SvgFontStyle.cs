// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>font-style</c> property.
/// </summary>
internal enum SvgFontStyle : byte
{
    /// <summary>
    /// <c>normal</c>, the initial value.
    /// </summary>
    Normal,

    /// <summary>
    /// <c>italic</c>.
    /// </summary>
    Italic,

    /// <summary>
    /// <c>oblique</c>, with or without an angle. The angle is not kept: font matching treats it as italic, as CSS font matching does when no oblique face exists.
    /// </summary>
    Oblique
}
