// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feGaussianBlur</c> filter primitive: blurs its input with a Gaussian of a standard deviation per axis.
/// </summary>
/// <remarks>
/// <para>
/// <c>stdDeviation</c> holds one number for both axes or two for x and y, in user units (scaled from
/// <c>primitiveUnits</c>). Filter Effects: a zero or negative value disables the primitive, whose result is then its
/// input; zero on one axis blurs only along the other. A missing value is the initial 0, and a list of another length is
/// invalid and takes the initial value too. <c>edgeMode</c> is not applied: the input extends with transparent black, the
/// initial <c>none</c>.
/// </para>
/// <para>
/// The blur is separable and runs on premultiplied channels, horizontally into an intermediate image and then
/// vertically. For a standard deviation <c>s</c> of 2 filter pixels or more, each pass is the three box blurs of size
/// <c>d = floor(s * 3 * sqrt(2 * pi) / 4 + 0.5)</c> that Filter Effects describes (for an even <c>d</c>, two boxes of
/// size <c>d</c> centered half a pixel left and right of the output pixel and one of size <c>d + 1</c>); each box is a
/// running sum, so the cost per pixel does not grow with <c>s</c>. Below 2 pixels a true Gaussian kernel of radius
/// <c>ceil(3 s)</c> runs instead. Every pass rounds to 8 bits, as the reference renderers store their intermediate images.
/// </para>
/// <para>
/// A standard deviation is limited to <see cref="MaxDeviation"/> filter pixels, which bounds the reach and the scratch
/// memory: at that size a Gaussian spreads an image of the largest allowed side to almost nothing.
/// </para>
/// </remarks>
internal sealed class SvgFeGaussianBlur : SvgFilterPrimitive
{
    /// <summary>
    /// The largest standard deviation in filter pixels.
    /// </summary>
    internal const float MaxDeviation = SvgFilterEngine.MaxSide / 2F;

    private readonly Vector2 deviation;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeGaussianBlur"/> class.
    /// </summary>
    /// <param name="input">The input index.</param>
    /// <param name="deviation">The standard deviation along x and y in user units; neither is negative and one is positive.</param>
    internal SvgFeGaussianBlur(int input, Vector2 deviation)
        : base([input])
        => this.deviation = deviation;

    /// <summary>
    /// Creates the primitive of an <c>feGaussianBlur</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>, or <see cref="SvgFilterBuildResult.PassThrough"/> when the blur is disabled.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        primitive = null;
        if (!context.TryGetValue(SvgAttributeId.StdDeviation, out SvgFilterAttributeValue value)
            || value.NumberCount is not (1 or 2))
        {
            return SvgFilterBuildResult.PassThrough;
        }

        ReadOnlySpan<float> numbers = context.GetNumbers(in value);
        float x = numbers[0];
        float y = numbers.Length == 2 ? numbers[1] : numbers[0];
        if (x < 0F || y < 0F || (x == 0F && y == 0F))
        {
            return SvgFilterBuildResult.PassThrough;
        }

        primitive = new SvgFeGaussianBlur(context.GetInput(context.Attributes.In), new Vector2(context.ToUserX(x), context.ToUserY(y)));
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override Rectangle GetInputArea(int input, Rectangle area, in SvgFilterRenderContext context)
    {
        Size reach = GetReach(this.deviation * context.Scale);
        return Rectangle.Inflate(area, reach.Width, reach.Height);
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
        => Blur(inputs[0], output, this.deviation * context.Scale, context.Allocator);

    /// <summary>
    /// Returns how far a blur reaches: the distance in pixels from an output pixel to the farthest input pixel it reads.
    /// </summary>
    /// <param name="deviation">The standard deviation along x and y in filter pixels; neither is negative.</param>
    /// <returns>The reach along x and y.</returns>
    internal static Size GetReach(Vector2 deviation)
        => new(GetPasses(deviation.X).Reach, GetPasses(deviation.Y).Reach);

    /// <summary>
    /// Blurs an image into the area of another. The output must be transparent; the input can have any area.
    /// </summary>
    /// <param name="input">The input image.</param>
    /// <param name="output">The output image, transparent, not empty.</param>
    /// <param name="deviation">The standard deviation along x and y in filter pixels; neither is negative.</param>
    /// <param name="allocator">The allocator for scratch memory.</param>
    internal static void Blur(SvgFilterImage input, SvgFilterImage output, Vector2 deviation, MemoryAllocator allocator)
    {
        if (input.IsEmpty)
        {
            return;
        }

        Rectangle area = output.Area;
        Rectangle source = input.Area;
        Passes horizontal = GetPasses(deviation.X);
        Passes vertical = GetPasses(deviation.Y);
        int reachX = horizontal.Reach;
        int reachY = vertical.Reach;

        // Rows of the intermediate image: the output rows widened by the vertical reach, limited to the input rows,
        // because the horizontal pass does not spread across rows.
        int top = Math.Max(area.Top - reachY, source.Top);
        int bottom = Math.Min(area.Bottom + reachY, source.Bottom);
        if (bottom <= top)
        {
            return;
        }

        // The horizontal line spans the output columns widened by the reach, limited to where data can be: the input
        // columns widened by the reach, because each pass spreads data by at most its own reach. It always holds the
        // output columns, which can lie beyond the data.
        int lineLeft = Math.Min(area.Left, Math.Max(area.Left - reachX, source.Left - reachX));
        int lineRight = Math.Max(area.Right, Math.Min(area.Right + reachX, source.Right + reachX));
        int columnTop = Math.Min(area.Top, Math.Max(area.Top - reachY, top - reachY));
        int columnBottom = Math.Max(area.Bottom, Math.Min(area.Bottom + reachY, bottom + reachY));
        int lineLength = Math.Max(Math.Max(lineRight - lineLeft, columnBottom - columnTop), 1);

        using SvgFilterImage intermediate = new(allocator, Rectangle.FromLTRB(area.Left, top, area.Right, bottom), output.ColorSpace);
        using IMemoryOwner<Rgba32> lines = allocator.Allocate<Rgba32>(lineLength * 2);
        Span<Rgba32> line = lines.Memory.Span[..lineLength];
        Span<Rgba32> scratch = lines.Memory.Span.Slice(lineLength, lineLength);

        // Horizontal pass: input rows into the intermediate image.
        for (int y = top; y < bottom; y++)
        {
            Span<Rgba32> current = line[..(lineRight - lineLeft)];
            input.CopyRow(y, lineLeft, current);
            Span<Rgba32> blurred = horizontal.Apply(current, scratch[..current.Length]);
            int offset = area.Left - lineLeft;
            blurred.Slice(offset, area.Width).CopyTo(intermediate.GetRow(y));
        }

        // Vertical pass: intermediate columns into the output image.
        int length = columnBottom - columnTop;
        for (int x = area.Left; x < area.Right; x++)
        {
            Span<Rgba32> current = line[..length];
            int column = x - area.Left;
            for (int y = columnTop; y < columnBottom; y++)
            {
                current[y - columnTop] = y >= top && y < bottom ? intermediate.GetRow(y)[column] : default;
            }

            Span<Rgba32> blurred = vertical.Apply(current, scratch[..length]);
            for (int y = area.Top; y < area.Bottom; y++)
            {
                output.GetRow(y)[column] = blurred[y - columnTop];
            }
        }
    }

    /// <summary>
    /// Returns the passes of a blur along one axis.
    /// </summary>
    /// <param name="deviation">The standard deviation in filter pixels; not negative.</param>
    /// <returns>The passes.</returns>
    private static Passes GetPasses(float deviation)
    {
        deviation = MathF.Min(deviation, MaxDeviation);
        if (!(deviation > 0F))
        {
            return default;
        }

        if (deviation >= 2F)
        {
            // d = floor(s * 3 * sqrt(2 * pi) / 4 + 0.5).
            int d = (int)MathF.Floor((deviation * 3F * MathF.Sqrt(2F * MathF.PI) / 4F) + 0.5F);
            return new Passes(d, null);
        }

        int radius = (int)MathF.Ceiling(deviation * 3F);
        float[] kernel = new float[(radius * 2) + 1];
        float sum = 0F;
        for (int i = -radius; i <= radius; i++)
        {
            float weight = MathF.Exp(-(i * i) / (2F * deviation * deviation));
            kernel[i + radius] = weight;
            sum += weight;
        }

        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= sum;
        }

        return new Passes(0, kernel);
    }

    /// <summary>
    /// One box blur pass over a line: <c>destination[i]</c> is the rounded mean of <c>source[i - left]</c> to
    /// <c>source[i + right]</c>, where pixels outside the line are transparent black.
    /// </summary>
    /// <param name="source">The line.</param>
    /// <param name="destination">The result, of the same length.</param>
    /// <param name="left">The number of pixels the window reaches to the left.</param>
    /// <param name="right">The number of pixels the window reaches to the right.</param>
    private static void Box(ReadOnlySpan<Rgba32> source, Span<Rgba32> destination, int left, int right)
    {
        int size = left + right + 1;
        int half = size / 2;
        int n = source.Length;
        int r = 0;
        int g = 0;
        int b = 0;
        int a = 0;

        // The window of the first pixel, [-left, right].
        for (int i = 0; i <= Math.Min(right, n - 1); i++)
        {
            Rgba32 p = source[i];
            r += p.R;
            g += p.G;
            b += p.B;
            a += p.A;
        }

        for (int i = 0; i < n; i++)
        {
            destination[i] = new Rgba32((byte)((r + half) / size), (byte)((g + half) / size), (byte)((b + half) / size), (byte)((a + half) / size));

            // Slide the window one pixel: add the pixel entering on the right, remove the one leaving on the left.
            int enter = i + right + 1;
            if (enter < n)
            {
                Rgba32 p = source[enter];
                r += p.R;
                g += p.G;
                b += p.B;
                a += p.A;
            }

            int leave = i - left;
            if (leave >= 0)
            {
                Rgba32 p = source[leave];
                r -= p.R;
                g -= p.G;
                b -= p.B;
                a -= p.A;
            }
        }
    }

    /// <summary>
    /// One convolution pass over a line with a symmetric kernel, where pixels outside the line are transparent black.
    /// </summary>
    /// <param name="source">The line.</param>
    /// <param name="destination">The result, of the same length.</param>
    /// <param name="kernel">The normalized kernel, of odd length.</param>
    private static void Convolve(ReadOnlySpan<Rgba32> source, Span<Rgba32> destination, float[] kernel)
    {
        int radius = kernel.Length / 2;
        int n = source.Length;
        for (int i = 0; i < n; i++)
        {
            Vector4 sum = Vector4.Zero;
            int start = Math.Max(0, i - radius);
            int end = Math.Min(n - 1, i + radius);
            for (int j = start; j <= end; j++)
            {
                Rgba32 p = source[j];
                sum += new Vector4(p.R, p.G, p.B, p.A) * kernel[j - i + radius];
            }

            sum += new Vector4(0.5F);
            destination[i] = new Rgba32((byte)MathF.Min(sum.X, 255F), (byte)MathF.Min(sum.Y, 255F), (byte)MathF.Min(sum.Z, 255F), (byte)MathF.Min(sum.W, 255F));
        }
    }

    /// <summary>
    /// The passes of a blur along one axis: three box blurs of size <see cref="BoxSize"/>, a convolution with
    /// <see cref="Kernel"/>, or nothing.
    /// </summary>
    private readonly struct Passes
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Passes"/> struct.
        /// </summary>
        /// <param name="boxSize">The box size <c>d</c>, or 0 for no box blur.</param>
        /// <param name="kernel">The Gaussian kernel, or <see langword="null"/>.</param>
        public Passes(int boxSize, float[]? kernel)
        {
            this.BoxSize = boxSize;
            this.Kernel = kernel;
        }

        /// <summary>
        /// Gets the box size <c>d</c> of the three box blurs, or 0.
        /// </summary>
        public int BoxSize { get; }

        /// <summary>
        /// Gets the Gaussian kernel, or <see langword="null"/>.
        /// </summary>
        public float[]? Kernel { get; }

        /// <summary>
        /// Gets the distance from an output pixel to the farthest input pixel that the passes read.
        /// </summary>
        public int Reach
        {
            get
            {
                if (this.Kernel is not null)
                {
                    return this.Kernel.Length / 2;
                }

                int d = this.BoxSize;
                return d == 0 ? 0 : (d % 2 == 1 ? 3 * ((d - 1) / 2) : (3 * d / 2));
            }
        }

        /// <summary>
        /// Applies the passes to a line.
        /// </summary>
        /// <param name="line">The line; it is overwritten.</param>
        /// <param name="scratch">A scratch line of the same length.</param>
        /// <returns>The span that holds the result: <paramref name="line"/> or <paramref name="scratch"/>.</returns>
        public Span<Rgba32> Apply(Span<Rgba32> line, Span<Rgba32> scratch)
        {
            if (this.Kernel is not null)
            {
                Convolve(line, scratch, this.Kernel);
                return scratch;
            }

            int d = this.BoxSize;
            if (d == 0)
            {
                return line;
            }

            if (d % 2 == 1)
            {
                int half = (d - 1) / 2;
                Box(line, scratch, half, half);
                Box(scratch, line, half, half);
                Box(line, scratch, half, half);
                return scratch;
            }

            // Two boxes of size d centered on the pixel boundary to the left and to the right, then one of size d + 1
            // centered on the pixel.
            Box(line, scratch, d / 2, (d / 2) - 1);
            Box(scratch, line, (d / 2) - 1, d / 2);
            Box(line, scratch, d / 2, d / 2);
            return scratch;
        }
    }
}
