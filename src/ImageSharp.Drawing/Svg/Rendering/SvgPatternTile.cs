// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The resolved content of one <c>pattern</c> tile, and the cache of its renders at the resolutions that draws ask for.
/// </summary>
/// <remarks>
/// <para>
/// The content is a small document of its own: the render nodes of the pattern children, in tile space, where the tile
/// is the rectangle from the origin to <see cref="Size"/>. Its mapped box is that rectangle, with
/// <c>preserveAspectRatio="none"</c>, so drawing it into a <c>W</c> x <c>H</c> pixel rectangle scales tile space by
/// <c>W / width</c> and <c>H / height</c>: rounding the tile to whole pixels changes the scale a little and leaves no gap
/// between tiles. The canvas bounds clip the content to the tile, which is the <c>overflow: hidden</c> of a pattern.
/// </para>
/// <para>
/// Tiles render on first use per pixel size and stay in a small cache that is shared by every render of the document,
/// so the cache is guarded by a lock. A tile renders while the lock is held, so concurrent first draws of one pattern
/// render it once. Nested patterns have their own tiles and locks; the resolve stage has rejected reference cycles, so a
/// render never waits for a lock that it holds.
/// </para>
/// </remarks>
internal sealed class SvgPatternTile
{
    /// <summary>
    /// The number of tile renders, at different pixel sizes, that the cache keeps. A document drawn at a few output sizes
    /// reuses its tiles; a document drawn at ever new sizes replaces the oldest entry instead of growing.
    /// </summary>
    private const int CacheSize = 4;

    private readonly SvgDocument content;
    private readonly int maxPixels;
    private readonly (int Width, int Height, SvgTexture Texture)[] cache = new (int, int, SvgTexture)[CacheSize];
    private int cacheCount;
    private int nextSlot;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgPatternTile"/> class.
    /// </summary>
    /// <param name="content">The content as a document whose mapped box is the tile rectangle.</param>
    /// <param name="maxPixels">
    /// The largest number of pixels of one tile render: <see cref="SvgLimits.MaxPatternTilePixels"/>, lowered for a tile
    /// of a deeply nested pattern.
    /// </param>
    public SvgPatternTile(SvgDocument content, int maxPixels)
    {
        this.content = content;
        this.maxPixels = maxPixels;
    }

    /// <summary>
    /// Gets the tile size in tile space.
    /// </summary>
    public SizeF Size => this.content.MappedBox.Size;

    /// <summary>
    /// Gets the largest number of pixels of one tile render.
    /// </summary>
    public int MaxPixels => this.maxPixels;

    /// <summary>
    /// Returns the tile rendered at a pixel size, rendering it on first use.
    /// </summary>
    /// <param name="width">The width in pixels. It is greater than zero.</param>
    /// <param name="height">The height in pixels. It is greater than zero. The product with the width is at most <see cref="MaxPixels"/>.</param>
    /// <returns>The rendered tile.</returns>
    public SvgTexture GetTexture(int width, int height)
    {
        lock (this.cache)
        {
            for (int i = 0; i < this.cacheCount; i++)
            {
                (int w, int h, SvgTexture texture) = this.cache[i];
                if (w == width && h == height)
                {
                    return texture;
                }
            }

            SvgTexture result = this.Render(width, height);
            this.cache[this.nextSlot] = (width, height, result);
            this.nextSlot = (this.nextSlot + 1) % CacheSize;
            this.cacheCount = Math.Min(this.cacheCount + 1, CacheSize);
            return result;
        }
    }

    /// <summary>
    /// Renders the tile content into a new texture.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The texture.</returns>
    private SvgTexture Render(int width, int height)
    {
        // The canvas draws into a managed array through an image that wraps it. The image does not own the memory, so
        // disposing it leaves the array to the texture, and no pooled buffer outlives this method.
        Rgba32[] pixels = new Rgba32[width * height];
        using (Image<Rgba32> image = Image.WrapMemory<Rgba32>(Configuration.Default, pixels.AsMemory(), width, height))
        {
            using DrawingCanvas canvas = image.Frames.RootFrame.CreateCanvas(Configuration.Default, new DrawingOptions());
            canvas.DrawSvg(this.content, new RectangleF(0F, 0F, width, height));
        }

        return SvgTexture.FromStraight(pixels, width, height);
    }
}
