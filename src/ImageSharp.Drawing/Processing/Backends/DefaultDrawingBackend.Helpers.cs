// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// CPU backend that executes path coverage rasterization and brush composition directly against a CPU region.
/// </summary>
public sealed partial class DefaultDrawingBackend
{
    /// <summary>
    /// Adapts rasterizer coverage callbacks into brush application against the active band target.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private readonly struct FillCoverageRowHandler<TPixel> : IRasterizerCoverageRowHandler
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly BrushRenderer<TPixel> renderer;
        private readonly BandTarget<TPixel> target;
        private readonly DrawingClipState clipState;
        private readonly PreparedPathClipState? pathClipState;
        private readonly Point destinationOffset;
        private readonly BrushWorkspace<TPixel> brushWorkspace;
        private readonly WorkerState<TPixel> workerState;
        private readonly Rectangle interest;

        // Bit i is set when clip descriptor i passes every span of this handler unchanged; see GetSkippedClips.
        private readonly ulong skippedClips;

        /// <summary>
        /// Initializes a new instance of the <see cref="FillCoverageRowHandler{TPixel}"/> struct.
        /// </summary>
        /// <param name="renderer">The brush renderer that will consume emitted coverage spans.</param>
        /// <param name="target">The active band target being rendered.</param>
        /// <param name="clipState">The exact clip state captured with the retained scene item.</param>
        /// <param name="pathClipState">The retained path clip raster data captured with the retained scene item.</param>
        /// <param name="destinationOffset">The destination offset used to place clip descriptors.</param>
        /// <param name="brushWorkspace">The worker-local brush workspace.</param>
        /// <param name="workerState">The worker-local execution state.</param>
        /// <param name="interest">
        /// The absolute bounds that the item can write. A region canvas narrows only this rectangle,
        /// and an aliased item rasterizes one pixel outside it, so every span is clipped to it.
        /// </param>
        public FillCoverageRowHandler(
            BrushRenderer<TPixel> renderer,
            BandTarget<TPixel> target,
            DrawingClipState clipState,
            PreparedPathClipState? pathClipState,
            Point destinationOffset,
            BrushWorkspace<TPixel> brushWorkspace,
            WorkerState<TPixel> workerState,
            Rectangle interest)
        {
            this.interest = interest;
            this.renderer = renderer;
            this.target = target;
            this.clipState = clipState;
            this.pathClipState = pathClipState;
            this.destinationOffset = destinationOffset;
            this.brushWorkspace = brushWorkspace;
            this.workerState = workerState;
            this.skippedClips = clipState.Count == 0 ? 0 : GetSkippedClips(clipState, destinationOffset, target, interest);
        }

        /// <summary>
        /// Finds the rectangle clips that cannot change a span of a handler.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="Handle"/> passes on only spans inside the band target and the interest, so every span lies in the
        /// rectangle of their intersection. An intersecting rectangle clip whose interior rows and interior columns
        /// (<see cref="GetInteriorColumns"/>: the pixels whose axis coverage is exactly 1 by the comparisons of
        /// <see cref="GetAxisCoverage"/>) contain that rectangle passes every such span on unchanged: the row coverage is
        /// 1, the span starts and ends inside the interior, so <see cref="ApplyRectangleInterior"/> forms one run of the
        /// whole span with the multiplier 1 * 1 = 1, and <see cref="ApplyCoverageRun"/> hands the same span, unscaled, to
        /// the next descriptor. Skipping the descriptor gives the next descriptor the same call.
        /// </para>
        /// <para>
        /// The root viewport clip of an SVG document and the viewport clips of its <c>use</c> instances usually contain
        /// the items they clip; without the skip every span pays for the rectangle tests.
        /// </para>
        /// </remarks>
        /// <param name="clipState">The clip state of the item.</param>
        /// <param name="destinationOffset">The destination offset used to place clip descriptors.</param>
        /// <param name="target">The band target.</param>
        /// <param name="interest">The absolute bounds that the item can write.</param>
        /// <returns>A mask with bit i set when descriptor i can be skipped; descriptors from index 64 on are never skipped.</returns>
        private static ulong GetSkippedClips(DrawingClipState clipState, Point destinationOffset, BandTarget<TPixel> target, Rectangle interest)
        {
            int left = Math.Max(target.AbsoluteLeft, interest.Left);
            int right = Math.Min(target.AbsoluteLeft + target.Region.Width, interest.Right);
            int top = Math.Max(target.AbsoluteTop, interest.Top);
            int bottom = Math.Min(target.AbsoluteTop + target.Region.Height, interest.Bottom);
            if (right <= left || bottom <= top)
            {
                return 0;
            }

            ulong skipped = 0;
            int count = Math.Min(clipState.Count, 64);
            for (int i = 0; i < count; i++)
            {
                DrawingClipDescriptor descriptor = clipState.GetDescriptor(i);
                if (descriptor.Kind != DrawingClipKind.Rectangle || descriptor.Operation == ClipOperation.Difference)
                {
                    continue;
                }

                RectangleF rectangle = descriptor.Rectangle;
                rectangle.Offset(destinationOffset.X, destinationOffset.Y);
                GetInteriorColumns(rectangle.Left, rectangle.Right, descriptor.EdgeMode, out int columnStart, out int columnEnd);
                GetInteriorColumns(rectangle.Top, rectangle.Bottom, descriptor.EdgeMode, out int rowStart, out int rowEnd);
                if (left >= columnStart && right <= columnEnd && top >= rowStart && bottom <= rowEnd)
                {
                    skipped |= 1UL << i;
                }
            }

            return skipped;
        }

        /// <summary>
        /// Applies one emitted coverage span to the active destination band.
        /// </summary>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of the coverage span.</param>
        /// <param name="coverage">The emitted coverage values.</param>
        public void Handle(int y, int startX, Span<float> coverage)
        {
            int localY = y - this.target.AbsoluteTop;
            if ((uint)localY >= (uint)this.target.Region.Height || y < this.interest.Top || y >= this.interest.Bottom)
            {
                return;
            }

            int clipStartX = Math.Max(startX, Math.Max(this.target.AbsoluteLeft, this.interest.Left));
            int clipEndX = Math.Min(startX + coverage.Length, Math.Min(this.target.AbsoluteLeft + this.target.Region.Width, this.interest.Right));
            if (clipEndX <= clipStartX)
            {
                return;
            }

            // The rasterizer emits absolute coordinates; clip them once here so the brush
            // renderer can operate against a tight destination span with no extra bounds work.
            int coverageOffset = clipStartX - startX;
            int clippedLength = clipEndX - clipStartX;
            Span<TPixel> destinationRow = this.target.Region
                .DangerousGetRowSpan(localY)
                .Slice(clipStartX - this.target.AbsoluteLeft, clippedLength);

            Span<float> clippedCoverage = coverage.Slice(coverageOffset, clippedLength);
            this.ApplyClipDescriptors(0, y, clipStartX, destinationRow, clippedCoverage);
        }

        /// <summary>
        /// Applies the retained clip stack to a coverage span, then forwards surviving coverage to the brush renderer.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyClipDescriptors(
            int clipIndex,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            if (coverage.Length == 0)
            {
                return;
            }

            // A skipped descriptor would pass the span on unchanged (GetSkippedClips).
            while (clipIndex < 64 && ((this.skippedClips >> clipIndex) & 1UL) != 0)
            {
                clipIndex++;
            }

            if (clipIndex == this.clipState.Count)
            {
                // Clip descriptors are applied recursively by narrowing or scaling the active span.
                // Once the stack is exhausted, the brush sees only pixels that survived every clip.
                this.renderer.Apply(destination, coverage, startX, y, this.brushWorkspace);
                return;
            }

            DrawingClipDescriptor descriptor = this.clipState.GetDescriptor(clipIndex);
            switch (descriptor.Kind)
            {
                case DrawingClipKind.Rectangle:
                    this.ApplyRectangleClip(clipIndex, descriptor, y, startX, destination, coverage);
                    break;

                case DrawingClipKind.IntegerRegion:
                    this.ApplyIntegerRegionClip(clipIndex, descriptor, y, startX, destination, coverage);
                    break;

                case DrawingClipKind.Region:
                    this.ApplyRegionClip(clipIndex, descriptor, y, startX, destination, coverage);
                    break;

                default:
                    this.ApplyPathClip(clipIndex, descriptor, y, startX, destination, coverage);
                    break;
            }
        }

        /// <summary>
        /// Applies one rectangle clip descriptor to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The rectangle clip descriptor.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRectangleClip(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            // Clip descriptors are recorded in canvas space; the item's destination offset
            // anchors them into the space the rasterizer emits so clips track the drawn geometry.
            RectangleF rectangle = descriptor.Rectangle;
            rectangle.Offset(this.destinationOffset.X, this.destinationOffset.Y);

            if (descriptor.Operation == ClipOperation.Difference)
            {
                this.ApplyRectangleDifference(clipIndex, descriptor, rectangle, y, startX, destination, coverage);
                return;
            }

            this.ApplyRectangleIntersection(clipIndex, descriptor, rectangle, y, startX, destination, coverage);
        }

        /// <summary>
        /// Applies an intersecting rectangle clip to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The rectangle clip descriptor.</param>
        /// <param name="rectangle">The destination-space rectangle.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRectangleIntersection(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            RectangleF rectangle,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            float yCoverage = GetAxisCoverage(y, rectangle.Top, rectangle.Bottom, descriptor.EdgeMode);
            if (yCoverage <= 0F)
            {
                return;
            }

            int clipStart = Math.Max(startX, (int)MathF.Floor(rectangle.Left));
            int clipEnd = Math.Min(startX + coverage.Length, (int)MathF.Ceiling(rectangle.Right));
            if (clipEnd <= clipStart)
            {
                return;
            }

            this.ApplyRectangleInterior(clipIndex, descriptor, rectangle, yCoverage, y, startX, clipStart, clipEnd, destination, coverage);
        }

        /// <summary>
        /// Applies a subtracting rectangle clip to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The rectangle clip descriptor.</param>
        /// <param name="rectangle">The destination-space rectangle.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRectangleDifference(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            RectangleF rectangle,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            float yCoverage = GetAxisCoverage(y, rectangle.Top, rectangle.Bottom, descriptor.EdgeMode);
            if (yCoverage <= 0F)
            {
                this.ApplyClipDescriptors(clipIndex + 1, y, startX, destination, coverage);
                return;
            }

            int spanEnd = startX + coverage.Length;
            int clipStart = Math.Max(startX, (int)MathF.Floor(rectangle.Left));
            int clipEnd = Math.Min(spanEnd, (int)MathF.Ceiling(rectangle.Right));
            if (clipEnd <= clipStart)
            {
                this.ApplyClipDescriptors(clipIndex + 1, y, startX, destination, coverage);
                return;
            }

            // Difference clips preserve the span outside the rectangle and invert only the
            // overlapping interior, so the outside ranges can advance to the next clip unchanged.
            this.ApplyClipDescriptors(clipIndex + 1, y, startX, destination[..(clipStart - startX)], coverage[..(clipStart - startX)]);
            this.ApplyRectangleDifferenceInterior(clipIndex, descriptor, rectangle, yCoverage, y, startX, clipStart, clipEnd, destination, coverage);
            this.ApplyClipDescriptors(clipIndex + 1, y, clipEnd, destination[(clipEnd - startX)..], coverage[(clipEnd - startX)..]);
        }

        /// <summary>
        /// Applies the interior of an intersecting rectangle clip to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The rectangle clip descriptor.</param>
        /// <param name="rectangle">The destination-space rectangle.</param>
        /// <param name="yCoverage">The vertical coverage contribution for the current row.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="spanStart">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="clipStart">The first absolute column overlapped by the rectangle.</param>
        /// <param name="clipEnd">The exclusive absolute column where rectangle overlap ends.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRectangleInterior(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            RectangleF rectangle,
            float yCoverage,
            int y,
            int spanStart,
            int clipStart,
            int clipEnd,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            GetInteriorColumns(rectangle.Left, rectangle.Right, descriptor.EdgeMode, out int interiorStart, out int interiorEnd);

            int x = clipStart;
            while (x < clipEnd)
            {
                float xCoverage = GetAxisCoverage(x, rectangle.Left, rectangle.Right, descriptor.EdgeMode);
                int runStart = x++;
                float multiplier = xCoverage * yCoverage;

                // Rectangle edge coverage is constant across interior pixels and only changes
                // at the left/right edge pixels, so collapse adjacent equal-coverage columns.
                x = ExtendAxisRun(x, clipEnd, xCoverage, interiorStart, interiorEnd, rectangle.Left, rectangle.Right, descriptor.EdgeMode);

                this.ApplyCoverageRun(clipIndex + 1, y, runStart, x, spanStart, multiplier, destination, coverage);
            }
        }

        /// <summary>
        /// Applies the interior of a subtracting rectangle clip to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The rectangle clip descriptor.</param>
        /// <param name="rectangle">The destination-space rectangle.</param>
        /// <param name="yCoverage">The vertical coverage contribution for the current row.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="spanStart">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="clipStart">The first absolute column overlapped by the rectangle.</param>
        /// <param name="clipEnd">The exclusive absolute column where rectangle overlap ends.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRectangleDifferenceInterior(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            RectangleF rectangle,
            float yCoverage,
            int y,
            int spanStart,
            int clipStart,
            int clipEnd,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            GetInteriorColumns(rectangle.Left, rectangle.Right, descriptor.EdgeMode, out int interiorStart, out int interiorEnd);

            int x = clipStart;
            while (x < clipEnd)
            {
                float xCoverage = GetAxisCoverage(x, rectangle.Left, rectangle.Right, descriptor.EdgeMode);
                int runStart = x++;
                float multiplier = 1F - (xCoverage * yCoverage);

                // Subtraction uses the inverse coverage of the rectangle, but the same run
                // coalescing applies: edge pixels differ, interior pixels share one value.
                x = ExtendAxisRun(x, clipEnd, xCoverage, interiorStart, interiorEnd, rectangle.Left, rectangle.Right, descriptor.EdgeMode);

                this.ApplyCoverageRun(clipIndex + 1, y, runStart, x, spanStart, multiplier, destination, coverage);
            }
        }

        /// <summary>
        /// Applies an integer-region clip descriptor to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The integer-region clip descriptor.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyIntegerRegionClip(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            IReadOnlyList<Rectangle> rectangles = descriptor.IntegerRectangles;
            if (descriptor.Operation == ClipOperation.Difference)
            {
                int cursor = startX;
                int endX = startX + coverage.Length;

                // Integer regions are hard-edged, so difference clips can split the span into
                // retained gaps without allocating or rasterizing a mask.
                for (int i = 0; i < rectangles.Count && cursor < endX; i++)
                {
                    Rectangle rectangle = rectangles[i];
                    rectangle.Offset(this.destinationOffset);
                    if ((uint)(y - rectangle.Top) >= (uint)rectangle.Height || rectangle.Right <= cursor)
                    {
                        continue;
                    }

                    if (rectangle.Left > cursor)
                    {
                        int runEnd = Math.Min(rectangle.Left, endX);
                        this.ApplyIntegerRun(clipIndex + 1, y, cursor, startX, runEnd, destination, coverage);
                    }

                    cursor = Math.Max(cursor, rectangle.Right);
                }

                this.ApplyIntegerRun(clipIndex + 1, y, cursor, startX, endX, destination, coverage);
                return;
            }

            for (int i = 0; i < rectangles.Count; i++)
            {
                Rectangle rectangle = rectangles[i];
                rectangle.Offset(this.destinationOffset);
                if ((uint)(y - rectangle.Top) >= (uint)rectangle.Height)
                {
                    continue;
                }

                int runStart = Math.Max(startX, rectangle.Left);
                int runEnd = Math.Min(startX + coverage.Length, rectangle.Right);
                this.ApplyIntegerRun(clipIndex + 1, y, runStart, startX, runEnd, destination, coverage);
            }
        }

        /// <summary>
        /// Applies a floating-point region clip descriptor to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The region clip descriptor.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyRegionClip(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            IReadOnlyList<RectangleF> rectangles = descriptor.Rectangles;
            if (descriptor.Operation == ClipOperation.Difference)
            {
                this.ApplyFloatingRegionDifference(clipIndex, descriptor, rectangles, y, startX, destination, coverage);
                return;
            }

            for (int i = 0; i < rectangles.Count; i++)
            {
                RectangleF rectangle = rectangles[i];
                rectangle.Offset(this.destinationOffset.X, this.destinationOffset.Y);
                this.ApplyRectangleIntersection(clipIndex, descriptor, rectangle, y, startX, destination, coverage);
            }
        }

        /// <summary>
        /// Applies a subtracting floating-point region clip to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The region clip descriptor.</param>
        /// <param name="rectangles">The region rectangles in descriptor order.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyFloatingRegionDifference(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            IReadOnlyList<RectangleF> rectangles,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            int cursor = startX;
            int endX = startX + coverage.Length;

            // The cursor marks the prefix already emitted to the next clip. This avoids
            // revisiting or re-emitting spans when a region contributes multiple rectangles.
            for (int i = 0; i < rectangles.Count && cursor < endX; i++)
            {
                RectangleF rectangle = rectangles[i];
                rectangle.Offset(this.destinationOffset.X, this.destinationOffset.Y);

                float yCoverage = GetAxisCoverage(y, rectangle.Top, rectangle.Bottom, descriptor.EdgeMode);
                if (yCoverage <= 0F)
                {
                    continue;
                }

                int clipStart = Math.Max(startX, (int)MathF.Floor(rectangle.Left));
                int clipEnd = Math.Min(endX, (int)MathF.Ceiling(rectangle.Right));
                if (clipEnd <= cursor)
                {
                    continue;
                }

                if (clipStart > cursor)
                {
                    int runEnd = Math.Min(clipStart, endX);
                    this.ApplyIntegerRun(clipIndex + 1, y, cursor, startX, runEnd, destination, coverage);
                }

                this.ApplyRectangleDifferenceInterior(
                    clipIndex,
                    descriptor,
                    rectangle,
                    yCoverage,
                    y,
                    startX,
                    Math.Max(cursor, clipStart),
                    clipEnd,
                    destination,
                    coverage);

                cursor = Math.Max(cursor, clipEnd);
            }

            this.ApplyIntegerRun(clipIndex + 1, y, cursor, startX, endX, destination, coverage);
        }

        /// <summary>
        /// Applies a retained path clip descriptor to a coverage span.
        /// </summary>
        /// <param name="clipIndex">The current clip descriptor index.</param>
        /// <param name="descriptor">The path clip descriptor.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="startX">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="destination">The destination pixels covered by the span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyPathClip(
            int clipIndex,
            DrawingClipDescriptor descriptor,
            int y,
            int startX,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            // A missing or empty clip raster means the clip path contributes no coverage here:
            // a Difference clip removes nothing so the subject span passes through unchanged,
            // while an intersecting clip keeps nothing so the span is dropped entirely.
            DefaultRasterizer.RasterizableGeometry? rasterizable = this.pathClipState?.GetRasterizable(clipIndex);
            if (rasterizable is null)
            {
                if (descriptor.Operation == ClipOperation.Difference)
                {
                    this.ApplyClipDescriptors(clipIndex + 1, y, startX, destination, coverage);
                }

                return;
            }

            int rowBandIndex = y / DefaultRasterizer.DefaultTileHeight;
            int localRowIndex = rowBandIndex - rasterizable.FirstRowBandIndex;
            if ((uint)localRowIndex >= (uint)rasterizable.RowBandCount || !rasterizable.HasCoverage(localRowIndex))
            {
                if (descriptor.Operation == ClipOperation.Difference)
                {
                    this.ApplyClipDescriptors(clipIndex + 1, y, startX, destination, coverage);
                }

                return;
            }

            // Path clips use the same retained rasterizer as fills. The rasterizer emits a whole band
            // of rows at once, and one subject item asks for the same clip band once for every span of
            // every row of the band, so the worker keeps the emitted band per clip index and rasterizes
            // it again only for another clip geometry or band.
            PathClipBand band = this.workerState.GetPathClipBand(clipIndex);
            if (!band.Holds(rasterizable, localRowIndex))
            {
                DefaultRasterizer.RasterizableItem item = new(rasterizable, localRowIndex);
                DefaultRasterizer.RasterizableBandInfo bandInfo = rasterizable.GetBandInfo(localRowIndex);
                DefaultRasterizer.WorkerScratch scratch = this.workerState.GetOrCreatePathClipScratch(rasterizable.Width);
                DefaultRasterizer.Context context = scratch.CreateContext(
                    bandInfo.IntersectionRule,
                    bandInfo.RasterizationMode,
                    bandInfo.LineCount);

                band.Begin(rasterizable, localRowIndex, bandInfo.DestinationLeft, bandInfo.DestinationTop, bandInfo.Width, bandInfo.BandHeight);
                PathClipBandRowHandler rowHandler = new(band);
                DefaultRasterizer.ExecuteRasterizableItem(
                    ref context,
                    in item,
                    in bandInfo,
                    scratch.Scanline,
                    ref rowHandler);
            }

            // The clip row is materialized into worker-local coverage only for the subject span
            // currently being painted, so rectangle and region clips stay on their cheaper
            // span-slicing paths.
            Span<float> clipCoverage = this.workerState.GetOrCreatePathClipCoverage(coverage.Length);
            clipCoverage = clipCoverage[..coverage.Length];
            clipCoverage.Fill(descriptor.Operation == ClipOperation.Difference ? 1F : 0F);
            band.CopyRow(y, startX, clipCoverage, descriptor.Operation);

            // Coalesce equal clip coverage into runs so deeper clips and brush rendering are invoked
            // per coverage run instead of once per pixel. The run ends are found with the vectorized
            // integer search of the runtime on the bit patterns. Bit equality is float equality here:
            // the values are the fill values +0 and 1, coverage that the rasterizer emitted (positive),
            // zeros of unwritten band columns (+0), and 1 - c for c in 0 to 1 (never -0), so no value
            // is -0 or NaN, the two cases where the comparisons differ.
            // A deeper path clip reuses the same worker buffer, but it writes only the first
            // (runEnd - runStart) entries, which lie before every entry this loop reads afterwards.
            ReadOnlySpan<int> clipBits = MemoryMarshal.Cast<float, int>(clipCoverage);
            int length = clipBits.Length;
            int runStart = 0;
            while (runStart < length)
            {
                float runCoverage = clipCoverage[runStart];
                int runLength = clipBits[(runStart + 1)..].IndexOfAnyExcept(clipBits[runStart]);
                int runEnd = runLength < 0 ? length : runStart + 1 + runLength;
                this.ApplyCoverageRun(clipIndex + 1, y, startX + runStart, startX + runEnd, startX, runCoverage, destination, coverage);
                runStart = runEnd;
            }
        }

        /// <summary>
        /// Advances one hard-edged integer run to the next clip descriptor.
        /// </summary>
        /// <param name="clipIndex">The next clip descriptor index.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="runStart">The absolute first column in the run.</param>
        /// <param name="spanStart">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="runEnd">The exclusive absolute end column in the run.</param>
        /// <param name="destination">The destination pixels covered by the parent span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyIntegerRun(
            int clipIndex,
            int y,
            int runStart,
            int spanStart,
            int runEnd,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            if (runEnd <= runStart)
            {
                return;
            }

            int offset = runStart - spanStart;
            this.ApplyClipDescriptors(clipIndex, y, runStart, destination.Slice(offset, runEnd - runStart), coverage.Slice(offset, runEnd - runStart));
        }

        /// <summary>
        /// Applies a coverage multiplier to one run before advancing it to the next clip descriptor.
        /// </summary>
        /// <param name="clipIndex">The next clip descriptor index.</param>
        /// <param name="y">The absolute destination row.</param>
        /// <param name="runStart">The absolute first column in the run.</param>
        /// <param name="runEnd">The exclusive absolute end column in the run.</param>
        /// <param name="spanStart">The absolute start column of <paramref name="destination"/> and <paramref name="coverage"/>.</param>
        /// <param name="multiplier">The clip coverage multiplier applied to the run.</param>
        /// <param name="destination">The destination pixels covered by the parent span.</param>
        /// <param name="coverage">The subject coverage values for <paramref name="destination"/>.</param>
        private void ApplyCoverageRun(
            int clipIndex,
            int y,
            int runStart,
            int runEnd,
            int spanStart,
            float multiplier,
            Span<TPixel> destination,
            Span<float> coverage)
        {
            if (multiplier <= 0F)
            {
                return;
            }

            int offset = runStart - spanStart;
            int length = runEnd - runStart;

            Span<float> runCoverage = coverage.Slice(offset, length);
            if (multiplier != 1F)
            {
                int i = 0;
                if (Vector.IsHardwareAccelerated)
                {
                    int vectorCount = Vector<float>.Count;
                    int vectorLength = runCoverage.Length - (runCoverage.Length % vectorCount);
                    Vector<float> multiplierVector = new(multiplier);
                    Span<Vector<float>> vectors = MemoryMarshal.Cast<float, Vector<float>>(runCoverage[..vectorLength]);

                    // The coverage span is borrowed from the rasterizer. Scale it in place so
                    // downstream clipping and blending consume the combined coverage without
                    // allocating a temporary span for every clipped run.
                    for (int j = 0; j < vectors.Length; j++)
                    {
                        vectors[j] *= multiplierVector;
                    }

                    i = vectorLength;
                }

                for (; i < runCoverage.Length; i++)
                {
                    runCoverage[i] *= multiplier;
                }
            }

            this.ApplyClipDescriptors(clipIndex, y, runStart, destination.Slice(offset, length), runCoverage);

            if (multiplier != 1F)
            {
                int i = 0;
                if (Vector.IsHardwareAccelerated)
                {
                    int vectorCount = Vector<float>.Count;
                    int vectorLength = runCoverage.Length - (runCoverage.Length % vectorCount);
                    Vector<float> multiplierVector = new(multiplier);
                    Span<Vector<float>> vectors = MemoryMarshal.Cast<float, Vector<float>>(runCoverage[..vectorLength]);

                    // Restore the borrowed coverage before returning; sibling runs and outer
                    // clip descriptors still observe the original rasterizer coverage buffer.
                    for (int j = 0; j < vectors.Length; j++)
                    {
                        vectors[j] /= multiplierVector;
                    }

                    i = vectorLength;
                }

                for (; i < runCoverage.Length; i++)
                {
                    runCoverage[i] /= multiplier;
                }
            }
        }

        /// <summary>
        /// Computes one-dimensional pixel coverage against a clip interval.
        /// </summary>
        /// <param name="pixel">The integer pixel coordinate.</param>
        /// <param name="clipStart">The inclusive clip interval start.</param>
        /// <param name="clipEnd">The inclusive clip interval end for centre sampling.</param>
        /// <param name="edgeMode">The edge mode used to select continuous or centre-sampled coverage.</param>
        /// <returns>The coverage contribution for the pixel.</returns>
        private static float GetAxisCoverage(
            int pixel,
            float clipStart,
            float clipEnd,
            DrawingClipEdgeMode edgeMode)
        {
            if (edgeMode == DrawingClipEdgeMode.Hard)
            {
                float pixelCentre = pixel + 0.5F;
                return pixelCentre >= clipStart && pixelCentre <= clipEnd ? 1F : 0F;
            }

            float coverage = MathF.Min(pixel + 1F, clipEnd) - MathF.Max(pixel, clipStart);
            return Math.Clamp(coverage, 0F, 1F);
        }

        /// <summary>
        /// Advances past the columns that continue a run of equal axis coverage, as a column-by-column
        /// comparison with <see cref="GetAxisCoverage"/> would, but skips the interior columns in one step.
        /// </summary>
        /// <param name="x">The first column after the start of the run.</param>
        /// <param name="clipEnd">The exclusive column where the walk ends.</param>
        /// <param name="runCoverage">The axis coverage of the first column of the run.</param>
        /// <param name="interiorStart">The first interior column from <see cref="GetInteriorColumns"/>.</param>
        /// <param name="interiorEnd">The exclusive last interior column from <see cref="GetInteriorColumns"/>.</param>
        /// <param name="clipStart">The clip interval start.</param>
        /// <param name="clipIntervalEnd">The clip interval end.</param>
        /// <param name="edgeMode">The edge mode.</param>
        /// <returns>The exclusive end column of the run.</returns>
        private static int ExtendAxisRun(
            int x,
            int clipEnd,
            float runCoverage,
            int interiorStart,
            int interiorEnd,
            float clipStart,
            float clipIntervalEnd,
            DrawingClipEdgeMode edgeMode)
        {
            while (x < clipEnd)
            {
                if (x >= interiorStart && x < interiorEnd)
                {
                    // Every interior column has coverage 1, so the run either ends here or
                    // continues through the whole interior.
                    if (runCoverage != 1F)
                    {
                        break;
                    }

                    x = Math.Min(interiorEnd, clipEnd);
                    continue;
                }

                if (GetAxisCoverage(x, clipStart, clipIntervalEnd, edgeMode) != runCoverage)
                {
                    break;
                }

                x++;
            }

            return x;
        }

        /// <summary>
        /// Finds the interval of columns for which <see cref="GetAxisCoverage"/> returns exactly 1 by
        /// its comparisons alone, so the column walk can skip them.
        /// </summary>
        /// <param name="clipStart">The clip interval start.</param>
        /// <param name="clipEnd">The clip interval end.</param>
        /// <param name="edgeMode">The edge mode.</param>
        /// <param name="interiorStart">The first interior column.</param>
        /// <param name="interiorEnd">The exclusive last interior column; equal to the start when there is none.</param>
        private static void GetInteriorColumns(
            float clipStart,
            float clipEnd,
            DrawingClipEdgeMode edgeMode,
            out int interiorStart,
            out int interiorEnd)
        {
            // Below 2^22 in magnitude every integer column x, x + 1, and x + 0.5 is exact in single
            // precision, so the comparisons below are the comparisons of GetAxisCoverage. NaN and
            // larger bounds fail these tests and keep the column walk.
            const float limit = 1 << 22;
            if (!(clipStart > -limit && clipEnd < limit && clipStart < clipEnd))
            {
                interiorStart = 0;
                interiorEnd = 0;
                return;
            }

            if (edgeMode == DrawingClipEdgeMode.Hard)
            {
                // Centre sampling returns 1 exactly when x + 0.5 >= clipStart and x + 0.5 <= clipEnd.
                // Both tests are monotonic in x, so the columns that pass form one interval; its ends
                // are within one column of the floors and are found with the same float tests.
                int first = (int)MathF.Floor(clipStart) - 1;
                while (!(first + 0.5F >= clipStart))
                {
                    first++;
                }

                int last = (int)MathF.Floor(clipEnd) + 1;
                while (!(last + 0.5F <= clipEnd))
                {
                    last--;
                }

                interiorStart = first;
                interiorEnd = Math.Max(first, last + 1);
                return;
            }

            // Area coverage is Min(x + 1, end) - Max(x, start). When x >= start and x + 1 <= end the two
            // operands are x + 1 and x, both exact, so the difference is exactly 1. For integer x these
            // tests hold from Ceiling(start) to Floor(end) - 1. Columns outside can still reach 1 through
            // rounding; the walk compares those one by one.
            interiorStart = (int)MathF.Ceiling(clipStart);
            interiorEnd = Math.Max(interiorStart, (int)MathF.Floor(clipEnd));
        }
    }

    /// <summary>
    /// Writes the rows that the rasterizer emits for one band of a path clip into a <see cref="PathClipBand"/>.
    /// </summary>
    private readonly struct PathClipBandRowHandler : IRasterizerCoverageRowHandler
    {
        private readonly PathClipBand band;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathClipBandRowHandler"/> struct.
        /// </summary>
        /// <param name="band">The band that receives the emitted rows; <see cref="PathClipBand.Begin"/> ran on it.</param>
        public PathClipBandRowHandler(PathClipBand band) => this.band = band;

        /// <summary>
        /// Records one emitted span of clip coverage.
        /// </summary>
        /// <param name="y">The absolute row emitted by the clip rasterizer.</param>
        /// <param name="startX">The absolute start column emitted by the clip rasterizer.</param>
        /// <param name="coverage">The emitted clip coverage values.</param>
        public void Handle(int y, int startX, Span<float> coverage) => this.band.Write(y, startX, coverage);
    }

    /// <summary>
    /// Walks retained row-operation blocks without flattening them into another list.
    /// </summary>
    private struct SceneOperationCursor
    {
        private FlushScene.SceneOperationBlock? block;
        private int index;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneOperationCursor"/> struct.
        /// </summary>
        /// <param name="block">The first retained row-operation block.</param>
        public SceneOperationCursor(FlushScene.SceneOperationBlock? block)
        {
            this.block = block;
            this.index = 0;
        }

        /// <summary>
        /// Reads the next retained row operation.
        /// </summary>
        /// <param name="operation">The next operation.</param>
        /// <returns>True when an operation was read; otherwise false.</returns>
        public bool TryRead(out FlushScene.SceneOperation operation)
        {
            while (this.block is not null)
            {
                Span<FlushScene.SceneOperation> items = this.block.Items;
                if (this.index < items.Length)
                {
                    operation = items[this.index++];
                    return true;
                }

                this.block = this.block.Next;
                this.index = 0;
            }

            operation = default;
            return false;
        }
    }

    /// <summary>
    /// Represents one active composition target for a retained row.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private readonly struct BandTarget<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Buffer2D<TPixel>? owner;

        /// <summary>
        /// Initializes a new instance of the <see cref="BandTarget{TPixel}"/> struct over an existing region.
        /// </summary>
        /// <param name="region">The destination region.</param>
        /// <param name="absoluteLeft">The absolute X origin of the region.</param>
        /// <param name="absoluteTop">The absolute Y origin of the region.</param>
        /// <param name="graphicsOptions">The graphics options used when this target is later composited.</param>
        public BandTarget(Buffer2DRegion<TPixel> region, int absoluteLeft, int absoluteTop, GraphicsOptions? graphicsOptions)
        {
            this.Region = region;
            this.AbsoluteLeft = absoluteLeft;
            this.AbsoluteTop = absoluteTop;
            this.GraphicsOptions = graphicsOptions;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BandTarget{TPixel}"/> struct over an owned temporary buffer.
        /// </summary>
        /// <param name="owner">The owned buffer backing the target.</param>
        /// <param name="bounds">The absolute bounds represented by the target.</param>
        /// <param name="graphicsOptions">The graphics options used when this target is later composited.</param>
        public BandTarget(Buffer2D<TPixel> owner, Rectangle bounds, GraphicsOptions? graphicsOptions)
        {
            this.owner = owner;
            this.Region = owner.GetRegion();
            this.AbsoluteLeft = bounds.X;
            this.AbsoluteTop = bounds.Y;
            this.GraphicsOptions = graphicsOptions;
        }

        /// <summary>
        /// Gets the writable pixel region for the target.
        /// </summary>
        public Buffer2DRegion<TPixel> Region { get; }

        /// <summary>
        /// Gets the absolute bounds represented by <see cref="Region"/>.
        /// </summary>
        public Rectangle Bounds => new(this.AbsoluteLeft, this.AbsoluteTop, this.Region.Width, this.Region.Height);

        /// <summary>
        /// Gets the absolute X origin of <see cref="Region"/>.
        /// </summary>
        public int AbsoluteLeft { get; }

        /// <summary>
        /// Gets the absolute Y origin of <see cref="Region"/>.
        /// </summary>
        public int AbsoluteTop { get; }

        /// <summary>
        /// Gets the graphics options associated with the target when it is used as a layer.
        /// </summary>
        public GraphicsOptions? GraphicsOptions { get; }

        /// <summary>
        /// Releases the owned temporary buffer when the target represents a layer.
        /// </summary>
        public void Dispose() => this.owner?.Dispose();
    }

    /// <summary>
    /// Holds the coverage that the rasterizer emitted for one band of one path clip geometry, so that every subject
    /// span of the band reads its clip row without rasterizing the band again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A row stores the emitted values in a dense array over the columns of the band. Each row keeps the interval of
    /// columns written since <see cref="Begin"/>; a column inside that interval that no span wrote holds 0, and a column
    /// outside it counts as 0. This gives the same clip coverage as a direct copy of the emitted spans into a span that
    /// holds the empty value of the clip: an intersecting clip fills with 0 and copies the values, a subtracting clip
    /// fills with 1 and writes <c>1 - value</c>, and <c>1 - 0</c> is exactly 1.
    /// </para>
    /// <para>
    /// The band is keyed by the identity of the retained geometry and the band index. Retained geometry does not change
    /// while its scene exists, so a key that matches holds the rows that the rasterizer would emit again.
    /// </para>
    /// </remarks>
    private sealed class PathClipBand : IDisposable
    {
        private readonly MemoryAllocator allocator;
        private IMemoryOwner<float>? rowsOwner;
        private int[] rowStarts = [];
        private int[] rowEnds = [];
        private DefaultRasterizer.RasterizableGeometry? geometry;
        private int localRowIndex;
        private int left;
        private int top;
        private int width;
        private int height;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathClipBand"/> class.
        /// </summary>
        /// <param name="allocator">The allocator of the row storage.</param>
        public PathClipBand(MemoryAllocator allocator) => this.allocator = allocator;

        /// <summary>
        /// Gets a value indicating whether the band holds the rows of a clip geometry band.
        /// </summary>
        /// <param name="rasterizable">The clip geometry.</param>
        /// <param name="rowIndex">The band index local to the geometry.</param>
        /// <returns><see langword="true"/> when the rows were emitted for this geometry and band.</returns>
        public bool Holds(DefaultRasterizer.RasterizableGeometry rasterizable, int rowIndex)
            => ReferenceEquals(this.geometry, rasterizable) && this.localRowIndex == rowIndex;

        /// <summary>
        /// Empties the band and keys it to a clip geometry band before the rasterizer emits its rows.
        /// </summary>
        /// <param name="rasterizable">The clip geometry.</param>
        /// <param name="rowIndex">The band index local to the geometry.</param>
        /// <param name="destinationLeft">The absolute column of the first column of the band.</param>
        /// <param name="destinationTop">The absolute row of the first row of the band.</param>
        /// <param name="bandWidth">The number of columns of the band.</param>
        /// <param name="bandHeight">The number of rows of the band.</param>
        public void Begin(
            DefaultRasterizer.RasterizableGeometry rasterizable,
            int rowIndex,
            int destinationLeft,
            int destinationTop,
            int bandWidth,
            int bandHeight)
        {
            int length = bandWidth * bandHeight;
            if (this.rowsOwner is null || this.rowsOwner.Memory.Length < length)
            {
                // Null before allocating, as in WorkerState.GetOrCreateScratch.
                this.geometry = null;
                this.rowsOwner?.Dispose();
                this.rowsOwner = null;
                this.rowsOwner = this.allocator.Allocate<float>(length);
            }

            if (this.rowStarts.Length < bandHeight)
            {
                this.rowStarts = new int[bandHeight];
                this.rowEnds = new int[bandHeight];
            }

            this.rowStarts.AsSpan(0, bandHeight).Clear();
            this.rowEnds.AsSpan(0, bandHeight).Clear();
            this.geometry = rasterizable;
            this.localRowIndex = rowIndex;
            this.left = destinationLeft;
            this.top = destinationTop;
            this.width = bandWidth;
            this.height = bandHeight;
        }

        /// <summary>
        /// Stores one emitted span. The rasterizer emits only columns inside the band.
        /// </summary>
        /// <param name="y">The absolute row.</param>
        /// <param name="startX">The absolute start column.</param>
        /// <param name="coverage">The emitted coverage values.</param>
        public void Write(int y, int startX, ReadOnlySpan<float> coverage)
        {
            int row = y - this.top;
            if ((uint)row >= (uint)this.height || coverage.Length == 0)
            {
                return;
            }

            int start = startX - this.left;
            int end = start + coverage.Length;
            Span<float> values = this.rowsOwner!.Memory.Span.Slice(row * this.width, this.width);
            int rowStart = this.rowStarts[row];
            int rowEnd = this.rowEnds[row];
            if (rowStart == rowEnd)
            {
                rowStart = start;
                rowEnd = end;
            }
            else
            {
                // Columns between the written interval and the new span were not emitted: they hold 0.
                if (start > rowEnd)
                {
                    values[rowEnd..start].Clear();
                }

                if (end < rowStart)
                {
                    values[end..rowStart].Clear();
                }

                rowStart = Math.Min(rowStart, start);
                rowEnd = Math.Max(rowEnd, end);
            }

            coverage.CopyTo(values[start..end]);
            this.rowStarts[row] = rowStart;
            this.rowEnds[row] = rowEnd;
        }

        /// <summary>
        /// Writes the clip coverage of one subject span into a span that holds the empty value of the clip:
        /// the stored values for an intersecting clip, and one minus them for a subtracting clip.
        /// </summary>
        /// <param name="y">The absolute subject row.</param>
        /// <param name="targetStartX">The absolute start column of the subject span.</param>
        /// <param name="target">The clip coverage of the subject span, filled with 0 or 1 by the caller.</param>
        /// <param name="operation">The clip operation.</param>
        public void CopyRow(int y, int targetStartX, Span<float> target, ClipOperation operation)
        {
            int row = y - this.top;
            if ((uint)row >= (uint)this.height)
            {
                return;
            }

            int overlapStart = Math.Max(this.left + this.rowStarts[row], targetStartX);
            int overlapEnd = Math.Min(this.left + this.rowEnds[row], targetStartX + target.Length);
            if (overlapEnd <= overlapStart)
            {
                return;
            }

            int length = overlapEnd - overlapStart;
            ReadOnlySpan<float> source = this.rowsOwner!.Memory.Span.Slice((row * this.width) + (overlapStart - this.left), length);
            Span<float> destination = target.Slice(overlapStart - targetStartX, length);

            if (operation == ClipOperation.Difference)
            {
                int i = 0;
                if (Vector.IsHardwareAccelerated)
                {
                    int vectorCount = Vector<float>.Count;
                    int vectorLength = length - (length % vectorCount);
                    Vector<float> one = Vector<float>.One;
                    ReadOnlySpan<Vector<float>> sourceVectors = MemoryMarshal.Cast<float, Vector<float>>(source[..vectorLength]);
                    Span<Vector<float>> destinationVectors = MemoryMarshal.Cast<float, Vector<float>>(destination[..vectorLength]);

                    // Difference clips invert the clip coverage into the reusable target span.
                    // Casting the vector-width prefix avoids per-lane loads and stores when SIMD is available.
                    for (int j = 0; j < sourceVectors.Length; j++)
                    {
                        destinationVectors[j] = one - sourceVectors[j];
                    }

                    i = vectorLength;
                }

                for (; i < length; i++)
                {
                    destination[i] = 1F - source[i];
                }

                return;
            }

            source.CopyTo(destination);
        }

        /// <summary>
        /// Forgets the key, so that a pooled worker state keeps no reference to the geometry of a finished scene.
        /// </summary>
        public void Reset() => this.geometry = null;

        /// <summary>
        /// Releases the row storage.
        /// </summary>
        public void Dispose()
        {
            this.geometry = null;
            this.rowsOwner?.Dispose();
            this.rowsOwner = null;
        }
    }

    /// <summary>
    /// Holds the reusable worker-local scratch used while executing retained scene rows.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class WorkerState<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Worker states hold only allocator-owned buffers (brush workspace, raster scratch,
        // clip coverage), never scene references, so instances can be pooled across flushes
        // exactly like the WebGPU backend's scheduling arenas. One Parallel.For's worth of
        // states covers steady-state rendering; anything beyond the cap is released.
        //
        // The pool trims itself on Gen2 collections, mirroring the memory allocator's own
        // trimming: when a Gen2 GC arrives and nothing rented since the previous one, the
        // renderer is idle and every pooled state is disposed, returning its buffers to the
        // allocator. Active rendering keeps the pool untouched. This bounds idle retention
        // without an explicit lifetime or shutdown hook.
        private static readonly Stack<WorkerState<TPixel>> Pool = new();
        private static readonly object PoolSync = new();
        private static readonly int MaxPooledStates = Environment.ProcessorCount;

        // 1 when Rent ran since the last Gen2 trim check; the sentinel resets it each Gen2.
        private static int rentedSinceLastGen2;

        private readonly MemoryAllocator allocator;
        private DefaultRasterizer.WorkerScratch? scratch;
        private DefaultRasterizer.WorkerScratch? pathClipScratch;
        private IMemoryOwner<float>? pathClipCoverageOwner;
        private int pathClipCoverageLength;
        private PathClipBand?[] pathClipBands = [];

        /// <summary>
        /// Initializes static members of the <see cref="WorkerState{TPixel}"/> class,
        /// arming the Gen2 trim sentinel. The instance is intentionally dropped: it lives
        /// on the finalizer queue and resurrects itself after every collection.
        /// </summary>
        static WorkerState() => _ = new Gen2TrimSentinel();

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkerState{TPixel}"/> class.
        /// </summary>
        /// <param name="allocator">The memory allocator used for scratch growth.</param>
        /// <param name="destinationWidth">The destination width used to size the brush workspace.</param>
        public WorkerState(
            MemoryAllocator allocator,
            int destinationWidth)
        {
            this.allocator = allocator;
            this.DestinationWidth = destinationWidth;
            this.BrushWorkspace = new BrushWorkspace<TPixel>(allocator, destinationWidth);
        }

        /// <summary>
        /// Gets the destination width the brush workspace was sized for. A wider state can
        /// service any narrower target, so pooling reuses states whose width is sufficient.
        /// </summary>
        public int DestinationWidth { get; }

        /// <summary>
        /// Gets the reusable brush workspace for the worker.
        /// </summary>
        public BrushWorkspace<TPixel> BrushWorkspace { get; }

        /// <summary>
        /// Gets the worker-local path clip coverage span last requested by <see cref="GetOrCreatePathClipCoverage"/>.
        /// </summary>
        public Span<float> PathClipCoverage
        {
            get
            {
                IMemoryOwner<float>? owner = this.pathClipCoverageOwner;
                return owner is null ? [] : owner.Memory.Span[..this.pathClipCoverageLength];
            }
        }

        /// <summary>
        /// Rents a pooled worker state sized for at least the requested width, or creates one.
        /// A popped state with a different allocator or an insufficient width is released and
        /// replaced, so the pool converges to full-width states for the active allocator.
        /// </summary>
        /// <param name="allocator">The memory allocator that must own the state's buffers.</param>
        /// <param name="destinationWidth">The minimum destination width the state must service.</param>
        /// <returns>A worker state ready for one worker's row execution.</returns>
        public static WorkerState<TPixel> Rent(MemoryAllocator allocator, int destinationWidth)
        {
            Volatile.Write(ref rentedSinceLastGen2, 1);

            WorkerState<TPixel>? pooled = null;
            lock (PoolSync)
            {
                if (Pool.Count > 0)
                {
                    pooled = Pool.Pop();
                }
            }

            if (pooled is not null)
            {
                if (ReferenceEquals(pooled.allocator, allocator) && pooled.DestinationWidth >= destinationWidth)
                {
                    return pooled;
                }

                pooled.Dispose();
            }

            return new WorkerState<TPixel>(allocator, destinationWidth);
        }

        /// <summary>
        /// Returns a worker state to the pool for a later flush, or releases it when the pool
        /// is already holding one full set of worker states.
        /// </summary>
        /// <param name="state">The state to recycle.</param>
        public static void Return(WorkerState<TPixel> state)
        {
            foreach (PathClipBand? band in state.pathClipBands)
            {
                band?.Reset();
            }

            lock (PoolSync)
            {
                if (Pool.Count < MaxPooledStates)
                {
                    Pool.Push(state);
                    return;
                }
            }

            state.Dispose();
        }

        /// <summary>
        /// Disposes every pooled state when no rent occurred since the previous Gen2
        /// collection, returning all held buffers to the memory allocator while the
        /// renderer is idle. Invoked from the trim sentinel's finalizer.
        /// </summary>
        private static void TrimPoolIfIdle()
        {
            if (Interlocked.Exchange(ref rentedSinceLastGen2, 0) == 1)
            {
                return;
            }

            WorkerState<TPixel>[] trimmed;
            lock (PoolSync)
            {
                if (Pool.Count == 0)
                {
                    return;
                }

                trimmed = [.. Pool];
                Pool.Clear();
            }

            foreach (WorkerState<TPixel> state in trimmed)
            {
                state.Dispose();
            }
        }

        /// <summary>
        /// Returns a reusable raster scratch instance sized for the requested width.
        /// </summary>
        /// <param name="requiredWidth">The minimum scanline width required by the current row.</param>
        /// <returns>A scratch instance that can execute the row.</returns>
        public DefaultRasterizer.WorkerScratch GetOrCreateScratch(int requiredWidth)
        {
            DefaultRasterizer.WorkerScratch? current = this.scratch;
            if (current is not null && current.CanReuse(requiredWidth))
            {
                return current;
            }

            // Null before allocating: an allocation throw must not leave the field pointing at
            // the disposed scratch, or later rows read released memory and Dispose re-releases it.
            current?.Dispose();
            this.scratch = null;
            this.scratch = DefaultRasterizer.CreateWorkerScratch(this.allocator, requiredWidth);
            return this.scratch;
        }

        /// <summary>
        /// Returns a reusable raster scratch instance reserved for path clip rasterization.
        /// </summary>
        /// <param name="requiredWidth">The minimum scanline width required by the current clip row.</param>
        /// <returns>A scratch instance that can execute the clip row.</returns>
        public DefaultRasterizer.WorkerScratch GetOrCreatePathClipScratch(int requiredWidth)
        {
            DefaultRasterizer.WorkerScratch? current = this.pathClipScratch;
            if (current is not null && current.CanReuse(requiredWidth))
            {
                return current;
            }

            // Null before allocating; see GetOrCreateScratch.
            current?.Dispose();
            this.pathClipScratch = null;
            this.pathClipScratch = DefaultRasterizer.CreateWorkerScratch(this.allocator, requiredWidth);
            return this.pathClipScratch;
        }

        /// <summary>
        /// Returns a reusable coverage span used to combine one path clip with one subject span.
        /// </summary>
        /// <param name="requiredLength">The minimum coverage length required by the current subject span.</param>
        /// <returns>A worker-local coverage span.</returns>
        public Span<float> GetOrCreatePathClipCoverage(int requiredLength)
        {
            IMemoryOwner<float>? current = this.pathClipCoverageOwner;
            if (current is null || current.Memory.Length < requiredLength)
            {
                // Null before allocating; see GetOrCreateScratch.
                current?.Dispose();
                this.pathClipCoverageOwner = null;
                this.pathClipCoverageOwner = this.allocator.Allocate<float>(requiredLength);
            }

            this.pathClipCoverageLength = requiredLength;
            return this.PathClipCoverage;
        }

        /// <summary>
        /// Returns the worker-local band of path clip coverage for one clip descriptor index. Nested path clips have
        /// different indices, so each keeps its own band while the outer clip walks its row.
        /// </summary>
        /// <param name="clipIndex">The clip descriptor index.</param>
        /// <returns>The band for the index.</returns>
        public PathClipBand GetPathClipBand(int clipIndex)
        {
            if (clipIndex >= this.pathClipBands.Length)
            {
                Array.Resize(ref this.pathClipBands, clipIndex + 1);
            }

            return this.pathClipBands[clipIndex] ??= new PathClipBand(this.allocator);
        }

        /// <summary>
        /// Releases the worker-local scratch and brush workspace. The scratch fields are nulled
        /// so a disposed state can never hand out spans over returned pool memory.
        /// </summary>
        public void Dispose()
        {
            this.scratch?.Dispose();
            this.scratch = null;
            this.pathClipScratch?.Dispose();
            this.pathClipScratch = null;
            this.pathClipCoverageOwner?.Dispose();
            this.pathClipCoverageOwner = null;
            foreach (PathClipBand? band in this.pathClipBands)
            {
                band?.Dispose();
            }

            this.pathClipBands = [];
            this.BrushWorkspace.Dispose();
        }

        /// <summary>
        /// A finalization-resurrected sentinel that runs one pool trim check per garbage
        /// collection of its generation. After the first resurrection the instance is
        /// tenured, so in steady state the check runs once per Gen2 collection, matching
        /// the trimming cadence of the pooled memory allocator itself.
        /// </summary>
        private sealed class Gen2TrimSentinel
        {
            /// <summary>
            /// Finalizes an instance of the <see cref="Gen2TrimSentinel"/> class.
            /// Runs the idle trim and resurrects the sentinel for the next collection.
            /// </summary>
            ~Gen2TrimSentinel()
            {
                // Never resurrect during teardown: the runtime is finalizing for exit and
                // the pool's buffers are reclaimed with the process.
                if (Environment.HasShutdownStarted || AppDomain.CurrentDomain.IsFinalizingForUnload())
                {
                    return;
                }

                TrimPoolIfIdle();
                GC.ReRegisterForFinalize(this);
            }
        }
    }
}
