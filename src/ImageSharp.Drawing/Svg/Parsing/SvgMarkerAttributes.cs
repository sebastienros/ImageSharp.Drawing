// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The attributes of <c>marker</c>. An unspecified or invalid length is <c>default(SvgLength)</c> and an unspecified
/// keyword is <c>Unspecified</c>; the consumer applies the defaults.
/// </summary>
internal struct SvgMarkerAttributes
{
    /// <summary>
    /// Gets or sets <c>refX</c>, in the coordinate system of the marker content after the view box. The default is 0.
    /// </summary>
    public SvgLength RefX { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>refY</c>, in the coordinate system of the marker content after the view box. The default is 0.
    /// </summary>
    public SvgLength RefY { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>markerWidth</c>. The default is 3. A negative value is kept, because SVG makes it an error that
    /// disables the marker, which differs from the default; zero also disables the marker.
    /// </summary>
    public SvgLength MarkerWidth { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>markerHeight</c>, with the same rules as <see cref="MarkerWidth"/>.
    /// </summary>
    public SvgLength MarkerHeight { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>markerUnits</c>. The default is <c>strokeWidth</c>.
    /// </summary>
    public SvgMarkerUnits Units { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>orient</c>. The default is the angle 0.
    /// </summary>
    public SvgMarkerOrient Orient { readonly get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>viewBox</c> is specified and valid.
    /// </summary>
    public bool HasViewBox { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>viewBox</c> rectangle when <see cref="HasViewBox"/> is <see langword="true"/>. Its width and
    /// height are never negative. A zero width or height disables the marker.
    /// </summary>
    public RectangleF ViewBox { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>preserveAspectRatio</c>. The default is <c>xMidYMid meet</c>.
    /// </summary>
    public SvgPreserveAspectRatio PreserveAspectRatio { readonly get; set; }
}
