// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The attributes of <c>pattern</c>. Every field records whether it was specified, so that the resolve stage can follow
/// the <c>href</c> chain as for gradients: an attribute that a pattern does not specify comes from the pattern it
/// references (see <see cref="InheritFrom(in SvgPatternAttributes)"/>). An unspecified length is
/// <c>default(SvgLength)</c> and an unspecified keyword is <c>Unspecified</c>.
/// </summary>
internal struct SvgPatternAttributes
{
    /// <summary>
    /// Gets or sets the id of the referenced pattern, without the leading <c>#</c>. <c>href</c> wins over
    /// <c>xlink:href</c>. It is <see langword="null"/> when no reference is specified or when the reference is not a
    /// same-document fragment.
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
    /// Gets or sets <c>width</c>. The default is 0, which disables the pattern. A negative value is kept, because SVG
    /// makes it an error that disables the pattern.
    /// </summary>
    public SvgLength Width { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>height</c>, with the same rules as <see cref="Width"/>.
    /// </summary>
    public SvgLength Height { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>patternUnits</c>, which applies to <see cref="X"/>, <see cref="Y"/>, <see cref="Width"/>, and
    /// <see cref="Height"/>. The default is <c>objectBoundingBox</c>.
    /// </summary>
    public SvgCoordinateUnits Units { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>patternContentUnits</c>. The default is <c>userSpaceOnUse</c>. A <c>viewBox</c> overrides it.
    /// </summary>
    public SvgCoordinateUnits ContentUnits { readonly get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>patternTransform</c> is specified and valid.
    /// </summary>
    public bool HasTransform { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>patternTransform</c> when <see cref="HasTransform"/> is <see langword="true"/>.
    /// </summary>
    public Matrix3x2 Transform { readonly get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>viewBox</c> is specified and valid.
    /// </summary>
    public bool HasViewBox { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>viewBox</c> rectangle when <see cref="HasViewBox"/> is <see langword="true"/>. Its width and
    /// height are never negative. A zero width or height disables the pattern.
    /// </summary>
    public RectangleF ViewBox { readonly get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>preserveAspectRatio</c> is specified and valid.
    /// </summary>
    public bool HasPreserveAspectRatio { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>preserveAspectRatio</c>. When <see cref="HasPreserveAspectRatio"/> is <see langword="false"/> it
    /// is the default, <c>xMidYMid meet</c>.
    /// </summary>
    public SvgPreserveAspectRatio PreserveAspectRatio { readonly get; set; }

    /// <summary>
    /// Fills every attribute that this pattern does not specify from the resolved attributes of the pattern it
    /// references, as the <c>href</c> chain of SVG requires. <see cref="Href"/> keeps its own value.
    /// </summary>
    /// <param name="referenced">The resolved attributes of the referenced pattern.</param>
    public void InheritFrom(in SvgPatternAttributes referenced)
    {
        this.X = this.X.IsSpecified ? this.X : referenced.X;
        this.Y = this.Y.IsSpecified ? this.Y : referenced.Y;
        this.Width = this.Width.IsSpecified ? this.Width : referenced.Width;
        this.Height = this.Height.IsSpecified ? this.Height : referenced.Height;
        this.Units = this.Units != SvgCoordinateUnits.Unspecified ? this.Units : referenced.Units;
        this.ContentUnits = this.ContentUnits != SvgCoordinateUnits.Unspecified ? this.ContentUnits : referenced.ContentUnits;

        if (!this.HasTransform && referenced.HasTransform)
        {
            this.HasTransform = true;
            this.Transform = referenced.Transform;
        }

        if (!this.HasViewBox && referenced.HasViewBox)
        {
            this.HasViewBox = true;
            this.ViewBox = referenced.ViewBox;
        }

        if (!this.HasPreserveAspectRatio && referenced.HasPreserveAspectRatio)
        {
            this.HasPreserveAspectRatio = true;
            this.PreserveAspectRatio = referenced.PreserveAspectRatio;
        }
    }
}
