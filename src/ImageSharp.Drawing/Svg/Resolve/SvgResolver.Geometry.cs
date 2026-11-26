// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The geometry properties (SVG 2, chapter 9 and section 10.4) and the CSS transform with <c>transform-box</c>.
/// </content>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// Returns the attributes of a <c>rect</c> element with the geometry properties that CSS sets applied over them.
    /// </summary>
    /// <remarks>
    /// The computed values have the forms of the attributes: <c>auto</c> is unspecified, so <c>width: auto</c> gives no
    /// rectangle and <c>rx: auto</c> takes <c>ry</c>, as Chromium renders them.
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <returns>The attributes.</returns>
    private SvgRectAttributes GetRectAttributes(int index, in SvgComputedStyle style)
    {
        SvgRectAttributes rect = this.tree.GetRect(index);
        SvgGeometryStyle geometry = style.Geometry;
        if (!HasStyledGeometry(geometry))
        {
            return rect;
        }

        rect.X = this.GetGeometryLength(index, geometry, SvgPropertyId.X, rect.X);
        rect.Y = this.GetGeometryLength(index, geometry, SvgPropertyId.Y, rect.Y);
        rect.Width = this.GetGeometryLength(index, geometry, SvgPropertyId.Width, rect.Width);
        rect.Height = this.GetGeometryLength(index, geometry, SvgPropertyId.Height, rect.Height);
        rect.Rx = this.GetGeometryLength(index, geometry, SvgPropertyId.Rx, rect.Rx);
        rect.Ry = this.GetGeometryLength(index, geometry, SvgPropertyId.Ry, rect.Ry);
        return rect;
    }

    /// <summary>
    /// Returns the attributes of a <c>circle</c> or <c>ellipse</c> element with the geometry properties that CSS sets
    /// applied over them.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <returns>The attributes.</returns>
    private SvgCircleAttributes GetCircleAttributes(int index, in SvgComputedStyle style)
    {
        SvgCircleAttributes circle = this.tree.GetCircle(index);
        SvgGeometryStyle geometry = style.Geometry;
        if (!HasStyledGeometry(geometry))
        {
            return circle;
        }

        circle.Cx = this.GetGeometryLength(index, geometry, SvgPropertyId.Cx, circle.Cx);
        circle.Cy = this.GetGeometryLength(index, geometry, SvgPropertyId.Cy, circle.Cy);
        circle.R = this.GetGeometryLength(index, geometry, SvgPropertyId.R, circle.R);
        circle.Rx = this.GetGeometryLength(index, geometry, SvgPropertyId.Rx, circle.Rx);
        circle.Ry = this.GetGeometryLength(index, geometry, SvgPropertyId.Ry, circle.Ry);
        return circle;
    }

    /// <summary>
    /// Returns the attributes of a <c>use</c> element with <c>x</c> and <c>y</c> from CSS applied over them.
    /// </summary>
    /// <remarks>
    /// Chromium applies the CSS <c>x</c> and <c>y</c> to <c>use</c>, but not <c>width</c> and <c>height</c>: a CSS width
    /// does not size a referenced <c>symbol</c>. The attributes keep those.
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <returns>The attributes.</returns>
    private SvgUseAttributes GetUseAttributes(int index, in SvgComputedStyle style)
    {
        SvgUseAttributes use = this.tree.GetUse(index);
        SvgGeometryStyle geometry = style.Geometry;
        if (!HasStyledGeometry(geometry))
        {
            return use;
        }

        use.X = this.GetGeometryLength(index, geometry, SvgPropertyId.X, use.X);
        use.Y = this.GetGeometryLength(index, geometry, SvgPropertyId.Y, use.Y);
        return use;
    }

    /// <summary>
    /// Returns the attributes of an <c>image</c> element with the geometry properties that CSS sets applied over them.
    /// <c>width: auto</c> and <c>height: auto</c> take the size from the image, as a missing attribute does.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <returns>The attributes.</returns>
    private SvgImageAttributes GetImageAttributes(int index, in SvgComputedStyle style)
    {
        SvgImageAttributes image = this.tree.GetImage(index);
        SvgGeometryStyle geometry = style.Geometry;
        if (!HasStyledGeometry(geometry))
        {
            return image;
        }

        image.X = this.GetGeometryLength(index, geometry, SvgPropertyId.X, image.X);
        image.Y = this.GetGeometryLength(index, geometry, SvgPropertyId.Y, image.Y);
        image.Width = this.GetGeometryLength(index, geometry, SvgPropertyId.Width, image.Width);
        image.Height = this.GetGeometryLength(index, geometry, SvgPropertyId.Height, image.Height);
        return image;
    }

    /// <summary>
    /// Returns the path of a <c>path</c> element: the <c>d</c> property when CSS sets it (<see langword="null"/> for
    /// <c>none</c>), otherwise the attribute.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <returns>The path, or <see langword="null"/>.</returns>
    private IPath? GetPathData(int index, in SvgComputedStyle style)
    {
        // inherit from a parent without a declaration takes the d attribute of the parent, which only a path has; the
        // parent of a path element is never a path.
        SvgGeometryStyle geometry = style.Geometry;
        if (geometry.FromParentAttribute.Contains(SvgPropertyId.D))
        {
            return null;
        }

        return geometry.FromStyle.Contains(SvgPropertyId.D) ? geometry.D : this.tree.GetPath(index).Path;
    }

    /// <summary>
    /// Returns the length of a geometry property: the computed value when CSS sets it, otherwise the attribute.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="geometry">The computed geometry values of the element.</param>
    /// <param name="property">A property from <see cref="SvgPropertyId.X"/> to <see cref="SvgPropertyId.Ry"/>.</param>
    /// <param name="attribute">The attribute of the element.</param>
    /// <returns>The length.</returns>
    private SvgLength GetGeometryLength(int index, SvgGeometryStyle geometry, SvgPropertyId property, SvgLength attribute)
    {
        // inherit from a parent without a declaration takes the attribute of the parent.
        if (geometry.FromParentAttribute.Contains(property))
        {
            return this.GetParentGeometryAttribute(index, property);
        }

        return geometry.FromStyle.Contains(property) ? geometry.GetLength(property) : attribute;
    }

    /// <summary>
    /// Returns whether a declaration sets a geometry property of an element, so that its geometry differs from its
    /// attributes.
    /// </summary>
    /// <param name="geometry">The computed geometry values of the element.</param>
    /// <returns><see langword="true"/> if a geometry property comes from CSS.</returns>
    internal static bool HasStyledGeometry(SvgGeometryStyle geometry)
        => !geometry.FromStyle.IsEmpty || !geometry.FromParentAttribute.IsEmpty;

    /// <summary>
    /// Returns the attribute of the parent element that the <c>inherit</c> keyword of a geometry property takes when the
    /// parent has no declaration of the property: the computed value of the parent is its presentation attribute.
    /// </summary>
    /// <remarks>
    /// The parent of a shape, <c>use</c>, or <c>image</c> element is a container. Of the containers, only <c>svg</c> and
    /// <c>symbol</c> have attributes of these names (<c>x</c>, <c>y</c>, <c>width</c>, <c>height</c>); any other parent
    /// gives an unspecified length, which is the initial value. The parent is the parent in the document, also for content
    /// that a <c>use</c> element references.
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="property">The property.</param>
    /// <returns>The attribute, or an unspecified length.</returns>
    private SvgLength GetParentGeometryAttribute(int index, SvgPropertyId property)
    {
        int parent = this.tree[index].Parent;
        if (parent < 0 || this.tree[parent].Kind is not (SvgElementKind.Svg or SvgElementKind.Symbol))
        {
            return default;
        }

        ref readonly SvgViewportAttributes viewport = ref this.tree.GetViewport(parent);
        return property switch
        {
            SvgPropertyId.X => viewport.X,
            SvgPropertyId.Y => viewport.Y,
            SvgPropertyId.Width => viewport.Width,
            SvgPropertyId.Height => viewport.Height,
            _ => default
        };
    }

    /// <summary>
    /// Returns the lengths of a <c>stroke-dasharray</c> value, from the element tree or, for a value that <c>var()</c>
    /// substitution produced, from the style context.
    /// </summary>
    /// <param name="dashArray">The computed dash array.</param>
    /// <returns>The lengths.</returns>
    private ReadOnlySpan<SvgLength> GetDashLengths(SvgDashArray dashArray)
        => dashArray.Start < 0 ? this.units.Context!.GetDashArray(dashArray) : this.tree.GetDashArray(dashArray);

    /// <summary>
    /// Returns whether the local transform of an element needs <see cref="TryGetStyledLocalTransform"/>: CSS sets the
    /// <c>transform</c> property, <c>transform-box</c> is not the view box, or <c>transform-origin</c> is a math function
    /// that mixes units. Other elements keep the plain path through the transform table.
    /// </summary>
    /// <param name="geometry">The computed geometry values of the element.</param>
    /// <returns><see langword="true"/> if the transform depends on the computed style.</returns>
    private static bool NeedsStyledTransform(SvgGeometryStyle geometry)
        => geometry.CssTransform is not null
        || geometry.TransformBox != SvgTransformBox.ViewBox
        || geometry.TransformOriginX.Unit == SvgLengthUnit.Calc
        || geometry.TransformOriginY.Unit == SvgLengthUnit.Calc;

    /// <summary>
    /// Computes the local transform of an element from its computed style when CSS sets the <c>transform</c> property or
    /// <c>transform-box</c> selects a box other than the view box, and otherwise from the transform table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>transform-box</c> (CSS Transforms Level 1, section 5) chooses the reference box of <c>transform-origin</c> and
    /// of the percentages of the CSS transform. For an SVG element, <c>fill-box</c> and <c>content-box</c> are the object
    /// bounding box, <c>stroke-box</c> and <c>border-box</c> the stroke bounding box, and <c>view-box</c> the nearest
    /// viewport at the origin of its coordinate system. A box other than the view box moves the origin by the position of
    /// the box. It applies to the attribute transform too.
    /// </para>
    /// <para>
    /// The stroke bounding box is the object bounding box grown by half the stroke width on each side when the element has
    /// a stroke; joins and caps that reach further are not included.
    /// </para>
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <param name="referenceBox">The size of the nearest viewport.</param>
    /// <param name="transform">When this method returns <see langword="true"/>, contains the transform about the origin.</param>
    /// <returns><see langword="true"/> if the element has a transform; otherwise, <see langword="false"/>.</returns>
    private bool TryGetStyledLocalTransform(int index, in SvgComputedStyle style, SizeF referenceBox, out Matrix3x2 transform)
    {
        SvgGeometryStyle geometry = style.Geometry;
        SvgCssTransform? css = geometry.CssTransform;
        if (css is null)
        {
            if (!this.tree.TryGetTransform(index, out transform))
            {
                return false;
            }
        }
        else if (css.IsIdentity)
        {
            transform = Matrix3x2.Identity;
            return false;
        }
        else
        {
            transform = Matrix3x2.Identity;
        }

        RectangleF box = new(0F, 0F, referenceBox.Width, referenceBox.Height);
        if (geometry.TransformBox != SvgTransformBox.ViewBox)
        {
            SvgResolveContext boxContext = new(in style, Matrix3x2.Identity, referenceBox, 0);
            if (!this.TryGetObjectBoundingBox(index, in boxContext, out box))
            {
                box = default;
            }
            else if (geometry.TransformBox is SvgTransformBox.StrokeBox or SvgTransformBox.BorderBox && style.Stroke.Kind != SvgPaintKind.None)
            {
                float half = SvgUnits.ToUser(style.StrokeWidth, SvgUnits.Diagonal(referenceBox), style.FontSize, this.units, 0F) / 2F;
                box = RectangleF.Inflate(box, half, half);
            }
        }

        SvgUnitBasis units = this.units.WithFont(style.Text);
        if (css is not null)
        {
            transform = css.Resolve(box.Size, style.FontSize, units);
        }

        Vector2 origin = new(
            box.X + SvgUnits.ToUser(geometry.TransformOriginX, box.Width, style.FontSize, units, 0F),
            box.Y + SvgUnits.ToUser(geometry.TransformOriginY, box.Height, style.FontSize, units, 0F));
        transform = SvgComputedStyle.ApplyTransformOrigin(transform, origin);
        return true;
    }
}
