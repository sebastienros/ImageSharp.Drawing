// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Svg.Skia;
using IOPath = System.IO.Path;
using Vips = NetVips.NetVips;
using VipsCache = NetVips.Cache;
using VipsImage = NetVips.Image;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// Shared setup and drawing code for the SVG reference renderers: Svg.Skia and NetVips (librsvg).
/// </summary>
/// <remarks>
/// The fairness rules are in <c>src/ImageSharp.Drawing/Svg/SVG_DESIGN.md</c>, section 9.3.
/// Every renderer draws to raw pixels in memory. No benchmark encodes an image.
/// </remarks>
internal static class SvgReferenceRenderers
{
    /// <summary>
    /// Turns off every libvips cache and returns the default libvips thread count.
    /// </summary>
    /// <remarks>
    /// The libvips operation cache returns the earlier result when the same operation runs again on
    /// the same buffer, so a repeated load would measure a cache lookup.
    /// </remarks>
    /// <returns>The number of worker threads that libvips uses by default.</returns>
    public static int ConfigureNetVips()
    {
        VipsCache.Max = 0;
        VipsCache.MaxMem = 0;
        VipsCache.MaxFiles = 0;
        return Vips.Concurrency;
    }

    /// <summary>
    /// Parses a document with Svg.Skia and records it as an <see cref="SKPicture"/>, with the fonts of
    /// <see cref="SvgBenchmarkFonts"/>.
    /// </summary>
    /// <param name="document">The SVG bytes.</param>
    /// <returns>The loaded document. The caller disposes it; it owns its picture.</returns>
    public static SKSvg LoadSkia(byte[] document)
    {
        SKSvg svg = new();
        SvgBenchmarkFonts.Configure(svg);
        using MemoryStream stream = new(document, writable: false);
        if (svg.Load(stream) is null)
        {
            svg.Dispose();
            throw new InvalidOperationException("Svg.Skia could not load the document.");
        }

        return svg;
    }

    /// <summary>
    /// Gets the uniform scale that maps the longer side of an intrinsic size to <paramref name="longEdge"/> pixels.
    /// </summary>
    /// <param name="width">The intrinsic width.</param>
    /// <param name="height">The intrinsic height.</param>
    /// <param name="longEdge">The output length of the longer side, in pixels.</param>
    /// <returns>The scale factor.</returns>
    public static float GetScale(float width, float height, int longEdge)
        => longEdge / MathF.Max(width, height);

    /// <summary>
    /// Gets the output size in pixels of a Svg.Skia picture drawn with <see cref="GetScale"/>.
    /// </summary>
    /// <param name="picture">The picture.</param>
    /// <param name="longEdge">The output length of the longer side, in pixels.</param>
    /// <param name="scale">Receives the scale factor.</param>
    /// <returns>The output size.</returns>
    public static SKSizeI GetSkiaOutputSize(SKPicture picture, int longEdge, out float scale)
    {
        SKRect bounds = picture.CullRect;
        scale = GetScale(bounds.Width, bounds.Height, longEdge);
        return new SKSizeI(
            Math.Max(1, (int)MathF.Round(bounds.Width * scale)),
            Math.Max(1, (int)MathF.Round(bounds.Height * scale)));
    }

    /// <summary>
    /// Draws a picture at a uniform scale.
    /// </summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="picture">The picture.</param>
    /// <param name="scale">The scale factor.</param>
    public static void DrawSkia(SKCanvas canvas, SKPicture picture, float scale)
    {
        canvas.Save();
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    /// <summary>
    /// Gets the libvips <c>svgload</c> scale that maps the longer side of the document to
    /// <paramref name="longEdge"/> pixels.
    /// </summary>
    /// <remarks>
    /// Loading at scale 1 reads the header only; it does not render. librsvg decides the intrinsic size.
    /// </remarks>
    /// <param name="document">The SVG bytes.</param>
    /// <param name="longEdge">The output length of the longer side, in pixels.</param>
    /// <returns>The scale factor.</returns>
    public static double GetNetVipsScale(byte[] document, int longEdge)
    {
        using VipsImage header = VipsImage.SvgloadBuffer(document);
        return longEdge / (double)Math.Max(header.Width, header.Height);
    }

    /// <summary>
    /// Loads and renders a document with libvips <c>svgload</c> into native memory, then frees the pixels.
    /// </summary>
    /// <remarks>
    /// libvips loads lazily. <c>WriteToMemory</c> forces the render of every pixel. The native buffer
    /// keeps the w x h x 4 byte output out of the managed allocation figures, as for Skia surfaces.
    /// </remarks>
    /// <param name="document">The SVG bytes.</param>
    /// <param name="scale">The scale factor from <see cref="GetNetVipsScale"/>.</param>
    public static void RenderNetVips(byte[] document, double scale)
    {
        using VipsImage image = VipsImage.SvgloadBuffer(document, scale: scale);
        nint pixels = image.WriteToMemory(out ulong _);
        Vips.Free(pixels);
    }

    /// <summary>
    /// Prints the versions and the configuration of the reference renderers, and renders one document
    /// with each of them to prove that the native libraries load.
    /// </summary>
    public static void PrintInfo()
    {
        int defaultConcurrency = ConfigureNetVips();
        SvgBenchmarkFonts.CheckFontConfig();
        Console.WriteLine($"libvips          {Vips.Version(0)}.{Vips.Version(1)}.{Vips.Version(2)}");
        Console.WriteLine($"svgload_buffer   {(Vips.TypeFind("VipsOperation", "svgload_buffer") != IntPtr.Zero ? "available" : "missing")}");
        Console.WriteLine($"libvips threads  {defaultConcurrency} by default");
        Console.WriteLine($"libvips cache    max {VipsCache.Max} operations, {VipsCache.MaxMem} bytes, {VipsCache.MaxFiles} files");
        Console.WriteLine($"SkiaSharp native {SkiaSharpVersion.Native}");

        byte[] tiger = SvgBenchmarkCorpus.ReadDocuments("Tiger")[0];
        using (SKSvg svg = LoadSkia(tiger))
        {
            SKRect bounds = svg.Picture!.CullRect;
            Console.WriteLine($"Svg.Skia         Tiger loaded, picture {bounds.Width} x {bounds.Height}");
        }

        using VipsImage image = VipsImage.SvgloadBuffer(tiger);
        nint pixels = image.WriteToMemory(out ulong size);
        Vips.Free(pixels);
        Console.WriteLine($"NetVips          Tiger rendered, {image.Width} x {image.Height} x {image.Bands} bands, {size} bytes");
    }

    /// <summary>
    /// Renders every corpus entry with each reference renderer and with ImageSharp.Drawing at one size and writes PNG
    /// files, so that the outputs can be compared with each other and with the Chromium oracle in
    /// <c>tests/SvgTools</c> before benchmark numbers count.
    /// </summary>
    /// <remarks>
    /// ImageSharp.Drawing renders each document twice, on a new canvas and through a retained scene, as the
    /// <see cref="RenderSvg"/> methods do. The two outputs must be equal; the method reports whether they are.
    /// </remarks>
    /// <param name="longEdge">The output length of the longer side, in pixels.</param>
    public static void VerifyOutput(int longEdge)
    {
        ConfigureNetVips();
        SvgBenchmarkFonts.CheckFontConfig();
        string outDir = IOPath.Combine(AppContext.BaseDirectory, $"svg-{longEdge}-verify");
        Directory.CreateDirectory(outDir);

        foreach (string name in SvgBenchmarkCorpus.Names)
        {
            string[] files = SvgBenchmarkCorpus.GetFiles(name);
            byte[][] documents = SvgBenchmarkCorpus.ReadDocuments(name);
            for (int i = 0; i < documents.Length; i++)
            {
                string stem = IOPath.GetFileNameWithoutExtension(files[i]);

                // The exact input bytes, so that the oracle renders what the renderers rendered.
                File.WriteAllBytes(IOPath.Combine(outDir, $"{stem}.svg"), documents[i]);

                using (SKSvg svg = LoadSkia(documents[i]))
                {
                    SKSizeI size = GetSkiaOutputSize(svg.Picture!, longEdge, out float scale);
                    using SKSurface surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height));
                    surface.Canvas.Clear(SKColors.Transparent);
                    DrawSkia(surface.Canvas, svg.Picture!, scale);
                    using SKImage snapshot = surface.Snapshot();
                    using SKData data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
                    using FileStream stream = File.Create(IOPath.Combine(outDir, $"{stem}-svgskia.png"));
                    data.SaveTo(stream);
                    SKRect bounds = svg.Picture!.CullRect;
                    Console.WriteLine($"{name,-16} {stem,-28} Svg.Skia {size.Width} x {size.Height} (intrinsic {bounds.Width} x {bounds.Height})");
                }

                double vipsScale = GetNetVipsScale(documents[i], longEdge);
                using VipsImage image = VipsImage.SvgloadBuffer(documents[i], scale: vipsScale);
                image.WriteToFile(IOPath.Combine(outDir, $"{stem}-netvips.png"));
                Console.WriteLine($"{name,-16} {stem,-28} NetVips  {image.Width} x {image.Height} (scale {vipsScale:0.######})");

                SvgDocument document = SvgImageSharpRenderer.Load(documents[i]);
                Size isSize = SvgImageSharpRenderer.GetOutputSize(document, longEdge, out RectangleF destination);
                using Image<Rgba32> fresh = new(isSize.Width, isSize.Height);
                fresh.Mutate(c => c.Paint(canvas => canvas.DrawSvg(document, destination)));
                fresh.SaveAsPng(IOPath.Combine(outDir, $"{stem}-imagesharp.png"));

                using Image<Rgba32> retained = new(isSize.Width, isSize.Height);
                DrawingBackendScene scene;
                using (DrawingCanvas canvas = retained.Frames.RootFrame.CreateCanvas(retained.Configuration, new DrawingOptions()))
                {
                    canvas.DrawSvg(document, destination);
                    scene = canvas.CreateScene();
                }

                retained.Mutate(c => c.Paint(canvas => canvas.RenderScene(scene)));
                scene.Dispose();

                // Large images do not have one contiguous buffer, so the rows are compared one by one.
                bool equal = true;
                fresh.ProcessPixelRows(retained, (a, b) =>
                {
                    for (int y = 0; y < a.Height && equal; y++)
                    {
                        equal = a.GetRowSpan(y).SequenceEqual(b.GetRowSpan(y));
                    }
                });

                string sceneResult = equal ? "equal" : "DIFFERENT";
                Console.WriteLine(
                    $"{name,-16} {stem,-28} ImageSharp {isSize.Width} x {isSize.Height} " +
                    $"(intrinsic {document.Size.Width} x {document.Size.Height}), retained scene {sceneResult}");
            }
        }

        Console.WriteLine($"Output saved to: {outDir}");
    }
}
