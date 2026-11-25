// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Defines how a gradient brush interpolates the alpha component of its colors between two color stops.
/// </summary>
/// <remarks>
/// The modes give different colors only between stops whose alpha values differ.
/// </remarks>
public enum GradientAlphaInterpolation
{
    /// <summary>
    /// Multiplies the color components by alpha before interpolation, as CSS Color 4 requires. The color of a transparent
    /// stop does not show: a gradient from opaque red to transparent blue fades red to transparent. This is the default.
    /// </summary>
    Premultiplied,

    /// <summary>
    /// Interpolates the color components and alpha separately, as SVG 1.1 defines for gradient stops and as browsers
    /// render SVG gradients. The color of a transparent stop shows: a gradient from opaque red to transparent blue passes
    /// through a half-transparent purple.
    /// </summary>
    Unpremultiplied
}
