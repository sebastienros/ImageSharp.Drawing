// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// <c>clipPath</c>: classification, geometry clips, and clip coverage.
/// </content>
/// <remarks>
/// <para>
/// The clip region of a <c>clipPath</c> is the union of the fill regions of its children, each with its own
/// <c>clip-rule</c> and transform, intersected with the clip of each child and with the clip of the <c>clipPath</c>
/// itself. Only shapes, and <c>use</c> elements that reference a shape directly, add geometry; fill, stroke, opacity,
/// and masks of the children do not matter, and hidden children add nothing.
/// </para>
/// <para>
/// A canvas clip combines the paths of one call with the non-zero rule and intersects successive calls, so it expresses a
/// clip region exactly only when the region is an intersection of single outlines that the non-zero rule fills as SVG
/// does: one child with the <c>nonzero</c> rule, or one basic shape (rectangle, circle, ellipse), whose simple outline
/// fills the same with either rule. Such a clip becomes <see cref="SvgRenderNodeKind.BeginClip"/> nodes. Any other clip,
/// a union of children for example, draws its children as opaque fills with their own rules into an alpha mask layer.
/// That is exact for every rule and costs a layer and a mask layer per clipped element.
/// </para>
/// <para>
/// The content coordinate system of a <c>clipPath</c> maps to the user space of the clipped element through the unit
/// square mapping of <c>clipPathUnits="objectBoundingBox"</c> first, then through the <c>transform</c> of the
/// <c>clipPath</c> about its <c>transform-origin</c>, whose reference box is the viewport.
/// </para>
/// </remarks>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// Classifies how a <c>clipPath</c> applies to an element. For <see cref="SvgClipKind.Geometry"/>, it appends the
    /// paths that the clip region intersects, in document user space, to <paramref name="geometry"/>; for any other result
    /// it leaves the list as it was.
    /// </summary>
    /// <param name="clip">The <c>clipPath</c> element.</param>
    /// <param name="target">The clipped element.</param>
    /// <param name="geometry">The list that receives the geometry.</param>
    /// <returns>The classification.</returns>
    private SvgClipKind ClassifyClip(int clip, in SvgClipTarget target, List<(IPath Path, Matrix3x2 Transform)> geometry)
    {
        this.CountInstance();
        if (!this.TryEnterReferenceChecked(clip))
        {
            return SvgClipKind.None;
        }

        int first = geometry.Count;
        SvgClipKind kind = this.ClassifyClipContent(clip, in target, geometry);
        if (kind != SvgClipKind.Geometry)
        {
            geometry.RemoveRange(first, geometry.Count - first);
        }

        this.ExitReference();
        return kind;
    }

    /// <summary>
    /// Classifies the content of a <c>clipPath</c> that is on the reference chain.
    /// </summary>
    /// <param name="clip">The <c>clipPath</c> element.</param>
    /// <param name="target">The clipped element.</param>
    /// <param name="geometry">The list that receives the geometry. The caller removes it unless the result is <see cref="SvgClipKind.Geometry"/>.</param>
    /// <returns>The classification.</returns>
    private SvgClipKind ClassifyClipContent(int clip, in SvgClipTarget target, List<(IPath Path, Matrix3x2 Transform)> geometry)
    {
        SvgComputedStyle clipStyle = this.ComputeDocumentStyle(clip);
        if (!this.TryGetClipTransform(clip, in clipStyle, in target, out Matrix3x2 content))
        {
            return SvgClipKind.Empty;
        }

        // A clip-path on the clipPath element intersects the whole region.
        SvgClipKind self = SvgClipKind.None;
        int selfClip = this.GetReference(clipStyle.ClipPath, SvgElementKind.ClipPath);
        if (selfClip >= 0)
        {
            self = this.ClassifyClip(selfClip, in target, geometry);
            if (self == SvgClipKind.Empty)
            {
                return SvgClipKind.Empty;
            }
        }

        int count = 0;
        bool simple = self != SvgClipKind.Coverage;
        ClipChild single = default;
        for (int child = this.tree.GetFirstChild(clip); child >= 0; child = this.tree.GetNextSibling(child))
        {
            if (!this.TryGetClipChild(child, in clipStyle, content, in target, out ClipChild entry))
            {
                continue;
            }

            // A clip-path on a child intersects that child. The geometry of a child clip stays in the list: with a single
            // child it is part of the intersection, and with several children the result is coverage, which drops it.
            SvgClipKind childClip = SvgClipKind.None;
            if (entry.Clip >= 0)
            {
                childClip = this.ClassifyClip(entry.Clip, entry.Target, geometry);
                if (childClip == SvgClipKind.Empty)
                {
                    continue;
                }
            }

            single = entry;
            count++;
            simple &= childClip != SvgClipKind.Coverage && entry.RuleIndependent;
        }

        // A clipPath without a child that adds geometry clips everything.
        if (count == 0)
        {
            return SvgClipKind.Empty;
        }

        if (count == 1 && simple)
        {
            geometry.Add((single.Path, single.Transform));
            return SvgClipKind.Geometry;
        }

        return SvgClipKind.Coverage;
    }

    /// <summary>
    /// Adds one <see cref="SvgRenderNodeKind.BeginClip"/> node per path of a geometry clip, and removes the paths from the
    /// list.
    /// </summary>
    /// <param name="geometry">The list of paths.</param>
    /// <param name="first">The index of the first path to add.</param>
    private void AddClipGeometry(List<(IPath Path, Matrix3x2 Transform)> geometry, int first)
    {
        for (int i = first; i < geometry.Count; i++)
        {
            (IPath path, Matrix3x2 transform) = geometry[i];
            this.nodes.Add(new SvgRenderNode
            {
                Kind = SvgRenderNodeKind.BeginClip,
                TransformIndex = this.GetTransformIndex(transform),
                Clip = [path],
                Bounds = TransformBounds(path.Bounds, transform),
            });
        }

        geometry.RemoveRange(first, geometry.Count - first);
    }

    /// <summary>
    /// Adds the nodes that draw the coverage of a <c>clipPath</c>: each child that adds geometry fills with opaque black
    /// and its own <c>clip-rule</c>, inside the clip of the child, and all children inside the clip of the
    /// <c>clipPath</c>. The caller places the nodes in a mask layer with <see cref="SvgMaskMode.Alpha"/>.
    /// </summary>
    /// <param name="clip">The <c>clipPath</c> element.</param>
    /// <param name="target">The clipped element.</param>
    private void AddClipCoverage(int clip, in SvgClipTarget target)
    {
        if (!this.TryEnterReferenceChecked(clip))
        {
            return;
        }

        SvgComputedStyle clipStyle = this.ComputeDocumentStyle(clip);
        if (this.TryGetClipTransform(clip, in clipStyle, in target, out Matrix3x2 content))
        {
            SvgClipScope self = this.BeginClipScope(this.GetReference(clipStyle.ClipPath, SvgElementKind.ClipPath), in target);
            if (self.Kind != SvgClipKind.Empty)
            {
                Brush coverage = this.paints.GetSolidBrush(Color.Black);
                for (int child = this.tree.GetFirstChild(clip); child >= 0; child = this.tree.GetNextSibling(child))
                {
                    if (!this.TryGetClipChild(child, in clipStyle, content, in target, out ClipChild entry))
                    {
                        continue;
                    }

                    SvgClipScope childScope = this.BeginClipScope(entry.Clip, entry.Target);
                    if (childScope.Kind == SvgClipKind.Empty)
                    {
                        continue;
                    }

                    this.CountInstance();

                    // Prime the cached closed form of the path, which the fill uses, while the resolve stage is single
                    // threaded.
                    _ = entry.Path.AsClosedPath();
                    this.nodes.Add(new SvgRenderNode
                    {
                        Kind = SvgRenderNodeKind.Draw,
                        Path = entry.Path,
                        Fill = coverage,
                        FillRule = entry.Rule,
                        TransformIndex = this.GetTransformIndex(entry.Transform),
                        Bounds = TransformBounds(entry.Path.Bounds, entry.Transform),
                    });

                    this.EndClipScope(in childScope);
                }

                this.EndClipScope(in self);
            }
        }

        this.ExitReference();
    }

    /// <summary>
    /// Opens a clip around clip coverage: geometry clip nodes, or a placeholder for a layer whose coverage comes from an
    /// alpha mask.
    /// </summary>
    /// <param name="clip">The <c>clipPath</c> element, or -1 for none.</param>
    /// <param name="target">The clipped element.</param>
    /// <returns>The scope to close with <see cref="EndClipScope(in SvgClipScope)"/>. Its kind is <see cref="SvgClipKind.Empty"/> when the content must not render.</returns>
    private SvgClipScope BeginClipScope(int clip, in SvgClipTarget target)
    {
        if (clip < 0)
        {
            return default;
        }

        List<(IPath Path, Matrix3x2 Transform)> geometry = this.clipGeometry ??= [];
        int first = geometry.Count;
        SvgClipKind kind = this.ClassifyClip(clip, in target, geometry);
        int value = 0;
        if (kind == SvgClipKind.Geometry)
        {
            value = geometry.Count - first;
            this.AddClipGeometry(geometry, first);
        }
        else if (kind == SvgClipKind.Coverage)
        {
            value = this.nodes.Count;
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.None });
        }

        return new SvgClipScope { Kind = kind, Clip = clip, Target = target, Value = value };
    }

    /// <summary>
    /// Closes a clip that <see cref="BeginClipScope(int, in SvgClipTarget)"/> opened.
    /// </summary>
    /// <param name="scope">The scope.</param>
    private void EndClipScope(in SvgClipScope scope)
    {
        if (scope.Kind == SvgClipKind.Geometry)
        {
            for (int i = 0; i < scope.Value; i++)
            {
                this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndClip });
            }
        }
        else if (scope.Kind == SvgClipKind.Coverage)
        {
            if (this.nodes.Count == scope.Value + 1)
            {
                this.nodes.RemoveAt(scope.Value);
                return;
            }

            RectangleF bounds = this.GetDrawBounds(scope.Value + 1);
            this.nodes[scope.Value] = new SvgRenderNode { Kind = SvgRenderNodeKind.BeginLayer, Opacity = 1F, Bounds = bounds };
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.BeginMask, MaskMode = SvgMaskMode.Alpha, Bounds = bounds });
            this.AddClipCoverage(scope.Clip, scope.Target);
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndMask });
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndLayer });
        }
    }

    /// <summary>
    /// Returns the transform from the content coordinate system of a <c>clipPath</c> to document user space.
    /// </summary>
    /// <param name="clip">The <c>clipPath</c> element.</param>
    /// <param name="clipStyle">The computed style of the <c>clipPath</c> element, which holds its <c>transform-origin</c>.</param>
    /// <param name="target">The clipped element.</param>
    /// <param name="transform">When this method returns <see langword="true"/>, contains the transform.</param>
    /// <returns><see langword="true"/> if the clip has a coordinate system; <see langword="false"/> for <c>objectBoundingBox</c> units on an empty box.</returns>
    private bool TryGetClipTransform(int clip, in SvgComputedStyle clipStyle, in SvgClipTarget target, out Matrix3x2 transform)
    {
        transform = target.Transform;
        if (this.TryGetLocalTransform(clip, in clipStyle, target.Viewport, out Matrix3x2 local))
        {
            transform = local * transform;
        }

        if (this.tree.GetClipPathUnits(clip) == SvgCoordinateUnits.ObjectBoundingBox)
        {
            if (!target.TryGetBoxTransform(out Matrix3x2 box))
            {
                return false;
            }

            transform = box * transform;
        }

        return true;
    }

    /// <summary>
    /// Returns the geometry that one child of a <c>clipPath</c> adds.
    /// </summary>
    /// <param name="child">The child element.</param>
    /// <param name="clipStyle">The computed style of the <c>clipPath</c>, which the child inherits.</param>
    /// <param name="content">The transform from the content coordinate system of the <c>clipPath</c> to document user space.</param>
    /// <param name="target">The clipped element.</param>
    /// <param name="result">When this method returns <see langword="true"/>, contains the geometry.</param>
    /// <returns><see langword="true"/> if the child adds geometry; otherwise, <see langword="false"/>.</returns>
    private bool TryGetClipChild(int child, in SvgComputedStyle clipStyle, Matrix3x2 content, in SvgClipTarget target, out ClipChild result)
    {
        result = default;
        SvgElementKind kind = this.tree[child].Kind;
        if (!IsShape(kind) && kind is not SvgElementKind.Use and not SvgElementKind.Text)
        {
            return false;
        }

        if (this.tree[child].HasConditionalAttributes && !this.PassesConditions(child))
        {
            return false;
        }

        SvgComputedStyle style = SvgComputedStyle.Compute(in this.tree.GetStyle(child), in clipStyle, in this.initialStyle, this.units, kind);
        if (style.Display == SvgDisplay.None)
        {
            return false;
        }

        SizeF viewport = target.Viewport;
        Matrix3x2 user = this.TryGetLocalTransform(child, in style, viewport, out Matrix3x2 local) ? local * content : content;
        int shape = child;
        SvgElementKind shapeKind = kind;
        SvgComputedStyle shapeStyle = style;
        Matrix3x2 shapeTransform = Matrix3x2.Identity;
        if (kind == SvgElementKind.Use)
        {
            // A use element adds geometry only when it references a shape directly; the shape inherits from the use
            // element and moves by x and y after its own transform.
            SvgUseAttributes use = this.GetUseAttributes(child, in style);
            if (use.Href is null || !this.tree.TryGetElementById(use.Href, out shape))
            {
                return false;
            }

            shapeKind = this.tree[shape].Kind;
            if ((!IsShape(shapeKind) && shapeKind != SvgElementKind.Text)
                || (this.tree[shape].HasConditionalAttributes && !this.PassesConditions(shape)))
            {
                return false;
            }

            shapeStyle = SvgComputedStyle.Compute(in this.tree.GetStyle(shape), in style, in this.initialStyle, this.units, shapeKind);
            if (shapeStyle.Display == SvgDisplay.None)
            {
                return false;
            }

            float x = SvgUnits.ToUser(use.X, viewport.Width, style.FontSize, this.units, 0F);
            float y = SvgUnits.ToUser(use.Y, viewport.Height, style.FontSize, this.units, 0F);
            shapeTransform = Matrix3x2.CreateTranslation(x, y);
            if (this.TryGetLocalTransform(shape, in shapeStyle, viewport, out Matrix3x2 shapeLocal))
            {
                shapeTransform = shapeLocal * shapeTransform;
            }
        }

        // A line has no fill region, and a hidden shape adds nothing. The glyphs of text add their outlines; visibility
        // applies per span there, because a visible tspan of hidden text still renders.
        bool isText = shapeKind == SvgElementKind.Text;
        if (shapeKind == SvgElementKind.Line || (!isText && shapeStyle.Visibility != SvgVisibility.Visible))
        {
            return false;
        }

        SvgResolveContext shapeContext = new(in shapeStyle, Matrix3x2.Identity, viewport, target.Depth + 1);
        IPath? path = isText
            ? this.GetTextClipPath(shape, in shapeStyle, viewport)
            : this.GetShape(shape, shapeKind, in shapeContext);
        if (path is null)
        {
            return false;
        }

        // The clip region is the fill region, which closes every open subpath. A canvas clip does not close an open path
        // itself, so the geometry clip takes the closed form; the coverage fill closes it too.
        path = path.AsClosedPath();

        // clip-rule does not apply to text, as fill-rule does not: overlapping glyphs add up, as in Chromium.
        bool evenOdd = shapeStyle.ClipRule == SvgFillRule.EvenOdd && !isText;
        result = new ClipChild
        {
            Path = path,
            Transform = shapeTransform * user,
            Rule = evenOdd ? IntersectionRule.EvenOdd : IntersectionRule.NonZero,

            // The outline of a basic shape is one simple contour, which every fill rule fills the same.
            RuleIndependent = !evenOdd || shapeKind is SvgElementKind.Rect or SvgElementKind.Circle or SvgElementKind.Ellipse,
            Clip = this.GetReference(style.ClipPath, SvgElementKind.ClipPath),
            Target = new SvgClipTarget(user, viewport, TransformBounds(path.Bounds, shapeTransform), target.Depth + 1),
        };

        return true;
    }

    /// <summary>
    /// Returns whether an element kind is a basic shape or a path.
    /// </summary>
    /// <param name="kind">The element kind.</param>
    /// <returns><see langword="true"/> for the shape kinds; otherwise, <see langword="false"/>.</returns>
    private static bool IsShape(SvgElementKind kind)
        => kind is SvgElementKind.Path
            or SvgElementKind.Rect
            or SvgElementKind.Circle
            or SvgElementKind.Ellipse
            or SvgElementKind.Line
            or SvgElementKind.Polyline
            or SvgElementKind.Polygon;

    /// <summary>
    /// The geometry that one child of a <c>clipPath</c> adds.
    /// </summary>
    private struct ClipChild
    {
        /// <summary>
        /// Gets or sets the shape geometry.
        /// </summary>
        public IPath Path { readonly get; set; }

        /// <summary>
        /// Gets or sets the transform from the geometry to document user space.
        /// </summary>
        public Matrix3x2 Transform { readonly get; set; }

        /// <summary>
        /// Gets or sets the <c>clip-rule</c> of the shape.
        /// </summary>
        public IntersectionRule Rule { readonly get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the non-zero rule fills the geometry as its own rule does.
        /// </summary>
        public bool RuleIndependent { readonly get; set; }

        /// <summary>
        /// Gets or sets the <c>clipPath</c> that clips the child, or -1.
        /// </summary>
        public int Clip { readonly get; set; }

        /// <summary>
        /// Gets or sets the child as the target of its own clip: its user space and its bounding box.
        /// </summary>
        public SvgClipTarget Target { readonly get; set; }
    }
}
