// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feMorphology</c> filter primitive: erodes (takes the minimum) or dilates (takes the maximum) its input over a
/// rectangle of <c>2 rx + 1</c> by <c>2 ry + 1</c> pixels, per channel on premultiplied values.
/// </summary>
/// <remarks>
/// <para>
/// Attributes, from Filter Effects Level 1: <c>operator</c> is <c>erode</c> (the initial value, also for an invalid
/// keyword) or <c>dilate</c>. <c>radius</c> holds one number for both axes or two for x and y, in user units (scaled
/// from <c>primitiveUnits</c>). A missing radius, an empty one, and a list of another length take the initial value 0.
/// Filter Effects says that a negative or zero value disables the primitive, whose result is then its input. Chromium,
/// whose renders match the reference images of the conformance suite, reads that per axis: a radius of <c>10 0</c>
/// erodes horizontally only, and a negative number counts as 0 for its axis. The primitive follows Chromium for one
/// axis, because the specification text does not say which reading applies to a list of two numbers; when neither axis
/// is positive, the primitive is a pass-through, as the specification says.
/// </para>
/// <para>
/// Pixels: the radius in filter pixels is the radius in user units times the filter scale, rounded to the nearest whole
/// pixel (halves round up), as Chromium renders it: 0.5 user units at scale 1 erode by one pixel, 0.3 by none. The input
/// is transparent black outside its area, so an erosion whose window leaves the input area gives transparent black and a
/// dilation simply ignores the part of the window outside it. The radius is limited to
/// <see cref="SvgFilterEngine.MaxSide"/> pixels: a larger window covers every image the engine creates.
/// </para>
/// <para>
/// The operation is separable: a horizontal pass into an intermediate image and a vertical pass into the output. Each
/// pass is the van Herk and Gil-Werman algorithm: the line is cut into blocks of the window length, a running extremum
/// is taken forward from each block start and backward from each block end, and the extremum of any window is the
/// extremum of two of those values. The cost per pixel is three comparisons for any radius. The vertical pass works on
/// whole rows of bytes, so it uses the vectorized byte minimum and maximum of <see cref="Vector{T}"/>.
/// </para>
/// </remarks>
internal sealed class SvgFeMorphology : SvgFilterPrimitive
{
    private readonly Vector2 radius;
    private readonly bool dilate;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeMorphology"/> class.
    /// </summary>
    /// <param name="input">The input index.</param>
    /// <param name="radius">The radius along x and y in user units; neither is negative and one is positive.</param>
    /// <param name="dilate">Whether the operator is <c>dilate</c>; otherwise <c>erode</c>.</param>
    private SvgFeMorphology(int input, Vector2 radius, bool dilate)
        : base([input])
    {
        this.radius = radius;
        this.dilate = dilate;
    }

    /// <inheritdoc/>
    /// <remarks>Each channel of the output is the minimum or maximum of the same channel of input pixels.</remarks>
    public override bool PreservesAlphaOnly => true;

    /// <summary>
    /// Creates the primitive of an <c>feMorphology</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>, or <see cref="SvgFilterBuildResult.PassThrough"/> when no radius is positive.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        primitive = null;
        if (!context.TryGetValue(SvgAttributeId.Radius, out SvgFilterAttributeValue value) || value.NumberCount is not (1 or 2))
        {
            return SvgFilterBuildResult.PassThrough;
        }

        ReadOnlySpan<float> numbers = context.GetNumbers(in value);
        float x = MathF.Max(numbers[0], 0F);
        float y = MathF.Max(numbers.Length == 2 ? numbers[1] : numbers[0], 0F);
        if (x == 0F && y == 0F)
        {
            return SvgFilterBuildResult.PassThrough;
        }

        bool dilate = context.TryGetValue(SvgAttributeId.Operator, out SvgFilterAttributeValue op) && op.Text == "dilate";
        primitive = new SvgFeMorphology(context.GetInput(context.Attributes.In), new Vector2(context.ToUserX(x), context.ToUserY(y)), dilate);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override Rectangle GetInputArea(int input, Rectangle area, in SvgFilterRenderContext context)
    {
        Size radius = this.GetPixelRadius(in context);
        return Rectangle.Inflate(area, radius.Width, radius.Height);
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        SvgFilterImage input = inputs[0];
        if (input.IsEmpty)
        {
            return;
        }

        Size radius = this.GetPixelRadius(in context);
        Rectangle area = output.Area;
        Rectangle source = input.Area;
        int rx = radius.Width;
        int ry = radius.Height;

        // The intermediate image holds the rows that the vertical pass reads (the output rows widened by the vertical
        // radius) and that have input data; rows outside the input are transparent. For an erosion, a window that leaves
        // the input is transparent black, so only rows inside the input matter for either operator.
        int top = Math.Max(area.Top - ry, source.Top);
        int bottom = Math.Min(area.Bottom + ry, source.Bottom);
        if (bottom <= top)
        {
            return;
        }

        int width = area.Width;
        using SvgFilterImage intermediate = new(context.Allocator, Rectangle.FromLTRB(area.Left, top, area.Right, bottom), output.ColorSpace);
        using (IMemoryOwner<Rgba32> lines = context.Allocator.Allocate<Rgba32>(source.Width * 2))
        {
            Span<Rgba32> forward = lines.Memory.Span[..source.Width];
            Span<Rgba32> backward = lines.Memory.Span.Slice(source.Width, source.Width);
            for (int y = top; y < bottom; y++)
            {
                this.FilterRow(input.GetRow(y), source.Left, area.Left, rx, intermediate.GetRow(y), forward, backward);
            }
        }

        // Vertical pass over whole rows. The intermediate rows are the input rows that the windows can reach, so a window
        // that leaves them leaves the input.
        int rows = bottom - top;
        using IMemoryOwner<Rgba32> blocks = context.Allocator.Allocate<Rgba32>(rows * width * 2);
        Span<Rgba32> forwardRows = blocks.Memory.Span[..(rows * width)];
        Span<Rgba32> backwardRows = blocks.Memory.Span.Slice(rows * width, rows * width);
        this.FilterColumns(intermediate, area, ry, forwardRows, backwardRows, output);
    }

    /// <summary>
    /// Returns the window extremum of two pixels, per channel.
    /// </summary>
    /// <param name="a">The first pixel.</param>
    /// <param name="b">The second pixel.</param>
    /// <param name="dilate">Whether to take the maximum; otherwise the minimum.</param>
    /// <returns>The pixel of channel extrema.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba32 Extremum(Rgba32 a, Rgba32 b, bool dilate)
        => dilate
            ? new Rgba32(Math.Max(a.R, b.R), Math.Max(a.G, b.G), Math.Max(a.B, b.B), Math.Max(a.A, b.A))
            : new Rgba32(Math.Min(a.R, b.R), Math.Min(a.G, b.G), Math.Min(a.B, b.B), Math.Min(a.A, b.A));

    /// <summary>
    /// Computes the running extrema of the van Herk and Gil-Werman algorithm over a line of pixels: forward from the start
    /// of each block of <paramref name="window"/> pixels, and backward from its end, the last block ending at the line end.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="window">The block length, the window length.</param>
    /// <param name="dilate">Whether to take maxima; otherwise minima.</param>
    /// <param name="forward">Receives the forward extrema.</param>
    /// <param name="backward">Receives the backward extrema.</param>
    private static void Prepare(ReadOnlySpan<Rgba32> line, int window, bool dilate, Span<Rgba32> forward, Span<Rgba32> backward)
    {
        int length = line.Length;
        for (int start = 0; start < length; start += window)
        {
            int end = Math.Min(start + window, length);
            forward[start] = line[start];
            for (int i = start + 1; i < end; i++)
            {
                forward[i] = Extremum(forward[i - 1], line[i], dilate);
            }

            backward[end - 1] = line[end - 1];
            for (int i = end - 2; i >= start; i--)
            {
                backward[i] = Extremum(backward[i + 1], line[i], dilate);
            }
        }
    }

    /// <summary>
    /// Returns the radius in filter pixels.
    /// </summary>
    /// <param name="context">The context of the run.</param>
    /// <returns>The radius along x and y, each between 0 and <see cref="SvgFilterEngine.MaxSide"/>.</returns>
    private Size GetPixelRadius(in SvgFilterRenderContext context)
    {
        // Round half up, as Chromium does: 0.5 pixels erode by one pixel. The clamp also keeps the conversion defined.
        Vector2 pixels = Vector2.Clamp((this.radius * context.Scale) + new Vector2(0.5F), Vector2.Zero, new Vector2(SvgFilterEngine.MaxSide));
        return new Size((int)pixels.X, (int)pixels.Y);
    }

    /// <summary>
    /// Runs the horizontal pass over one row.
    /// </summary>
    /// <param name="row">The input row, from column <paramref name="sourceLeft"/>.</param>
    /// <param name="sourceLeft">The first column of the input.</param>
    /// <param name="left">The first output column.</param>
    /// <param name="rx">The horizontal radius in pixels.</param>
    /// <param name="destination">The output row, from column <paramref name="left"/>.</param>
    /// <param name="forward">Scratch for the forward extrema, as long as the input row.</param>
    /// <param name="backward">Scratch for the backward extrema, as long as the input row.</param>
    private void FilterRow(
        ReadOnlySpan<Rgba32> row,
        int sourceLeft,
        int left,
        int rx,
        Span<Rgba32> destination,
        Span<Rgba32> forward,
        Span<Rgba32> backward)
    {
        int length = row.Length;
        if (rx == 0)
        {
            // A window of one pixel copies the input; columns outside it are transparent.
            int start = Math.Max(left, sourceLeft);
            int end = Math.Min(left + destination.Length, sourceLeft + length);
            if (start < end)
            {
                row.Slice(start - sourceLeft, end - start).CopyTo(destination[(start - left)..]);
            }

            return;
        }

        int window = (2 * rx) + 1;
        Prepare(row, window, this.dilate, forward, backward);
        for (int x = 0; x < destination.Length; x++)
        {
            // The window of output column x in input coordinates, clipped to the input.
            int lo = left + x - rx - sourceLeft;
            int hi = lo + window - 1;
            if (lo < 0 || hi >= length)
            {
                // An erosion with a window that leaves the input gives transparent black (the destination is already
                // transparent). A dilation takes the extremum of the part inside.
                if (!this.dilate)
                {
                    continue;
                }

                lo = Math.Max(lo, 0);
                hi = Math.Min(hi, length - 1);
                if (lo > hi)
                {
                    continue;
                }
            }

            destination[x] = Window(forward, backward, lo, hi, window, length, this.dilate);
        }
    }

    /// <summary>
    /// Runs the vertical pass: for each output row, the extremum over the rows of its window in the intermediate image.
    /// </summary>
    /// <param name="intermediate">The result of the horizontal pass; its columns are the output columns.</param>
    /// <param name="area">The output area.</param>
    /// <param name="ry">The vertical radius in pixels.</param>
    /// <param name="forward">Scratch for the forward extrema of every intermediate row.</param>
    /// <param name="backward">Scratch for the backward extrema of every intermediate row.</param>
    /// <param name="output">The output image.</param>
    private void FilterColumns(SvgFilterImage intermediate, Rectangle area, int ry, Span<Rgba32> forward, Span<Rgba32> backward, SvgFilterImage output)
    {
        Rectangle rows = intermediate.Area;
        int width = area.Width;
        int count = rows.Height;
        int window = (2 * ry) + 1;

        // The running extrema of the algorithm over whole rows: row r of forward is the extremum of the rows from its
        // block start to r. The operations are per byte, so they run on vectors of bytes over the row.
        Span<byte> pixels = MemoryMarshal.AsBytes(intermediate.Pixels);
        Span<byte> forwardBytes = MemoryMarshal.AsBytes(forward);
        Span<byte> backwardBytes = MemoryMarshal.AsBytes(backward);
        int stride = width * 4;
        for (int start = 0; start < count; start += window)
        {
            int end = Math.Min(start + window, count);
            pixels.Slice(start * stride, stride).CopyTo(forwardBytes.Slice(start * stride, stride));
            for (int r = start + 1; r < end; r++)
            {
                Combine(forwardBytes.Slice((r - 1) * stride, stride), pixels.Slice(r * stride, stride), forwardBytes.Slice(r * stride, stride), this.dilate);
            }

            pixels.Slice((end - 1) * stride, stride).CopyTo(backwardBytes.Slice((end - 1) * stride, stride));
            for (int r = end - 2; r >= start; r--)
            {
                Combine(backwardBytes.Slice((r + 1) * stride, stride), pixels.Slice(r * stride, stride), backwardBytes.Slice(r * stride, stride), this.dilate);
            }
        }

        for (int y = area.Top; y < area.Bottom; y++)
        {
            int lo = y - ry - rows.Top;
            int hi = lo + window - 1;
            if (lo < 0 || hi >= count)
            {
                if (!this.dilate)
                {
                    continue;
                }

                lo = Math.Max(lo, 0);
                hi = Math.Min(hi, count - 1);
                if (lo > hi)
                {
                    continue;
                }
            }

            Span<byte> destination = MemoryMarshal.AsBytes(output.GetRow(y));
            int block = lo / window;
            if (block != hi / window)
            {
                // The window spans the end of one block and the start of the next.
                Combine(backwardBytes.Slice(lo * stride, stride), forwardBytes.Slice(hi * stride, stride), destination, this.dilate);
            }
            else if (lo == block * window)
            {
                forwardBytes.Slice(hi * stride, stride).CopyTo(destination);
            }
            else
            {
                // A window clipped by the last row ends where the last block ends.
                backwardBytes.Slice(lo * stride, stride).CopyTo(destination);
            }
        }
    }

    /// <summary>
    /// Returns the extremum of a window from the running extrema: the window <c>[lo, hi]</c> is at most one block long,
    /// so it is the end of one block and the start of the next, or lies inside one block.
    /// </summary>
    /// <param name="forward">The forward extrema.</param>
    /// <param name="backward">The backward extrema.</param>
    /// <param name="lo">The first index of the window.</param>
    /// <param name="hi">The last index of the window.</param>
    /// <param name="window">The block length.</param>
    /// <param name="length">The line length.</param>
    /// <param name="dilate">Whether to take the maximum.</param>
    /// <returns>The extremum.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba32 Window(ReadOnlySpan<Rgba32> forward, ReadOnlySpan<Rgba32> backward, int lo, int hi, int window, int length, bool dilate)
    {
        int block = lo / window;
        if (block != hi / window)
        {
            return Extremum(backward[lo], forward[hi], dilate);
        }

        // Inside one block: a window that starts at the block start is a forward extremum. Otherwise the window was
        // clipped at the line end, where the last block ends, so it is a backward extremum.
        return lo == block * window ? forward[hi] : backward[lo];
    }

    /// <summary>
    /// Writes the per-byte minimum or maximum of two byte spans.
    /// </summary>
    /// <param name="a">The first span.</param>
    /// <param name="b">The second span, as long as the first.</param>
    /// <param name="destination">The destination, as long as the first; it can be one of the sources.</param>
    /// <param name="dilate">Whether to take the maximum; otherwise the minimum.</param>
    private static void Combine(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> destination, bool dilate)
    {
        // Lanes are independent bytes (the channels of consecutive pixels), so a vector covers Vector<byte>.Count / 4
        // pixels; the remainder runs as scalar code with the same per-byte result.
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            int count = Vector<byte>.Count;
            for (; i <= a.Length - count; i += count)
            {
                Vector<byte> x = new(a[i..]);
                Vector<byte> y = new(b[i..]);
                (dilate ? Vector.Max(x, y) : Vector.Min(x, y)).CopyTo(destination[i..]);
            }
        }

        for (; i < a.Length; i++)
        {
            destination[i] = dilate ? Math.Max(a[i], b[i]) : Math.Min(a[i], b[i]);
        }
    }
}
