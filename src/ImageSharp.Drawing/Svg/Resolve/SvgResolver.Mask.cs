// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// <c>mask</c>: the mask region and the mask layer.
/// </content>
/// <remarks>
/// <para>
/// A mask layer holds the region clip and the content of the <c>mask</c> element. The content inherits style from the
/// ancestors of the <c>mask</c> element; the <c>opacity</c>, <c>transform</c>, clip, and mask of the <c>mask</c> element
/// itself do not apply to it, except that a <c>mask</c> on the <c>mask</c> element masks the mask result.
/// </para>
/// <para>
/// <c>mask-type</c> and <c>color-interpolation</c> come from the computed style of the <c>mask</c> element and select the
/// <see cref="SvgMaskMode"/> of the layer.
/// </para>
/// </remarks>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// The default <c>x</c> and <c>y</c> of a mask region, as a fraction of the bounding box or the viewport.
    /// </summary>
    private const float DefaultMaskOrigin = -0.1F;

    /// <summary>
    /// The default <c>width</c> and <c>height</c> of a mask region, as a fraction of the bounding box or the viewport.
    /// </summary>
    private const float DefaultMaskSize = 1.2F;

    /// <summary>
    /// Returns the mask region of a <c>mask</c> element for a masked element, in the user space of the masked element.
    /// Outside the region the element is fully masked.
    /// </summary>
    /// <remarks>
    /// With <c>maskUnits="objectBoundingBox"</c>, the default, a number or a percentage is a fraction of the bounding
    /// box. With <c>userSpaceOnUse</c>, lengths are in user space and percentages refer to the viewport. The defaults are
    /// <c>-10%</c>, <c>-10%</c>, <c>120%</c>, and <c>120%</c> in both cases.
    /// </remarks>
    /// <param name="mask">The <c>mask</c> element.</param>
    /// <param name="target">The masked element.</param>
    /// <param name="region">When this method returns <see langword="true"/>, contains the region.</param>
    /// <returns>
    /// <see langword="true"/> if the region has area; <see langword="false"/> for a zero or negative size, and for
    /// <c>objectBoundingBox</c> units on an element without a box with area. Either way the element renders nothing.
    /// </returns>
    private bool TryGetMaskRegion(int mask, in SvgClipTarget target, out RectangleF region)
    {
        ref readonly SvgMaskAttributes attributes = ref this.tree.GetMask(mask);
        float fontSize = this.ComputeDocumentStyle(mask).FontSize;
        float x;
        float y;
        float width;
        float height;
        region = default;

        if (attributes.Units == SvgCoordinateUnits.UserSpaceOnUse)
        {
            SizeF viewport = target.Viewport;
            x = SvgUnits.ToUser(attributes.X, viewport.Width, fontSize, this.units, DefaultMaskOrigin * viewport.Width);
            y = SvgUnits.ToUser(attributes.Y, viewport.Height, fontSize, this.units, DefaultMaskOrigin * viewport.Height);
            width = SvgUnits.ToUser(attributes.Width, viewport.Width, fontSize, this.units, DefaultMaskSize * viewport.Width);
            height = SvgUnits.ToUser(attributes.Height, viewport.Height, fontSize, this.units, DefaultMaskSize * viewport.Height);
        }
        else
        {
            if (target.Box is not RectangleF box || !target.TryGetBoxTransform(out _))
            {
                return false;
            }

            x = box.X + (this.ToBoxFraction(attributes.X, fontSize, DefaultMaskOrigin) * box.Width);
            y = box.Y + (this.ToBoxFraction(attributes.Y, fontSize, DefaultMaskOrigin) * box.Height);
            width = this.ToBoxFraction(attributes.Width, fontSize, DefaultMaskSize) * box.Width;
            height = this.ToBoxFraction(attributes.Height, fontSize, DefaultMaskSize) * box.Height;
        }

        if (!(width > 0F) || !(height > 0F) || !float.IsFinite(x + y + width + height))
        {
            return false;
        }

        region = new RectangleF(x, y, width, height);
        return true;
    }

    /// <summary>
    /// Converts a length in <c>objectBoundingBox</c> units to a fraction of the bounding box: a percentage is divided by
    /// 100, and a number is the fraction itself.
    /// </summary>
    /// <param name="value">The length.</param>
    /// <param name="fontSize">The font size, for the rare font-relative unit.</param>
    /// <param name="defaultValue">The fraction for an unspecified length.</param>
    /// <returns>The fraction.</returns>
    private float ToBoxFraction(SvgLength value, float fontSize, float defaultValue)
    {
        if (!value.IsSpecified)
        {
            return defaultValue;
        }

        return value.Unit == SvgLengthUnit.Percent ? value.Value / 100F : SvgUnits.ToPixels(value, fontSize, this.units);
    }

    /// <summary>
    /// Returns whether the chain of <c>mask</c> properties on <c>mask</c> elements that starts at a mask element leads
    /// back to an element on the reference chain, or loops.
    /// </summary>
    /// <param name="mask">The first <c>mask</c> element of the chain.</param>
    /// <returns><see langword="true"/> if the chain is a cycle; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="InvalidImageContentException">The chain is longer than <see cref="SvgLimits.MaxNestingDepth"/>.</exception>
    private bool IsMaskChainCyclic(int mask)
    {
        // Only a mask on a mask element follows this path, so the set is rarely created.
        HashSet<int> visited = [];
        while (true)
        {
            if (this.IsReferenceActive(mask) || !visited.Add(mask))
            {
                return true;
            }

            if (visited.Count + this.referenceChain.Count > this.maxDepth)
            {
                throw new InvalidImageContentException(
                    $"The SVG document exceeds the nesting depth limit of {this.maxDepth} through clip and mask references.");
            }

            string? id = this.ComputeDocumentStyle(mask).Mask.Id;
            if (id is null || !this.tree.TryGetElementById(id, out mask) || this.tree[mask].Kind != SvgElementKind.Mask)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Adds the mask layer of a <c>mask</c> element inside the layer of the masked element: the region clip, the content,
    /// and the mask of the <c>mask</c> element, when it has one.
    /// </summary>
    /// <param name="mask">The <c>mask</c> element. It is not on the reference chain.</param>
    /// <param name="target">The masked element.</param>
    /// <param name="region">The mask region in the user space of the masked element.</param>
    /// <param name="bounds">The bounds of the layer of the masked element, which the mask layer takes.</param>
    private void AddMask(int mask, in SvgClipTarget target, RectangleF region, RectangleF bounds)
    {
        SvgComputedStyle maskStyle = this.ComputeDocumentStyle(mask);
        SvgMaskMode mode = maskStyle.MaskType == SvgMaskType.Alpha
            ? SvgMaskMode.Alpha
            : maskStyle.ColorInterpolation == SvgColorInterpolation.LinearRgb ? SvgMaskMode.LinearLuminance : SvgMaskMode.Luminance;

        this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.BeginMask, MaskMode = mode, Bounds = bounds });

        // A cycle cannot reach this point, because the reference was checked when it was looked up. The empty mask layer
        // that remains in that case masks everything.
        if (this.TryEnterReferenceChecked(mask))
        {
            int clip = this.nodes.Count;
            this.nodes.Add(new SvgRenderNode
            {
                Kind = SvgRenderNodeKind.BeginClip,
                TransformIndex = this.GetTransformIndex(target.Transform),
                Clip = [new RectanglePolygon(region)],
                Bounds = TransformBounds(region, target.Transform),
            });

            // maskContentUnits="objectBoundingBox" maps the unit square to the bounding box of the masked element.
            Matrix3x2 content = target.Transform;
            bool hasContent = true;
            if (this.tree.GetMask(mask).ContentUnits == SvgCoordinateUnits.ObjectBoundingBox)
            {
                hasContent = target.TryGetBoxTransform(out Matrix3x2 box);
                content = box * content;
            }

            if (hasContent)
            {
                SvgResolveContext context = new(in maskStyle, content, target.Viewport, target.Depth + 1);
                this.ResolveChildren(mask, in context);
            }

            if (this.nodes.Count == clip + 1)
            {
                this.nodes.RemoveAt(clip);
            }
            else
            {
                this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndClip });
            }

            // A mask on the mask element masks the mask result, with its region measured against the same element. A
            // chain of such masks that leads back into itself is ignored from its first link, as Chromium and resvg do.
            int nested = this.GetReference(maskStyle.Mask, SvgElementKind.Mask);
            if (nested >= 0 && !this.IsMaskChainCyclic(nested))
            {
                if (this.TryGetMaskRegion(nested, in target, out RectangleF nestedRegion))
                {
                    this.AddMask(nested, in target, nestedRegion, bounds);
                }
                else
                {
                    this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.BeginMask, MaskMode = SvgMaskMode.Alpha, Bounds = bounds });
                    this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndMask });
                }
            }

            this.ExitReference();
        }

        this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndMask });
    }
}
