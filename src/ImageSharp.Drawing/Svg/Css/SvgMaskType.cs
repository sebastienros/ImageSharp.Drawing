// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>mask-type</c> property.
/// </summary>
internal enum SvgMaskType : byte
{
    /// <summary>
    /// <c>luminance</c>, the initial value: the mask uses the luminance of its content times the alpha.
    /// </summary>
    Luminance,

    /// <summary>
    /// <c>alpha</c>: the mask uses the alpha of its content only.
    /// </summary>
    Alpha
}
