// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The attributes of <c>linearGradient</c> and <c>radialGradient</c>. One struct serves both kinds, so that the resolve
/// stage can follow an <c>href</c> chain across kinds field by field: an attribute that is not specified here comes from
/// the referenced gradient. Unspecified lengths are <c>default(SvgLength)</c> and unspecified keywords are
/// <c>Unspecified</c>.
/// </summary>
internal struct SvgGradientAttributes
{
    /// <summary>
    /// Gets or sets the id of the referenced gradient, without the leading <c>#</c>. <c>href</c> wins over
    /// <c>xlink:href</c>. It is <see langword="null"/> when no reference is specified or when the reference is not a
    /// same-document fragment.
    /// </summary>
    public string? Href { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>x1</c> of a linear gradient. The default is 0%.
    /// </summary>
    public SvgLength X1 { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>y1</c> of a linear gradient. The default is 0%.
    /// </summary>
    public SvgLength Y1 { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>x2</c> of a linear gradient. The default is 100%.
    /// </summary>
    public SvgLength X2 { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>y2</c> of a linear gradient. The default is 0%.
    /// </summary>
    public SvgLength Y2 { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>cx</c> of a radial gradient. The default is 50%.
    /// </summary>
    public SvgLength Cx { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>cy</c> of a radial gradient. The default is 50%.
    /// </summary>
    public SvgLength Cy { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>r</c> of a radial gradient. Never negative. The default is 50%.
    /// </summary>
    public SvgLength R { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>fx</c> of a radial gradient. The default is the computed <c>cx</c>.
    /// </summary>
    public SvgLength Fx { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>fy</c> of a radial gradient. The default is the computed <c>cy</c>.
    /// </summary>
    public SvgLength Fy { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>fr</c> of a radial gradient. Never negative. The default is 0%.
    /// </summary>
    public SvgLength Fr { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>gradientUnits</c>.
    /// </summary>
    public SvgCoordinateUnits Units { readonly get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>gradientTransform</c> is specified and valid.
    /// </summary>
    public bool HasTransform { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>gradientTransform</c> when <see cref="HasTransform"/> is <see langword="true"/>.
    /// </summary>
    public Matrix3x2 Transform { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>spreadMethod</c>.
    /// </summary>
    public SvgSpreadMethod SpreadMethod { readonly get; set; }
}
