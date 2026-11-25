// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The geometry attributes of <c>rect</c>. An unspecified or invalid attribute is <c>default(SvgLength)</c>.
/// </summary>
internal struct SvgRectAttributes
{
    /// <summary>
    /// Gets or sets <c>x</c>.
    /// </summary>
    public SvgLength X { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>y</c>.
    /// </summary>
    public SvgLength Y { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>width</c>. Never negative.
    /// </summary>
    public SvgLength Width { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>height</c>. Never negative.
    /// </summary>
    public SvgLength Height { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>rx</c>. Never negative; a negative value is an error and leaves the attribute unspecified, which
    /// means <c>auto</c>.
    /// </summary>
    public SvgLength Rx { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>ry</c>. Never negative; a negative value is an error and leaves the attribute unspecified, which
    /// means <c>auto</c>.
    /// </summary>
    public SvgLength Ry { readonly get; set; }
}
