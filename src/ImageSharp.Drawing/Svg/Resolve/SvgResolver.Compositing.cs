// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// Compositing of one element: <c>clip-path</c>, <c>mask</c>, <c>mix-blend-mode</c>, and <c>isolation</c>.
/// </content>
/// <remarks>
/// <para>
/// The nodes of an element with any of these properties take this form, where every part is optional:
/// </para>
/// <code>
/// BeginClip ...                    geometry clips, outside the layer, so they also limit its composite
///   BeginLayer (opacity, blend)    the content composites as one group
///     content
///     BeginMask (alpha)            a clip that needs coverage: its children draw into an alpha mask
///       clip coverage
///     EndMask
///     BeginMask (luminance)        the mask: its region clip and its content
///       BeginClip region  content  EndClip
///     EndMask
///   EndLayer
/// EndClip ...
/// </code>
/// <para>
/// A mask layer composites with <c>DestIn</c> into the layer of the element, so the content keeps only the coverage of the
/// mask. The mask layer has the bounds of the content layer, so the composite clears the content wherever the mask has
/// no coverage, including outside the mask region. The layer bounds are the union of the content, cut by the geometry
/// clips and the mask region.
/// </para>
/// <para>
/// A reference that names no element of the right kind, or that is a cycle, is ignored, as browsers do: the element renders
/// as if the property were <c>none</c>.
/// </para>
/// </remarks>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// The bounds of a draw node whose paint extent depends on the scale of the render: it covers every destination, so
    /// the node is never culled and a layer around it covers the whole destination.
    /// </summary>
    private static readonly RectangleF UnboundedBounds = new(-1E15F, -1E15F, 2E15F, 2E15F);

    /// <summary>
    /// The scratch list of paths and transforms that a geometry clip intersects, created when the first clip resolves.
    /// </summary>
    private List<(IPath Path, Matrix3x2 Transform)>? clipGeometry;

    /// <summary>
    /// Opens the compositing of an element before its content resolves: its geometry clips and its layer.
    /// </summary>
    /// <remarks>
    /// Elements without <c>clip-path</c>, <c>mask</c>, <c>mix-blend-mode</c>, and <c>isolation</c> return at the first
    /// test. When the element needs a layer, the layer takes the opacity of the element and the opacity of
    /// <paramref name="context"/> becomes 1, so the content does not apply it again.
    /// </remarks>
    /// <param name="element">The element index.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <param name="context">The context of the element. Its style can change; see the remarks.</param>
    /// <param name="scope">When this method returns, contains what to close in <see cref="EndComposite(in SvgCompositeScope)"/>.</param>
    /// <returns><see langword="true"/> if the element renders; <see langword="false"/> if its clip or mask leaves nothing.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryBeginComposite(int element, in SvgComputedStyle style, ref SvgResolveContext context, out SvgCompositeScope scope)
    {
        if (style.ClipPath.IsNone
            && style.Mask.IsNone
            && style.MixBlendMode == SvgBlendMode.Normal
            && style.Isolation == SvgIsolation.Auto)
        {
            scope = default;
            return true;
        }

        return this.BeginComposite(element, in style, ref context, out scope);
    }

    /// <summary>
    /// Opens the compositing of an element that has at least one compositing property. See
    /// <see cref="TryBeginComposite(int, in SvgComputedStyle, ref SvgResolveContext, out SvgCompositeScope)"/>.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <param name="context">The context of the element.</param>
    /// <param name="scope">When this method returns, contains what to close.</param>
    /// <returns><see langword="true"/> if the element renders; otherwise, <see langword="false"/>.</returns>
    private bool BeginComposite(int element, in SvgComputedStyle style, ref SvgResolveContext context, out SvgCompositeScope scope)
    {
        scope = default;
        int clip = this.GetReference(style.ClipPath, SvgElementKind.ClipPath);
        SvgClipShape? clipShape = style.ClipPath.Shape;
        int mask = this.GetReference(style.Mask, SvgElementKind.Mask);
        SvgBlendMode blend = style.MixBlendMode;
        bool isolate = style.Isolation == SvgIsolation.Isolate;
        if (clip < 0 && clipShape is null && mask < 0 && blend == SvgBlendMode.Normal && !isolate)
        {
            return true;
        }

        RectangleF? box = null;
        if (clip >= 0 || mask >= 0 || clipShape is not null)
        {
            // The user space of the root element is already the view box space, so its box is the box of its children
            // without the viewport transform that a nested svg element applies.
            bool found = element == 0
                ? this.TryGetChildrenBoundingBox(0, in context, Matrix3x2.Identity, context.Viewport, out RectangleF bounds)
                : this.TryGetObjectBoundingBox(element, in context, out bounds);
            box = found ? bounds : null;
        }

        SvgClipTarget target = new(context.Transform, context.Viewport, box, context.Depth);
        SvgClipKind clipKind = SvgClipKind.None;
        List<(IPath Path, Matrix3x2 Transform)> geometry = this.clipGeometry ??= [];
        int firstGeometry = geometry.Count;
        if (clip >= 0)
        {
            clipKind = this.ClassifyClip(clip, in target, geometry);
            if (clipKind == SvgClipKind.Empty)
            {
                return false;
            }
        }

        // A basic shape clips in the coordinates of its reference box (SvgResolver.ClipShape.cs).
        IPath? coverageShape = null;
        Matrix3x2 shapeTransform = default;
        if (clipShape is not null)
        {
            RectangleF? referenceBox = this.GetClipShapeBox(element, clipShape.Box, in context, box, out shapeTransform);
            clipKind = this.ClassifyClipShape(clipShape, referenceBox, style.FontSize, style.Text, shapeTransform, geometry, out coverageShape);
            if (clipKind == SvgClipKind.Empty)
            {
                return false;
            }
        }

        RectangleF maskRegion = default;
        if (mask >= 0 && !this.TryGetMaskRegion(mask, in target, out maskRegion))
        {
            // A mask region without area, or objectBoundingBox units on an element without a box, mask everything.
            geometry.RemoveRange(firstGeometry, geometry.Count - firstGeometry);
            return false;
        }

        int start = this.nodes.Count;
        this.AddClipGeometry(geometry, firstGeometry);
        int clipCount = this.nodes.Count - start;

        // A geometry clip alone needs no layer, and the content then applies the opacity as without a clip.
        float opacity = style.Opacity;
        int layer = -1;
        if (mask >= 0 || clipKind == SvgClipKind.Coverage || blend != SvgBlendMode.Normal || isolate)
        {
            layer = this.nodes.Count;
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.None });
            if (opacity < 1F)
            {
                SvgComputedStyle opaque = style;
                opaque.Opacity = 1F;
                context = new SvgResolveContext(in opaque, context.Transform, context.Viewport, context.Depth);
            }
        }

        scope = new SvgCompositeScope
        {
            IsActive = true,
            ClipStart = start,
            ClipCount = clipCount,
            Layer = layer,
            CoverageClip = clipKind == SvgClipKind.Coverage ? clip : -1,
            CoverageShape = coverageShape,
            CoverageShapeTransform = shapeTransform,
            Mask = mask,
            MaskRegion = maskRegion,
            Opacity = layer >= 0 ? opacity : 1F,
            BlendMode = blend,
            Target = target,
        };

        return true;
    }

    /// <summary>
    /// Closes the compositing of an element after its content resolved: it turns the placeholder into the layer, adds the
    /// clip coverage and the mask, and closes the layer and the geometry clips. An element whose content rendered
    /// nothing, or whose clips and mask region leave no area, loses all its nodes.
    /// </summary>
    /// <param name="scope">The scope that <see cref="TryBeginComposite(int, in SvgComputedStyle, ref SvgResolveContext, out SvgCompositeScope)"/> returned.</param>
    private void EndComposite(in SvgCompositeScope scope)
    {
        if (!scope.IsActive)
        {
            return;
        }

        int contentStart = scope.ClipStart + scope.ClipCount + (scope.Layer >= 0 ? 1 : 0);
        if (this.nodes.Count == contentStart || (scope.Layer >= 0 && !this.EndCompositeLayer(in scope, contentStart)))
        {
            this.nodes.RemoveRange(scope.ClipStart, this.nodes.Count - scope.ClipStart);
            return;
        }

        for (int i = 0; i < scope.ClipCount; i++)
        {
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndClip });
        }
    }

    /// <summary>
    /// Turns the placeholder of an element into its layer and adds the clip coverage, the mask, and the end of the layer.
    /// </summary>
    /// <param name="scope">The scope of the element.</param>
    /// <param name="contentStart">The index of the first content node.</param>
    /// <returns><see langword="true"/> if the layer has area; <see langword="false"/> if the element renders nothing.</returns>
    private bool EndCompositeLayer(in SvgCompositeScope scope, int contentStart)
    {
        Span<SvgRenderNode> nodes = this.nodes.AsSpan();
        RectangleF bounds = default;
        bool found = false;
        bool blends = false;
        for (int i = contentStart; i < nodes.Length; i++)
        {
            // A filter node paints its filter region, which can be larger than the geometry of the element.
            ref SvgRenderNode node = ref nodes[i];
            if (node.Kind is SvgRenderNodeKind.Draw or SvgRenderNodeKind.Filter)
            {
                bounds = found ? RectangleF.Union(bounds, node.Bounds) : node.Bounds;
                found = true;
            }
            else if (node.Kind == SvgRenderNodeKind.BeginLayer && node.BlendMode != SvgBlendMode.Normal)
            {
                blends = true;
            }
        }

        if (!found)
        {
            return false;
        }

        // Nothing outside the geometry clips and the mask region can show.
        for (int i = scope.ClipStart; i < scope.ClipStart + scope.ClipCount; i++)
        {
            bounds = RectangleF.Intersect(bounds, nodes[i].Bounds);
        }

        if (scope.Mask >= 0)
        {
            bounds = RectangleF.Intersect(bounds, TransformBounds(scope.MaskRegion, scope.Target.Transform));
        }

        if (scope.CoverageShape is IPath shape)
        {
            bounds = RectangleF.Intersect(bounds, TransformBounds(shape.Bounds, scope.CoverageShapeTransform));
        }

        if (!(bounds.Width > 0F) || !(bounds.Height > 0F))
        {
            return false;
        }

        // isolation: isolate changes the result only when content blends with what is below the element.
        if (scope.CoverageClip < 0
            && scope.CoverageShape is null
            && scope.Mask < 0
            && scope.BlendMode == SvgBlendMode.Normal
            && scope.Opacity >= 1F
            && !blends)
        {
            this.hasEmptyNodes = true;
            return true;
        }

        nodes[scope.Layer] = new SvgRenderNode
        {
            Kind = SvgRenderNodeKind.BeginLayer,
            Opacity = scope.Opacity,
            BlendMode = scope.BlendMode,
            Bounds = bounds,
        };

        if (scope.CoverageClip >= 0)
        {
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.BeginMask, MaskMode = SvgMaskMode.Alpha, Bounds = bounds });
            this.AddClipCoverage(scope.CoverageClip, scope.Target);
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndMask });
        }
        else if (scope.CoverageShape is not null)
        {
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.BeginMask, MaskMode = SvgMaskMode.Alpha, Bounds = bounds });
            this.AddClipShapeCoverage(scope.CoverageShape, scope.CoverageShapeTransform);
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndMask });
        }

        if (scope.Mask >= 0)
        {
            this.AddMask(scope.Mask, scope.Target, scope.MaskRegion, bounds);
        }

        this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndLayer });
        return true;
    }

    /// <summary>
    /// Returns the element that a <c>clip-path</c> or <c>mask</c> value names, when it can apply.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <param name="kind">The element kind that the property requires.</param>
    /// <returns>
    /// The element index, or -1 for <c>none</c>, a URL that names no element of <paramref name="kind"/>, and a reference
    /// to an element on the reference chain, which is a cycle.
    /// </returns>
    private int GetReference(SvgReference reference, SvgElementKind kind)
    {
        string? id = reference.Id;
        if (id is null
            || !this.tree.TryGetElementById(id, out int element)
            || this.tree[element].Kind != kind
            || this.IsReferenceActive(element))
        {
            return -1;
        }

        return element;
    }

    /// <summary>
    /// Pushes a referenced <c>clipPath</c> or <c>mask</c> on the reference chain, unless it is already on it.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <returns><see langword="true"/> if the element was pushed; <see langword="false"/> if following it is a cycle.</returns>
    /// <exception cref="InvalidImageContentException">The chain is longer than <see cref="SvgLimits.MaxNestingDepth"/>.</exception>
    private bool TryEnterReferenceChecked(int element)
    {
        // Clip and mask references nest without bound in a hostile document, and each level recurses.
        if (this.referenceChain.Count >= this.maxDepth)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the nesting depth limit of {this.maxDepth} through clip and mask references.");
        }

        return this.TryEnterReference(element);
    }

    /// <summary>
    /// Counts one resolved instance against <see cref="SvgLimits.MaxNodes"/>. Clip references that nest can multiply
    /// the content of a small document, as <c>use</c> references can.
    /// </summary>
    /// <exception cref="InvalidImageContentException">The count exceeds the limit.</exception>
    private void CountInstance()
    {
        if (++this.instanceCount > this.maxNodes)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the node limit of {this.maxNodes} after references are expanded.");
        }
    }

    /// <summary>
    /// Returns the union of the bounds of the draw nodes from an index to the end of the node list.
    /// </summary>
    /// <param name="start">The first node.</param>
    /// <returns>The union, or an empty rectangle when no draw node follows.</returns>
    private RectangleF GetDrawBounds(int start)
    {
        ReadOnlySpan<SvgRenderNode> nodes = this.nodes.AsSpan();
        RectangleF bounds = default;
        bool found = false;
        for (int i = start; i < nodes.Length; i++)
        {
            if (nodes[i].Kind is SvgRenderNodeKind.Draw or SvgRenderNodeKind.Filter)
            {
                bounds = found ? RectangleF.Union(bounds, nodes[i].Bounds) : nodes[i].Bounds;
                found = true;
            }
        }

        return bounds;
    }
}
