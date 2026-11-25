// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The non-presentation attributes that the reader parses. Presentation attributes map to <see cref="SvgPropertyId"/>.
/// </summary>
internal enum SvgAttributeId : byte
{
    /// <summary>
    /// <c>id</c>.
    /// </summary>
    Id,

    /// <summary>
    /// <c>class</c>.
    /// </summary>
    Class,

    /// <summary>
    /// <c>style</c>.
    /// </summary>
    Style,

    /// <summary>
    /// <c>transform</c>.
    /// </summary>
    Transform,

    /// <summary>
    /// <c>x</c>.
    /// </summary>
    X,

    /// <summary>
    /// <c>y</c>.
    /// </summary>
    Y,

    /// <summary>
    /// <c>width</c>.
    /// </summary>
    Width,

    /// <summary>
    /// <c>height</c>.
    /// </summary>
    Height,

    /// <summary>
    /// <c>rx</c>.
    /// </summary>
    Rx,

    /// <summary>
    /// <c>ry</c>.
    /// </summary>
    Ry,

    /// <summary>
    /// <c>cx</c>.
    /// </summary>
    Cx,

    /// <summary>
    /// <c>cy</c>.
    /// </summary>
    Cy,

    /// <summary>
    /// <c>r</c>.
    /// </summary>
    R,

    /// <summary>
    /// <c>fx</c>.
    /// </summary>
    Fx,

    /// <summary>
    /// <c>fy</c>.
    /// </summary>
    Fy,

    /// <summary>
    /// <c>fr</c>.
    /// </summary>
    Fr,

    /// <summary>
    /// <c>x1</c>.
    /// </summary>
    X1,

    /// <summary>
    /// <c>y1</c>.
    /// </summary>
    Y1,

    /// <summary>
    /// <c>x2</c>.
    /// </summary>
    X2,

    /// <summary>
    /// <c>y2</c>.
    /// </summary>
    Y2,

    /// <summary>
    /// <c>points</c>.
    /// </summary>
    Points,

    /// <summary>
    /// <c>d</c>.
    /// </summary>
    D,

    /// <summary>
    /// <c>viewBox</c>.
    /// </summary>
    ViewBox,

    /// <summary>
    /// <c>preserveAspectRatio</c>.
    /// </summary>
    PreserveAspectRatio,

    /// <summary>
    /// <c>href</c>, in no namespace or in the XLink namespace.
    /// </summary>
    Href,

    /// <summary>
    /// <c>offset</c>.
    /// </summary>
    Offset,

    /// <summary>
    /// <c>gradientUnits</c>.
    /// </summary>
    GradientUnits,

    /// <summary>
    /// <c>gradientTransform</c>.
    /// </summary>
    GradientTransform,

    /// <summary>
    /// <c>spreadMethod</c>.
    /// </summary>
    SpreadMethod,

    /// <summary>
    /// <c>type</c>.
    /// </summary>
    Type
}
