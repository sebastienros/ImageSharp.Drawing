// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The <c>transform</c> of the root element, and the paint options of a draw node: <c>paint-order</c>,
/// <c>vector-effect</c>, and <c>shape-rendering</c>.
/// </content>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// Returns the transform from the user space of the root element to document user space, which is the space that
    /// the viewport transform of a render maps into the destination.
    /// </summary>
    /// <remarks>
    /// SVG 2 applies the <c>transform</c> of the root element as a CSS transform of its box: in the coordinate system of
    /// the intrinsic viewport, outside the view box transform, about its <c>transform-origin</c>. The root element has a
    /// CSS layout box, so an unspecified origin is the center of the box (<c>50% 50%</c>), and percentages refer to the
    /// intrinsic size. Expressed in document user space, the result is <c>V × T × V⁻¹</c>, where <c>V</c> maps the view box
    /// to the intrinsic viewport.
    /// </remarks>
    /// <param name="style">The computed style of the root element.</param>
    /// <param name="mappedBox">The rectangle of document user space that maps into the destination.</param>
    /// <returns>The transform; the identity when the root element has no transform.</returns>
    private Matrix3x2 GetRootTransform(in SvgComputedStyle style, RectangleF mappedBox)
    {
        if (!this.tree.TryGetTransform(0, out Matrix3x2 transform))
        {
            return Matrix3x2.Identity;
        }

        SizeF size = this.intrinsicSize;
        Vector2 origin = this.tree.GetStyle(0).IsSpecified(SvgPropertyId.TransformOrigin)
            ? style.GetTransformOrigin(size)
            : new Vector2(size.Width / 2F, size.Height / 2F);

        Matrix3x2 viewBox = SvgViewBoxTransform.Create(mappedBox, this.tree.GetViewport(0).PreserveAspectRatio, new RectangleF(PointF.Empty, size));
        if (!Matrix3x2.Invert(viewBox, out Matrix3x2 inverse))
        {
            return Matrix3x2.Identity;
        }

        return viewBox * SvgComputedStyle.ApplyTransformOrigin(transform, origin) * inverse;
    }

    /// <summary>
    /// Returns the paint options of a shape that differ from the defaults.
    /// </summary>
    /// <param name="style">The computed style of the shape.</param>
    /// <param name="hasFill">Whether the shape has a fill.</param>
    /// <param name="hasStroke">Whether the shape has a stroke.</param>
    /// <returns>The options.</returns>
    private static SvgDrawFlags GetDrawFlags(in SvgComputedStyle style, bool hasFill, bool hasStroke)
    {
        SvgDrawFlags flags = SvgDrawFlags.None;

        // paint-order: only the relative order of fill and stroke matters for the draw node; markers are separate nodes.
        if (hasFill
            && hasStroke
            && style.PaintOrder is SvgPaintOrder.StrokeFillMarkers or SvgPaintOrder.StrokeMarkersFill or SvgPaintOrder.MarkersStrokeFill)
        {
            flags |= SvgDrawFlags.StrokeFirst;
        }

        if (hasStroke && style.VectorEffect == SvgVectorEffect.NonScalingStroke)
        {
            flags |= SvgDrawFlags.NonScalingStroke;
        }

        // Browsers draw crispEdges and optimizeSpeed without antialiasing.
        if (style.ShapeRendering is SvgShapeRendering.CrispEdges or SvgShapeRendering.OptimizeSpeed)
        {
            flags |= SvgDrawFlags.Aliased;
        }

        return flags;
    }
}
