// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A specified <c>stroke-dasharray</c> value: a range in the dash length table of the element tree. Read the lengths with
/// <see cref="SvgElementTree.GetDashArray(SvgDashArray)"/>.
/// </summary>
internal readonly struct SvgDashArray
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgDashArray"/> struct.
    /// </summary>
    /// <param name="start">The index of the first length in the dash length table.</param>
    /// <param name="count">The number of lengths; zero for <c>none</c>.</param>
    public SvgDashArray(int start, int count)
    {
        this.Start = start;
        this.Count = count;
    }

    /// <summary>
    /// Gets the index of the first length in the dash length table.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the number of lengths. The parser rejects an empty list, so zero means <c>none</c>.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets a value indicating whether the value is <c>none</c>.
    /// </summary>
    public bool IsNone => this.Count == 0;
}
