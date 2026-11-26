// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Stage 4: <c>textLength</c> and <c>lengthAdjust</c>, after the "apply textLength attribute" step of the SVG 2 text
/// layout algorithm (SVG 2, 11.5.1).
/// </summary>
/// <remarks>
/// <para>
/// Elements resolve from the innermost outward, so a descendant with its own <c>textLength</c> (a resolved descendant)
/// is final when its ancestor resolves, and the ancestor treats it as one typographic character. For each element the
/// step measures the extent of its clusters along the inline axis, <c>(b - a)</c>, and spreads the difference
/// <c>delta = textLength - (b - a)</c>:
/// </para>
/// <list type="bullet">
/// <item><description><c>spacing</c>: as SVG 2 writes the step, with <c>n</c> the number of typographic characters
/// (each resolved descendant counts as one) minus one, every cluster moves by <c>delta / n</c> times the number of
/// characters before it, and a resolved descendant takes one such share after its first cluster. An element with a
/// single character does not change.</description></item>
/// <item><description><c>spacingAndGlyphs</c>: the clusters that the element controls scale along the inline axis by
/// <c>(textLength - u) / ((b - a) - u)</c>, with <c>u</c> the extent of the resolved descendants, which move without
/// scaling. SVG 2 leaves the method to the user agent; the scale about the start of the text matches the Chromium
/// oracle.</description></item>
/// </list>
/// <para>
/// SVG 2 applies <c>textLength</c> before the absolute positions of <c>x</c> and <c>y</c>. Stage 4 has already applied
/// them, so the extent is measured on a virtual line without them: inside a text chunk the clusters keep their
/// relative positions, and each later chunk of the element starts where the advances and <c>dx</c> shifts before it
/// end. Each chunk then keeps its start: a cluster moves by its shift minus the shift of the first cluster of its
/// chunk. The text after the element moves by the shift after its last cluster, up to the next text chunk, as in the
/// Chromium oracle (for <c>spacing</c> that shift counts the last cluster too). SVG 2 says only that user agents must
/// shift the last character by <c>delta</c> and may adjust the others; these choices were decided from rendered
/// output (<c>text/textLength/on-text-and-tspan</c> of the resvg suite for the virtual line).
/// </para>
/// <para>
/// Bidirectional and vertical text: the step works on the visual positions of stage 4. Within a chunk the clusters take
/// their shares in visual order along the direction of the line, rightward in left-to-right text, leftward in
/// right-to-left text, downward in vertical text, and upward in <c>sideways-lr</c> text, so the start of the chunk stays
/// and the characters never cross. Percentages refer to the width of the viewport, also in vertical text, as in the
/// Chromium oracle. Zero, negative, and invalid values have no effect.
/// </para>
/// </remarks>
internal static partial class SvgTextPositioner
{
    /// <inheritdoc cref="ApplyTextLength"/>
    static partial void ApplyTextLength(SvgTextLayout layout)
    {
        Span<SvgTextCluster> clusters = layout.Clusters;
        if (clusters.IsEmpty)
        {
            return;
        }

        SvgTextSpan[] spans = layout.Content.Spans;
        TextLengthResolver? resolver = null;

        // Spans are in preorder, so walking them backward resolves every descendant before its ancestors.
        for (int s = spans.Length - 1; s >= 0; s--)
        {
            SvgTextSpan span = spans[s];
            if (!TryGetTextLength(layout, span, out float length, out SvgLengthAdjust adjust))
            {
                continue;
            }

            // The clusters are in logical order with ascending first characters; a span holds a consecutive run.
            int first = FindFirstCluster(clusters, span.CharStart);
            int last = first;
            while (last < clusters.Length && clusters[last].CharStart < span.CharEnd)
            {
                last++;
            }

            if (last == first)
            {
                continue;
            }

            resolver ??= new TextLengthResolver(layout);
            resolver.Resolve(s, first, last, length, adjust);
        }
    }

    /// <summary>
    /// Returns the valid <c>textLength</c> of a span in user units and its <c>lengthAdjust</c>.
    /// </summary>
    /// <param name="layout">The layout, for the viewport and the units.</param>
    /// <param name="span">The span.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the length.</param>
    /// <param name="adjust">When this method returns <see langword="true"/>, contains the adjust method.</param>
    /// <returns><see langword="true"/> if the span has a positive, finite <c>textLength</c>; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetTextLength(SvgTextLayout layout, SvgTextSpan span, out float length, out SvgLengthAdjust adjust)
    {
        SvgElementTree tree = layout.Resolver.Tree;
        SvgLength value;
        switch (span.Kind)
        {
            case SvgElementKind.Text:
            case SvgElementKind.TSpan:
            case SvgElementKind.Tref:
            {
                ref readonly SvgTextAttributes attributes = ref tree.GetText(span.Element);
                value = attributes.TextLength;
                adjust = attributes.LengthAdjust;
                break;
            }

            case SvgElementKind.TextPath:
            {
                ref readonly SvgTextPathAttributes attributes = ref tree.GetTextPath(span.Element);
                value = attributes.TextLength;
                adjust = attributes.LengthAdjust;
                break;
            }

            default:
                length = 0F;
                adjust = default;
                return false;
        }

        length = SvgUnits.ToUser(value, layout.Viewport.Width, span.Style.FontSize, layout.Resolver.Units, 0F);
        return length > 0F && float.IsFinite(length);
    }

    /// <summary>
    /// Returns the index of the first cluster whose first character is at or after a character.
    /// </summary>
    /// <param name="clusters">The clusters in logical order.</param>
    /// <param name="charStart">The character.</param>
    /// <returns>The cluster index, or the cluster count when no cluster starts there or later.</returns>
    private static int FindFirstCluster(ReadOnlySpan<SvgTextCluster> clusters, int charStart)
    {
        int low = 0;
        int high = clusters.Length;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (clusters[middle].CharStart < charStart)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// Resolves the <c>textLength</c> of the elements of one layout, with scratch arrays that all elements share, so an
    /// element costs O(m log m) for its m clusters and no allocation.
    /// </summary>
    private sealed class TextLengthResolver
    {
        private readonly SvgTextLayout layout;
        private readonly bool vertical;
        private readonly float direction;

        // Per cluster: the outermost resolved span that holds it, or -1; its start along the line direction; its
        // position on the virtual line of the element; its shift; and the visit order.
        private readonly int[] owner;
        private readonly float[] start;
        private readonly float[] line;
        private readonly float[] shift;
        private readonly int[] order;

        // Per span, for the resolved descendants of the element being resolved: the element that last used the entry,
        // whether the visit reached the descendant, its extent on the virtual line, and its shift.
        private readonly int[] unitStamp;
        private readonly int[] unitVisit;
        private readonly float[] unitLow;
        private readonly float[] unitHigh;
        private readonly float[] unitShift;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextLengthResolver"/> class.
        /// </summary>
        /// <param name="layout">The positioned layout.</param>
        public TextLengthResolver(SvgTextLayout layout)
        {
            this.layout = layout;
            SvgTextContent content = layout.Content;
            SvgTextDirections? directions = content.Directions;
            this.vertical = directions?.IsVertical == true;

            // The sign of the line direction along the inline axis: the pen of right-to-left text moves left from the
            // start of a chunk, and that of sideways-lr text moves up.
            this.direction = this.vertical
                ? directions!.WritingMode == SvgWritingMode.SidewaysLr ? -1F : 1F
                : content.Spans[0].Style.Text.Direction == SvgDirection.Rtl ? -1F : 1F;

            int count = layout.Clusters.Length;
            this.owner = new int[count];
            this.owner.AsSpan().Fill(-1);
            this.start = new float[count];
            this.line = new float[count];
            this.shift = new float[count];
            this.order = new int[count];

            int spans = content.Spans.Length;
            this.unitStamp = new int[spans];
            this.unitStamp.AsSpan().Fill(-1);
            this.unitVisit = new int[spans];
            this.unitVisit.AsSpan().Fill(-1);
            this.unitLow = new float[spans];
            this.unitHigh = new float[spans];
            this.unitShift = new float[spans];
        }

        /// <summary>
        /// Resolves the <c>textLength</c> of one element and marks its clusters as one resolved descendant for its
        /// ancestors.
        /// </summary>
        /// <param name="element">The span index of the element.</param>
        /// <param name="first">The first cluster of the element.</param>
        /// <param name="last">The cluster after the element.</param>
        /// <param name="length">The <c>textLength</c> in user units.</param>
        /// <param name="adjust">The <c>lengthAdjust</c> of the element.</param>
        public void Resolve(int element, int first, int last, float length, SvgLengthAdjust adjust)
        {
            this.Adjust(element, first, last, length, adjust);
            this.owner.AsSpan(first, last - first).Fill(element);
        }

        /// <summary>
        /// Moves, and for <c>spacingAndGlyphs</c> scales, the clusters of one element and the text after it.
        /// </summary>
        /// <param name="element">The span index of the element.</param>
        /// <param name="first">The first cluster of the element.</param>
        /// <param name="last">The cluster after the element.</param>
        /// <param name="length">The <c>textLength</c> in user units.</param>
        /// <param name="adjust">The <c>lengthAdjust</c> of the element.</param>
        private void Adjust(int element, int first, int last, float length, SvgLengthAdjust adjust)
        {
            Span<SvgTextCluster> clusters = this.layout.Clusters;
            float direction = this.direction;

            // Pass 1: the position of every cluster on the virtual line of the element, the single line of SVG 2 on which
            // no absolute position applies, and the extent (b - a) on it. A cluster spans [start, start + |advance|] in
            // the direction of the line, wherever its pen lies. Inside a chunk the clusters keep their positions
            // relative to each other, with the shifts of resolved descendants. A chunk after the first starts on the
            // virtual line where the advances and dx shifts before it in the element end: the shifts that a resolved
            // descendant gives the text after it stop at the end of its chunk, as in the Chromium oracle.
            SvgTextCharacter[] characters = this.layout.Content.Characters;
            float virtualPen = 0F;
            float lineLow = float.MaxValue;
            float lineHigh = float.MinValue;
            int pieceStart = first;
            while (pieceStart < last)
            {
                int pieceEnd = pieceStart + 1;
                while (pieceEnd < last && !clusters[pieceEnd].StartsChunk)
                {
                    pieceEnd++;
                }

                float low = float.MaxValue;
                for (int k = pieceStart; k < pieceEnd; k++)
                {
                    ref readonly SvgTextCluster cluster = ref clusters[k];
                    float position = (this.vertical ? cluster.Y : cluster.X) * direction;
                    float end = position + (cluster.Width * cluster.ScaleX * direction);
                    this.start[k] = MathF.Min(position, end);
                    low = MathF.Min(low, this.start[k]);
                }

                float origin = pieceStart == first ? 0F : virtualPen + (this.GetShift(characters, clusters[pieceStart]) * direction);
                for (int k = pieceStart; k < pieceEnd; k++)
                {
                    ref readonly SvgTextCluster cluster = ref clusters[k];
                    float advance = MathF.Abs(cluster.Width * cluster.ScaleX);
                    this.line[k] = origin + (this.start[k] - low);
                    lineLow = MathF.Min(lineLow, this.line[k]);
                    lineHigh = MathF.Max(lineHigh, this.line[k] + advance);
                    virtualPen += advance + (k == first ? 0F : this.GetShift(characters, cluster) * direction);
                }

                // The visit order: the clusters of the chunk in the direction of the line. Text without bidirectional
                // reordering is already in that order and skips the sort.
                Span<int> visit = this.order.AsSpan(pieceStart, pieceEnd - pieceStart);
                bool sorted = true;
                for (int k = pieceStart; k < pieceEnd; k++)
                {
                    visit[k - pieceStart] = k;
                    sorted &= k == pieceStart || this.start[k] >= this.start[k - 1];
                }

                if (!sorted)
                {
                    visit.Sort(new StartComparer(this.start));
                }

                pieceStart = pieceEnd;
            }

            // The virtual line starts at 0, the start of the text that spacingAndGlyphs scales about.
            float extent = lineHigh - lineLow;
            for (int k = first; k < last; k++)
            {
                this.line[k] -= lineLow;
            }

            // The resolved descendants: their number and their extents on the virtual line.
            int own = 0;
            int units = 0;
            float unitExtent = 0F;
            for (int k = first; k < last; k++)
            {
                int unit = this.owner[k];
                if (unit < 0)
                {
                    own++;
                    continue;
                }

                float end = this.line[k] + MathF.Abs(clusters[k].Width * clusters[k].ScaleX);
                if (this.unitStamp[unit] != element)
                {
                    this.unitStamp[unit] = element;
                    this.unitLow[unit] = this.line[k];
                    this.unitHigh[unit] = end;
                    units++;
                }
                else
                {
                    this.unitLow[unit] = MathF.Min(this.unitLow[unit], this.line[k]);
                    this.unitHigh[unit] = MathF.Max(this.unitHigh[unit], end);
                }
            }

            for (int k = first; k < last; k++)
            {
                int unit = this.owner[k];
                if (unit >= 0 && this.unitVisit[unit] != element)
                {
                    this.unitVisit[unit] = element;
                    unitExtent += this.unitHigh[unit] - this.unitLow[unit];
                }
            }

            float delta = length - extent;
            bool glyphs = adjust == SvgLengthAdjust.SpacingAndGlyphs;
            float share = 0F;
            float scale = 1F;
            if (glyphs)
            {
                // The clusters of the element itself scale; resolved descendants keep their size.
                float ownExtent = extent - unitExtent;
                scale = (length - unitExtent) / ownExtent;
                if (!(ownExtent > 0F) || !(scale > 0F) || !float.IsFinite(scale))
                {
                    return;
                }
            }
            else
            {
                int gaps = own + units - 1;
                if (gaps <= 0)
                {
                    return;
                }

                share = delta / gaps;
            }

            // Pass 2: the shift of every cluster in visit order. The shift accumulates over the chunks of the element,
            // as SVG 2 spreads it before the absolute positions apply; unitVisit marks the descendants already visited.
            float total = 0F;
            float unitsBefore = 0F;
            for (int i = first; i < last; i++)
            {
                int k = this.order[i];
                int unit = this.owner[k];
                bool firstOfUnit = unit >= 0 && this.unitVisit[unit] == element;
                if (firstOfUnit)
                {
                    this.unitVisit[unit] = -2 - element;
                }

                if (glyphs)
                {
                    if (unit < 0)
                    {
                        this.shift[k] = (scale - 1F) * (this.line[k] - unitsBefore);
                    }
                    else
                    {
                        if (firstOfUnit)
                        {
                            this.unitShift[unit] = (scale - 1F) * (this.unitLow[unit] - unitsBefore);
                            unitsBefore += this.unitHigh[unit] - this.unitLow[unit];
                        }

                        this.shift[k] = this.unitShift[unit];
                    }
                }
                else
                {
                    this.shift[k] = total;
                    if (unit < 0 || firstOfUnit)
                    {
                        total += share;
                    }
                }
            }

            float after = glyphs ? delta : total;
            if (!float.IsFinite(after))
            {
                return;
            }

            // Pass 3: every chunk keeps its start, which is the first cluster that the visit reached, and the text after
            // the element moves by the shift after it, up to the next chunk.
            float reference = 0F;
            for (int i = first; i < last; i++)
            {
                if (i == first || clusters[i].StartsChunk)
                {
                    reference = this.shift[this.order[i]];
                }

                // Pass 2 visited the chunk from its first position in the order array, so the first entry of the chunk
                // in that array is its start.
                int k = this.order[i];
                ref SvgTextCluster cluster = ref clusters[k];
                float move = this.shift[k] - reference;
                if (glyphs && this.owner[k] < 0)
                {
                    // The cluster scales about its pen. Its start along the line direction moves by the shift; where
                    // the glyphs extend against the line direction from the pen, the pen lies at the far end.
                    cluster.ScaleX *= scale;
                    float advance = cluster.Width * cluster.ScaleX * direction;
                    float position = (this.start[k] + move - MathF.Min(advance, 0F)) * direction;
                    this.SetPosition(ref cluster, position);
                }
                else
                {
                    this.Move(ref cluster, move * direction);
                }
            }

            for (int k = last; k < clusters.Length && !clusters[k].StartsChunk; k++)
            {
                this.Move(ref clusters[k], (after - reference) * direction);
            }
        }

        /// <summary>
        /// Returns the shift of a cluster along the inline axis: the <c>dx</c> value of its first character in horizontal
        /// text, the <c>dy</c> value in vertical text.
        /// </summary>
        /// <param name="characters">The characters of the layout.</param>
        /// <param name="cluster">The cluster.</param>
        /// <returns>The shift in user units.</returns>
        private float GetShift(SvgTextCharacter[] characters, in SvgTextCluster cluster)
        {
            ref readonly SvgTextCharacter character = ref characters[cluster.CharStart];
            return this.vertical ? character.Dy : character.Dx;
        }

        /// <summary>
        /// Moves a cluster along the inline axis.
        /// </summary>
        /// <param name="cluster">The cluster.</param>
        /// <param name="distance">The distance in user units.</param>
        private void Move(ref SvgTextCluster cluster, float distance)
        {
            if (this.vertical)
            {
                cluster.Y += distance;
            }
            else
            {
                cluster.X += distance;
            }
        }

        /// <summary>
        /// Sets the position of a cluster on the inline axis.
        /// </summary>
        /// <param name="cluster">The cluster.</param>
        /// <param name="position">The position in user units.</param>
        private void SetPosition(ref SvgTextCluster cluster, float position)
        {
            if (this.vertical)
            {
                cluster.Y = position;
            }
            else
            {
                cluster.X = position;
            }
        }

        /// <summary>
        /// Orders cluster indices by their start along the line direction, and by logical order where the starts are
        /// equal, so that the order does not depend on the sort algorithm.
        /// </summary>
        private readonly struct StartComparer : IComparer<int>
        {
            private readonly float[] start;

            /// <summary>
            /// Initializes a new instance of the <see cref="StartComparer"/> struct.
            /// </summary>
            /// <param name="start">The start of each cluster along the line direction.</param>
            public StartComparer(float[] start) => this.start = start;

            /// <inheritdoc/>
            public int Compare(int x, int y)
            {
                int order = this.start[x].CompareTo(this.start[y]);
                return order != 0 ? order : x.CompareTo(y);
            }
        }
    }
}
