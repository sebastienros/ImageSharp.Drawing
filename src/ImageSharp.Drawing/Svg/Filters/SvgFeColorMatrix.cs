// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feColorMatrix</c> filter primitive: multiplies each straight color <c>[R G B A 1]</c> by a 4 x 5 matrix.
/// </summary>
/// <remarks>
/// <para>
/// Rules of Filter Effects Level 1: <c>type="matrix"</c> (the default, also for an invalid type) takes 20 numbers row by
/// row; without <c>values</c> it is the identity, and with another count of numbers the primitive is a pass-through.
/// <c>saturate</c> takes one number (default 1), <c>hueRotate</c> one angle in degrees (default 0), and
/// <c>luminanceToAlpha</c> ignores <c>values</c> and uses the coefficients 0.2126, 0.7152, and 0.0722 of Filter Effects.
/// The matrices of <c>saturate</c> and <c>hueRotate</c> use the coefficients 0.213, 0.715, and 0.072 that Filter Effects
/// gives for them. A matrix that is the identity makes the primitive a pass-through.
/// </para>
/// <para>
/// The computation runs on unpremultiplied values in floating point and clamps each channel to the range 0 to 1, as the
/// specification requires; the offsets of the fifth column are fractions of 1.
/// </para>
/// </remarks>
internal sealed class SvgFeColorMatrix : SvgFilterPrimitive
{
    // Row r of the matrix is (rows[r], offsets[r]): the output channel r is Dot(rows[r], color) + offsets[r].
    private readonly Vector4[] rows;
    private readonly Vector4 offsets;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeColorMatrix"/> class.
    /// </summary>
    /// <param name="input">The input index.</param>
    /// <param name="matrix">The 20 numbers of the matrix, row by row.</param>
    private SvgFeColorMatrix(int input, ReadOnlySpan<float> matrix)
        : base([input])
    {
        this.rows =
        [
            new(matrix[0], matrix[1], matrix[2], matrix[3]),
            new(matrix[5], matrix[6], matrix[7], matrix[8]),
            new(matrix[10], matrix[11], matrix[12], matrix[13]),
            new(matrix[15], matrix[16], matrix[17], matrix[18]),
        ];
        this.offsets = new Vector4(matrix[4], matrix[9], matrix[14], matrix[19]);
    }

    /// <inheritdoc/>
    public override bool IsColorFilter => true;

    /// <summary>
    /// Creates the primitive of an <c>feColorMatrix</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>, or <see cref="SvgFilterBuildResult.PassThrough"/> for an identity or an invalid matrix.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        primitive = null;
        string type = context.TryGetValue(SvgAttributeId.Type, out SvgFilterAttributeValue typeValue) ? typeValue.Text : "matrix";
        bool hasValues = context.TryGetValue(SvgAttributeId.Values, out SvgFilterAttributeValue values);
        ReadOnlySpan<float> numbers = hasValues ? context.GetNumbers(in values) : default;
        Span<float> matrix = stackalloc float[20];

        switch (type)
        {
            case "saturate":
            {
                // One number; a missing or empty list is the default 1, the identity.
                if (hasValues && values.NumberCount > 1)
                {
                    return SvgFilterBuildResult.PassThrough;
                }

                float s = numbers.Length == 1 ? numbers[0] : 1F;
                ReadOnlySpan<float> saturate =
                [
                    0.213F + (0.787F * s), 0.715F - (0.715F * s), 0.072F - (0.072F * s), 0F, 0F,
                    0.213F - (0.213F * s), 0.715F + (0.285F * s), 0.072F - (0.072F * s), 0F, 0F,
                    0.213F - (0.213F * s), 0.715F - (0.715F * s), 0.072F + (0.928F * s), 0F, 0F,
                    0F, 0F, 0F, 1F, 0F,
                ];
                saturate.CopyTo(matrix);
                break;
            }

            case "hueRotate":
            {
                if (hasValues && values.NumberCount > 1)
                {
                    return SvgFilterBuildResult.PassThrough;
                }

                float angle = numbers.Length == 1 ? numbers[0] * (MathF.PI / 180F) : 0F;
                float cos = MathF.Cos(angle);
                float sin = MathF.Sin(angle);
                ReadOnlySpan<float> hueRotate =
                [
                    0.213F + (cos * 0.787F) - (sin * 0.213F), 0.715F - (cos * 0.715F) - (sin * 0.715F), 0.072F - (cos * 0.072F) + (sin * 0.928F), 0F, 0F,
                    0.213F - (cos * 0.213F) + (sin * 0.143F), 0.715F + (cos * 0.285F) + (sin * 0.140F), 0.072F - (cos * 0.072F) - (sin * 0.283F), 0F, 0F,
                    0.213F - (cos * 0.213F) - (sin * 0.787F), 0.715F - (cos * 0.715F) + (sin * 0.715F), 0.072F + (cos * 0.928F) + (sin * 0.072F), 0F, 0F,
                    0F, 0F, 0F, 1F, 0F,
                ];
                hueRotate.CopyTo(matrix);
                break;
            }

            case "luminanceToAlpha":
            {
                ReadOnlySpan<float> luminance =
                [
                    0F, 0F, 0F, 0F, 0F,
                    0F, 0F, 0F, 0F, 0F,
                    0F, 0F, 0F, 0F, 0F,
                    0.2126F, 0.7152F, 0.0722F, 0F, 0F,
                ];
                luminance.CopyTo(matrix);
                break;
            }

            default:
                // matrix, and an invalid type, which takes the initial value. Without values the matrix is the identity.
                if (!hasValues)
                {
                    return SvgFilterBuildResult.PassThrough;
                }

                if (values.NumberCount != 20)
                {
                    return SvgFilterBuildResult.PassThrough;
                }

                numbers.CopyTo(matrix);
                break;
        }

        if (IsIdentity(matrix))
        {
            return SvgFilterBuildResult.PassThrough;
        }

        primitive = new SvgFeColorMatrix(context.GetInput(context.Attributes.In), matrix);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        SvgFilterImage input = inputs[0];
        Rectangle area = output.Area;
        Vector4 r = this.rows[0];
        Vector4 g = this.rows[1];
        Vector4 b = this.rows[2];
        Vector4 a = this.rows[3];
        Vector4 offsets = this.offsets;
        SvgColorInterpolation inputSpace = input.ColorSpace;
        SvgColorInterpolation space = context.ColorSpace;
        SvgColorInterpolation outputSpace = context.OutputColorSpace;

        for (int y = area.Top; y < area.Bottom; y++)
        {
            Span<Rgba32> row = output.GetRow(y);
            input.CopyRow(y, area.Left, row);
            for (int x = 0; x < row.Length; x++)
            {
                // Convert to the space of the primitive, apply the matrix, clamp, and convert to the output space, all in
                // floating point.
                Vector4 color = SvgFilterColor.Convert(SvgFilterColor.Unpremultiply(row[x]), inputSpace, space);
                Vector4 result = new Vector4(Vector4.Dot(r, color), Vector4.Dot(g, color), Vector4.Dot(b, color), Vector4.Dot(a, color)) + offsets;
                result = Vector4.Clamp(result, Vector4.Zero, Vector4.One);
                row[x] = SvgFilterColor.Premultiply(SvgFilterColor.Convert(result, space, outputSpace));
            }
        }
    }

    /// <summary>
    /// Returns whether 20 numbers are the identity matrix.
    /// </summary>
    /// <param name="matrix">The matrix, row by row.</param>
    /// <returns><see langword="true"/> if the matrix is the identity; otherwise, <see langword="false"/>.</returns>
    private static bool IsIdentity(ReadOnlySpan<float> matrix)
    {
        for (int i = 0; i < 20; i++)
        {
            float expected = i % 6 == 0 && i < 19 ? 1F : 0F;
            if (matrix[i] != expected)
            {
                return false;
            }
        }

        return true;
    }
}
