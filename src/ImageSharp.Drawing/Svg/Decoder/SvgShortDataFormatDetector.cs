// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Formats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Detects SVG documents that are shorter than <see cref="SvgImageFormatDetector.HeaderSize"/>.
/// </summary>
/// <remarks>
/// ImageSharp reads a header of the largest header size of the registered detectors, or the whole data when it is
/// shorter, and calls only the detectors whose header size fits. A minified icon can be shorter than the 512 bytes of
/// <see cref="SvgImageFormatDetector"/>, which would then never see it. This detector has the header size of the
/// shortest data that can start an SVG document (<c>&lt;svg&gt;</c>), and it checks only data that the main detector does
/// not get, with the same rules.
/// </remarks>
internal sealed class SvgShortDataFormatDetector : IImageFormatDetector
{
    /// <summary>
    /// Gets the header size: 5 bytes, the length of <c>&lt;svg&gt;</c>.
    /// </summary>
    public int HeaderSize => 5;

    /// <summary>
    /// Detects whether a header that is shorter than <see cref="SvgImageFormatDetector.HeaderSize"/> is the start of an
    /// SVG document.
    /// </summary>
    /// <param name="header">The first bytes of the data.</param>
    /// <param name="format">When this method returns <see langword="true"/>, contains <see cref="SvgFormat.Instance"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the header is short and the start of an SVG document; otherwise, <see langword="false"/>.</returns>
    public bool TryDetectFormat(ReadOnlySpan<byte> header, [NotNullWhen(true)] out IImageFormat? format)
    {
        format = header.Length < SvgImageFormatDetector.WindowSize && SvgImageFormatDetector.IsSvg(header) ? SvgFormat.Instance : null;
        return format is not null;
    }
}
