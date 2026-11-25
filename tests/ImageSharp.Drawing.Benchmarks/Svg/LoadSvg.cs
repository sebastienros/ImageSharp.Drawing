// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SkiaSharp;
using Svg.Skia;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// Benchmarks the time from SVG bytes to a render-ready document.
/// </summary>
/// <remarks>
/// <para>
/// Svg.Skia parses the document, builds its model, and records an <see cref="SKPicture"/>.
/// NetVips does not take part: libvips renders lazily and cannot separate loading from rendering.
/// It is measured in <see cref="LoadAndRenderSvg"/>.
/// </para>
/// <para>
/// Loading does not depend on the output size, so this class has no size parameter.
/// Svg.Skia loads on one thread, so there is no thread configuration.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class LoadSvg
{
    private byte[][] documents;

    /// <summary>
    /// Gets the corpus entry names for <see cref="Document"/>.
    /// </summary>
    public static IEnumerable<string> Corpus => SvgBenchmarkCorpus.Names;

    /// <summary>
    /// Gets or sets the corpus entry. An entry with several documents loads all of them in one operation.
    /// </summary>
    [ParamsSource(nameof(Corpus))]
    public string Document { get; set; }

    /// <summary>
    /// Reads the document bytes.
    /// </summary>
    [GlobalSetup]
    public void Setup() => this.documents = SvgBenchmarkCorpus.ReadDocuments(this.Document);

    /// <summary>
    /// Loads every document of the entry with Svg.Skia, including the <see cref="SKPicture"/> recording.
    /// </summary>
    /// <returns>The summed picture width, so that the work cannot be removed.</returns>
    [Benchmark(Baseline = true)]
    public float SvgSkia()
    {
        float width = 0;
        foreach (byte[] document in this.documents)
        {
            using SKSvg svg = SvgReferenceRenderers.LoadSkia(document);
            width += svg.Picture!.CullRect.Width;
        }

        return width;
    }

    // Package C: add the ImageSharp.Drawing method here. It loads every document with
    // SvgDocument.Load(stream) from a MemoryStream over the same bytes and returns a value that
    // depends on the result, for example the summed SvgDocument.Size.Width.
}
