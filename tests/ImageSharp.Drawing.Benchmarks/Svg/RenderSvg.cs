// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Svg.Skia;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// Benchmarks the time from a render-ready document to pixels.
/// </summary>
/// <remarks>
/// <para>
/// Every document draws into one target that is large enough for the largest document of the entry.
/// The target area of each document is cleared before it draws; the clear is part of the measured time
/// for every renderer.
/// </para>
/// <para>
/// Svg.Skia replays its <see cref="SKPicture"/> on a raster surface, on one thread.
/// NetVips does not take part: libvips cannot render a loaded document again without loading it.
/// </para>
/// <para>
/// Skia keeps the results of image filters between draws of the same picture. A replay of a document with filters
/// then costs a fraction of its first draw (the Filter Chain document at 1000 pixels: 2.3 ms against 57 ms; see
/// <see cref="SvgInvocationCurve"/>), and the output is the same. So the Svg.Skia methods come in two forms, each with
/// its ImageSharp.Drawing counterpart. <see cref="SvgSkia"/> draws with the caches as the earlier draws left them: it is
/// the repeated draw of one document, and its counterpart is <see cref="ImageSharpRetainedScene"/>, whose scene also
/// holds the filter results. <see cref="SvgSkia_ColdCaches"/> empties the Skia caches before every draw: it is the first
/// draw of a loaded document, and its counterpart is <see cref="ImageSharp"/>, which computes every filter again. For
/// documents without filters the two Svg.Skia methods cost the same.
/// </para>
/// <para>
/// <see cref="Setup"/> runs every method until the runtime has compiled its code at the final tier
/// (<see cref="SvgBenchmarkWarmup"/>), so that a job with few warmup iterations does not measure code that is still
/// being optimized.
/// </para>
/// <para>
/// ImageSharp.Drawing draws into one <see cref="Image{TPixel}"/> target. Both targets are allocated in
/// <see cref="Setup"/>, outside the measured time. <see cref="ImageSharp"/> opens a new canvas per operation, records
/// the clears and the <see cref="DrawingCanvas.DrawSvg(SvgDocument, RectangleF)"/> commands, and renders them when the
/// canvas closes: this is the emit, the command preparation, and the rasterization. <see cref="ImageSharpRetainedScene"/>
/// replays a scene that <see cref="Setup"/> recorded from the same commands, so it measures the rasterization of a
/// prepared scene only. The default variants use the default configuration, which rasterizes on every processor; the
/// <c>_SingleThreaded</c> variants use a configuration with <see cref="Configuration.MaxDegreeOfParallelism"/> 1, which
/// is the per-core cost to compare with Svg.Skia.
/// </para>
/// <para>
/// No WebGPU method exists: the development container has no GPU, and the WebGPU tests of the test project skip there.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class RenderSvg
{
    private SKSvg[] skDocuments;
    private SKPicture[] skPictures;
    private float[] skScales;
    private SKRect[] skTargets;
    private SKSurface skSurface;

    private SvgDocument[] isDocuments;
    private RectangleF[] isDestinations;
    private Rectangle[] isTargets;
    private Image<Rgba32> image;
    private Configuration singleThreadedConfiguration;
    private DrawingBackendScene imageSharpRetainedScene;

    /// <summary>
    /// Gets the corpus entry names for <see cref="Document"/>.
    /// </summary>
    public static IEnumerable<string> Corpus => SvgBenchmarkCorpus.Names;

    /// <summary>
    /// Gets the long-edge sizes: 100, 1000, and 4000 pixels, or those that <c>SVG_BENCHMARK_SIZES</c> lists.
    /// </summary>
    public static IEnumerable<int> Sizes => SvgBenchmarkCorpus.Sizes;

    /// <summary>
    /// Gets or sets the corpus entry. An entry with several documents renders all of them in one operation.
    /// </summary>
    [ParamsSource(nameof(Corpus))]
    public string Document { get; set; }

    /// <summary>
    /// Gets or sets the output length of the longer side of each document, in pixels.
    /// </summary>
    [ParamsSource(nameof(Sizes))]
    public int LongEdge { get; set; }

    /// <summary>
    /// Loads the documents and creates the render targets.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        byte[][] documents = SvgBenchmarkCorpus.ReadDocuments(this.Document);
        this.skDocuments = new SKSvg[documents.Length];
        this.skPictures = new SKPicture[documents.Length];
        this.skScales = new float[documents.Length];
        this.skTargets = new SKRect[documents.Length];

        int width = 1;
        int height = 1;
        for (int i = 0; i < documents.Length; i++)
        {
            this.skDocuments[i] = SvgReferenceRenderers.LoadSkia(documents[i]);
            this.skPictures[i] = this.skDocuments[i].Picture;
            SKSizeI size = SvgReferenceRenderers.GetSkiaOutputSize(this.skPictures[i], this.LongEdge, out this.skScales[i]);
            this.skTargets[i] = SKRect.Create(size.Width, size.Height);
            width = Math.Max(width, size.Width);
            height = Math.Max(height, size.Height);
        }

        this.skSurface = SKSurface.Create(new SKImageInfo(width, height));

        SvgImageSharpRenderer.WarmThreadPool();
        this.singleThreadedConfiguration = SvgImageSharpRenderer.CreateSingleThreadedConfiguration();
        this.isDocuments = new SvgDocument[documents.Length];
        this.isDestinations = new RectangleF[documents.Length];
        this.isTargets = new Rectangle[documents.Length];

        width = 1;
        height = 1;
        for (int i = 0; i < documents.Length; i++)
        {
            this.isDocuments[i] = SvgImageSharpRenderer.Load(documents[i]);
            Size size = SvgImageSharpRenderer.GetOutputSize(this.isDocuments[i], this.LongEdge, out this.isDestinations[i]);
            this.isTargets[i] = new Rectangle(0, 0, size.Width, size.Height);
            width = Math.Max(width, size.Width);
            height = Math.Max(height, size.Height);
        }

        this.image = new Image<Rgba32>(width, height);

        // The retained scene holds the same commands as one operation of the ImageSharp method.
        using DrawingCanvas canvas = this.image.Frames.RootFrame.CreateCanvas(this.image.Configuration, new DrawingOptions());
        this.DrawImageSharp(canvas);
        this.imageSharpRetainedScene = canvas.CreateScene();

        SvgBenchmarkWarmup.Run(
            this.SvgSkia,
            this.ImageSharp,
            this.ImageSharp_SingleThreaded,
            this.ImageSharpRetainedScene,
            this.ImageSharpRetainedScene_SingleThreaded);
    }

    /// <summary>
    /// Releases the documents and the render targets.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (SKSvg document in this.skDocuments)
        {
            document.Dispose();
        }

        this.skSurface.Dispose();

        this.imageSharpRetainedScene.Dispose();
        this.image.Dispose();
    }

    /// <summary>
    /// Draws every document of the entry with Svg.Skia, with the Skia caches as the earlier draws left them.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void SvgSkia()
    {
        SKCanvas canvas = this.skSurface.Canvas;
        for (int i = 0; i < this.skPictures.Length; i++)
        {
            canvas.Save();
            canvas.ClipRect(this.skTargets[i]);
            canvas.Clear(SKColors.Transparent);
            SvgReferenceRenderers.DrawSkia(canvas, this.skPictures[i], this.skScales[i]);
            canvas.Restore();
        }

        canvas.Flush();
    }

    /// <summary>
    /// Draws every document of the entry with Svg.Skia after emptying the Skia caches, so that no draw reuses a filter
    /// result of an earlier draw.
    /// </summary>
    /// <remarks>
    /// The purge is inside the measured time. It costs less than the noise of the measurement: for documents without
    /// filters this method and <see cref="SvgSkia"/> give the same time.
    /// </remarks>
    [Benchmark]
    public void SvgSkia_ColdCaches()
    {
        SKGraphics.PurgeAllCaches();
        this.SvgSkia();
    }

    /// <summary>
    /// Draws every document of the entry with ImageSharp.Drawing on a new canvas, with the default configuration.
    /// </summary>
    [Benchmark]
    public void ImageSharp()
        => this.image.Mutate(c => c.Paint(this.DrawImageSharp));

    /// <summary>
    /// Draws every document of the entry with ImageSharp.Drawing on a new canvas, on one thread.
    /// </summary>
    [Benchmark]
    public void ImageSharp_SingleThreaded()
        => this.image.Mutate(this.singleThreadedConfiguration, c => c.Paint(this.DrawImageSharp));

    /// <summary>
    /// Replays the scene recorded in <see cref="Setup"/>, with the default configuration.
    /// </summary>
    [Benchmark]
    public void ImageSharpRetainedScene()
        => this.image.Mutate(c => c.Paint(canvas => canvas.RenderScene(this.imageSharpRetainedScene)));

    /// <summary>
    /// Replays the scene recorded in <see cref="Setup"/>, on one thread.
    /// </summary>
    [Benchmark]
    public void ImageSharpRetainedScene_SingleThreaded()
        => this.image.Mutate(this.singleThreadedConfiguration, c => c.Paint(canvas => canvas.RenderScene(this.imageSharpRetainedScene)));

    /// <summary>
    /// Records, for every document of the entry, the clear of its target area and the document, as
    /// <see cref="SvgSkia"/> does.
    /// </summary>
    /// <param name="canvas">The canvas to record into.</param>
    private void DrawImageSharp(DrawingCanvas canvas)
    {
        for (int i = 0; i < this.isDocuments.Length; i++)
        {
            canvas.Clear(SvgImageSharpRenderer.ClearBrush, this.isTargets[i]);
            canvas.DrawSvg(this.isDocuments[i], this.isDestinations[i]);
        }
    }
}
