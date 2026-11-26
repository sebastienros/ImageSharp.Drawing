// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A range of values in one of the list tables of <see cref="SvgElementTree"/>: the lengths of the <c>x</c>, <c>y</c>,
/// <c>dx</c>, and <c>dy</c> attributes, or the angles of <c>rotate</c>. The default value is the empty range of an
/// attribute that is missing or invalid.
/// </summary>
internal readonly struct SvgListRange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgListRange"/> struct.
    /// </summary>
    /// <param name="start">The index of the first value in the table.</param>
    /// <param name="count">The number of values.</param>
    public SvgListRange(int start, int count)
    {
        this.Start = start;
        this.Count = count;
    }

    /// <summary>
    /// Gets the index of the first value in the table.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the number of values.
    /// </summary>
    public int Count { get; }
}
