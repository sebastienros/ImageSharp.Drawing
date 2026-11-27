// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One image of a filter run: a rectangle of filter space and its pixels, premultiplied RGBA with 8 bits per channel,
/// tagged with the color space that the color channels are in.
/// </summary>
/// <remarks>
/// <para>
/// Filter space is the pixel grid that the filter runs on (see <see cref="SvgFilterRenderContext"/>). <see cref="Area"/>
/// is the part of it that the image holds; every pixel outside it is transparent black. Rows are stored top to bottom,
/// <see cref="Area"/>.Width pixels each, without padding. <see cref="Rgba32"/> is the storage type only: the channels are
/// premultiplied by alpha, so a valid pixel has every color channel at most its alpha.
/// </para>
/// <para>
/// Ownership: the pixels come from the memory allocator of the configuration and return to it on
/// <see cref="Dispose"/>. The engine owns every image of a run and disposes each after its last consumer. A primitive
/// writes only to its output image and does not keep references to any image after it returns. A band view
/// (<see cref="GetBand"/>) shares the pixels of its image and owns nothing.
/// </para>
/// </remarks>
internal sealed class SvgFilterImage : IDisposable
{
    private IMemoryOwner<Rgba32>? buffer;
    private Memory<Rgba32> memory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterImage"/> class with transparent pixels.
    /// </summary>
    /// <param name="allocator">The allocator of the pixels.</param>
    /// <param name="area">The rectangle of filter space that the image holds. It can be empty.</param>
    /// <param name="colorSpace">The color space of the color channels: sRGB or linearRGB.</param>
    public SvgFilterImage(MemoryAllocator allocator, Rectangle area, SvgColorInterpolation colorSpace)
    {
        this.Area = area.Width > 0 && area.Height > 0 ? area : Rectangle.Empty;
        this.ColorSpace = colorSpace;
        if (!this.Area.IsEmpty)
        {
            int length = this.Area.Width * this.Area.Height;
            this.buffer = allocator.Allocate<Rgba32>(length, AllocationOptions.Clean);
            this.memory = this.buffer.Memory[..length];
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterImage"/> class that views pixels of another image. The view
    /// does not own them: disposing it releases nothing.
    /// </summary>
    /// <param name="memory">The pixels, row by row, <paramref name="area"/>.Width pixels each.</param>
    /// <param name="area">The rectangle of filter space that the pixels hold; not empty.</param>
    /// <param name="colorSpace">The color space of the color channels.</param>
    private SvgFilterImage(Memory<Rgba32> memory, Rectangle area, SvgColorInterpolation colorSpace)
    {
        this.Area = area;
        this.ColorSpace = colorSpace;
        this.memory = memory;
    }

    /// <summary>
    /// Gets the rectangle of filter space that the image holds. It is empty for an image that is transparent everywhere.
    /// </summary>
    public Rectangle Area { get; }

    /// <summary>
    /// Gets or sets the color space of the color channels: <see cref="SvgColorInterpolation.SRgb"/> or
    /// <see cref="SvgColorInterpolation.LinearRgb"/>.
    /// </summary>
    public SvgColorInterpolation ColorSpace { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every color channel of every pixel is 0, so that only the alpha channel
    /// carries data, as in <c>SourceAlpha</c> and the results that only move or blur it. Primitives that know this can
    /// compute the alpha channel alone, and a color space conversion leaves such an image unchanged. The code that
    /// creates the pixels sets it; <see langword="false"/> is always safe.
    /// </summary>
    public bool IsAlphaOnly { get; set; }

    /// <summary>
    /// Gets a value indicating whether the image is transparent everywhere because its area is empty.
    /// </summary>
    public bool IsEmpty => this.Area.IsEmpty;

    /// <summary>
    /// Gets all pixels, row by row. Empty for an empty image.
    /// </summary>
    public Span<Rgba32> Pixels => this.Memory.Span;

    /// <summary>
    /// Gets the memory of all pixels, row by row, for code that wraps it in an image. Empty for an empty image.
    /// </summary>
    public Memory<Rgba32> Memory => this.memory;

    /// <summary>
    /// Returns one row of the image.
    /// </summary>
    /// <param name="y">The row in filter space, inside <see cref="Area"/>.</param>
    /// <returns>The <see cref="Area"/>.Width pixels of the row, from <see cref="Area"/>.Left.</returns>
    public Span<Rgba32> GetRow(int y) => this.memory.Span.Slice((y - this.Area.Top) * this.Area.Width, this.Area.Width);

    /// <summary>
    /// Returns a view of a band of rows of the image, which a primitive can fill on its own thread. See
    /// <see cref="SvgFilterPrimitive.CanRenderInBands"/>.
    /// </summary>
    /// <param name="top">The first row of the band in filter space, inside <see cref="Area"/>.</param>
    /// <param name="bottom">The row after the last row of the band, greater than <paramref name="top"/> and at most <see cref="Area"/>.Bottom.</param>
    /// <returns>The view, which does not own its pixels.</returns>
    public SvgFilterImage GetBand(int top, int bottom)
    {
        Rectangle area = this.Area;
        Memory<Rgba32> rows = this.memory.Slice((top - area.Top) * area.Width, (bottom - top) * area.Width);
        return new SvgFilterImage(rows, Rectangle.FromLTRB(area.Left, top, area.Right, bottom), this.ColorSpace);
    }

    /// <summary>
    /// Returns a pixel, or transparent black outside <see cref="Area"/>.
    /// </summary>
    /// <param name="x">The column in filter space.</param>
    /// <param name="y">The row in filter space.</param>
    /// <returns>The premultiplied pixel.</returns>
    public Rgba32 GetPixel(int x, int y)
    {
        Rectangle area = this.Area;
        if ((uint)(x - area.X) >= (uint)area.Width || (uint)(y - area.Y) >= (uint)area.Height)
        {
            return default;
        }

        return this.memory.Span[((y - area.Y) * area.Width) + (x - area.X)];
    }

    /// <summary>
    /// Copies the part of a row of this image that lies in a span of columns into a destination, with transparent black
    /// for the columns outside <see cref="Area"/>.
    /// </summary>
    /// <param name="y">The row in filter space.</param>
    /// <param name="left">The first column in filter space.</param>
    /// <param name="destination">The destination; its length is the number of columns.</param>
    public void CopyRow(int y, int left, Span<Rgba32> destination)
    {
        Rectangle area = this.Area;
        int start = Math.Max(left, area.Left);
        int end = Math.Min(left + destination.Length, area.Right);
        if (y < area.Top || y >= area.Bottom || start >= end)
        {
            destination.Clear();
            return;
        }

        destination[..(start - left)].Clear();
        this.GetRow(y).Slice(start - area.Left, end - start).CopyTo(destination[(start - left)..]);
        destination[(end - left)..].Clear();
    }

    /// <summary>
    /// Moves the pixels of this image into a texture without a copy. The texture owns the pooled buffer afterwards, and
    /// this image is left without pixels: only <see cref="Dispose"/> may be called on it.
    /// </summary>
    /// <returns>The texture, which the caller disposes.</returns>
    public SvgTexture MoveToTexture()
    {
        // Only an image that owns a buffer and has pixels can give them away; the engine calls this on the result of a
        // run, which is never a band view or empty.
        SvgTexture texture = new(this.buffer!, this.memory, this.Area.Width, this.Area.Height);
        this.buffer = null;
        this.memory = default;
        return texture;
    }

    /// <summary>
    /// Returns a view of the pixels of this image tagged with another color space, for an image whose color channels are
    /// all 0 (<see cref="IsAlphaOnly"/>): a conversion would leave every pixel unchanged (see
    /// <see cref="SvgFilterColor.Convert(SvgFilterImage, SvgColorInterpolation)"/>), so the view stands for the converted
    /// copy. The view does not own the pixels: disposing it releases nothing.
    /// </summary>
    /// <param name="colorSpace">The color space of the view.</param>
    /// <returns>The view.</returns>
    public SvgFilterImage ViewAs(SvgColorInterpolation colorSpace)
        => new(this.memory, this.Area, colorSpace) { IsAlphaOnly = true };

    /// <inheritdoc/>
    public void Dispose()
    {
        this.buffer?.Dispose();
        this.buffer = null;
        this.memory = default;
    }
}
