// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feDropShadow</c> filter primitive: draws its input over a blurred, offset copy of its alpha filled with
/// <c>flood-color</c> at <c>flood-opacity</c>. The <c>drop-shadow()</c> filter function is the same primitive.
/// </summary>
/// <remarks>
/// <para>
/// Filter Effects Level 1 defines the primitive as the chain <c>feGaussianBlur</c> on the alpha of the input,
/// <c>feOffset</c> by <c>dx</c> and <c>dy</c>, <c>feFlood</c>, <c>feComposite</c> with <c>in</c> against the offset blur,
/// and <c>feMerge</c> of that shadow with the input on top. <see cref="Render"/> runs the same steps with the same code
/// as those primitives (<see cref="SvgFeGaussianBlur.Blur"/>, <see cref="SvgFeOffset.Offset"/>, an exact 8-bit
/// <c>in</c>, and <see cref="SvgFeMerge.SourceOver"/>), so it produces their pixels, without the images in between living
/// in the graph.
/// </para>
/// <para>
/// Attributes: <c>dx</c> and <c>dy</c> are numbers with the initial value 2, and a missing or invalid value, such as a
/// percentage, takes it, as Chromium does. <c>stdDeviation</c> holds one or two numbers with the initial value 2; a missing
/// or invalid list takes it. A zero or negative deviation on an axis does not blur along it, as for
/// <c>feGaussianBlur</c>, and with both the shadow is the offset alpha. The lengths follow <c>primitiveUnits</c>.
/// </para>
/// <para>
/// The input is converted to the color space of the primitive, and the flood color, an sRGB color, is converted in
/// floating point, as <c>feFlood</c> does.
/// </para>
/// </remarks>
internal sealed class SvgFeDropShadow : SvgFilterPrimitive
{
    /// <summary>
    /// The initial value of <c>dx</c>, <c>dy</c>, and <c>stdDeviation</c>.
    /// </summary>
    private const float InitialValue = 2F;

    private readonly Vector2 offset;
    private readonly Vector2 deviation;
    private readonly Vector4 color;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeDropShadow"/> class.
    /// </summary>
    /// <param name="input">The input index.</param>
    /// <param name="offset">The offset of the shadow in user units.</param>
    /// <param name="deviation">The standard deviation of the blur along x and y in user units; zero or less does not blur along that axis.</param>
    /// <param name="color">The straight sRGB color of the shadow, with the flood opacity multiplied into its alpha.</param>
    internal SvgFeDropShadow(int input, Vector2 offset, Vector2 deviation, Vector4 color)
        : base([input])
    {
        this.offset = offset;
        this.deviation = Vector2.Max(deviation, Vector2.Zero);
        this.color = color;
    }

    /// <inheritdoc/>
    /// <remarks>The blur inside splits its own passes over threads.</remarks>
    public override bool CanRenderInBands => false;

    /// <summary>
    /// Creates the primitive of an <c>feDropShadow</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        float dx = GetNumber(context, SvgAttributeId.Dx);
        float dy = GetNumber(context, SvgAttributeId.Dy);
        Vector2 deviation = new(InitialValue);
        if (context.TryGetValue(SvgAttributeId.StdDeviation, out SvgFilterAttributeValue value) && value.NumberCount is 1 or 2)
        {
            ReadOnlySpan<float> numbers = context.GetNumbers(in value);
            deviation = new Vector2(numbers[0], numbers[^1]);
        }

        SvgFilterStyle colors = context.Style.FilterColors;
        Vector4 color = context.ResolveColor(colors.FloodColor).ToPixel<RgbaVector>().ToScaledVector4();
        color.W *= colors.FloodOpacity;

        primitive = new SvgFeDropShadow(
            context.GetInput(context.Attributes.In),
            new Vector2(context.ToUserX(dx), context.ToUserY(dy)),
            new Vector2(context.ToUserX(deviation.X), context.ToUserY(deviation.Y)),
            color);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override Rectangle GetInputArea(int input, Rectangle area, in SvgFilterRenderContext context)
    {
        // The merge reads the input under the output; the shadow reads it offset and widened by the blur.
        Size reach = SvgFeGaussianBlur.GetReach(this.deviation * context.Scale);
        Rectangle shadow = SvgFeOffset.GetInputArea(area, SvgFeOffset.ToPixelOffset(this.offset, context.Scale));
        return Rectangle.Union(area, Rectangle.Inflate(shadow, reach.Width, reach.Height));
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        SvgFilterImage input = inputs[0];
        Rectangle area = output.Area;
        MemoryAllocator allocator = context.Allocator;
        SvgColorInterpolation space = output.ColorSpace;
        Vector2 offset = SvgFeOffset.ToPixelOffset(this.offset, context.Scale);
        Vector2 deviation = this.deviation * context.Scale;
        Size reach = SvgFeGaussianBlur.GetReach(deviation);

        // The blurred alpha is needed where the offset reads it for the output area, and the alpha where the blur reads
        // it for that; beyond the input area the alpha is transparent.
        Rectangle blurredArea = SvgFeOffset.GetInputArea(area, offset);
        Rectangle alphaArea = Rectangle.Intersect(Rectangle.Inflate(blurredArea, reach.Width, reach.Height), input.Area);
        using SvgFilterImage alpha = new(allocator, alphaArea, space) { IsAlphaOnly = true };
        for (int y = alpha.Area.Top; y < alpha.Area.Bottom; y++)
        {
            Span<Rgba32> row = alpha.GetRow(y);
            input.CopyRow(y, alpha.Area.Left, row);
            for (int x = 0; x < row.Length; x++)
            {
                row[x] = new Rgba32(0, 0, 0, row[x].A);
            }
        }

        // feGaussianBlur and feOffset. Without a blur the alpha is the blurred image.
        SvgFilterImage? blurred = null;
        try
        {
            if (deviation.X > 0F || deviation.Y > 0F)
            {
                // The blur of an alpha-only image is alpha-only, which lets the offset mix the alpha channel alone.
                blurred = new SvgFilterImage(allocator, blurredArea, space) { IsAlphaOnly = true };
                SvgFeGaussianBlur.Blur(alpha, blurred, deviation, context.Configuration);
            }

            SvgFeOffset.Offset(blurred ?? alpha, output, offset, allocator);
        }
        finally
        {
            blurred?.Dispose();
        }

        // feFlood and feComposite in: the premultiplied flood color times the alpha of the offset blur, rounded once.
        Rgba32 flood = SvgFilterColor.Premultiply(SvgFilterColor.FromSrgb(this.color, space));
        for (int y = area.Top; y < area.Bottom; y++)
        {
            Span<Rgba32> row = output.GetRow(y);
            for (int x = 0; x < row.Length; x++)
            {
                byte a = row[x].A;
                row[x] = new Rgba32(
                    SvgFilterColor.Multiply(flood.R, a),
                    SvgFilterColor.Multiply(flood.G, a),
                    SvgFilterColor.Multiply(flood.B, a),
                    SvgFilterColor.Multiply(flood.A, a));
            }
        }

        // feMerge: the input over the shadow.
        Rectangle overlap = Rectangle.Intersect(area, input.Area);
        if (overlap.Width <= 0 || overlap.Height <= 0)
        {
            return;
        }

        for (int y = overlap.Top; y < overlap.Bottom; y++)
        {
            ReadOnlySpan<Rgba32> source = input.GetRow(y).Slice(overlap.Left - input.Area.Left, overlap.Width);
            SvgFeMerge.SourceOver(source, output.GetRow(y).Slice(overlap.Left - area.Left, overlap.Width));
        }
    }

    /// <summary>
    /// Reads <c>dx</c> or <c>dy</c>.
    /// </summary>
    /// <param name="context">The build context.</param>
    /// <param name="attribute">The attribute.</param>
    /// <returns>The number, or the initial value 2 when the attribute is missing or is not exactly one number.</returns>
    private static float GetNumber(SvgFilterBuildContext context, SvgAttributeId attribute)
        => context.TryGetValue(attribute, out SvgFilterAttributeValue value) && value.NumberCount == 1 ? context.GetNumbers(in value)[0] : InitialValue;
}
