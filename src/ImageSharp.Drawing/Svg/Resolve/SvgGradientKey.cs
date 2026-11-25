// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The inputs that a gradient brush depends on, used to share one brush between elements that paint with the same
/// gradient in the same geometry.
/// </summary>
internal readonly struct SvgGradientKey : IEquatable<SvgGradientKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgGradientKey"/> struct.
    /// </summary>
    /// <param name="element">The index of the gradient element.</param>
    /// <param name="box">
    /// The bounding box of the painted element for <c>objectBoundingBox</c> units, or the viewport rectangle at the origin
    /// for <c>userSpaceOnUse</c> units.
    /// </param>
    /// <param name="fontSize">The computed font size that <c>em</c> and <c>ex</c> refer to.</param>
    /// <param name="opacity">The opacity that multiplies the stop colors.</param>
    /// <param name="transformOrigin">
    /// The resolved <c>transform-origin</c> of the gradient transform. It depends on the viewport, which the box does not
    /// hold for <c>objectBoundingBox</c> units.
    /// </param>
    public SvgGradientKey(int element, RectangleF box, float fontSize, float opacity, Vector2 transformOrigin)
    {
        this.Element = element;
        this.Box = box;
        this.FontSize = fontSize;
        this.Opacity = opacity;
        this.TransformOrigin = transformOrigin;
    }

    /// <summary>
    /// Gets the index of the gradient element.
    /// </summary>
    public int Element { get; }

    /// <summary>
    /// Gets the bounding box or viewport rectangle that the gradient geometry refers to.
    /// </summary>
    public RectangleF Box { get; }

    /// <summary>
    /// Gets the computed font size that <c>em</c> and <c>ex</c> refer to.
    /// </summary>
    public float FontSize { get; }

    /// <summary>
    /// Gets the opacity that multiplies the stop colors.
    /// </summary>
    public float Opacity { get; }

    /// <summary>
    /// Gets the resolved <c>transform-origin</c> of the gradient transform.
    /// </summary>
    public Vector2 TransformOrigin { get; }

    /// <inheritdoc/>
    public bool Equals(SvgGradientKey other)
        => this.Element == other.Element
        && this.Box == other.Box
        && this.FontSize == other.FontSize
        && this.Opacity == other.Opacity
        && this.TransformOrigin == other.TransformOrigin;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SvgGradientKey other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Element, this.Box, this.FontSize, this.Opacity, this.TransformOrigin);
}
