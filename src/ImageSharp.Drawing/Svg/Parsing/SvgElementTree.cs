// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The output of <see cref="SvgReader"/>: every element of the document in document order, with typed geometry and
/// reference attributes, the id index, and one cascaded specified style per element.
/// </summary>
/// <remarks>
/// <para>
/// The tree is a set of flat tables. <see cref="Elements"/> holds one <see cref="SvgElement"/> per element; the root
/// <c>svg</c> element is at index 0. Attributes that only some kinds have live in one table per kind, and
/// <see cref="SvgElement.DataIndex"/> points into the table of the element kind. Use the accessor for the kind:
/// <see cref="GetViewport(int)"/> for <see cref="SvgElementKind.Svg"/> and <see cref="SvgElementKind.Symbol"/>,
/// <see cref="GetRect(int)"/>, <see cref="GetCircle(int)"/> for <see cref="SvgElementKind.Circle"/> and
/// <see cref="SvgElementKind.Ellipse"/>, <see cref="GetLine(int)"/>, <see cref="GetPoints(int)"/> for
/// <see cref="SvgElementKind.Polyline"/> and <see cref="SvgElementKind.Polygon"/>, <see cref="GetPath(int)"/>,
/// <see cref="GetUse(int)"/>, <see cref="GetGradient(int)"/> for both gradient kinds, and <see cref="GetStopOffset(int)"/>.
/// Calling an accessor for another kind is a caller error.
/// </para>
/// <para>
/// <see cref="GetStyle(int)"/> returns the cascaded specified style: presentation attributes, style sheet rules, and the
/// <c>style</c> attribute combined in cascade order. Inheritance is not applied, because content that <c>use</c>
/// references inherits from the <c>use</c> element, not from its parent in this tree; the resolve stage applies it.
/// </para>
/// <para>
/// The tree is not changed after the reader returns it, so concurrent reads are safe.
/// </para>
/// </remarks>
internal sealed class SvgElementTree
{
    private static readonly SvgSpecifiedStyle EmptyStyle;

    private readonly List<SvgElement> elements;
    private readonly List<string> classNames;
    private readonly List<Matrix3x2> transforms;
    private readonly List<SvgSpecifiedStyle> styles;
    private readonly List<SvgLength> dashLengths;
    private readonly List<SvgViewportAttributes> viewports;
    private readonly List<SvgRectAttributes> rects;
    private readonly List<SvgCircleAttributes> circles;
    private readonly List<SvgLineAttributes> lines;
    private readonly List<SvgPointsAttributes> pointRanges;
    private readonly List<PointF> points;
    private readonly List<SvgPathAttributes> paths;
    private readonly List<SvgUseAttributes> uses;
    private readonly List<SvgGradientAttributes> gradients;
    private readonly List<float> stopOffsets;
    private readonly Dictionary<string, int> ids;
    private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> idLookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgElementTree"/> class. The tree takes ownership of the tables.
    /// </summary>
    /// <param name="tables">The tables that the reader filled.</param>
    /// <param name="styleRuleCount">The number of style sheet rules that took part in the cascade.</param>
    internal SvgElementTree(SvgReader.Tables tables, int styleRuleCount)
    {
        this.elements = tables.Elements;
        this.classNames = tables.ClassNames;
        this.transforms = tables.Transforms;
        this.styles = tables.Styles;
        this.dashLengths = tables.DashLengths;
        this.viewports = tables.Viewports;
        this.rects = tables.Rects;
        this.circles = tables.Circles;
        this.lines = tables.Lines;
        this.pointRanges = tables.PointRanges;
        this.points = tables.Points;
        this.paths = tables.Paths;
        this.uses = tables.Uses;
        this.gradients = tables.Gradients;
        this.stopOffsets = tables.StopOffsets;
        this.ids = tables.Ids;
        this.idLookup = this.ids.GetAlternateLookup<ReadOnlySpan<char>>();
        this.StyleRuleCount = styleRuleCount;
    }

    /// <summary>
    /// Gets the number of elements.
    /// </summary>
    public int Count => this.elements.Count;

    /// <summary>
    /// Gets the elements in document order. The root <c>svg</c> element is at index 0.
    /// </summary>
    public ReadOnlySpan<SvgElement> Elements => CollectionsMarshal.AsSpan(this.elements);

    /// <summary>
    /// Gets the number of style sheet rules, one per selector, that took part in the cascade. Zero means the document has
    /// no usable <c>style</c> element rules and selector matching was skipped.
    /// </summary>
    public int StyleRuleCount { get; }

    /// <summary>
    /// Gets the element at an index.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The element.</returns>
    public ref readonly SvgElement this[int index] => ref CollectionsMarshal.AsSpan(this.elements)[index];

    /// <summary>
    /// Returns the first child of an element.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The index of the first child, or -1 when the element has no children.</returns>
    public int GetFirstChild(int index) => index + 1 < this[index].SubtreeEnd ? index + 1 : -1;

    /// <summary>
    /// Returns the next sibling of an element.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The index of the next sibling, or -1 when the element is the last child or the root.</returns>
    public int GetNextSibling(int index)
    {
        ref readonly SvgElement element = ref this[index];
        return element.Parent >= 0 && element.SubtreeEnd < this[element.Parent].SubtreeEnd ? element.SubtreeEnd : -1;
    }

    /// <summary>
    /// Finds an element by its <c>id</c>. When several elements share an id, the first in document order wins.
    /// </summary>
    /// <param name="id">The id, without a leading <c>#</c>.</param>
    /// <param name="index">When this method returns <see langword="true"/>, contains the element index.</param>
    /// <returns><see langword="true"/> if an element has the id; otherwise, <see langword="false"/>.</returns>
    public bool TryGetElementById(ReadOnlySpan<char> id, out int index) => this.idLookup.TryGetValue(id, out index);

    /// <summary>
    /// Returns the class names of an element, in attribute order.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The class names.</returns>
    public ReadOnlySpan<string> GetClassNames(int index)
    {
        ref readonly SvgElement element = ref this[index];
        return CollectionsMarshal.AsSpan(this.classNames).Slice(element.ClassStart, element.ClassCount);
    }

    /// <summary>
    /// Gets the <c>transform</c> attribute of an element.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="transform">When this method returns <see langword="true"/>, contains the transform.</param>
    /// <returns><see langword="true"/> if the element has a valid transform; otherwise, <see langword="false"/>.</returns>
    public bool TryGetTransform(int index, out Matrix3x2 transform)
    {
        int transformIndex = this[index].TransformIndex;
        if (transformIndex < 0)
        {
            transform = Matrix3x2.Identity;
            return false;
        }

        transform = this.transforms[transformIndex];
        return true;
    }

    /// <summary>
    /// Gets the cascaded specified style of an element. An element without specified properties returns an empty style.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The specified style.</returns>
    public ref readonly SvgSpecifiedStyle GetStyle(int index)
    {
        int styleIndex = this[index].StyleIndex;
        return ref styleIndex < 0 ? ref EmptyStyle : ref CollectionsMarshal.AsSpan(this.styles)[styleIndex];
    }

    /// <summary>
    /// Gets the lengths of a <c>stroke-dasharray</c> value.
    /// </summary>
    /// <param name="dashArray">The value from a style of this tree.</param>
    /// <returns>The lengths; empty for <c>none</c>.</returns>
    public ReadOnlySpan<SvgLength> GetDashArray(SvgDashArray dashArray)
        => CollectionsMarshal.AsSpan(this.dashLengths).Slice(dashArray.Start, dashArray.Count);

    /// <summary>
    /// Gets the attributes of an <c>svg</c> or <c>symbol</c> element.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The attributes.</returns>
    public ref readonly SvgViewportAttributes GetViewport(int index) => ref CollectionsMarshal.AsSpan(this.viewports)[this[index].DataIndex];

    /// <summary>
    /// Gets the attributes of a <c>rect</c> element.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The attributes.</returns>
    public ref readonly SvgRectAttributes GetRect(int index) => ref CollectionsMarshal.AsSpan(this.rects)[this[index].DataIndex];

    /// <summary>
    /// Gets the attributes of a <c>circle</c> or <c>ellipse</c> element.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The attributes.</returns>
    public ref readonly SvgCircleAttributes GetCircle(int index) => ref CollectionsMarshal.AsSpan(this.circles)[this[index].DataIndex];

    /// <summary>
    /// Gets the attributes of a <c>line</c> element.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The attributes.</returns>
    public ref readonly SvgLineAttributes GetLine(int index) => ref CollectionsMarshal.AsSpan(this.lines)[this[index].DataIndex];

    /// <summary>
    /// Gets the <c>points</c> attribute of a <c>polyline</c> or <c>polygon</c> element.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The attribute, with the range of its points.</returns>
    public ref readonly SvgPointsAttributes GetPointsAttributes(int index)
        => ref CollectionsMarshal.AsSpan(this.pointRanges)[this[index].DataIndex];

    /// <summary>
    /// Gets the points of a <c>polyline</c> or <c>polygon</c> element in user units.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The points; empty when <c>points</c> is missing or has no valid pair.</returns>
    public ReadOnlySpan<PointF> GetPoints(int index)
    {
        ref readonly SvgPointsAttributes range = ref this.GetPointsAttributes(index);
        return CollectionsMarshal.AsSpan(this.points).Slice(range.Start, range.Count);
    }

    /// <summary>
    /// Gets the attributes of a <c>path</c> element.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The attributes.</returns>
    public ref readonly SvgPathAttributes GetPath(int index) => ref CollectionsMarshal.AsSpan(this.paths)[this[index].DataIndex];

    /// <summary>
    /// Gets the attributes of a <c>use</c> element.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The attributes.</returns>
    public ref readonly SvgUseAttributes GetUse(int index) => ref CollectionsMarshal.AsSpan(this.uses)[this[index].DataIndex];

    /// <summary>
    /// Gets the attributes of a <c>linearGradient</c> or <c>radialGradient</c> element.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The attributes.</returns>
    public ref readonly SvgGradientAttributes GetGradient(int index) => ref CollectionsMarshal.AsSpan(this.gradients)[this[index].DataIndex];

    /// <summary>
    /// Gets the <c>offset</c> of a <c>stop</c> element as a fraction clamped to the range 0 to 1. A missing or invalid
    /// offset is 0. Making each offset at least the previous one is left to the resolve stage.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <returns>The offset.</returns>
    public float GetStopOffset(int index) => this.stopOffsets[this[index].DataIndex];
}
