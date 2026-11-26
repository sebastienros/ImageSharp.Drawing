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
    /// <param name="graphs">The resolved filters of the filter list, in order; at least one.</param>
    /// <param name="growth">The growth of each filter function entry; see <see cref="Growth"/>.</param>
    /// <param name="transform">The transform from the user space of the element to document user space.</param>
    /// <param name="opacity">The opacity that multiplies the filter result.</param>
    public SvgFilterScope(int start, SvgFilterGraph[] graphs, Vector4[] growth, Matrix3x2 transform, float opacity)
    {
        this.Start = start;
        this.Graphs = graphs;
        this.Growth = growth;
        this.Transform = transform;
        this.Opacity = opacity;
    }

    /// <summary>
    /// Gets the index of the first content node.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the resolved filters of the filter list in the order in which they apply, or <see langword="null"/> for an
    /// inactive scope. The graph of a <c>url()</c> entry is final. The graph of a filter function has an empty region,
    /// because its region grows from what the entries before it can paint, which is known only once the content has
    /// resolved; the end of the scope replaces it.
    /// </summary>
    public SvgFilterGraph[]? Graphs { get; }

    /// <summary>
    /// Gets, per entry of <see cref="Graphs"/>, how far a filter function paints beyond its input, in user units: the
    /// result can paint the input area and the input area moved by (X, Y) and widened by Z horizontally and W vertically
    /// on each side. Zero for a color function and unused for a <c>url()</c> entry.
    /// </summary>
    public Vector4[]? Growth { get; }

    /// <summary>
    /// Gets the transform from the user space of the element to document user space.
    /// </summary>
    public Matrix3x2 Transform { get; }

    /// <summary>
    /// Gets the opacity that multiplies the filter result.
    /// </summary>
    public float Opacity { get; }
}
