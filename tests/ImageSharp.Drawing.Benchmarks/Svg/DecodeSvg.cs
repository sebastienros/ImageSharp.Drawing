// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// Benchmarks the SVG decoder against the same work through the <see cref="SvgDocument"/> API: bytes to a new image,
/// and bytes to the image size.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImageLoad"/> decodes with <see cref="Image.Load{TPixel}(DecoderOptions, byte[])"/> through a configuration
/// with <see cref="SvgConfigurationModule"/>, so the time includes format detection and the decoder.
/// <see cref="DocumentLoadAndDraw"/> loads with <see cref="SvgDocument.Load(Stream, SvgLoadOptions)"/> and draws with
/// <see cref="DrawingCanvas.DrawSvg(SvgDocument, RectangleF)"/> into a new image of the same size, which is the work the
/// decoder does. <see cref="ImageIdentify"/> reads the image size with <see cref="Image.Identify(DecoderOptions, byte[])"/>,
/// which loads the document, against <see cref="DocumentLoad"/>, the same load through the document API.
/// </para>
/// <para>
/// <c>Image.Load</c> passes general decoder options only, so the decoder loads with default load options, whose fonts
/// are the system fonts. Every method therefore loads with default load options, and the text documents draw with the
/// system fonts of the machine, not with the font of the other SVG benchmarks.
/// </para>
/// <para>
/// The long edge is the target size of the decoder (a square, so the document fits with its aspect ratio); the image
/// of <see cref="DocumentLoadAndDraw"/> has the size that the decoder computes, measured once in <see cref="Setup"/>.
/// Every method uses the default configuration, which rasterizes on every processor.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class DecodeSvg
{
    private byte[][] documents;
    private Size[] sizes;
    private DecoderOptions decoderOptions;
    private DecoderOptions identifyOptions;

    /// <summary>
    /// Gets the corpus entry names for <see cref="Document"/>: a path document, a set of small icons, a text document,
    /// a filter document, and the CSS features document, limited by <c>SVG_BENCHMARK_CORPUS</c> when it is set.
    /// </summary>
    public static IEnumerable<string> Corpus => SvgBenchmarkCorpus.Names.Where(
        static name => name is "Tiger" or "Icons" or "TextHeavy" or "FilterShadows" or "CssFeatures");

    /// <summary>
    /// Gets the long-edge sizes: 100 and 1000 pixels, or those of them that <c>SVG_BENCHMARK_SIZES</c> lists.
    /// </summary>
    public static IEnumerable<int> Sizes => SvgBenchmarkCorpus.Sizes.Where(static size => size <= 1000);

    /// <summary>
    /// Gets or sets the corpus entry. An entry with several documents decodes all of them in one operation.
    /// </summary>
    [ParamsSource(nameof(Corpus))]
    public string Document { get; set; }

    /// <summary>
    /// Gets or sets the target length of the longer side of each image, in pixels.
    /// </summary>
    [ParamsSource(nameof(Sizes))]
    public int LongEdge { get; set; }

    /// <summary>
    /// Reads the documents, creates the decoder options, and computes the image size of each document.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.documents = SvgBenchmarkCorpus.ReadDocuments(this.Document);
        Configuration configuration = Configuration.Default.Clone();
        configuration.Configure(new SvgConfigurationModule());
        this.decoderOptions = new DecoderOptions { Configuration = configuration, TargetSize = new Size(this.LongEdge, this.LongEdge) };
        this.identifyOptions = new DecoderOptions { Configuration = configuration };

        this.sizes = new Size[this.documents.Length];
        for (int i = 0; i < this.documents.Length; i++)
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(this.decoderOptions, this.documents[i]);
            this.sizes[i] = image.Size;
        }

        SvgImageSharpRenderer.WarmThreadPool();
    }

    /// <summary>
    /// Decodes every document of the entry with <see cref="Image.Load{TPixel}(DecoderOptions, byte[])"/>.
    /// </summary>
    /// <returns>The summed image width, so that the work cannot be removed.</returns>
    [Benchmark]
    public int ImageLoad()
    {
        int width = 0;
        foreach (byte[] document in this.documents)
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(this.decoderOptions, document);
            width += image.Width;
        }

        return width;
    }

    /// <summary>
    /// Loads every document of the entry with <see cref="SvgDocument.Load(Stream, SvgLoadOptions)"/> and draws it into a
    /// new image of the size that the decoder gives it.
    /// </summary>
    /// <returns>The summed image width, so that the work cannot be removed.</returns>
    [Benchmark(Baseline = true)]
    public int DocumentLoadAndDraw()
    {
        int width = 0;
        for (int i = 0; i < this.documents.Length; i++)
        {
            using MemoryStream stream = new(this.documents[i], writable: false);
            SvgDocument document = SvgDocument.Load(stream, new SvgLoadOptions());
            Size size = this.sizes[i];
            using Image<Rgba32> image = new(Configuration.Default, size.Width, size.Height);
            image.Mutate(c => c.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, size.Width, size.Height))));
            width += image.Width;
        }

        return width;
    }

    /// <summary>
    /// Reads the image size of every document of the entry with <see cref="Image.Identify(DecoderOptions, byte[])"/>.
    /// </summary>
    /// <returns>The summed width, so that the work cannot be removed.</returns>
    [Benchmark]
    public int ImageIdentify()
    {
        int width = 0;
        foreach (byte[] document in this.documents)
        {
            width += Image.Identify(this.identifyOptions, document).Width;
        }

        return width;
    }

    /// <summary>
    /// Loads every document of the entry with <see cref="SvgDocument.Load(Stream, SvgLoadOptions)"/> and default load
    /// options, the work of <see cref="ImageIdentify"/>.
    /// </summary>
    /// <returns>The summed intrinsic width, so that the work cannot be removed.</returns>
    [Benchmark]
    public float DocumentLoad()
    {
        float width = 0;
        foreach (byte[] document in this.documents)
        {
            using MemoryStream stream = new(document, writable: false);
            width += SvgDocument.Load(stream, new SvgLoadOptions()).Size.Width;
        }

        return width;
    }
}
