// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of <c>white-space-collapse</c> (CSS Text Level 4): the values that Chromium accepts, in keyword order.
/// The <c>white-space</c> shorthand sets it too: <c>normal</c> and <c>nowrap</c> give <see cref="Collapse"/>,
/// <c>pre</c> and <c>pre-wrap</c> give <see cref="Preserve"/>, <c>pre-line</c> gives <see cref="PreserveBreaks"/>, and
/// <c>break-spaces</c> gives <see cref="BreakSpaces"/>.
/// </summary>
/// <remarks>
/// Chromium renders <see cref="Preserve"/> and <see cref="BreakSpaces"/> alike in SVG text, and
/// <see cref="PreserveBreaks"/> like <see cref="Collapse"/>, because SVG text does not break lines. It rejects
/// <c>preserve-spaces</c> and <c>discard</c>.
/// </remarks>
internal enum SvgWhiteSpaceCollapse : byte
{
    /// <summary>
    /// <c>collapse</c>, the initial value.
    /// </summary>
    Collapse,

    /// <summary>
    /// <c>preserve</c>.
    /// </summary>
    Preserve,

    /// <summary>
    /// <c>preserve-breaks</c>.
    /// </summary>
    PreserveBreaks,

    /// <summary>
    /// <c>break-spaces</c>.
    /// </summary>
    BreakSpaces
}
