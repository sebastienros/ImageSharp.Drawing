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
    RequiredFeatures,

    /// <summary>
    /// <c>dx</c>, a list of lengths, on <c>text</c> and <c>tspan</c>.
    /// </summary>
    Dx,

    /// <summary>
    /// <c>dy</c>, a list of lengths, on <c>text</c> and <c>tspan</c>.
    /// </summary>
    Dy,

    /// <summary>
    /// <c>rotate</c>, a list of numbers, on <c>text</c> and <c>tspan</c>.
    /// </summary>
    Rotate,

    /// <summary>
    /// <c>textLength</c>, on <c>text</c>, <c>tspan</c>, and <c>textPath</c>.
    /// </summary>
    TextLength,

    /// <summary>
    /// <c>lengthAdjust</c>, on <c>text</c>, <c>tspan</c>, and <c>textPath</c>.
    /// </summary>
    LengthAdjust,

    /// <summary>
    /// <c>startOffset</c>, on <c>textPath</c>.
    /// </summary>
    StartOffset,

    /// <summary>
    /// <c>method</c>, on <c>textPath</c>.
    /// </summary>
    Method,

    /// <summary>
    /// <c>spacing</c>, on <c>textPath</c>.
    /// </summary>
    Spacing,

    /// <summary>
    /// <c>side</c>, on <c>textPath</c> (SVG 2).
    /// </summary>
    Side,

    /// <summary>
    /// <c>path</c>, path data, on <c>textPath</c> (SVG 2).
    /// </summary>
    PathData,

    /// <summary>
    /// <c>filterUnits</c>, on <c>filter</c>.
    /// </summary>
    FilterUnits,

    /// <summary>
    /// <c>primitiveUnits</c>, on <c>filter</c>.
    /// </summary>
    PrimitiveUnits,

    /// <summary>
    /// <c>filterRes</c>, on <c>filter</c>. Removed by Filter Effects; it is recognized and has no effect.
    /// </summary>
    FilterRes,

    /// <summary>
    /// <c>in</c>, the first input of a filter primitive, also on <c>feMergeNode</c>.
    /// </summary>
    In,

    /// <summary>
    /// <c>in2</c>, the second input of a filter primitive.
    /// </summary>
    In2,

    /// <summary>
    /// <c>result</c>, the name of the result of a filter primitive.
    /// </summary>
    Result,

    /// <summary>
    /// <c>mode</c>, on <c>feBlend</c>.
    /// </summary>
    Mode,

    /// <summary>
    /// <c>values</c>, on <c>feColorMatrix</c>.
    /// </summary>
    Values,

    /// <summary>
    /// <c>operator</c>, on <c>feComposite</c> and <c>feMorphology</c>.
    /// </summary>
    Operator,

    /// <summary>
    /// <c>k1</c>, on <c>feComposite</c>.
    /// </summary>
    K1,

    /// <summary>
    /// <c>k2</c>, on <c>feComposite</c>.
    /// </summary>
    K2,

    /// <summary>
    /// <c>k3</c>, on <c>feComposite</c>.
    /// </summary>
    K3,

    /// <summary>
    /// <c>k4</c>, on <c>feComposite</c>.
    /// </summary>
    K4,

    /// <summary>
    /// <c>stdDeviation</c>, on <c>feGaussianBlur</c> and <c>feDropShadow</c>.
    /// </summary>
    StdDeviation,

    /// <summary>
    /// <c>edgeMode</c>, on <c>feGaussianBlur</c> and <c>feConvolveMatrix</c>.
    /// </summary>
    EdgeMode,

    /// <summary>
    /// <c>tableValues</c>, on the transfer functions.
    /// </summary>
    TableValues,

    /// <summary>
    /// <c>slope</c>, on the transfer functions.
    /// </summary>
    Slope,

    /// <summary>
    /// <c>intercept</c>, on the transfer functions.
    /// </summary>
    Intercept,

    /// <summary>
    /// <c>amplitude</c>, on the transfer functions.
    /// </summary>
    Amplitude,

    /// <summary>
    /// <c>exponent</c>, on the transfer functions.
    /// </summary>
    Exponent,

    /// <summary>
    /// <c>order</c>, on <c>feConvolveMatrix</c>.
    /// </summary>
    Order,

    /// <summary>
    /// <c>kernelMatrix</c>, on <c>feConvolveMatrix</c>.
    /// </summary>
    KernelMatrix,

    /// <summary>
    /// <c>divisor</c>, on <c>feConvolveMatrix</c>.
    /// </summary>
    Divisor,

    /// <summary>
    /// <c>bias</c>, on <c>feConvolveMatrix</c>.
    /// </summary>
    Bias,

    /// <summary>
    /// <c>targetX</c>, on <c>feConvolveMatrix</c>.
    /// </summary>
    TargetX,

    /// <summary>
    /// <c>targetY</c>, on <c>feConvolveMatrix</c>.
    /// </summary>
    TargetY,

    /// <summary>
    /// <c>kernelUnitLength</c>, on <c>feConvolveMatrix</c> and the lighting primitives.
    /// </summary>
    KernelUnitLength,

    /// <summary>
    /// <c>preserveAlpha</c>, on <c>feConvolveMatrix</c>.
    /// </summary>
    PreserveAlpha,

    /// <summary>
    /// <c>surfaceScale</c>, on the lighting primitives.
    /// </summary>
    SurfaceScale,

    /// <summary>
    /// <c>diffuseConstant</c>, on <c>feDiffuseLighting</c>.
    /// </summary>
    DiffuseConstant,

    /// <summary>
    /// <c>specularConstant</c>, on <c>feSpecularLighting</c>.
    /// </summary>
    SpecularConstant,

    /// <summary>
    /// <c>specularExponent</c>, on <c>feSpecularLighting</c> and <c>feSpotLight</c>.
    /// </summary>
    SpecularExponent,

    /// <summary>
    /// <c>limitingConeAngle</c>, on <c>feSpotLight</c>.
    /// </summary>
    LimitingConeAngle,

    /// <summary>
    /// <c>azimuth</c>, on <c>feDistantLight</c>.
    /// </summary>
    Azimuth,

    /// <summary>
    /// <c>elevation</c>, on <c>feDistantLight</c>.
    /// </summary>
    Elevation,

    /// <summary>
    /// <c>z</c>, on <c>fePointLight</c> and <c>feSpotLight</c>.
    /// </summary>
    Z,

    /// <summary>
    /// <c>pointsAtX</c>, on <c>feSpotLight</c>.
    /// </summary>
    PointsAtX,

    /// <summary>
    /// <c>pointsAtY</c>, on <c>feSpotLight</c>.
    /// </summary>
    PointsAtY,

    /// <summary>
    /// <c>pointsAtZ</c>, on <c>feSpotLight</c>.
    /// </summary>
    PointsAtZ,

    /// <summary>
    /// <c>scale</c>, on <c>feDisplacementMap</c>.
    /// </summary>
    Scale,

    /// <summary>
    /// <c>xChannelSelector</c>, on <c>feDisplacementMap</c>.
    /// </summary>
    XChannelSelector,

    /// <summary>
    /// <c>yChannelSelector</c>, on <c>feDisplacementMap</c>.
    /// </summary>
    YChannelSelector,

    /// <summary>
    /// <c>baseFrequency</c>, on <c>feTurbulence</c>.
    /// </summary>
    BaseFrequency,

    /// <summary>
    /// <c>numOctaves</c>, on <c>feTurbulence</c>.
    /// </summary>
    NumOctaves,

    /// <summary>
    /// <c>seed</c>, on <c>feTurbulence</c>.
    /// </summary>
    Seed,

    /// <summary>
    /// <c>stitchTiles</c>, on <c>feTurbulence</c>.
    /// </summary>
    StitchTiles,

    /// <summary>
    /// <c>radius</c>, on <c>feMorphology</c>.
    /// </summary>
    Radius
}
