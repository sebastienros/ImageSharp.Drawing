// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified <c>gradientUnits</c> value.
/// </summary>
internal enum SvgGradientUnits : byte
{
    /// <summary>
    /// Not specified. The value comes from the <c>href</c> chain, else it is <see cref="ObjectBoundingBox"/>.
    /// </summary>
    Unspecified,

    /// <summary>
    /// <c>objectBoundingBox</c>.
    /// </summary>
    ObjectBoundingBox,

    /// <summary>
    /// <c>userSpaceOnUse</c>.
    /// </summary>
    UserSpaceOnUse
}
