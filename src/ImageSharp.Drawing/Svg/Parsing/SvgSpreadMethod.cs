// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified <c>spreadMethod</c> value.
/// </summary>
internal enum SvgSpreadMethod : byte
{
    /// <summary>
    /// Not specified. The value comes from the <c>href</c> chain, else it is <see cref="Pad"/>.
    /// </summary>
    Unspecified,

    /// <summary>
    /// <c>pad</c>.
    /// </summary>
    Pad,

    /// <summary>
    /// <c>reflect</c>.
    /// </summary>
    Reflect,

    /// <summary>
    /// <c>repeat</c>.
    /// </summary>
    Repeat
}
