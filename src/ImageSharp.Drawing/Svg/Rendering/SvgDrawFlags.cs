// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The paint options of a <see cref="SvgRenderNodeKind.Draw"/> node that differ from the defaults. The default draw fills,
/// then strokes in user space, with antialiasing.
/// </summary>
[Flags]
internal enum SvgDrawFlags : byte
{
    /// <summary>
    /// The defaults.
    /// </summary>
    None = 0,

    /// <summary>
    /// The stroke paints before the fill (<c>paint-order</c>).
    /// </summary>
    StrokeFirst = 1,

    /// <summary>
    /// The stroke width and dashes are measured in the destination space of the render, not in user space
    /// (<c>vector-effect: non-scaling-stroke</c>).
    /// </summary>
    NonScalingStroke = 2,

    /// <summary>
    /// The path draws without antialiasing (<c>shape-rendering: crispEdges</c> or <c>optimizeSpeed</c>).
    /// </summary>
    Aliased = 4
}
