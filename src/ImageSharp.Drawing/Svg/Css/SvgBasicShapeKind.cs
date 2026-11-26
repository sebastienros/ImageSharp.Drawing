// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of a CSS basic shape in a <c>clip-path</c> value (CSS Shapes Level 1 and 2). Each member says how
/// <see cref="SvgClipShape.Lengths"/> is laid out.
/// </summary>
internal enum SvgBasicShapeKind : byte
{
    /// <summary>
    /// No shape: the value is a geometry box alone, such as <c>clip-path: fill-box</c>, which clips to the box.
    /// <see cref="SvgClipShape.Lengths"/> is empty.
    /// </summary>
    None,

    /// <summary>
    /// <c>inset()</c>: <see cref="SvgClipShape.Lengths"/> holds the offsets from the top, right, bottom, and left edges of
    /// the reference box, after the CSS rule that fills omitted sides; <see cref="SvgClipShape.Radii"/> holds the corner
    /// radii of <c>round</c>.
    /// </summary>
    Inset,

    /// <summary>
    /// <c>xywh()</c> (CSS Shapes Level 2): <see cref="SvgClipShape.Lengths"/> holds x, y, width, and height, measured from
    /// the top left corner of the reference box; <see cref="SvgClipShape.Radii"/> holds the corner radii of <c>round</c>.
    /// </summary>
    Xywh,

    /// <summary>
    /// <c>rect()</c> (CSS Shapes Level 2): <see cref="SvgClipShape.Lengths"/> holds the positions of the top, right,
    /// bottom, and left edges, each measured from the top or the left edge of the reference box. An unspecified length
    /// is <c>auto</c>: 0% for top and left, 100% for right and bottom. <see cref="SvgClipShape.Radii"/> holds the corner
    /// radii of <c>round</c>.
    /// </summary>
    Rect,

    /// <summary>
    /// <c>circle()</c>: <see cref="SvgClipShape.Lengths"/> holds the radius when <see cref="SvgClipShape.RadiusXKind"/> is
    /// <see cref="SvgShapeRadiusKind.Length"/>; the center is <see cref="SvgClipShape.Position"/>.
    /// </summary>
    Circle,

    /// <summary>
    /// <c>ellipse()</c>: <see cref="SvgClipShape.Lengths"/> holds the horizontal and the vertical radius, each meaningful
    /// when its kind (<see cref="SvgClipShape.RadiusXKind"/>, <see cref="SvgClipShape.RadiusYKind"/>) is
    /// <see cref="SvgShapeRadiusKind.Length"/>; the center is <see cref="SvgClipShape.Position"/>.
    /// </summary>
    Ellipse,

    /// <summary>
    /// <c>polygon()</c>: <see cref="SvgClipShape.Lengths"/> holds the vertices as x, y pairs, measured from the top left
    /// corner of the reference box; <see cref="SvgClipShape.FillRule"/> is the fill rule.
    /// </summary>
    Polygon,

    /// <summary>
    /// <c>path()</c>: <see cref="SvgClipShape.Path"/> is the path in the coordinates of the reference box, and
    /// <see cref="SvgClipShape.FillRule"/> is the fill rule. <see cref="SvgClipShape.Lengths"/> is empty.
    /// </summary>
    Path
}
