// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Options for <see cref="SvgDecoder"/>.
/// </summary>
public sealed class SvgDecoderOptions : ISpecializedDecoderOptions
{
    private readonly SvgLoadOptions loadOptions = new();

    /// <summary>
    /// Gets the general decoder options. <see cref="DecoderOptions.TargetSize"/> selects the size that the document
    /// renders at, and <see cref="DecoderOptions.Configuration"/> supplies the memory allocator and the parallelism of
    /// the render. <see cref="DecoderOptions.Sampler"/> has no effect: the document renders at the output size and is not
    /// resampled.
    /// </summary>
    public DecoderOptions GeneralOptions { get; init; } = new();

    /// <summary>
    /// Gets the options that the document loads with: resolution, fonts, limits, and the directory of referenced image
    /// files. The decoder never changes them; in particular it never sets <see cref="SvgLoadOptions.ResourceBaseDirectory"/>,
    /// not even when the data comes from a file, so by default no other file is read. The default is a new instance with
    /// default values.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public SvgLoadOptions LoadOptions
    {
        get => this.loadOptions;
        init
        {
            Guard.NotNull(value, nameof(this.LoadOptions));
            this.loadOptions = value;
        }
    }

    /// <summary>
    /// Gets the color that fills the image before the document renders over it. The default is
    /// <see cref="Color.Transparent"/>. A pixel format without an alpha channel stores a transparent background as black,
    /// so choose an opaque color for such formats.
    /// </summary>
    public Color BackgroundColor { get; init; } = Color.Transparent;
}
