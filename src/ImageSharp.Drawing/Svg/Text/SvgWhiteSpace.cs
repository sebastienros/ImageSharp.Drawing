// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>white-space</c> property. On <c>text</c>, <c>tspan</c>, <c>textPath</c>, and <c>tref</c>, the <c>xml:space</c> attribute sets it at the precedence of a presentation attribute, as Chromium does: <c>preserve</c> gives <see cref="Pre"/>, and any other value gives <see cref="Nowrap"/>.
/// </summary>
internal enum SvgWhiteSpace : byte
{
    /// <summary>
    /// <c>normal</c>, the initial value: white space collapses.
    /// </summary>
    Normal,

    /// <summary>
    /// <c>pre</c>: white space is preserved.
    /// </summary>
    Pre,

    /// <summary>
    /// <c>nowrap</c>: white space collapses.
    /// </summary>
    Nowrap,

    /// <summary>
    /// <c>pre-wrap</c>: white space is preserved.
    /// </summary>
    PreWrap,

    /// <summary>
    /// <c>break-spaces</c>: white space is preserved.
    /// </summary>
    BreakSpaces,

    /// <summary>
    /// <c>pre-line</c>: spaces and tabs collapse. SVG text is one line, so segment breaks become spaces as well.
    /// </summary>
    PreLine
}
