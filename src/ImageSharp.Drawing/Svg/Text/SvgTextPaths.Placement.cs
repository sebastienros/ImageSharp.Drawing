// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// Package T2: text on a path, as Chromium lays it out (<c>SvgTextLayoutAlgorithm::PositionOnPath</c> in Blink).
/// </content>
/// <remarks>
/// <para>
/// The work has three steps. Before <c>textLength</c> and anchoring, <see cref="ResolvePathPositions"/> applies the
/// position rules of a <c>textPath</c> to the pen positions that stage 4 set. After <c>textLength</c>,
/// <see cref="SeparatePathChunks"/> ends the anchored text chunk of each <c>textPath</c> at its last character. After
/// anchoring, <see cref="PlaceOnPaths"/> maps the clusters onto their paths and moves the text after each path to its
/// end point.
/// </para>
/// <para>
/// Chromium choices, where SVG 2 or other renderers differ: only a <c>path</c> element can be referenced (no basic
/// shapes); the SVG 2 <c>path</c> attribute wins over <c>href</c> when its data is not empty; the transform of the
/// referenced element applies, the transforms of its ancestors do not; <c>method</c> and <c>spacing</c> have no effect;
/// a closed path does not wrap; a cluster is placed by the middle of its advance including <c>letter-spacing</c>, and
/// the <c>rotate</c> value turns it about that point on the path.
/// </para>
/// </remarks>
internal static partial class SvgTextPaths
{
    /// <summary>
    /// Returns whether the inline direction of a layout is horizontal: the <c>writing-mode</c> of the <c>text</c>
    /// element, as Chromium decides it for the whole element.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <returns><see langword="true"/> for horizontal text; otherwise, <see langword="false"/>.</returns>
    internal static bool IsHorizontal(SvgTextLayout layout)
        => layout.Content.Spans[0].Style.Text.WritingMode == SvgWritingMode.HorizontalTb;

    /// <summary>
    /// Applies the position rules of text on a path to the pen positions of stage 4, before <c>textLength</c> and
    /// anchoring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chromium starts each <c>textPath</c> at inline position 0 unless its first character has an inline position
    /// value of its own, and resets the accumulated <c>dx</c> and <c>dy</c> there. Inside a <c>textPath</c> the
    /// position values across the inline direction (<c>y</c> for horizontal text) are ignored, and the cross position
    /// restarts at 0, so it holds only the shifts and the baseline shift. The first character of a <c>textPath</c>
    /// starts a text chunk; inside it, only an inline position value of a <c>tspan</c> inside the <c>textPath</c> does.
    /// </para>
    /// <para>
    /// Stage 4 applies the position values without these rules. This method changes its positions by the difference
    /// between the two pen walks, so a baseline shift that stage 4 added to a cluster stays. Both walks advance the pen
    /// by the same advances, so the inline difference only changes where one of the walks sets the pen.
    /// </para>
    /// </remarks>
    /// <param name="layout">The positioned layout.</param>
    /// <param name="horizontal">Whether the inline direction is horizontal.</param>
    internal static void ResolvePathPositions(SvgTextLayout layout, bool horizontal)
    {
        SvgTextSpan[] spans = layout.Content.Spans;
        if (!HasTextPath(spans))
        {
            return;
        }

        Span<SvgTextCluster> clusters = layout.Clusters;
        SvgTextCharacter[] characters = layout.Content.Characters;
        bool[]? inlineFromPath = null;
        float inlineDelta = 0F;
        float blockStage4 = 0F;
        float blockPath = 0F;
        int previousPath = -1;

        for (int i = 0; i < clusters.Length; i++)
        {
            ref SvgTextCluster cluster = ref clusters[i];
            ref readonly SvgTextCharacter first = ref characters[cluster.CharStart];
            int path = spans[cluster.Span].TextPath;
            bool firstInPath = path >= 0 && path != previousPath;
            previousPath = path;

            bool hasInline = (first.Flags & (horizontal ? SvgTextCharacterFlags.HasX : SvgTextCharacterFlags.HasY)) != 0;
            bool hasBlock = (first.Flags & (horizontal ? SvgTextCharacterFlags.HasY : SvgTextCharacterFlags.HasX)) != 0;
            float inlineShift = horizontal ? first.Dx : first.Dy;
            float blockShift = horizontal ? first.Dy : first.Dx;
            float blockValue = horizontal ? first.Y : first.X;

            // Inline: both walks set the pen at an inline position value. At the start of a path without one, Chromium
            // sets the pen to 0, where stage 4 kept its pen: the position before this cluster's own shift.
            if (hasInline)
            {
                inlineDelta = 0F;
            }
            else if (firstInPath)
            {
                inlineDelta = -((horizontal ? cluster.X : cluster.Y) - inlineShift);
            }

            // Cross: replay both pen walks; they differ in the position values they take and where they restart.
            if (hasBlock)
            {
                blockStage4 = blockValue;
            }

            blockStage4 += blockShift;
            if (firstInPath)
            {
                blockPath = 0F;
            }
            else if (hasBlock && path < 0)
            {
                blockPath = blockValue;
            }

            blockPath += blockShift;
            float blockDelta = blockPath - blockStage4;
            cluster.X += horizontal ? inlineDelta : blockDelta;
            cluster.Y += horizontal ? blockDelta : inlineDelta;

            if (path >= 0)
            {
                inlineFromPath ??= hasInline ? GetInlineValuesInsidePaths(layout, horizontal) : null;
                cluster.StartsChunk = firstInPath || (hasInline && inlineFromPath![cluster.CharStart]);
            }
        }
    }

    /// <summary>
    /// Ends the anchored chunk of each <c>textPath</c> at its last cluster: the clusters after a path anchor as a chunk
    /// of their own, as Chromium scopes anchoring to the <c>textPath</c>. Runs after <c>textLength</c>, which shifts the
    /// clusters after an element up to the next real chunk start.
    /// </summary>
    /// <param name="layout">The layout.</param>
    internal static void SeparatePathChunks(SvgTextLayout layout)
    {
        SvgTextSpan[] spans = layout.Content.Spans;
        Span<SvgTextCluster> clusters = layout.Clusters;
        for (int i = 1; i < clusters.Length; i++)
        {
            if (spans[clusters[i - 1].Span].TextPath >= 0 && spans[clusters[i].Span].TextPath < 0)
            {
                clusters[i].StartsChunk = true;
            }
        }
    }

    /// <summary>
    /// Places the clusters on their paths, as the remarks of the type describe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cluster on a path takes the point at the distance of the middle of its advance plus the start offset. A cluster
    /// whose middle lies before the start or after the end of the path, or whose path is missing, is left out. The
    /// glyphs turn to the path direction plus the <c>rotate</c> value about that point, and the cross position (the
    /// shifts and the baseline shift) moves them perpendicular to the path.
    /// </para>
    /// <para>
    /// The clusters after a path move by the end point of the path minus the position of their first cluster, up to the
    /// next cluster with a position value of its own. After a missing path, Chromium moves them by the end of the last
    /// cluster of the text that is not left out instead.
    /// </para>
    /// </remarks>
    /// <param name="layout">The anchored layout.</param>
    /// <param name="resolver">The resolver.</param>
    /// <param name="placed">Set to <see langword="true"/>.</param>
    static partial void PlaceOnPaths(SvgTextLayout layout, SvgResolver resolver, ref bool placed)
    {
        placed = true;
        SvgTextSpan[] spans = layout.Content.Spans;
        if (!HasTextPath(spans))
        {
            return;
        }

        Span<SvgTextCluster> clusters = layout.Clusters;
        SvgTextCharacter[] characters = layout.Content.Characters;
        bool horizontal = IsHorizontal(layout);
        bool upward = spans[0].Style.Text.WritingMode == SvgWritingMode.SidewaysLr;
        SvgTextPathMeasure? measure = null;
        float startOffset = 0F;
        bool reverse = false;
        int pathElement = -1;
        bool inPath = false;
        bool afterPath = false;
        Vector2 pathEnd = default;

        for (int i = 0; i < clusters.Length; i++)
        {
            ref SvgTextCluster cluster = ref clusters[i];
            int path = spans[cluster.Span].TextPath;
            if (path < 0)
            {
                if (inPath)
                {
                    inPath = false;
                    afterPath = true;
                    Vector2 end = measure is not null
                        ? GetEndPoint(measure, reverse)
                        : GetLastClusterEnd(clusters[i..], horizontal, upward);
                    pathEnd = end - new Vector2(cluster.X, cluster.Y);
                }

                if (afterPath)
                {
                    // A position value starts a new chunk, which keeps its own position.
                    if ((characters[cluster.CharStart].Flags & (SvgTextCharacterFlags.HasX | SvgTextCharacterFlags.HasY)) != 0)
                    {
                        afterPath = false;
                    }
                    else
                    {
                        cluster.X += pathEnd.X;
                        cluster.Y += pathEnd.Y;
                    }
                }

                continue;
            }

            if (!inPath || path != pathElement)
            {
                pathElement = path;
                measure = CreateMeasure(layout, resolver, cluster.Span, out startOffset, out reverse);
            }

            inPath = true;
            float size = cluster.Width * cluster.ScaleX;
            float inlinePosition = horizontal ? cluster.X : upward ? -cluster.Y : cluster.Y;
            float middle = inlinePosition + (size / 2F) + startOffset;
            if (measure is null || !(middle >= 0F && middle <= measure.Length))
            {
                cluster.Skipped = true;
                continue;
            }

            // side="right" reverses the path: distances run from its end, and the direction turns by 180 degrees.
            float angle = reverse
                ? measure.GetPoint(measure.Length - middle, out Vector2 point) + 180F
                : measure.GetPoint(middle, out point);
            angle += cluster.Rotation;

            // The offset of the pen from the path point before the rotation: back by half the advance along the inline
            // direction, and across by the cross position. Vertical glyphs turn by a quarter turn less than the path.
            Vector2 local;
            if (horizontal)
            {
                local = new Vector2(-size / 2F, cluster.Y);
            }
            else if (upward)
            {
                angle += 90F;
                local = new Vector2(-cluster.X, size / 2F);
            }
            else
            {
                angle -= 90F;
                local = new Vector2(cluster.X, -size / 2F);
            }

            Vector2 pen = point + Vector2.Transform(local, Matrix3x2.CreateRotation(angle * (MathF.PI / 180F)));
            cluster.X = pen.X;
            cluster.Y = pen.Y;
            cluster.Rotation = angle;
        }
    }

    /// <summary>
    /// Returns whether a layout has text on a path.
    /// </summary>
    /// <param name="spans">The spans of the layout.</param>
    /// <returns><see langword="true"/> if a <c>textPath</c> holds characters; otherwise, <see langword="false"/>.</returns>
    private static bool HasTextPath(SvgTextSpan[] spans)
    {
        foreach (SvgTextSpan span in spans)
        {
            if (span.Kind == SvgElementKind.TextPath && span.CharEnd > span.CharStart)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Marks the characters whose inline position value comes from a <c>tspan</c> inside a <c>textPath</c>. Only such a
    /// value starts a text chunk inside a path.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="horizontal">Whether the inline direction is horizontal, so the inline list is <c>x</c>.</param>
    /// <returns>A flag per character.</returns>
    private static bool[] GetInlineValuesInsidePaths(SvgTextLayout layout, bool horizontal)
    {
        SvgTextContent content = layout.Content;
        bool[] marks = new bool[content.Characters.Length];
        SvgElementTree tree = layout.Resolver.Tree;
        foreach (SvgTextSpan span in content.Spans)
        {
            if (span.TextPath < 0 || span.Kind != SvgElementKind.TSpan)
            {
                continue;
            }

            ref readonly SvgTextAttributes attributes = ref tree.GetText(span.Element);
            int count = Math.Min(horizontal ? attributes.X.Count : attributes.Y.Count, span.AddressableEnd - span.AddressableStart);
            for (int c = span.CharStart; c < span.CharEnd && count > 0; c++)
            {
                if (content.Characters[c].IsAddressable)
                {
                    marks[c] = true;
                    count--;
                }
            }
        }

        return marks;
    }

    /// <summary>
    /// Measures the path of a <c>textPath</c>.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="resolver">The resolver.</param>
    /// <param name="spanIndex">The span of a cluster on the path: the <c>textPath</c> or a descendant.</param>
    /// <param name="startOffset">The start offset in user units.</param>
    /// <param name="reverse">Whether <c>side="right"</c> reverses the path.</param>
    /// <returns>The measure, or <see langword="null"/> when the <c>textPath</c> has no valid path.</returns>
    private static SvgTextPathMeasure? CreateMeasure(SvgTextLayout layout, SvgResolver resolver, int spanIndex, out float startOffset, out bool reverse)
    {
        SvgTextSpan[] spans = layout.Content.Spans;
        while (spans[spanIndex].Kind != SvgElementKind.TextPath)
        {
            spanIndex = spans[spanIndex].Parent;
        }

        SvgTextSpan span = spans[spanIndex];
        SvgElementTree tree = resolver.Tree;
        ref readonly SvgTextPathAttributes attributes = ref tree.GetTextPath(span.Element);
        float fontSize = span.Style.FontSize;

        // Glyph placement needs a precision relative to the glyph size.
        float tolerance = fontSize / 1024F;
        startOffset = 0F;
        reverse = attributes.Side == SvgTextPathSide.Right;

        // The path attribute wins unless its data is empty. Otherwise the reference must be a path element, whose own
        // transform maps the path into the user space of the text. Measures are shared through the load, so many
        // textPath elements on one long path measure it once.
        Dictionary<(int Element, float Tolerance, SizeF Viewport), SvgTextPathMeasure?> cache = resolver.TextState.PathMeasures;
        SvgTextPathMeasure? measure = null;
        if (attributes.Path is IPath inline && !cache.TryGetValue((span.Element, tolerance, layout.Viewport), out measure))
        {
            measure = SvgTextPathMeasure.Create(inline, Matrix3x2.Identity, tolerance);
            cache.Add((span.Element, tolerance, layout.Viewport), measure);
        }

        if (measure is null
            && attributes.Href is string id
            && tree.TryGetElementById(id, out int target)
            && tree[target].Kind == SvgElementKind.Path
            && tree.GetPath(target).Path is IPath referenced
            && !cache.TryGetValue((target, tolerance, layout.Viewport), out measure))
        {
            Matrix3x2 transform = resolver.TryGetLocalTransform(target, resolver.ComputeDocumentStyle(target), layout.Viewport, out Matrix3x2 local)
                ? local
                : Matrix3x2.Identity;
            measure = SvgTextPathMeasure.Create(referenced, transform, tolerance);
            cache.Add((target, tolerance, layout.Viewport), measure);
        }

        if (measure is not null)
        {
            // A percentage refers to the length of the path.
            startOffset = SvgUnits.ToUser(attributes.StartOffset, measure.Length, fontSize, resolver.Units, 0F);
        }

        return measure;
    }

    /// <summary>
    /// Returns the end point of a path, where the text after the path continues.
    /// </summary>
    /// <param name="measure">The measure of the path.</param>
    /// <param name="reverse">Whether the path is reversed, so that it ends at its start point.</param>
    /// <returns>The end point.</returns>
    private static Vector2 GetEndPoint(SvgTextPathMeasure measure, bool reverse)
    {
        _ = measure.GetPoint(reverse ? 0F : measure.Length, out Vector2 point);
        return point;
    }

    /// <summary>
    /// Returns the end of the last cluster that is not left out, as Chromium does for the text after a <c>textPath</c>
    /// without a valid path.
    /// </summary>
    /// <param name="clusters">The clusters from the first one after the path to the end of the text.</param>
    /// <param name="horizontal">Whether the inline direction is horizontal.</param>
    /// <param name="upward">Whether the inline direction is upward.</param>
    /// <returns>The pen position after the last cluster, or the origin when every cluster is left out.</returns>
    private static Vector2 GetLastClusterEnd(ReadOnlySpan<SvgTextCluster> clusters, bool horizontal, bool upward)
    {
        for (int i = clusters.Length - 1; i >= 0; i--)
        {
            ref readonly SvgTextCluster cluster = ref clusters[i];
            if (!cluster.Skipped)
            {
                float size = cluster.Width * cluster.ScaleX;
                return horizontal
                    ? new Vector2(cluster.X + size, cluster.Y)
                    : new Vector2(cluster.X, upward ? cluster.Y - size : cluster.Y + size);
            }
        }

        return Vector2.Zero;
    }
}
