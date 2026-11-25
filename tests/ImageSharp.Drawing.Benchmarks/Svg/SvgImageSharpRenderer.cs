// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// Shared setup and drawing code for the ImageSharp.Drawing SVG renderer in the SVG benchmarks.
/// </summary>
/// <remarks>
/// The output size and the destination follow the rules of <see cref="SvgReferenceRenderers"/>: the longer side of the
/// intrinsic size maps to the long edge, and the pixel size is the rounded scaled size. The destination rectangle keeps
/// the exact scaled size, as Svg.Skia draws its picture at the exact scale.
/// </remarks>
internal static class SvgImageSharpRenderer
{
    /// <summary>
    /// The brush that clears a target area to transparent pixels.
    /// </summary>
    public static readonly Brush ClearBrush = Brushes.Solid(Color.Transparent);

    /// <summary>
    /// Loads a document from its bytes through a stream, as a caller that receives an upload does.
    /// </summary>
    /// <param name="document">The SVG bytes.</param>
    /// <returns>The loaded document.</returns>
    public static SvgDocument Load(byte[] document)
    {
        using MemoryStream stream = new(document, writable: false);
        return SvgDocument.Load(stream);
    }

    /// <summary>
    /// Gets the output size in pixels and the destination rectangle of a document drawn with its longer side at
    /// <paramref name="longEdge"/> pixels.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="longEdge">The output length of the longer side, in pixels.</param>
    /// <param name="destination">Receives the destination rectangle at the origin, with the exact scaled size.</param>
    /// <returns>The output size.</returns>
    public static Size GetOutputSize(SvgDocument document, int longEdge, out RectangleF destination)
    {
        SizeF size = document.Size;
        float scale = SvgReferenceRenderers.GetScale(size.Width, size.Height, longEdge);
        destination = new RectangleF(0, 0, size.Width * scale, size.Height * scale);
        return new Size(
            Math.Max(1, (int)MathF.Round(size.Width * scale)),
            Math.Max(1, (int)MathF.Round(size.Height * scale)));
    }

    /// <summary>
    /// Creates a copy of the default configuration that processes on one thread.
    /// </summary>
    /// <returns>The configuration.</returns>
    public static Configuration CreateSingleThreadedConfiguration()
    {
        Configuration configuration = Configuration.Default.Clone();
        configuration.MaxDegreeOfParallelism = 1;
        return configuration;
    }

    /// <summary>
    /// Prints a stopwatch breakdown of the ImageSharp.Drawing stages for one corpus entry: the XML read with the
    /// cascade, the resolve stage, the emit stage (recording the canvas commands), the scene build (command
    /// preparation, stroking, flattening, and coverage), and the scene render (compositing into the target).
    /// </summary>
    /// <remarks>
    /// The stages run in sequence on one canvas, which is how one fresh render of <see cref="RenderSvg"/> runs them,
    /// except that the scene build and the scene render are separate calls here. The first iterations warm up the
    /// JIT and the pools and are not reported.
    /// </remarks>
    /// <param name="name">The corpus entry.</param>
    /// <param name="longEdge">The output length of the longer side, in pixels.</param>
    /// <param name="iterations">The number of measured iterations.</param>
    /// <param name="singleThreaded">Whether the canvas uses one thread.</param>
    public static void Profile(string name, int longEdge, int iterations, bool singleThreaded)
    {
        WarmThreadPool();
        byte[][] documents = SvgBenchmarkCorpus.ReadDocuments(name);
        Configuration configuration = singleThreaded ? CreateSingleThreadedConfiguration() : Configuration.Default;
        SvgLoadOptions loadOptions = new();
        SvgLimits limits = loadOptions.Limits;
        SvgReaderOptions readerOptions = new(limits.MaxElements, limits.MaxNestingDepth, limits.MaxCharactersFromEntities);

        // Stages: read, resolve, emit, scene build, scene render. The allocation count covers every thread, so it
        // includes the rasterizer workers.
        string[] stages = ["read + cascade", "resolve", "emit", "scene build", "scene render"];
        double[] milliseconds = new double[stages.Length];
        long[] allocated = new long[stages.Length];
        long startTimestamp = 0;
        long startAllocation = 0;
        int warmup = Math.Max(2, iterations / 5);
        for (int iteration = 0; iteration < warmup + iterations; iteration++)
        {
            if (iteration == warmup)
            {
                Array.Clear(milliseconds);
                Array.Clear(allocated);
            }

            foreach (byte[] bytes in documents)
            {
                Begin();
                SvgElementTree tree;
                using (MemoryStream stream = new(bytes, writable: false))
                {
                    tree = SvgReader.Read(stream, in readerOptions);
                }

                End(0);

                Begin();
                SvgDocument document = SvgResolver.Resolve(tree, loadOptions);
                End(1);

                // The image comes from the pools of the memory allocator; it is not part of any stage.
                Size size = GetOutputSize(document, longEdge, out RectangleF destination);
                using Image<PixelFormats.Rgba32> image = new(configuration, size.Width, size.Height);

                DrawingBackendScene scene;
                Begin();
                using (DrawingCanvas canvas = image.Frames.RootFrame.CreateCanvas(configuration, new DrawingOptions()))
                {
                    canvas.DrawSvg(document, destination);
                    End(2);

                    Begin();
                    scene = canvas.CreateScene();
                    End(3);
                }

                Begin();
                using (DrawingCanvas canvas = image.Frames.RootFrame.CreateCanvas(configuration, new DrawingOptions()))
                {
                    canvas.RenderScene(scene);
                }

                End(4);
                scene.Dispose();
            }
        }

        double sum = 0;
        long allocatedSum = 0;
        for (int i = 0; i < stages.Length; i++)
        {
            sum += milliseconds[i];
            allocatedSum += allocated[i];
        }

        Console.WriteLine($"{name} at {longEdge} px, {(singleThreaded ? "one thread" : "default threads")}, mean of {iterations} iterations");
        for (int i = 0; i < stages.Length; i++)
        {
            Console.WriteLine(
                $"  {stages[i],-16} {milliseconds[i] / iterations,10:0.000} ms {milliseconds[i] * 100 / sum,5:0.0}%" +
                $"  {allocated[i] / iterations / 1024.0,10:0.0} KB");
        }

        Console.WriteLine($"  {"total",-16} {sum / iterations,10:0.000} ms        {allocatedSum / iterations / 1024.0,10:0.0} KB");

        // Records the start of a stage.
        void Begin()
        {
            startAllocation = GC.GetTotalAllocatedBytes(precise: true);
            startTimestamp = Stopwatch.GetTimestamp();
        }

        // Adds the time and the allocated bytes since the last Begin to a stage.
        void End(int stage)
        {
            long now = Stopwatch.GetTimestamp();
            milliseconds[stage] += Stopwatch.GetElapsedTime(startTimestamp, now).TotalMilliseconds;
            allocated[stage] += GC.GetTotalAllocatedBytes(precise: true) - startAllocation;
        }
    }

    /// <summary>
    /// Makes sure that the thread pool has a worker thread per processor before the first measurement, as
    /// <c>FillTiger</c> does, so that the parallel rasterizer does not wait for the thread pool to grow.
    /// </summary>
    public static void WarmThreadPool()
    {
        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
        int desiredWorkerThreads = Math.Max(minWorkerThreads, Environment.ProcessorCount);
        ThreadPool.SetMinThreads(desiredWorkerThreads, minCompletionPortThreads);
        Parallel.For(0, desiredWorkerThreads, static _ => { });
    }
}
