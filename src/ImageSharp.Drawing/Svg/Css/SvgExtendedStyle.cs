// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified values of the properties from <see cref="SvgPropertyId.ClipPath"/> on, which phase 2 added. An
/// <see cref="SvgSpecifiedStyle"/> refers to one only when it specifies at least one of these properties.
/// </summary>
/// <remarks>
/// <para>
/// Most elements specify none of these properties. Keeping their values out of <see cref="SvgSpecifiedStyle"/> keeps
/// the style table, which has one entry per styled element, at its phase 1 size. The masks of
/// <see cref="SvgSpecifiedStyle"/> still hold the specified, inherit, and initial bits of every property; a value here
/// is meaningful only when its bit says so.
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
    /// Gets or sets the <c>clip-path</c> value.
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
    /// Gets or sets the horizontal component of <c>transform-origin</c>. Keywords are stored as percentages:
    /// <c>left</c> is 0%, <c>center</c> 50%, and <c>right</c> 100%.
    /// </summary>
    public SvgLength TransformOriginX { get; set; }

    /// <summary>
    /// Gets or sets the vertical component of <c>transform-origin</c>. Keywords are stored as percentages:
    /// <c>top</c> is 0%, <c>center</c> 50%, and <c>bottom</c> 100%.
    /// </summary>
    public SvgLength TransformOriginY { get; set; }

    /// <summary>
    /// Gets or sets the CSS <c>transform</c> value. <c>none</c> is the identity.
    /// </summary>
    public Matrix3x2 Transform { get; set; }

    /// <summary>
    /// Returns a new extension that holds the values of <paramref name="target"/> with the values of the properties in
    /// <paramref name="properties"/> taken from <paramref name="source"/>. Neither input changes.
    /// </summary>
    /// <param name="target">The extension of the style of lower precedence, or <see langword="null"/>.</param>
    /// <param name="source">The extension of the style of higher precedence.</param>
    /// <param name="properties">The mask of the properties that <paramref name="source"/> specifies.</param>
    /// <returns>The merged extension.</returns>
    public static SvgExtendedStyle Merge(SvgExtendedStyle? target, SvgExtendedStyle source, ulong properties)
    {
        SvgExtendedStyle result = target is null ? new SvgExtendedStyle() : (SvgExtendedStyle)target.MemberwiseClone();
        ulong bits = properties;
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
                case SvgPropertyId.TransformOrigin:
                    result.TransformOriginX = source.TransformOriginX;
                    result.TransformOriginY = source.TransformOriginY;
                    break;
                case SvgPropertyId.Transform:
                    result.Transform = source.Transform;
                    break;
            }
        }

        return result;
    }
}
