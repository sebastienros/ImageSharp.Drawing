// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>text-rendering</c> property.
/// </summary>
internal enum SvgTextRendering : byte
{
    /// <summary>
    /// <c>auto</c>, the initial value.
    /// </summary>
    Auto,

    /// <summary>
    /// <c>optimizeSpeed</c>.
    /// </summary>
    OptimizeSpeed,

    /// <summary>
    /// <c>optimizeLegibility</c>.
    /// </summary>
    OptimizeLegibility,

    /// <summary>
    /// <c>geometricPrecision</c>.
    /// </summary>
    GeometricPrecision
}
