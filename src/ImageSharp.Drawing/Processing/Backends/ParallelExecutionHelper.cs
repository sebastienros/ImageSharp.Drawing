// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Advanced;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Centralizes the conversion from configuration parallelism settings to partition counts and
/// <see cref="ParallelOptions"/> instances used by retained-scene CPU execution paths.
/// </summary>
internal static class ParallelExecutionHelper
{
    /// <summary>
    /// Computes the number of partitions to schedule for work constrained by a single work-item limit.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// The configured maximum degree of parallelism. A value of <c>-1</c> leaves the runtime
    /// parallelism cap unbounded, but partition planning remains capped to
    /// <see cref="Environment.ProcessorCount"/> to avoid excessive fan-out.
    /// </param>
    /// <param name="workItemCount">The total number of work items available for partitioning.</param>
    /// <returns>The number of partitions to schedule.</returns>
    public static int GetPartitionCount(int maxDegreeOfParallelism, int workItemCount)
        => Math.Min(GetPartitionLimit(maxDegreeOfParallelism), workItemCount);

    /// <summary>
    /// Computes the number of partitions to schedule for work constrained by two independent limits.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// The configured maximum degree of parallelism. A value of <c>-1</c> leaves the runtime
    /// parallelism cap unbounded, but partition planning remains capped to
    /// <see cref="Environment.ProcessorCount"/> to avoid excessive fan-out.
    /// </param>
    /// <param name="workItemCount">The total number of work items available for partitioning.</param>
    /// <param name="secondaryLimit">An additional caller-specific upper bound on useful partitions.</param>
    /// <returns>The number of partitions to schedule.</returns>
    public static int GetPartitionCount(int maxDegreeOfParallelism, int workItemCount, int secondaryLimit)
        => Math.Min(GetPartitionLimit(maxDegreeOfParallelism), Math.Min(workItemCount, secondaryLimit));

    /// <summary>
    /// Computes the number of partitions for row bands of pixels, with at least
    /// <see cref="ParallelExecutionSettings.DefaultMinimumPixelsProcessedPerTask"/> pixels per partition,
    /// as <c>ParallelRowIterator</c> of ImageSharp plans its steps. A result of 1 means the caller runs the
    /// bands on the current thread without a parallel loop.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">The configured maximum degree of parallelism.</param>
    /// <param name="bandCount">The number of row bands.</param>
    /// <param name="pixelCount">The number of pixels that the bands cover together.</param>
    /// <returns>The number of partitions to schedule, at least 1.</returns>
    public static int GetPartitionCountForPixels(int maxDegreeOfParallelism, int bandCount, long pixelCount)
    {
        long minimum = ParallelExecutionSettings.DefaultMinimumPixelsProcessedPerTask;
        long pixelLimit = Math.Max(1, (pixelCount + minimum - 1) / minimum);
        return (int)Math.Max(1, Math.Min(GetPartitionCount(maxDegreeOfParallelism, bandCount), pixelLimit));
    }

    /// <summary>
    /// Creates the <see cref="ParallelOptions"/> for a partitioned operation.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// The configured maximum degree of parallelism. A value of <c>-1</c> retains the runtime's
    /// unbounded sentinel because <paramref name="partitionCount"/> is always positive; positive
    /// values are capped to the smaller of the configured limit and the useful partition count.
    /// </param>
    /// <param name="partitionCount">The computed positive number of useful partitions for the operation.</param>
    /// <returns>The <see cref="ParallelOptions"/> instance for the operation.</returns>
    public static ParallelOptions CreateParallelOptions(int maxDegreeOfParallelism, int partitionCount)
        => new() { MaxDegreeOfParallelism = Math.Min(maxDegreeOfParallelism, partitionCount) };

    /// <summary>
    /// Computes the internal partition-planning cap for the configured parallelism setting.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// The configured maximum degree of parallelism. A value of <c>-1</c> keeps the runtime
    /// parallelism setting unbounded, but partition planning is capped to
    /// <see cref="Environment.ProcessorCount"/>.
    /// </param>
    /// <returns>The maximum number of partitions to plan for.</returns>
    private static int GetPartitionLimit(int maxDegreeOfParallelism)
        => maxDegreeOfParallelism == -1 ? Environment.ProcessorCount : maxDegreeOfParallelism;
}
