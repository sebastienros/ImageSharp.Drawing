// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// Rendering a document into a new image.
/// </content>
public sealed partial class SvgDocument
{
    /// <summary>
    /// The excess over a whole number of pixels that the intrinsic size can have and still round to that number. Unit
    /// conversions in single precision can add such an excess, for example to <c>2.54cm</c>.
    /// </summary>
    private const float PixelSizeTolerance = 0.01F;

    /// <summary>
    /// The longest side of an image whose size the document determines. The pixel limit alone does not bound the memory
    /// of a render: every rasterizer worker keeps scratch rows of the width of the canvas, about 128 bytes per pixel of
    /// width, so a document of 8,000,000 x 8 pixels, within the pixel limit, would make one worker allocate 1 GB. At
    /// 65,535 pixels a worker needs about 8 MB.
    /// </summary>
    internal const int MaxRenderSide = 65535;

    /// <summary>
    /// Gets the largest number of pixels of an image that <see cref="Render{TPixel}(Configuration, Color)"/> creates at
    /// the intrinsic size: <see cref="SvgLimits.MaxImagePixels"/> of the load options. Documents that are not loaded
    /// through a public entry point have no limit.
    /// </summary>
    internal long MaxRenderPixels { get; private set; } = long.MaxValue;

    /// <summary>
    /// Renders the document into a new image at its intrinsic size, over a transparent background, with
    /// <see cref="Configuration.Default"/>.
    /// </summary>
    /// <remarks>
    /// See <see cref="Render{TPixel}(Configuration, Color)"/>.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format of the image.</typeparam>
    /// <returns>The image. The caller owns it and disposes it.</returns>
    /// <exception cref="InvalidImageContentException">
    /// The intrinsic size is not a usable image size, the image would have more than <see cref="SvgLimits.MaxImagePixels"/>
    /// pixels or a side longer than 65,535 pixels, or drawing needs more than <see cref="SvgLimits.MaxLayerPixels"/> layer
    /// pixels.
    /// </exception>
    public Image<TPixel> Render<TPixel>()
        where TPixel : unmanaged, IPixel<TPixel>
        => this.Render<TPixel>(Configuration.Default, Color.Transparent);

    /// <summary>
    /// Renders the document into a new image of a given size, over a transparent background, with
    /// <see cref="Configuration.Default"/>.
    /// </summary>
    /// <remarks>
    /// See <see cref="Render{TPixel}(Configuration, Size, Color)"/>.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format of the image.</typeparam>
    /// <param name="size">The size of the image, in pixels.</param>
    /// <returns>The image. The caller owns it and disposes it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The width or the height of <paramref name="size"/> is not greater than zero.</exception>
    /// <exception cref="InvalidImageContentException">Drawing needs more than <see cref="SvgLimits.MaxLayerPixels"/> layer pixels.</exception>
    public Image<TPixel> Render<TPixel>(Size size)
        where TPixel : unmanaged, IPixel<TPixel>
        => this.Render<TPixel>(Configuration.Default, size, Color.Transparent);

    /// <summary>
    /// Renders the document into a new image at its intrinsic size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The image has the intrinsic size (<see cref="Size"/>) rounded up to whole pixels; an excess of less than 0.01
    /// pixels over a whole number is dropped. The document draws at its intrinsic size from the top left corner, as
    /// <see cref="DrawingCanvas.DrawSvg(SvgDocument, PointF)"/> draws it, so a fractional size leaves a part of the last
    /// column and row with only the background.
    /// </para>
    /// <para>
    /// The size comes from the document, so it is checked before the image is allocated: an image of more than
    /// <see cref="SvgLimits.MaxImagePixels"/> pixels of the load options, or with a side longer than 65,535 pixels, is
    /// refused. The memory of the rasterizer grows with the width of the image, so the side is bounded apart from the
    /// pixel count.
    /// </para>
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format of the image.</typeparam>
    /// <param name="configuration">The configuration of the image and of the drawing.</param>
    /// <param name="backgroundColor">The color that fills the image before the document draws over it.</param>
    /// <returns>The image. The caller owns it and disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">
    /// The intrinsic size is not a usable image size (a side is zero or not finite), the image would have more than
    /// <see cref="SvgLimits.MaxImagePixels"/> pixels or a side longer than 65,535 pixels, or drawing needs more than
    /// <see cref="SvgLimits.MaxLayerPixels"/> layer pixels.
    /// </exception>
    public Image<TPixel> Render<TPixel>(Configuration configuration, Color backgroundColor)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));

        Size size = this.GetPixelSize();
        CheckRenderPixels(size, this.MaxRenderPixels);
        return this.RenderCore<TPixel>(configuration, size, new RectangleF(PointF.Empty, this.Size), backgroundColor);
    }

    /// <summary>
    /// Renders the document into a new image of a given size.
    /// </summary>
    /// <remarks>
    /// The <c>viewBox</c> of the document, or the rectangle of its intrinsic size when it has none, fits the whole image
    /// following the <c>preserveAspectRatio</c> of the root element, as
    /// <see cref="DrawingCanvas.DrawSvg(SvgDocument, RectangleF)"/> fits it into a destination. The vector content renders
    /// at that size; nothing is resampled. The size is the choice of the caller, so
    /// <see cref="SvgLimits.MaxImagePixels"/> does not apply.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format of the image.</typeparam>
    /// <param name="configuration">The configuration of the image and of the drawing.</param>
    /// <param name="size">The size of the image, in pixels.</param>
    /// <param name="backgroundColor">The color that fills the image before the document draws over it.</param>
    /// <returns>The image. The caller owns it and disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The width or the height of <paramref name="size"/> is not greater than zero.</exception>
    /// <exception cref="InvalidImageContentException">Drawing needs more than <see cref="SvgLimits.MaxLayerPixels"/> layer pixels.</exception>
    public Image<TPixel> Render<TPixel>(Configuration configuration, Size size, Color backgroundColor)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.MustBeGreaterThan(size.Width, 0, nameof(size));
        Guard.MustBeGreaterThan(size.Height, 0, nameof(size));

        return this.RenderCore<TPixel>(configuration, size, new RectangleF(0F, 0F, size.Width, size.Height), backgroundColor);
    }

    /// <summary>
    /// Returns the intrinsic size rounded up to whole pixels.
    /// </summary>
    /// <returns>The size in pixels.</returns>
    /// <exception cref="InvalidImageContentException">A side is not greater than zero, not finite, or larger than <see cref="int.MaxValue"/> pixels.</exception>
    internal Size GetPixelSize()
    {
        float width = this.Size.Width;
        float height = this.Size.Height;

        // The comparisons are false for NaN, so NaN is refused with the other values.
        if (!(width > 0F && width <= int.MaxValue) || !(height > 0F && height <= int.MaxValue))
        {
            throw new InvalidImageContentException(string.Create(
                CultureInfo.InvariantCulture,
                $"The SVG document has no usable image size: its intrinsic size is {width} x {height}."));
        }

        return new Size(ToPixels(width), ToPixels(height));

        static int ToPixels(float value) => (int)Math.Min(int.MaxValue, Math.Max(1D, Math.Ceiling(value - PixelSizeTolerance)));
    }

    /// <summary>
    /// Throws when an image of a size that the document determines is too large to render: it has more pixels than a
    /// limit, or a side longer than <see cref="MaxRenderSide"/>.
    /// </summary>
    /// <param name="size">The size of the image.</param>
    /// <param name="maxPixels">The largest number of pixels.</param>
    /// <exception cref="InvalidImageContentException">The image is too large.</exception>
    internal static void CheckRenderPixels(Size size, long maxPixels)
    {
        long pixels = (long)size.Width * size.Height;
        if (pixels > maxPixels || size.Width > MaxRenderSide || size.Height > MaxRenderSide)
        {
            throw new InvalidImageContentException(string.Create(
                CultureInfo.InvariantCulture,
                $"The SVG document renders to {size.Width} x {size.Height} pixels, more than the image pixel limit of {maxPixels} or longer than {MaxRenderSide} pixels on a side."));
        }
    }

    /// <summary>
    /// Sets the render limit of a document that a public entry point loaded.
    /// </summary>
    /// <param name="options">The load options.</param>
    /// <returns>This document.</returns>
    private SvgDocument WithLoadLimits(SvgLoadOptions options)
    {
        this.MaxRenderPixels = options.Limits.MaxImagePixels;
        return this;
    }

    /// <summary>
    /// Creates an image filled with the background and draws the document into a destination rectangle of it.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the image.</typeparam>
    /// <param name="configuration">The configuration.</param>
    /// <param name="size">The size of the image. Both sides are greater than zero.</param>
    /// <param name="destination">The rectangle that the document draws into.</param>
    /// <param name="backgroundColor">The background.</param>
    /// <returns>The image.</returns>
    private Image<TPixel> RenderCore<TPixel>(Configuration configuration, Size size, RectangleF destination, Color backgroundColor)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image<TPixel> image = new(configuration, size.Width, size.Height, backgroundColor.ToPixel<TPixel>());
        try
        {
            // Disposing the canvas replays the recorded drawing into the image.
            using DrawingCanvas canvas = image.Frames.RootFrame.CreateCanvas(configuration, new DrawingOptions());
            canvas.DrawSvg(this, destination);
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
    }
}
