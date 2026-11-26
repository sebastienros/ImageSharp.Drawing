// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feComposite</c> filter primitive: combines <c>in</c> (A) and <c>in2</c> (B) with a Porter-Duff operator or
/// the <c>arithmetic</c> operator.
/// </summary>
/// <remarks>
/// <para>
/// On premultiplied channels, with <c>a</c> and <c>b</c> the alphas: <c>over</c> is <c>A + B(1 - a)</c>, <c>in</c> is
/// <c>A b</c>, <c>out</c> is <c>A (1 - b)</c>, <c>atop</c> is <c>A b + B (1 - a)</c>, <c>xor</c> is
/// <c>A (1 - b) + B (1 - a)</c>, and <c>lighter</c> is <c>A + B</c> clamped to 1. <c>arithmetic</c> is
/// <c>k1 A B + k2 A + k3 B + k4</c> per channel, alpha included, clamped to the range 0 to 1 and then each color channel
/// to at most the alpha, so the result is a valid premultiplied color. <c>over</c> is the default, also for an invalid
/// operator; <c>k1</c> to <c>k4</c> default to 0.
/// </para>
/// <para>
/// The Porter-Duff operators run in integers with exact rounded division by 255; <c>arithmetic</c> runs in floating point.
/// </para>
/// </remarks>
internal sealed class SvgFeComposite : SvgFilterPrimitive
{
    private readonly Operator op;
    private readonly Vector4 k;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeComposite"/> class.
    /// </summary>
    /// <param name="input">The index of <c>in</c>.</param>
    /// <param name="input2">The index of <c>in2</c>.</param>
    /// <param name="op">The operator.</param>
    /// <param name="k">The coefficients <c>k1</c> to <c>k4</c> of <c>arithmetic</c>.</param>
    private SvgFeComposite(int input, int input2, Operator op, Vector4 k)
        : base([input, input2])
    {
        this.op = op;
        this.k = k;
    }

    /// <summary>
    /// The compositing operators.
    /// </summary>
    private enum Operator
    {
        /// <summary><c>over</c>.</summary>
        Over,

        /// <summary><c>in</c>.</summary>
        In,

        /// <summary><c>out</c>.</summary>
        Out,

        /// <summary><c>atop</c>.</summary>
        Atop,

        /// <summary><c>xor</c>.</summary>
        Xor,

        /// <summary><c>lighter</c>.</summary>
        Lighter,

        /// <summary><c>arithmetic</c>.</summary>
        Arithmetic
    }

    /// <summary>
    /// Creates the primitive of an <c>feComposite</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        string? name = context.TryGetValue(SvgAttributeId.Operator, out SvgFilterAttributeValue value) ? value.Text : null;
        Operator op = name switch
        {
            "in" => Operator.In,
            "out" => Operator.Out,
            "atop" => Operator.Atop,
            "xor" => Operator.Xor,
            "lighter" => Operator.Lighter,
            "arithmetic" => Operator.Arithmetic,
            _ => Operator.Over
        };

        Vector4 k = new(
            GetNumber(context, SvgAttributeId.K1),
            GetNumber(context, SvgAttributeId.K2),
            GetNumber(context, SvgAttributeId.K3),
            GetNumber(context, SvgAttributeId.K4));

        ref readonly SvgFilterPrimitiveAttributes attributes = ref context.Attributes;
        primitive = new SvgFeComposite(context.GetInput(attributes.In), context.GetInput(attributes.In2), op, k);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        Rectangle area = output.Area;
        using IMemoryOwner<Rgba32> scratch = context.Allocator.Allocate<Rgba32>(area.Width);
        Span<Rgba32> backdrop = scratch.Memory.Span[..area.Width];
        for (int y = area.Top; y < area.Bottom; y++)
        {
            // The output row first holds A, then the result.
            Span<Rgba32> row = output.GetRow(y);
            inputs[0].CopyRow(y, area.Left, row);
            inputs[1].CopyRow(y, area.Left, backdrop);
            if (this.op == Operator.Arithmetic)
            {
                this.Arithmetic(row, backdrop);
            }
            else
            {
                this.PorterDuff(row, backdrop);
            }
        }
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
    /// Applies a Porter-Duff operator to one row.
    /// </summary>
    /// <param name="row">The pixels of A, which receive the result.</param>
    /// <param name="backdrop">The pixels of B.</param>
    private void PorterDuff(Span<Rgba32> row, ReadOnlySpan<Rgba32> backdrop)
    {
        for (int x = 0; x < row.Length; x++)
        {
            Rgba32 a = row[x];
            Rgba32 b = backdrop[x];

            // The weights of A and B in 8-bit units: result = A * wa / 255 + B * wb / 255.
            int wa;
            int wb;
            switch (this.op)
            {
                case Operator.In:
                    wa = b.A;
                    wb = 0;
                    break;
                case Operator.Out:
                    wa = 255 - b.A;
                    wb = 0;
                    break;
                case Operator.Atop:
                    wa = b.A;
                    wb = 255 - a.A;
                    break;
                case Operator.Xor:
                    wa = 255 - b.A;
                    wb = 255 - a.A;
                    break;
                case Operator.Lighter:
                    row[x] = new Rgba32(
                        (byte)Math.Min(255, a.R + b.R),
                        (byte)Math.Min(255, a.G + b.G),
                        (byte)Math.Min(255, a.B + b.B),
                        (byte)Math.Min(255, a.A + b.A));
                    continue;
                default:
                    wa = 255;
                    wb = 255 - a.A;
                    break;
            }

            row[x] = new Rgba32(
                (byte)Math.Min(255, SvgFilterColor.Multiply(a.R, wa) + SvgFilterColor.Multiply(b.R, wb)),
                (byte)Math.Min(255, SvgFilterColor.Multiply(a.G, wa) + SvgFilterColor.Multiply(b.G, wb)),
                (byte)Math.Min(255, SvgFilterColor.Multiply(a.B, wa) + SvgFilterColor.Multiply(b.B, wb)),
                (byte)Math.Min(255, SvgFilterColor.Multiply(a.A, wa) + SvgFilterColor.Multiply(b.A, wb)));
        }
    }

    /// <summary>
    /// Applies the <c>arithmetic</c> operator to one row.
    /// </summary>
    /// <param name="row">The pixels of A, which receive the result.</param>
    /// <param name="backdrop">The pixels of B.</param>
    private void Arithmetic(Span<Rgba32> row, ReadOnlySpan<Rgba32> backdrop)
    {
        const float scale = 1F / 255F;
        Vector4 k = this.k;
        for (int x = 0; x < row.Length; x++)
        {
            Rgba32 pa = row[x];
            Rgba32 pb = backdrop[x];
            Vector4 a = new Vector4(pa.R, pa.G, pa.B, pa.A) * scale;
            Vector4 b = new Vector4(pb.R, pb.G, pb.B, pb.A) * scale;
            Vector4 result = (k.X * a * b) + (k.Y * a) + (k.Z * b) + new Vector4(k.W);
            row[x] = SvgFilterColor.FromPremultiplied(result);
        }
    }
}
