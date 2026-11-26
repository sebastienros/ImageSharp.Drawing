// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Globalization;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Svg.Skia;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// Times every draw of a sequence of draws of one corpus entry, so that an effect that depends on how often a document
/// was drawn before becomes visible: a cache that a renderer keeps between draws, or code that the runtime compiles
/// again after some calls.
/// </summary>
/// <remarks>
/// <para>
/// A benchmark reports the mean of many draws after a warmup. That mean is the cost of a draw only when every draw does
/// the same work. This runner checks that assumption for the methods of <see cref="RenderSvg"/>.
/// </para>
/// <para>
/// Svg.Skia modes: <c>replay</c> draws the same picture again, as <see cref="RenderSvg.SvgSkia"/> does;
/// <c>replay-purged</c> empties the Skia caches before every draw (the purge is not timed); <c>fresh</c> loads the
/// document again before every draw and times the draw only, so no draw can reuse a result that belongs to an earlier
/// picture. ImageSharp.Drawing modes: <c>render</c> draws the loaded document on a new canvas, <c>render-single</c>
/// does so on one thread, and <c>retained</c> replays a retained scene.
/// </para>
/// </remarks>
internal static class SvgInvocationCurve
{
    /// <summary>
    /// The draw numbers, counted from 1, whose single times are printed.
    /// </summary>
    private static readonly int[] PrintedDraws = [1, 2, 3, 4, 5, 10, 20, 30, 50, 75, 100, 150, 200, 300, 500];

    /// <summary>
    /// Runs every mode for one corpus entry and prints the time of single draws and the medians of three ranges.
    /// </summary>
    /// <param name="entry">The corpus entry or the path of an SVG file.</param>
    /// <param name="longEdge">The output length of the longer side, in pixels.</param>
    /// <param name="draws">The number of draws per mode.</param>
    public static void Run(string entry, int longEdge, int draws)
    {
        byte[][] documents = SvgBenchmarkCorpus.ReadEntryOrFile(entry);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"curve entry={entry} longEdge={longEdge} draws={draws} documents={documents.Length}"));

        RunSkia(documents, longEdge, draws);
        RunImageSharp(documents, longEdge, draws);
    }

    /// <summary>
    /// Runs the Svg.Skia modes.
    /// </summary>
    /// <param name="documents">The SVG documents of the entry.</param>
    /// <param name="longEdge">The output length of the longer side, in pixels.</param>
    /// <param name="draws">The number of draws per mode.</param>
    private static void RunSkia(byte[][] documents, int longEdge, int draws)
    {
        SKSvg[] loaded = new SKSvg[documents.Length];
        float[] scales = new float[documents.Length];
        SKRect[] targets = new SKRect[documents.Length];
        int width = 1;
        int height = 1;
        for (int i = 0; i < documents.Length; i++)
        {
            loaded[i] = SvgReferenceRenderers.LoadSkia(documents[i]);
            SKSizeI size = SvgReferenceRenderers.GetSkiaOutputSize(loaded[i].Picture!, longEdge, out scales[i]);
            targets[i] = SKRect.Create(size.Width, size.Height);
            width = Math.Max(width, size.Width);
            height = Math.Max(height, size.Height);
        }

        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
        double[] times = new double[draws];

        // Fresh first, so that its first draw is the first draw of the process and its pictures leave nothing that the
        // replay could use. The loads are not timed.
        ulong firstHash = 0;
        for (int d = 0; d < draws; d++)
        {
            SKSvg[] fresh = new SKSvg[documents.Length];
            for (int i = 0; i < documents.Length; i++)
            {
                fresh[i] = SvgReferenceRenderers.LoadSkia(documents[i]);
            }

            long start = Stopwatch.GetTimestamp();
            DrawSkia(surface, fresh, scales, targets);
            times[d] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (d == 0)
            {
                firstHash = Hash(surface);
            }

            foreach (SKSvg svg in fresh)
            {
                svg.Dispose();
            }
        }

        Print("svgskia fresh", times);
        SKGraphics.PurgeAllCaches();
        for (int d = 0; d < draws; d++)
        {
            long start = Stopwatch.GetTimestamp();
            DrawSkia(surface, loaded, scales, targets);
            times[d] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Print("svgskia replay", times);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  output equal to the first fresh draw: {Hash(surface) == firstHash}"));

        for (int d = 0; d < draws; d++)
        {
            SKGraphics.PurgeAllCaches();
            long start = Stopwatch.GetTimestamp();
            DrawSkia(surface, loaded, scales, targets);
            times[d] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Print("svgskia replay-purged", times);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  output equal to the first fresh draw: {Hash(surface) == firstHash}"));

        foreach (SKSvg svg in loaded)
        {
            svg.Dispose();
        }
    }

    /// <summary>
    /// Runs the ImageSharp.Drawing modes.
    /// </summary>
    /// <param name="documents">The SVG documents of the entry.</param>
    /// <param name="longEdge">The output length of the longer side, in pixels.</param>
    /// <param name="draws">The number of draws per mode.</param>
    private static void RunImageSharp(byte[][] documents, int longEdge, int draws)
    {
        SvgImageSharpRenderer.WarmThreadPool();
        Configuration single = SvgImageSharpRenderer.CreateSingleThreadedConfiguration();
        SvgDocument[] loaded = new SvgDocument[documents.Length];
        RectangleF[] destinations = new RectangleF[documents.Length];
        Rectangle[] targets = new Rectangle[documents.Length];
        int width = 1;
        int height = 1;
        for (int i = 0; i < documents.Length; i++)
        {
            loaded[i] = SvgImageSharpRenderer.Load(documents[i]);
            Size size = SvgImageSharpRenderer.GetOutputSize(loaded[i], longEdge, out destinations[i]);
            targets[i] = new Rectangle(0, 0, size.Width, size.Height);
            width = Math.Max(width, size.Width);
            height = Math.Max(height, size.Height);
        }

        using Image<Rgba32> image = new(width, height);
        double[] times = new double[draws];

        void Draw(DrawingCanvas canvas)
        {
            for (int i = 0; i < loaded.Length; i++)
            {
                canvas.Clear(SvgImageSharpRenderer.ClearBrush, targets[i]);
                canvas.DrawSvg(loaded[i], destinations[i]);
            }
        }

        for (int d = 0; d < draws; d++)
        {
            long start = Stopwatch.GetTimestamp();
            image.Mutate(c => c.Paint(Draw));
            times[d] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Print("imagesharp render", times);

        for (int d = 0; d < draws; d++)
        {
            long start = Stopwatch.GetTimestamp();
            image.Mutate(single, c => c.Paint(Draw));
            times[d] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Print("imagesharp render-single", times);

        using DrawingCanvas recorder = image.Frames.RootFrame.CreateCanvas(image.Configuration, new DrawingOptions());
        Draw(recorder);
        using DrawingBackendScene scene = recorder.CreateScene();
        for (int d = 0; d < draws; d++)
        {
            long start = Stopwatch.GetTimestamp();
            image.Mutate(c => c.Paint(canvas => canvas.RenderScene(scene)));
            times[d] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Print("imagesharp retained", times);
    }

    /// <summary>
    /// Draws every picture into its target area of the surface, as <see cref="RenderSvg.SvgSkia"/> does.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <param name="documents">The loaded documents.</param>
    /// <param name="scales">The scale of each document.</param>
    /// <param name="targets">The target area of each document.</param>
    private static void DrawSkia(SKSurface surface, SKSvg[] documents, float[] scales, SKRect[] targets)
    {
        SKCanvas canvas = surface.Canvas;
        for (int i = 0; i < documents.Length; i++)
        {
            canvas.Save();
            canvas.ClipRect(targets[i]);
            canvas.Clear(SKColors.Transparent);
            SvgReferenceRenderers.DrawSkia(canvas, documents[i].Picture!, scales[i]);
            canvas.Restore();
        }

        canvas.Flush();
    }

    /// <summary>
    /// Computes a hash of the pixels of a surface, to tell whether two draws gave the same output.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <returns>The FNV-1a hash of the pixel bytes.</returns>
    private static ulong Hash(SKSurface surface)
    {
        using SKPixmap pixels = surface.PeekPixels();
        ulong hash = 14695981039346656037UL;
        foreach (byte value in pixels.GetPixelSpan())
        {
            hash = (hash ^ value) * 1099511628211UL;
        }

        return hash;
    }

    /// <summary>
    /// Prints the times of single draws and the medians of the first ten draws, the next twenty, and the rest.
    /// </summary>
    /// <param name="mode">The name of the mode.</param>
    /// <param name="times">The time of each draw in milliseconds, in the order of the draws.</param>
    private static void Print(string mode, double[] times)
    {
        Console.WriteLine(mode);
        Console.Write("  draw:");
        foreach (int draw in PrintedDraws)
        {
            if (draw <= times.Length)
            {
                Console.Write(string.Create(CultureInfo.InvariantCulture, $" {draw}={times[draw - 1]:0.00}"));
            }
        }

        Console.WriteLine(" ms");
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  median: draws 1-10 {Median(times, 0, 10):0.00} ms, 11-30 {Median(times, 10, 30):0.00} ms, 31-{times.Length} {Median(times, 30, times.Length):0.00} ms; last quarter {Median(times, times.Length * 3 / 4, times.Length):0.00} ms; shortest {times.Min():0.00} ms"));
    }

    /// <summary>
    /// Returns the median of a range of the times.
    /// </summary>
    /// <param name="times">The times.</param>
    /// <param name="start">The first index of the range.</param>
    /// <param name="end">The index after the last of the range; values beyond the array are cut off.</param>
    /// <returns>The median, or 0 for an empty range.</returns>
    private static double Median(double[] times, int start, int end)
    {
        end = Math.Min(end, times.Length);
        if (start >= end)
        {
            return 0;
        }

        double[] range = times[start..end];
        Array.Sort(range);
        return range[range.Length / 2];
    }
}
