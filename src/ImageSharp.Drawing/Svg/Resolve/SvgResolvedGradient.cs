// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A gradient element after its <c>href</c> chain is followed: every attribute that the element does not specify comes
/// from the first gradient in the chain that does, and the stops come from the first gradient in the chain that has any.
/// </summary>
internal sealed class SvgResolvedGradient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgResolvedGradient"/> class.
    /// </summary>
    /// <param name="isRadial">Whether the element is a <c>radialGradient</c>.</param>
    /// <param name="attributes">The merged attributes.</param>
    /// <param name="stops">The stops, with <c>stop-opacity</c> applied and offsets that never decrease.</param>
    /// <param name="transformOriginX">The computed horizontal <c>transform-origin</c> of the element that supplies the <c>gradientTransform</c>.</param>
    /// <param name="transformOriginY">The computed vertical <c>transform-origin</c> of the element that supplies the <c>gradientTransform</c>.</param>
    public SvgResolvedGradient(bool isRadial, SvgGradientAttributes attributes, ColorStop[] stops, SvgLength transformOriginX, SvgLength transformOriginY)
    {
        this.IsRadial = isRadial;
        this.Attributes = attributes;
        this.Stops = stops;
        this.TransformOriginX = transformOriginX;
        this.TransformOriginY = transformOriginY;
    }

    /// <summary>
    /// Gets the computed horizontal <c>transform-origin</c> of the gradient element whose <c>gradientTransform</c> the
    /// merged attributes hold, in pixels or as a percentage. Unspecified means 0.
    /// </summary>
    public SvgLength TransformOriginX { get; }

    /// <summary>
    /// Gets the computed vertical <c>transform-origin</c>, with the same rules as <see cref="TransformOriginX"/>.
    /// </summary>
    public SvgLength TransformOriginY { get; }

    /// <summary>
    /// Gets a value indicating whether the element is a <c>radialGradient</c>. The kind comes from the element itself, not
    /// from the elements it references.
    /// </summary>
    public bool IsRadial { get; }

    /// <summary>
    /// Gets the merged attributes. Attributes that no gradient in the chain specifies are unspecified.
    /// </summary>
    public SvgGradientAttributes Attributes { get; }

    /// <summary>
    /// Gets the stops. An empty array means that the gradient paints nothing.
    /// </summary>
    public ColorStop[] Stops { get; }
}
