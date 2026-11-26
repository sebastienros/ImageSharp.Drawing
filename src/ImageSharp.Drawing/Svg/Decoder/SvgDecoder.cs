// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Decodes an SVG document, uncompressed or gzip-compressed (<c>.svgz</c>), by rendering it into a new image.
/// </summary>
/// <remarks>
/// <para>
/// The document loads with <see cref="SvgDecoderOptions.LoadOptions"/>, as <see cref="SvgDocument.Load(Stream, SvgLoadOptions)"/>
/// loads it, and renders with <see cref="SvgDocument.Render{TPixel}(Configuration, Size, Color)"/> over
/// <see cref="SvgDecoderOptions.BackgroundColor"/>. Text uses the fonts of the load options (by default the system fonts).
/// Image references follow the load options; the decoder never sets <see cref="SvgLoadOptions.ResourceBaseDirectory"/>,
/// so by default no other file is read, also when the data comes from a file.
/// </para>
/// <para>
/// The image has the intrinsic size of the document (<see cref="SvgDocument.Size"/>) rounded up to whole pixels. A
/// document without <c>width</c>, <c>height</c>, and <c>viewBox</c> has the size 300 x 150. With
/// <see cref="DecoderOptions.TargetSize"/> the size follows the rule of the ImageSharp decoders: when both sides differ
/// from the target, the image takes the largest size that fits inside the target with the aspect ratio of the intrinsic
/// size kept (<see cref="ResizeMode.Max"/>); otherwise it keeps the intrinsic size. The vector content renders directly at
/// that size; it is not rendered at the intrinsic size and resampled, so <see cref="DecoderOptions.Sampler"/> has no
/// effect. <c>Identify</c> loads the document but does not render it, and returns the intrinsic size and the resolution
/// of the load options.
/// </para>
/// <para>
/// Limits: the image size is checked before any pixel memory is allocated, and an image of more than
/// <see cref="SvgLimits.MaxImagePixels"/> pixels, or with a side longer than 65,535 pixels, throws
/// <see cref="InvalidImageContentException"/>, whether the size is the intrinsic size or fits a target size. Gzip data inflates into memory up to 64 MiB (67,108,864 bytes); data that
/// inflates to more throws <see cref="InvalidImageContentException"/> when the limit is reached. The other limits of
/// <see cref="SvgLoadOptions.Limits"/> apply as when the document loads and draws.
/// </para>
/// <para>
/// Exceptions: data that is not a valid SVG document (malformed XML, a root element that is not <c>svg</c> in the SVG
/// namespace, corrupt or truncated gzip data), a document that exceeds a limit, and a document whose intrinsic size is
/// not a usable image size (a side that is zero, not finite, or more than <see cref="int.MaxValue"/> pixels) throw
/// <see cref="InvalidImageContentException"/>. When the format is detected from the data and no registered detector
/// matches, ImageSharp throws <see cref="UnknownImageFormatException"/>. The cancellation token is checked before the
/// document loads and again between loading and rendering.
/// </para>
/// </remarks>
public sealed class SvgDecoder : SpecializedImageDecoder<SvgDecoderOptions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgDecoder"/> class.
    /// </summary>
    private SvgDecoder()
    {
    }

    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static SvgDecoder Instance { get; } = new();

    /// <summary>
    /// Loads the document and returns its intrinsic size, rounded up to whole pixels, and the resolution of the default
    /// load options. The document does not render.
    /// </summary>
    /// <param name="options">The general decoder options.</param>
    /// <param name="stream">The data, readable and seekable.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The image information.</returns>
    /// <exception cref="InvalidImageContentException">The data is not a valid SVG document, exceeds a limit, or has no usable image size.</exception>
    protected override ImageInfo Identify(DecoderOptions options, Stream stream, CancellationToken cancellationToken)
    {
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(stream, nameof(stream));

        SvgLoadOptions loadOptions = new();
        SvgDocument document = Load(stream, loadOptions, cancellationToken);
        return new ImageInfo(document.GetPixelSize(), CreateMetadata(loadOptions));
    }

    /// <summary>
    /// Loads the document and renders it into a new image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the image.</typeparam>
    /// <param name="options">The decoder options.</param>
    /// <param name="stream">The data, readable and seekable.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The image.</returns>
    /// <exception cref="InvalidImageContentException">The data is not a valid SVG document, exceeds a limit, or has no usable image size.</exception>
    protected override Image<TPixel> Decode<TPixel>(SvgDecoderOptions options, Stream stream, CancellationToken cancellationToken)
    {
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(stream, nameof(stream));

        SvgLoadOptions loadOptions = options.LoadOptions;
        DecoderOptions general = options.GeneralOptions;
        SvgDocument document = Load(stream, loadOptions, cancellationToken);

        // The size is known and checked before the image allocates its pixels.
        Size intrinsic = document.GetPixelSize();
        Size size = GetOutputSize(general.TargetSize, intrinsic);
        SvgDocument.CheckRenderPixels(size, loadOptions.Limits.MaxImagePixels);
        cancellationToken.ThrowIfCancellationRequested();

        // At the intrinsic size the document draws at its exact size from the origin, as DrawSvg draws it; at a target
        // size its view box fits the whole image.
        Image<TPixel> image = size == intrinsic
            ? document.Render<TPixel>(general.Configuration, options.BackgroundColor)
            : document.Render<TPixel>(general.Configuration, size, options.BackgroundColor);

        SetResolution(image.Metadata, loadOptions.Dpi);
        return image;
    }

    /// <summary>
    /// Loads the document and renders it into a new image with <see cref="Rgba32"/> pixels.
    /// </summary>
    /// <param name="options">The decoder options.</param>
    /// <param name="stream">The data, readable and seekable.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The image.</returns>
    /// <exception cref="InvalidImageContentException">The data is not a valid SVG document, exceeds a limit, or has no usable image size.</exception>
    protected override Image Decode(SvgDecoderOptions options, Stream stream, CancellationToken cancellationToken)
        => this.Decode<Rgba32>(options, stream, cancellationToken);

    /// <summary>
    /// Creates the default decoder options for general options.
    /// </summary>
    /// <param name="options">The general decoder options.</param>
    /// <returns>The decoder options, with default load options and a transparent background.</returns>
    protected override SvgDecoderOptions CreateDefaultSpecializedOptions(DecoderOptions options)
        => new() { GeneralOptions = options };

    /// <summary>
    /// Loads a document from uncompressed or gzip data.
    /// </summary>
    /// <param name="stream">The data, readable and seekable, at the start of the document.</param>
    /// <param name="options">The load options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidImageContentException">The data is not a valid SVG document or exceeds a limit.</exception>
    private static SvgDocument Load(Stream stream, SvgLoadOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The base class passes a seekable stream: a stream that cannot seek is copied into memory first.
        long start = stream.Position;
        Span<byte> magic = stackalloc byte[2];
        int read = stream.ReadAtLeast(magic, magic.Length, false);
        stream.Position = start;

        SvgDocument document;
        if (read == 2 && magic[0] == 0x1F && magic[1] == 0x8B)
        {
            // The same bounded inflation as for svgz resources of image elements.
            MemoryStream inflated;
            try
            {
                inflated = SvgResolver.Decompress(stream);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidImageContentException($"The compressed SVG document cannot be read: {exception.Message}", exception);
            }

            using (inflated)
            {
                document = SvgDocument.Load(inflated, options);
            }
        }
        else
        {
            document = SvgDocument.Load(stream, options);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return document;
    }

    /// <summary>
    /// Returns the size of the decoded image, following the target size rule of <c>ImageDecoder.ScaleToTargetSize</c>.
    /// </summary>
    /// <remarks>
    /// The ImageSharp decoders resize a decoded image only when both of its sides differ from the target, and then with
    /// <see cref="ResizeMode.Max"/>. <see cref="ResizeProcessor"/> computes the same size from the same options, so an
    /// SVG document gets the size that a raster image of its intrinsic size would get.
    /// </remarks>
    /// <param name="targetSize">The target size, or <see langword="null"/>.</param>
    /// <param name="intrinsic">The intrinsic size in pixels.</param>
    /// <returns>The size of the image.</returns>
    private static Size GetOutputSize(Size? targetSize, Size intrinsic)
    {
        if (targetSize is not Size target || intrinsic.Width == target.Width || intrinsic.Height == target.Height)
        {
            return intrinsic;
        }

        ResizeProcessor fit = new(new ResizeOptions { Size = target, Mode = ResizeMode.Max }, intrinsic);
        return new Size(fit.DestinationWidth, fit.DestinationHeight);
    }

    /// <summary>
    /// Creates the metadata of a document: the resolution of the load options.
    /// </summary>
    /// <param name="options">The load options.</param>
    /// <returns>The metadata.</returns>
    private static ImageMetadata CreateMetadata(SvgLoadOptions options)
    {
        ImageMetadata metadata = new();
        SetResolution(metadata, options.Dpi);
        return metadata;
    }

    /// <summary>
    /// Sets the resolution of image metadata, in pixels per inch.
    /// </summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="dpi">The resolution.</param>
    private static void SetResolution(ImageMetadata metadata, float dpi)
    {
        metadata.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;
        metadata.HorizontalResolution = dpi;
        metadata.VerticalResolution = dpi;
    }
}
