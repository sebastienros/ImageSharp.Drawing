// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>text-decoration-line</c> property: a set of lines.
/// </summary>
[Flags]
internal enum SvgTextDecorationLine : byte
{
    /// <summary>
    /// <c>none</c>, the initial value.
    /// </summary>
    None = 0,

    /// <summary>
    /// <c>underline</c>.
    /// </summary>
    Underline = 1,

    /// <summary>
    /// <c>overline</c>.
    /// </summary>
    Overline = 2,

    /// <summary>
    /// <c>line-through</c>.
    /// </summary>
    LineThrough = 4,

    /// <summary>
    /// <c>blink</c>, which parses and draws nothing, as in browsers.
    /// </summary>
    Blink = 8
}
