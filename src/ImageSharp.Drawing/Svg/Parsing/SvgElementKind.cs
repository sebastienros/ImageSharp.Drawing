// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of an element in the <see cref="SvgElementTree"/>.
/// </summary>
/// <remarks>
/// The kinds from <see cref="Svg"/> to <see cref="Style"/> are the phase 1 elements; the reader parses their geometry
/// and reference attributes. The kinds from <see cref="A"/> on are SVG elements of later phases. The reader recognizes
/// them so that selectors and later phases can name them, but reads only their common attributes; phase 1 renders neither
/// them nor their children, the same as <see cref="Unknown"/>.
/// </remarks>
internal enum SvgElementKind : byte
{
    /// <summary>
    /// An element in another namespace, or an SVG element name that the reader does not know. It does not render, and its
    /// children do not render, but it stays in the tree so that structure and selectors still work.
    /// </summary>
    Unknown,

    /// <summary>
    /// <c>svg</c>.
    /// </summary>
    Svg,

    /// <summary>
    /// <c>g</c>.
    /// </summary>
    G,

    /// <summary>
    /// <c>defs</c>.
    /// </summary>
    Defs,

    /// <summary>
    /// <c>use</c>.
    /// </summary>
    Use,

    /// <summary>
    /// <c>symbol</c>.
    /// </summary>
    Symbol,

    /// <summary>
    /// <c>path</c>.
    /// </summary>
    Path,

    /// <summary>
    /// <c>rect</c>.
    /// </summary>
    Rect,

    /// <summary>
    /// <c>circle</c>.
    /// </summary>
    Circle,

    /// <summary>
    /// <c>ellipse</c>.
    /// </summary>
    Ellipse,

    /// <summary>
    /// <c>line</c>.
    /// </summary>
    Line,

    /// <summary>
    /// <c>polyline</c>.
    /// </summary>
    Polyline,

    /// <summary>
    /// <c>polygon</c>.
    /// </summary>
    Polygon,

    /// <summary>
    /// <c>linearGradient</c>.
    /// </summary>
    LinearGradient,

    /// <summary>
    /// <c>radialGradient</c>.
    /// </summary>
    RadialGradient,

    /// <summary>
    /// <c>stop</c>.
    /// </summary>
    Stop,

    /// <summary>
    /// <c>style</c>. Its text has already been read into the style sheet.
    /// </summary>
    Style,

    /// <summary>
    /// <c>a</c>. Recognized for later phases.
    /// </summary>
    A,

    /// <summary>
    /// <c>clipPath</c>. Recognized for later phases.
    /// </summary>
    ClipPath,

    /// <summary>
    /// <c>mask</c>. Recognized for later phases.
    /// </summary>
    Mask,

    /// <summary>
    /// <c>pattern</c>. Recognized for later phases.
    /// </summary>
    Pattern,

    /// <summary>
    /// <c>marker</c>. Recognized for later phases.
    /// </summary>
    Marker,

    /// <summary>
    /// <c>image</c>. Recognized for later phases.
    /// </summary>
    Image,

    /// <summary>
    /// <c>switch</c>. Recognized for later phases.
    /// </summary>
    Switch,

    /// <summary>
    /// <c>text</c>. Recognized for later phases.
    /// </summary>
    Text,

    /// <summary>
    /// <c>tspan</c>. Recognized for later phases.
    /// </summary>
    TSpan,

    /// <summary>
    /// <c>textPath</c>. Recognized for later phases.
    /// </summary>
    TextPath,

    /// <summary>
    /// <c>filter</c>. Recognized for later phases.
    /// </summary>
    Filter,

    /// <summary>
    /// <c>foreignObject</c>. Never rendered.
    /// </summary>
    ForeignObject,

    /// <summary>
    /// <c>title</c>. Never rendered.
    /// </summary>
    Title,

    /// <summary>
    /// <c>desc</c>. Never rendered.
    /// </summary>
    Desc,

    /// <summary>
    /// <c>metadata</c>. Never rendered.
    /// </summary>
    Metadata
}
