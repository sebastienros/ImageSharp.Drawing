// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>shape-rendering</c> property.
/// </summary>
internal enum SvgShapeRendering : byte
{
    /// <summary>
    /// <c>auto</c>, the initial value.
    /// </summary>
    Auto,

    /// <summary>
    /// <c>optimizeSpeed</c>: browsers draw without antialiasing.
    /// </summary>
    OptimizeSpeed,

    /// <summary>
    /// <c>crispEdges</c>: browsers draw without antialiasing.
    /// </summary>
    CrispEdges,

    /// <summary>
    /// <c>geometricPrecision</c>: antialiased, as <see cref="Auto"/>.
    /// </summary>
    GeometricPrecision
}
