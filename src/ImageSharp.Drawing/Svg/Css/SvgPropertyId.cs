// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The style properties that the parser reads. The value of each member is its bit index in an
/// <see cref="SvgPropertySet"/>, which holds up to <see cref="SvgPropertySet.Capacity"/> properties.
/// </summary>
/// <remarks>
/// The members below 64 have their mask bits inline in <see cref="SvgSpecifiedStyle"/>. The members from
/// <see cref="X"/> on have theirs in <see cref="SvgExtendedStyle"/>, with their values, so documents that specify none of
/// them pay nothing for them. A new property gets the next free id from 64 on.
/// </remarks>
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
    Transform,

    /// <summary>
    /// <c>font-family</c>. Inherited. Initial: the default family of the load options. The first text property: the
    /// values of every member from here on live in <see cref="SvgTextStyle"/>, which <see cref="SvgExtendedStyle.Text"/>
    /// and <see cref="SvgComputedStyle.Text"/> hold.
    /// </summary>
    FontFamily,

    /// <summary>
    /// <c>font-style</c>. Inherited. Initial <c>normal</c>.
    /// </summary>
    FontStyle,

    /// <summary>
    /// <c>font-weight</c>. Inherited. Initial <c>normal</c> (400).
    /// </summary>
    FontWeight,

    /// <summary>
    /// <c>font-stretch</c>. Inherited. Initial <c>normal</c> (100%).
    /// </summary>
    FontStretch,

    /// <summary>
    /// <c>font-variant</c>. Inherited. Initial <c>normal</c>.
    /// </summary>
    FontVariant,

    /// <summary>
    /// <c>font-size-adjust</c>. Inherited. Initial <c>none</c>.
    /// </summary>
    FontSizeAdjust,

    /// <summary>
    /// <c>font-kerning</c>. Inherited. Initial <c>auto</c>.
    /// </summary>
    FontKerning,

    /// <summary>
    /// <c>text-anchor</c>. Inherited. Initial <c>start</c>.
    /// </summary>
    TextAnchor,

    /// <summary>
    /// <c>text-decoration-line</c>. Not inherited. Initial <c>none</c>. The shorthand <c>text-decoration</c> sets it.
    /// </summary>
    TextDecorationLine,

    /// <summary>
    /// <c>text-decoration-style</c>. Not inherited. Initial <c>solid</c>. The shorthand <c>text-decoration</c> sets it.
    /// </summary>
    TextDecorationStyle,

    /// <summary>
    /// <c>text-decoration-color</c>. Not inherited. Initial <c>currentColor</c>. The shorthand <c>text-decoration</c>
    /// sets it.
    /// </summary>
    TextDecorationColor,

    /// <summary>
    /// <c>letter-spacing</c>. Inherited. Initial <c>normal</c>.
    /// </summary>
    LetterSpacing,

    /// <summary>
    /// <c>word-spacing</c>. Inherited. Initial <c>normal</c>.
    /// </summary>
    WordSpacing,

    /// <summary>
    /// <c>dominant-baseline</c>. Inherited (SVG 2). Initial <c>auto</c>.
    /// </summary>
    DominantBaseline,

    /// <summary>
    /// <c>alignment-baseline</c>. Not inherited. Initial <c>auto</c>.
    /// </summary>
    AlignmentBaseline,

    /// <summary>
    /// <c>baseline-shift</c>. Not inherited. Initial <c>baseline</c>.
    /// </summary>
    BaselineShift,

    /// <summary>
    /// <c>writing-mode</c>. Inherited. Initial <c>horizontal-tb</c>.
    /// </summary>
    WritingMode,

    /// <summary>
    /// <c>direction</c>. Inherited. Initial <c>ltr</c>.
    /// </summary>
    Direction,

    /// <summary>
    /// <c>unicode-bidi</c>. Not inherited. Initial <c>normal</c>.
    /// </summary>
    UnicodeBidi,

    /// <summary>
    /// <c>white-space</c>. Inherited. Initial <c>normal</c>. The <c>xml:space</c> attribute of text content elements sets
    /// it at the precedence of a presentation attribute.
    /// </summary>
    WhiteSpace,

    /// <summary>
    /// <c>text-rendering</c>. Inherited. Initial <c>auto</c>.
    /// </summary>
    TextRendering,

    /// <summary>
    /// <c>kerning</c> (SVG 1.1). Inherited. Initial <c>auto</c>.
    /// </summary>
    Kerning,

    /// <summary>
    /// <c>glyph-orientation-vertical</c> (SVG 1.1). Inherited. Initial <c>auto</c>.
    /// </summary>
    GlyphOrientationVertical,

    /// <summary>
    /// <c>glyph-orientation-horizontal</c> (SVG 1.1). Inherited. Initial <c>0deg</c>. The last text property.
    /// </summary>
    GlyphOrientationHorizontal,

    /// <summary>
    /// <c>filter</c>. Not inherited. Initial <c>none</c>. The first filter property; the values of the filter properties
    /// live in <see cref="SvgFilterStyle"/>.
    /// </summary>
    Filter,

    /// <summary>
    /// <c>color-interpolation-filters</c>. Inherited. Initial <c>linearRGB</c>.
    /// </summary>
    ColorInterpolationFilters,

    /// <summary>
    /// <c>flood-color</c>. Not inherited. Initial <c>black</c>.
    /// </summary>
    FloodColor,

    /// <summary>
    /// <c>flood-opacity</c>. Not inherited. Initial <c>1</c>.
    /// </summary>
    FloodOpacity,

    /// <summary>
    /// <c>lighting-color</c>. Not inherited. Initial <c>white</c>. The last property with an inline mask bit.
    /// </summary>
    LightingColor,

    /// <summary>
    /// The CSS geometry property <c>x</c> (SVG 2). Not inherited. Initial <c>0</c>. The first geometry property: the
    /// values of the geometry properties live in <see cref="SvgGeometryStyle"/>. A presentation attribute of the same name
    /// stays in the attribute record of the element; only declarations in <c>style</c> and in style sheets set the
    /// property. The first property whose mask bits live in <see cref="SvgExtendedStyle"/>.
    /// </summary>
    X = 64,

    /// <summary>
    /// The CSS geometry property <c>y</c>. Not inherited. Initial <c>0</c>.
    /// </summary>
    Y,

    /// <summary>
    /// The CSS geometry property <c>width</c>. Not inherited. Initial <c>auto</c>.
    /// </summary>
    Width,

    /// <summary>
    /// The CSS geometry property <c>height</c>. Not inherited. Initial <c>auto</c>.
    /// </summary>
    Height,

    /// <summary>
    /// The CSS geometry property <c>cx</c>. Not inherited. Initial <c>0</c>.
    /// </summary>
    Cx,

    /// <summary>
    /// The CSS geometry property <c>cy</c>. Not inherited. Initial <c>0</c>.
    /// </summary>
    Cy,

    /// <summary>
    /// The CSS geometry property <c>r</c>. Not inherited. Initial <c>0</c>.
    /// </summary>
    R,

    /// <summary>
    /// The CSS geometry property <c>rx</c>. Not inherited. Initial <c>auto</c>.
    /// </summary>
    Rx,

    /// <summary>
    /// The CSS geometry property <c>ry</c>. Not inherited. Initial <c>auto</c>.
    /// </summary>
    Ry,

    /// <summary>
    /// The CSS property <c>d</c>: <c>none</c> or <c>path(&lt;string&gt;)</c>. Not inherited. Initial <c>none</c>. The
    /// last geometry property.
    /// </summary>
    D,

    /// <summary>
    /// <c>transform-box</c>. Not inherited. Initial <c>view-box</c>. CSS only: Chromium has no presentation attribute for
    /// it. Its value lives in <see cref="SvgGeometryStyle"/>.
    /// </summary>
    TransformBox,

    /// <summary>
    /// <c>font-feature-settings</c>. Inherited. Initial <c>normal</c>. CSS only. The first of the text properties with an
    /// id from 64 on; their values live in <see cref="SvgTextStyle"/>.
    /// </summary>
    FontFeatureSettings,

    /// <summary>
    /// <c>font-variant-ligatures</c>. Inherited. Initial <c>normal</c>. CSS only; the <c>font-variant</c> shorthand sets
    /// it too.
    /// </summary>
    FontVariantLigatures,

    /// <summary>
    /// <c>font-variant-caps</c>. Inherited. Initial <c>normal</c>. CSS only; the <c>font-variant</c> shorthand and the
    /// <c>font</c> shorthand set it too.
    /// </summary>
    FontVariantCaps,

    /// <summary>
    /// <c>font-variant-numeric</c>. Inherited. Initial <c>normal</c>. CSS only; the <c>font-variant</c> shorthand sets it
    /// too.
    /// </summary>
    FontVariantNumeric,

    /// <summary>
    /// <c>font-variant-east-asian</c>. Inherited. Initial <c>normal</c>. CSS only; the <c>font-variant</c> shorthand sets
    /// it too.
    /// </summary>
    FontVariantEastAsian,

    /// <summary>
    /// <c>font-variant-position</c>. Inherited. Initial <c>normal</c>. CSS only; the <c>font-variant</c> shorthand sets it
    /// too.
    /// </summary>
    FontVariantPosition,

    /// <summary>
    /// <c>white-space-collapse</c> (CSS Text Level 4). Inherited. Initial <c>collapse</c>. The <c>white-space</c>
    /// shorthand sets it too, so after the cascade it holds the collapse mode of whichever of the two declarations won.
    /// The last text property.
    /// </summary>
    WhiteSpaceCollapse
}
