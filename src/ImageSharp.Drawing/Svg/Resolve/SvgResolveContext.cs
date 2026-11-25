// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// What the resolve walk carries from an element to its children: the computed style, the transform to document user
/// space, the viewport size for percentages, and the nesting depth.
/// </summary>
internal readonly struct SvgResolveContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgResolveContext"/> struct.
    /// </summary>
    /// <param name="style">The computed style of the element.</param>
    /// <param name="transform">The transform from the user space of the element to document user space.</param>
    /// <param name="viewport">The size of the nearest viewport, which percentages refer to.</param>
    /// <param name="depth">The nesting depth, counted in the document and through <c>use</c> references.</param>
    public SvgResolveContext(in SvgComputedStyle style, Matrix3x2 transform, SizeF viewport, int depth)
    {
        this.Style = style;
        this.Transform = transform;
        this.Viewport = viewport;
        this.Depth = depth;
    }

    /// <summary>
    /// Gets the computed style of the element.
    /// </summary>
    public SvgComputedStyle Style { get; }

    /// <summary>
    /// Gets the transform from the user space of the element to document user space.
    /// </summary>
    public Matrix3x2 Transform { get; }

    /// <summary>
    /// Gets the size of the nearest viewport, which percentages refer to.
    /// </summary>
    public SizeF Viewport { get; }

    /// <summary>
    /// Gets the nesting depth, counted in the document and through <c>use</c> references.
    /// </summary>
    public int Depth { get; }
}
