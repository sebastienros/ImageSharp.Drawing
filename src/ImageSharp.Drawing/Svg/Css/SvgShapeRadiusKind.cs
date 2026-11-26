// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The form of one radius of <c>circle()</c> or <c>ellipse()</c> (<c>&lt;shape-radius&gt;</c>).
/// </summary>
internal enum SvgShapeRadiusKind : byte
{
    /// <summary>
    /// <c>closest-side</c>, the default when the radius is omitted.
    /// </summary>
    ClosestSide,

    /// <summary>
    /// <c>farthest-side</c>.
    /// </summary>
    FarthestSide,

    /// <summary>
    /// A non-negative length or percentage, in <see cref="SvgClipShape.Lengths"/>.
    /// </summary>
    Length
}
