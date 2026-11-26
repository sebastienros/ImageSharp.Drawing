// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feConvolveMatrix</c> filter primitive: convolves its input with a kernel of <c>orderX</c> by <c>orderY</c>
/// entries.
/// </summary>
/// <remarks>
/// <para>
/// The formula of Filter Effects Level 1, per channel of premultiplied values:
/// <c>result(x, y) = sum(source(x - targetX + j, y - targetY + i) * kernelMatrix[orderX - j - 1, orderY - i - 1]) / divisor + bias</c>,
/// over <c>0 &lt;= i &lt; orderY</c> and <c>0 &lt;= j &lt; orderX</c>: the kernel is rotated 180 degrees against the image.
/// With <c>preserveAlpha="true"</c> the formula applies to the unpremultiplied color channels and the alpha of the
/// result is the alpha of the source pixel. The result is clamped to valid premultiplied values. The bias is added to
/// each channel as it is, which is what Chromium renders; the specification writes it as <c>bias * alpha</c> without
/// saying which alpha, and the conformance suite marks its bias tests as undefined.
/// </para>
/// <para>
/// Attributes. <c>order</c> holds one or two numbers, truncated toward zero; missing or not a list of one or two numbers,
/// it is the initial 3. <c>targetX</c> and <c>targetY</c> default to <c>floor(order / 2)</c>, also for a value that is
/// not one integer. A <c>divisor</c> that is missing or 0 is the sum of the kernel, or 1 when the sum is 0.
/// <c>edgeMode</c> is <c>duplicate</c> (the initial value, also for an invalid keyword), <c>wrap</c>, or <c>none</c>.
/// <c>kernelUnitLength</c> has no effect, as in Chromium: the kernel always steps by one filter pixel.
/// </para>
/// <para>
/// Unfulfilled preconditions make the primitive a pass-through, as Filter Effects Level 1 defines (the entry count of
/// <c>kernelMatrix</c> is not <c>orderX * orderY</c>, and its change list extends this to every precondition): an order
/// below 1, a missing <c>kernelMatrix</c> or one with another number of entries, and a target outside the kernel.
/// SVG 1.1 made these cases errors, and Chromium renders them as transparent black; the reference images of the
/// conformance suite show the element not drawn.
/// </para>
/// <para>
/// Edges. <c>duplicate</c> and <c>wrap</c> extend the image at the edges of the primitive subregion, which is how
/// Chromium renders them; <c>none</c> reads the input as it is, transparent black outside its area (and outside the
/// filter region), as Chromium also renders it.
/// </para>
/// <para>
/// Limits. A valid kernel of more than <see cref="MaxKernelEntries"/> entries gives transparent black, as Chromium
/// renders it in sRGB (in linearRGB Chromium shows the input with a wrong color conversion instead). The work of one
/// run is then at most <see cref="MaxKernelEntries"/> multiply-adds of four channels per output pixel, and the scratch
/// memory is <c>orderY</c> rows of 16 bytes per pixel, the width of the output plus <c>orderX - 1</c>.
/// </para>
/// </remarks>
internal sealed class SvgFeConvolveMatrix : SvgFilterPrimitive
{
    /// <summary>
    /// The largest number of kernel entries, <c>orderX * orderY</c>.
    /// </summary>
    internal const int MaxKernelEntries = 256;

    /// <summary>
    /// The largest order and target that the attributes convert to an integer; larger values cannot match a list of
    /// numbers in memory.
    /// </summary>
    private const float MaxOrder = 1 << 24;

    private readonly int orderX;
    private readonly int orderY;
    private readonly int targetX;
    private readonly int targetY;

    // Weight of the tap at row i and column j of the window, kernelMatrix[orderX - j - 1, orderY - i - 1] / divisor,
    // stored row by row so that the convolution walks the window and the weights in the same order.
    private readonly float[] weights;
    private readonly float bias;
    private readonly EdgeMode edgeMode;
    private readonly bool preserveAlpha;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeConvolveMatrix"/> class.
    /// </summary>
    /// <param name="input">The input index.</param>
    /// <param name="orderX">The number of kernel columns.</param>
    /// <param name="orderY">The number of kernel rows.</param>
    /// <param name="targetX">The kernel column over the output pixel.</param>
    /// <param name="targetY">The kernel row over the output pixel.</param>
    /// <param name="weights">The weights in window order, divided by the divisor.</param>
    /// <param name="bias">The bias.</param>
    /// <param name="edgeMode">The edge mode.</param>
    /// <param name="preserveAlpha">Whether the alpha channel is kept and the colors convolve unpremultiplied.</param>
    private SvgFeConvolveMatrix(
        int input,
        int orderX,
        int orderY,
        int targetX,
        int targetY,
        float[] weights,
        float bias,
        EdgeMode edgeMode,
        bool preserveAlpha)
        : base([input])
    {
        this.orderX = orderX;
        this.orderY = orderY;
        this.targetX = targetX;
        this.targetY = targetY;
        this.weights = weights;
        this.bias = bias;
        this.edgeMode = edgeMode;
        this.preserveAlpha = preserveAlpha;
    }

    /// <summary>
    /// How the input extends beyond the edges of the primitive subregion.
    /// </summary>
    private enum EdgeMode
    {
        /// <summary>
        /// The edge pixels repeat.
        /// </summary>
        Duplicate,

        /// <summary>
        /// The image repeats from the opposite edge.
        /// </summary>
        Wrap,

        /// <summary>
        /// The input is read as it is.
        /// </summary>
        None
    }

    /// <summary>
    /// Creates the primitive of an <c>feConvolveMatrix</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns>
    /// <see cref="SvgFilterBuildResult.Created"/>; <see cref="SvgFilterBuildResult.PassThrough"/> when a precondition fails;
    /// <see cref="SvgFilterBuildResult.Transparent"/> for a kernel above the limit.
    /// </returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        primitive = null;
        int orderX = 3;
        int orderY = 3;
        if (context.TryGetValue(SvgAttributeId.Order, out SvgFilterAttributeValue order) && order.NumberCount is 1 or 2)
        {
            // Truncation toward zero; the clamp keeps the conversion defined for huge values, whose entry count no
            // list can match.
            ReadOnlySpan<float> numbers = context.GetNumbers(in order);
            orderX = (int)Math.Clamp(numbers[0], -1F, MaxOrder);
            orderY = numbers.Length == 2 ? (int)Math.Clamp(numbers[1], -1F, MaxOrder) : orderX;
        }

        long entries = (long)orderX * orderY;
        if (orderX < 1 || orderY < 1
            || !context.TryGetValue(SvgAttributeId.KernelMatrix, out SvgFilterAttributeValue matrix)
            || matrix.NumberCount != entries)
        {
            return SvgFilterBuildResult.PassThrough;
        }

        int targetX = GetTarget(context, SvgAttributeId.TargetX, orderX);
        int targetY = GetTarget(context, SvgAttributeId.TargetY, orderY);
        if ((uint)targetX >= (uint)orderX || (uint)targetY >= (uint)orderY)
        {
            return SvgFilterBuildResult.PassThrough;
        }

        if (entries > MaxKernelEntries)
        {
            return SvgFilterBuildResult.Transparent;
        }

        ReadOnlySpan<float> kernel = context.GetNumbers(in matrix);
        float divisor = GetNumber(context, SvgAttributeId.Divisor, 0F);
        if (divisor == 0F)
        {
            // The entries are decimal numbers stored as floats, so a kernel such as eight times 0.1 and one -0.8 sums to
            // a tiny value instead of 0, which would divide the result by almost nothing. The sum runs in double precision
            // and counts as 0 when it is below the rounding error of the entries.
            double sum = 0;
            double magnitude = 0;
            foreach (float value in kernel)
            {
                sum += value;
                magnitude += Math.Abs(value);
            }

            divisor = Math.Abs(sum) <= magnitude * 1e-6 ? 1F : (float)sum;
        }

        // Window tap (i, j) reads source(x - targetX + j, y - targetY + i) with kernel entry
        // (orderX - j - 1, orderY - i - 1), which is entry count - 1 - (i * orderX + j) of the row-major list.
        float[] weights = new float[kernel.Length];
        for (int k = 0; k < weights.Length; k++)
        {
            weights[k] = kernel[kernel.Length - 1 - k] / divisor;
        }

        EdgeMode edgeMode = EdgeMode.Duplicate;
        if (context.TryGetValue(SvgAttributeId.EdgeMode, out SvgFilterAttributeValue edge))
        {
            edgeMode = edge.Text switch
            {
                "wrap" => EdgeMode.Wrap,
                "none" => EdgeMode.None,
                _ => EdgeMode.Duplicate
            };
        }

        bool preserveAlpha = context.TryGetValue(SvgAttributeId.PreserveAlpha, out SvgFilterAttributeValue alpha) && alpha.Text == "true";
        primitive = new SvgFeConvolveMatrix(
            context.GetInput(context.Attributes.In),
            orderX,
            orderY,
            targetX,
            targetY,
            weights,
            GetNumber(context, SvgAttributeId.Bias, 0F),
            edgeMode,
            preserveAlpha);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override Rectangle GetInputArea(int input, Rectangle area, in SvgFilterRenderContext context)
    {
        // Output pixel (x, y) reads the columns x - targetX to x - targetX + orderX - 1, and the rows likewise.
        Rectangle window = new(area.X - this.targetX, area.Y - this.targetY, area.Width + this.orderX - 1, area.Height + this.orderY - 1);
        Rectangle edges = context.Subregion;
        return this.edgeMode switch
        {
            // Positions outside the subregion repeat its edge pixels, which lie inside the window.
            EdgeMode.Duplicate => Rectangle.Intersect(window, edges),

            // Positions outside the subregion come from its opposite side, which can be anywhere in it.
            EdgeMode.Wrap => edges.Contains(window) ? window : edges,
            _ => window
        };
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        SvgFilterImage input = inputs[0];
        if (input.IsEmpty)
        {
            return;
        }

        Rectangle area = output.Area;
        Rectangle edges = context.Subregion;
        int width = area.Width;
        int lineWidth = width + this.orderX - 1;
        int lineLeft = area.Left - this.targetX;
        int orderY = this.orderY;

        // A ring of orderY source lines in floating point: virtual row v (a row of the extended image) lives in slot
        // v mod orderY. Each line is converted once and read by orderY output rows.
        using IMemoryOwner<Vector4> buffer = context.Allocator.Allocate<Vector4>((lineWidth * orderY) + width);
        using IMemoryOwner<Rgba32> rowBuffer = context.Allocator.Allocate<Rgba32>(lineWidth);
        Span<Vector4> lines = buffer.Memory.Span[..(lineWidth * orderY)];
        Span<Vector4> sums = buffer.Memory.Span.Slice(lineWidth * orderY, width);
        Span<Rgba32> row = rowBuffer.Memory.Span;

        int firstRow = area.Top - this.targetY;
        for (int v = firstRow; v < firstRow + orderY - 1; v++)
        {
            this.FillLine(input, edges, v, lineLeft, row, lines.Slice(Slot(v, orderY) * lineWidth, lineWidth));
        }

        ReadOnlySpan<float> weights = this.weights;
        Vector4 bias = new(this.bias);
        for (int y = area.Top; y < area.Bottom; y++)
        {
            // The window of row y spans the virtual rows y - targetY to y - targetY + orderY - 1; the last one is new.
            int top = y - this.targetY;
            int newest = top + orderY - 1;
            this.FillLine(input, edges, newest, lineLeft, row, lines.Slice(Slot(newest, orderY) * lineWidth, lineWidth));

            // Accumulate tap by tap over the whole row: sums[x] += line[x + j] * weight. The inner loop is a
            // multiply-add of Vector4 values (four channels in one SIMD register) over contiguous memory.
            sums.Clear();
            for (int i = 0; i < orderY; i++)
            {
                ReadOnlySpan<Vector4> line = lines.Slice(Slot(top + i, orderY) * lineWidth, lineWidth);
                ReadOnlySpan<float> rowWeights = weights.Slice(i * this.orderX, this.orderX);
                for (int j = 0; j < rowWeights.Length; j++)
                {
                    float weight = rowWeights[j];
                    if (weight == 0F)
                    {
                        continue;
                    }

                    ReadOnlySpan<Vector4> taps = line.Slice(j, width);
                    for (int x = 0; x < sums.Length; x++)
                    {
                        sums[x] += taps[x] * weight;
                    }
                }
            }

            Span<Rgba32> destination = output.GetRow(y);
            if (this.preserveAlpha)
            {
                // The colors convolved unpremultiplied; the alpha is the alpha of the source pixel.
                for (int x = 0; x < destination.Length; x++)
                {
                    Vector4 color = sums[x] + bias;
                    color.W = input.GetPixel(area.Left + x, y).A / 255F;
                    destination[x] = SvgFilterColor.Premultiply(color);
                }
            }
            else
            {
                for (int x = 0; x < destination.Length; x++)
                {
                    destination[x] = SvgFilterColor.FromPremultiplied(sums[x] + bias);
                }
            }
        }
    }

    /// <summary>
    /// Returns the ring slot of a virtual row.
    /// </summary>
    /// <param name="row">The virtual row.</param>
    /// <param name="count">The number of slots.</param>
    /// <returns>The slot, from 0 to <paramref name="count"/> - 1.</returns>
    private static int Slot(int row, int count)
    {
        int slot = row % count;
        return slot < 0 ? slot + count : slot;
    }

    /// <summary>
    /// Reads a target attribute.
    /// </summary>
    /// <param name="context">The build context.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="order">The order along the axis of the target.</param>
    /// <returns>The target, which can be out of range; <c>floor(order / 2)</c> when the attribute is missing or not one integer.</returns>
    private static int GetTarget(SvgFilterBuildContext context, SvgAttributeId attribute, int order)
    {
        if (context.TryGetValue(attribute, out SvgFilterAttributeValue value) && value.NumberCount == 1)
        {
            float number = context.GetNumbers(in value)[0];
            if (number == MathF.Floor(number))
            {
                // Any integer outside the order fails the precondition; the clamp keeps the conversion defined.
                return (int)Math.Clamp(number, -1F, MaxOrder);
            }
        }

        return order / 2;
    }

    /// <summary>
    /// Reads a number attribute.
    /// </summary>
    /// <param name="context">The build context.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="initial">The value when the attribute is missing or not exactly one number.</param>
    /// <returns>The number.</returns>
    private static float GetNumber(SvgFilterBuildContext context, SvgAttributeId attribute, float initial)
        => context.TryGetValue(attribute, out SvgFilterAttributeValue value) && value.NumberCount == 1 ? context.GetNumbers(in value)[0] : initial;

    /// <summary>
    /// Maps a coordinate of the extended image to the input through the edge mode.
    /// </summary>
    /// <param name="value">The coordinate.</param>
    /// <param name="start">The first coordinate of the subregion along the axis.</param>
    /// <param name="length">The length of the subregion along the axis.</param>
    /// <returns>The coordinate to read.</returns>
    private int MapEdge(int value, int start, int length)
    {
        int offset = value - start;
        if ((uint)offset < (uint)length)
        {
            return value;
        }

        return this.edgeMode switch
        {
            EdgeMode.Duplicate => offset < 0 ? start : start + length - 1,
            EdgeMode.Wrap => start + Slot(offset, length),
            _ => value
        };
    }

    /// <summary>
    /// Fills one line of the ring: a row of the extended image as floating point channels in the range 0 to 1,
    /// premultiplied, or unpremultiplied colors when the alpha is preserved.
    /// </summary>
    /// <param name="input">The input image.</param>
    /// <param name="edges">The subregion, whose edges the edge mode extends.</param>
    /// <param name="v">The virtual row.</param>
    /// <param name="left">The virtual column of the first entry.</param>
    /// <param name="row">Scratch for the pixels of the row.</param>
    /// <param name="line">The line to fill.</param>
    private void FillLine(SvgFilterImage input, Rectangle edges, int v, int left, Span<Rgba32> row, Span<Vector4> line)
    {
        int y = this.MapEdge(v, edges.Top, edges.Height);
        if (this.edgeMode == EdgeMode.None || (left >= edges.Left && left + row.Length <= edges.Right))
        {
            input.CopyRow(y, left, row);
        }
        else
        {
            for (int x = 0; x < row.Length; x++)
            {
                row[x] = input.GetPixel(this.MapEdge(left + x, edges.Left, edges.Width), y);
            }
        }

        if (this.preserveAlpha)
        {
            for (int x = 0; x < row.Length; x++)
            {
                line[x] = SvgFilterColor.Unpremultiply(row[x]);
            }
        }
        else
        {
            const float scale = 1F / 255F;
            for (int x = 0; x < row.Length; x++)
            {
                Rgba32 pixel = row[x];
                line[x] = new Vector4(pixel.R, pixel.G, pixel.B, pixel.A) * scale;
            }
        }
    }
}
