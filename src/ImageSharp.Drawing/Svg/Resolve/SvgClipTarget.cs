// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The element that a <c>clipPath</c> or <c>mask</c> applies to, as the resolve stage needs it: the user space of the
/// element, its object bounding box, and the viewport that percentages refer to.
/// </summary>
internal readonly struct SvgClipTarget
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgClipTarget"/> struct.
    /// </summary>
    /// <param name="transform">The transform from the user space of the element to document user space.</param>
    /// <param name="viewport">The size of the viewport that percentages refer to.</param>
    /// <param name="box">The object bounding box in the user space of the element, or <see langword="null"/> when the element has none.</param>
    /// <param name="depth">The nesting depth of the element.</param>
    public SvgClipTarget(Matrix3x2 transform, SizeF viewport, RectangleF? box, int depth)
    {
        this.Transform = transform;
        this.Viewport = viewport;
        this.Box = box;
        this.Depth = depth;
    }

    /// <summary>
    /// Gets the transform from the user space of the element to document user space.
    /// </summary>
    public Matrix3x2 Transform { get; }

    /// <summary>
    /// Gets the size of the viewport that percentages refer to.
    /// </summary>
    public SizeF Viewport { get; }

    /// <summary>
    /// Gets the object bounding box in the user space of the element, or <see langword="null"/> when the element has none.
    /// </summary>
    public RectangleF? Box { get; }

    /// <summary>
    /// Gets the nesting depth of the element.
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// Returns the transform that maps the unit square to the object bounding box, for <c>objectBoundingBox</c> units.
    /// </summary>
    /// <param name="transform">When this method returns <see langword="true"/>, contains the transform.</param>
    /// <returns>
    /// <see langword="true"/> if the element has a finite bounding box with a positive width and height; otherwise,
    /// <see langword="false"/>, and <c>objectBoundingBox</c> units make the clip or mask render nothing.
    /// </returns>
    public bool TryGetBoxTransform(out Matrix3x2 transform)
    {
        if (this.Box is RectangleF box && box.Width > 0F && box.Height > 0F && float.IsFinite(box.Width) && float.IsFinite(box.Height))
        {
            transform = new Matrix3x2(box.Width, 0F, 0F, box.Height, box.X, box.Y);
            return true;
        }

        transform = default;
        return false;
    }
}
