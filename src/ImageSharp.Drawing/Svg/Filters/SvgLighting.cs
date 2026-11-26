// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The computation that <c>feDiffuseLighting</c> and <c>feSpecularLighting</c> share: the surface normal from the alpha
/// channel of the input, the light vector of the light source, and the lighting equations of Filter Effects Level 1.
/// </summary>
/// <remarks>
/// <para>
/// Surface. The input alpha <c>A</c>, in the range 0 to 1, is a height map: <c>Z(x, y) = surfaceScale * A(x, y)</c>. The
/// normal is <c>N = (Nx, Ny, 1) / Norm(Nx, Ny, 1)</c> with <c>Nx = -surfaceScale * FACTORx * (Kx * A)</c>, where the
/// 3 x 3 Sobel kernels and their factors are the nine tables of the specification: one for interior pixels, one for each
/// edge, and one for each corner. Every table is the same separable form, which this code evaluates: a difference across
/// the columns <c>x - 1</c> and <c>x + 1</c>, or across the pixel and its one neighbour at an edge, weighted 1, 2, 1 over
/// the rows that exist (2 for the center row), divided by the sum of the row weights and the column span, and doubled.
/// For example the interior factor 1/4 is 2 / (4 * 2) and the top left corner factor 2/3 is 2 / (3 * 1). A subregion
/// one pixel wide has no horizontal difference, so <c>Nx</c> is 0 there.
/// </para>
/// <para>
/// Edges. The image that the kernels see is the primitive subregion, clipped to the filter region: its outer pixels use
/// the edge and corner kernels and input pixels outside it are not read, as in Chromium, which also treats the subregion
/// as the image edge. Chromium differs in one point that the specification settles: it extends the image by repeating
/// its edge pixels and applies the interior kernel there, which gives half the gradient of the edge kernels on an alpha
/// ramp. Pixels of a flat surface come out the same either way.
/// </para>
/// <para>
/// Scale. The computation runs in filter space: x and y in filter pixels, and z, the surface height and the z of the
/// light, in user units times the mean of the two scales, <c>(Scale.X + Scale.Y) / 2</c>. The kernels step one filter
/// pixel. Under a uniform scale this is user space scaled as a whole, so the result does not change with the output size;
/// under a scale that differs per axis, the mean is what the Chromium oracle shows (the geometric and the quadratic mean
/// and either axis alone do not match it). Light positions map as points, and the direction of a distant light and the
/// axis of a spot light map through the same scales and normalize again, which also matches the oracle.
/// <c>kernelUnitLength</c> is ignored, as Chromium ignores it.
/// </para>
/// <para>
/// Equations. Diffuse: <c>D = kd * N.L * Light</c> with alpha 1. Specular: <c>S = ks * pow(N.H, specularExponent) *
/// Light</c> with <c>H = (L + E) / Norm(L + E)</c>, <c>E = (0, 0, 1)</c>, and alpha <c>max(Sr, Sg, Sb)</c>; the result is
/// premultiplied, which the alpha rule makes valid, and Chromium treats it so. A negative <c>N.L</c> or <c>N.H</c> gives
/// no light. Every channel is clamped to the range 0 to 1, and a value that is not a number (for example from a huge
/// <c>surfaceScale</c>) gives 0.
/// </para>
/// <para>
/// Color. The lighting color converts to the color space of the primitive and is then held with 8 bits per channel, and
/// the result is stored with 8 bits in that space, which the engine converts afterward. Both steps match the Chromium
/// oracle, whose dark results in linearRGB show the quantized color. The alpha of the lighting color is ignored, as in
/// Chromium.
/// </para>
/// </remarks>
internal static class SvgLighting
{
    /// <summary>
    /// Reads a number attribute of the lighting primitive. A missing value, a value that is not a single number, and a
    /// value that is not finite take the initial value, as Chromium does.
    /// </summary>
    /// <param name="context">The build context, moved to the primitive.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="initial">The initial value.</param>
    /// <returns>The number.</returns>
    public static float GetNumber(SvgFilterBuildContext context, SvgAttributeId attribute, float initial)
        => context.TryGetValue(attribute, out SvgFilterAttributeValue value) && value.NumberCount == 1
            ? context.GetNumbers(in value)[0]
            : initial;

    /// <summary>
    /// Returns the area of the input that a lighting primitive reads for an output area: one pixel around it, for the
    /// 3 x 3 normal kernels.
    /// </summary>
    /// <param name="area">The output area.</param>
    /// <returns>The input area.</returns>
    public static Rectangle GetInputArea(Rectangle area) => Rectangle.Inflate(area, 1, 1);

    /// <summary>
    /// Lights the alpha surface of the input into the output.
    /// </summary>
    /// <param name="context">The context of the run.</param>
    /// <param name="input">The input image; only its alpha channel is read.</param>
    /// <param name="output">The output image, transparent, inside the subregion.</param>
    /// <param name="light">The light source.</param>
    /// <param name="color">The lighting color, sRGB.</param>
    /// <param name="surfaceScale">The height of the surface where the alpha is 1, in user units.</param>
    /// <param name="specular">Whether to apply the specular equation; otherwise the diffuse one.</param>
    /// <param name="constant">The diffuse or specular constant.</param>
    /// <param name="exponent">The specular exponent; 1 for the diffuse equation.</param>
    public static void Render(
        in SvgFilterRenderContext context,
        SvgFilterImage input,
        SvgFilterImage output,
        SvgLightSource light,
        Rgba32 color,
        float surfaceScale,
        bool specular,
        float constant,
        float exponent)
    {
        Rectangle area = output.Area;
        Rectangle image = context.Subregion;

        // The color in the space of the primitive, held in 8 bits, which is what the dark linearRGB results of the
        // Chromium oracle show.
        Vector4 converted = SvgFilterColor.FromSrgb(new Vector4(color.R, color.G, color.B, 255F) / 255F, context.ColorSpace);
        Vector3 lightColor = new Vector3(
            MathF.Round(converted.X * 255F),
            MathF.Round(converted.Y * 255F),
            MathF.Round(converted.Z * 255F)) / 255F;

        // The alpha of the input over the output area and one pixel around it, limited to the image: pixels outside the
        // image are never read, because the edge kernels stop at its border.
        Rectangle source = Rectangle.Intersect(Rectangle.Inflate(area, 1, 1), image);
        int stride = source.Width;
        using IMemoryOwner<byte> alphaOwner = context.Allocator.Allocate<byte>(stride * source.Height, AllocationOptions.Clean);
        Span<byte> alpha = alphaOwner.Memory.Span[..(stride * source.Height)];
        Rectangle copy = Rectangle.Intersect(source, input.Area);
        if (copy.Width > 0 && copy.Height > 0)
        {
            for (int y = copy.Top; y < copy.Bottom; y++)
            {
                ReadOnlySpan<Rgba32> row = input.GetRow(y).Slice(copy.Left - input.Area.Left, copy.Width);
                Span<byte> target = alpha.Slice(((y - source.Top) * stride) + (copy.Left - source.Left), copy.Width);
                for (int x = 0; x < row.Length; x++)
                {
                    target[x] = row[x].A;
                }
            }
        }

        Vector2 scale = context.Scale;

        // Filter space for the lighting: x and y in filter pixels, z in user units times the mean of the two scales.
        // The kernels step one pixel, and the normal factor folds in -surfaceScale, the z scale, the doubling of the
        // specification tables, and 1/255 for the alpha bytes.
        float zScale = (scale.X + scale.Y) / 2F;
        light = light.ToFilter(scale, context.Translation, zScale);
        float factor = -2F * surfaceScale * zScale / 255F;
        float height = surfaceScale * zScale / 255F;
        bool hasExponent = exponent != 1F;

        for (int y = area.Top; y < area.Bottom; y++)
        {
            // The rows of the kernel: the neighbours that lie inside the image, weighted 1, around the center row,
            // weighted 2. The vertical difference spans the outer rows that exist.
            int top = Math.Max(y - 1, image.Top);
            int bottom = Math.Min(y + 1, image.Bottom - 1);
            float aboveWeight = top < y ? 1F : 0F;
            float belowWeight = bottom > y ? 1F : 0F;
            float rowWeights = 2F + aboveWeight + belowWeight;
            int rowSpan = bottom - top;
            float rowFactorX = factor / rowWeights;
            float rowFactorY = rowSpan > 0 ? factor / rowSpan : 0F;
            ReadOnlySpan<byte> above = alpha.Slice((top - source.Top) * stride, stride);
            ReadOnlySpan<byte> center = alpha.Slice((y - source.Top) * stride, stride);
            ReadOnlySpan<byte> below = alpha.Slice((bottom - source.Top) * stride, stride);
            float centerY = y + 0.5F;
            Span<Rgba32> outputRow = output.GetRow(y);

            for (int x = area.Left; x < area.Right; x++)
            {
                int left = Math.Max(x - 1, image.Left) - source.Left;
                int right = Math.Min(x + 1, image.Right - 1) - source.Left;
                int i = x - source.Left;
                float leftWeight = left < i ? 1F : 0F;
                float rightWeight = right > i ? 1F : 0F;
                float columnWeights = 2F + leftWeight + rightWeight;
                int columnSpan = right - left;

                // Horizontal difference, weighted over the rows; vertical difference, weighted over the columns. A
                // missing neighbour row or column has weight 0, and the span is then 1 instead of 2.
                float dx = (aboveWeight * (above[right] - above[left]))
                    + (2F * (center[right] - center[left]))
                    + (belowWeight * (below[right] - below[left]));
                float dy = (leftWeight * (below[left] - above[left]))
                    + (2F * (below[i] - above[i]))
                    + (rightWeight * (below[right] - above[right]));
                float nx = columnSpan > 0 ? dx * rowFactorX / columnSpan : 0F;
                float ny = dy * rowFactorY / columnWeights;

                Vector3 normal = Vector3.Normalize(new Vector3(nx, ny, 1F));
                Vector3 surface = new(x + 0.5F, centerY, center[i] * height);
                Vector3 l = light.GetLight(surface, out float lightFactor);

                if (!specular)
                {
                    float diffuse = constant * Vector3.Dot(normal, l) * lightFactor;
                    outputRow[x - area.Left] = new Rgba32(
                        ToByte(diffuse * lightColor.X),
                        ToByte(diffuse * lightColor.Y),
                        ToByte(diffuse * lightColor.Z),
                        255);
                    continue;
                }

                // H = (L + E) / Norm(L + E); a light straight below the surface has no half vector and gives no light.
                Vector3 half = l + Vector3.UnitZ;
                float length = half.Length();
                float cosine = length > 0F ? Vector3.Dot(normal, half) / length : 0F;
                cosine = cosine > 0F ? cosine : 0F;
                float specularFactor = constant * (hasExponent ? MathF.Pow(cosine, exponent) : cosine) * lightFactor;
                byte r = ToByte(specularFactor * lightColor.X);
                byte g = ToByte(specularFactor * lightColor.Y);
                byte b = ToByte(specularFactor * lightColor.Z);
                outputRow[x - area.Left] = new Rgba32(r, g, b, Math.Max(r, Math.Max(g, b)));
            }
        }
    }

    /// <summary>
    /// Converts a channel in the range 0 to 1 to a byte with rounding. Values outside the range are clamped, and a value
    /// that is not a number gives 0.
    /// </summary>
    /// <param name="value">The channel.</param>
    /// <returns>The byte.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ToByte(float value)
        => value > 0F ? (value < 1F ? (byte)((value * 255F) + 0.5F) : (byte)255) : (byte)0;
}
