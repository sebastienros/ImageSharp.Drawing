// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SkiaSharp;
using Svg.Skia;
using Vips = NetVips.NetVips;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// Benchmarks the time from SVG bytes to pixels, as a service that turns an upload into a new image does.
/// </summary>
/// <remarks>
/// <para>
/// Each renderer allocates a new output for every document, as libvips always does.
/// The output size of each document is computed in <see cref="Setup"/> from the intrinsic size that
/// the renderer reports, so that size lookups are not part of the measured time.
/// </para>
/// <para>
/// Thread configurations: Svg.Skia loads and draws on one thread, so it has one method.
/// libvips renders with a pool of worker threads; <see cref="NetVips"/> uses the default pool size and
/// <see cref="NetVips_SingleThreaded"/> uses one thread. The libvips caches are off.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class LoadAndRenderSvg
{
    private byte[][] documents;
    private double[] vipsScales;
    private int vipsDefaultConcurrency;

    /// <summary>
    /// Gets the corpus entry names for <see cref="Document"/>.
    /// </summary>
    public static IEnumerable<string> Corpus => SvgBenchmarkCorpus.Names;

    /// <summary>
    /// Gets or sets the corpus entry. An entry with several documents processes all of them in one operation.
    /// </summary>
    [ParamsSource(nameof(Corpus))]
    public string Document { get; set; }

    /// <summary>
    /// Gets or sets the output length of the longer side of each document, in pixels.
    /// </summary>
    [Params(100, 1000, 4000)]
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

    // Package C: add the ImageSharp.Drawing methods here, following FillTiger:
    // - ImageSharp: for each document, SvgDocument.Load(stream) from a MemoryStream over the same bytes,
    //   a new Image<Rgba32> of the output size, and image.Mutate(c => c.Paint(canvas => canvas.DrawSvg(document, destination))).
    // - ImageSharp_SingleThreaded: the same with a cloned Configuration where MaxDegreeOfParallelism = 1.
}
