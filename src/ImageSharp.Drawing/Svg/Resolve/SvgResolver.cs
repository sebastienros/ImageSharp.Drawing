// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The resolve stage: turns an <see cref="SvgElementTree"/> into the flat render model of an <see cref="SvgDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// One walk goes top down from the root. It computes the style of each element from its cascaded specified style and
/// the computed style of its parent, composes transforms into document user space, converts lengths against the nearest
/// viewport, builds the geometry of each shape once, resolves paint to shared brushes and pens, and expands each
/// <c>use</c> reference into its own nodes. After the walk no string parsing and no reference lookup remains.
/// </para>
/// <para>
/// Group opacity takes the cheapest correct form (see <see cref="EndGroup(int, float)"/>): a group whose content is one
/// path with only a fill or only a stroke multiplies the paint alpha, and other groups become a layer bounded to the
/// conservative bounding box of their content.
/// </para>
/// <para>
/// The walk recurses on the element structure. The reader bounds the document depth, and the walk counts the depth
/// that <c>use</c> references add against the same limit, so the recursion depth is bounded. The walk also counts every
/// element instance against <see cref="SvgLimits.MaxNodes"/>, because nested references can multiply the content of a
/// small document.
/// </para>
/// <para>
/// Helpers that the features of phase 2 share, each with its contract on the member:
/// <see cref="ResolveContent(int, in SvgResolveContext)"/> resolves the content of a referenced element with cycle
/// detection; <see cref="TryGetObjectBoundingBox(int, in SvgResolveContext, out RectangleF)"/> computes the object
/// bounding box; <see cref="TryGetLocalTransform(int, in SvgComputedStyle, SizeF, out Matrix3x2)"/> applies
/// <c>transform-origin</c>; <see cref="ComputeDocumentStyle(int)"/> computes a style through the document ancestors; and
/// <see cref="TakeNodes(int, out int)"/> takes resolved nodes out of the main paint order.
/// </para>
/// </remarks>
internal sealed partial class SvgResolver : IDisposable
{
    /// <summary>
    /// The control point distance of a cubic Bézier quarter circle, as a fraction of the radius.
    /// </summary>
    private const float Kappa = 0.5522848F;

    private readonly SvgElementTree tree;
    private readonly SvgUnitBasis units;
    private readonly int maxNodes;
    private readonly int maxDepth;
    private readonly SvgComputedStyle initialStyle;
    private readonly SvgComputedStyle rootStyle;
    private readonly SizeF intrinsicSize;
    private readonly RectangleF? rootViewBox;
    private readonly bool viewBoxDisablesRendering;
    private readonly SvgPaintCache paints;

    // The node list and the transform table live for one load and grow to sizes that are not known in advance (a
    // nested SVG image or a text run adds thousands of entries), so their storage is pooled and returns to the pool
    // when Resolve ends (see SvgPooledList<T>).
    private readonly SvgPooledList<SvgRenderNode> nodes = new();
    private readonly SvgTransformTable transforms = new();
    private readonly List<int> referenceChain = [];
    private readonly IPath?[] shapes;
    private readonly Vector3[] shapeContexts;
    private Matrix3x2 lastTransform;
    private int lastTransformIndex = -1;
    private int instanceCount;
    private bool hasEmptyNodes;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgResolver"/> class. It computes the style of the root element and
    /// the intrinsic size of the document, which the units <c>rem</c>, <c>vw</c>, and <c>vh</c> of every other element
    /// refer to.
    /// </summary>
    /// <param name="tree">The element tree. The root element is an <c>svg</c> element.</param>
    /// <param name="options">The load options.</param>
    internal SvgResolver(SvgElementTree tree, SvgLoadOptions options)
    {
        this.tree = tree;
        this.loadOptions = options;
        this.maxNodes = options.Limits.MaxNodes;
        this.maxDepth = options.Limits.MaxNestingDepth;
        this.MaxImagePixels = options.Limits.MaxImagePixels;
        this.MaxPatternTilePixels = options.Limits.MaxPatternTilePixels;
        this.Languages = options.Languages;
        this.ResourceBaseDirectory = options.ResourceBaseDirectory;
        this.initialStyle = SvgComputedStyle.CreateInitial(options.CurrentColor);

        // rem in the font size of the root refers to the initial font size; the other root lengths refer to the root
        // font size. Viewport units refer to the intrinsic size, which the root size defines, so the root itself uses
        // the default size.
        SvgStyleContext styleContext = new(tree.CalcTable, options);
        this.rootStyle = SvgComputedStyle.ComputeRoot(in tree.GetStyle(0), in this.initialStyle, SvgUnitBasis.ForRoot(options.Dpi, styleContext));

        SvgUnitBasis rootUnits = new(options.Dpi, this.rootStyle.FontSize, SvgUnitBasis.DefaultViewportSize, styleContext);
        this.intrinsicSize = this.ComputeIntrinsicSize(rootUnits, out this.rootViewBox, out this.viewBoxDisablesRendering);
        this.units = new SvgUnitBasis(options.Dpi, this.rootStyle.FontSize, this.intrinsicSize, styleContext);
        this.paints = new SvgPaintCache(tree, this.units, in this.initialStyle);
        this.shapes = new IPath?[tree.Count];
        this.shapeContexts = new Vector3[tree.Count];
    }

    /// <summary>
    /// Gets the language tags that <c>systemLanguage</c> tests match, most preferred first, from
    /// <see cref="SvgLoadOptions.Languages"/>.
    /// </summary>
    internal IReadOnlyList<string> Languages { get; }

    /// <summary>
    /// Gets the directory that file references of <c>image</c> elements resolve against, or <see langword="null"/> when
    /// only <c>data:</c> URIs may load. From <see cref="SvgLoadOptions.ResourceBaseDirectory"/>.
    /// </summary>
    internal string? ResourceBaseDirectory { get; }

    /// <summary>
    /// Gets the largest total number of decoded pixels of all <c>image</c> elements, from
    /// <see cref="SvgLimits.MaxImagePixels"/>.
    /// </summary>
    internal long MaxImagePixels { get; }

    /// <summary>
    /// Gets the largest pixel count of one rendered pattern tile, from <see cref="SvgLimits.MaxPatternTilePixels"/>.
    /// Tiles render at emit time, so a document that uses patterns must carry this value.
    /// </summary>
    internal int MaxPatternTilePixels { get; }

    /// <summary>
    /// Gets the document values that length units refer to.
    /// </summary>
    internal SvgUnitBasis Units => this.units;

    /// <summary>
    /// Gets the number of render nodes resolved so far. Pass it to <see cref="TakeNodes(int, out int)"/> to take the nodes
    /// that a later call adds.
    /// </summary>
    internal int NodeCount => this.nodes.Count;

    /// <summary>
    /// Resolves an element tree into a document.
    /// </summary>
    /// <param name="tree">The element tree. The root element is an <c>svg</c> element.</param>
    /// <param name="options">The load options.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidImageContentException">
    /// The expansion of <c>use</c> references exceeds <see cref="SvgLimits.MaxNodes"/> or <see cref="SvgLimits.MaxNestingDepth"/>.
    /// </exception>
    public static SvgDocument Resolve(SvgElementTree tree, SvgLoadOptions options)
    {
        // The document holds copies of the nodes and the transforms, so the pooled storage goes back when the resolve
        // ends, also when a limit stops the load.
        using SvgResolver resolver = new(tree, options);
        return resolver.ResolveDocument();
    }

    /// <summary>
    /// Returns the pooled storage of the node list and the transform table. The resolver is not used afterwards.
    /// </summary>
    public void Dispose()
    {
        this.nodes.Dispose();
        this.transforms.Dispose();
    }

    /// <summary>
    /// Resolves the root element and its content, then builds the document.
    /// </summary>
    /// <returns>The document.</returns>
    private SvgDocument ResolveDocument()
    {
        SvgComputedStyle style = this.rootStyle;
        ref readonly SvgViewportAttributes root = ref this.tree.GetViewport(0);
        float width = this.intrinsicSize.Width;
        float height = this.intrinsicSize.Height;
        RectangleF? viewBox = this.rootViewBox;

        // Without a view box, the intrinsic size is the coordinate system that maps into the destination.
        RectangleF mappedBox = viewBox ?? new RectangleF(0F, 0F, width, height);
        bool renders = !this.viewBoxDisablesRendering
            && width > 0F
            && height > 0F
            && float.IsFinite(width)
            && float.IsFinite(height)
            && style.Display != SvgDisplay.None
            && style.Opacity > 0F
            && this.PassesConditions(0);

        if (renders)
        {
            this.instanceCount = 1;
            SvgResolveContext context = new(in style, this.GetRootTransform(in style, mappedBox), mappedBox.Size, 1);
            if (this.TryBeginComposite(0, in style, ref context, out SvgCompositeScope composite))
            {
                SvgFilterScope filter = default;
                if (style.Filter is null || this.TryBeginFilter(0, ref context, out filter))
                {
                    float opacity = context.Style.Opacity;
                    int group = this.BeginGroup(opacity);
                    this.ResolveChildren(0, in context);
                    this.EndGroup(group, opacity);
                    this.EndFilter(in filter);
                }

                this.EndComposite(in composite);
            }
        }

        SvgRenderNode[] nodes = this.TakeNodes(0, out int maxScopeDepth);
        return new SvgDocument(
            nodes,
            this.transforms.ToArray(),
            new SizeF(width, height),
            viewBox,
            mappedBox,
            root.PreserveAspectRatio,
            maxScopeDepth,
            this.loadOptions.Limits.MaxLayerPixels,
            this.TakeFilters(nodes));
    }

    /// <summary>
    /// Computes the intrinsic size of the document from the root element.
    /// </summary>
    /// <remarks>
    /// The intrinsic size comes from absolute width and height. A missing or percentage side follows the aspect ratio of
    /// the view box, and without a view box the browser fallback of 300 x 150 applies. A view box with a zero side is kept
    /// by the parser because SVG 2 makes it disable rendering; it gives no size.
    /// </remarks>
    /// <param name="rootUnits">The unit basis of the root element.</param>
    /// <param name="viewBox">When this method returns, contains the root view box, or <see langword="null"/> when it is missing or disables rendering.</param>
    /// <param name="disablesRendering">When this method returns, contains whether the root view box has a zero side.</param>
    /// <returns>The intrinsic size.</returns>
    private SizeF ComputeIntrinsicSize(SvgUnitBasis rootUnits, out RectangleF? viewBox, out bool disablesRendering)
    {
        ref readonly SvgViewportAttributes root = ref this.tree.GetViewport(0);
        bool hasWidth = root.Width.IsSpecified && root.Width.Unit != SvgLengthUnit.Percent;
        bool hasHeight = root.Height.IsSpecified && root.Height.Unit != SvgLengthUnit.Percent;
        float width = hasWidth ? SvgUnits.ToPixels(root.Width, this.rootStyle.FontSize, rootUnits) : 0F;
        float height = hasHeight ? SvgUnits.ToPixels(root.Height, this.rootStyle.FontSize, rootUnits) : 0F;
        disablesRendering = root.HasViewBox && (root.ViewBox.Width <= 0F || root.ViewBox.Height <= 0F);
        viewBox = root.HasViewBox && !disablesRendering ? root.ViewBox : null;

        if (viewBox is RectangleF box)
        {
            if (!hasWidth && !hasHeight)
            {
                width = box.Width;
                height = box.Height;
            }
            else if (!hasWidth)
            {
                width = height * box.Width / box.Height;
            }
            else if (!hasHeight)
            {
                height = width * box.Height / box.Width;
            }
        }
        else
        {
            width = hasWidth ? width : 300F;
            height = hasHeight ? height : 150F;
        }

        return new SizeF(width, height);
    }

    /// <summary>
    /// Resolves the children of an element in document order.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element, which is the parent context of the children.</param>
    private void ResolveChildren(int index, in SvgResolveContext context)
    {
        for (int child = this.tree.GetFirstChild(index); child >= 0; child = this.tree.GetNextSibling(child))
        {
            this.ResolveElement(child, in context, default, default, false);
        }
    }

    /// <summary>
    /// Resolves one element instance and its content.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="parent">The context of the parent, or of the referencing <c>use</c> element.</param>
    /// <param name="useWidth">The <c>width</c> of the referencing <c>use</c> element, for <c>svg</c> and <c>symbol</c>.</param>
    /// <param name="useHeight">The <c>height</c> of the referencing <c>use</c> element, for <c>svg</c> and <c>symbol</c>.</param>
    /// <param name="viaUse">Whether a <c>use</c> element references this element.</param>
    private void ResolveElement(int index, in SvgResolveContext parent, SvgLength useWidth, SvgLength useHeight, bool viaUse)
    {
        SvgElementKind kind = this.tree[index].Kind;

        // Only the phase 1 graphics and container elements render. Definitions, paint servers, style sheets, unknown
        // elements, and later-phase elements render nothing, and neither do their children. A symbol renders only
        // through use.
        switch (kind)
        {
            case SvgElementKind.G:
            case SvgElementKind.A:
            case SvgElementKind.Switch:
            case SvgElementKind.Svg:
            case SvgElementKind.Use:
            case SvgElementKind.Path:
            case SvgElementKind.Rect:
            case SvgElementKind.Circle:
            case SvgElementKind.Ellipse:
            case SvgElementKind.Line:
            case SvgElementKind.Polyline:
            case SvgElementKind.Polygon:
            case SvgElementKind.Image:
            case SvgElementKind.Text:
                break;
            case SvgElementKind.Symbol when viaUse:
                break;
            default:
                return;
        }

        // Conditional processing attributes that fail remove the element, inside or outside switch.
        if (this.tree[index].HasConditionalAttributes && !this.PassesConditions(index))
        {
            return;
        }

        if (++this.instanceCount > this.maxNodes)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the node limit of {this.maxNodes} after use references are expanded.");
        }

        int depth = parent.Depth + 1;
        if (depth > this.maxDepth)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the nesting depth limit of {this.maxDepth} through use references.");
        }

        SvgComputedStyle parentStyle = parent.Style;
        SvgComputedStyle style = SvgComputedStyle.Compute(in this.tree.GetStyle(index), in parentStyle, in this.initialStyle, this.units, kind);

        // display: none removes the subtree, and opacity 0 paints nothing.
        if (style.Display == SvgDisplay.None || style.Opacity <= 0F)
        {
            return;
        }

        // The transform maps the user space of the element into the user space of its parent. With row vectors the
        // element transform comes first.
        Matrix3x2 transform = this.TryGetLocalTransform(index, in style, parent.Viewport, out Matrix3x2 local) ? local * parent.Transform : parent.Transform;
        SvgResolveContext context = new(in style, transform, parent.Viewport, depth);

        // Clipping, masking, blending, and isolation wrap the content of the element. A composite that takes over the
        // opacity of the element sets the opacity of the context to 1.
        if (!this.TryBeginComposite(index, in style, ref context, out SvgCompositeScope composite))
        {
            return;
        }

        // The filter applies before the clip, the mask, and the opacity, so it sits inside the composite and its content
        // resolves in the user space of the element.
        SvgFilterScope filter = default;
        if (style.Filter is not null && !this.TryBeginFilter(index, ref context, out filter))
        {
            this.EndComposite(in composite);
            return;
        }

        switch (kind)
        {
            case SvgElementKind.G:
            case SvgElementKind.A:
            {
                // The a element renders as a group.
                float opacity = context.Style.Opacity;
                int group = this.BeginGroup(opacity);
                this.ResolveChildren(index, in context);
                this.EndGroup(group, opacity);
                break;
            }

            case SvgElementKind.Switch:
                this.ResolveSwitch(index, in context);
                break;

            case SvgElementKind.Svg:
            case SvgElementKind.Symbol:
                this.ResolveViewport(index, in context, useWidth, useHeight);
                break;

            case SvgElementKind.Use:
                this.ResolveUse(index, in context);
                break;

            case SvgElementKind.Image:
                this.ResolveImage(index, in context);
                break;

            case SvgElementKind.Text:
                this.ResolveText(index, in context);
                break;

            default:
                this.ResolveShape(index, kind, in context);
                break;
        }

        this.EndFilter(in filter);
        this.EndComposite(in composite);
    }

    /// <summary>
    /// Resolves a nested <c>svg</c> element, or a <c>symbol</c> that a <c>use</c> element references: a new viewport
    /// with an optional view box transform and a clip to the viewport rectangle.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    /// <param name="useWidth">The <c>width</c> of the referencing <c>use</c> element, which wins when specified.</param>
    /// <param name="useHeight">The <c>height</c> of the referencing <c>use</c> element, which wins when specified.</param>
    private void ResolveViewport(int index, in SvgResolveContext context, SvgLength useWidth, SvgLength useHeight)
    {
        if (!this.TryGetViewportTransform(
            index,
            in context,
            useWidth,
            useHeight,
            out Matrix3x2 viewportTransform,
            out SizeF contentViewport,
            out RectangleF viewportRectangle))
        {
            return;
        }

        Matrix3x2 content = viewportTransform * context.Transform;
        float opacity = context.Style.Opacity;
        int group = this.BeginGroup(opacity);

        // The viewport clips its content unless overflow is visible. The clip rectangle is in the user space of the
        // element, before the view box. overflow: auto does not clip, as in browsers.
        int clip = this.nodes.Count;
        bool clips = context.Style.Overflow is SvgOverflow.Hidden or SvgOverflow.Scroll;
        if (clips)
        {
            this.nodes.Add(new SvgRenderNode
            {
                Kind = SvgRenderNodeKind.BeginClip,
                TransformIndex = this.GetTransformIndex(context.Transform),
                Clip = [new RectanglePolygon(viewportRectangle)],
                Bounds = TransformBounds(viewportRectangle, context.Transform),
            });
        }

        SvgResolveContext contentContext = new(context.Style, content, contentViewport, context.Depth);
        this.ResolveChildren(index, in contentContext);

        if (clips && this.nodes.Count == clip + 1)
        {
            // Nothing rendered inside the viewport, so the clip is not needed.
            this.nodes.RemoveAt(clip);
        }
        else if (clips)
        {
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndClip });
        }

        this.EndGroup(group, opacity);
    }

    /// <summary>
    /// Computes the viewport of a nested <c>svg</c> element, or of a <c>symbol</c> that a <c>use</c> element references.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    /// <param name="useWidth">The <c>width</c> of the referencing <c>use</c> element, which wins when specified.</param>
    /// <param name="useHeight">The <c>height</c> of the referencing <c>use</c> element, which wins when specified.</param>
    /// <param name="content">
    /// When this method returns <see langword="true"/>, contains the transform from the content coordinate system to the
    /// user space of the element: the view box transform, or the translation to the viewport origin.
    /// </param>
    /// <param name="contentViewport">When this method returns <see langword="true"/>, contains the viewport size that percentages in the content refer to.</param>
    /// <param name="viewportRectangle">When this method returns <see langword="true"/>, contains the viewport rectangle in the user space of the element.</param>
    /// <returns><see langword="true"/> if the viewport renders; <see langword="false"/> for a zero size or a view box with a zero side.</returns>
    private bool TryGetViewportTransform(
        int index,
        in SvgResolveContext context,
        SvgLength useWidth,
        SvgLength useHeight,
        out Matrix3x2 content,
        out SizeF contentViewport,
        out RectangleF viewportRectangle)
    {
        ref readonly SvgViewportAttributes attributes = ref this.tree.GetViewport(index);
        SizeF viewport = context.Viewport;
        float fontSize = context.Style.FontSize;

        // Width and height default to auto, which is 100% of the parent viewport.
        SvgLength widthLength = useWidth.IsSpecified ? useWidth : attributes.Width;
        SvgLength heightLength = useHeight.IsSpecified ? useHeight : attributes.Height;
        float x = SvgUnits.ToUser(attributes.X, viewport.Width, fontSize, this.units, 0F);
        float y = SvgUnits.ToUser(attributes.Y, viewport.Height, fontSize, this.units, 0F);
        float width = SvgUnits.ToUser(widthLength, viewport.Width, fontSize, this.units, viewport.Width);
        float height = SvgUnits.ToUser(heightLength, viewport.Height, fontSize, this.units, viewport.Height);
        content = Matrix3x2.Identity;
        contentViewport = default;
        viewportRectangle = default;

        if (width <= 0F || height <= 0F)
        {
            return false;
        }

        viewportRectangle = new RectangleF(x, y, width, height);
        if (attributes.HasViewBox)
        {
            // A view box with a zero side disables rendering of the element.
            RectangleF viewBox = attributes.ViewBox;
            if (viewBox.Width <= 0F || viewBox.Height <= 0F)
            {
                return false;
            }

            content = SvgViewBoxTransform.Create(viewBox, attributes.PreserveAspectRatio, viewportRectangle);
            contentViewport = viewBox.Size;
        }
        else
        {
            content = Matrix3x2.CreateTranslation(x, y);
            contentViewport = viewportRectangle.Size;
        }

        return true;
    }

    /// <summary>
    /// Resolves a <c>use</c> element: an instance of the referenced element that inherits style from the <c>use</c>
    /// element, translated by <c>x</c> and <c>y</c>.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the <c>use</c> element.</param>
    private void ResolveUse(int index, in SvgResolveContext context)
    {
        SvgUseAttributes use = this.GetUseAttributes(index, context.Style);
        if (use.Href is null || !this.tree.TryGetElementById(use.Href, out int target))
        {
            return;
        }

        // A reference to the element itself or to an ancestor would contain itself, and a reference back into the chain
        // of references being expanded is a cycle. Both render nothing.
        if ((target <= index && index < this.tree[target].SubtreeEnd) || !this.TryEnterReference(index))
        {
            return;
        }

        SizeF viewport = context.Viewport;
        SvgUnitBasis useUnits = this.units.WithFont(context.Style.Text);
        float x = SvgUnits.ToUser(use.X, viewport.Width, context.Style.FontSize, useUnits, 0F);
        float y = SvgUnits.ToUser(use.Y, viewport.Height, context.Style.FontSize, useUnits, 0F);

        // x and y add a translation after the transform of the use element.
        Matrix3x2 transform = Matrix3x2.CreateTranslation(x, y) * context.Transform;
        SvgResolveContext instance = new(context.Style, transform, viewport, context.Depth);

        float opacity = context.Style.Opacity;
        int group = this.BeginGroup(opacity);
        this.EnterUseContext(index, in context);
        this.ResolveElement(target, in instance, use.Width, use.Height, true);
        this.ExitContextElement();
        this.ExitReference();
        this.EndGroup(group, opacity);
    }

    /// <summary>
    /// Resolves the children of a referenced element into render nodes, appended to the node list in paint order. This
    /// is the entry point for content that another element references: the content of <c>clipPath</c>, <c>mask</c>,
    /// <c>pattern</c>, and <c>marker</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contract for the caller:
    /// </para>
    /// <list type="bullet">
    /// <item><description><paramref name="element"/> is the referenced container. It does not render itself and its own
    /// <c>opacity</c>, <c>transform</c>, clip, and mask are not applied; each child renders through
    /// <see cref="ResolveElement(int, in SvgResolveContext, SvgLength, SvgLength, bool)"/> as a child of a group would,
    /// with its own transform, style, and group opacity.</description></item>
    /// <item><description><paramref name="context"/> carries the style that the children inherit, the transform from the
    /// content coordinate system to document user space (for example the marker transform, or the object bounding box
    /// mapping), the viewport size that percentages in the content refer to, and the current depth. For
    /// <c>clipPath</c>, <c>mask</c>, <c>pattern</c>, and <c>marker</c>, take the style from
    /// <see cref="ComputeDocumentStyle(int)"/>: their content inherits from their own ancestors, not from the referencing
    /// element.</description></item>
    /// <item><description>Cycle detection: the method pushes <paramref name="element"/> on the reference chain while it
    /// resolves the children and returns <see langword="false"/>, adding nothing, when the element is already on the
    /// chain. Code that follows a reference without resolving content, such as a paint server lookup, checks
    /// <see cref="IsReferenceActive(int)"/> first.</description></item>
    /// <item><description>The nodes go to the shared node list. A caller that needs them elsewhere, such as a pattern tile
    /// or a marker instance list, takes them out with <see cref="TakeNodes(int, out int)"/>, passing the
    /// <see cref="NodeCount"/> from before the call.</description></item>
    /// </list>
    /// <para>
    /// The same element can be resolved many times, once per reference; every call counts against
    /// <see cref="SvgLimits.MaxNodes"/> and <see cref="SvgLimits.MaxNestingDepth"/> as <c>use</c> expansion does.
    /// </para>
    /// </remarks>
    /// <param name="element">The index of the referenced element whose children render.</param>
    /// <param name="context">The context that the children inherit.</param>
    /// <returns><see langword="true"/> if the content was resolved; <see langword="false"/> if the reference is a cycle.</returns>
    internal bool ResolveContent(int element, in SvgResolveContext context)
    {
        if (!this.TryEnterReference(element))
        {
            return false;
        }

        this.ResolveChildren(element, in context);
        this.ExitReference();
        return true;
    }

    /// <summary>
    /// Returns whether an element is on the reference chain: a <c>use</c> element being expanded, or a referenced element
    /// whose content <see cref="ResolveContent(int, in SvgResolveContext)"/> is resolving. Following a reference to such an
    /// element is a cycle, which SVG makes an error.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <returns><see langword="true"/> if the element is on the chain; otherwise, <see langword="false"/>.</returns>
    internal bool IsReferenceActive(int element) => this.referenceChain.Contains(element);

    /// <summary>
    /// Pushes an element on the reference chain, unless it is already on it.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <returns><see langword="true"/> if the element was pushed; <see langword="false"/> if following it is a cycle.</returns>
    internal bool TryEnterReference(int element)
    {
        if (this.referenceChain.Contains(element))
        {
            return false;
        }

        this.referenceChain.Add(element);
        return true;
    }

    /// <summary>
    /// Pops the element that the last successful <see cref="TryEnterReference(int)"/> pushed.
    /// </summary>
    internal void ExitReference() => this.referenceChain.RemoveAt(this.referenceChain.Count - 1);

    /// <summary>
    /// Computes the style of an element through its ancestors in the document, starting from the initial style. This is
    /// the style that the content of a <c>clipPath</c>, <c>mask</c>, <c>pattern</c>, or <c>marker</c> inherits, because
    /// such content inherits from its place in the document, not from the element that references it.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <returns>The computed style of the element.</returns>
    internal SvgComputedStyle ComputeDocumentStyle(int element) => this.paints.ComputeDocumentStyle(element);

    /// <summary>
    /// Returns the local transform of an element: its <c>transform</c> attribute or CSS <c>transform</c> property, about
    /// its <c>transform-origin</c>.
    /// </summary>
    /// <remarks>
    /// The transform maps the user space of the element into the user space of its parent. The origin resolves against
    /// <paramref name="referenceBox"/>: for SVG elements the nearest viewport (<c>transform-box: view-box</c>), which is
    /// <see cref="SvgResolveContext.Viewport"/> of the parent. <c>clipPath</c>, <c>pattern</c> (with
    /// <c>patternTransform</c>), and paint servers in bounding box units pass their own reference box to
    /// <see cref="SvgComputedStyle.GetTransformOrigin(SizeF, SvgUnitBasis)"/> and
    /// <see cref="SvgComputedStyle.ApplyTransformOrigin(Matrix3x2, Vector2)"/> instead.
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="style">The computed style of the element, which holds its <c>transform-origin</c>.</param>
    /// <param name="referenceBox">The size of the reference box for percentages of the origin.</param>
    /// <param name="transform">When this method returns <see langword="true"/>, contains the transform.</param>
    /// <returns><see langword="true"/> if the element has a transform; otherwise, <see langword="false"/>.</returns>
    internal bool TryGetLocalTransform(int index, in SvgComputedStyle style, SizeF referenceBox, out Matrix3x2 transform)
    {
        if (NeedsStyledTransform(style.Geometry))
        {
            return this.TryGetStyledLocalTransform(index, in style, referenceBox, out transform);
        }

        if (!this.tree.TryGetTransform(index, out transform))
        {
            return false;
        }

        transform = SvgComputedStyle.ApplyTransformOrigin(transform, style.GetTransformOrigin(referenceBox, this.units));
        return true;
    }

    /// <summary>
    /// Computes the object bounding box of an element in its own user space, as SVG defines it for
    /// <c>objectBoundingBox</c> units.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contract: the box is the fill geometry of a shape, without the stroke, markers, clipping, masking, or opacity.
    /// For a container (<c>g</c>, <c>a</c>, nested <c>svg</c>, <c>symbol</c>) it is the union of the boxes of its
    /// children, each mapped through the transform of the child (with its <c>transform-origin</c>) and, for a nested
    /// viewport, through the viewport transform; the box of a transformed child is the bounding box of its transformed
    /// box, as browsers compute it. A <c>use</c> element is a container of its referenced element, translated by its
    /// <c>x</c> and <c>y</c>. The own transform of <paramref name="element"/> is not applied.
    /// </para>
    /// <para>
    /// An element that draws nothing contributes nothing: <c>display: none</c>, invalid or empty geometry, a missing or
    /// cyclic <c>use</c> reference, and elements that do not render (definitions, paint servers, unknown elements).
    /// <c>visibility: hidden</c> and <c>opacity: 0</c> elements still contribute, because SVG excludes neither from the
    /// box. A line contributes a box with a zero side. An <c>image</c> contributes its viewport. A <c>switch</c>
    /// contributes the box of the child that it renders, and an element whose conditional processing attributes fail
    /// contributes nothing. Kinds that are not handled here contribute nothing; code that renders a new kind extends the
    /// switch in <see cref="TryGetObjectBoundingBox(int, in SvgResolveContext, out RectangleF)"/>.
    /// </para>
    /// <para>
    /// Geometry comes from the shared shape cache, so a shape that later renders builds its path once.
    /// </para>
    /// </remarks>
    /// <param name="element">The element index.</param>
    /// <param name="context">
    /// The context of the element: its computed style (for font-relative lengths and the inherited style of children) and
    /// the viewport that percentages refer to. The transform and depth of the context are ignored.
    /// </param>
    /// <param name="bounds">When this method returns <see langword="true"/>, contains the box in the user space of the element.</param>
    /// <returns><see langword="true"/> if the element has a box; otherwise, <see langword="false"/>.</returns>
    internal bool TryGetObjectBoundingBox(int element, in SvgResolveContext context, out RectangleF bounds)
    {
        bounds = default;
        if (context.Style.Display == SvgDisplay.None)
        {
            return false;
        }

        SvgElementKind kind = this.tree[element].Kind;
        switch (kind)
        {
            case SvgElementKind.Path:
            case SvgElementKind.Rect:
            case SvgElementKind.Circle:
            case SvgElementKind.Ellipse:
            case SvgElementKind.Line:
            case SvgElementKind.Polyline:
            case SvgElementKind.Polygon:
            {
                IPath? path = this.GetShape(element, kind, in context);
                if (path is null)
                {
                    return false;
                }

                bounds = path.Bounds;
                return float.IsFinite(bounds.Width) && float.IsFinite(bounds.Height);
            }

            case SvgElementKind.A when this.IsActiveTextSpan(element):
            case SvgElementKind.Text:
            case SvgElementKind.TSpan:
            case SvgElementKind.TextPath:
                // A tspan, an a inside text, and a textPath have a box only while the text that holds them resolves.
                return this.TryGetTextBoundingBox(element, in context, out bounds);

            case SvgElementKind.G:
            case SvgElementKind.A:
                return this.TryGetChildrenBoundingBox(element, in context, Matrix3x2.Identity, context.Viewport, out bounds);

            case SvgElementKind.Switch:
                return this.TryGetSwitchBoundingBox(element, in context, out bounds);
            case SvgElementKind.Image:
                return this.TryGetImageBoundingBox(element, in context, out bounds);

            case SvgElementKind.Svg:
            case SvgElementKind.Symbol:
            {
                // A nested viewport maps its content through its viewport transform. The clip of the viewport does not
                // reduce the box. A symbol only has a viewport through use, which passes its size in the viewport
                // attributes of the use element; outside use the symbol takes the size of the parent viewport.
                if (!this.TryGetViewportTransform(element, in context, default, default, out Matrix3x2 content, out SizeF contentViewport, out _))
                {
                    return false;
                }

                return this.TryGetChildrenBoundingBox(element, in context, content, contentViewport, out bounds);
            }

            case SvgElementKind.Use:
            {
                SvgUseAttributes use = this.GetUseAttributes(element, context.Style);
                if (use.Href is null
                    || !this.tree.TryGetElementById(use.Href, out int target)
                    || (target <= element && element < this.tree[target].SubtreeEnd)
                    || !this.TryEnterReference(element))
                {
                    return false;
                }

                SvgElementKind targetKind = this.tree[target].Kind;
                ref readonly SvgSpecifiedStyle targetSpecified = ref this.tree.GetStyle(target);
                SvgComputedStyle targetStyle = SvgComputedStyle.Compute(in targetSpecified, context.Style, in this.initialStyle, this.units, targetKind);
                SvgResolveContext targetContext = new(in targetStyle, Matrix3x2.Identity, context.Viewport, context.Depth + 1);
                bool found = false;
                RectangleF box = default;
                if (targetKind is SvgElementKind.Svg or SvgElementKind.Symbol)
                {
                    // The referenced viewport takes its size from the use element.
                    if (targetStyle.Display != SvgDisplay.None
                        && this.TryGetViewportTransform(target, in targetContext, use.Width, use.Height, out Matrix3x2 content, out SizeF viewport, out _))
                    {
                        found = this.TryGetChildrenBoundingBox(target, in targetContext, content, viewport, out box);
                    }
                }
                else
                {
                    found = this.TryGetObjectBoundingBox(target, in targetContext, out box);
                }

                this.ExitReference();
                if (!found)
                {
                    return false;
                }

                SvgUnitBasis useUnits = this.units.WithFont(context.Style.Text);
                float x = SvgUnits.ToUser(use.X, context.Viewport.Width, context.Style.FontSize, useUnits, 0F);
                float y = SvgUnits.ToUser(use.Y, context.Viewport.Height, context.Style.FontSize, useUnits, 0F);
                Matrix3x2 transform = this.TryGetLocalTransform(target, in targetStyle, context.Viewport, out Matrix3x2 local) ? local : Matrix3x2.Identity;
                bounds = TransformBounds(box, transform * Matrix3x2.CreateTranslation(x, y));
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Computes the union of the object bounding boxes of the children of an element, each mapped through its own local
    /// transform and then through <paramref name="contentTransform"/>.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="context">The context of the element, whose style the children inherit.</param>
    /// <param name="contentTransform">The transform from the content coordinate system of the element to its user space.</param>
    /// <param name="viewport">The viewport that percentages in the children refer to.</param>
    /// <param name="bounds">When this method returns <see langword="true"/>, contains the union.</param>
    /// <returns><see langword="true"/> if at least one child has a box; otherwise, <see langword="false"/>.</returns>
    private bool TryGetChildrenBoundingBox(int element, in SvgResolveContext context, Matrix3x2 contentTransform, SizeF viewport, out RectangleF bounds)
    {
        bounds = default;
        bool found = false;
        int depth = context.Depth + 1;
        if (depth > this.maxDepth)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the nesting depth limit of {this.maxDepth} through use references.");
        }

        for (int child = this.tree.GetFirstChild(element); child >= 0; child = this.tree.GetNextSibling(child))
        {
            if (this.tree[child].HasConditionalAttributes && !this.PassesConditions(child))
            {
                continue;
            }

            SvgElementKind kind = this.tree[child].Kind;
            SvgComputedStyle style = SvgComputedStyle.Compute(in this.tree.GetStyle(child), context.Style, in this.initialStyle, this.units, kind);
            SvgResolveContext childContext = new(in style, Matrix3x2.Identity, viewport, depth);
            if (!this.TryGetObjectBoundingBox(child, in childContext, out RectangleF box))
            {
                continue;
            }

            Matrix3x2 transform = this.TryGetLocalTransform(child, in style, viewport, out Matrix3x2 local) ? local * contentTransform : contentTransform;
            box = TransformBounds(box, transform);
            bounds = found ? RectangleF.Union(bounds, box) : box;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Resolves a shape into one draw node: its geometry, its fill brush, and its pen.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="kind">The element kind.</param>
    /// <param name="context">The context of the element.</param>
    private void ResolveShape(int index, SvgElementKind kind, in SvgResolveContext context)
    {
        if (this.TryResolveMarkedShape(index, kind, in context))
        {
            return;
        }

        SvgComputedStyle style = context.Style;

        // visibility hides the shape itself; children of hidden groups can still render, so this is checked per shape.
        if (style.Visibility != SvgVisibility.Visible)
        {
            return;
        }

        IPath? path = this.GetShape(index, kind, in context);
        if (path is null)
        {
            return;
        }

        RectangleF bounds = path.Bounds;
        SizeF viewport = context.Viewport;
        float opacity = style.Opacity;

        // A line has no interior, so it is never filled.
        Brush? fill = kind == SvgElementKind.Line
            ? null
            : this.ResolvePaint(style.Fill, style.FillOpacity, bounds, in context);

        Pen? stroke = null;
        float strokeWidth = SvgUnits.ToUser(style.StrokeWidth, SvgUnits.Diagonal(viewport), style.FontSize, this.units, 0F);

        if (strokeWidth > 0F)
        {
            Brush? strokeBrush = this.ResolvePaint(style.Stroke, style.StrokeOpacity, bounds, in context);
            if (strokeBrush is not null)
            {
                stroke = this.CreatePen(strokeBrush, strokeWidth, in style, viewport, this.GetDashScale(index, path, in style));
            }
        }

        if (fill is null && stroke is null)
        {
            return;
        }

        // Opacity on a shape with only a fill or only a stroke multiplies the paint alpha. With both, the stroke overlaps
        // the fill, so the shape needs a layer.
        bool needsLayer = opacity < 1F && fill is not null && stroke is not null;
        if (opacity < 1F && !needsLayer)
        {
            fill = fill is null ? null : this.paints.MultiplyOpacity(fill, opacity);
            stroke = stroke is null ? null : this.paints.MultiplyOpacity(stroke, opacity);
        }

        // Prime the cached closed form of the path, which every fill uses, while the resolve stage is single threaded.
        if (fill is not null)
        {
            _ = path.AsClosedPath();
        }

        RectangleF paintBounds = stroke is null ? bounds : RectangleF.Inflate(bounds, stroke.GetOutlineRadius(1F), stroke.GetOutlineRadius(1F));
        SvgDrawFlags flags = GetDrawFlags(in style, fill is not null, stroke is not null);
        SvgRenderNode draw = new()
        {
            Kind = SvgRenderNodeKind.Draw,
            Path = path,
            Fill = fill,
            Stroke = stroke,
            FillRule = style.FillRule == SvgFillRule.EvenOdd ? IntersectionRule.EvenOdd : IntersectionRule.NonZero,
            TransformIndex = this.GetTransformIndex(context.Transform),
            Bounds = (flags & SvgDrawFlags.NonScalingStroke) != 0 ? UnboundedBounds : TransformBounds(paintBounds, context.Transform),
            Flags = flags,
        };

        if (needsLayer)
        {
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.BeginLayer, Opacity = opacity, Bounds = draw.Bounds });
            this.nodes.Add(draw);
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndLayer });
        }
        else
        {
            this.nodes.Add(draw);
        }
    }

    /// <summary>
    /// Creates the pen of a stroke from the computed stroke properties.
    /// </summary>
    /// <param name="brush">The stroke brush.</param>
    /// <param name="width">The stroke width in user units. It is greater than zero.</param>
    /// <param name="style">The computed style of the shape.</param>
    /// <param name="viewport">The viewport size, for percentages.</param>
    /// <param name="dashScale">The factor of <c>pathLength</c> for the dash pattern and offset; see <c>SvgResolver.PathLength.cs</c>.</param>
    /// <returns>The pen.</returns>
    private Pen CreatePen(Brush brush, float width, in SvgComputedStyle style, SizeF viewport, float dashScale = 1F)
    {
        LineCap lineCap = style.StrokeLineCap switch
        {
            SvgStrokeLineCap.Round => LineCap.Round,
            SvgStrokeLineCap.Square => LineCap.Square,
            _ => LineCap.Butt
        };

        // SVG miter falls back to a bevel past the miter limit, which is MiterRevert. miter-clip and arcs have no match
        // and fall back to miter, as SVG 2 allows for user agents that do not support them.
        LineJoin lineJoin = style.StrokeLineJoin switch
        {
            SvgStrokeLineJoin.Round => LineJoin.Round,
            SvgStrokeLineJoin.Bevel => LineJoin.Bevel,
            _ => LineJoin.MiterRevert
        };

        float[]? pattern = null;
        float offset = 0F;
        if (!style.StrokeDashArray.IsNone)
        {
            pattern = this.CreateDashPattern(style.StrokeDashArray, width, style.FontSize, viewport, dashScale);
            if (pattern is not null)
            {
                float dashOffset = SvgUnits.ToUser(style.StrokeDashOffset, SvgUnits.Diagonal(viewport), style.FontSize, this.units, 0F);

                // An infinite offset, from a pathLength of zero, has no defined result and is ignored.
                dashOffset = ScalePathDistance(dashOffset, dashScale);
                if (!float.IsFinite(dashOffset))
                {
                    dashOffset = 0F;
                }

                // The pen measures the dash offset in multiples of the stroke width.
                offset = dashOffset / width;
            }
        }

        return this.paints.GetPen(brush, width, lineCap, lineJoin, style.StrokeMiterLimit, pattern, offset);
    }

    /// <summary>
    /// Converts a <c>stroke-dasharray</c> value to the dash pattern of a pen.
    /// </summary>
    /// <remarks>
    /// The pen measures the pattern in multiples of the stroke width, so each length is divided by the width. A list
    /// with an odd number of values repeats once to make an even number, and a list whose sum is zero draws a solid
    /// stroke, as SVG defines. The parser has already rejected negative values.
    /// </remarks>
    /// <param name="dashArray">The computed dash array.</param>
    /// <param name="width">The stroke width in user units.</param>
    /// <param name="fontSize">The computed font size, for <c>em</c> and <c>ex</c>.</param>
    /// <param name="viewport">The viewport size, for percentages.</param>
    /// <param name="scale">The factor of <c>pathLength</c>, or 1.</param>
    /// <returns>The pattern, or <see langword="null"/> for a solid stroke.</returns>
    private float[]? CreateDashPattern(SvgDashArray dashArray, float width, float fontSize, SizeF viewport, float scale)
    {
        ReadOnlySpan<SvgLength> lengths = this.GetDashLengths(dashArray);
        float diagonal = SvgUnits.Diagonal(viewport);
        int count = lengths.Length % 2 == 0 ? lengths.Length : lengths.Length * 2;
        float[] pattern = new float[count];
        float sum = 0F;
        float first = 0F;

        for (int i = 0; i < lengths.Length; i++)
        {
            float length = SvgUnits.ToUser(lengths[i], diagonal, fontSize, this.units, 0F);
            if (scale != 1F)
            {
                length = ScaleDashLength(length, scale);
                first = i == 0 ? length : first;
            }

            pattern[i] = length / width;
            sum += length;
        }

        if (!(sum > 0F) || !float.IsFinite(sum))
        {
            return null;
        }

        // A pathLength of zero, or one so small that the scale overflows, makes the first dash infinite when it is not
        // zero, which covers every subpath: the stroke is solid.
        if (first >= InfiniteDashLength)
        {
            return null;
        }

        if (count != lengths.Length)
        {
            pattern.AsSpan(0, lengths.Length).CopyTo(pattern.AsSpan(lengths.Length));
        }

        return pattern;
    }

    /// <summary>
    /// Returns the geometry of a shape in its user space, building it once per element and sharing it across every
    /// <c>use</c> instance with the same viewport and font size.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="kind">The element kind.</param>
    /// <param name="context">The context of the element.</param>
    /// <returns>The path, or <see langword="null"/> when the geometry is invalid or empty and the shape does not render.</returns>
    private IPath? GetShape(int index, SvgElementKind kind, in SvgResolveContext context)
    {
        if (kind == SvgElementKind.Path)
        {
            // The parser built the path. An empty path, which the parser builds when the first command has an error, has
            // nothing to paint.
            IPath? path = this.GetPathData(index, context.Style);
            if (path is ComplexPolygon complex)
            {
                using IEnumerator<IPath> subpaths = complex.Paths.GetEnumerator();
                if (!subpaths.MoveNext())
                {
                    return null;
                }
            }

            return path;
        }

        if (kind is SvgElementKind.Polyline or SvgElementKind.Polygon)
        {
            ReadOnlySpan<PointF> points = this.tree.GetPoints(index);
            if (points.Length < 2)
            {
                return null;
            }

            // A polyline is an open path, which fill closes implicitly; a polygon is closed.
            LinearLineSegment segment = new(points.ToArray());
            return kind == SvgElementKind.Polygon ? new Polygon(segment) : new Path(segment);
        }

        // Percentages in the other shapes refer to the viewport, and em and ex to the font size, so the shared geometry is
        // keyed by both. Instances in the same viewport, which is the usual case, share one path. Geometry from CSS can
        // differ per instance, so such a shape is not cached.
        Vector3 key = new(context.Viewport.Width, context.Viewport.Height, context.Style.FontSize);
        bool styled = HasStyledGeometry(context.Style.Geometry);
        IPath? cached = this.shapes[index];
        if (!styled && cached is not null && this.shapeContexts[index] == key)
        {
            return cached;
        }

        IPath? shape = kind switch
        {
            SvgElementKind.Rect => this.CreateRect(index, in context),
            SvgElementKind.Circle or SvgElementKind.Ellipse => this.CreateEllipse(index, kind, in context),
            _ => this.CreateLine(index, in context),
        };

        if (shape is not null && !styled)
        {
            this.shapes[index] = shape;
            this.shapeContexts[index] = key;
        }

        return shape;
    }

    /// <summary>
    /// Builds the geometry of a <c>rect</c> element.
    /// </summary>
    /// <remarks>
    /// A missing <c>rx</c> takes the value of <c>ry</c> and the other way round, then each radius is clamped to half the
    /// size on its axis, as SVG 2 defines. The rounded corners are quarter ellipses with separate radii, each a cubic
    /// Bézier curve.
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    /// <returns>The path, or <see langword="null"/> when the width or height is not positive.</returns>
    private IPath? CreateRect(int index, in SvgResolveContext context)
    {
        SvgRectAttributes rect = this.GetRectAttributes(index, context.Style);
        SizeF viewport = context.Viewport;
        float fontSize = context.Style.FontSize;
        SvgUnitBasis units = this.units.WithFont(context.Style.Text);

        float width = SvgUnits.ToUser(rect.Width, viewport.Width, fontSize, units, 0F);
        float height = SvgUnits.ToUser(rect.Height, viewport.Height, fontSize, units, 0F);
        if (!(width > 0F) || !(height > 0F))
        {
            return null;
        }

        float x = SvgUnits.ToUser(rect.X, viewport.Width, fontSize, units, 0F);
        float y = SvgUnits.ToUser(rect.Y, viewport.Height, fontSize, units, 0F);
        float rx = SvgUnits.ToUser(rect.Rx, viewport.Width, fontSize, units, 0F);
        float ry = SvgUnits.ToUser(rect.Ry, viewport.Height, fontSize, units, 0F);

        if (!rect.Rx.IsSpecified)
        {
            rx = ry;
        }
        else if (!rect.Ry.IsSpecified)
        {
            ry = rx;
        }

        rx = MathF.Min(rx, width / 2F);
        ry = MathF.Min(ry, height / 2F);

        if (!(rx > 0F && ry > 0F))
        {
            return new RectanglePolygon(x, y, width, height);
        }

        // Each corner is a quarter ellipse as one cubic Bézier curve, built like the quarters of CreateEllipse. Curves
        // flatten at the resolution of each draw, as Skia and cairo flatten theirs, so a rectangle that is small in user
        // space and scaled up keeps smooth corners; an arc segment would flatten once, in user space. The outline starts
        // after the top-left corner and runs clockwise, as the SVG path equivalent of the shape does, so dash patterns
        // start where SVG starts them. Edges of zero length, where a radius is half the size, are left out.
        float right = x + width;
        float bottom = y + height;
        float kx = rx * Kappa;
        float ky = ry * Kappa;
        List<ILineSegment> segments = new(8);
        AddEdge(segments, new PointF(x + rx, y), new PointF(right - rx, y));
        segments.Add(new CubicBezierLineSegment(
            new PointF(right - rx, y), new PointF(right - rx + kx, y), new PointF(right, y + ry - ky), new PointF(right, y + ry)));
        AddEdge(segments, new PointF(right, y + ry), new PointF(right, bottom - ry));
        segments.Add(new CubicBezierLineSegment(
            new PointF(right, bottom - ry), new PointF(right, bottom - ry + ky), new PointF(right - rx + kx, bottom), new PointF(right - rx, bottom)));
        AddEdge(segments, new PointF(right - rx, bottom), new PointF(x + rx, bottom));
        segments.Add(new CubicBezierLineSegment(
            new PointF(x + rx, bottom), new PointF(x + rx - kx, bottom), new PointF(x, bottom - ry + ky), new PointF(x, bottom - ry)));
        AddEdge(segments, new PointF(x, bottom - ry), new PointF(x, y + ry));
        segments.Add(new CubicBezierLineSegment(
            new PointF(x, y + ry), new PointF(x, y + ry - ky), new PointF(x + rx - kx, y), new PointF(x + rx, y)));

        return new Polygon(segments.ToArray());

        static void AddEdge(List<ILineSegment> segments, PointF start, PointF end)
        {
            if (start != end)
            {
                segments.Add(new LinearLineSegment(start, end));
            }
        }
    }

    /// <summary>
    /// Builds the geometry of a <c>circle</c> or <c>ellipse</c> element.
    /// </summary>
    /// <remarks>
    /// The outline starts at the rightmost point and runs through the bottom, as the SVG path equivalent of the shapes
    /// does, so dash patterns start where SVG starts them. Each quarter is a cubic Bézier curve. For an ellipse, a missing
    /// <c>rx</c> takes the value of <c>ry</c> and the other way round, as SVG 2 defines.
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="kind">The element kind.</param>
    /// <param name="context">The context of the element.</param>
    /// <returns>The path, or <see langword="null"/> when a radius is not positive.</returns>
    private Polygon? CreateEllipse(int index, SvgElementKind kind, in SvgResolveContext context)
    {
        SvgCircleAttributes circle = this.GetCircleAttributes(index, context.Style);
        SizeF viewport = context.Viewport;
        float fontSize = context.Style.FontSize;
        SvgUnitBasis units = this.units.WithFont(context.Style.Text);

        float rx;
        float ry;
        if (kind == SvgElementKind.Circle)
        {
            rx = SvgUnits.ToUser(circle.R, SvgUnits.Diagonal(viewport), fontSize, units, 0F);
            ry = rx;
        }
        else
        {
            rx = SvgUnits.ToUser(circle.Rx, viewport.Width, fontSize, units, 0F);
            ry = SvgUnits.ToUser(circle.Ry, viewport.Height, fontSize, units, 0F);
            if (!circle.Rx.IsSpecified)
            {
                rx = ry;
            }
            else if (!circle.Ry.IsSpecified)
            {
                ry = rx;
            }
        }

        if (!(rx > 0F) || !(ry > 0F))
        {
            return null;
        }

        float cx = SvgUnits.ToUser(circle.Cx, viewport.Width, fontSize, units, 0F);
        float cy = SvgUnits.ToUser(circle.Cy, viewport.Height, fontSize, units, 0F);
        float kx = rx * Kappa;
        float ky = ry * Kappa;

        PointF[] points =
        [
            new(cx + rx, cy),
            new(cx + rx, cy + ky), new(cx + kx, cy + ry), new(cx, cy + ry),
            new(cx - kx, cy + ry), new(cx - rx, cy + ky), new(cx - rx, cy),
            new(cx - rx, cy - ky), new(cx - kx, cy - ry), new(cx, cy - ry),
            new(cx + kx, cy - ry), new(cx + rx, cy - ky), new(cx + rx, cy),
        ];

        return new Polygon(new CubicBezierLineSegment(points));
    }

    /// <summary>
    /// Builds the geometry of a <c>line</c> element: an open path with one segment.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    /// <returns>The path.</returns>
    private Path CreateLine(int index, in SvgResolveContext context)
    {
        ref readonly SvgLineAttributes line = ref this.tree.GetLine(index);
        SizeF viewport = context.Viewport;
        float fontSize = context.Style.FontSize;
        SvgUnitBasis units = this.units.WithFont(context.Style.Text);

        PointF start = new(
            SvgUnits.ToUser(line.X1, viewport.Width, fontSize, units, 0F),
            SvgUnits.ToUser(line.Y1, viewport.Height, fontSize, units, 0F));
        PointF end = new(
            SvgUnits.ToUser(line.X2, viewport.Width, fontSize, units, 0F),
            SvgUnits.ToUser(line.Y2, viewport.Height, fontSize, units, 0F));

        return new Path(new LinearLineSegment(start, end));
    }

    /// <summary>
    /// Starts a group with an opacity. The group reserves a placeholder node that <see cref="EndGroup(int, float)"/> turns
    /// into a layer when the group needs one, so no node has to move.
    /// </summary>
    /// <param name="opacity">The opacity of the group.</param>
    /// <returns>The index of the placeholder, or -1 when the group is opaque.</returns>
    private int BeginGroup(float opacity)
    {
        if (opacity >= 1F)
        {
            return -1;
        }

        this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.None });
        return this.nodes.Count - 1;
    }

    /// <summary>
    /// Ends a group with an opacity and picks the cheapest correct way to apply the opacity to its content.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>A group with no content needs nothing.</item>
    /// <item>A group whose content is one path with only a fill or only a stroke multiplies the paint alpha. One path
    /// cannot overlap itself in paint, so the result is the same as with a layer. Viewport clips around the path do not
    /// change that.</item>
    /// <item>A group whose content is exactly one layer without a blend mode multiplies the opacity of that layer.</item>
    /// <item>Any other group becomes a layer bounded to the union of the paint bounds of its content, which include the
    /// stroke extent.</item>
    /// </list>
    /// </remarks>
    /// <param name="placeholder">The index that <see cref="BeginGroup(float)"/> returned.</param>
    /// <param name="opacity">The opacity of the group.</param>
    private void EndGroup(int placeholder, float opacity)
    {
        if (placeholder < 0)
        {
            return;
        }

        int first = placeholder + 1;
        int end = this.nodes.Count;
        if (first == end)
        {
            this.nodes.RemoveAt(placeholder);
            return;
        }

        Span<SvgRenderNode> nodes = this.nodes.AsSpan();
        int drawCount = 0;
        int drawIndex = -1;
        bool hasLayer = false;
        RectangleF bounds = default;

        for (int i = first; i < end; i++)
        {
            // A filter node paints its filter region, so it counts as a draw with those bounds.
            ref SvgRenderNode node = ref nodes[i];
            if (node.Kind is SvgRenderNodeKind.Draw or SvgRenderNodeKind.Filter)
            {
                bounds = drawCount == 0 ? node.Bounds : RectangleF.Union(bounds, node.Bounds);
                drawCount++;
                drawIndex = i;
            }
            else if (node.Kind == SvgRenderNodeKind.BeginLayer)
            {
                hasLayer = true;
            }
        }

        // A filter result is one image, so a group opacity multiplies it as it multiplies the paint of one path.
        if (!hasLayer && drawCount == 1 && nodes[drawIndex].Kind == SvgRenderNodeKind.Filter)
        {
            nodes[drawIndex].Opacity *= opacity;
            this.hasEmptyNodes = true;
            return;
        }

        if (!hasLayer && drawCount == 1 && (nodes[drawIndex].Fill is null || nodes[drawIndex].Stroke is null))
        {
            ref SvgRenderNode draw = ref nodes[drawIndex];
            draw.Fill = draw.Fill is null ? null : this.paints.MultiplyOpacity(draw.Fill, opacity);
            draw.Stroke = draw.Stroke is null ? null : this.paints.MultiplyOpacity(draw.Stroke, opacity);
            this.hasEmptyNodes = true;
            return;
        }

        // A layer that blends must blend inside the isolated group, so it cannot take the opacity of the group.
        if (nodes[first].Kind == SvgRenderNodeKind.BeginLayer
            && nodes[first].BlendMode == SvgBlendMode.Normal
            && IsLayerSpanning(nodes, first, end))
        {
            nodes[first].Opacity *= opacity;
            this.hasEmptyNodes = true;
            return;
        }

        nodes[placeholder] = new SvgRenderNode { Kind = SvgRenderNodeKind.BeginLayer, Opacity = opacity, Bounds = bounds };
        this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndLayer });
    }

    /// <summary>
    /// Returns whether the layer that begins at <paramref name="first"/> ends at the last node before <paramref name="end"/>.
    /// </summary>
    /// <param name="nodes">The nodes.</param>
    /// <param name="first">The index of a <see cref="SvgRenderNodeKind.BeginLayer"/> node.</param>
    /// <param name="end">The end of the range.</param>
    /// <returns><see langword="true"/> if the layer spans the whole range; otherwise, <see langword="false"/>.</returns>
    private static bool IsLayerSpanning(ReadOnlySpan<SvgRenderNode> nodes, int first, int end)
    {
        int depth = 0;
        for (int i = first; i < end; i++)
        {
            SvgRenderNodeKind kind = nodes[i].Kind;
            if (kind == SvgRenderNodeKind.BeginLayer)
            {
                depth++;
            }
            else if (kind == SvgRenderNodeKind.EndLayer && --depth == 0)
            {
                return i == end - 1;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the index of a transform in the transform table, adding it when it is new. Consecutive nodes usually share
    /// a transform, so the last lookup is remembered.
    /// </summary>
    /// <param name="transform">The transform.</param>
    /// <returns>The index.</returns>
    private int GetTransformIndex(Matrix3x2 transform)
    {
        if (this.lastTransformIndex >= 0 && transform == this.lastTransform)
        {
            return this.lastTransformIndex;
        }

        int index = this.transforms.GetOrAdd(transform);
        this.lastTransform = transform;
        this.lastTransformIndex = index;
        return index;
    }

    /// <summary>
    /// Takes the render nodes from <paramref name="start"/> to the end of the node list out of the list, as a finished
    /// node array: the placeholder nodes are removed, every begin node is linked to its end node, and the deepest scope
    /// nesting is measured. The document takes all nodes this way; content that is kept apart from the main paint order,
    /// such as a pattern tile, takes the nodes that <see cref="ResolveContent(int, in SvgResolveContext)"/> added.
    /// </summary>
    /// <remarks>
    /// The range must hold whole scopes: every begin node in it has its end node in it. The transform indexes of the nodes
    /// refer to the transform table of the document, which every node array of the document shares.
    /// </remarks>
    /// <param name="start">The first node to take, a value of <see cref="NodeCount"/> from before the nodes were added.</param>
    /// <param name="maxScopeDepth">When this method returns, contains the deepest nesting of layer, mask, and clip scopes.</param>
    /// <returns>The nodes.</returns>
    internal SvgRenderNode[] TakeNodes(int start, out int maxScopeDepth)
    {
        SvgRenderNode[] result = this.BuildNodes(this.nodes.AsSpan()[start..], out maxScopeDepth);
        this.nodes.RemoveRange(start, this.nodes.Count - start);
        return result;
    }

    /// <summary>
    /// Removes the placeholder nodes, links every begin node to its end node, and measures the deepest scope nesting.
    /// </summary>
    /// <param name="source">The nodes, which hold whole scopes.</param>
    /// <param name="maxScopeDepth">When this method returns, contains the deepest nesting of layer and clip scopes.</param>
    /// <returns>The finished nodes.</returns>
    private SvgRenderNode[] BuildNodes(ReadOnlySpan<SvgRenderNode> source, out int maxScopeDepth)
    {
        int count = source.Length;
        if (this.hasEmptyNodes)
        {
            count = 0;
            foreach (SvgRenderNode node in source)
            {
                if (node.Kind != SvgRenderNodeKind.None)
                {
                    count++;
                }
            }
        }

        SvgRenderNode[] result = new SvgRenderNode[count];

        // The stack of open scopes is written before it is read, so a pooled array of any content serves.
        int[] open = ArrayPool<int>.Shared.Rent(Math.Max(count, 1));
        int depth = 0;
        int written = 0;
        maxScopeDepth = 0;

        foreach (SvgRenderNode node in source)
        {
            switch (node.Kind)
            {
                case SvgRenderNodeKind.None:
                    continue;
                case SvgRenderNodeKind.BeginLayer:
                case SvgRenderNodeKind.BeginClip:
                case SvgRenderNodeKind.BeginMask:
                    open[depth++] = written;
                    maxScopeDepth = Math.Max(maxScopeDepth, depth);
                    break;
                case SvgRenderNodeKind.EndLayer:
                case SvgRenderNodeKind.EndClip:
                case SvgRenderNodeKind.EndMask:
                    result[open[--depth]].End = written;
                    break;
            }

            result[written++] = node;
        }

        ArrayPool<int>.Shared.Return(open);
        return result;
    }

    /// <summary>
    /// Returns the axis-aligned bounding box of a transformed rectangle.
    /// </summary>
    /// <param name="bounds">The rectangle.</param>
    /// <param name="transform">The transform.</param>
    /// <returns>The bounding box.</returns>
    private static RectangleF TransformBounds(RectangleF bounds, Matrix3x2 transform)
    {
        if (transform.IsIdentity)
        {
            return bounds;
        }

        Vector2 a = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), transform);
        Vector2 b = Vector2.Transform(new Vector2(bounds.Right, bounds.Top), transform);
        Vector2 c = Vector2.Transform(new Vector2(bounds.Left, bounds.Bottom), transform);
        Vector2 d = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), transform);
        Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        return RectangleF.FromLTRB(min.X, min.Y, max.X, max.Y);
    }
}
