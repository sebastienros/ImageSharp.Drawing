// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The allocator of a scene that a document keeps (<see cref="SvgSceneCache"/>): every buffer is a new managed array.
/// </summary>
/// <remarks>
/// <para>
/// A kept scene is never disposed, because a canvas or an outer scene can still render it after the cache has dropped it.
/// Its memory must therefore be memory that the garbage collector frees: a pooled buffer of ImageSharp that is not
/// disposed returns to its pool only through a finalizer, which ImageSharp counts as an undisposed allocation. These
/// buffers have no finalizer and no lifetime guard. Their disposal only counts the released bytes.
/// </para>
/// <para>
/// The allocator copies the buffer and group limits of the allocator that the draw would use, so a recording fails where
/// a direct draw fails. Groups are split into buffers of at most <see cref="BufferCapacityInBytes"/>, the buffer size of
/// the pool of ImageSharp, so no single buffer of a group is larger than with the default allocator.
/// </para>
/// <para>
/// The allocator counts the bytes that it hands out and the bytes of the buffers that are disposed. What a scene keeps of
/// it is the difference: the buffers of a recording that were not disposed when the scene was sealed, such as the row
/// storage and the filter results. The rest were working buffers.
/// </para>
/// </remarks>
internal sealed class SvgSceneAllocator : MemoryAllocator
{
    /// <summary>
    /// The largest buffer of a memory group, in bytes: the buffer size of the pool of ImageSharp's default allocator.
    /// </summary>
    private const int BufferCapacityInBytes = 4 * 1024 * 1024;

    private long allocatedBytes;
    private long releasedBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgSceneAllocator"/> class.
    /// </summary>
    /// <param name="limits">The allocator whose buffer and group limits apply.</param>
    public SvgSceneAllocator(MemoryAllocator limits)
    {
        this.MemoryGroupAllocationLimitBytes = limits.MemoryGroupAllocationLimitBytes;
        this.SingleBufferAllocationLimitBytes = limits.SingleBufferAllocationLimitBytes;
    }

    /// <summary>
    /// Gets the number of bytes of the buffers that were handed out and are not disposed.
    /// </summary>
    public long LiveBytes => Interlocked.Read(ref this.allocatedBytes) - Interlocked.Read(ref this.releasedBytes);

    /// <summary>
    /// Gets the number of bytes of the buffers that were handed out and then disposed.
    /// </summary>
    public long ReleasedBytes => Interlocked.Read(ref this.releasedBytes);

    /// <inheritdoc />
    protected override int GetBufferCapacityInBytes() => BufferCapacityInBytes;

    /// <inheritdoc />
    protected override AllocationTrackedMemoryManager<T> AllocateCore<T>(int length, AllocationOptions options = AllocationOptions.None)
    {
        // A new array is zeroed, so AllocationOptions.Clean holds without a clear.
        ManagedBuffer<T> buffer = new(this, length);
        _ = Interlocked.Add(ref this.allocatedBytes, buffer.LengthInBytes);
        return buffer;
    }

    /// <summary>
    /// Counts the bytes of a disposed buffer.
    /// </summary>
    /// <param name="bytes">The number of bytes.</param>
    private void Release(long bytes) => Interlocked.Add(ref this.releasedBytes, bytes);

    /// <summary>
    /// A buffer over a managed array.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    private sealed class ManagedBuffer<T> : AllocationTrackedMemoryManager<T>
        where T : struct
    {
        private readonly SvgSceneAllocator owner;
        private readonly T[] array;
        private int disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ManagedBuffer{T}"/> class.
        /// </summary>
        /// <param name="owner">The allocator that counts the bytes.</param>
        /// <param name="length">The number of elements.</param>
        public ManagedBuffer(SvgSceneAllocator owner, int length)
        {
            this.owner = owner;
            this.array = new T[length];
            this.LengthInBytes = (long)length * Unsafe.SizeOf<T>();
        }

        /// <summary>
        /// Gets the length of the buffer in bytes.
        /// </summary>
        public long LengthInBytes { get; }

        /// <inheritdoc />
        public override Span<T> GetSpan() => this.array;

        /// <inheritdoc />
        /// <remarks>
        /// The returned handle owns the pin: disposing it frees the pin, so no pin can keep the array alive after the
        /// handle is gone.
        /// </remarks>
        public override unsafe MemoryHandle Pin(int elementIndex = 0)
        {
            GCHandle handle = GCHandle.Alloc(this.array, GCHandleType.Pinned);
            void* pointer = Unsafe.Add<T>((void*)handle.AddrOfPinnedObject(), elementIndex);
            return new MemoryHandle(pointer, handle);
        }

        /// <inheritdoc />
        public override void Unpin()
        {
        }

        /// <inheritdoc />
        protected override void DisposeCore(bool disposing)
        {
            // The array stays valid: only the count changes, once. A scene that is still in use after a dispose of one of
            // its buffers is not a case of this allocator, but a managed array cannot be reused by anything else either.
            if (Interlocked.Exchange(ref this.disposed, 1) == 0)
            {
                this.owner.Release(this.LengthInBytes);
            }
        }
    }
}
