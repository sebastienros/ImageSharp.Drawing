// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of an <see cref="SvgMarkerOrient"/> value.
/// </summary>
internal enum SvgMarkerOrientKind : byte
{
    /// <summary>
    /// <c>orient</c> is not specified or not valid. The marker uses the angle 0, the initial value.
    /// </summary>
    Unspecified,

    /// <summary>
    /// A fixed angle, in <see cref="SvgMarkerOrient.Angle"/>.
    /// </summary>
    Angle,

    /// <summary>
    /// <c>auto</c>: the marker follows the direction of the path at its vertex.
    /// </summary>
    Auto,

    /// <summary>
    /// <c>auto-start-reverse</c>: as <see cref="Auto"/>, but the marker at the start of the path points backward.
    /// </summary>
    AutoStartReverse
}
