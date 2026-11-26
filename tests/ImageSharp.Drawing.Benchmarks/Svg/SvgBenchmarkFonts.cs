// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests;
using SkiaSharp;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// The font files of the SVG benchmarks. Every renderer draws the text of the corpus with these files, and the corpus
/// documents name their families, so no output depends on the fonts of the machine.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>ImageSharp.Drawing loads them into the font collection of its load options.</description></item>
/// <item><description>Svg.Skia gets a typeface provider for each file. The SkiaSharp native assets of the benchmarks
/// are built without fontconfig, so this is the way to give Svg.Skia a font.</description></item>
/// <item><description>NetVips (librsvg through Pango) finds fonts through fontconfig: run the benchmarks with
/// <c>FONTCONFIG_FILE</c> set to <c>tests/ImageSharp.Drawing.Benchmarks/Svg/fonts.conf</c>, which offers these files and
/// no other font. <see cref="CheckFontConfig"/> warns when the variable is missing.</description></item>
/// </list>
/// </remarks>
internal static class SvgBenchmarkFonts
{
    /// <summary>
    /// The font files, relative to the repository root. <c>fonts.conf</c> lists the same files.
    /// </summary>
    private static readonly string[] RelativePaths = ["tests/ImageSharp.Drawing.Tests/TestFonts/OpenSans-Regular.ttf"];

    /// <summary>
    /// The font collection of ImageSharp.Drawing, loaded once.
    /// </summary>
    private static readonly Lazy<FontCollection> Collection = new(() =>
    {
        FontCollection collection = new();
        foreach (string path in RelativePaths)
        {
            _ = collection.Add(TestEnvironment.GetFullPath(path));
        }

        return collection;
    });

    /// <summary>
    /// The typeface providers of Svg.Skia, created once, so that a load does not pay for reading the font files.
    /// </summary>
    private static readonly Lazy<ITypefaceProvider[]> SkiaProviders = new(() =>
    {
        ITypefaceProvider[] providers = new ITypefaceProvider[RelativePaths.Length];
        for (int i = 0; i < providers.Length; i++)
        {
            using FileStream stream = File.OpenRead(TestEnvironment.GetFullPath(RelativePaths[i]));
            providers[i] = new CustomTypefaceProvider(stream);
        }

        return providers;
    });

    /// <summary>
    /// Creates the load options of ImageSharp.Drawing: the default options with the font collection of the benchmarks.
    /// </summary>
    /// <returns>The options.</returns>
    public static SvgLoadOptions CreateLoadOptions() => new() { FontCollection = Collection.Value };

    /// <summary>
    /// Puts the typeface providers of the benchmark fonts first in the settings of an Svg.Skia document, before it loads.
    /// </summary>
    /// <param name="svg">The Svg.Skia document.</param>
    public static void Configure(SKSvg svg)
    {
        IList<ITypefaceProvider> providers = svg.Settings.TypefaceProviders ??= [];
        ITypefaceProvider[] fonts = SkiaProviders.Value;
        for (int i = 0; i < fonts.Length; i++)
        {
            providers.Insert(i, fonts[i]);
        }
    }

    /// <summary>
    /// Writes a warning when <c>FONTCONFIG_FILE</c> is not set, because NetVips then draws text with the fonts of the
    /// machine.
    /// </summary>
    public static void CheckFontConfig()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FONTCONFIG_FILE")))
        {
            Console.WriteLine(
                "Warning: FONTCONFIG_FILE is not set. NetVips draws the text of the corpus with the fonts of the machine; " +
                "set it to tests/ImageSharp.Drawing.Benchmarks/Svg/fonts.conf.");
        }
    }
}
