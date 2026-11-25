// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>image-rendering</c> property.
/// </summary>
internal enum SvgImageRendering : byte
{
    /// <summary>
    /// <c>auto</c>, the initial value.
    /// </summary>
    Auto,

    /// <summary>
    /// <c>optimizeQuality</c>, an SVG 1.1 value that CSS maps to smooth sampling.
    /// </summary>
    OptimizeQuality,

    /// <summary>
    /// <c>optimizeSpeed</c>, an SVG 1.1 value that browsers map to nearest-neighbor sampling.
    /// </summary>
    OptimizeSpeed,

    /// <summary>
    /// <c>pixelated</c>: nearest-neighbor sampling.
    /// </summary>
    Pixelated,

    /// <summary>
    /// <c>crisp-edges</c>: a sampling that keeps edges sharp; nearest-neighbor in browsers.
    /// </summary>
    CrispEdges,

    /// <summary>
    /// <c>smooth</c>: smooth sampling.
    /// </summary>
    Smooth,

    /// <summary>
    /// <c>high-quality</c>: smooth sampling.
    /// </summary>
    HighQuality
}
