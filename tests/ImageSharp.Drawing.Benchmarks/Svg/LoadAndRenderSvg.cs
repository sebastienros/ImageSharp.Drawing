// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Svg.Skia;
using Vips = NetVips.NetVips;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// Benchmarks the time from SVG bytes to pixels, as a service that turns an upload into a new image does.
/// </summary>
/// <remarks>
/// <para>
/// Each renderer allocates a new output for every document inside the measured time, as libvips always does.
/// Svg.Skia and ImageSharp.Drawing compute the output size from the document they load, inside the measured time.
/// libvips needs a scale before it loads, so its scale is computed in <see cref="Setup"/> from the intrinsic size that
/// it reports; a size lookup there would load the document a second time.
/// </para>
/// <para>
/// Allocation figures count managed memory only. The Skia surface and the libvips output are native memory.
/// ImageSharp.Drawing takes the pixels of each new <see cref="Image{TPixel}"/> from the pools of its memory allocator,
/// so after warmup the pixel buffer does not count either.
/// </para>
/// <para>
/// Thread configurations: Svg.Skia loads and draws on one thread, so it has one method.
/// libvips renders with a pool of worker threads; <see cref="NetVips"/> uses the default pool size and
/// <see cref="NetVips_SingleThreaded"/> uses one thread. The libvips caches are off.
/// ImageSharp.Drawing loads on one thread and rasterizes on every processor; <see cref="ImageSharp_SingleThreaded"/>
/// uses a configuration with <see cref="Configuration.MaxDegreeOfParallelism"/> 1.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class LoadAndRenderSvg
{
    private byte[][] documents;
    private double[] vipsScales;
    private int vipsDefaultConcurrency;
    private Configuration singleThreadedConfiguration;

    /// <summary>
    /// Gets the corpus entry names for <see cref="Document"/>.
    /// </summary>
    public static IEnumerable<string> Corpus => SvgBenchmarkCorpus.Names;

    /// <summary>
    /// Gets the long-edge sizes: 100, 1000, and 4000 pixels, or those that <c>SVG_BENCHMARK_SIZES</c> lists.
    /// </summary>
    public static IEnumerable<int> Sizes => SvgBenchmarkCorpus.Sizes;

    /// <summary>
    /// Gets or sets the corpus entry. An entry with several documents processes all of them in one operation.
    /// </summary>
    [ParamsSource(nameof(Corpus))]
    public string Document { get; set; }

    /// <summary>
    /// Gets or sets the output length of the longer side of each document, in pixels.
    /// </summary>
    [ParamsSource(nameof(Sizes))]
    public int LongEdge { get; set; }

    /// <summary>
    /// Reads the documents, configures libvips, and computes the libvips scale of each document.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.documents = SvgBenchmarkCorpus.ReadDocuments(this.Document);
        this.vipsDefaultConcurrency = SvgReferenceRenderers.ConfigureNetVips();
        this.vipsScales = new double[this.documents.Length];
        for (int i = 0; i < this.documents.Length; i++)
        {
            this.vipsScales[i] = SvgReferenceRenderers.GetNetVipsScale(this.documents[i], this.LongEdge);
        }

        SvgImageSharpRenderer.WarmThreadPool();
        this.singleThreadedConfiguration = SvgImageSharpRenderer.CreateSingleThreadedConfiguration();
    }

    /// <summary>
    /// Restores the default libvips thread count.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup() => Vips.Concurrency = this.vipsDefaultConcurrency;

    /// <summary>
    /// Loads every document of the entry with Svg.Skia and draws it on a new raster surface.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void SvgSkia()
    {
        foreach (byte[] document in this.documents)
        {
            using SKSvg svg = SvgReferenceRenderers.LoadSkia(document);
            SKPicture picture = svg.Picture;
            SKSizeI size = SvgReferenceRenderers.GetSkiaOutputSize(picture, this.LongEdge, out float scale);
            using SKSurface surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height));
            SvgReferenceRenderers.DrawSkia(surface.Canvas, picture, scale);
            surface.Canvas.Flush();
        }
    }

    /// <summary>
    /// Loads and renders every document of the entry with libvips <c>svgload</c> and the default thread count.
    /// </summary>
    [Benchmark]
    public void NetVips()
    {
        Vips.Concurrency = this.vipsDefaultConcurrency;
        for (int i = 0; i < this.documents.Length; i++)
        {
            SvgReferenceRenderers.RenderNetVips(this.documents[i], this.vipsScales[i]);
        }
    }

    /// <summary>
    /// Loads and renders every document of the entry with libvips <c>svgload</c> on one worker thread.
    /// </summary>
    [Benchmark]
    public void NetVips_SingleThreaded()
    {
        Vips.Concurrency = 1;
        for (int i = 0; i < this.documents.Length; i++)
        {
            SvgReferenceRenderers.RenderNetVips(this.documents[i], this.vipsScales[i]);
        }
    }

    /// <summary>
    /// Loads every document of the entry with ImageSharp.Drawing and draws it into a new image, with the default
    /// configuration.
    /// </summary>
    [Benchmark]
    public void ImageSharp() => this.LoadAndRenderImageSharp(Configuration.Default);

    /// <summary>
    /// Loads every document of the entry with ImageSharp.Drawing and draws it into a new image, on one thread.
    /// </summary>
    [Benchmark]
    public void ImageSharp_SingleThreaded() => this.LoadAndRenderImageSharp(this.singleThreadedConfiguration);

    /// <summary>
    /// Loads every document of the entry with ImageSharp.Drawing and draws it into a new image.
    /// </summary>
    /// <param name="configuration">The configuration of the images, which sets the rasterizer thread count.</param>
    private void LoadAndRenderImageSharp(Configuration configuration)
    {
        foreach (byte[] bytes in this.documents)
        {
            SvgDocument document = SvgImageSharpRenderer.Load(bytes);
            Size size = SvgImageSharpRenderer.GetOutputSize(document, this.LongEdge, out RectangleF destination);
            using Image<Rgba32> image = new(configuration, size.Width, size.Height);
            image.Mutate(c => c.Paint(canvas => canvas.DrawSvg(document, destination)));
        }
    }
}
