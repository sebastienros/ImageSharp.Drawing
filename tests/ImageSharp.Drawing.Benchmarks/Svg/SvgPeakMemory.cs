// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Svg.Skia;
using Vips = NetVips.NetVips;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// Measures the peak memory of one load and render of a corpus entry in the current process, for the comparison of
/// peak process memory between the renderers.
/// </summary>
/// <remarks>
/// <para>
/// The caller starts a fresh process for every measurement, so the peak belongs to one renderer and one document only.
/// The peak is the high-water mark of the resident set (<c>VmHWM</c> in <c>/proc/self/status</c>) that the kernel keeps
/// for the process: it counts managed and native memory alike, which is why it is the measure that compares renderers.
/// </para>
/// <para>
/// The run prints the high-water mark before the render (the runtime, the assemblies, and the document bytes) and at the
/// end. The baseline of a renderer is the same run with the entry <c>empty</c>, a document of one pixel, so that the
/// renderer library and its fonts are loaded and initialized; the entry <c>none</c> with the renderer <c>none</c> gives
/// the baseline of the process alone.
/// </para>
/// </remarks>
internal static class SvgPeakMemory
{
    /// <summary>
    /// The document of the entry <c>empty</c>: one pixel, nothing drawn.
    /// </summary>
    private const string EmptyDocument = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"/>";

    /// <summary>
    /// Loads and renders every document of an entry once with one renderer and prints the peak resident set of the
    /// process.
    /// </summary>
    /// <param name="renderer">The renderer: <c>imagesharp</c>, <c>svgskia</c>, <c>netvips</c>, or <c>none</c>.</param>
    /// <param name="entry">The corpus entry, the path of an SVG file, <c>empty</c>, or <c>none</c>.</param>
    /// <param name="longEdge">The output length of the longer side, in pixels.</param>
    /// <param name="singleThreaded">Whether ImageSharp.Drawing and libvips use one thread.</param>
    public static void Run(string renderer, string entry, int longEdge, bool singleThreaded)
    {
        byte[][] documents = entry switch
        {
            "none" => [],
            "empty" => [Encoding.UTF8.GetBytes(EmptyDocument)],
            _ => SvgBenchmarkCorpus.ReadEntryOrFile(entry)
        };

        long start = ReadStatusKilobytes("VmHWM");
        switch (renderer)
        {
            case "none":
                break;
            case "imagesharp":
                Configuration configuration = singleThreaded
                    ? SvgImageSharpRenderer.CreateSingleThreadedConfiguration()
                    : Configuration.Default;
                foreach (byte[] bytes in documents)
                {
                    SvgDocument document = SvgImageSharpRenderer.Load(bytes);
                    Size size = SvgImageSharpRenderer.GetOutputSize(document, longEdge, out RectangleF destination);
                    using Image<Rgba32> image = new(configuration, size.Width, size.Height);
                    image.Mutate(c => c.Paint(canvas => canvas.DrawSvg(document, destination)));
                }

                break;
            case "svgskia":
                foreach (byte[] bytes in documents)
                {
                    using SKSvg svg = SvgReferenceRenderers.LoadSkia(bytes);
                    SKSizeI size = SvgReferenceRenderers.GetSkiaOutputSize(svg.Picture!, longEdge, out float scale);
                    using SKSurface surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height));
                    SvgReferenceRenderers.DrawSkia(surface.Canvas, svg.Picture!, scale);
                    surface.Canvas.Flush();
                }

                break;
            case "netvips":
                int defaultConcurrency = SvgReferenceRenderers.ConfigureNetVips();
                Vips.Concurrency = singleThreaded ? 1 : defaultConcurrency;
                foreach (byte[] bytes in documents)
                {
                    SvgReferenceRenderers.RenderNetVips(bytes, SvgReferenceRenderers.GetNetVipsScale(bytes, longEdge));
                }

                break;
            default:
                Console.WriteLine($"Unknown renderer: {renderer}. Use 'imagesharp', 'svgskia', 'netvips', or 'none'.");
                return;
        }

        long peak = ReadStatusKilobytes("VmHWM");
        long resident = ReadStatusKilobytes("VmRSS");
        string threads = singleThreaded ? "one" : "default";
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"peak renderer={renderer} entry={entry} longEdge={longEdge} threads={threads} startKB={start} peakKB={peak} rssKB={resident}"));
    }

    /// <summary>
    /// Reads a field of <c>/proc/self/status</c> that the kernel gives in kilobytes.
    /// </summary>
    /// <param name="field">The field name, such as <c>VmHWM</c>.</param>
    /// <returns>The value in kilobytes, or -1 when the field is not there.</returns>
    private static long ReadStatusKilobytes(string field)
    {
        foreach (string line in File.ReadLines("/proc/self/status"))
        {
            if (line.StartsWith(field + ":", StringComparison.Ordinal))
            {
                string value = line.AsSpan(field.Length + 1).Trim().ToString();
                return long.Parse(value.AsSpan(0, value.IndexOf(' ', StringComparison.Ordinal)), CultureInfo.InvariantCulture);
            }
        }

        return -1;
    }
}
