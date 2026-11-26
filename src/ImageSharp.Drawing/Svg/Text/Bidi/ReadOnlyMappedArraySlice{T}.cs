// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Provides a readonly mapped view of an underlying slice, selecting arbitrary indices
/// from the source array.
/// </summary>
/// <typeparam name="T">The type of item contained in the underlying array.</typeparam>
/// <remarks>
/// <para>
/// Temporary copy of <c>src/SixLabors.Fonts/ReadOnlyMappedArraySlice{T}.cs</c> of SixLabors.Fonts
/// (https://github.com/SixLabors/Fonts), tag <c>v3.2.1</c> (commit 50dae68), where the type is internal. SVG text
/// layout needs the embedding levels of the Unicode bidirectional algorithm, and SixLabors.Fonts does not expose them.
/// Remove this copy when SixLabors.Fonts exposes the algorithm publicly. The code stays as close to the source as it
/// compiles, so that it can be compared with upstream. Edits: the namespace and these remarks.
/// </para>
/// </remarks>
internal readonly struct ReadonlyMappedArraySlice<T>
    where T : struct
{
    private readonly ReadOnlyArraySlice<T> data;
    private readonly ReadOnlyArraySlice<int> map;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadonlyMappedArraySlice{T}"/> struct.
    /// </summary>
    /// <param name="data">The data slice.</param>
    /// <param name="map">The map slice.</param>
    public ReadonlyMappedArraySlice(in ReadOnlyArraySlice<T> data, in ReadOnlyArraySlice<int> map)
    {
        Guard.MustBeGreaterThanOrEqualTo(data.Length, map.Length, nameof(map));

        this.data = data;
        this.map = map;
    }

    /// <summary>
    /// Gets the number of items in the map.
    /// </summary>
    public int Length => this.map.Length;

    /// <summary>
    /// Returns a reference to specified element of the slice.
    /// </summary>
    /// <param name="index">The index of the element to return.</param>
    /// <returns>The <typeparamref name="T"/>.</returns>
    /// <exception cref="IndexOutOfRangeException">
    /// Thrown when index less than 0 or index greater than or equal to <see cref="Length"/>.
    /// </exception>
    public readonly T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.data[this.map[index]];
    }
}
