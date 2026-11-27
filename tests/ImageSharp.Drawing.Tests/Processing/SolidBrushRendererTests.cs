// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

/// <summary>
/// Checks that <see cref="SolidBrush"/> gives the same pixels as one blend of every coverage row.
/// </summary>
public class SolidBrushRendererTests
{
    /// <summary>
    /// The colors of the scenes: opaque, translucent down to the smallest 8-bit and 16-bit alpha, and transparent with
    /// and without color channels, so that a full-coverage write of the wrong pixel shows for every alpha class.
    /// </summary>
    private static readonly Color[] Colors =
    [
        Color.HotPink,
        Color.ParseHex("#1E90FF"),
        Color.Black,
        Color.White,
        Color.ParseHex("#2E8B5780"),
        Color.ParseHex("#FF000001"),
        Color.ParseHex("#00000000"),
        Color.ParseHex("#7F3FBFFE"),
        Color.ParseHex("#FFFFFF00"),
        Color.ParseHex("#C0408002"),
        Color.FromPixel(new Rgba64(65535, 30000, 1000, 1)),
        Color.FromPixel(new Rgba64(20000, 65535, 40000, 300)),
    ];

    public static TheoryData<string> OptionNames { get; } =
    [
        "Default",
        "Aliased",
        "HalfOpacity",
        "Multiply",
        "SrcAtop",
        "Src",
    ];

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_Rgba32(string optionName) => AssertMatchesWholeRowBlend<Rgba32>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_Bgra32(string optionName) => AssertMatchesWholeRowBlend<Bgra32>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_Argb32(string optionName) => AssertMatchesWholeRowBlend<Argb32>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_Abgr32(string optionName) => AssertMatchesWholeRowBlend<Abgr32>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_Rgb24(string optionName) => AssertMatchesWholeRowBlend<Rgb24>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_Bgr24(string optionName) => AssertMatchesWholeRowBlend<Bgr24>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_Rgba64(string optionName) => AssertMatchesWholeRowBlend<Rgba64>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_Rgb48(string optionName) => AssertMatchesWholeRowBlend<Rgb48>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_RgbaVector(string optionName) => AssertMatchesWholeRowBlend<RgbaVector>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_Bgra5551(string optionName) => AssertMatchesWholeRowBlend<Bgra5551>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_L8(string optionName) => AssertMatchesWholeRowBlend<L8>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Fill_MatchesWholeRowBlend_HalfVector4(string optionName) => AssertMatchesWholeRowBlend<HalfVector4>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Clear_MatchesWholeRowBlend_Rgba32(string optionName) => AssertClearMatchesWholeRowBlend<Rgba32>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Clear_MatchesWholeRowBlend_Bgra32(string optionName) => AssertClearMatchesWholeRowBlend<Bgra32>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Clear_MatchesWholeRowBlend_Argb32(string optionName) => AssertClearMatchesWholeRowBlend<Argb32>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Clear_MatchesWholeRowBlend_Abgr32(string optionName) => AssertClearMatchesWholeRowBlend<Abgr32>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Clear_MatchesWholeRowBlend_Rgb24(string optionName) => AssertClearMatchesWholeRowBlend<Rgb24>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Clear_MatchesWholeRowBlend_Bgr24(string optionName) => AssertClearMatchesWholeRowBlend<Bgr24>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Clear_MatchesWholeRowBlend_Rgba64(string optionName) => AssertClearMatchesWholeRowBlend<Rgba64>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Clear_MatchesWholeRowBlend_Rgb48(string optionName) => AssertClearMatchesWholeRowBlend<Rgb48>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Clear_MatchesWholeRowBlend_RgbaVector(string optionName) => AssertClearMatchesWholeRowBlend<RgbaVector>(optionName);

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void Clear_MatchesWholeRowBlend_L8(string optionName) => AssertClearMatchesWholeRowBlend<L8>(optionName);

    private static void AssertMatchesWholeRowBlend<TPixel>(string optionName)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions options = CreateOptions(optionName);
        using Image<TPixel> expected = CreateBackground<TPixel>();
        using Image<TPixel> actual = expected.Clone();

        expected.Mutate(c => c.Paint(options, canvas => DrawScene(canvas, Colors, color => new WholeRowBlendSolidBrush(color))));
        actual.Mutate(c => c.Paint(options, canvas => DrawScene(canvas, Colors, color => new SolidBrush(color))));
        AssertEqual(expected, actual);
    }

    private static void AssertClearMatchesWholeRowBlend<TPixel>(string optionName)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DrawingOptions options = CreateOptions(optionName);
        using Image<TPixel> expected = CreateBackground<TPixel>();
        using Image<TPixel> actual = expected.Clone();

        expected.Mutate(c => c.Paint(options, canvas => DrawClearScene(canvas, Colors, color => new WholeRowBlendSolidBrush(color))));
        actual.Mutate(c => c.Paint(options, canvas => DrawClearScene(canvas, Colors, color => new SolidBrush(color))));
        AssertEqual(expected, actual);
    }

    private static void AssertEqual<TPixel>(Image<TPixel> expected, Image<TPixel> actual)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                TPixel e = expected[x, y];
                TPixel a = actual[x, y];
                if (!e.Equals(a))
                {
                    Assert.Fail($"Pixel ({x}, {y}) differs: expected {e}, actual {a}.");
                }
            }
        }
    }

    /// <summary>
    /// Draws shapes whose rows start and end at every column modulo the vector width, with interior runs of
    /// many lengths, as fills and as strokes, in opaque and translucent colors.
    /// </summary>
    private static void DrawScene(DrawingCanvas canvas, Color[] colors, Func<Color, Brush> createBrush)
    {
        int index = 0;
        for (int i = 0; i < 24; i++)
        {
            float left = 1.3F + (i * 2.37F);
            float top = 0.6F + (i * 3.11F);
            float width = 30F + (i * 7.9F);
            float height = 8F + (i * 2.3F);
            Brush brush = createBrush(colors[index++ % colors.Length]);
            canvas.Fill(brush, new RectanglePolygon(left, top, width, height));

            brush = createBrush(colors[index++ % colors.Length]);
            canvas.Fill(brush, new EllipsePolygon(120F + (i * 3.7F), 60F + (i * 2.9F), 20F + (i * 6.1F), 10F + (i * 3.3F)));

            brush = createBrush(colors[index++ % colors.Length]);
            IPath rotated = new RectanglePolygon(40F + (i * 5F), 20F + (i * 4F), 90F, 25F)
                .Transform(Matrix4x4.CreateRotationZ(0.1F + (i * 0.13F), new Vector3(100F, 80F, 0F)));
            canvas.Fill(brush, rotated);

            brush = createBrush(colors[index++ % colors.Length]);
            canvas.Draw(new SolidPen(brush, 1F + (i * 1.7F)), new EllipsePolygon(150F, 90F, 30F + (i * 4F), 20F + (i * 3F)));
        }

        // Rows of every length from 1 to 79 pixels, at every start column modulo 16.
        for (int length = 1; length < 80; length++)
        {
            Brush brush = createBrush(colors[length % 4]);
            canvas.Fill(brush, new RectanglePolygon(length % 16, 100 + length, length, 1));
            brush = createBrush(colors[(length + 1) % 4]);
            canvas.Fill(brush, new RectanglePolygon((length % 16) + 100.5F, 100 + length, length, 1));
        }
    }

    /// <summary>
    /// Clears rectangles on whole and fractional pixel edges, rows of every length from 1 to 79 pixels at every start
    /// column modulo 16, and ellipses, in every color of the list. A clear replaces the pixels with the source
    /// composition, so the interior of each shape takes the full-coverage pixel and its edges are blended.
    /// </summary>
    private static void DrawClearScene(DrawingCanvas canvas, Color[] colors, Func<Color, Brush> createBrush)
    {
        int index = 0;
        for (int i = 0; i < 30; i++)
        {
            canvas.Clear(createBrush(colors[index++ % colors.Length]), new Rectangle(i * 3, i * 2, 60 + (i * 5), 12 + i));
            canvas.Clear(createBrush(colors[index++ % colors.Length]), new RectanglePolygon(1.3F + (i * 2.37F), 0.6F + (i * 3.11F), 30F + (i * 7.9F), 8F + (i * 2.3F)));
            canvas.Clear(createBrush(colors[index++ % colors.Length]), new EllipsePolygon(120F + (i * 3.7F), 60F + (i * 2.9F), 20F + (i * 6.1F), 10F + (i * 3.3F)));
        }

        for (int length = 1; length < 80; length++)
        {
            canvas.Clear(createBrush(colors[index++ % colors.Length]), new Rectangle(length % 16, 100 + length, length, 1));
            canvas.Clear(createBrush(colors[index++ % colors.Length]), new RectanglePolygon((length % 16) + 100.5F, 100 + length, length, 1));
        }
    }

    private static DrawingOptions CreateOptions(string name)
    {
        GraphicsOptions graphics = name switch
        {
            "Aliased" => new GraphicsOptions { Antialias = false },
            "HalfOpacity" => new GraphicsOptions { BlendPercentage = 0.5F },
            "Multiply" => new GraphicsOptions { ColorBlendingMode = PixelColorBlendingMode.Multiply },
            "SrcAtop" => new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.SrcAtop },
            "Src" => new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.Src },
            _ => new GraphicsOptions(),
        };

        return new DrawingOptions { GraphicsOptions = graphics };
    }

    /// <summary>
    /// Creates a background with a different color and alpha in every pixel, so a fill that skipped the
    /// blend of a pixel whose result depends on the backdrop would show.
    /// </summary>
    private static Image<TPixel> CreateBackground<TPixel>()
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image<TPixel> image = new(253, 181);
        uint state = 0x9E3779B9;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                Vector4 value = new(
                    (state & 0xFF) / 255F,
                    ((state >> 8) & 0xFF) / 255F,
                    ((state >> 16) & 0xFF) / 255F,
                    ((state >> 24) & 0xFF) / 255F);
                image[x, y] = TPixel.FromScaledVector4(value);
            }
        }

        return image;
    }

    /// <summary>
    /// A solid brush whose renderer blends every coverage row in one blender call, as the solid brush did
    /// before it wrote full-coverage runs directly.
    /// </summary>
    private sealed class WholeRowBlendSolidBrush : Brush
    {
        private readonly Color color;

        /// <summary>
        /// Initializes a new instance of the <see cref="WholeRowBlendSolidBrush"/> class.
        /// </summary>
        /// <param name="color">The color.</param>
        public WholeRowBlendSolidBrush(Color color) => this.color = color;

        /// <inheritdoc />
        public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            RectangleF region)
            => new Renderer<TPixel>(configuration, options, canvasWidth, this.color.ToPixel<TPixel>());

        /// <inheritdoc />
        public override bool Equals(Brush? other) => ReferenceEquals(this, other);

        /// <inheritdoc />
        public override int GetHashCode() => this.color.GetHashCode();

        /// <summary>
        /// Blends the color into each coverage row with one blender call.
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        private sealed class Renderer<TPixel> : BrushRenderer<TPixel>
            where TPixel : unmanaged, IPixel<TPixel>
        {
            private readonly TPixel color;

            /// <summary>
            /// Initializes a new instance of the <see cref="Renderer{TPixel}"/> class.
            /// </summary>
            /// <param name="configuration">The configuration.</param>
            /// <param name="options">The graphics options.</param>
            /// <param name="canvasWidth">The canvas width.</param>
            /// <param name="color">The color.</param>
            public Renderer(Configuration configuration, GraphicsOptions options, int canvasWidth, TPixel color)
                : base(configuration, options, canvasWidth)
                => this.color = color;

            /// <inheritdoc />
            public override void Apply(
                Span<TPixel> destinationRow,
                ReadOnlySpan<float> scanline,
                int x,
                int y,
                BrushWorkspace<TPixel> workspace)
            {
                if (destinationRow.Length > scanline.Length)
                {
                    destinationRow = destinationRow[..scanline.Length];
                }
                else
                {
                    scanline = scanline[..destinationRow.Length];
                }

                this.Blender.BlendWithCoverage(
                    this.Configuration,
                    destinationRow,
                    destinationRow,
                    this.color,
                    this.Options.BlendPercentage,
                    scanline,
                    workspace.GetBlendScratch(scanline.Length, 2));
            }
        }
    }
}
