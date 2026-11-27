// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The pixels of a decoded <c>image</c>, of a rendered <c>pattern</c> tile, or of a filter result, with premultiplied
/// alpha, and the box-filtered levels that <see cref="SvgTextureBrush"/> samples when it draws the texture smaller.
/// </summary>
/// <remarks>
/// <para>
/// Ownership. A texture made from an array owns the array and nothing else, needs no disposal, and the garbage
/// collector frees it when the last document, brush, or deferred canvas command that refers to it is gone. That is what
/// lets an immutable, non-disposable <see cref="SvgDocument"/> keep decoded images and pattern tiles. A texture made
/// from a pooled buffer (a filter result, which belongs to one draw) owns that buffer and returns it to its allocator on
/// <see cref="Dispose"/>. The emitter hands such a texture to the canvas that draws it
/// (<see cref="DrawingCanvas.TryTakeOwnership"/>): the canvas disposes it after its recorded commands have rendered, or
/// moves it to the retained scene that holds those commands, which disposes it with the scene. So the pixels live as
/// long as any command that samples them can still run.
/// </para>
/// <para>
/// The pixels are premultiplied, so bilinear filtering and the box filter of the smaller levels weight color by
/// coverage, and transparent texels do not bleed their color into opaque ones. Level <c>n</c> has half the size of level
/// <c>n - 1</c>, rounded up, down to one pixel. The levels are built on first use, are managed arrays whatever holds level
/// 0, and are safe to request from several threads at the same time.
/// </para>
/// </remarks>
internal sealed class SvgTexture : IDisposable
{
    private readonly object levelLock = new();
    private SvgTexture?[]? levels;
    private IMemoryOwner<Rgba32>? owner;
    private Memory<Rgba32> memory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTexture"/> class. The texture takes ownership of the array.
    /// </summary>
    /// <param name="pixels">The pixels in rows, with premultiplied alpha. The length is <paramref name="width"/> times <paramref name="height"/>.</param>
    /// <param name="width">The width in pixels. It is greater than zero.</param>
    /// <param name="height">The height in pixels. It is greater than zero.</param>
    public SvgTexture(Rgba32[] pixels, int width, int height)
    {
        this.memory = pixels;
        this.Width = width;
        this.Height = height;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTexture"/> class over a pooled buffer. The texture takes
    /// ownership of the buffer and returns it to its allocator on <see cref="Dispose"/>.
    /// </summary>
    /// <param name="owner">The buffer.</param>
    /// <param name="pixels">The pixels in rows, with premultiplied alpha: the first <paramref name="width"/> times <paramref name="height"/> elements of the buffer.</param>
    /// <param name="width">The width in pixels. It is greater than zero.</param>
    /// <param name="height">The height in pixels. It is greater than zero.</param>
    public SvgTexture(IMemoryOwner<Rgba32> owner, Memory<Rgba32> pixels, int width, int height)
    {
        this.owner = owner;
        this.memory = pixels;
        this.Width = width;
        this.Height = height;
    }

    /// <summary>
    /// Gets the pixels in rows, with premultiplied alpha. Empty after <see cref="Dispose"/> of a pooled texture.
    /// </summary>
    public ReadOnlySpan<Rgba32> Pixels => this.memory.Span;

    /// <summary>
    /// Gets the width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Creates a texture from pixels with straight alpha, as decoders and the canvas produce them, by premultiplying
    /// them in place. The texture takes ownership of the array.
    /// </summary>
    /// <param name="pixels">The pixels in rows, with straight alpha.</param>
    /// <param name="width">The width in pixels. It is greater than zero.</param>
    /// <param name="height">The height in pixels. It is greater than zero.</param>
    /// <returns>The texture.</returns>
    public static SvgTexture FromStraight(Rgba32[] pixels, int width, int height)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            ref Rgba32 pixel = ref pixels[i];
            byte a = pixel.A;
            if (a == 255)
            {
                continue;
            }

            // Rounded division by 255, exact for every byte product.
            pixel.R = Multiply(pixel.R, a);
            pixel.G = Multiply(pixel.G, a);
            pixel.B = Multiply(pixel.B, a);
        }

        return new SvgTexture(pixels, width, height);
    }

    /// <summary>
    /// Returns a texture with a managed copy of the pixels, which needs no disposal.
    /// </summary>
    /// <returns>The copy.</returns>
    public SvgTexture ToManaged() => new(this.Pixels.ToArray(), this.Width, this.Height);

    /// <summary>
    /// Returns the pooled buffer of the texture to its allocator. A texture made from an array has nothing to release.
    /// No brush may sample the texture afterwards.
    /// </summary>
    public void Dispose()
    {
        this.owner?.Dispose();
        this.owner = null;
        this.memory = default;
    }

    /// <summary>
    /// Returns the level of a given index: level 0 is this texture, and each further level halves the size.
    /// </summary>
    /// <param name="level">The level index. Indexes past the last level, which is one pixel in size on its longer side, return the last level.</param>
    /// <returns>The level.</returns>
    public SvgTexture GetLevel(int level)
    {
        if (level <= 0)
        {
            return this;
        }

        SvgTexture?[] levels = this.GetLevels();
        return levels[Math.Min(level, levels.Length) - 1]!;
    }

    /// <summary>
    /// Returns the smaller levels, building them on first use.
    /// </summary>
    /// <returns>The levels from 1 on.</returns>
    private SvgTexture?[] GetLevels()
    {
        SvgTexture?[]? levels = Volatile.Read(ref this.levels);
        if (levels is not null)
        {
            return levels;
        }

        lock (this.levelLock)
        {
            if (this.levels is not null)
            {
                return this.levels;
            }

            int count = 0;
            for (int size = Math.Max(this.Width, this.Height); size > 1; size = (size + 1) / 2)
            {
                count++;
            }

            // A one-pixel texture has no smaller level; it returns itself.
            levels = new SvgTexture?[Math.Max(count, 1)];
            SvgTexture source = this;
            for (int i = 0; i < count; i++)
            {
                source = source.Halve();
                levels[i] = source;
            }

            if (count == 0)
            {
                levels[0] = this;
            }

            Volatile.Write(ref this.levels, levels);
            return levels;
        }
    }

    /// <summary>
    /// Builds the next smaller level with a 2 x 2 box filter. An odd last row or column averages with itself.
    /// </summary>
    /// <returns>The smaller texture.</returns>
    private SvgTexture Halve()
    {
        int width = (this.Width + 1) / 2;
        int height = (this.Height + 1) / 2;
        ReadOnlySpan<Rgba32> source = this.Pixels;
        Rgba32[] result = new Rgba32[width * height];

        for (int y = 0; y < height; y++)
        {
            int y0 = Math.Min(y * 2, this.Height - 1) * this.Width;
            int y1 = Math.Min((y * 2) + 1, this.Height - 1) * this.Width;
            for (int x = 0; x < width; x++)
            {
                int x0 = Math.Min(x * 2, this.Width - 1);
                int x1 = Math.Min((x * 2) + 1, this.Width - 1);
                Rgba32 a = source[y0 + x0];
                Rgba32 b = source[y0 + x1];
                Rgba32 c = source[y1 + x0];
                Rgba32 d = source[y1 + x1];

                // Premultiplied channels average linearly; the + 2 rounds to nearest.
                result[(y * width) + x] = new Rgba32(
                    (byte)((a.R + b.R + c.R + d.R + 2) >> 2),
                    (byte)((a.G + b.G + c.G + d.G + 2) >> 2),
                    (byte)((a.B + b.B + c.B + d.B + 2) >> 2),
                    (byte)((a.A + b.A + c.A + d.A + 2) >> 2));
            }
        }

        return new SvgTexture(result, width, height);
    }

    /// <summary>
    /// Multiplies two bytes as fractions of 255 with rounding.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="alpha">The alpha.</param>
    /// <returns>The product.</returns>
    private static byte Multiply(byte value, byte alpha)
    {
        int product = (value * alpha) + 128;
        return (byte)((product + (product >> 8)) >> 8);
    }
}
