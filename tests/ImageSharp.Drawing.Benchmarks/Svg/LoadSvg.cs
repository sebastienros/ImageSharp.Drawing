// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Svg;
using SkiaSharp;
using Svg.Skia;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// Benchmarks the time from SVG bytes to a render-ready document.
/// </summary>
/// <remarks>
/// <para>
/// Svg.Skia parses the document, builds its model, and records an <see cref="SKPicture"/>.
/// ImageSharp.Drawing parses the document, applies the cascade, and resolves it to its render model, the
/// <see cref="SvgDocument"/>; recording canvas commands is part of a render, so it is measured in <see cref="RenderSvg"/>.
/// NetVips does not take part: libvips renders lazily and cannot separate loading from rendering.
/// It is measured in <see cref="LoadAndRenderSvg"/>.
/// </para>
/// <para>
/// Loading does not depend on the output size, so this class has no size parameter.
/// Both renderers load on one thread, so there is no thread configuration.
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

    /// <summary>
    /// Loads every document of the entry with <see cref="SvgDocument.Load(Stream)"/>: parse, cascade, and resolve to
    /// the render model.
    /// </summary>
    /// <returns>The summed intrinsic width, so that the work cannot be removed.</returns>
    [Benchmark]
    public float ImageSharp()
    {
        float width = 0;
        foreach (byte[] document in this.documents)
        {
            width += SvgImageSharpRenderer.Load(document).Size.Width;
        }

        return width;
    }
}
