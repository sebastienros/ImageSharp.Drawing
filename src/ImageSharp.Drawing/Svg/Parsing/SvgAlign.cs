// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The alignment part of <c>preserveAspectRatio</c>.
/// </summary>
internal enum SvgAlign : byte
{
    /// <summary>
    /// <c>xMidYMid</c>, the initial value. It is first so that <c>default</c> is the initial value.
    /// </summary>
    XMidYMid,

    /// <summary>
    /// <c>none</c>: scale each axis independently to fill the viewport.
    /// </summary>
    None,

    /// <summary>
    /// <c>xMinYMin</c>.
    /// </summary>
    XMinYMin,

    /// <summary>
    /// <c>xMidYMin</c>.
    /// </summary>
    XMidYMin,

    /// <summary>
    /// <c>xMaxYMin</c>.
    /// </summary>
    XMaxYMin,

    /// <summary>
    /// <c>xMinYMid</c>.
    /// </summary>
    XMinYMid,

    /// <summary>
    /// <c>xMaxYMid</c>.
    /// </summary>
    XMaxYMid,

    /// <summary>
    /// <c>xMinYMax</c>.
    /// </summary>
    XMinYMax,

    /// <summary>
    /// <c>xMidYMax</c>.
    /// </summary>
    XMidYMax,

    /// <summary>
    /// <c>xMaxYMax</c>.
    /// </summary>
    XMaxYMax
}
