// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// What <see cref="SvgResolver"/> opened around the content of one element for clipping, masking, blending, and
/// isolation, so that the end of the element can close it. <c>default</c> is an element without any of these.
/// </summary>
internal struct SvgCompositeScope
{
    /// <summary>
    /// Gets or sets a value indicating whether the element opened anything.
    /// </summary>
    public bool IsActive { readonly get; set; }

    /// <summary>
    /// Gets or sets the index of the first geometry clip node of the element, which is also the node count before the
    /// element added any node.
    /// </summary>
    public int ClipStart { readonly get; set; }

    /// <summary>
    /// Gets or sets the number of geometry clip nodes that the element opened.
    /// </summary>
    public int ClipCount { readonly get; set; }

    /// <summary>
    /// Gets or sets the index of the placeholder node that becomes the layer of the element, or -1 when the element
    /// needs no layer.
    /// </summary>
    public int Layer { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>clipPath</c> element that clips the element through an alpha mask, or -1.
    /// </summary>
    public int CoverageClip { readonly get; set; }

    /// <summary>
    /// Gets or sets the outline of a CSS basic shape that clips the element through an alpha mask, with the
    /// <c>evenodd</c> rule, or <see langword="null"/>.
    /// </summary>
    public IPath? CoverageShape { readonly get; set; }

    /// <summary>
    /// Gets or sets the transform from the coordinates of <see cref="CoverageShape"/> to document user space.
    /// </summary>
    public Matrix3x2 CoverageShapeTransform { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>mask</c> element, or -1.
    /// </summary>
    public int Mask { readonly get; set; }

    /// <summary>
    /// Gets or sets the mask region in the user space of the element.
    /// </summary>
    public RectangleF MaskRegion { readonly get; set; }

    /// <summary>
    /// Gets or sets the opacity that the layer composites with.
    /// </summary>
    public float Opacity { readonly get; set; }

    /// <summary>
    /// Gets or sets the blend mode that the layer composites with.
    /// </summary>
    public SvgBlendMode BlendMode { readonly get; set; }

    /// <summary>
    /// Gets or sets the element that clips and masks apply to.
    /// </summary>
    public SvgClipTarget Target { readonly get; set; }
}
