// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feOffset</c> filter primitive: moves its input by <c>dx</c> and <c>dy</c>.
/// </summary>
/// <remarks>
/// The offset is in user units (scaled from <c>primitiveUnits</c>) and scales to filter pixels at render time. A whole
/// pixel offset moves the pixels exactly; a fractional one interpolates bilinearly, as Chromium does inside an image.
/// Moving pixels does not depend on the color space, as Chromium also treats it, so the primitive works in the space of
/// its input. A missing or invalid value (a percentage, more than one number) is 0.
/// </remarks>
internal sealed class SvgFeOffset : SvgFilterPrimitive
{
    private readonly float dx;
    private readonly float dy;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeOffset"/> class.
    /// </summary>
    /// <param name="input">The input index.</param>
    /// <param name="dx">The horizontal offset in user units.</param>
    /// <param name="dy">The vertical offset in user units.</param>
    private SvgFeOffset(int input, float dx, float dy)
        : base([input])
    {
        this.dx = dx;
        this.dy = dy;
    }

    /// <inheritdoc/>
    public override bool PreservesColorSpace => true;

    /// <inheritdoc/>
    /// <remarks>The output copies or mixes input pixels channel by channel.</remarks>
    public override bool PreservesAlphaOnly => true;

    /// <summary>
    /// Creates the primitive of an <c>feOffset</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        float dx = context.ToUserX(GetNumber(context, SvgAttributeId.Dx));
        float dy = context.ToUserY(GetNumber(context, SvgAttributeId.Dy));
        primitive = new SvgFeOffset(context.GetInput(context.Attributes.In), dx, dy);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override Rectangle GetInputArea(int input, Rectangle area, in SvgFilterRenderContext context)
        => GetInputArea(area, this.GetPixelOffset(in context));

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
        => Offset(inputs[0], output, this.GetPixelOffset(in context), context.Allocator);

    /// <summary>
    /// Returns the area of the input that an offset output area reads.
    /// </summary>
    /// <param name="area">The output area in filter space.</param>
    /// <param name="offset">The offset in filter pixels, as <see cref="ToPixelOffset"/> returns it.</param>
    /// <returns>The input area.</returns>
    internal static Rectangle GetInputArea(Rectangle area, Vector2 offset)
    {
        // Output pixel x reads input pixels floor(x - dx) and the one after it; the second only for a fractional offset.
        int x = (int)MathF.Floor(-offset.X);
        int y = (int)MathF.Floor(-offset.Y);
        int extraX = -offset.X - x > 0F ? 1 : 0;
        int extraY = -offset.Y - y > 0F ? 1 : 0;
        return new Rectangle(area.X + x, area.Y + y, area.Width + extraX, area.Height + extraY);
    }

    /// <summary>
    /// Moves an image into the area of another. The output must be transparent; the input can have any area.
    /// </summary>
    /// <param name="input">The input image.</param>
    /// <param name="output">The output image, transparent, not empty.</param>
    /// <param name="offset">The offset in filter pixels, as <see cref="ToPixelOffset"/> returns it.</param>
    /// <param name="allocator">The allocator for scratch memory.</param>
    internal static void Offset(SvgFilterImage input, SvgFilterImage output, Vector2 offset, MemoryAllocator allocator)
    {
        Rectangle area = output.Area;

        // The output pixel x takes the input at x - dx, interpolated linearly between the two nearest pixels on each
        // axis: with u = x - dx, i = floor(u), and t = u - i, the value is (1 - t) in[i] + t in[i + 1]. The weights are
        // fixed point with 8 fraction bits per axis, so a whole pixel offset copies the pixels exactly.
        int x0 = (int)MathF.Floor(-offset.X);
        int y0 = (int)MathF.Floor(-offset.Y);
        int tx = (int)MathF.Round((-offset.X - x0) * 256F);
        int ty = (int)MathF.Round((-offset.Y - y0) * 256F);
        if (tx == 256)
        {
            x0++;
            tx = 0;
        }

        if (ty == 256)
        {
            y0++;
            ty = 0;
        }

        if (tx == 0 && ty == 0)
        {
            for (int row = area.Top; row < area.Bottom; row++)
            {
                input.CopyRow(row + y0, area.Left + x0, output.GetRow(row));
            }

            return;
        }

        // The weights of Mix, for the alpha-only rows.
        bool alphaOnly = input.IsAlphaOnly;
        int wa = (256 - tx) * (256 - ty);
        int wb = tx * (256 - ty);
        int wc = (256 - tx) * ty;
        int wd = tx * ty;
        int width = area.Width + 1;
        using IMemoryOwner<Rgba32> scratch = allocator.Allocate<Rgba32>(width * 2);
        Span<Rgba32> upper = scratch.Memory.Span[..width];
        Span<Rgba32> lower = scratch.Memory.Span.Slice(width, width);
        for (int row = area.Top; row < area.Bottom; row++)
        {
            input.CopyRow(row + y0, area.Left + x0, upper);
            input.CopyRow(row + y0 + 1, area.Left + x0, lower);
            Span<Rgba32> destination = output.GetRow(row);
            if (alphaOnly)
            {
                // The color channels of the input are all 0, so Mix gives 0 for them: (0 + 32768) >> 16 is 0. Only the
                // alpha channel is mixed, with the expression of Mix.
                for (int x = 0; x < destination.Length; x++)
                {
                    int alpha = (upper[x].A * wa) + (upper[x + 1].A * wb) + (lower[x].A * wc) + (lower[x + 1].A * wd) + 32768;
                    destination[x] = new Rgba32(0, 0, 0, (byte)(alpha >> 16));
                }

                continue;
            }

            for (int x = 0; x < destination.Length; x++)
            {
                destination[x] = Mix(upper[x], upper[x + 1], lower[x], lower[x + 1], tx, ty);
            }
        }
    }

    /// <summary>
    /// Interpolates four premultiplied pixels bilinearly.
    /// </summary>
    /// <param name="a">The pixel at (i, j).</param>
    /// <param name="b">The pixel at (i + 1, j).</param>
    /// <param name="c">The pixel at (i, j + 1).</param>
    /// <param name="d">The pixel at (i + 1, j + 1).</param>
    /// <param name="tx">The horizontal weight of the right pixels, in 1/256.</param>
    /// <param name="ty">The vertical weight of the lower pixels, in 1/256.</param>
    /// <returns>The interpolated pixel, rounded.</returns>
    private static Rgba32 Mix(Rgba32 a, Rgba32 b, Rgba32 c, Rgba32 d, int tx, int ty)
    {
        // The four weights sum to 65536, so the sum of a channel fits in an int and the shift divides with rounding.
        int wa = (256 - tx) * (256 - ty);
        int wb = tx * (256 - ty);
        int wc = (256 - tx) * ty;
        int wd = tx * ty;
        return new Rgba32(
            (byte)(((a.R * wa) + (b.R * wb) + (c.R * wc) + (d.R * wd) + 32768) >> 16),
            (byte)(((a.G * wa) + (b.G * wb) + (c.G * wc) + (d.G * wd) + 32768) >> 16),
            (byte)(((a.B * wa) + (b.B * wb) + (c.B * wc) + (d.B * wd) + 32768) >> 16),
            (byte)(((a.A * wa) + (b.A * wb) + (c.A * wc) + (d.A * wd) + 32768) >> 16));
    }

    /// <summary>
    /// Reads a number attribute.
    /// </summary>
    /// <param name="context">The build context.</param>
    /// <param name="attribute">The attribute.</param>
    /// <returns>The number, or 0 when the attribute is missing or is not exactly one number.</returns>
    private static float GetNumber(SvgFilterBuildContext context, SvgAttributeId attribute)
        => context.TryGetValue(attribute, out SvgFilterAttributeValue value) && value.NumberCount == 1 ? context.GetNumbers(in value)[0] : 0F;

    /// <summary>
    /// Converts an offset in user units to filter pixels.
    /// </summary>
    /// <param name="offset">The offset in user units.</param>
    /// <param name="scale">The filter pixels per user unit.</param>
    /// <returns>The horizontal and vertical offset in filter pixels, clamped to 2^28 in magnitude.</returns>
    internal static Vector2 ToPixelOffset(Vector2 offset, Vector2 scale)
    {
        // An offset larger than any image moves everything out; clamping keeps the int conversions defined.
        const float limit = 1 << 28;
        return Vector2.Clamp(offset * scale, new Vector2(-limit), new Vector2(limit));
    }

    /// <summary>
    /// Returns the offset in filter pixels.
    /// </summary>
    /// <param name="context">The context of the run.</param>
    /// <returns>The horizontal and vertical offset.</returns>
    private Vector2 GetPixelOffset(in SvgFilterRenderContext context) => ToPixelOffset(new Vector2(this.dx, this.dy), context.Scale);
}
