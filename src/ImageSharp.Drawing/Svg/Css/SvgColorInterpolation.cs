// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>color-interpolation</c> property.
/// </summary>
internal enum SvgColorInterpolation : byte
{
    /// <summary>
    /// <c>sRGB</c>, the initial value.
    /// </summary>
    SRgb,

    /// <summary>
    /// <c>auto</c>: the user agent chooses; browsers use sRGB.
    /// </summary>
    Auto,

    /// <summary>
    /// <c>linearRGB</c>: colors are interpolated in linear light.
    /// </summary>
    LinearRgb
}
