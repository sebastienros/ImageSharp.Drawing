// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>transform-box</c> values of CSS Transforms Level 1, in keyword order.
/// </summary>
/// <remarks>
/// For SVG elements, which have no CSS layout box, CSS Transforms maps <c>content-box</c> to <c>fill-box</c> and
/// <c>border-box</c> to <c>stroke-box</c>. The keyword is kept as written.
/// </remarks>
internal enum SvgTransformBox : byte
{
    /// <summary>
    /// <c>content-box</c>.
    /// </summary>
    ContentBox,

    /// <summary>
    /// <c>border-box</c>.
    /// </summary>
    BorderBox,

    /// <summary>
    /// <c>fill-box</c>: the object bounding box.
    /// </summary>
    FillBox,

    /// <summary>
    /// <c>stroke-box</c>: the stroke bounding box.
    /// </summary>
    StrokeBox,

    /// <summary>
    /// <c>view-box</c>, the initial value: the nearest SVG viewport.
    /// </summary>
    ViewBox
}
