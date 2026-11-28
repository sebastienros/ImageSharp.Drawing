// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A growable list of values whose storage comes from <see cref="ArrayPool{T}.Shared"/>, for the lists of the resolve
/// stage that live for one load.
/// </summary>
/// <remarks>
/// <para>
/// The resolver builds its node list and its transform table by appending, and the final size is not known before the
/// document is resolved: a nested SVG image or a text run adds thousands of entries. A <see cref="List{T}"/> doubles its
/// array as it grows, so every load leaves the smaller arrays as garbage, about twice the final size; for large lists
/// they are large-object allocations, which drive full collections. This list doubles the same way, but it returns the
/// smaller array to the pool and rents the larger one, so a later load reuses the storage.
/// </para>
/// <para>
/// The members behave as their <see cref="List{T}"/> counterparts. A span from <see cref="AsSpan"/> is valid until the
/// next call that adds values: growing returns the old array to the pool, where another list can rent it. The owner must
/// call <see cref="Dispose"/> once the list is no longer used; values that hold references are cleared before the array
/// returns to the pool, so the pool keeps nothing of the document alive.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the values.</typeparam>
internal sealed class SvgPooledList<T> : IDisposable
{
    // The smallest array the shared pool hands out; renting less gives the same array.
    private const int MinimumCapacity = 16;

    private T[] items = [];
    private int count;

    /// <summary>
    /// Gets the number of values in the list.
    /// </summary>
    public int Count => this.count;

    /// <summary>
    /// Gets a reference to the value at an index.
    /// </summary>
    /// <param name="index">The index, below <see cref="Count"/>.</param>
    /// <returns>A reference to the value, valid until the next call that adds values.</returns>
    public ref T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)this.count)
            {
                ThrowIndexOutOfRange(nameof(index));
            }

            return ref this.items[index];
        }
    }

    /// <summary>
    /// Adds a value at the end of the list.
    /// </summary>
    /// <param name="value">The value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T value)
    {
        T[] array = this.items;
        int size = this.count;
        if ((uint)size < (uint)array.Length)
        {
            array[size] = value;
            this.count = size + 1;
            return;
        }

        this.Grow(size + 1);
        this.items[size] = value;
        this.count = size + 1;
    }

    /// <summary>
    /// Inserts a value at an index and moves the values from that index on one place up.
    /// </summary>
    /// <param name="index">The index, at most <see cref="Count"/>.</param>
    /// <param name="value">The value.</param>
    public void Insert(int index, T value)
    {
        if ((uint)index > (uint)this.count)
        {
            ThrowIndexOutOfRange(nameof(index));
        }

        if (this.count == this.items.Length)
        {
            this.Grow(this.count + 1);
        }

        T[] array = this.items;
        Array.Copy(array, index, array, index + 1, this.count - index);
        array[index] = value;
        this.count++;
    }

    /// <summary>
    /// Removes the value at an index and moves the values after it one place down.
    /// </summary>
    /// <param name="index">The index, below <see cref="Count"/>.</param>
    public void RemoveAt(int index) => this.RemoveRange(index, 1);

    /// <summary>
    /// Removes a range of values and moves the values after it down.
    /// </summary>
    /// <param name="index">The first index of the range.</param>
    /// <param name="length">The number of values to remove; the range must lie inside the list.</param>
    public void RemoveRange(int index, int length)
    {
        if (index < 0 || length < 0 || this.count - index < length)
        {
            ThrowIndexOutOfRange(nameof(index));
        }

        if (length == 0)
        {
            return;
        }

        T[] array = this.items;
        int end = index + length;
        Array.Copy(array, end, array, index, this.count - end);
        this.count -= length;

        // The slots after the new end are cleared, as List<T> does, so that a returned array holds no references and
        // the next value written there starts from a default value.
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(array, this.count, length);
        }
    }

    /// <summary>
    /// Returns the values of the list without copying them.
    /// </summary>
    /// <returns>The values, valid until the next call that adds values.</returns>
    public Span<T> AsSpan() => this.items.AsSpan(0, this.count);

    /// <summary>
    /// Returns the array to the pool. The list is empty afterwards and can grow again.
    /// </summary>
    public void Dispose()
    {
        T[] array = this.items;
        int size = this.count;
        this.items = [];
        this.count = 0;
        Return(array, size);
    }

    /// <summary>
    /// Returns an array that holds <paramref name="size"/> values to the pool, clearing the values first when they hold
    /// references. The slots after them are clear already: they were never written or were cleared on removal.
    /// </summary>
    /// <param name="array">The array; the empty array is not returned.</param>
    /// <param name="size">The number of values in use.</param>
    private static void Return(T[] array, int size)
    {
        if (array.Length == 0)
        {
            return;
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(array, 0, size);
        }

        ArrayPool<T>.Shared.Return(array);
    }

    /// <summary>
    /// Throws the exception of an index outside the list.
    /// </summary>
    /// <param name="paramName">The name of the parameter that holds the index.</param>
    private static void ThrowIndexOutOfRange(string paramName) => throw new ArgumentOutOfRangeException(paramName);

    /// <summary>
    /// Moves the values into a pooled array that holds at least <paramref name="required"/> values, doubling the
    /// capacity as <see cref="List{T}"/> does.
    /// </summary>
    /// <param name="required">The number of values the array must hold.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int required)
    {
        T[] old = this.items;
        int capacity = (int)Math.Min((uint)Math.Max(old.Length, MinimumCapacity / 2) * 2, (uint)Array.MaxLength);
        T[] array = ArrayPool<T>.Shared.Rent(Math.Max(capacity, required));
        Array.Copy(old, array, this.count);
        this.items = array;
        Return(old, this.count);
    }
}
