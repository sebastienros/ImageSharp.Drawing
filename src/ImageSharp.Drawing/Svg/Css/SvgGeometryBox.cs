// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The reference box of a <c>clip-path</c> basic shape (CSS Masking Level 1, <c>&lt;geometry-box&gt;</c>), as written.
/// </summary>
/// <remarks>
/// CSS Masking maps the CSS layout boxes for SVG elements, which have none: <c>content-box</c> and <c>padding-box</c>
/// use <c>fill-box</c>, and <c>border-box</c> and <c>margin-box</c> use <c>stroke-box</c>. The keyword is kept as written,
/// so the code that builds the clip applies the mapping.
/// </remarks>
internal enum SvgGeometryBox : byte
{
    /// <summary>
    /// The value names no box. CSS Masking makes the default <c>border-box</c>.
    /// </summary>
    Unspecified,

    /// <summary>
    /// <c>fill-box</c>: the object bounding box.
    /// </summary>
    FillBox,

    /// <summary>
    /// <c>stroke-box</c>: the stroke bounding box.
    /// </summary>
    StrokeBox,

    /// <summary>
    /// <c>view-box</c>: the nearest SVG viewport.
    /// </summary>
    ViewBox,

    /// <summary>
    /// <c>content-box</c>.
    /// </summary>
    ContentBox,

    /// <summary>
    /// <c>padding-box</c>.
    /// </summary>
    PaddingBox,

    /// <summary>
    /// <c>border-box</c>.
    /// </summary>
    BorderBox,

    /// <summary>
    /// <c>margin-box</c>.
    /// </summary>
    MarginBox
}
