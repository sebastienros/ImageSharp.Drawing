// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The <c>pattern</c> paint server.
/// </content>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// The number of nesting levels whose tiles get the full pixel budget of <see cref="SvgLimits.MaxPatternTilePixels"/>.
    /// Each deeper level halves the budget.
    /// </summary>
    private const int FullBudgetPatternNesting = 3;

    /// <summary>
    /// The resolved tiles, shared by every shape that paints the same pattern content with the same tile geometry.
    /// Created on first use, so documents without the feature allocate nothing for it.
    /// </summary>
    private Dictionary<SvgPatternTileKey, SvgPatternTile?>? patternTiles;

    /// <summary>
    /// The elements of the <c>href</c> chain that <see cref="TryResolvePattern"/> is following, reused between calls.
    /// </summary>
    private List<int>? referenceScratch;

    /// <summary>
    /// The number of reference cycles that pattern resolution has met. A tile whose content met a cycle depends on the
    /// reference chain at the time it was resolved, so it is not shared.
    /// </summary>
    private int patternCycles;

    /// <summary>
    /// The number of pattern tiles whose content is resolving: 0 for a pattern that the document paints directly, 1 for a
    /// pattern that the content of such a tile paints, and so on.
    /// </summary>
    private int patternNesting;

    /// <summary>
    /// Resolves a computed <c>fill</c> or <c>stroke</c> value of a shape to a brush. A reference to a <c>pattern</c> and
    /// the context keywords resolve here; every other paint resolves in <see cref="SvgPaintCache.ResolvePaint"/>.
    /// </summary>
    /// <param name="paint">The computed paint.</param>
    /// <param name="opacity">The fill or stroke opacity.</param>
    /// <param name="bounds">The fill bounding box of the shape in its user space.</param>
    /// <param name="context">The context of the shape.</param>
    /// <returns>The brush, or <see langword="null"/> when nothing is painted.</returns>
    private Brush? ResolvePaint(SvgPaint paint, float opacity, RectangleF bounds, in SvgResolveContext context)
    {
        if (paint.Kind is SvgPaintKind.ContextFill or SvgPaintKind.ContextStroke)
        {
            return this.ResolveContextPaint(paint.Kind, opacity, in context);
        }

        if (paint.Kind == SvgPaintKind.Url
            && paint.Reference is not null
            && this.tree.TryGetElementById(paint.Reference, out int element)
            && this.tree[element].Kind == SvgElementKind.Pattern)
        {
            if (this.TryResolvePattern(element, opacity, bounds, in context, out Brush? brush))
            {
                return brush;
            }

            // A pattern that cannot apply to this shape uses the fallback, as an unusable gradient does.
            paint = new SvgPaint(paint.Fallback, paint.Color, null, SvgPaintKind.None);
        }

        return this.paints.ResolvePaint(paint, context.Style.Color, opacity, bounds, context.Viewport, context.Style.FontSize);
    }

    /// <summary>
    /// Resolves a <c>pattern</c> reference for one shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rules, from SVG 1.1 and SVG 2 and checked against the resvg tests:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Unspecified attributes come from the patterns that <c>href</c> references, through the whole
    /// chain; the content comes from the first pattern in the chain that has child elements. A reference back into the
    /// chain ends it.</description></item>
    /// <item><description><c>patternUnits</c> defaults to <c>objectBoundingBox</c>, where <c>x</c>, <c>y</c>,
    /// <c>width</c>, and <c>height</c> are fractions of the bounding box of the shape; <c>patternContentUnits</c> defaults
    /// to <c>userSpaceOnUse</c>. A <c>viewBox</c> maps to the tile and overrides <c>patternContentUnits</c>.</description></item>
    /// <item><description>The content has its origin at the tile origin. In <c>objectBoundingBox</c> content units it is
    /// scaled by the box size, not moved to the box origin. The tile clips the content.</description></item>
    /// <item><description>The <c>patternTransform</c>, about its <c>transform-origin</c>, maps the pattern space with its
    /// grid of tiles into the user space of the shape.</description></item>
    /// <item><description>A zero or negative tile size, a zero-size bounding box with <c>objectBoundingBox</c> units, a
    /// view box with a zero side, or a singular <c>patternTransform</c> make the pattern unusable for the shape: the
    /// fallback paints. A pattern without content paints nothing. A pattern whose content references the pattern again
    /// is an error and paints nothing.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="element">The index of the referenced <c>pattern</c> element.</param>
    /// <param name="opacity">The fill or stroke opacity.</param>
    /// <param name="bounds">The fill bounding box of the shape in its user space.</param>
    /// <param name="context">The context of the shape.</param>
    /// <param name="brush">When this method returns <see langword="true"/>, contains the brush, or <see langword="null"/> when the pattern paints nothing.</param>
    /// <returns><see langword="true"/> if the pattern applies; <see langword="false"/> if the fallback paints instead.</returns>
    private bool TryResolvePattern(int element, float opacity, RectangleF bounds, in SvgResolveContext context, out Brush? brush)
    {
        brush = null;

        // Follow the href chain. Each element fills its unspecified attributes from the next one, and the content
        // comes from the first element with child elements.
        SvgPatternAttributes attributes = this.tree.GetPattern(element);
        int content = this.tree.GetFirstChild(element) >= 0 ? element : -1;
        int transformElement = attributes.HasTransform ? element : -1;
        List<int> chain = this.referenceScratch ??= [];
        chain.Clear();
        chain.Add(element);

        for (string? href = attributes.Href; href is not null;)
        {
            if (!this.tree.TryGetElementById(href, out int next)
                || this.tree[next].Kind != SvgElementKind.Pattern
                || chain.Contains(next))
            {
                break;
            }

            chain.Add(next);
            ref readonly SvgPatternAttributes referenced = ref this.tree.GetPattern(next);
            if (!attributes.HasTransform && referenced.HasTransform)
            {
                transformElement = next;
            }

            attributes.InheritFrom(in referenced);
            if (content < 0 && this.tree.GetFirstChild(next) >= 0)
            {
                content = next;
            }

            href = referenced.Href;
        }

        bool boxUnits = attributes.Units != SvgCoordinateUnits.UserSpaceOnUse;
        if (boxUnits && !(bounds.Width > 0F && bounds.Height > 0F))
        {
            return false;
        }

        // In bounding box units every length is a fraction of the box, so a percentage is a fraction of 1.
        float fontSize = context.Style.FontSize;
        SvgUnitBasis units = this.units.WithFont(context.Style.Text);
        SizeF viewport = context.Viewport;
        float x;
        float y;
        float width;
        float height;
        if (boxUnits)
        {
            x = bounds.X + (SvgUnits.ToUser(attributes.X, 1F, fontSize, units, 0F) * bounds.Width);
            y = bounds.Y + (SvgUnits.ToUser(attributes.Y, 1F, fontSize, units, 0F) * bounds.Height);
            width = SvgUnits.ToUser(attributes.Width, 1F, fontSize, units, 0F) * bounds.Width;
            height = SvgUnits.ToUser(attributes.Height, 1F, fontSize, units, 0F) * bounds.Height;
        }
        else
        {
            x = SvgUnits.ToUser(attributes.X, viewport.Width, fontSize, units, 0F);
            y = SvgUnits.ToUser(attributes.Y, viewport.Height, fontSize, units, 0F);
            width = SvgUnits.ToUser(attributes.Width, viewport.Width, fontSize, units, 0F);
            height = SvgUnits.ToUser(attributes.Height, viewport.Height, fontSize, units, 0F);
        }

        if (!(width > 0F) || !(height > 0F) || !float.IsFinite(width) || !float.IsFinite(height) || !float.IsFinite(x) || !float.IsFinite(y))
        {
            return false;
        }

        // The content transform maps the content coordinate system to tile space. Percentages in the content refer to
        // the view box when there is one, otherwise to the viewport of the shape.
        Matrix3x2 contentTransform = Matrix3x2.Identity;
        SizeF contentViewport = viewport;
        if (attributes.HasViewBox)
        {
            RectangleF viewBox = attributes.ViewBox;
            if (!(viewBox.Width > 0F) || !(viewBox.Height > 0F))
            {
                return false;
            }

            contentTransform = SvgViewBoxTransform.Create(viewBox, attributes.PreserveAspectRatio, new RectangleF(0F, 0F, width, height));
            contentViewport = viewBox.Size;
        }
        else if (attributes.ContentUnits == SvgCoordinateUnits.ObjectBoundingBox)
        {
            if (!(bounds.Width > 0F && bounds.Height > 0F))
            {
                return false;
            }

            contentTransform = Matrix3x2.CreateScale(bounds.Width, bounds.Height);
        }

        // The tile origin, then the pattern transform about its origin. As for gradients, the origin belongs to the
        // element whose transform applies, and its percentages refer to the viewport (transform-box: view-box).
        Matrix3x2 tileToUser = Matrix3x2.CreateTranslation(x, y);
        if (attributes.HasTransform)
        {
            Vector2 origin = Vector2.Zero;
            if (this.tree.GetStyle(transformElement).IsSpecified(SvgPropertyId.TransformOrigin))
            {
                origin = this.ComputeDocumentStyle(transformElement).GetTransformOrigin(viewport, this.units);
            }

            Matrix3x2 patternTransform = SvgComputedStyle.ApplyTransformOrigin(attributes.Transform, origin);
            if (!(MathF.Abs(patternTransform.GetDeterminant()) > 0F) || !float.IsFinite(patternTransform.GetDeterminant()))
            {
                return false;
            }

            tileToUser *= patternTransform;
        }

        // A pattern on the reference chain is resolving its own content: referencing it again is a cycle, which is an
        // error, so the paint is none.
        if (this.IsReferenceActive(element) || (content >= 0 && this.IsReferenceActive(content)))
        {
            this.patternCycles++;
            return true;
        }

        if (content < 0)
        {
            // A pattern without content is transparent.
            return true;
        }

        SvgPatternTile? tile = this.GetPatternTile(element, content, contentTransform, new SizeF(width, height), contentViewport, context.Depth);
        if (tile is not null)
        {
            brush = new SvgPatternPaint(tile, tileToUser, opacity);
        }

        return true;
    }

    /// <summary>
    /// Returns the tile of a pattern content in one tile geometry, resolving the content on first use.
    /// </summary>
    /// <param name="element">The referenced <c>pattern</c> element.</param>
    /// <param name="content">The <c>pattern</c> element whose children are the content.</param>
    /// <param name="contentTransform">The transform from the content coordinate system to tile space.</param>
    /// <param name="size">The tile size.</param>
    /// <param name="viewport">The viewport that percentages in the content refer to.</param>
    /// <param name="depth">The depth of the painted shape.</param>
    /// <returns>The tile, or <see langword="null"/> when the content renders nothing.</returns>
    private SvgPatternTile? GetPatternTile(int element, int content, Matrix3x2 contentTransform, SizeF size, SizeF viewport, int depth)
    {
        // A tile renders while the tile that paints it renders, and every level can reach the pixel limit, so nested
        // patterns would multiply the work and the memory that one render needs by the nesting depth, up to
        // SvgLimits.MaxNestingDepth. Halving the budget at each level past the first few bounds the pixels of a whole
        // chain to a few times the limit. Tiles below the limit, which are most tiles, keep their full resolution.
        int halvings = Math.Min(Math.Max(this.patternNesting - FullBudgetPatternNesting, 0), 30);
        int maxPixels = Math.Max(this.MaxPatternTilePixels >> halvings, 1);
        SvgPatternTileKey key = new(content, contentTransform, size, viewport, maxPixels);
        this.patternTiles ??= [];
        if (this.patternTiles.TryGetValue(key, out SvgPatternTile? tile))
        {
            return tile;
        }

        // The content inherits style from the ancestors of the pattern that holds it, not from the painted shape. Both
        // the referenced pattern and the content holder go on the reference chain, so a paint inside the content that
        // references either one again is a cycle.
        int cycles = this.patternCycles;
        int start = this.nodes.Count;
        bool entered = element != content && this.TryEnterReference(element);
        SvgComputedStyle style = this.ComputeDocumentStyle(content);
        SvgResolveContext contentContext = new(in style, contentTransform, viewport, depth + 1);
        this.patternNesting++;
        bool resolved = this.ResolveContent(content, in contentContext);
        this.patternNesting--;
        if (entered)
        {
            this.ExitReference();
        }

        SvgRenderNode[] nodes = this.TakeNodes(start, out int maxScopeDepth);
        tile = resolved && nodes.Length > 0
            ? new SvgPatternTile(this.CreateContentDocument(nodes, new RectangleF(PointF.Empty, size), maxScopeDepth), maxPixels)
            : null;

        if (resolved && cycles == this.patternCycles)
        {
            this.patternTiles[key] = tile;
        }
        else
        {
            this.patternCycles++;
        }

        return tile;
    }

    /// <summary>
    /// Builds a document from nodes that the resolve stage took out of the main paint order, such as a pattern tile. The
    /// nodes get their own transform table, so the content document does not depend on the transform table of the
    /// document that is still being resolved.
    /// </summary>
    /// <param name="nodes">The nodes. Their transform indexes are rewritten.</param>
    /// <param name="mappedBox">The rectangle of content space that maps into the destination of a render.</param>
    /// <param name="maxScopeDepth">The deepest scope nesting of the nodes.</param>
    /// <returns>The document. It maps its box with <c>preserveAspectRatio="none"</c>.</returns>
    private SvgDocument CreateContentDocument(SvgRenderNode[] nodes, RectangleF mappedBox, int maxScopeDepth)
    {
        Dictionary<int, int> map = [];
        List<Matrix3x2> transforms = [];
        for (int i = 0; i < nodes.Length; i++)
        {
            ref SvgRenderNode node = ref nodes[i];
            if (!map.TryGetValue(node.TransformIndex, out int index))
            {
                index = transforms.Count;
                transforms.Add(this.transforms[node.TransformIndex]);
                map.Add(node.TransformIndex, index);
            }

            node.TransformIndex = index;
        }

        return new SvgDocument(
            nodes,
            [.. transforms],
            mappedBox.Size,
            null,
            mappedBox,
            new SvgPreserveAspectRatio(SvgAlign.None, false),
            maxScopeDepth,
            this.loadOptions.Limits.MaxLayerPixels,
            this.TakeFilters(nodes));
    }

    /// <summary>
    /// The key of a shared pattern tile: what the resolved content depends on.
    /// </summary>
    private readonly struct SvgPatternTileKey : IEquatable<SvgPatternTileKey>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SvgPatternTileKey"/> struct.
        /// </summary>
        /// <param name="content">The <c>pattern</c> element whose children are the content.</param>
        /// <param name="contentTransform">The transform from the content coordinate system to tile space.</param>
        /// <param name="size">The tile size.</param>
        /// <param name="viewport">The viewport that percentages in the content refer to.</param>
        /// <param name="maxPixels">The pixel budget of one render of the tile, which depends on the pattern nesting.</param>
        public SvgPatternTileKey(int content, Matrix3x2 contentTransform, SizeF size, SizeF viewport, int maxPixels)
        {
            this.Content = content;
            this.ContentTransform = contentTransform;
            this.Size = size;
            this.Viewport = viewport;
            this.MaxPixels = maxPixels;
        }

        /// <summary>
        /// Gets the content element.
        /// </summary>
        public int Content { get; }

        /// <summary>
        /// Gets the content transform.
        /// </summary>
        public Matrix3x2 ContentTransform { get; }

        /// <summary>
        /// Gets the tile size.
        /// </summary>
        public SizeF Size { get; }

        /// <summary>
        /// Gets the content viewport.
        /// </summary>
        public SizeF Viewport { get; }

        /// <summary>
        /// Gets the pixel budget of one render.
        /// </summary>
        public int MaxPixels { get; }

        /// <inheritdoc/>
        public bool Equals(SvgPatternTileKey other)
            => this.Content == other.Content
            && this.ContentTransform == other.ContentTransform
            && this.Size == other.Size
            && this.Viewport == other.Viewport
            && this.MaxPixels == other.MaxPixels;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is SvgPatternTileKey other && this.Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(this.Content, this.ContentTransform, this.Size, this.Viewport, this.MaxPixels);
    }
}
