// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The transform table of the resolve stage: every distinct transform once, in the order of first use, with a lookup
/// from a transform to its index. Its storage comes from <see cref="ArrayPool{T}.Shared"/> and returns there on
/// <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// <para>
/// The table replaces a <see cref="List{T}"/> with a <see cref="Dictionary{TKey, TValue}"/> beside it. Both grow by
/// doubling, so every load left about twice the final size of both as garbage; text and nested SVG images add one
/// transform per glyph or per instance, which made these large-object allocations. The table gives the same index for
/// every transform as the dictionary did: two transforms are the same entry exactly when
/// <see cref="Matrix3x2.Equals(Matrix3x2)"/> says so, which is the default comparer of the dictionary, and a new
/// transform gets the next index.
/// </para>
/// <para>
/// The lookup is a chained hash table over the entries: <c>buckets</c> holds one plus the index of the last entry added
/// to a bucket (0 for an empty bucket) and <c>next</c> holds one plus the index of the entry added before it to the same
/// bucket. The bucket count is a power of two at least the entry count, so a chain is short.
/// </para>
/// </remarks>
internal sealed class SvgTransformTable : IDisposable
{
    private readonly SvgPooledList<Matrix3x2> transforms = new();
    private int[] buckets = [];
    private int[] next = [];
    private int bucketMask = -1;

    /// <summary>
    /// Gets the number of transforms in the table.
    /// </summary>
    public int Count => this.transforms.Count;

    /// <summary>
    /// Gets the transform at an index.
    /// </summary>
    /// <param name="index">The index, below <see cref="Count"/>.</param>
    /// <returns>The transform.</returns>
    public Matrix3x2 this[int index] => this.transforms[index];

    /// <summary>
    /// Returns the index of a transform, adding it when the table does not hold an equal one.
    /// </summary>
    /// <param name="transform">The transform.</param>
    /// <returns>The index.</returns>
    public int GetOrAdd(Matrix3x2 transform)
    {
        int hash = transform.GetHashCode();
        if (this.bucketMask >= 0)
        {
            for (int i = this.buckets[hash & this.bucketMask] - 1; i >= 0; i = this.next[i] - 1)
            {
                if (this.transforms[i].Equals(transform))
                {
                    return i;
                }
            }
        }

        int index = this.transforms.Count;
        if (index > this.bucketMask)
        {
            this.Resize(index + 1);
        }

        this.transforms.Add(transform);
        ref int bucket = ref this.buckets[hash & this.bucketMask];
        this.next[index] = bucket;
        bucket = index + 1;
        return index;
    }

    /// <summary>
    /// Copies the transforms into a new array, in index order.
    /// </summary>
    /// <returns>The array.</returns>
    public Matrix3x2[] ToArray() => this.transforms.AsSpan().ToArray();

    /// <summary>
    /// Returns the storage to the pool. The table is empty afterwards.
    /// </summary>
    public void Dispose()
    {
        this.transforms.Dispose();
        ReturnArray(this.buckets);
        ReturnArray(this.next);
        this.buckets = [];
        this.next = [];
        this.bucketMask = -1;
    }

    /// <summary>
    /// Returns an array of integers to the pool; the empty array is not returned.
    /// </summary>
    /// <param name="array">The array.</param>
    private static void ReturnArray(int[] array)
    {
        if (array.Length != 0)
        {
            ArrayPool<int>.Shared.Return(array);
        }
    }

    /// <summary>
    /// Grows the buckets and the chains to the next power of two that holds <paramref name="required"/> entries, at
    /// least twice the current bucket count, and links the existing entries into the new buckets in index order.
    /// </summary>
    /// <param name="required">The number of entries the table must hold.</param>
    private void Resize(int required)
    {
        int size = Math.Max(16, (this.bucketMask + 1) * 2);
        while (size < required)
        {
            size *= 2;
        }

        int[] newBuckets = ArrayPool<int>.Shared.Rent(size);
        int[] newNext = ArrayPool<int>.Shared.Rent(size);
        newBuckets.AsSpan(0, size).Clear();
        int mask = size - 1;

        // Linking in index order rebuilds the chains of the old table; the order inside a chain does not change which
        // entry matches, because no two entries are equal.
        Span<Matrix3x2> entries = this.transforms.AsSpan();
        for (int i = 0; i < entries.Length; i++)
        {
            ref int bucket = ref newBuckets[entries[i].GetHashCode() & mask];
            newNext[i] = bucket;
            bucket = i + 1;
        }

        ReturnArray(this.buckets);
        ReturnArray(this.next);
        this.buckets = newBuckets;
        this.next = newNext;
        this.bucketMask = mask;
    }
}
