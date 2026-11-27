// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Builds the retained <see cref="PointF"/> array used by flattened segment caches without an intermediate collection copy.
/// </summary>
/// <remarks>
/// Segment flatteners ultimately need to return a tightly-sized array that can be cached by the segment instance.
/// The builder appends into a scratch array that the thread reuses between builds, and <see cref="Detach"/>
/// copies the appended points into one array of the exact length. A build therefore allocates only its result,
/// not the arrays that a growing buffer would discard.
/// </remarks>
internal struct FlattenedPointBuilder
{
    /// <summary>
    /// The largest scratch array that a thread keeps between builds (32 KB), so one large curve does not
    /// keep a large buffer alive on the thread.
    /// </summary>
    private const int MaxRetainedScratchLength = 4096;

    /// <summary>
    /// The scratch array of the thread. A builder takes it while it builds and gives it back in
    /// <see cref="Detach"/>, so a builder that starts while another is building gets its own array.
    /// </summary>
    [ThreadStatic]
    private static PointF[]? threadScratch;

    /// <summary>
    /// The scratch array the points are appended to. Grows geometrically.
    /// </summary>
    private PointF[] points;

    /// <summary>
    /// The number of points appended so far.
    /// </summary>
    private int count;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlattenedPointBuilder"/> struct.
    /// </summary>
    /// <param name="capacity">The estimated number of points that will be appended.</param>
    public FlattenedPointBuilder(int capacity)
    {
        PointF[]? scratch = threadScratch;
        threadScratch = null;
        this.points = scratch is not null && scratch.Length >= capacity ? scratch : new PointF[Math.Max(capacity, 64)];
        this.count = 0;
    }

    /// <summary>
    /// Appends one point to the retained point array.
    /// </summary>
    /// <param name="point">The point to append.</param>
    public void Add(PointF point)
    {
        this.EnsureCapacity(this.count + 1);
        this.points[this.count++] = point;
    }

    /// <summary>
    /// Reserves a writable append window for callers that populate multiple points directly.
    /// </summary>
    /// <param name="length">The number of points to reserve.</param>
    /// <returns>A span covering the reserved append window.</returns>
    public Span<PointF> GetAppendSpan(int length)
    {
        this.EnsureCapacity(this.count + length);
        return this.points.AsSpan(this.count, length);
    }

    /// <summary>
    /// Commits points previously written through <see cref="GetAppendSpan"/>.
    /// </summary>
    /// <param name="length">The number of points written to the reserved append window.</param>
    public void Advance(int length) => this.count += length;

    /// <summary>
    /// Returns a new array with the appended points and gives the scratch array back to the thread.
    /// </summary>
    /// <remarks>
    /// The builder must not be appended to afterwards.
    /// </remarks>
    /// <returns>The tightly-sized retained point array.</returns>
    public PointF[] Detach()
    {
        PointF[] result = this.points.AsSpan(0, this.count).ToArray();
        if (this.points.Length <= MaxRetainedScratchLength)
        {
            threadScratch = this.points;
        }

        this.points = [];
        this.count = 0;
        return result;
    }

    /// <summary>
    /// Ensures the owned array can store the requested total point count.
    /// </summary>
    /// <param name="capacity">The total number of points that must fit.</param>
    private void EnsureCapacity(int capacity)
    {
        if (capacity <= this.points.Length)
        {
            return;
        }

        Array.Resize(ref this.points, Math.Max(capacity, this.points.Length * 2));
    }
}
