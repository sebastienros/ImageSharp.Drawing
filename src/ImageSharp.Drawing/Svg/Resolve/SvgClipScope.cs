// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A clip that <see cref="SvgResolver"/> opened around content that draws clip coverage, so that the end of the content
/// can close it. <c>default</c> is no clip.
/// </summary>
internal struct SvgClipScope
{
    /// <summary>
    /// Gets or sets how the clip applies.
    /// </summary>
    public SvgClipKind Kind { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>clipPath</c> element.
    /// </summary>
    public int Clip { readonly get; set; }

    /// <summary>
    /// Gets or sets the element that the clip applies to.
    /// </summary>
    public SvgClipTarget Target { readonly get; set; }

    /// <summary>
    /// Gets or sets, for <see cref="SvgClipKind.Geometry"/>, the number of clip nodes opened; for
    /// <see cref="SvgClipKind.Coverage"/>, the index of the placeholder node that becomes the layer.
    /// </summary>
    public int Value { readonly get; set; }
}
