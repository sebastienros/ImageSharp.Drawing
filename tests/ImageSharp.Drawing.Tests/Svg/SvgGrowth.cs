// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Time measurements for hostile documents. A hostile case runs once with a small input and once with a large one, and
/// the assertion compares the two times instead of comparing one time with an absolute limit: a machine under load
/// slows both runs, so the ratio stays meaningful where an absolute limit fails at random, and a cost that grows faster
/// than the input still fails.
/// </summary>
internal static class SvgGrowth
{
    /// <summary>
    /// The shortest time that a small run counts as. A small run can be dominated by fixed costs and by timer
    /// resolution, so a ratio against a few milliseconds would say nothing.
    /// </summary>
    private const double MinimumMilliseconds = 50D;

    /// <summary>
    /// The factor of slack on every expected ratio. It covers a change of machine load between the two runs and costs
    /// that are linear only above a fixed part; a cost of a higher order than the expected one exceeds it at the input
    /// ratios that the tests use.
    /// </summary>
    private const double Slack = 4D;

    /// <summary>
    /// Runs an action once to warm it up (just-in-time compilation and caches), then measures it.
    /// </summary>
    /// <param name="output">The test output, which receives the measured time.</param>
    /// <param name="name">The name of the case in the output.</param>
    /// <param name="action">The action.</param>
    /// <returns>The time of the measured run.</returns>
    public static TimeSpan MeasureWarm(ITestOutputHelper output, string name, Action action)
    {
        action();
        return Measure(output, name, action);
    }

    /// <summary>
    /// Measures one run of an action.
    /// </summary>
    /// <param name="output">The test output, which receives the measured time.</param>
    /// <param name="name">The name of the case in the output.</param>
    /// <param name="action">The action.</param>
    /// <returns>The time of the run.</returns>
    public static TimeSpan Measure(ITestOutputHelper output, string name, Action action)
    {
        Stopwatch watch = Stopwatch.StartNew();
        action();
        watch.Stop();
        output.WriteLine($"{name}: {watch.Elapsed.TotalMilliseconds:0} ms");
        return watch.Elapsed;
    }

    /// <summary>
    /// Runs an action once to warm it up, then measures it several times and returns the shortest time. Use it for a
    /// cost that varies much between runs (parallel work on a loaded machine): the shortest run is the one with the
    /// least disturbance.
    /// </summary>
    /// <param name="output">The test output, which receives the measured time.</param>
    /// <param name="name">The name of the case in the output.</param>
    /// <param name="runs">The number of measured runs.</param>
    /// <param name="action">The action.</param>
    /// <returns>The shortest time of the measured runs.</returns>
    public static TimeSpan MeasureBest(ITestOutputHelper output, string name, int runs, Action action)
    {
        action();
        TimeSpan best = TimeSpan.MaxValue;
        for (int i = 0; i < runs; i++)
        {
            Stopwatch watch = Stopwatch.StartNew();
            action();
            watch.Stop();
            if (watch.Elapsed < best)
            {
                best = watch.Elapsed;
            }
        }

        output.WriteLine($"{name}: {best.TotalMilliseconds:0} ms (best of {runs})");
        return best;
    }

    /// <summary>
    /// Asserts that a cost grew at most in proportion to its input: the large run, with <paramref name="factor"/> times
    /// the input of the small run, took less than <paramref name="factor"/> times the small run, with slack. For a cost
    /// that may be quadratic, pass the square of the input ratio.
    /// </summary>
    /// <param name="small">The time of the small run.</param>
    /// <param name="large">The time of the large run.</param>
    /// <param name="factor">The largest expected ratio of the two times.</param>
    public static void AssertLinear(TimeSpan small, TimeSpan large, double factor)
    {
        double reference = Math.Max(small.TotalMilliseconds, MinimumMilliseconds);
        Assert.True(
            large.TotalMilliseconds < reference * factor * Slack,
            $"{small.TotalMilliseconds:0} ms grew to {large.TotalMilliseconds:0} ms; the limit is {factor} times, with slack {Slack}.");
    }

    /// <summary>
    /// Asserts that a limit bounds a cost: a larger input that also exceeds the limit took about as long as a smaller
    /// one, so the work stopped at the limit and did not follow the input.
    /// </summary>
    /// <param name="small">The time of the run with the smaller input.</param>
    /// <param name="large">The time of the run with the larger input.</param>
    public static void AssertBounded(TimeSpan small, TimeSpan large) => AssertLinear(small, large, 1D);
}
