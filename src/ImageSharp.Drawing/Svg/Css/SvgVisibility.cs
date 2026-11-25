// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified value of the <c>visibility</c> property.
/// </summary>
internal enum SvgVisibility : byte
{
    /// <summary>
    /// <c>visible</c>, the initial value.
    /// </summary>
    Visible,

    /// <summary>
    /// <c>hidden</c>: the element does not render, but its children can.
    /// </summary>
    Hidden,

    /// <summary>
    /// <c>collapse</c>, which SVG treats as <c>hidden</c>.
    /// </summary>
    Collapse
}
