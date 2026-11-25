// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>points</c> attribute of <c>polyline</c> and <c>polygon</c>: a range in the point table of the element tree.
/// Read the points with <see cref="SvgElementTree.GetPoints(int)"/>.
/// </summary>
internal readonly struct SvgPointsAttributes
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgPointsAttributes"/> struct.
    /// </summary>
    /// <param name="start">The index of the first point in the point table.</param>
    /// <param name="count">The number of points.</param>
    /// <param name="isComplete">Whether the attribute parsed without error.</param>
    public SvgPointsAttributes(int start, int count, bool isComplete)
    {
        this.Start = start;
        this.Count = count;
        this.IsComplete = isComplete;
    }

    /// <summary>
    /// Gets the index of the first point in the point table.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the number of points. When the attribute has an error, the points are the pairs before the first error and a
    /// final odd number is dropped.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets a value indicating whether <c>points</c> parsed without error.
    /// </summary>
    public bool IsComplete { get; }
}
