// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified values of the properties from <see cref="SvgPropertyId.ClipPath"/> on, the mask bits of the properties
/// from <see cref="SvgPropertyId.X"/> on, and the <c>var()</c> declarations and custom properties. An
/// <see cref="SvgSpecifiedStyle"/> refers to one only when it specifies at least one of these.
/// </summary>
/// <remarks>
/// <para>
/// Most elements specify none of these properties. Keeping their values out of <see cref="SvgSpecifiedStyle"/> keeps
/// the style table, which has one entry per styled element, at its phase 1 size. The masks of
/// <see cref="SvgSpecifiedStyle"/> hold the specified, inherit, and initial bits of the properties below 64; the bits of
/// the properties from 64 on are <see cref="SpecifiedHigh"/>, <see cref="InheritHigh"/>, and <see cref="InitialHigh"/>
/// here. A value is meaningful only when its bit says so.
/// </para>
/// <para>
/// Ownership: <see cref="SvgPropertyParser"/> changes the extension of the style it fills, while that style is a local
/// value that is not yet stored in a table. <see cref="SvgSpecifiedStyle.Apply(in SvgSpecifiedStyle)"/> never changes an
/// extension; it shares the extension of the source with a target that has none, and gives any other target a new merged
/// one. So an extension that a stored style refers to never changes, and several styles can share it.
/// </para>
/// </remarks>
internal sealed class SvgExtendedStyle
{
    /// <summary>
    /// Gets or sets the <c>clip-path</c> value: <c>none</c>, a <c>url()</c> reference, or a basic shape with a geometry
    /// box (<see cref="SvgReference.Shape"/>).
    /// </summary>
    public SvgReference ClipPath { get; set; }

    /// <summary>
    /// Gets or sets the <c>clip-rule</c> value.
    /// </summary>
    public SvgFillRule ClipRule { get; set; }

    /// <summary>
    /// Gets or sets the <c>mask</c> value.
    /// </summary>
    public SvgReference Mask { get; set; }

    /// <summary>
    /// Gets or sets the <c>mask-type</c> value.
    /// </summary>
    public SvgMaskType MaskType { get; set; }

    /// <summary>
    /// Gets or sets the <c>mix-blend-mode</c> value.
    /// </summary>
    public SvgBlendMode MixBlendMode { get; set; }

    /// <summary>
    /// Gets or sets the <c>isolation</c> value.
    /// </summary>
    public SvgIsolation Isolation { get; set; }

    /// <summary>
    /// Gets or sets the <c>paint-order</c> value.
    /// </summary>
    public SvgPaintOrder PaintOrder { get; set; }

    /// <summary>
    /// Gets or sets the <c>vector-effect</c> value.
    /// </summary>
    public SvgVectorEffect VectorEffect { get; set; }

    /// <summary>
    /// Gets or sets the <c>overflow</c> value.
    /// </summary>
    public SvgOverflow Overflow { get; set; }

    /// <summary>
    /// Gets or sets the <c>marker-start</c> value.
    /// </summary>
    public SvgReference MarkerStart { get; set; }

    /// <summary>
    /// Gets or sets the <c>marker-mid</c> value.
    /// </summary>
    public SvgReference MarkerMid { get; set; }

    /// <summary>
    /// Gets or sets the <c>marker-end</c> value.
    /// </summary>
    public SvgReference MarkerEnd { get; set; }

    /// <summary>
    /// Gets or sets the <c>image-rendering</c> value.
    /// </summary>
    public SvgImageRendering ImageRendering { get; set; }

    /// <summary>
    /// Gets or sets the <c>shape-rendering</c> value.
    /// </summary>
    public SvgShapeRendering ShapeRendering { get; set; }

    /// <summary>
    /// Gets or sets the <c>color-interpolation</c> value.
    /// </summary>
    public SvgColorInterpolation ColorInterpolation { get; set; }

    /// <summary>
    /// Gets or sets the specified values of the text properties, or <see langword="null"/> when the style specifies none of
    /// them with a value. The ownership rules of this type apply to it as well.
    /// </summary>
    public SvgTextStyle? Text { get; set; }

    /// <summary>
    /// Gets or sets the specified values of the filter properties, or <see langword="null"/> when the style specifies none
    /// of them with a value. The ownership rules of this type apply to it as well.
    /// </summary>
    public SvgFilterStyle? Filters { get; set; }

    /// <summary>
    /// Gets or sets the specified values of the geometry properties, <c>transform-box</c>, <c>transform-origin</c>, and the
    /// CSS <c>transform</c>, or <see langword="null"/> when the style specifies none of them with a value. The ownership
    /// rules of this type apply to it as well.
    /// </summary>
    public SvgGeometryStyle? Geometry { get; set; }

    /// <summary>
    /// Gets or sets the custom properties and the pending <c>var()</c> declarations of the style, or
    /// <see langword="null"/> when it has none. The ownership rules of this type apply to it as well.
    /// </summary>
    public SvgVariableStyle? Variables { get; set; }

    /// <summary>
    /// Gets or sets the bits of <see cref="SvgSpecifiedStyle.Specified"/> for the properties from 64 on: bit <c>n</c> is
    /// the property <c>64 + n</c>.
    /// </summary>
    public ulong SpecifiedHigh { get; set; }

    /// <summary>
    /// Gets or sets the bits of <see cref="SvgSpecifiedStyle.Inherit"/> for the properties from 64 on.
    /// </summary>
    public ulong InheritHigh { get; set; }

    /// <summary>
    /// Gets or sets the bits of <see cref="SvgSpecifiedStyle.Initial"/> for the properties from 64 on.
    /// </summary>
    public ulong InitialHigh { get; set; }

    /// <summary>
    /// Gets a value indicating whether the extension specifies something that the inline masks of
    /// <see cref="SvgSpecifiedStyle"/> do not show: a property from 64 on, a pending declaration, or a custom property.
    /// </summary>
    public bool HasHighContent => this.SpecifiedHigh != 0 || this.Variables is not null;

    /// <summary>
    /// Returns the text values, creating them when the extension has none. Only the property parser calls this, while it
    /// fills a style that is not yet stored in a table.
    /// </summary>
    /// <returns>The text values, which the caller may change.</returns>
    public SvgTextStyle GetOrCreateText() => this.Text ??= new SvgTextStyle();

    /// <summary>
    /// Returns the filter values, creating them when the extension has none. Only the property parser calls this, while
    /// it fills a style that is not yet stored in a table.
    /// </summary>
    /// <returns>The filter values, which the caller may change.</returns>
    public SvgFilterStyle GetOrCreateFilters() => this.Filters ??= new SvgFilterStyle();

    /// <summary>
    /// Returns the geometry values, creating them when the extension has none. Only the property parser calls this, while
    /// it fills a style that is not yet stored in a table.
    /// </summary>
    /// <returns>The geometry values, which the caller may change.</returns>
    public SvgGeometryStyle GetOrCreateGeometry() => this.Geometry ??= new SvgGeometryStyle();

    /// <summary>
    /// Returns the custom properties and pending declarations, creating them when the extension has none. Only the
    /// property parser calls this, while it fills a style that is not yet stored in a table.
    /// </summary>
    /// <returns>The values, which the caller may change.</returns>
    public SvgVariableStyle GetOrCreateVariables() => this.Variables ??= new SvgVariableStyle();

    /// <summary>
    /// Returns a copy of this extension without the pending declarations of some properties. Neither this extension nor
    /// its parts change.
    /// </summary>
    /// <param name="properties">The properties whose pending declarations a declaration without <c>var()</c> replaced.</param>
    /// <returns>The copy.</returns>
    public SvgExtendedStyle WithoutPending(SvgPropertySet properties)
    {
        SvgExtendedStyle result = (SvgExtendedStyle)this.MemberwiseClone();
        result.Variables = this.Variables?.WithoutPending(properties);
        return result;
    }

    /// <summary>
    /// Returns a new extension that holds the values of <paramref name="target"/> with the values of the properties in
    /// <paramref name="properties"/> taken from <paramref name="source"/>, and the masks, pending declarations, and custom
    /// properties of both, the source winning. Neither input changes.
    /// </summary>
    /// <param name="target">The extension of the style of lower precedence, or <see langword="null"/>.</param>
    /// <param name="source">The extension of the style of higher precedence.</param>
    /// <param name="properties">The mask of the properties that the style of <paramref name="source"/> specifies.</param>
    /// <returns>The merged extension.</returns>
    public static SvgExtendedStyle Merge(SvgExtendedStyle? target, SvgExtendedStyle source, SvgPropertySet properties)
    {
        SvgExtendedStyle result = target is null ? new SvgExtendedStyle() : (SvgExtendedStyle)target.MemberwiseClone();

        // The high keyword bits of the applied properties come from the source, as Apply does for the low bits.
        ulong appliedHigh = source.SpecifiedHigh;
        result.SpecifiedHigh |= appliedHigh;
        result.InheritHigh = (result.InheritHigh & ~appliedHigh) | source.InheritHigh;
        result.InitialHigh = (result.InitialHigh & ~appliedHigh) | source.InitialHigh;

        // The text values are one shared object, so a merge that changes any of them builds a new one.
        SvgPropertySet text = properties & SvgTextStyle.Properties;
        if (!text.IsEmpty && source.Text is not null)
        {
            result.Text = SvgTextStyle.Merge(result.Text, source.Text, text);
        }

        // The filter values, the geometry values, and the variables are shared objects too.
        SvgPropertySet filters = properties & SvgFilterStyle.Properties;
        if (!filters.IsEmpty && source.Filters is not null)
        {
            result.Filters = SvgFilterStyle.Merge(result.Filters, source.Filters, filters);
        }

        SvgPropertySet geometry = properties & SvgGeometryStyle.Properties;
        if (!geometry.IsEmpty && source.Geometry is not null)
        {
            result.Geometry = SvgGeometryStyle.Merge(result.Geometry, source.Geometry, geometry);
        }

        if (source.Variables is not null || (result.Variables is not null && result.Variables.Pending.Overlaps(properties)))
        {
            result.Variables = SvgVariableStyle.Merge(result.Variables, source.Variables, properties);
        }

        ulong bits = properties.Low
            & ~(SvgTextStyle.Properties.Low | SvgFilterStyle.Properties.Low | SvgGeometryStyle.Properties.Low)
            & SvgSpecifiedStyle.ExtendedLowProperties;
        while (bits != 0)
        {
            SvgPropertyId property = (SvgPropertyId)BitOperations.TrailingZeroCount(bits);
            bits &= bits - 1;
            switch (property)
            {
                case SvgPropertyId.ClipPath:
                    result.ClipPath = source.ClipPath;
                    break;
                case SvgPropertyId.ClipRule:
                    result.ClipRule = source.ClipRule;
                    break;
                case SvgPropertyId.Mask:
                    result.Mask = source.Mask;
                    break;
                case SvgPropertyId.MaskType:
                    result.MaskType = source.MaskType;
                    break;
                case SvgPropertyId.MixBlendMode:
                    result.MixBlendMode = source.MixBlendMode;
                    break;
                case SvgPropertyId.Isolation:
                    result.Isolation = source.Isolation;
                    break;
                case SvgPropertyId.PaintOrder:
                    result.PaintOrder = source.PaintOrder;
                    break;
                case SvgPropertyId.VectorEffect:
                    result.VectorEffect = source.VectorEffect;
                    break;
                case SvgPropertyId.Overflow:
                    result.Overflow = source.Overflow;
                    break;
                case SvgPropertyId.MarkerStart:
                    result.MarkerStart = source.MarkerStart;
                    break;
                case SvgPropertyId.MarkerMid:
                    result.MarkerMid = source.MarkerMid;
                    break;
                case SvgPropertyId.MarkerEnd:
                    result.MarkerEnd = source.MarkerEnd;
                    break;
                case SvgPropertyId.ImageRendering:
                    result.ImageRendering = source.ImageRendering;
                    break;
                case SvgPropertyId.ShapeRendering:
                    result.ShapeRendering = source.ShapeRendering;
                    break;
                case SvgPropertyId.ColorInterpolation:
                    result.ColorInterpolation = source.ColorInterpolation;
                    break;
            }
        }

        return result;
    }
}
