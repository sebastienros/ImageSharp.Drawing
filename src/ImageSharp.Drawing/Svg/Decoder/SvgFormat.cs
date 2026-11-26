// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The SVG image format: static SVG documents, uncompressed (<c>.svg</c>) or gzip-compressed (<c>.svgz</c>).
/// </summary>
/// <remarks>
/// The format has a decoder (<see cref="SvgDecoder"/>) and no encoder. It is not part of
/// <see cref="Configuration.Default"/>; register it with <see cref="SvgConfigurationModule"/>.
/// </remarks>
public sealed class SvgFormat : IImageFormat
{
    /// <summary>
    /// The MIME types of the format.
    /// </summary>
    private static readonly string[] MimeTypeList = ["image/svg+xml"];

    /// <summary>
    /// The file extensions of the format.
    /// </summary>
    private static readonly string[] FileExtensionList = ["svg", "svgz"];

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFormat"/> class.
    /// </summary>
    private SvgFormat()
    {
    }

    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static SvgFormat Instance { get; } = new();

    /// <summary>
    /// Gets the name of the format: <c>SVG</c>.
    /// </summary>
    public string Name => "SVG";

    /// <summary>
    /// Gets the default MIME type of the format: <c>image/svg+xml</c>.
    /// </summary>
    public string DefaultMimeType => "image/svg+xml";

    /// <summary>
    /// Gets the MIME types of the format: <c>image/svg+xml</c>.
    /// </summary>
    public IEnumerable<string> MimeTypes => MimeTypeList;

    /// <summary>
    /// Gets the file extensions of the format: <c>svg</c> and <c>svgz</c>.
    /// </summary>
    public IEnumerable<string> FileExtensions => FileExtensionList;
}
