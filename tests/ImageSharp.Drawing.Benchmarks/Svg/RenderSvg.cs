// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
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
/// </remarks>
[MemoryDiagnoser]
public class RenderSvg
{
    private SKSvg[] skDocuments;
    private SKPicture[] skPictures;
    private float[] skScales;
    private SKRect[] skTargets;
    private SKSurface skSurface;

    /// <summary>
    /// Gets the corpus entry names for <see cref="Document"/>.
    /// </summary>
    public static IEnumerable<string> Corpus => SvgBenchmarkCorpus.Names;

    /// <summary>
    /// Gets or sets the corpus entry. An entry with several documents renders all of them in one operation.
    /// </summary>
    [ParamsSource(nameof(Corpus))]
    public string Document { get; set; }

    /// <summary>
    /// Gets or sets the output length of the longer side of each document, in pixels.
    /// </summary>
    [Params(100, 1000, 4000)]
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

        // Package C: load the same bytes with SvgDocument.Load here and create one Image<Rgba32>
        // target of the same size. Add a WebGPU target only if it works without a GPU.
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
    }

    /// <summary>
    /// Draws every document of the entry with Svg.Skia.
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

    // Package C: add the ImageSharp.Drawing methods here, following FillTiger:
    // - ImageSharp: image.Mutate(c => c.Paint(canvas => ...)) that clears each target rectangle and
    //   calls canvas.DrawSvg(document, destination) with the same output rectangle as skTargets.
    // - ImageSharp_SingleThreaded: the same with a cloned Configuration where MaxDegreeOfParallelism = 1.
    // - ImageSharpRetainedScene: canvas.RenderScene(...) of a scene recorded from DrawSvg in GlobalSetup.
}
