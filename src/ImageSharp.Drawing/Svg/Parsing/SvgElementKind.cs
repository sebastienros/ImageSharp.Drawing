// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of an element in the <see cref="SvgElementTree"/>.
/// </summary>
/// <remarks>
/// The kinds from <see cref="Svg"/> to <see cref="Style"/> are the phase 1 elements; the reader parses their geometry
/// and reference attributes. The kinds from <see cref="A"/> on are SVG elements of later phases. The reader recognizes
/// them so that selectors and later phases can name them. It parses the attributes of <see cref="ClipPath"/>,
/// <see cref="Mask"/>, <see cref="Pattern"/>, <see cref="Marker"/>, and <see cref="Image"/>, and the common attributes
/// of the others. Until the phase that renders a kind, neither it nor its children render, the same as
/// <see cref="Unknown"/>.
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
    /// <c>clipPath</c>. The reader parses its attributes; it renders from phase 2.
    /// </summary>
    ClipPath,

    /// <summary>
    /// <c>mask</c>. The reader parses its attributes; it renders from phase 2.
    /// </summary>
    Mask,

    /// <summary>
    /// <c>pattern</c>. The reader parses its attributes; it renders from phase 2.
    /// </summary>
    Pattern,

    /// <summary>
    /// <c>marker</c>. The reader parses its attributes; it renders from phase 2.
    /// </summary>
    Marker,

    /// <summary>
    /// <c>image</c>. The reader parses its attributes; it renders from phase 2.
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
    /// <c>filter</c>. The reader parses its attributes and those of the filter primitives from <see cref="FeBlend"/> on;
    /// it renders through the <c>filter</c> property.
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
    Metadata,

    /// <summary>
    /// <c>tref</c> (SVG 1.1, removed in SVG 2). A text content child that references the character data of another
    /// element.
    /// </summary>
    Tref,

    /// <summary>
    /// <c>feBlend</c>, a filter primitive.
    /// </summary>
    FeBlend,

    /// <summary>
    /// <c>feColorMatrix</c>, a filter primitive.
    /// </summary>
    FeColorMatrix,

    /// <summary>
    /// <c>feComponentTransfer</c>, a filter primitive.
    /// </summary>
    FeComponentTransfer,

    /// <summary>
    /// <c>feComposite</c>, a filter primitive.
    /// </summary>
    FeComposite,

    /// <summary>
    /// <c>feConvolveMatrix</c>, a filter primitive.
    /// </summary>
    FeConvolveMatrix,

    /// <summary>
    /// <c>feDiffuseLighting</c>, a filter primitive.
    /// </summary>
    FeDiffuseLighting,

    /// <summary>
    /// <c>feDisplacementMap</c>, a filter primitive.
    /// </summary>
    FeDisplacementMap,

    /// <summary>
    /// <c>feDropShadow</c>, a filter primitive.
    /// </summary>
    FeDropShadow,

    /// <summary>
    /// <c>feFlood</c>, a filter primitive.
    /// </summary>
    FeFlood,

    /// <summary>
    /// <c>feGaussianBlur</c>, a filter primitive.
    /// </summary>
    FeGaussianBlur,

    /// <summary>
    /// <c>feImage</c>, a filter primitive.
    /// </summary>
    FeImage,

    /// <summary>
    /// <c>feMerge</c>, a filter primitive.
    /// </summary>
    FeMerge,

    /// <summary>
    /// <c>feMorphology</c>, a filter primitive.
    /// </summary>
    FeMorphology,

    /// <summary>
    /// <c>feOffset</c>, a filter primitive.
    /// </summary>
    FeOffset,

    /// <summary>
    /// <c>feSpecularLighting</c>, a filter primitive.
    /// </summary>
    FeSpecularLighting,

    /// <summary>
    /// <c>feTile</c>, a filter primitive.
    /// </summary>
    FeTile,

    /// <summary>
    /// <c>feTurbulence</c>, a filter primitive.
    /// </summary>
    FeTurbulence,

    /// <summary>
    /// <c>feMergeNode</c>, a child of <c>feMerge</c>.
    /// </summary>
    FeMergeNode,

    /// <summary>
    /// <c>feFuncR</c>, a child of <c>feComponentTransfer</c>.
    /// </summary>
    FeFuncR,

    /// <summary>
    /// <c>feFuncG</c>, a child of <c>feComponentTransfer</c>.
    /// </summary>
    FeFuncG,

    /// <summary>
    /// <c>feFuncB</c>, a child of <c>feComponentTransfer</c>.
    /// </summary>
    FeFuncB,

    /// <summary>
    /// <c>feFuncA</c>, a child of <c>feComponentTransfer</c>.
    /// </summary>
    FeFuncA,

    /// <summary>
    /// <c>feDistantLight</c>, a light source of the lighting primitives.
    /// </summary>
    FeDistantLight,

    /// <summary>
    /// <c>fePointLight</c>, a light source of the lighting primitives.
    /// </summary>
    FePointLight,

    /// <summary>
    /// <c>feSpotLight</c>, a light source of the lighting primitives.
    /// </summary>
    FeSpotLight
}
