// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The laid out text of one <c>text</c> element, and the driver of the layout pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Create"/> runs the stages in order, each in its own type:
/// </para>
/// <list type="number">
/// <item><description><see cref="SvgTextCharacters"/>: characters, spans, and position values
/// (<see cref="SvgTextContent"/>).</description></item>
/// <item><description><see cref="SvgFontMatcher"/> and <see cref="SvgFontChain"/>: the face of every grapheme.</description></item>
/// <item><description><see cref="SvgTextShaper"/>: glyphs and clusters (<see cref="SvgShapedGlyph"/>,
/// <see cref="SvgTextCluster"/>).</description></item>
/// <item><description><see cref="SvgTextPositioner"/>: pen positions, rotations, and spacing; the hooks of T2
/// (<c>textLength</c>), T3 (baselines), and T4 (vertical, bidi).</description></item>
/// <item><description><see cref="SvgTextAnchoring"/>: <c>text-anchor</c> per text chunk.</description></item>
/// <item><description><see cref="SvgTextPaths"/>: <c>textPath</c>, the hook of T2.</description></item>
/// <item><description>The resolver turns the clusters into draw nodes (<c>SvgResolver.Text.cs</c>), with the
/// decoration hook of T3.</description></item>
/// </list>
/// <para>
/// The layout is in the user space of the <c>text</c> element. It depends on the computed styles of the subtree, the
/// viewport, and the fonts, so the resolver caches it per element and per inherited text style.
/// </para>
/// </remarks>
internal sealed class SvgTextLayout
{
    private readonly SvgTextCluster[] clusters;
    private readonly SvgShapedGlyph[] glyphs;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextLayout"/> class.
    /// </summary>
    /// <param name="content">The characters.</param>
    /// <param name="clusters">The clusters in logical order.</param>
    /// <param name="glyphs">The glyphs in logical order.</param>
    private SvgTextLayout(SvgTextContent content, SvgTextCluster[] clusters, SvgShapedGlyph[] glyphs)
    {
        this.Content = content;
        this.clusters = clusters;
        this.glyphs = glyphs;
    }

    /// <summary>
    /// Gets the characters, spans, and position values from stage 1.
    /// </summary>
    public SvgTextContent Content { get; }

    /// <summary>
    /// Gets the clusters in logical order. Stages 4 to 6 change their positions in place.
    /// </summary>
    public Span<SvgTextCluster> Clusters => this.clusters;

    /// <summary>
    /// Gets the glyphs in logical order; <see cref="SvgTextCluster.GlyphStart"/> indexes into them.
    /// </summary>
    public ReadOnlySpan<SvgShapedGlyph> Glyphs => this.glyphs;

    /// <summary>
    /// Gets the object bounding box of the text in its user space: the union of the cells of the clusters that are not
    /// left out, each the advance by the ascent and descent of its font, turned by its rotation. <see langword="null"/>
    /// when no cluster has a cell.
    /// </summary>
    public RectangleF? Bounds { get; private set; }

    /// <summary>
    /// Gets the font matcher of the load. Stages after shaping use it for the fonts of spans, such as the metrics of the
    /// element that sets <c>baseline-shift</c>.
    /// </summary>
    public SvgFontMatcher FontMatcher { get; private init; } = null!;

    /// <summary>
    /// Lays out a <c>text</c> element.
    /// </summary>
    /// <param name="resolver">The resolver, for the styles of the children and the referenced elements.</param>
    /// <param name="element">The index of the <c>text</c> element.</param>
    /// <param name="style">The computed style of the <c>text</c> element.</param>
    /// <param name="viewport">The viewport that percentages refer to.</param>
    /// <param name="matcher">The font matcher of the load.</param>
    /// <returns>The layout.</returns>
    public static SvgTextLayout Create(SvgResolver resolver, int element, in SvgComputedStyle style, SizeF viewport, SvgFontMatcher matcher)
    {
        SvgTextContent content = SvgTextCharacters.Build(resolver, element, in style, viewport);
        SvgShapedGlyph[] glyphs = [];
        SvgTextCluster[] clusters = [];
        if (content.Text.Length > 0)
        {
            clusters = SvgTextShaper.Shape(content, matcher, out glyphs);
        }

        SvgTextLayout layout = new(content, clusters, glyphs) { FontMatcher = matcher };
        SvgTextPositioner.Position(layout);
        SvgTextAnchoring.Apply(layout);
        SvgTextPaths.Apply(layout, resolver);
        layout.Bounds = layout.ComputeBounds(0, int.MaxValue);
        return layout;
    }

    /// <summary>
    /// Returns the bounding box of the clusters of a span and its descendants, in the user space of the text.
    /// </summary>
    /// <param name="span">The span index.</param>
    /// <returns>The box, or <see langword="null"/> when no cluster of the span has a cell.</returns>
    public RectangleF? GetSpanBounds(int span)
    {
        SvgTextSpan value = this.Content.Spans[span];
        return this.ComputeBounds(value.CharStart, value.CharEnd);
    }

    /// <summary>
    /// Returns the transform that places a glyph of a cluster: from the glyph outline, whose origin is the glyph origin,
    /// to the user space of the text.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="glyphIndex">The index of the glyph in <see cref="Glyphs"/>.</param>
    /// <returns>The transform.</returns>
    public Matrix3x2 GetGlyphTransform(in SvgTextCluster cluster, int glyphIndex)
    {
        // The glyphs of a cluster follow each other from the pen position by their advances; each adds its own offset.
        float x = 0F;
        for (int i = cluster.GlyphStart; i < glyphIndex; i++)
        {
            x += this.glyphs[i].Advance;
        }

        Vector2 offset = this.glyphs[glyphIndex].Offset;
        Matrix3x2 transform = Matrix3x2.CreateTranslation(x + offset.X, offset.Y);
        if (cluster.ScaleX != 1F)
        {
            transform *= Matrix3x2.CreateScale(cluster.ScaleX, 1F);
        }

        if (cluster.Rotation != 0F)
        {
            transform *= Matrix3x2.CreateRotation(cluster.Rotation * (MathF.PI / 180F));
        }

        return transform * Matrix3x2.CreateTranslation(cluster.X, cluster.Y);
    }

    /// <summary>
    /// Computes the union of the cells of the clusters whose first character lies in a range.
    /// </summary>
    /// <param name="charStart">The first character.</param>
    /// <param name="charEnd">The character after the range.</param>
    /// <returns>The union, or <see langword="null"/> when no cluster has a cell.</returns>
    private RectangleF? ComputeBounds(int charStart, int charEnd)
    {
        RectangleF? bounds = null;
        foreach (ref readonly SvgTextCluster cluster in this.clusters.AsSpan())
        {
            if (cluster.Skipped || cluster.Font is null || cluster.CharStart < charStart || cluster.CharStart >= charEnd)
            {
                continue;
            }

            RectangleF cell = GetCell(in cluster);
            bounds = bounds is RectangleF union ? RectangleF.Union(union, cell) : cell;
        }

        return bounds;
    }

    /// <summary>
    /// Returns the cell of a cluster in the user space of the text: its advance along the baseline by the ascent and
    /// descent of its font, turned about the pen position.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <returns>The bounding box of the cell.</returns>
    private static RectangleF GetCell(in SvgTextCluster cluster)
    {
        Font font = cluster.Font!;
        FontMetrics metrics = font.FontMetrics;
        float scale = font.Size / metrics.UnitsPerEm;
        float ascent = metrics.HorizontalMetrics.Ascender * scale;
        float descent = -metrics.HorizontalMetrics.Descender * scale;
        RectangleF cell = new(0F, -ascent, cluster.Width * cluster.ScaleX, ascent + descent);

        Matrix3x2 transform = Matrix3x2.CreateTranslation(cluster.X, cluster.Y);
        if (cluster.Rotation != 0F)
        {
            transform = Matrix3x2.CreateRotation(cluster.Rotation * (MathF.PI / 180F)) * transform;
        }

        Vector2 a = Vector2.Transform(new Vector2(cell.Left, cell.Top), transform);
        Vector2 b = Vector2.Transform(new Vector2(cell.Right, cell.Top), transform);
        Vector2 c = Vector2.Transform(new Vector2(cell.Left, cell.Bottom), transform);
        Vector2 d = Vector2.Transform(new Vector2(cell.Right, cell.Bottom), transform);
        Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        return RectangleF.FromLTRB(min.X, min.Y, max.X, max.Y);
    }
}
