// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The geometry attributes of <c>circle</c> and <c>ellipse</c>. An unspecified or invalid attribute is
/// <c>default(SvgLength)</c>.
/// </summary>
internal struct SvgCircleAttributes
{
    /// <summary>
    /// Gets or sets <c>cx</c>.
    /// </summary>
    public SvgLength Cx { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>cy</c>.
    /// </summary>
    public SvgLength Cy { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>r</c> of a circle. Never negative. Unused for an ellipse.
    /// </summary>
    public SvgLength R { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>rx</c> of an ellipse. Never negative. Unused for a circle.
    /// </summary>
    public SvgLength Rx { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>ry</c> of an ellipse. Never negative. Unused for a circle.
    /// </summary>
    public SvgLength Ry { readonly get; set; }
}
