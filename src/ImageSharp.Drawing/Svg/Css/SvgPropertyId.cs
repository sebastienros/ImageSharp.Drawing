// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The style properties that the parser reads. The value of each member is its bit index in the masks of
/// <see cref="SvgSpecifiedStyle"/>, so the list can grow to 64 properties without a change of representation. Later
/// phases add members such as <c>clip-path</c>, <c>mask</c>, <c>filter</c>, markers, and font properties at the end.
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
    StopOpacity
}
