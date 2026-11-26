// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The color spaces of CSS Color Level 4 that the color functions name.
/// </summary>
internal enum SvgColorSpace : byte
{
    /// <summary>
    /// sRGB, gamma encoded (<c>srgb</c>, and <c>rgb()</c>, named and hexadecimal colors). Components 0 to 1.
    /// </summary>
    Srgb,

    /// <summary>
    /// Linear-light sRGB (<c>srgb-linear</c>).
    /// </summary>
    SrgbLinear,

    /// <summary>
    /// Display P3 (<c>display-p3</c>), with the sRGB transfer function.
    /// </summary>
    DisplayP3,

    /// <summary>
    /// Linear-light Display P3 (<c>display-p3-linear</c>).
    /// </summary>
    DisplayP3Linear,

    /// <summary>
    /// A98 RGB (<c>a98-rgb</c>).
    /// </summary>
    A98Rgb,

    /// <summary>
    /// ProPhoto RGB (<c>prophoto-rgb</c>), relative to D50.
    /// </summary>
    ProphotoRgb,

    /// <summary>
    /// ITU-R BT.2020-2 (<c>rec2020</c>).
    /// </summary>
    Rec2020,

    /// <summary>
    /// CIE XYZ relative to D50 (<c>xyz-d50</c>).
    /// </summary>
    XyzD50,

    /// <summary>
    /// CIE XYZ relative to D65 (<c>xyz-d65</c> and <c>xyz</c>).
    /// </summary>
    XyzD65,

    /// <summary>
    /// CIE Lab relative to D50 (<c>lab()</c>): L 0 to 100, a, b.
    /// </summary>
    Lab,

    /// <summary>
    /// CIE LCH (<c>lch()</c>): L, C, hue in degrees.
    /// </summary>
    Lch,

    /// <summary>
    /// Oklab (<c>oklab()</c>): L 0 to 1, a, b.
    /// </summary>
    Oklab,

    /// <summary>
    /// OkLCh (<c>oklch()</c>): L, C, hue in degrees.
    /// </summary>
    Oklch,

    /// <summary>
    /// HSL (<c>hsl()</c>): hue in degrees, saturation and lightness 0 to 100.
    /// </summary>
    Hsl,

    /// <summary>
    /// HWB (<c>hwb()</c>): hue in degrees, whiteness and blackness 0 to 100.
    /// </summary>
    Hwb
}
