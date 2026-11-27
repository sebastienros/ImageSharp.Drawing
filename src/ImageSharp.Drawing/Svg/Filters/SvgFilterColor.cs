// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Color conversions of the filter engine: between premultiplied 8-bit pixels and straight floating point colors, and
/// between the sRGB and linearRGB color spaces of <c>color-interpolation-filters</c>.
/// </summary>
/// <remarks>
/// The color spaces convert on straight (unpremultiplied) values with one table of 256 entries per direction, as Filter
/// Effects defines the conversion per channel: linear = ((srgb + 0.055) / 1.055)^2.4 above 0.04045 and srgb / 12.92
/// below, and the inverse. Alpha does not change. The 8-bit result of each direction is rounded, so converting to one
/// space and back is not exact for dark colors; the reference renderers store their intermediate images in 8 bits as
/// well.
/// </remarks>
internal static class SvgFilterColor
{
    /// <summary>
    /// The number of intervals of the table that converts linear light in floating point to sRGB.
    /// </summary>
    private const int FloatTableSize = 4096;

    private static readonly byte[] SrgbToLinear = CreateTable(true);
    private static readonly byte[] LinearToSrgb = CreateTable(false);
    private static readonly float[] LinearToSrgbFloat = CreateFloatTable();

    /// <summary>
    /// Converts the color channels of an image to another color space in place and tags the image with it.
    /// </summary>
    /// <remarks>
    /// The conversion leaves a pixel with zero color channels unchanged: both tables map 0 to 0, the straight value of
    /// a zero channel is (0 + alpha / 2) / alpha = 0, and 0 premultiplies to (128 + 0) >> 8 = 0. So an image whose color
    /// channels are all 0 (<see cref="SvgFilterImage.IsAlphaOnly"/>) is only tagged.
    /// </remarks>
    /// <param name="image">The image.</param>
    /// <param name="target">The color space to convert to.</param>
    public static void Convert(SvgFilterImage image, SvgColorInterpolation target)
    {
        if (image.ColorSpace == target)
        {
            return;
        }

        if (image.IsAlphaOnly)
        {
            image.ColorSpace = target;
            return;
        }

        byte[] table = target == SvgColorInterpolation.LinearRgb ? SrgbToLinear : LinearToSrgb;
        Span<Rgba32> pixels = image.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            ref Rgba32 pixel = ref pixels[i];
            byte a = pixel.A;
            if (a == 0)
            {
                continue;
            }

            if (a == 255)
            {
                pixel.R = table[pixel.R];
                pixel.G = table[pixel.G];
                pixel.B = table[pixel.B];
                continue;
            }

            // Unpremultiply with rounding, convert, premultiply with rounding. A color channel is at most the alpha, so
            // the straight value fits in a byte.
            int half = a >> 1;
            pixel.R = Multiply(table[((pixel.R * 255) + half) / a], a);
            pixel.G = Multiply(table[((pixel.G * 255) + half) / a], a);
            pixel.B = Multiply(table[((pixel.B * 255) + half) / a], a);
        }

        image.ColorSpace = target;
    }

    /// <summary>
    /// Converts one straight color, in the range 0 to 1, from sRGB to another color space.
    /// </summary>
    /// <param name="color">The straight sRGB color with alpha.</param>
    /// <param name="target">The color space to convert to.</param>
    /// <returns>The straight color in <paramref name="target"/>.</returns>
    public static Vector4 FromSrgb(Vector4 color, SvgColorInterpolation target)
    {
        if (target != SvgColorInterpolation.LinearRgb)
        {
            return color;
        }

        return new Vector4(ToLinear(color.X), ToLinear(color.Y), ToLinear(color.Z), color.W);
    }

    /// <summary>
    /// Converts the color channels of a straight color, in the range 0 to 1, from one color space to another in floating
    /// point. Linear light converts to sRGB through a table of 4096 intervals with linear interpolation, which is
    /// within 0.1 of an 8-bit level.
    /// </summary>
    /// <param name="color">The straight color with alpha.</param>
    /// <param name="from">The color space of <paramref name="color"/>.</param>
    /// <param name="to">The color space to convert to.</param>
    /// <returns>The straight color in <paramref name="to"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Convert(Vector4 color, SvgColorInterpolation from, SvgColorInterpolation to)
    {
        if (from == to)
        {
            return color;
        }

        if (to == SvgColorInterpolation.LinearRgb)
        {
            return new Vector4(ToLinear(color.X), ToLinear(color.Y), ToLinear(color.Z), color.W);
        }

        return new Vector4(ToSrgbFast(color.X), ToSrgbFast(color.Y), ToSrgbFast(color.Z), color.W);
    }

    /// <summary>
    /// Converts a premultiplied pixel to a straight color with channels in the range 0 to 1.
    /// </summary>
    /// <param name="pixel">The premultiplied pixel.</param>
    /// <returns>The straight color; transparent black for a pixel with zero alpha.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 Unpremultiply(Rgba32 pixel)
    {
        if (pixel.A == 0)
        {
            return Vector4.Zero;
        }

        float scale = 1F / pixel.A;
        return new Vector4(pixel.R * scale, pixel.G * scale, pixel.B * scale, pixel.A / 255F);
    }

    /// <summary>
    /// Converts a straight color to a premultiplied pixel. Channels outside the range 0 to 1 are clamped.
    /// </summary>
    /// <param name="color">The straight color.</param>
    /// <returns>The premultiplied pixel, rounded.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 Premultiply(Vector4 color)
    {
        color = Vector4.Clamp(color, Vector4.Zero, Vector4.One);
        float a = color.W * 255F;
        return new Rgba32(
            (byte)((color.X * a) + 0.5F),
            (byte)((color.Y * a) + 0.5F),
            (byte)((color.Z * a) + 0.5F),
            (byte)(a + 0.5F));
    }

    /// <summary>
    /// Converts premultiplied channels in the range 0 to 1 to a pixel. Channels are clamped to the range 0 to 1 and the
    /// color channels to at most the alpha, so the pixel is a valid premultiplied pixel.
    /// </summary>
    /// <param name="color">The premultiplied color.</param>
    /// <returns>The pixel, rounded.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 FromPremultiplied(Vector4 color)
    {
        float a = Math.Clamp(color.W, 0F, 1F);
        color = Vector4.Clamp(color, Vector4.Zero, new Vector4(a, a, a, 1F)) * 255F;
        return new Rgba32((byte)(color.X + 0.5F), (byte)(color.Y + 0.5F), (byte)(color.Z + 0.5F), (byte)((a * 255F) + 0.5F));
    }

    /// <summary>
    /// Multiplies two bytes as fractions of 255 with rounding, exact for every pair.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="alpha">The alpha.</param>
    /// <returns>The product.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Multiply(int value, int alpha)
    {
        int product = (value * alpha) + 128;
        return (byte)((product + (product >> 8)) >> 8);
    }

    /// <summary>
    /// Converts one sRGB channel to linear light.
    /// </summary>
    /// <param name="value">The channel in the range 0 to 1.</param>
    /// <returns>The linear channel.</returns>
    private static float ToLinear(float value)
        => value <= 0.04045F ? value / 12.92F : MathF.Pow((value + 0.055F) / 1.055F, 2.4F);

    /// <summary>
    /// Converts one linear channel to sRGB.
    /// </summary>
    /// <param name="value">The channel in the range 0 to 1.</param>
    /// <returns>The sRGB channel.</returns>
    private static float ToSrgb(float value)
        => value <= 0.0031308F ? value * 12.92F : (1.055F * MathF.Pow(value, 1F / 2.4F)) - 0.055F;

    /// <summary>
    /// Converts one linear channel to sRGB through the floating point table.
    /// </summary>
    /// <param name="value">The channel; values outside the range 0 to 1 are clamped.</param>
    /// <returns>The sRGB channel.</returns>
    private static float ToSrgbFast(float value)
    {
        float position = Math.Clamp(value, 0F, 1F) * FloatTableSize;
        int index = Math.Min((int)position, FloatTableSize - 1);
        float t = position - index;
        return LinearToSrgbFloat[index] + ((LinearToSrgbFloat[index + 1] - LinearToSrgbFloat[index]) * t);
    }

    /// <summary>
    /// Builds the table that converts linear light to sRGB in floating point.
    /// </summary>
    /// <returns>The table of <see cref="FloatTableSize"/> + 1 entries.</returns>
    private static float[] CreateFloatTable()
    {
        float[] table = new float[FloatTableSize + 1];
        for (int i = 0; i <= FloatTableSize; i++)
        {
            table[i] = ToSrgb(i / (float)FloatTableSize);
        }

        return table;
    }

    /// <summary>
    /// Builds a conversion table of 256 entries.
    /// </summary>
    /// <param name="toLinear">Whether the table converts sRGB to linear light; otherwise linear light to sRGB.</param>
    /// <returns>The table.</returns>
    private static byte[] CreateTable(bool toLinear)
    {
        byte[] table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            float value = i / 255F;
            float converted = toLinear ? ToLinear(value) : ToSrgb(value);
            table[i] = (byte)Math.Clamp((int)MathF.Round(converted * 255F), 0, 255);
        }

        return table;
    }
}
