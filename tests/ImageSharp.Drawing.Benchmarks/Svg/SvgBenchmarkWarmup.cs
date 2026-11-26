// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// Runs benchmark operations in the setup of a benchmark until the runtime has compiled their code at its final tier.
/// </summary>
/// <remarks>
/// <para>
/// BenchmarkDotNet runs one operation per iteration when the first call of a method is slow, which it is for every
/// method of managed code that a new process calls for the first time. A job with a few warmup iterations then measures
/// after a few operations only, while the runtime still replaces code with optimized versions: the per-draw times of
/// <see cref="SvgInvocationCurve"/> show a render of the Dashboard document that takes 678 ms on the first draw, about
/// 50 ms on draw 30, and about 36 ms from draw 100. The mean of such a job is too high for managed code and right for
/// native code, so it is not a fair comparison.
/// </para>
/// <para>
/// The warmup runs the operations in turn, so that every method reaches the same state whichever benchmark method the
/// process then measures. It stops after <see cref="MinimumRounds"/> rounds once <see cref="MinimumTime"/> has passed,
/// and in any case after <see cref="MaximumTime"/>, so that large documents do not make the setup long.
/// </para>
/// </remarks>
internal static class SvgBenchmarkWarmup
{
    /// <summary>
    /// The number of rounds after which the warmup may stop. The runtime promotes a method after 30 calls.
    /// </summary>
    private const int MinimumRounds = 60;

    /// <summary>
    /// The time before which the warmup does not stop: the runtime compiles optimized code in the background.
    /// </summary>
    private static readonly TimeSpan MinimumTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The time after which the warmup stops even when it has not run <see cref="MinimumRounds"/> rounds.
    /// </summary>
    private static readonly TimeSpan MaximumTime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs the operations in turn until the warmup limits are reached.
    /// </summary>
    /// <param name="operations">The operations: the bodies of the benchmark methods to warm up.</param>
    public static void Run(params Action[] operations)
    {
        long start = Stopwatch.GetTimestamp();
        for (int round = 0; ; round++)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
            if (elapsed >= MaximumTime || (round >= MinimumRounds && elapsed >= MinimumTime))
            {
                return;
            }

            foreach (Action operation in operations)
            {
                operation();
            }
        }
    }
}
