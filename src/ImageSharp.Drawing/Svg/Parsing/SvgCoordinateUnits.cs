// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified value of an attribute that selects a coordinate system: <c>gradientUnits</c>, <c>clipPathUnits</c>,
/// <c>maskUnits</c>, <c>maskContentUnits</c>, <c>patternUnits</c>, and <c>patternContentUnits</c>.
/// </summary>
/// <remarks>
/// The default differs per attribute, so the parser keeps <see cref="Unspecified"/> and the consumer applies the default:
/// <c>objectBoundingBox</c> for <c>gradientUnits</c>, <c>maskUnits</c>, and <c>patternUnits</c>; <c>userSpaceOnUse</c>
/// for <c>clipPathUnits</c>, <c>maskContentUnits</c>, and <c>patternContentUnits</c>. For gradients and patterns an
/// unspecified value first comes from the <c>href</c> chain.
/// </remarks>
internal enum SvgCoordinateUnits : byte
{
    /// <summary>
    /// Not specified, or not a valid keyword.
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
