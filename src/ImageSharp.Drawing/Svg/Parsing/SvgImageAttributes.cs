// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The attributes of <c>image</c>. An unspecified or invalid length is <c>default(SvgLength)</c>.
/// </summary>
internal struct SvgImageAttributes
{
    /// <summary>
    /// Gets or sets the image URL as written, with surrounding whitespace removed: a <c>data:</c> URI or a file reference.
    /// <c>href</c> wins over <c>xlink:href</c>. It is <see langword="null"/> when neither is specified. The reader does not
    /// interpret or load it.
    /// </summary>
    public string? Href { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>x</c>. The default is 0.
    /// </summary>
    public SvgLength X { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>y</c>. The default is 0.
    /// </summary>
    public SvgLength Y { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>width</c>. Unspecified means <c>auto</c>: the keyword <c>auto</c>, a missing attribute, and an
    /// invalid value all take the size from the intrinsic size of the image. A negative value is kept, because SVG makes
    /// it an error.
    /// </summary>
    public SvgLength Width { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>height</c>, with the same rules as <see cref="Width"/>.
    /// </summary>
    public SvgLength Height { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>preserveAspectRatio</c>. The default is <c>xMidYMid meet</c>.
    /// </summary>
    public SvgPreserveAspectRatio PreserveAspectRatio { readonly get; set; }
}
