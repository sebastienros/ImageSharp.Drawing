// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests <see cref="SvgLoadOptions.CacheScenes"/>: a document drawn again with the same state replays the scene that it
/// kept from the second draw. Every draw must give the pixels of a draw of the same document loaded without the cache,
/// which is the requirement of the option; the internal cache tells whether a scene was kept. The tests run alone: the
/// cache counts the bytes of a scene from the managed allocations of the whole process while it records.
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgSceneCacheTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\"";

    /// <summary>
    /// The font collection of the text documents: Open Sans only.
    /// </summary>
    private static readonly Lazy<FontCollection> Fonts = new(() =>
    {
        FontCollection collection = new();
        _ = collection.Add(TestFontUtilities.GetPath("OpenSans-Regular.ttf"));
        return collection;
    });

    /// <summary>
    /// Gets every feature document in <c>tests/Images/Input/Svg/Features</c>.
    /// </summary>
    public static TheoryData<string> FeatureDocuments
    {
        get
        {
            TheoryData<string> data = [];
            foreach (string path in Directory.GetFiles(TestFile.GetInputFileFullPath("Svg/Features"), "*.svg").Order(StringComparer.Ordinal))
            {
                data.Add(IOPath.GetFileNameWithoutExtension(path));
            }

            return data;
        }
    }

    /// <summary>
    /// The documents with content that a scene owns or samples: patterns, images, filter results on whole pixels and under
    /// a rotation, masks (apply barriers), text, and group opacity (layers).
    /// </summary>
    private static readonly string[] ContentDocuments =
        ["pattern", "image", "filter-drop-shadow", "filter-blur", "filter-transform", "mask", "text-layout", "opacity"];

    /// <summary>
    /// Every feature document drawn five times on new canvases equals a draw without the cache, and the document keeps one
    /// scene from the second draw on.
    /// </summary>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [MemberData(nameof(FeatureDocuments))]
    public void RepeatedDraws_EqualTheUncachedDraw(string name)
    {
        SvgDocument cached = Load(name, cache: true);
        SvgDocument uncached = Load(name, cache: false);
        RectangleF destination = new(0, 0, 200, 200);

        using Image<Rgba32> expected = Draw<Rgba32>(uncached, 200, 200, canvas => canvas.DrawSvg(uncached, destination));
        for (int i = 0; i < 5; i++)
        {
            using Image<Rgba32> actual = Draw<Rgba32>(cached, 200, 200, canvas => canvas.DrawSvg(cached, destination));
            ImageComparer.Exact.VerifySimilarity(expected, actual);
            Assert.Equal(i == 0 ? 0 : 1, cached.SceneCache!.KeptSceneCount);
        }
    }

    /// <summary>
    /// The documents with owned and sampled content, in several pixel formats, equal a draw without the cache on every one
    /// of four draws, also when the canvas of an earlier draw is disposed and its pooled buffers are reused.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32 | PixelTypes.Bgra32 | PixelTypes.Rgb24 | PixelTypes.Rgba64 | PixelTypes.RgbaVector | PixelTypes.L8)]
    public void RepeatedDraws_EqualTheUncachedDrawInEveryPixelFormat<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        foreach (string name in ContentDocuments)
        {
            SvgDocument cached = Load(name, cache: true);
            SvgDocument uncached = Load(name, cache: false);
            using Image<TPixel> expected = provider.GetImage();
            expected.Mutate(x => x.Paint(canvas => canvas.DrawSvg(uncached, PointF.Empty)));

            for (int i = 0; i < 4; i++)
            {
                using Image<TPixel> actual = provider.GetImage();
                actual.Mutate(x => x.Paint(canvas => canvas.DrawSvg(cached, PointF.Empty)));
                ImageComparer.Exact.VerifySimilarity(expected, actual);
                OverwritePooledBuffers(actual.Configuration.MemoryAllocator);
            }

            Assert.Equal(1, cached.SceneCache!.KeptSceneCount);
        }
    }

    /// <summary>
    /// Gets placements that change the scene: destinations at fractional positions and scales, a canvas transform that
    /// rotates, a projective transform, and graphics options of the caller.
    /// </summary>
    public static TheoryData<string> Placements { get; } = ["offset", "scaled", "rotated", "projective", "blend", "multiply", "aliased", "src"];

    /// <summary>
    /// Each placement keeps its own scene, and the draws of every placement in turn equal the draws without the cache.
    /// </summary>
    /// <param name="placement">The placement.</param>
    [Theory]
    [MemberData(nameof(Placements))]
    public void Placements_EqualTheUncachedDraw(string placement)
    {
        (RectangleF destination, DrawingOptions options) = GetPlacement(placement);
        foreach (string name in ContentDocuments)
        {
            SvgDocument cached = Load(name, cache: true);
            SvgDocument uncached = Load(name, cache: false);
            using Image<Rgba32> expected = Draw<Rgba32>(uncached, 220, 220, canvas => canvas.DrawSvg(uncached, destination), options);
            for (int i = 0; i < 3; i++)
            {
                using Image<Rgba32> actual = Draw<Rgba32>(cached, 220, 220, canvas => canvas.DrawSvg(cached, destination), options);
                ImageComparer.Exact.VerifySimilarity(expected, actual);
            }

            Assert.Equal(1, cached.SceneCache!.KeptSceneCount);
        }
    }

    /// <summary>
    /// A kept scene is not used for another state: one document drawn in turn with four placements gives each placement
    /// its own pixels. Two of them differ only in the canvas transform and two only in the graphics options.
    /// </summary>
    [Fact]
    public void PlacementsInTurn_EachEqualsItsUncachedDraw()
    {
        SvgDocument cached = Load("text-layout", cache: true);
        SvgDocument uncached = Load("text-layout", cache: false);
        string[] placements = ["rotated", "projective", "blend", "multiply"];
        Image<Rgba32>[] expected = new Image<Rgba32>[placements.Length];
        try
        {
            for (int p = 0; p < placements.Length; p++)
            {
                (RectangleF destination, DrawingOptions options) = GetPlacement(placements[p]);
                expected[p] = Draw<Rgba32>(uncached, 220, 220, canvas => canvas.DrawSvg(uncached, destination), options);
            }

            for (int round = 0; round < 3; round++)
            {
                for (int p = 0; p < placements.Length; p++)
                {
                    (RectangleF destination, DrawingOptions options) = GetPlacement(placements[p]);
                    using Image<Rgba32> actual = Draw<Rgba32>(cached, 220, 220, canvas => canvas.DrawSvg(cached, destination), options);
                    ImageComparer.Exact.VerifySimilarity(expected[p], actual);
                }
            }

            Assert.Equal(SvgSceneCache.MaxScenes, cached.SceneCache!.KeptSceneCount);
        }
        finally
        {
            foreach (Image<Rgba32> image in expected)
            {
                image?.Dispose();
            }
        }
    }

    /// <summary>
    /// The caller can change its graphics options object after a draw. A kept scene keeps the values of its draw: after
    /// the object of the recorded draws changes, a draw with another object that has the recorded values equals the
    /// uncached draw with those values. Both documents draw shapes outside every layer, which use these options directly.
    /// </summary>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [InlineData("shapes")]
    [InlineData("text-layout")]
    public void ChangedGraphicsOptionsObject_DoesNotChangeTheKeptScene(string name)
    {
        SvgDocument cached = Load(name, cache: true);
        SvgDocument uncached = Load(name, cache: false);
        GraphicsOptions recorded = new() { BlendPercentage = 0.75F, ColorBlendingMode = PixelColorBlendingMode.Multiply };
        DrawingOptions recordedOptions = new() { GraphicsOptions = recorded };
        DrawingOptions sameValues = new() { GraphicsOptions = new GraphicsOptions { BlendPercentage = 0.75F, ColorBlendingMode = PixelColorBlendingMode.Multiply } };
        using Image<Rgba32> expected = Draw<Rgba32>(uncached, 200, 200, canvas => canvas.DrawSvg(uncached, PointF.Empty), sameValues);

        // The second draw records the scene.
        for (int i = 0; i < 2; i++)
        {
            using (Draw<Rgba32>(cached, 200, 200, canvas => canvas.DrawSvg(cached, PointF.Empty), recordedOptions))
            {
            }
        }

        recorded.BlendPercentage = 0.25F;
        recorded.ColorBlendingMode = PixelColorBlendingMode.Normal;
        for (int i = 0; i < 2; i++)
        {
            using Image<Rgba32> actual = Draw<Rgba32>(cached, 200, 200, canvas => canvas.DrawSvg(cached, PointF.Empty), sameValues);
            ImageComparer.Exact.VerifySimilarity(expected, actual);
        }

        Assert.Equal(1, cached.SceneCache!.KeptSceneCount);
    }

    /// <summary>
    /// Gets the canvas states in which a document is drawn: after other commands, under a canvas transform set with
    /// <see cref="DrawingCanvas.Save(DrawingOptions)"/>, inside a clip, inside a layer, in a region, several times on one
    /// canvas, and on a canvas that is sealed into a scene and replayed.
    /// </summary>
    public static TheoryData<string> CanvasStates { get; } = ["after-commands", "saved-transform", "clip", "layer", "region", "three-on-one-canvas", "recorded", "recorded-and-dropped"];

    /// <summary>
    /// Every canvas state gives the pixels of the uncached draw on every one of four draws. A clip, a layer, and a region
    /// keep no scene, because a retained scene does not render there as the commands do. In the last state the document
    /// drops its scene after each draw that kept one, before the outer scene replays.
    /// </summary>
    /// <param name="state">The canvas state.</param>
    [Theory]
    [MemberData(nameof(CanvasStates))]
    public void CanvasStates_EqualTheUncachedDraw(string state)
    {
        foreach (string name in ContentDocuments)
        {
            SvgDocument cached = Load(name, cache: true);
            SvgDocument uncached = Load(name, cache: false);
            using Image<Rgba32> expected = DrawInState(uncached, state);
            for (int i = 0; i < 4; i++)
            {
                using Image<Rgba32> actual = DrawInState(cached, state);
                ImageComparer.Exact.VerifySimilarity(expected, actual);
            }

            int expectedScenes = state is "clip" or "layer" or "region" or "recorded-and-dropped" ? 0 : 1;
            Assert.Equal(expectedScenes, cached.SceneCache!.KeptSceneCount);
        }
    }

    /// <summary>
    /// A document drawn at two sizes in turn keeps a scene for each, and at more sizes than the cache holds it keeps the
    /// most recently used ones, at most <see cref="SvgSceneCache.MaxScenes"/>; every draw equals the uncached draw.
    /// </summary>
    [Fact]
    public void SizesInTurn_KeepAtMostTheBoundAndStayEqual()
    {
        SvgDocument cached = Load("filter-drop-shadow", cache: true);
        SvgDocument uncached = Load("filter-drop-shadow", cache: false);
        RectangleF[] destinations = [new(0, 0, 200, 200), new(0, 0, 120, 120), new(10, 10, 150, 150), new(0, 0, 90, 180), new(5, 5, 60, 60), new(0, 0, 199, 199)];

        for (int sizes = 2; sizes <= destinations.Length; sizes += 4)
        {
            cached.SceneCache = new SvgSceneCache();
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < sizes; i++)
                {
                    RectangleF destination = destinations[i];
                    using Image<Rgba32> expected = Draw<Rgba32>(uncached, 200, 200, canvas => canvas.DrawSvg(uncached, destination));
                    using Image<Rgba32> actual = Draw<Rgba32>(cached, 200, 200, canvas => canvas.DrawSvg(cached, destination));
                    ImageComparer.Exact.VerifySimilarity(expected, actual);
                }

                Assert.True(cached.SceneCache.KeptSceneCount <= SvgSceneCache.MaxScenes);
                Assert.True(cached.SceneCache.KeptBytes <= SvgSceneCache.MaxBytes);
            }

            Assert.Equal(Math.Min(sizes, SvgSceneCache.MaxScenes), cached.SceneCache.KeptSceneCount);
        }
    }

    /// <summary>
    /// The byte bound holds: with a bound that fits one scene, a second scene drops the first, and a scene above the bound
    /// is not kept and not recorded again. Every draw equals the uncached draw.
    /// </summary>
    [Fact]
    public void ByteBound_DropsAndRejectsScenes()
    {
        SvgDocument cached = Load("text-layout", cache: true);
        SvgDocument uncached = Load("text-layout", cache: false);
        RectangleF large = new(0, 0, 200, 200);
        RectangleF small = new(0, 0, 100, 100);

        // The counted bytes of the scenes of each size. A count can vary a little between recordings (it counts the
        // managed allocations of the recording), so each size records three times.
        (long largeMin, long largeMax) = CountBytes(cached, large);
        (long smallMin, long smallMax) = CountBytes(cached, small);
        Assert.True(largeMin > 0 && smallMin > 0);

        // A bound that holds either scene but not both.
        long bound = Math.Max(largeMax, smallMax) + (Math.Min(largeMin, smallMin) / 2);
        Assert.True(bound < largeMin + smallMin);
        cached.SceneCache = new SvgSceneCache(SvgSceneCache.MaxScenes, bound);
        for (int round = 0; round < 3; round++)
        {
            foreach (RectangleF destination in new[] { large, small })
            {
                using Image<Rgba32> expected = Draw<Rgba32>(uncached, 200, 200, canvas => canvas.DrawSvg(uncached, destination));
                using Image<Rgba32> actual = Draw<Rgba32>(cached, 200, 200, canvas => canvas.DrawSvg(cached, destination));
                ImageComparer.Exact.VerifySimilarity(expected, actual);
                Assert.True(cached.SceneCache.KeptBytes <= bound);
                Assert.True(cached.SceneCache.KeptSceneCount <= 1);
            }
        }

        // A bound below one scene keeps nothing.
        cached.SceneCache = new SvgSceneCache(SvgSceneCache.MaxScenes, Math.Min(largeMin, smallMin) / 2);
        for (int i = 0; i < 4; i++)
        {
            using Image<Rgba32> expected = Draw<Rgba32>(uncached, 200, 200, canvas => canvas.DrawSvg(uncached, small));
            using Image<Rgba32> actual = Draw<Rgba32>(cached, 200, 200, canvas => canvas.DrawSvg(cached, small));
            ImageComparer.Exact.VerifySimilarity(expected, actual);
            Assert.Equal(0, cached.SceneCache.KeptSceneCount);
            Assert.Equal(0, cached.SceneCache.KeptBytes);
        }

        static (long Min, long Max) CountBytes(SvgDocument document, RectangleF destination)
        {
            long min = long.MaxValue;
            long max = 0;
            for (int recording = 0; recording < 3; recording++)
            {
                document.SceneCache = new SvgSceneCache();
                for (int i = 0; i < 2; i++)
                {
                    using (Draw<Rgba32>(document, 200, 200, canvas => canvas.DrawSvg(document, destination)))
                    {
                    }
                }

                min = Math.Min(min, document.SceneCache.KeptBytes);
                max = Math.Max(max, document.SceneCache.KeptBytes);
            }

            return (min, max);
        }
    }

    /// <summary>
    /// Concurrent draws of one document at two sizes and in two pixel formats equal the uncached draws, and the document
    /// keeps one scene for each state.
    /// </summary>
    [Fact]
    public void ConcurrentDraws_EqualTheUncachedDraw()
    {
        foreach (string name in new[] { "filter-blur", "mask", "text-layout" })
        {
            SvgDocument cached = Load(name, cache: true);
            SvgDocument uncached = Load(name, cache: false);
            RectangleF[] destinations = [new(0, 0, 200, 200), new(10, 10, 150, 150)];
            Image<Rgba32>[] expected = [.. destinations.Select(d => Draw<Rgba32>(uncached, 200, 200, canvas => canvas.DrawSvg(uncached, d)))];
            Image<Bgra32>[] expectedBgra = [.. destinations.Select(d => Draw<Bgra32>(uncached, 200, 200, canvas => canvas.DrawSvg(uncached, d)))];
            try
            {
                Parallel.For(
                    0,
                    64,
                    new ParallelOptions { MaxDegreeOfParallelism = 8 },
                    i =>
                    {
                        RectangleF destination = destinations[i % 2];
                        if ((i & 2) == 0)
                        {
                            using Image<Rgba32> actual = Draw<Rgba32>(cached, 200, 200, canvas => canvas.DrawSvg(cached, destination));
                            ImageComparer.Exact.VerifySimilarity(expected[i % 2], actual);
                        }
                        else
                        {
                            using Image<Bgra32> actual = Draw<Bgra32>(cached, 200, 200, canvas => canvas.DrawSvg(cached, destination));
                            ImageComparer.Exact.VerifySimilarity(expectedBgra[i % 2], actual);
                        }
                    });

                Assert.Equal(4, cached.SceneCache!.KeptSceneCount);
            }
            finally
            {
                foreach (Image image in expected.Concat<Image>(expectedBgra))
                {
                    image.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// With the option turned off the document keeps no scene, and every draw equals the first.
    /// </summary>
    [Fact]
    public void OptionOff_KeepsNothing()
    {
        SvgLoadOptions defaults = new();
        Assert.True(defaults.CacheScenes);

        SvgDocument document = Load("filter-blur", cache: false);
        Assert.Null(document.SceneCache);

        using Image<Rgba32> first = Draw<Rgba32>(document, 200, 200, canvas => canvas.DrawSvg(document, PointF.Empty));
        for (int i = 0; i < 3; i++)
        {
            using Image<Rgba32> next = Draw<Rgba32>(document, 200, 200, canvas => canvas.DrawSvg(document, PointF.Empty));
            ImageComparer.Exact.VerifySimilarity(first, next);
        }

        Assert.Null(document.SceneCache);

        // The default keeps a scene from the second draw on, and the option passes through a parse too.
        SvgDocument parsed = SvgDocument.Parse(SvgOpen + " width=\"20\" height=\"20\"><rect width=\"10\" height=\"10\"/></svg>");
        using (Draw<Rgba32>(parsed, 20, 20, canvas => canvas.DrawSvg(parsed, PointF.Empty)))
        {
        }

        Assert.Equal(0, parsed.SceneCache!.KeptSceneCount);
        using (Draw<Rgba32>(parsed, 20, 20, canvas => canvas.DrawSvg(parsed, PointF.Empty)))
        {
        }

        Assert.Equal(1, parsed.SceneCache.KeptSceneCount);
        Assert.Null(SvgDocument.Parse(SvgOpen + "/>", new SvgLoadOptions { CacheScenes = false }).SceneCache);
    }

    /// <summary>
    /// <see cref="SvgDocument.Render{TPixel}(Configuration, Size, Color)"/> uses the cache as a draw does: repeated renders
    /// equal the uncached render, and another configuration keeps its own scene.
    /// </summary>
    [Fact]
    public void Render_UsesTheCacheAndEqualsTheUncachedRender()
    {
        SvgDocument cached = Load("filter-transform", cache: true);
        SvgDocument uncached = Load("filter-transform", cache: false);
        Configuration oneThread = Configuration.Default.Clone();
        oneThread.MaxDegreeOfParallelism = 1;

        using Image<Rgba32> expected = uncached.Render<Rgba32>(Configuration.Default, new Size(150, 150), Color.White);
        using Image<Rgba32> expectedOneThread = uncached.Render<Rgba32>(oneThread, new Size(150, 150), Color.White);
        for (int i = 0; i < 3; i++)
        {
            using Image<Rgba32> actual = cached.Render<Rgba32>(Configuration.Default, new Size(150, 150), Color.White);
            ImageComparer.Exact.VerifySimilarity(expected, actual);
            using Image<Rgba32> actualOneThread = cached.Render<Rgba32>(oneThread, new Size(150, 150), Color.White);
            ImageComparer.Exact.VerifySimilarity(expectedOneThread, actualOneThread);
        }

        Assert.Equal(2, cached.SceneCache!.KeptSceneCount);
    }

    /// <summary>
    /// An allocator with an accumulative limit, or one that ImageSharp does not provide, accounts for the memory of every
    /// draw, so the document keeps no scene with it.
    /// </summary>
    [Fact]
    public void AllocatorsWithOwnRules_KeepNothing()
    {
        SvgDocument document = Load("pattern", cache: true);
        Configuration limited = Configuration.Default.Clone();
        limited.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions { AccumulativeAllocationLimitMegabytes = 1024 });
        Configuration custom = Configuration.Default.Clone();
        custom.MemoryAllocator = new SvgSceneAllocator(MemoryAllocator.Default);

        foreach (Configuration configuration in new[] { limited, custom })
        {
            for (int i = 0; i < 3; i++)
            {
                using Image<Rgba32> image = document.Render<Rgba32>(configuration, new Size(100, 100), Color.Transparent);
            }
        }

        Assert.Equal(0, document.SceneCache!.KeptSceneCount);
    }

    /// <summary>
    /// A document whose layers exceed <see cref="SvgLimits.MaxLayerPixels"/> fails on every draw, with the cache as without
    /// it, and leaves the canvas unchanged.
    /// </summary>
    [Fact]
    public void LayerLimit_HoldsOnEveryDraw()
    {
        const string groups = "<rect width=\"200\" height=\"100\" fill=\"red\"/><g opacity=\"0.5\"><rect x=\"10\" y=\"10\" width=\"50\" height=\"40\"/>"
            + "<rect x=\"20\" y=\"20\" width=\"50\" height=\"40\"/></g>";
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + " width=\"200\" height=\"100\">" + groups + "</svg>",
            new SvgLoadOptions { Limits = new SvgLimits { MaxLayerPixels = 100 } });

        for (int i = 0; i < 4; i++)
        {
            using Image<Rgba32> image = new(200, 100, new Rgba32(1, 2, 3, 255));
            Assert.Throws<InvalidImageContentException>(() => image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty))));
            Assert.Equal(new Rgba32(1, 2, 3, 255), image[100, 50]);
        }

        Assert.Equal(0, document.SceneCache!.KeptSceneCount);
    }

    /// <summary>
    /// Loads a feature document.
    /// </summary>
    /// <param name="name">The file name without extension.</param>
    /// <param name="cache">Whether the document keeps scenes.</param>
    /// <returns>The document.</returns>
    private static SvgDocument Load(string name, bool cache)
        => SvgDocument.Load(
            TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"),
            new SvgLoadOptions
            {
                FontCollection = Fonts.Value,
                DefaultFontFamily = "Open Sans",
                ResourceBaseDirectory = TestFile.GetInputFileFullPath("Svg/Features"),
                CacheScenes = cache,
            });

    /// <summary>
    /// Draws on a new canvas over a new transparent image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="document">The document, which only names the test in a failure.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    /// <param name="draw">The drawing.</param>
    /// <param name="options">The drawing options of the canvas, or <see langword="null"/> for the defaults.</param>
    /// <returns>The image.</returns>
    private static Image<TPixel> Draw<TPixel>(SvgDocument document, int width, int height, CanvasAction draw, DrawingOptions options = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        _ = document;
        Image<TPixel> image = new(width, height);
        image.Mutate(x => x.Paint(options ?? new DrawingOptions(), draw));
        return image;
    }

    /// <summary>
    /// Returns the destination and the canvas options of a placement.
    /// </summary>
    /// <param name="placement">The placement.</param>
    /// <returns>The destination and the options.</returns>
    private static (RectangleF Destination, DrawingOptions Options) GetPlacement(string placement) => placement switch
    {
        "offset" => (new RectangleF(10.25F, 7.5F, 200, 200), new DrawingOptions()),
        "scaled" => (new RectangleF(3.3F, 0, 150.7F, 211.9F), new DrawingOptions()),
        "rotated" => (new RectangleF(0, 0, 160, 160), new DrawingOptions
        {
            Transform = new Matrix4x4(Matrix3x2.CreateRotation(0.3F) * Matrix3x2.CreateTranslation(50F, 0F)),
        }),
        "projective" => (new RectangleF(0, 0, 160, 160), new DrawingOptions
        {
            Transform = new Matrix4x4(1F, 0F, 0F, 0.0008F, 0.1F, 1F, 0F, 0.0005F, 0F, 0F, 1F, 0F, 20F, 10F, 0F, 1F),
        }),
        "blend" => (new RectangleF(0, 0, 200, 200), new DrawingOptions { GraphicsOptions = new GraphicsOptions { BlendPercentage = 0.6F } }),
        "multiply" => (new RectangleF(0, 0, 200, 200), new DrawingOptions { GraphicsOptions = new GraphicsOptions { ColorBlendingMode = PixelColorBlendingMode.Multiply } }),
        "aliased" => (new RectangleF(0, 0, 200, 200), new DrawingOptions { GraphicsOptions = new GraphicsOptions { Antialias = false } }),
        "src" => (new RectangleF(0, 0, 200, 200), new DrawingOptions { GraphicsOptions = new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.Src } }),
        _ => throw new ArgumentOutOfRangeException(nameof(placement)),
    };

    /// <summary>
    /// Draws a document in a canvas state over a background with content, so that every state composites over pixels.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="state">The canvas state.</param>
    /// <returns>The image.</returns>
    private static Image<Rgba32> DrawInState(SvgDocument document, string state)
    {
        Image<Rgba32> image = new(200, 200, new Rgba32(40, 80, 120, 200));
        Brush stripe = Brushes.Solid(Color.Orange.WithAlpha(0.5F));
        RectangleF destination = new(0, 0, 200, 200);
        DrawingBackendScene outer = null;
        image.Mutate(x => x.Paint(canvas =>
        {
            canvas.Fill(stripe, new RectanglePolygon(20, 0, 30, 200));
            switch (state)
            {
                case "after-commands":
                    canvas.DrawSvg(document, destination);
                    canvas.Fill(stripe, new RectanglePolygon(0, 100, 200, 20));
                    break;

                case "saved-transform":
                    _ = canvas.Save(new DrawingOptions { Transform = Matrix4x4.CreateScale(0.8F) * Matrix4x4.CreateTranslation(15.5F, 3F, 0F) });
                    canvas.DrawSvg(document, destination);
                    canvas.Restore();
                    break;

                case "clip":
                    _ = canvas.Save();
                    canvas.Clip(new EllipsePolygon(100, 100, 80));
                    canvas.DrawSvg(document, destination);
                    canvas.Restore();
                    break;

                case "layer":
                    _ = canvas.SaveLayer(new GraphicsOptions { BlendPercentage = 0.7F }, new Rectangle(10, 10, 170, 180));
                    canvas.DrawSvg(document, destination);
                    canvas.Restore();
                    break;

                case "region":
                    using (DrawingCanvas region = canvas.CreateRegion(new Rectangle(15, 25, 160, 150)))
                    {
                        region.DrawSvg(document, destination);
                    }

                    break;

                case "three-on-one-canvas":
                    canvas.DrawSvg(document, destination);
                    canvas.Fill(stripe, new RectanglePolygon(0, 50, 200, 10));
                    canvas.DrawSvg(document, destination);
                    canvas.DrawSvg(document, destination);
                    break;

                case "recorded":
                case "recorded-and-dropped":
                    canvas.DrawSvg(document, destination);
                    outer = canvas.CreateScene();
                    break;
            }
        }));

        if (outer is not null)
        {
            // The outer scene holds the rows of the kept scene. Dropping the kept scenes of the document, collecting
            // garbage, and reusing pooled buffers before the replay must not change what the outer scene draws.
            if (state == "recorded-and-dropped" && document.SceneCache?.KeptSceneCount > 0)
            {
                document.SceneCache = new SvgSceneCache();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            OverwritePooledBuffers(image.Configuration.MemoryAllocator);
            image.Mutate(x => x.Paint(canvas => canvas.RenderScene(outer)));
            outer.Dispose();
        }

        return image;
    }

    /// <summary>
    /// Rents buffers of many sizes from an allocator, fills them, and returns them, so that a buffer that a scene still
    /// used after it went back to the pool would be overwritten.
    /// </summary>
    /// <param name="allocator">The allocator.</param>
    private static void OverwritePooledBuffers(MemoryAllocator allocator)
    {
        for (int length = 64; length <= 200 * 200 * 4; length *= 2)
        {
            System.Buffers.IMemoryOwner<Rgba32>[] owners = new System.Buffers.IMemoryOwner<Rgba32>[4];
            for (int i = 0; i < owners.Length; i++)
            {
                owners[i] = allocator.Allocate<Rgba32>(length - i);
                owners[i].Memory.Span.Fill(new Rgba32(255, 0, 255, 128));
            }

            foreach (System.Buffers.IMemoryOwner<Rgba32> owner in owners)
            {
                owner.Dispose();
            }
        }
    }
}
