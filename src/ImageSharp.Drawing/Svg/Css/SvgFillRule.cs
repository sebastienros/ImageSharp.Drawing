// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified value of the <c>fill-rule</c> property.
/// </summary>
internal enum SvgFillRule : byte
{
    /// <summary>
    /// <c>nonzero</c>, the initial value.
    /// </summary>
    NonZero,

    /// <summary>
    /// <c>evenodd</c>.
    /// </summary>
    EvenOdd
}
