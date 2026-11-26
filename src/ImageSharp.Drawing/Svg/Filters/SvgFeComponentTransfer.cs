// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feComponentTransfer</c> filter primitive: maps each channel of each straight color through its own transfer
/// function.
/// </summary>
/// <remarks>
/// <para>
/// Rules of Filter Effects Level 1. The children <c>feFuncR</c>, <c>feFuncG</c>, <c>feFuncB</c>, and <c>feFuncA</c> hold
/// the functions; a channel without a child keeps its values, and when a channel has several children the last one
/// applies. <c>type</c> is <c>identity</c> (the initial value, also for a missing or invalid type), <c>table</c>,
/// <c>discrete</c>, <c>linear</c>, or <c>gamma</c>. <c>tableValues</c> is a list of numbers; an empty, missing, or invalid
/// list makes <c>table</c> and <c>discrete</c> the identity. <c>slope</c> (1), <c>intercept</c> (0), <c>amplitude</c> (1),
/// <c>exponent</c> (1), and <c>offset</c> (0) are single numbers; a missing or invalid number, such as <c>0.5px</c> or
/// <c>50%</c>, takes the initial value in parentheses. With the values v0 to vn and the input C in the range 0 to 1:
/// </para>
/// <list type="bullet">
/// <item><description><c>table</c>: for C in [k/n, (k+1)/n], C' = vk + (C - k/n) * n * (vk+1 - vk). A single value is a
/// constant, as in Chromium.</description></item>
/// <item><description><c>discrete</c>: with n + 1 values, for C in [k/(n+1), (k+1)/(n+1)), C' = vk; C = 1 gives vn.</description></item>
/// <item><description><c>linear</c>: C' = slope * C + intercept.</description></item>
/// <item><description><c>gamma</c>: C' = amplitude * pow(C, exponent) + offset.</description></item>
/// </list>
/// <para>
/// Every result is clamped to the range 0 to 1. A primitive whose four functions are the identity is a pass-through.
/// </para>
/// <para>
/// Each function is sampled into a table of 256 entries, one per 8-bit level. The primitive is a color filter: it reads
/// each input pixel unpremultiplied in floating point, converts it to the color space of the primitive, looks every
/// channel up with linear interpolation between the two nearest entries, and converts to the output color space. An
/// input that is an exact 8-bit level, which is every opaque pixel computed in sRGB, takes the exact function value; other
/// inputs interpolate, which is exact for <c>identity</c>, <c>linear</c>, and <c>table</c> between their breakpoints.
/// </para>
/// </remarks>
internal sealed class SvgFeComponentTransfer : SvgFilterPrimitive
{
    /// <summary>
    /// The number of entries of a transfer table: one per 8-bit level.
    /// </summary>
    private const int TableSize = 256;

    // The tables of R, G, B, and A, in that order; null for a channel whose function is the identity.
    private readonly float[]?[] tables;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeComponentTransfer"/> class.
    /// </summary>
    /// <param name="input">The input index.</param>
    /// <param name="tables">
    /// The four tables of R, G, B, and A, made by <see cref="CreateTable"/>, <see cref="CreateDiscrete"/>,
    /// <see cref="CreateLinear"/>, or <see cref="CreateGamma"/>; <see langword="null"/> for the identity. At least one is
    /// not <see langword="null"/>.
    /// </param>
    internal SvgFeComponentTransfer(int input, float[]?[] tables)
        : base([input])
        => this.tables = tables;

    /// <inheritdoc/>
    public override bool IsColorFilter => true;

    /// <summary>
    /// Creates the primitive of an <c>feComponentTransfer</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>, or <see cref="SvgFilterBuildResult.PassThrough"/> when every function is the identity.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        primitive = null;
        SvgElementTree tree = context.Tree;
        float[]?[] tables = new float[4][];
        bool any = false;
        for (int child = tree.GetFirstChild(context.Element); child >= 0; child = tree.GetNextSibling(child))
        {
            int channel = tree[child].Kind switch
            {
                SvgElementKind.FeFuncR => 0,
                SvgElementKind.FeFuncG => 1,
                SvgElementKind.FeFuncB => 2,
                SvgElementKind.FeFuncA => 3,
                _ => -1,
            };

            if (channel < 0)
            {
                continue;
            }

            // A later child of the same channel replaces an earlier one, even when the later one is the identity.
            tables[channel] = CreateFunction(context, child);
        }

        foreach (float[]? table in tables)
        {
            any |= table is not null;
        }

        if (!any)
        {
            return SvgFilterBuildResult.PassThrough;
        }

        primitive = new SvgFeComponentTransfer(context.GetInput(context.Attributes.In), tables);
        return SvgFilterBuildResult.Created;
    }

    /// <summary>
    /// Samples a <c>table</c> transfer function.
    /// </summary>
    /// <param name="values">The table values; at least one.</param>
    /// <returns>The table.</returns>
    internal static float[] CreateTable(ReadOnlySpan<float> values)
    {
        float[] table = new float[TableSize];
        int n = values.Length - 1;
        for (int i = 0; i < TableSize; i++)
        {
            float c = i / (float)(TableSize - 1);
            float value;
            if (n == 0)
            {
                value = values[0];
            }
            else
            {
                // C in [k/n, (k+1)/n]; C = 1 falls in the last interval.
                int k = Math.Min((int)(c * n), n - 1);
                value = values[k] + ((c - (k / (float)n)) * n * (values[k + 1] - values[k]));
            }

            table[i] = Math.Clamp(value, 0F, 1F);
        }

        return table;
    }

    /// <summary>
    /// Samples a <c>discrete</c> transfer function.
    /// </summary>
    /// <param name="values">The table values; at least one.</param>
    /// <returns>The table.</returns>
    internal static float[] CreateDiscrete(ReadOnlySpan<float> values)
    {
        float[] table = new float[TableSize];
        int n = values.Length;
        for (int i = 0; i < TableSize; i++)
        {
            // C in [k/n, (k+1)/n); C = 1 takes the last value. The level i covers C = i / 255 exactly, and k is computed
            // in integers so that a level on a boundary, such as 85 of 255 for n = 3, falls in the upper interval.
            int k = Math.Min(i * n / (TableSize - 1), n - 1);
            table[i] = Math.Clamp(values[k], 0F, 1F);
        }

        return table;
    }

    /// <summary>
    /// Samples a <c>linear</c> transfer function.
    /// </summary>
    /// <param name="slope">The slope.</param>
    /// <param name="intercept">The intercept.</param>
    /// <returns>The table.</returns>
    internal static float[] CreateLinear(float slope, float intercept)
    {
        float[] table = new float[TableSize];
        for (int i = 0; i < TableSize; i++)
        {
            table[i] = Math.Clamp((slope * (i / (float)(TableSize - 1))) + intercept, 0F, 1F);
        }

        return table;
    }

    /// <summary>
    /// Samples a <c>gamma</c> transfer function.
    /// </summary>
    /// <param name="amplitude">The amplitude.</param>
    /// <param name="exponent">The exponent.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The table.</returns>
    internal static float[] CreateGamma(float amplitude, float exponent, float offset)
    {
        float[] table = new float[TableSize];
        for (int i = 0; i < TableSize; i++)
        {
            float value = (amplitude * MathF.Pow(i / (float)(TableSize - 1), exponent)) + offset;

            // A negative exponent makes pow(0, e) infinite, and NaN would survive the clamp; both clamp to 1, as the
            // limit of the function.
            table[i] = float.IsNaN(value) ? 1F : Math.Clamp(value, 0F, 1F);
        }

        return table;
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        SvgFilterImage input = inputs[0];
        Rectangle area = output.Area;
        float[]? r = this.tables[0];
        float[]? g = this.tables[1];
        float[]? b = this.tables[2];
        float[]? a = this.tables[3];
        SvgColorInterpolation inputSpace = input.ColorSpace;
        SvgColorInterpolation space = context.ColorSpace;
        SvgColorInterpolation outputSpace = context.OutputColorSpace;

        for (int y = area.Top; y < area.Bottom; y++)
        {
            Span<Rgba32> row = output.GetRow(y);
            input.CopyRow(y, area.Left, row);
            for (int x = 0; x < row.Length; x++)
            {
                // A transparent pixel is transparent black, which the functions map like any other color: a table on
                // alpha can make it visible.
                Vector4 color = SvgFilterColor.Convert(SvgFilterColor.Unpremultiply(row[x]), inputSpace, space);
                color = new Vector4(Lookup(r, color.X), Lookup(g, color.Y), Lookup(b, color.Z), Lookup(a, color.W));
                row[x] = SvgFilterColor.Premultiply(SvgFilterColor.Convert(color, space, outputSpace));
            }
        }
    }

    /// <summary>
    /// Creates the table of one transfer function element.
    /// </summary>
    /// <param name="context">The build context.</param>
    /// <param name="element">The <c>feFuncR</c>, <c>feFuncG</c>, <c>feFuncB</c>, or <c>feFuncA</c> element.</param>
    /// <returns>The table, or <see langword="null"/> for the identity.</returns>
    private static float[]? CreateFunction(SvgFilterBuildContext context, int element)
    {
        string type = context.TryGetValue(element, SvgAttributeId.Type, out SvgFilterAttributeValue value) ? value.Text : string.Empty;
        switch (type)
        {
            case "table":
            case "discrete":
            {
                ReadOnlySpan<float> values = context.TryGetValue(element, SvgAttributeId.TableValues, out SvgFilterAttributeValue list)
                    ? context.GetNumbers(in list)
                    : default;
                if (values.IsEmpty)
                {
                    return null;
                }

                return type == "table" ? CreateTable(values) : CreateDiscrete(values);
            }

            case "linear":
                return CreateLinear(
                    GetNumber(context, element, SvgAttributeId.Slope, 1F),
                    GetNumber(context, element, SvgAttributeId.Intercept, 0F));

            case "gamma":
                return CreateGamma(
                    GetNumber(context, element, SvgAttributeId.Amplitude, 1F),
                    GetNumber(context, element, SvgAttributeId.Exponent, 1F),
                    GetNumber(context, element, SvgAttributeId.Offset, 0F));

            default:
                // identity, and a missing or invalid type, which takes the initial value.
                return null;
        }
    }

    /// <summary>
    /// Reads a number attribute of a transfer function element.
    /// </summary>
    /// <param name="context">The build context.</param>
    /// <param name="element">The element.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="initial">The initial value, for a missing value or one that is not exactly one number.</param>
    /// <returns>The number.</returns>
    private static float GetNumber(SvgFilterBuildContext context, int element, SvgAttributeId attribute, float initial)
        => context.TryGetValue(element, attribute, out SvgFilterAttributeValue value) && value.NumberCount == 1
            ? context.GetNumbers(in value)[0]
            : initial;

    /// <summary>
    /// Looks a channel up in a table with linear interpolation between the two nearest entries.
    /// </summary>
    /// <param name="table">The table, or <see langword="null"/> for the identity.</param>
    /// <param name="value">The channel in the range 0 to 1.</param>
    /// <returns>The mapped channel.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lookup(float[]? table, float value)
    {
        if (table is null)
        {
            return value;
        }

        // position = value * 255 lies between entries i and i + 1; the clamp keeps i + 1 inside the table at value 1.
        float position = Math.Clamp(value, 0F, 1F) * (TableSize - 1);
        int i = Math.Min((int)position, TableSize - 2);
        float t = position - i;
        return table[i] + ((table[i + 1] - table[i]) * t);
    }
}
