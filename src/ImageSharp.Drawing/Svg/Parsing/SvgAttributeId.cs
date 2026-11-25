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
    Type,

    /// <summary>
    /// <c>clipPathUnits</c>.
    /// </summary>
    ClipPathUnits,

    /// <summary>
    /// <c>maskUnits</c>.
    /// </summary>
    MaskUnits,

    /// <summary>
    /// <c>maskContentUnits</c>.
    /// </summary>
    MaskContentUnits,

    /// <summary>
    /// <c>patternUnits</c>.
    /// </summary>
    PatternUnits,

    /// <summary>
    /// <c>patternContentUnits</c>.
    /// </summary>
    PatternContentUnits,

    /// <summary>
    /// <c>patternTransform</c>.
    /// </summary>
    PatternTransform,

    /// <summary>
    /// <c>refX</c>.
    /// </summary>
    RefX,

    /// <summary>
    /// <c>refY</c>.
    /// </summary>
    RefY,

    /// <summary>
    /// <c>markerWidth</c>.
    /// </summary>
    MarkerWidth,

    /// <summary>
    /// <c>markerHeight</c>.
    /// </summary>
    MarkerHeight,

    /// <summary>
    /// <c>markerUnits</c>.
    /// </summary>
    MarkerUnits,

    /// <summary>
    /// <c>orient</c>.
    /// </summary>
    Orient,

    /// <summary>
    /// <c>systemLanguage</c>, on any element.
    /// </summary>
    SystemLanguage,

    /// <summary>
    /// <c>requiredExtensions</c>, on any element.
    /// </summary>
    RequiredExtensions,

    /// <summary>
    /// <c>requiredFeatures</c>, on any element.
    /// </summary>
    RequiredFeatures
}
