// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// What the resolve stage must close after the content of a filtered element resolved. <c>default</c> is an inactive
/// scope: the element has no filter that applies.
/// </summary>
internal readonly struct SvgFilterScope
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterScope"/> struct.
    /// </summary>
    /// <param name="start">The index of the first content node.</param>
    /// <param name="graph">The resolved filter.</param>
    /// <param name="transform">The transform from the user space of the element to document user space.</param>
    /// <param name="opacity">The opacity that multiplies the filter result.</param>
    public SvgFilterScope(int start, SvgFilterGraph graph, Matrix3x2 transform, float opacity)
    {
        this.Start = start;
        this.Graph = graph;
        this.Transform = transform;
        this.Opacity = opacity;
    }

    /// <summary>
    /// Gets the index of the first content node.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the resolved filter, or <see langword="null"/> for an inactive scope.
    /// </summary>
    public SvgFilterGraph? Graph { get; }

    /// <summary>
    /// Gets the transform from the user space of the element to document user space.
    /// </summary>
    public Matrix3x2 Transform { get; }

    /// <summary>
    /// Gets the opacity that multiplies the filter result.
    /// </summary>
    public float Opacity { get; }
}
