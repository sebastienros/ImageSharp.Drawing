// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The geometry attributes of <c>svg</c> and <c>symbol</c>. An unspecified or invalid attribute has its default value.
/// </summary>
internal struct SvgViewportAttributes
{
    /// <summary>
    /// Gets or sets <c>x</c>. Ignored on the root element.
    /// </summary>
    public SvgLength X { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>y</c>. Ignored on the root element.
    /// </summary>
    public SvgLength Y { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>width</c>. Never negative; unspecified means <c>auto</c>, which is 100%.
    /// </summary>
    public SvgLength Width { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>height</c>. Never negative; unspecified means <c>auto</c>, which is 100%.
    /// </summary>
    public SvgLength Height { readonly get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>viewBox</c> is specified and valid.
    /// </summary>
    public bool HasViewBox { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>viewBox</c> rectangle when <see cref="HasViewBox"/> is <see langword="true"/>. Its width and
    /// height are never negative. A zero width or height disables rendering of the element.
    /// </summary>
    public RectangleF ViewBox { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>preserveAspectRatio</c>. The default is <c>xMidYMid meet</c>.
    /// </summary>
    public SvgPreserveAspectRatio PreserveAspectRatio { readonly get; set; }
}
