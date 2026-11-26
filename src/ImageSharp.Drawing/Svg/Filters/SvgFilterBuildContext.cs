// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// What the create method of a filter primitive needs from the resolve stage: the element and its computed style, the
/// inputs that its names resolve to, and the conversion of lengths from <c>primitiveUnits</c> to user space.
/// </summary>
/// <remarks>
/// <see cref="SvgFilterBuilder"/> creates one context per filter and moves it from primitive to primitive. A create
/// method uses it only during the call.
/// </remarks>
internal sealed class SvgFilterBuildContext
{
    private readonly Dictionary<string, int> results = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterBuildContext"/> class.
    /// </summary>
    /// <param name="resolver">The resolver of the document.</param>
    /// <param name="tree">The element tree.</param>
    /// <param name="filterRegion">The filter region in the user space of the filtered element.</param>
    /// <param name="primitiveUnits">The <c>primitiveUnits</c> of the filter, never unspecified.</param>
    /// <param name="boundingBox">The object bounding box of the filtered element, or <see langword="null"/> when it has none.</param>
    /// <param name="viewport">The viewport that percentages in user space units refer to.</param>
    public SvgFilterBuildContext(
        SvgResolver resolver,
        SvgElementTree tree,
        RectangleF filterRegion,
        SvgCoordinateUnits primitiveUnits,
        RectangleF? boundingBox,
        SizeF viewport)
    {
        this.Resolver = resolver;
        this.Tree = tree;
        this.FilterRegion = filterRegion;
        this.PrimitiveUnits = primitiveUnits;
        this.BoundingBox = boundingBox;
        this.Viewport = viewport;
    }

    /// <summary>
    /// Gets the resolver of the document, for primitives that resolve content (<c>feImage</c>).
    /// </summary>
    public SvgResolver Resolver { get; }

    /// <summary>
    /// Gets the element tree.
    /// </summary>
    public SvgElementTree Tree { get; }

    /// <summary>
    /// Gets the filter region in the user space of the filtered element.
    /// </summary>
    public RectangleF FilterRegion { get; }

    /// <summary>
    /// Gets the <c>primitiveUnits</c> of the filter: <see cref="SvgCoordinateUnits.UserSpaceOnUse"/> or
    /// <see cref="SvgCoordinateUnits.ObjectBoundingBox"/>.
    /// </summary>
    public SvgCoordinateUnits PrimitiveUnits { get; }

    /// <summary>
    /// Gets the object bounding box of the filtered element in its user space, or <see langword="null"/> when it has none.
    /// With <c>primitiveUnits="objectBoundingBox"</c> the builder does not create primitives without a box.
    /// </summary>
    public RectangleF? BoundingBox { get; }

    /// <summary>
    /// Gets the viewport that percentages in <c>userSpaceOnUse</c> lengths refer to.
    /// </summary>
    public SizeF Viewport { get; }

    /// <summary>
    /// Gets the index of the primitive element that is being created.
    /// </summary>
    public int Element { get; private set; }

    /// <summary>
    /// Gets the kind of the primitive element that is being created.
    /// </summary>
    public SvgElementKind Kind { get; private set; }

    /// <summary>
    /// Gets the computed style of the primitive element. Its <c>color-interpolation-filters</c> inherits from the
    /// <c>filter</c> element.
    /// </summary>
    public SvgComputedStyle Style { get; private set; }

    /// <summary>
    /// Gets the index that the primitive being created will have in the graph.
    /// </summary>
    public int Index { get; private set; }

    /// <summary>
    /// Gets the typed attributes of the primitive element.
    /// </summary>
    public ref readonly SvgFilterPrimitiveAttributes Attributes => ref this.Tree.GetFilterPrimitive(this.Element);

    /// <summary>
    /// Finds an attribute of the primitive element.
    /// </summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the value.</param>
    /// <returns><see langword="true"/> if the element specifies the attribute; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue(SvgAttributeId attribute, out SvgFilterAttributeValue value)
        => this.Tree.TryGetFilterValue(this.Element, attribute, out value);

    /// <summary>
    /// Finds an attribute of another element, such as a child of the primitive (<c>feMergeNode</c>, a transfer function,
    /// or a light source).
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the value.</param>
    /// <returns><see langword="true"/> if the element specifies the attribute; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue(int element, SvgAttributeId attribute, out SvgFilterAttributeValue value)
        => this.Tree.TryGetFilterValue(element, attribute, out value);

    /// <summary>
    /// Gets the numbers of an attribute value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The numbers; empty when the value is not a list of numbers.</returns>
    public ReadOnlySpan<float> GetNumbers(in SvgFilterAttributeValue value) => this.Tree.GetFilterNumbers(in value);

    /// <summary>
    /// Resolves an input name (<c>in</c>, <c>in2</c>, or the <c>in</c> of an <c>feMergeNode</c>) to an input index.
    /// </summary>
    /// <remarks>
    /// <c>SourceGraphic</c> and <c>SourceAlpha</c> are the standard inputs. A name that matches the <c>result</c> of an
    /// earlier primitive refers to the most recent one. A missing name, and a name that matches nothing, are the result of
    /// the previous primitive, or <c>SourceGraphic</c> for the first one: Filter Effects treats a reference to a
    /// non-existent result as if no result was specified. <c>BackgroundImage</c>, <c>BackgroundAlpha</c>,
    /// <c>FillPaint</c>, and <c>StrokePaint</c> are not supported and resolve like a name that matches nothing, as in
    /// Chromium.
    /// </remarks>
    /// <param name="name">The trimmed name, or <see langword="null"/> when the attribute is missing.</param>
    /// <returns>The input index: an earlier primitive, <see cref="SvgFilterPrimitive.SourceGraphic"/>, or <see cref="SvgFilterPrimitive.SourceAlpha"/>.</returns>
    public int GetInput(string? name)
    {
        if (name == "SourceGraphic")
        {
            return SvgFilterPrimitive.SourceGraphic;
        }

        if (name == "SourceAlpha")
        {
            return SvgFilterPrimitive.SourceAlpha;
        }

        if (name is not null && this.results.TryGetValue(name, out int index))
        {
            return index;
        }

        return this.Index > 0 ? this.Index - 1 : SvgFilterPrimitive.SourceGraphic;
    }

    /// <summary>
    /// Converts a horizontal length of a primitive attribute (such as <c>dx</c> or the first <c>stdDeviation</c>) from
    /// <c>primitiveUnits</c> to user space: unchanged for <c>userSpaceOnUse</c>, a fraction of the box width for
    /// <c>objectBoundingBox</c>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The length in user units.</returns>
    public float ToUserX(float value)
        => this.PrimitiveUnits == SvgCoordinateUnits.ObjectBoundingBox ? value * this.BoundingBox!.Value.Width : value;

    /// <summary>
    /// Converts a vertical length of a primitive attribute from <c>primitiveUnits</c> to user space; see <see cref="ToUserX"/>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The length in user units.</returns>
    public float ToUserY(float value)
        => this.PrimitiveUnits == SvgCoordinateUnits.ObjectBoundingBox ? value * this.BoundingBox!.Value.Height : value;

    /// <summary>
    /// Converts a point of a primitive attribute (such as a light position) from <c>primitiveUnits</c> to user space:
    /// unchanged for <c>userSpaceOnUse</c>, a point of the unit square mapped to the box for <c>objectBoundingBox</c>.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The point in user space.</returns>
    public Vector2 ToUserPoint(float x, float y)
    {
        if (this.PrimitiveUnits != SvgCoordinateUnits.ObjectBoundingBox)
        {
            return new Vector2(x, y);
        }

        RectangleF box = this.BoundingBox!.Value;
        return new Vector2(box.X + (x * box.Width), box.Y + (y * box.Height));
    }

    /// <summary>
    /// Resolves a color property of the primitive (<c>flood-color</c>, <c>lighting-color</c>, a color of
    /// <c>drop-shadow</c>): <c>currentColor</c> takes the computed <c>color</c> of the primitive element.
    /// </summary>
    /// <param name="paint">A paint of kind <see cref="SvgPaintKind.Color"/> or <see cref="SvgPaintKind.CurrentColor"/>.</param>
    /// <returns>The color.</returns>
    public Color ResolveColor(SvgPaint paint) => paint.Kind == SvgPaintKind.CurrentColor ? this.Style.Color : paint.Color;

    /// <summary>
    /// Moves the context to the next primitive element. Only the builder calls this.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <param name="index">The index that the primitive will have in the graph.</param>
    internal void MoveTo(int element, in SvgComputedStyle style, int index)
    {
        this.Element = element;
        this.Kind = this.Tree[element].Kind;
        this.Style = style;
        this.Index = index;
    }

    /// <summary>
    /// Records the <c>result</c> name of the primitive that was just added to the graph. Only the builder calls this.
    /// </summary>
    /// <param name="name">The name, or <see langword="null"/>.</param>
    /// <param name="index">The index of the primitive in the graph.</param>
    internal void SetResult(string? name, int index)
    {
        if (name is not null)
        {
            this.results[name] = index;
        }
    }
}
