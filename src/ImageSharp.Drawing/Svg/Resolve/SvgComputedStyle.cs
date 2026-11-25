// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The computed values of the phase 1 style properties of one element: the cascaded specified value, else the parent
/// value for an inherited property, else the initial value.
/// </summary>
/// <remarks>
/// <para>
/// The resolve walk computes the style top down. Content that a <c>use</c> element references takes the computed style of
/// the <c>use</c> element as its parent, so the same element can have a different computed style in each instance.
/// </para>
/// <para>
/// Lengths keep the CSS computed form: an absolute length or a font-relative length becomes pixels at the element that
/// specifies it, and a percentage stays a percentage, because it resolves against the viewport of the element that uses
/// it. <c>currentColor</c> in <see cref="Fill"/>, <see cref="Stroke"/>, and <see cref="StopColor"/> stays a keyword and
/// resolves against <see cref="Color"/> of the element that paints, as CSS Color Level 4 defines.
/// </para>
/// </remarks>
internal struct SvgComputedStyle
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
    /// Gets or sets <c>fill</c>.
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
    /// Gets or sets <c>stroke</c>.
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
            FontSize = 16F,
            StopColor = new SvgPaint(SvgPaintKind.Color, Color.Black, null, SvgPaintKind.None),
            StopOpacity = 1F,
        };

    /// <summary>
    /// Computes the style of an element from its cascaded specified style and the computed style of its parent.
    /// </summary>
    /// <param name="specified">The cascaded specified style of the element.</param>
    /// <param name="parent">The computed style of the parent, or the initial style for the root.</param>
    /// <param name="initial">The initial style.</param>
    /// <param name="dpi">The pixels per inch for absolute units.</param>
    /// <returns>The computed style.</returns>
    public static SvgComputedStyle Compute(in SvgSpecifiedStyle specified, in SvgComputedStyle parent, in SvgComputedStyle initial, float dpi)
    {
        // Start from the parent, which is the computed value of every inherited property that the element does not
        // specify. Properties that do not inherit start from their initial values instead.
        SvgComputedStyle style = parent;
        style.Display = initial.Display;
        style.Opacity = initial.Opacity;
        style.StopColor = initial.StopColor;
        style.StopOpacity = initial.StopOpacity;

        if (specified.IsEmpty)
        {
            return style;
        }

        // Font size first: em and ex lengths of the other properties refer to the computed font size of the element.
        if (specified.IsSpecified(SvgPropertyId.FontSize))
        {
            style.FontSize = ComputeFontSize(in specified, in parent, in initial, dpi);
        }

        // Visit only the specified properties. The inherit keyword takes the parent value for any property, and the
        // initial keyword takes the initial value.
        ulong bits = specified.Specified;
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
                    style.StrokeWidth = ToComputedLength(specified.StrokeWidth, style.FontSize, dpi);
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
                    style.StrokeDashOffset = ToComputedLength(specified.StrokeDashOffset, style.FontSize, dpi);
                    break;
                case SvgPropertyId.StopColor:
                    style.StopColor = specified.StopColor;
                    break;
                case SvgPropertyId.StopOpacity:
                    style.StopOpacity = specified.StopOpacity;
                    break;
            }
        }

        return style;
    }

    /// <summary>
    /// Computes <c>font-size</c>. A percentage, <c>em</c>, and <c>ex</c> refer to the font size of the parent.
    /// </summary>
    /// <param name="specified">The cascaded specified style of the element.</param>
    /// <param name="parent">The computed style of the parent.</param>
    /// <param name="initial">The initial style.</param>
    /// <param name="dpi">The pixels per inch for absolute units.</param>
    /// <returns>The font size in pixels.</returns>
    private static float ComputeFontSize(in SvgSpecifiedStyle specified, in SvgComputedStyle parent, in SvgComputedStyle initial, float dpi)
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
            : SvgUnits.ToPixels(value, parent.FontSize, dpi);
    }

    /// <summary>
    /// Converts a specified length to its computed form: pixels, or a percentage that is kept.
    /// </summary>
    /// <param name="value">The specified length.</param>
    /// <param name="fontSize">The computed font size of the element.</param>
    /// <param name="dpi">The pixels per inch for absolute units.</param>
    /// <returns>The computed length.</returns>
    private static SvgLength ToComputedLength(SvgLength value, float fontSize, float dpi)
        => value.Unit == SvgLengthUnit.Percent ? value : new SvgLength(SvgUnits.ToPixels(value, fontSize, dpi), SvgLengthUnit.Px);

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
        }
    }
}
