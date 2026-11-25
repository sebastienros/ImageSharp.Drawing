// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The style properties that the parser reads. The value of each member is its bit index in the masks of
/// <see cref="SvgSpecifiedStyle"/>, so the list can grow to 64 properties without a change of representation. Later
/// phases add members such as <c>filter</c> and font properties at the end.
/// </summary>
internal enum SvgPropertyId : byte
{
    /// <summary>
    /// <c>color</c>. Inherited. The initial value is the load option for the current color.
    /// </summary>
    Color,

    /// <summary>
    /// <c>display</c>. Not inherited. Initial <c>inline</c>.
    /// </summary>
    Display,

    /// <summary>
    /// <c>visibility</c>. Inherited. Initial <c>visible</c>.
    /// </summary>
    Visibility,

    /// <summary>
    /// <c>opacity</c>. Not inherited. Initial <c>1</c>.
    /// </summary>
    Opacity,

    /// <summary>
    /// <c>fill</c>. Inherited. Initial <c>black</c>.
    /// </summary>
    Fill,

    /// <summary>
    /// <c>fill-opacity</c>. Inherited. Initial <c>1</c>.
    /// </summary>
    FillOpacity,

    /// <summary>
    /// <c>fill-rule</c>. Inherited. Initial <c>nonzero</c>.
    /// </summary>
    FillRule,

    /// <summary>
    /// <c>stroke</c>. Inherited. Initial <c>none</c>.
    /// </summary>
    Stroke,

    /// <summary>
    /// <c>stroke-opacity</c>. Inherited. Initial <c>1</c>.
    /// </summary>
    StrokeOpacity,

    /// <summary>
    /// <c>stroke-width</c>. Inherited. Initial <c>1</c>.
    /// </summary>
    StrokeWidth,

    /// <summary>
    /// <c>stroke-linecap</c>. Inherited. Initial <c>butt</c>.
    /// </summary>
    StrokeLineCap,

    /// <summary>
    /// <c>stroke-linejoin</c>. Inherited. Initial <c>miter</c>.
    /// </summary>
    StrokeLineJoin,

    /// <summary>
    /// <c>stroke-miterlimit</c>. Inherited. Initial <c>4</c>.
    /// </summary>
    StrokeMiterLimit,

    /// <summary>
    /// <c>stroke-dasharray</c>. Inherited. Initial <c>none</c>.
    /// </summary>
    StrokeDashArray,

    /// <summary>
    /// <c>stroke-dashoffset</c>. Inherited. Initial <c>0</c>.
    /// </summary>
    StrokeDashOffset,

    /// <summary>
    /// <c>font-size</c>. Inherited. Initial <c>medium</c>, which is 16px.
    /// </summary>
    FontSize,

    /// <summary>
    /// <c>stop-color</c>. Not inherited. Initial <c>black</c>.
    /// </summary>
    StopColor,

    /// <summary>
    /// <c>stop-opacity</c>. Not inherited. Initial <c>1</c>.
    /// </summary>
    StopOpacity,

    /// <summary>
    /// <c>clip-path</c>. Not inherited. Initial <c>none</c>. The first property whose value lives in
    /// <see cref="SvgExtendedStyle"/>; every member from here on does too.
    /// </summary>
    ClipPath,

    /// <summary>
    /// <c>clip-rule</c>. Inherited. Initial <c>nonzero</c>.
    /// </summary>
    ClipRule,

    /// <summary>
    /// <c>mask</c>. Not inherited. Initial <c>none</c>.
    /// </summary>
    Mask,

    /// <summary>
    /// <c>mask-type</c>. Not inherited. Initial <c>luminance</c>.
    /// </summary>
    MaskType,

    /// <summary>
    /// <c>mix-blend-mode</c>. Not inherited. Initial <c>normal</c>.
    /// </summary>
    MixBlendMode,

    /// <summary>
    /// <c>isolation</c>. Not inherited. Initial <c>auto</c>.
    /// </summary>
    Isolation,

    /// <summary>
    /// <c>paint-order</c>. Inherited. Initial <c>normal</c>.
    /// </summary>
    PaintOrder,

    /// <summary>
    /// <c>vector-effect</c>. Not inherited. Initial <c>none</c>.
    /// </summary>
    VectorEffect,

    /// <summary>
    /// <c>overflow</c>. Not inherited. Initial <c>visible</c>; the SVG user agent style sheet makes it <c>hidden</c> on
    /// <c>svg</c>, <c>symbol</c>, <c>image</c>, <c>marker</c>, and <c>pattern</c>.
    /// </summary>
    Overflow,

    /// <summary>
    /// <c>marker-start</c>. Inherited. Initial <c>none</c>. The CSS shorthand <c>marker</c> sets it.
    /// </summary>
    MarkerStart,

    /// <summary>
    /// <c>marker-mid</c>. Inherited. Initial <c>none</c>. The CSS shorthand <c>marker</c> sets it.
    /// </summary>
    MarkerMid,

    /// <summary>
    /// <c>marker-end</c>. Inherited. Initial <c>none</c>. The CSS shorthand <c>marker</c> sets it.
    /// </summary>
    MarkerEnd,

    /// <summary>
    /// <c>image-rendering</c>. Inherited. Initial <c>auto</c>.
    /// </summary>
    ImageRendering,

    /// <summary>
    /// <c>shape-rendering</c>. Inherited. Initial <c>auto</c>.
    /// </summary>
    ShapeRendering,

    /// <summary>
    /// <c>color-interpolation</c>. Inherited. Initial <c>sRGB</c>.
    /// </summary>
    ColorInterpolation,

    /// <summary>
    /// <c>transform-origin</c>. Not inherited. Initial <c>0 0</c>, the used initial value for SVG elements without a CSS
    /// layout box.
    /// </summary>
    TransformOrigin,

    /// <summary>
    /// <c>transform</c> as a CSS property, from a style sheet or a <c>style</c> attribute. Not inherited. Initial
    /// <c>none</c>. The <c>transform</c> attribute is its presentation attribute, but the reader stores the attribute in
    /// the transform table of the element instead; after the cascade, a CSS value replaces the attribute there (see
    /// <see cref="SvgElementTree.TryGetTransform(int, out System.Numerics.Matrix3x2)"/>). The computed style does not
    /// carry it.
    /// </summary>
    Transform
}
