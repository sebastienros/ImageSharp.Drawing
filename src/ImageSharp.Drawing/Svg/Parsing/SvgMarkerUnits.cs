// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified <c>markerUnits</c> value.
/// </summary>
internal enum SvgMarkerUnits : byte
{
    /// <summary>
    /// Not specified, or not a valid keyword. The default is <see cref="StrokeWidth"/>.
    /// </summary>
    Unspecified,

    /// <summary>
    /// <c>strokeWidth</c>: the marker content is scaled by the stroke width of the shape.
    /// </summary>
    StrokeWidth,

    /// <summary>
    /// <c>userSpaceOnUse</c>: the marker content uses the user space of the shape without scaling.
    /// </summary>
    UserSpaceOnUse
}
