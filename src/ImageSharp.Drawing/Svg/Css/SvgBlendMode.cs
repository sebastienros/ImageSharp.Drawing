// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>mix-blend-mode</c> property: the 16 separable and non-separable blend modes of Compositing and Blending Level 1.
/// </summary>
internal enum SvgBlendMode : byte
{
    /// <summary>
    /// <c>normal</c>, the initial value.
    /// </summary>
    Normal,

    /// <summary>
    /// <c>multiply</c>.
    /// </summary>
    Multiply,

    /// <summary>
    /// <c>screen</c>.
    /// </summary>
    Screen,

    /// <summary>
    /// <c>overlay</c>.
    /// </summary>
    Overlay,

    /// <summary>
    /// <c>darken</c>.
    /// </summary>
    Darken,

    /// <summary>
    /// <c>lighten</c>.
    /// </summary>
    Lighten,

    /// <summary>
    /// <c>color-dodge</c>.
    /// </summary>
    ColorDodge,

    /// <summary>
    /// <c>color-burn</c>.
    /// </summary>
    ColorBurn,

    /// <summary>
    /// <c>hard-light</c>.
    /// </summary>
    HardLight,

    /// <summary>
    /// <c>soft-light</c>.
    /// </summary>
    SoftLight,

    /// <summary>
    /// <c>difference</c>.
    /// </summary>
    Difference,

    /// <summary>
    /// <c>exclusion</c>.
    /// </summary>
    Exclusion,

    /// <summary>
    /// <c>hue</c>.
    /// </summary>
    Hue,

    /// <summary>
    /// <c>saturation</c>.
    /// </summary>
    Saturation,

    /// <summary>
    /// <c>color</c>.
    /// </summary>
    Color,

    /// <summary>
    /// <c>luminosity</c>.
    /// </summary>
    Luminosity
}
