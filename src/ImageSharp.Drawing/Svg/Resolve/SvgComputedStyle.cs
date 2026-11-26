// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The computed values of the style properties of one element: the cascaded specified value, else the parent value for
/// an inherited property, else the initial value.
/// </summary>
/// <remarks>
/// <para>
/// The resolve walk computes the style top down. Content that a <c>use</c> element references takes the computed style of
/// the <c>use</c> element as its parent, so the same element can have a different computed style in each instance.
/// Content of <c>clipPath</c>, <c>mask</c>, <c>pattern</c>, and <c>marker</c> inherits from the ancestors of that element
/// in the document instead; see <see cref="SvgResolver.ComputeDocumentStyle(int)"/>.
/// </para>
/// <para>
/// Lengths keep the CSS computed form: an absolute length or a font-relative length becomes pixels at the element that
/// specifies it, and a percentage stays a percentage, because it resolves against the viewport of the element that uses
/// it. <c>currentColor</c> in <see cref="Fill"/>, <see cref="Stroke"/>, and <see cref="StopColor"/> stays a keyword and
/// resolves against <see cref="Color"/> of the element that paints, as CSS Color Level 4 defines.
/// </para>
/// <para>
/// Every property of <see cref="SvgPropertyId"/> has a member here except <see cref="SvgPropertyId.Transform"/>, which
/// the reader folds into the transform table of the element, the text properties, whose values live together in
/// <see cref="Text"/>, and the geometry properties and <c>transform-box</c>, whose values live together in
/// <see cref="Geometry"/>.
/// </para>
/// <para>
/// Declarations with <c>var()</c> are not substituted yet: a property whose winning declaration has <c>var()</c>
/// computes from the declaration of next lower precedence, as if the <c>var()</c> declaration were invalid at parse time.
/// The pending declarations stay in the specified style (<see cref="SvgSpecifiedStyle.Pending"/>), the computed custom
/// properties are in <see cref="CustomProperties"/>, and <see cref="SubstituteVariables"/> is where the substitution
/// joins the computation.
/// </para>
/// </remarks>
internal partial struct SvgComputedStyle
{
    /// <summary>
    /// Gets or sets <c>color</c>.
    /// </summary>
    public Color Color { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>display</c>.
    /// </summary>
    public SvgDisplay Display { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>visibility</c>.
    /// </summary>
    public SvgVisibility Visibility { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>opacity</c>, in the range 0 to 1.
    /// </summary>
    public float Opacity { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>fill</c>. <see cref="SvgPaintKind.ContextFill"/> and <see cref="SvgPaintKind.ContextStroke"/> stay
    /// keywords; they resolve against the context element of the painting element.
    /// </summary>
    public SvgPaint Fill { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>fill-opacity</c>, in the range 0 to 1.
    /// </summary>
    public float FillOpacity { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>fill-rule</c>.
    /// </summary>
    public SvgFillRule FillRule { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>stroke</c>, with the same keyword rules as <see cref="Fill"/>.
    /// </summary>
    public SvgPaint Stroke { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>stroke-opacity</c>, in the range 0 to 1.
    /// </summary>
    public float StrokeOpacity { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>stroke-width</c>, in <see cref="SvgLengthUnit.Px"/> or <see cref="SvgLengthUnit.Percent"/>.
    /// </summary>
    public SvgLength StrokeWidth { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>stroke-linecap</c>.
    /// </summary>
    public SvgStrokeLineCap StrokeLineCap { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>stroke-linejoin</c>.
    /// </summary>
    public SvgStrokeLineJoin StrokeLineJoin { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>stroke-miterlimit</c>. It is never less than 1.
    /// </summary>
    public float StrokeMiterLimit { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>stroke-dasharray</c>, as a range in the dash length table of the element tree.
    /// </summary>
    public SvgDashArray StrokeDashArray { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>stroke-dashoffset</c>, in <see cref="SvgLengthUnit.Px"/> or <see cref="SvgLengthUnit.Percent"/>.
    /// </summary>
    public SvgLength StrokeDashOffset { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>font-size</c> in pixels.
    /// </summary>
    public float FontSize { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>stop-color</c>, of kind <see cref="SvgPaintKind.Color"/> or <see cref="SvgPaintKind.CurrentColor"/>.
    /// </summary>
    public SvgPaint StopColor { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>stop-opacity</c>, in the range 0 to 1.
    /// </summary>
    public float StopOpacity { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>clip-path</c>. Not inherited.
    /// </summary>
    public SvgReference ClipPath { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>clip-rule</c>, the fill rule of an element when it is a child of a <c>clipPath</c>. Inherited.
    /// </summary>
    public SvgFillRule ClipRule { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>mask</c>. Not inherited.
    /// </summary>
    public SvgReference Mask { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>mask-type</c>, which applies to a <c>mask</c> element. Not inherited.
    /// </summary>
    public SvgMaskType MaskType { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>mix-blend-mode</c>. Not inherited.
    /// </summary>
    public SvgBlendMode MixBlendMode { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>isolation</c>. Not inherited.
    /// </summary>
    public SvgIsolation Isolation { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>paint-order</c>. Inherited.
    /// </summary>
    public SvgPaintOrder PaintOrder { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>vector-effect</c>. Not inherited.
    /// </summary>
    public SvgVectorEffect VectorEffect { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>overflow</c>. Not inherited. The value includes the SVG 2 user agent style sheet rule that sets
    /// <see cref="SvgOverflow.Hidden"/> on <c>svg</c>, <c>symbol</c>, <c>image</c>, <c>marker</c>, and <c>pattern</c>
    /// elements that do not specify the property. The rule excludes the root element, which computes through
    /// <see cref="ComputeRoot"/>; the root element always clips its viewport, but its value is what <c>inherit</c> takes.
    /// </summary>
    public SvgOverflow Overflow { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>marker-start</c>. Inherited.
    /// </summary>
    public SvgReference MarkerStart { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>marker-mid</c>. Inherited.
    /// </summary>
    public SvgReference MarkerMid { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>marker-end</c>. Inherited.
    /// </summary>
    public SvgReference MarkerEnd { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>image-rendering</c>. Inherited.
    /// </summary>
    public SvgImageRendering ImageRendering { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>shape-rendering</c>. Inherited.
    /// </summary>
    public SvgShapeRendering ShapeRendering { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>color-interpolation</c>. Inherited.
    /// </summary>
    public SvgColorInterpolation ColorInterpolation { readonly get; set; }

    /// <summary>
    /// Gets the horizontal component of <c>transform-origin</c>, in <see cref="SvgLengthUnit.Px"/> or
    /// <see cref="SvgLengthUnit.Percent"/>. Not inherited. A percentage refers to the width of the reference box; see
    /// <see cref="GetTransformOrigin(SizeF)"/>. The value lives in <see cref="Geometry"/>.
    /// </summary>
    public readonly SvgLength TransformOriginX => this.Geometry.TransformOriginX;

    /// <summary>
    /// Gets the vertical component of <c>transform-origin</c>, with the same rules as <see cref="TransformOriginX"/> for
    /// the height.
    /// </summary>
    public readonly SvgLength TransformOriginY => this.Geometry.TransformOriginY;

    /// <summary>
    /// Gets or sets the computed values of the text properties. Never <see langword="null"/> in a computed style. The
    /// instance is shared: elements that specify no text property and inherit no value of a property that does not
    /// inherit share the instance of their parent, so equal references mean equal text values.
    /// </summary>
    public SvgTextStyle Text { readonly get; set; }

    /// <summary>
    /// Gets or sets the computed values of the filter properties: <c>filter</c>, <c>color-interpolation-filters</c>
    /// (inherited), <c>flood-color</c>, <c>flood-opacity</c>, and <c>lighting-color</c>. Never <see langword="null"/> in a
    /// computed style. The instance is shared and never changes: elements that specify none of these properties share
    /// the initial values for the color space they inherit (<see cref="SvgFilterStyle.GetChildStyle"/>), so only elements
    /// that specify one allocate. <c>currentColor</c> stays a keyword, as for <see cref="StopColor"/>.
    /// </summary>
    public SvgFilterStyle Filters { readonly get; set; }

    /// <summary>
    /// Gets or sets the computed values of the geometry properties (<c>x</c>, <c>y</c>, <c>width</c>, <c>height</c>,
    /// <c>cx</c>, <c>cy</c>, <c>r</c>, <c>rx</c>, <c>ry</c>, <c>d</c>), of <c>transform-box</c>, and of
    /// <c>transform-origin</c>. None of them inherits. Never <see langword="null"/> in a computed style: an element that specifies none of them has
    /// <see cref="SvgGeometryStyle.Initial"/>. A geometry value counts only for the properties in
    /// <see cref="SvgGeometryStyle.FromStyle"/>; the element uses its attribute for the others.
    /// </summary>
    public SvgGeometryStyle Geometry { readonly get; set; }

    /// <summary>
    /// Gets or sets the computed custom properties (<c>--name</c>), which inherit, or <see langword="null"/> when neither
    /// the element nor an ancestor declares one. Elements that declare none share the instance of their parent.
    /// </summary>
    public SvgCustomProperties? CustomProperties { readonly get; set; }

    /// <summary>
    /// Gets <c>filter</c>, or <see langword="null"/> for <c>none</c>. Not inherited.
    /// </summary>
    public readonly SvgFilterList? Filter => this.Filters.Filter;

    /// <summary>
    /// Gets <c>color-interpolation-filters</c>. Inherited.
    /// </summary>
    public readonly SvgColorInterpolation ColorInterpolationFilters => this.Filters.ColorInterpolationFilters;

    /// <summary>
    /// Gets the values of <c>flood-color</c>, <c>flood-opacity</c>, and <c>lighting-color</c>, which are not inherited.
    /// </summary>
    public readonly SvgFilterStyle FilterColors => this.Filters;

    /// <summary>
    /// Creates the style that holds the initial value of every property. It is the parent style of the root element.
    /// </summary>
    /// <param name="currentColor">The initial value of <c>color</c>.</param>
    /// <returns>The initial style.</returns>
    public static SvgComputedStyle CreateInitial(Color currentColor)
        => new()
        {
            Color = currentColor,
            Display = SvgDisplay.Inline,
            Visibility = SvgVisibility.Visible,
            Opacity = 1F,
            Fill = new SvgPaint(SvgPaintKind.Color, Color.Black, null, SvgPaintKind.None),
            FillOpacity = 1F,
            FillRule = SvgFillRule.NonZero,
            Stroke = default,
            StrokeOpacity = 1F,
            StrokeWidth = new SvgLength(1F, SvgLengthUnit.Px),
            StrokeLineCap = SvgStrokeLineCap.Butt,
            StrokeLineJoin = SvgStrokeLineJoin.Miter,
            StrokeMiterLimit = 4F,
            StrokeDashArray = default,
            StrokeDashOffset = new SvgLength(0F, SvgLengthUnit.Px),
            FontSize = SvgUnitBasis.InitialFontSize,
            StopColor = new SvgPaint(SvgPaintKind.Color, Color.Black, null, SvgPaintKind.None),
            StopOpacity = 1F,
            ClipPath = default,
            ClipRule = SvgFillRule.NonZero,
            Mask = default,
            MaskType = SvgMaskType.Luminance,
            MixBlendMode = SvgBlendMode.Normal,
            Isolation = SvgIsolation.Auto,
            PaintOrder = SvgPaintOrder.FillStrokeMarkers,
            VectorEffect = SvgVectorEffect.None,
            Overflow = SvgOverflow.Visible,
            MarkerStart = default,
            MarkerMid = default,
            MarkerEnd = default,
            ImageRendering = SvgImageRendering.Auto,
            ShapeRendering = SvgShapeRendering.Auto,
            ColorInterpolation = SvgColorInterpolation.SRgb,
            Text = SvgTextStyle.Initial,
            Filters = SvgFilterStyle.Initial,
            Geometry = SvgGeometryStyle.Initial,
            CustomProperties = null,
        };

    /// <summary>
    /// Computes the style of an element from its cascaded specified style and the computed style of its parent.
    /// </summary>
    /// <param name="specified">The cascaded specified style of the element.</param>
    /// <param name="parent">The computed style of the parent, or the initial style for the root.</param>
    /// <param name="initial">The initial style.</param>
    /// <param name="units">The document values that length units refer to.</param>
    /// <param name="kind">The kind of the element, for the user agent style sheet rule of <see cref="Overflow"/>.</param>
    /// <returns>The computed style.</returns>
    public static SvgComputedStyle Compute(
        in SvgSpecifiedStyle specified,
        in SvgComputedStyle parent,
        in SvgComputedStyle initial,
        SvgUnitBasis units,
        SvgElementKind kind)
    {
        // Start from the parent, which is the computed value of every inherited property that the element does not
        // specify. Properties that do not inherit start from their initial values instead.
        SvgComputedStyle style = parent;
        style.Display = initial.Display;
        style.Opacity = initial.Opacity;
        style.StopColor = initial.StopColor;
        style.StopOpacity = initial.StopOpacity;
        style.ClipPath = initial.ClipPath;
        style.Mask = initial.Mask;
        style.MaskType = initial.MaskType;
        style.MixBlendMode = initial.MixBlendMode;
        style.Isolation = initial.Isolation;
        style.VectorEffect = initial.VectorEffect;
        style.Text = parent.Text.GetChildStyle();
        style.Filters = parent.Filters.GetChildStyle();
        style.Geometry = initial.Geometry;

        // SVG 2 user agent style sheet: svg:not(:root), symbol, image, marker, pattern { overflow: hidden }. It has the
        // lowest precedence, so any specified value, including inherit and initial, replaces it below.
        style.Overflow = kind is SvgElementKind.Svg or SvgElementKind.Symbol or SvgElementKind.Image or SvgElementKind.Marker or SvgElementKind.Pattern
            ? SvgOverflow.Hidden
            : initial.Overflow;

        // A text element starts from the initial white-space-collapse, as in the Chromium oracle, whose user agent style
        // sheet sets it on text: a value on an ancestor outside the text, such as a g, does not reach the text. Any value
        // that the text specifies, inherit included, replaces this below.
        if (kind == SvgElementKind.Text && style.Text.WhiteSpaceCollapse != initial.Text.WhiteSpaceCollapse)
        {
            SvgTextStyle text = style.Text.Clone();
            text.WhiteSpaceCollapse = initial.Text.WhiteSpaceCollapse;
            style.Text = text;
        }

        if (specified.IsEmpty)
        {
            return style;
        }

        // Font size first: em and ex lengths of the other properties refer to the computed font size of the element.
        if (specified.IsSpecified(SvgPropertyId.FontSize))
        {
            style.FontSize = ComputeFontSize(in specified, in parent, in initial, units);
        }

        // The text properties share one object, which changes as a whole, and so do the geometry properties.
        SvgPropertySet specifiedSet = specified.Specified;
        SvgPropertySet textBits = specifiedSet & SvgTextStyle.Properties;
        if (!textBits.IsEmpty)
        {
            style.Text = ComputeText(in specified, textBits, style.Text, parent.Text, initial.Text, style.FontSize, units);
        }

        SvgExtendedStyle? extended = specified.Extended;
        SvgPropertySet geometryBits = specifiedSet & SvgGeometryStyle.Properties;
        if (!geometryBits.IsEmpty)
        {
            style.Geometry = ComputeGeometry(in specified, geometryBits, parent.Geometry, initial.Geometry, style.FontSize, units);
        }

        // Custom properties inherit; an element without declarations keeps the map of its parent.
        if (extended?.Variables?.CustomProperties is IReadOnlyDictionary<string, SvgCustomPropertyValue> custom)
        {
            style.CustomProperties = SvgCustomProperties.Compute(parent.CustomProperties, custom);
        }

        // Visit only the specified inline and extended properties below 64. The inherit keyword takes the parent value
        // for any property, and the initial keyword takes the initial value. Every property from 64 on is a text or a
        // geometry property, computed above.
        ulong bits = specifiedSet.Low & ~(SvgTextStyle.Properties.Low | SvgGeometryStyle.Properties.Low);
        while (bits != 0)
        {
            SvgPropertyId property = (SvgPropertyId)BitOperations.TrailingZeroCount(bits);
            bits &= bits - 1;

            if (property == SvgPropertyId.FontSize)
            {
                continue;
            }

            if (specified.IsInherit(property))
            {
                CopyProperty(property, in parent, ref style);
                continue;
            }

            if (specified.IsInitial(property))
            {
                CopyProperty(property, in initial, ref style);
                continue;
            }

            switch (property)
            {
                case SvgPropertyId.Color:
                    style.Color = specified.Color;
                    break;
                case SvgPropertyId.Display:
                    style.Display = specified.Display;
                    break;
                case SvgPropertyId.Visibility:
                    style.Visibility = specified.Visibility;
                    break;
                case SvgPropertyId.Opacity:
                    style.Opacity = specified.Opacity;
                    break;
                case SvgPropertyId.Fill:
                    style.Fill = specified.Fill;
                    break;
                case SvgPropertyId.FillOpacity:
                    style.FillOpacity = specified.FillOpacity;
                    break;
                case SvgPropertyId.FillRule:
                    style.FillRule = specified.FillRule;
                    break;
                case SvgPropertyId.Stroke:
                    style.Stroke = specified.Stroke;
                    break;
                case SvgPropertyId.StrokeOpacity:
                    style.StrokeOpacity = specified.StrokeOpacity;
                    break;
                case SvgPropertyId.StrokeWidth:
                    style.StrokeWidth = ToComputedLength(specified.StrokeWidth, style.FontSize, units);
                    break;
                case SvgPropertyId.StrokeLineCap:
                    style.StrokeLineCap = specified.StrokeLineCap;
                    break;
                case SvgPropertyId.StrokeLineJoin:
                    style.StrokeLineJoin = specified.StrokeLineJoin;
                    break;
                case SvgPropertyId.StrokeMiterLimit:
                    style.StrokeMiterLimit = specified.StrokeMiterLimit;
                    break;
                case SvgPropertyId.StrokeDashArray:
                    style.StrokeDashArray = specified.StrokeDashArray;
                    break;
                case SvgPropertyId.StrokeDashOffset:
                    style.StrokeDashOffset = ToComputedLength(specified.StrokeDashOffset, style.FontSize, units);
                    break;
                case SvgPropertyId.StopColor:
                    style.StopColor = specified.StopColor;
                    break;
                case SvgPropertyId.StopOpacity:
                    style.StopOpacity = specified.StopOpacity;
                    break;
                default:
                    // A value bit of an extended property means the parser stored its value in the extension.
                    ComputeExtended(property, extended!, ref style, units);
                    break;
            }
        }

        if (extended?.Variables is not null)
        {
            SubstituteVariables(in specified, in parent, in initial, units, kind, ref style);
        }

        return style;
    }

    /// <summary>
    /// Computes the style of the root element. It is <see cref="Compute"/> with the initial style as the parent, except
    /// that the user agent style sheet rule for <see cref="Overflow"/> does not apply: the rule selects
    /// <c>svg:not(:root)</c>. The root clips its viewport whatever its value, but a child that inherits
    /// <c>overflow</c> from the root, such as a <c>marker</c> with <c>overflow="inherit"</c>, gets
    /// <see cref="SvgOverflow.Visible"/>.
    /// </summary>
    /// <param name="specified">The cascaded specified style of the root element.</param>
    /// <param name="initial">The initial style.</param>
    /// <param name="units">The unit basis of the root element.</param>
    /// <returns>The computed style.</returns>
    public static SvgComputedStyle ComputeRoot(in SvgSpecifiedStyle specified, in SvgComputedStyle initial, SvgUnitBasis units)
    {
        SvgComputedStyle style = Compute(in specified, in initial, in initial, units, SvgElementKind.Svg);
        if (!specified.IsSpecified(SvgPropertyId.Overflow))
        {
            style.Overflow = initial.Overflow;
        }

        return style;
    }

    /// <summary>
    /// Computes the properties whose winning declaration contains <c>var()</c>, after the rest of
    /// <see cref="Compute"/> has set the other properties and <see cref="CustomProperties"/>. Called only for elements
    /// whose specified style has custom properties or pending declarations (<see cref="SvgSpecifiedStyle.Pending"/>).
    /// </summary>
    /// <remarks>
    /// This partial method has no implementation yet, so the compiler removes the call: pending declarations do not change
    /// the computed style, and each such property keeps the value of its declaration of next lower precedence. An
    /// implementation substitutes the custom properties into the text of each pending declaration
    /// (<see cref="SvgVariableStyle.TryGetPending(SvgPropertyId, out SvgPendingValue)"/>), parses the result with
    /// <see cref="SvgPropertyParser"/> into a specified style, and computes the property from it; a result that does not
    /// parse makes the property behave as <c>unset</c> (invalid at computed-value time).
    /// </remarks>
    /// <param name="specified">The cascaded specified style of the element.</param>
    /// <param name="parent">The computed style of the parent.</param>
    /// <param name="initial">The initial style.</param>
    /// <param name="units">The document values that length units refer to.</param>
    /// <param name="kind">The kind of the element.</param>
    /// <param name="style">The computed style, which the implementation changes.</param>
    static partial void SubstituteVariables(
        in SvgSpecifiedStyle specified,
        in SvgComputedStyle parent,
        in SvgComputedStyle initial,
        SvgUnitBasis units,
        SvgElementKind kind,
        ref SvgComputedStyle style);

    /// <summary>
    /// Returns the transform origin in user units for a reference box.
    /// </summary>
    /// <remarks>
    /// For SVG elements without a CSS layout box, <c>transform-box</c> is <c>view-box</c>: the reference box is the
    /// nearest SVG viewport, placed at the origin of the coordinate system that its <c>viewBox</c> establishes, with the
    /// size of the view box. So a percentage is a fraction of the viewport size, measured from 0, not from the view box
    /// origin. Chromium uses the same reference box for the <c>gradientTransform</c> of a gradient in
    /// <c>objectBoundingBox</c> units, where the resulting origin then applies in the unit square.
    /// </remarks>
    /// <param name="referenceBox">The size of the reference box.</param>
    /// <returns>The origin.</returns>
    public readonly Vector2 GetTransformOrigin(SizeF referenceBox)
        => ResolveTransformOrigin(this.TransformOriginX, this.TransformOriginY, referenceBox);

    /// <summary>
    /// Returns a transform origin in user units from its computed components and a reference box.
    /// </summary>
    /// <param name="x">The computed horizontal component, in pixels or as a percentage; unspecified is 0.</param>
    /// <param name="y">The computed vertical component, in pixels or as a percentage; unspecified is 0.</param>
    /// <param name="referenceBox">The size of the reference box.</param>
    /// <returns>The origin.</returns>
    public static Vector2 ResolveTransformOrigin(SvgLength x, SvgLength y, SizeF referenceBox)
        => new(
            x.Unit == SvgLengthUnit.Percent ? x.Value * referenceBox.Width / 100F : x.Value,
            y.Unit == SvgLengthUnit.Percent ? y.Value * referenceBox.Height / 100F : y.Value);

    /// <summary>
    /// Applies a transform origin to a transform: the result is <c>translate(origin) × transform × translate(-origin)</c>
    /// in the column-vector notation of CSS.
    /// </summary>
    /// <remarks>
    /// <see cref="Matrix3x2"/> uses row vectors, where the transform applied first comes first in the product, so the
    /// result is <c>T(-origin) * transform * T(origin)</c>.
    /// </remarks>
    /// <param name="transform">The transform.</param>
    /// <param name="origin">The origin in the coordinate system that the transform maps into.</param>
    /// <returns>The transform about the origin.</returns>
    public static Matrix3x2 ApplyTransformOrigin(Matrix3x2 transform, Vector2 origin)
        => origin == Vector2.Zero
            ? transform
            : Matrix3x2.CreateTranslation(-origin) * transform * Matrix3x2.CreateTranslation(origin);

    /// <summary>
    /// Stores the computed value of an extended property from its specified value.
    /// </summary>
    /// <param name="property">A property from <see cref="SvgPropertyId.ClipPath"/> to <see cref="SvgPropertyId.LightingColor"/> that is not a text or a geometry property.</param>
    /// <param name="extended">The extension of the specified style, which holds the value.</param>
    /// <param name="style">The style to change.</param>
    /// <param name="units">The document values that length units refer to.</param>
    private static void ComputeExtended(SvgPropertyId property, SvgExtendedStyle extended, ref SvgComputedStyle style, SvgUnitBasis units)
    {
        switch (property)
        {
            case SvgPropertyId.ClipPath:
                style.ClipPath = extended.ClipPath;
                break;
            case SvgPropertyId.ClipRule:
                style.ClipRule = extended.ClipRule;
                break;
            case SvgPropertyId.Mask:
                style.Mask = extended.Mask;
                break;
            case SvgPropertyId.MaskType:
                style.MaskType = extended.MaskType;
                break;
            case SvgPropertyId.MixBlendMode:
                style.MixBlendMode = extended.MixBlendMode;
                break;
            case SvgPropertyId.Isolation:
                style.Isolation = extended.Isolation;
                break;
            case SvgPropertyId.PaintOrder:
                style.PaintOrder = extended.PaintOrder;
                break;
            case SvgPropertyId.VectorEffect:
                style.VectorEffect = extended.VectorEffect;
                break;
            case SvgPropertyId.Overflow:
                style.Overflow = extended.Overflow;
                break;
            case SvgPropertyId.MarkerStart:
                style.MarkerStart = extended.MarkerStart;
                break;
            case SvgPropertyId.MarkerMid:
                style.MarkerMid = extended.MarkerMid;
                break;
            case SvgPropertyId.MarkerEnd:
                style.MarkerEnd = extended.MarkerEnd;
                break;
            case SvgPropertyId.ImageRendering:
                style.ImageRendering = extended.ImageRendering;
                break;
            case SvgPropertyId.ShapeRendering:
                style.ShapeRendering = extended.ShapeRendering;
                break;
            case SvgPropertyId.ColorInterpolation:
                style.ColorInterpolation = extended.ColorInterpolation;
                break;
            case SvgPropertyId.Filter:
            case SvgPropertyId.ColorInterpolationFilters:
            case SvgPropertyId.FloodColor:
            case SvgPropertyId.FloodOpacity:
            case SvgPropertyId.LightingColor:
                CopyFilterProperty(property, extended.Filters!, ref style);
                break;
        }
    }

    /// <summary>
    /// Sets one filter property from another set of values. The computed values are shared, so the style gets a changed
    /// copy.
    /// </summary>
    /// <param name="property">A property for which <see cref="SvgFilterStyle.IsFilterProperty(SvgPropertyId)"/> is true.</param>
    /// <param name="source">The values to copy from.</param>
    /// <param name="style">The style to change.</param>
    private static void CopyFilterProperty(SvgPropertyId property, SvgFilterStyle source, ref SvgComputedStyle style)
    {
        SvgFilterStyle filters = style.Filters.Clone();
        filters.CopyFrom(source, SvgPropertySet.Of(property));
        style.Filters = filters;
    }

    /// <summary>
    /// Computes the text properties that an element specifies, on a copy of the text style it starts from.
    /// </summary>
    /// <remarks>
    /// <c>bolder</c> and <c>lighter</c> follow the relative weight table of CSS Fonts Level 4 from the parent weight.
    /// Lengths with font-relative or absolute units become pixels at this element, so descendants inherit the absolute
    /// value, as CSS computes <c>letter-spacing</c>, <c>word-spacing</c>, and <c>baseline-shift</c>. Percentages stay
    /// percentages: they refer to the font size of the element that uses them.
    /// </remarks>
    /// <param name="specified">The cascaded specified style of the element.</param>
    /// <param name="bits">The mask of the specified text properties.</param>
    /// <param name="start">The text style the element starts from: the parent style with the properties that do not inherit reset.</param>
    /// <param name="parent">The computed text style of the parent.</param>
    /// <param name="initial">The initial text style.</param>
    /// <param name="fontSize">The computed font size of the element.</param>
    /// <param name="units">The document values that length units refer to.</param>
    /// <returns>The new computed text style.</returns>
    private static SvgTextStyle ComputeText(
        in SvgSpecifiedStyle specified,
        SvgPropertySet bits,
        SvgTextStyle start,
        SvgTextStyle parent,
        SvgTextStyle initial,
        float fontSize,
        SvgUnitBasis units)
    {
        SvgTextStyle text = start.Clone();
        SvgTextStyle? values = specified.Extended?.Text;
        while (!bits.IsEmpty)
        {
            SvgPropertyId property = SvgPropertySet.TakeFirst(ref bits);

            if (specified.IsInherit(property))
            {
                text.CopyProperty(property, parent);
                continue;
            }

            if (specified.IsInitial(property))
            {
                text.CopyProperty(property, initial);
                continue;
            }

            // A value bit means the parser stored the value in the text values of the extension.
            text.CopyProperty(property, values!);
            switch (property)
            {
                case SvgPropertyId.FontWeight:
                    text.FontWeight = ComputeFontWeight(values!.FontWeight, parent.FontWeight);
                    break;
                case SvgPropertyId.LetterSpacing:
                    text.LetterSpacing = ToComputedOptionalLength(values!.LetterSpacing, fontSize, units);
                    break;
                case SvgPropertyId.WordSpacing:
                    text.WordSpacing = ToComputedOptionalLength(values!.WordSpacing, fontSize, units);
                    break;
                case SvgPropertyId.Kerning:
                    text.Kerning = ToComputedOptionalLength(values!.Kerning, fontSize, units);
                    break;
                case SvgPropertyId.BaselineShift when values!.BaselineShiftKind == SvgBaselineShiftKind.Length:
                    text.BaselineShift = ToComputedLength(values.BaselineShift, fontSize, units);
                    break;
            }
        }

        return text;
    }

    /// <summary>
    /// Computes the geometry properties and <c>transform-box</c> that an element specifies, on a copy of the initial
    /// values: none of them inherits.
    /// </summary>
    /// <remarks>
    /// A length computes as for the other properties: pixels at this element, or a percentage that resolves later
    /// against the viewport; <c>auto</c> stays unspecified. <c>inherit</c> copies the parent value when it came from a
    /// declaration, and otherwise records that the value is the attribute of the parent
    /// (<see cref="SvgGeometryStyle.FromParentAttribute"/>).
    /// </remarks>
    /// <param name="specified">The cascaded specified style of the element.</param>
    /// <param name="bits">The mask of the specified geometry properties.</param>
    /// <param name="parent">The computed geometry values of the parent.</param>
    /// <param name="initial">The initial geometry values.</param>
    /// <param name="fontSize">The computed font size of the element.</param>
    /// <param name="units">The document values that length units refer to.</param>
    /// <returns>The new computed geometry values.</returns>
    private static SvgGeometryStyle ComputeGeometry(
        in SvgSpecifiedStyle specified,
        SvgPropertySet bits,
        SvgGeometryStyle parent,
        SvgGeometryStyle initial,
        float fontSize,
        SvgUnitBasis units)
    {
        SvgGeometryStyle geometry = initial.Clone();
        SvgGeometryStyle? values = specified.Extended?.Geometry;
        SvgPropertySet fromStyle = default;
        SvgPropertySet fromParentAttribute = default;
        while (!bits.IsEmpty)
        {
            SvgPropertyId property = SvgPropertySet.TakeFirst(ref bits);

            // Only the geometry properties have attributes of the same name; transform-box and transform-origin are
            // ordinary properties, whose computed value always counts. The CSS transform is folded by the reader.
            bool hasAttribute = property is >= SvgPropertyId.X and <= SvgPropertyId.D;
            SvgPropertySet bit = hasAttribute ? SvgPropertySet.Of(property) : default;
            if (property == SvgPropertyId.Transform)
            {
                continue;
            }

            if (specified.IsInherit(property))
            {
                geometry.CopyProperty(property, parent);
                bool parentHasValue = parent.FromStyle.Overlaps(bit);
                fromStyle |= parentHasValue ? bit : default;
                fromParentAttribute |= parentHasValue ? parent.FromParentAttribute & bit : bit;
                continue;
            }

            fromStyle |= bit;
            if (specified.IsInitial(property))
            {
                continue;
            }

            // A value bit means the parser stored the value in the geometry values of the extension.
            geometry.CopyProperty(property, values!);
            if (property is >= SvgPropertyId.X and < SvgPropertyId.D)
            {
                geometry.SetLength(property, ToComputedOptionalLength(values!.GetLength(property), fontSize, units));
            }
            else if (property == SvgPropertyId.TransformOrigin)
            {
                geometry.TransformOriginX = ToComputedLength(values!.TransformOriginX, fontSize, units);
                geometry.TransformOriginY = ToComputedLength(values.TransformOriginY, fontSize, units);
            }
        }

        geometry.FromStyle = fromStyle;
        geometry.FromParentAttribute = fromParentAttribute;
        return geometry;
    }

    /// <summary>
    /// Computes a specified <c>font-weight</c>. A number keeps its value; <c>bolder</c> and <c>lighter</c> use the table of
    /// CSS Fonts Level 4, section 2.2.1.
    /// </summary>
    /// <param name="specified">The specified weight, or <see cref="SvgTextStyle.Bolder"/> or <see cref="SvgTextStyle.Lighter"/>.</param>
    /// <param name="parent">The computed weight of the parent.</param>
    /// <returns>The computed weight.</returns>
    internal static float ComputeFontWeight(float specified, float parent)
    {
        if (specified == SvgTextStyle.Bolder)
        {
            return parent < 350F ? 400F : parent < 550F ? 700F : parent < 900F ? 900F : parent;
        }

        if (specified == SvgTextStyle.Lighter)
        {
            return parent < 100F ? parent : parent < 550F ? 100F : parent < 750F ? 400F : 700F;
        }

        return specified;
    }

    /// <summary>
    /// Converts a specified length that may be unspecified, for a keyword such as <c>normal</c>, to its computed form.
    /// </summary>
    /// <param name="value">The specified length.</param>
    /// <param name="fontSize">The computed font size of the element.</param>
    /// <param name="units">The document values that length units refer to.</param>
    /// <returns>The computed length; unspecified stays unspecified.</returns>
    private static SvgLength ToComputedOptionalLength(SvgLength value, float fontSize, SvgUnitBasis units)
        => value.IsSpecified ? ToComputedLength(value, fontSize, units) : value;

    /// <summary>
    /// Computes <c>font-size</c>. A percentage, <c>em</c>, <c>ex</c>, and <c>ch</c> refer to the font size of the parent.
    /// </summary>
    /// <param name="specified">The cascaded specified style of the element.</param>
    /// <param name="parent">The computed style of the parent.</param>
    /// <param name="initial">The initial style.</param>
    /// <param name="units">The document values that length units refer to.</param>
    /// <returns>The font size in pixels.</returns>
    private static float ComputeFontSize(in SvgSpecifiedStyle specified, in SvgComputedStyle parent, in SvgComputedStyle initial, SvgUnitBasis units)
    {
        if (specified.IsInherit(SvgPropertyId.FontSize))
        {
            return parent.FontSize;
        }

        if (specified.IsInitial(SvgPropertyId.FontSize))
        {
            return initial.FontSize;
        }

        SvgLength value = specified.FontSize;
        return value.Unit == SvgLengthUnit.Percent
            ? parent.FontSize * value.Value / 100F
            : SvgUnits.ToPixels(value, parent.FontSize, units);
    }

    /// <summary>
    /// Converts a specified length to its computed form: pixels, or a percentage that is kept.
    /// </summary>
    /// <param name="value">The specified length.</param>
    /// <param name="fontSize">The computed font size of the element.</param>
    /// <param name="units">The document values that length units refer to.</param>
    /// <returns>The computed length.</returns>
    private static SvgLength ToComputedLength(SvgLength value, float fontSize, SvgUnitBasis units)
        => value.Unit == SvgLengthUnit.Percent ? value : new SvgLength(SvgUnits.ToPixels(value, fontSize, units), SvgLengthUnit.Px);

    /// <summary>
    /// Copies the value of one property from another style, for the <c>inherit</c> and <c>initial</c> keywords.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="source">The style to copy from.</param>
    /// <param name="style">The style to change.</param>
    private static void CopyProperty(SvgPropertyId property, in SvgComputedStyle source, ref SvgComputedStyle style)
    {
        switch (property)
        {
            case SvgPropertyId.Color:
                style.Color = source.Color;
                break;
            case SvgPropertyId.Display:
                style.Display = source.Display;
                break;
            case SvgPropertyId.Visibility:
                style.Visibility = source.Visibility;
                break;
            case SvgPropertyId.Opacity:
                style.Opacity = source.Opacity;
                break;
            case SvgPropertyId.Fill:
                style.Fill = source.Fill;
                break;
            case SvgPropertyId.FillOpacity:
                style.FillOpacity = source.FillOpacity;
                break;
            case SvgPropertyId.FillRule:
                style.FillRule = source.FillRule;
                break;
            case SvgPropertyId.Stroke:
                style.Stroke = source.Stroke;
                break;
            case SvgPropertyId.StrokeOpacity:
                style.StrokeOpacity = source.StrokeOpacity;
                break;
            case SvgPropertyId.StrokeWidth:
                style.StrokeWidth = source.StrokeWidth;
                break;
            case SvgPropertyId.StrokeLineCap:
                style.StrokeLineCap = source.StrokeLineCap;
                break;
            case SvgPropertyId.StrokeLineJoin:
                style.StrokeLineJoin = source.StrokeLineJoin;
                break;
            case SvgPropertyId.StrokeMiterLimit:
                style.StrokeMiterLimit = source.StrokeMiterLimit;
                break;
            case SvgPropertyId.StrokeDashArray:
                style.StrokeDashArray = source.StrokeDashArray;
                break;
            case SvgPropertyId.StrokeDashOffset:
                style.StrokeDashOffset = source.StrokeDashOffset;
                break;
            case SvgPropertyId.FontSize:
                style.FontSize = source.FontSize;
                break;
            case SvgPropertyId.StopColor:
                style.StopColor = source.StopColor;
                break;
            case SvgPropertyId.StopOpacity:
                style.StopOpacity = source.StopOpacity;
                break;
            case SvgPropertyId.ClipPath:
                style.ClipPath = source.ClipPath;
                break;
            case SvgPropertyId.ClipRule:
                style.ClipRule = source.ClipRule;
                break;
            case SvgPropertyId.Mask:
                style.Mask = source.Mask;
                break;
            case SvgPropertyId.MaskType:
                style.MaskType = source.MaskType;
                break;
            case SvgPropertyId.MixBlendMode:
                style.MixBlendMode = source.MixBlendMode;
                break;
            case SvgPropertyId.Isolation:
                style.Isolation = source.Isolation;
                break;
            case SvgPropertyId.PaintOrder:
                style.PaintOrder = source.PaintOrder;
                break;
            case SvgPropertyId.VectorEffect:
                style.VectorEffect = source.VectorEffect;
                break;
            case SvgPropertyId.Overflow:
                style.Overflow = source.Overflow;
                break;
            case SvgPropertyId.MarkerStart:
                style.MarkerStart = source.MarkerStart;
                break;
            case SvgPropertyId.MarkerMid:
                style.MarkerMid = source.MarkerMid;
                break;
            case SvgPropertyId.MarkerEnd:
                style.MarkerEnd = source.MarkerEnd;
                break;
            case SvgPropertyId.ImageRendering:
                style.ImageRendering = source.ImageRendering;
                break;
            case SvgPropertyId.ShapeRendering:
                style.ShapeRendering = source.ShapeRendering;
                break;
            case SvgPropertyId.ColorInterpolation:
                style.ColorInterpolation = source.ColorInterpolation;
                break;
            case SvgPropertyId.Filter:
            case SvgPropertyId.ColorInterpolationFilters:
            case SvgPropertyId.FloodColor:
            case SvgPropertyId.FloodOpacity:
            case SvgPropertyId.LightingColor:
                CopyFilterProperty(property, source.Filters, ref style);
                break;
        }
    }
}
