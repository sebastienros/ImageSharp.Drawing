// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The paint keywords <c>context-fill</c> and <c>context-stroke</c>.
/// </content>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// The context elements of the content being resolved, innermost last: the shapes whose markers resolve, and the
    /// <c>use</c> elements whose referenced content resolves. Created on first use, so documents without <c>use</c>
    /// elements and markers allocate nothing for it.
    /// </summary>
    private List<ContextElement>? contextElements;

    /// <summary>
    /// Makes a <c>use</c> element the context element of its referenced content.
    /// </summary>
    /// <remarks>
    /// The bounding box of the use element, which bounding box units of a context paint refer to, is computed only when
    /// such a paint needs it (see <see cref="GetContextBounds"/>), so <c>use</c> elements whose content uses no context
    /// paint cost nothing more. The caller ends the content with <see cref="ExitContextElement"/>.
    /// </remarks>
    /// <param name="use">The index of the <c>use</c> element.</param>
    /// <param name="context">The context of the <c>use</c> element, before its <c>x</c> and <c>y</c> translation.</param>
    private void EnterUseContext(int use, in SvgResolveContext context)
        => this.EnterContextElement(in context, default, use);

    /// <summary>
    /// Makes an element the context element of the content that resolves next.
    /// </summary>
    /// <remarks>
    /// The fill and the stroke of the context element are taken as they paint that element. When the element itself
    /// paints with a context keyword, the keyword resolves against the current context element first, so nested
    /// <c>use</c> elements and markers on shapes inside <c>use</c> content pass the outer paint on.
    /// </remarks>
    /// <param name="context">The context of the element: its computed style, transform, and viewport.</param>
    /// <param name="bounds">The fill bounding box of the element in its user space, when <paramref name="use"/> is -1.</param>
    /// <param name="use">The index of a <c>use</c> element whose bounding box is computed when needed, or -1.</param>
    private void EnterContextElement(in SvgResolveContext context, RectangleF bounds, int use)
    {
        List<ContextElement> elements = this.contextElements ??= [];
        ContextElement outer = elements.Count > 0 ? elements[^1] : default;
        SvgComputedStyle style = context.Style;
        elements.Add(new ContextElement(
            in context,
            bounds,
            use,
            ResolveContextSource(style.Fill, style.Color, in outer),
            ResolveContextSource(style.Stroke, style.Color, in outer)));
    }

    /// <summary>
    /// Returns the bounding box of the current context element, computing the box of a <c>use</c> element on first use.
    /// </summary>
    /// <returns>The box in the user space of the context element, or an empty box when it has none.</returns>
    private RectangleF GetContextBounds()
    {
        List<ContextElement> elements = this.contextElements!;
        ContextElement element = elements[^1];
        if (element.Use < 0)
        {
            return element.Bounds;
        }

        // The use element is on the reference chain while its content resolves, where the bounding box walk would see
        // it as a cycle, so it leaves the chain for the walk and returns to its place after it.
        int position = this.referenceChain.IndexOf(element.Use);
        if (position >= 0)
        {
            this.referenceChain.RemoveAt(position);
        }

        SvgComputedStyle style = element.Style;
        SvgResolveContext context = new(in style, element.Transform, element.Viewport, element.Depth);
        RectangleF bounds = this.TryGetObjectBoundingBox(element.Use, in context, out RectangleF box) ? box : default;
        if (position >= 0)
        {
            this.referenceChain.Insert(position, element.Use);
        }

        elements[^1] = element.WithBounds(bounds);
        return bounds;
    }

    /// <summary>
    /// Ends the content of the context element that
    /// <see cref="EnterContextElement(in SvgResolveContext, RectangleF, int)"/> or
    /// <see cref="EnterUseContext(int, in SvgResolveContext)"/> made current.
    /// </summary>
    private void ExitContextElement() => this.contextElements!.RemoveAt(this.contextElements.Count - 1);

    /// <summary>
    /// Resolves the paint of a context element that is itself a context keyword against the outer context element.
    /// </summary>
    /// <param name="paint">The computed paint of the context element.</param>
    /// <param name="color">The computed <c>color</c> of the context element.</param>
    /// <param name="outer">The outer context element.</param>
    /// <returns>The paint source.</returns>
    private static ContextSource ResolveContextSource(SvgPaint paint, Color color, in ContextElement outer)
    {
        if (paint.Kind is SvgPaintKind.ContextFill or SvgPaintKind.ContextStroke)
        {
            if (!outer.IsActive)
            {
                return default;
            }

            return paint.Kind == SvgPaintKind.ContextFill ? outer.Fill : outer.Stroke;
        }

        return new ContextSource(paint, color);
    }

    /// <summary>
    /// Resolves <c>context-fill</c> or <c>context-stroke</c> for a shape inside marker or <c>use</c> content.
    /// </summary>
    /// <remarks>
    /// The paint is the fill or the stroke paint of the context element, as SVG 2 defines; the opacity is the
    /// <c>fill-opacity</c> or <c>stroke-opacity</c> of the shape that uses the keyword. A gradient or pattern resolves in
    /// the user space of the context element, with its bounding box, and the brush is then mapped into the user space of
    /// the shape, so the paint lines up with the context element. Without a context element the paint is none.
    /// </remarks>
    /// <param name="kind"><see cref="SvgPaintKind.ContextFill"/> or <see cref="SvgPaintKind.ContextStroke"/>.</param>
    /// <param name="opacity">The fill or stroke opacity of the shape.</param>
    /// <param name="context">The context of the shape.</param>
    /// <returns>The brush in the user space of the shape, or <see langword="null"/> when nothing is painted.</returns>
    private Brush? ResolveContextPaint(SvgPaintKind kind, float opacity, in SvgResolveContext context)
    {
        if (this.contextElements is not { Count: > 0 } elements)
        {
            return null;
        }

        ContextElement element = elements[^1];

        ContextSource source = kind == SvgPaintKind.ContextFill ? element.Fill : element.Stroke;
        if (source.Paint.Kind is SvgPaintKind.ContextFill or SvgPaintKind.ContextStroke)
        {
            return null;
        }

        SvgComputedStyle style = element.Style;
        style.Color = source.Color;
        SvgResolveContext elementContext = new(in style, element.Transform, element.Viewport, context.Depth);

        // Only a paint server can use the bounding box. The source paint is never a context keyword, so the paint does
        // not come back here.
        RectangleF bounds = source.Paint.Kind == SvgPaintKind.Url ? this.GetContextBounds() : default;
        Brush? brush = this.ResolvePaint(source.Paint, opacity, bounds, in elementContext);
        if (brush is null or SolidBrush)
        {
            return brush;
        }

        // Map the brush from the user space of the context element into the user space of the shape.
        if (!Matrix3x2.Invert(context.Transform, out Matrix3x2 inverse))
        {
            return null;
        }

        Matrix3x2 elementToShape = element.Transform * inverse;
        return elementToShape.IsIdentity ? brush : brush.Transform(new Matrix4x4(elementToShape), default, default);
    }

    /// <summary>
    /// One paint of a context element.
    /// </summary>
    private readonly struct ContextSource
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ContextSource"/> struct.
        /// </summary>
        /// <param name="paint">The paint.</param>
        /// <param name="color">The computed <c>color</c> that <c>currentColor</c> in the paint refers to.</param>
        public ContextSource(SvgPaint paint, Color color)
        {
            this.Paint = paint;
            this.Color = color;
        }

        /// <summary>
        /// Gets the paint. A default value is none.
        /// </summary>
        public SvgPaint Paint { get; }

        /// <summary>
        /// Gets the computed <c>color</c> that <c>currentColor</c> in the paint refers to.
        /// </summary>
        public Color Color { get; }
    }

    /// <summary>
    /// The context element of marker or <c>use</c> content.
    /// </summary>
    private readonly struct ContextElement
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ContextElement"/> struct.
        /// </summary>
        /// <param name="context">The context of the element.</param>
        /// <param name="bounds">The fill bounding box of the element in its user space, when <paramref name="use"/> is -1.</param>
        /// <param name="use">The index of a <c>use</c> element whose bounding box is not computed yet, or -1.</param>
        /// <param name="fill">The fill of the element.</param>
        /// <param name="stroke">The stroke of the element.</param>
        public ContextElement(in SvgResolveContext context, RectangleF bounds, int use, ContextSource fill, ContextSource stroke)
        {
            this.IsActive = true;
            this.Style = context.Style;
            this.Transform = context.Transform;
            this.Viewport = context.Viewport;
            this.Depth = context.Depth;
            this.Bounds = bounds;
            this.Use = use;
            this.Fill = fill;
            this.Stroke = stroke;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContextElement"/> struct with the bounding box of another one.
        /// </summary>
        /// <param name="source">The context element.</param>
        /// <param name="bounds">The bounding box.</param>
        private ContextElement(in ContextElement source, RectangleF bounds)
        {
            this = source;
            this.Bounds = bounds;
            this.Use = -1;
        }

        /// <summary>
        /// Gets a value indicating whether there is a context element.
        /// </summary>
        public bool IsActive { get; }

        /// <summary>
        /// Gets the computed style of the element.
        /// </summary>
        public SvgComputedStyle Style { get; }

        /// <summary>
        /// Gets the transform from the user space of the element to document user space.
        /// </summary>
        public Matrix3x2 Transform { get; }

        /// <summary>
        /// Gets the viewport of the element.
        /// </summary>
        public SizeF Viewport { get; }

        /// <summary>
        /// Gets the depth of the element.
        /// </summary>
        public int Depth { get; }

        /// <summary>
        /// Gets the fill bounding box of the element in its user space, when <see cref="Use"/> is -1.
        /// </summary>
        public RectangleF Bounds { get; }

        /// <summary>
        /// Gets the index of a <c>use</c> element whose bounding box is not computed yet, or -1.
        /// </summary>
        public int Use { get; }

        /// <summary>
        /// Gets the fill of the element.
        /// </summary>
        public ContextSource Fill { get; }

        /// <summary>
        /// Gets the stroke of the element.
        /// </summary>
        public ContextSource Stroke { get; }

        /// <summary>
        /// Returns this context element with its bounding box computed.
        /// </summary>
        /// <param name="bounds">The bounding box.</param>
        /// <returns>The context element.</returns>
        public ContextElement WithBounds(RectangleF bounds) => new(in this, bounds);
    }
}
