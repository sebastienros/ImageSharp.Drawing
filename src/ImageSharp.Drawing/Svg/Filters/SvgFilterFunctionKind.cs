// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of one entry of a <c>filter</c> value: a <c>url()</c> reference or one of the ten filter functions of Filter
/// Effects Level 1.
/// </summary>
internal enum SvgFilterFunctionKind : byte
{
    /// <summary>
    /// <c>url()</c>: a reference to a <c>filter</c> element.
    /// </summary>
    Url,

    /// <summary>
    /// <c>blur()</c>.
    /// </summary>
    Blur,

    /// <summary>
    /// <c>brightness()</c>.
    /// </summary>
    Brightness,

    /// <summary>
    /// <c>contrast()</c>.
    /// </summary>
    Contrast,

    /// <summary>
    /// <c>drop-shadow()</c>.
    /// </summary>
    DropShadow,

    /// <summary>
    /// <c>grayscale()</c>.
    /// </summary>
    Grayscale,

    /// <summary>
    /// <c>hue-rotate()</c>.
    /// </summary>
    HueRotate,

    /// <summary>
    /// <c>invert()</c>.
    /// </summary>
    Invert,

    /// <summary>
    /// <c>opacity()</c>.
    /// </summary>
    Opacity,

    /// <summary>
    /// <c>saturate()</c>.
    /// </summary>
    Saturate,

    /// <summary>
    /// <c>sepia()</c>.
    /// </summary>
    Sepia
}
