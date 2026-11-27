// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A brush that paints an <see cref="SvgTexture"/> placed by an affine transform, with bilinear or nearest neighbor
/// sampling, either repeated (a <c>pattern</c> tile) or clamped to its edge (an <c>image</c>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImageBrush"/> samples its image in device pixels without a transform, so it cannot follow a rotated,
/// skewed, or scaled SVG user space. This brush keeps the full transform from texel space to drawing space. The canvas
/// bakes the command transform into it through <see cref="Transform(Matrix4x4, Rectangle, Rectangle)"/>, as it does for
/// gradient brushes, and the renderer maps each device pixel center back into texel space with the inverse.
/// </para>
/// <para>
/// Sampling follows the texel-center convention: texel <c>(i, j)</c> covers <c>[i, i + 1) x [j, j + 1)</c> and its
/// value lies at <c>(i + 0.5, j + 0.5)</c>. Bilinear sampling interpolates the four nearest texels in premultiplied
/// space. With <see cref="WrapMode.Repeat"/> the neighbors of an edge texel come from the opposite edge, so repeated
/// tiles join without seams; with <see cref="WrapMode.Clamp"/> they repeat the edge texel, and the geometry that the
/// brush fills supplies the antialiased edge.
/// </para>
/// </remarks>
internal sealed class SvgTextureBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextureBrush"/> class.
    /// </summary>
    /// <param name="texture">The texture.</param>
    /// <param name="textureToDrawing">The transform from texel space to drawing space.</param>
    /// <param name="wrap">The wrap mode: <see cref="WrapMode.Repeat"/> or <see cref="WrapMode.Clamp"/>.</param>
    /// <param name="nearest">Whether to sample the nearest texel instead of interpolating.</param>
    /// <param name="opacity">The opacity that multiplies every texel, in the range 0 to 1.</param>
    public SvgTextureBrush(SvgTexture texture, Matrix3x2 textureToDrawing, WrapMode wrap, bool nearest, float opacity)
    {
        this.Texture = texture;
        this.TextureToDrawing = textureToDrawing;
        this.Wrap = wrap;
        this.Nearest = nearest;
        this.Opacity = opacity;
    }

    /// <summary>
    /// Gets the texture.
    /// </summary>
    public SvgTexture Texture { get; }

    /// <summary>
    /// Gets the transform from texel space to drawing space.
    /// </summary>
    public Matrix3x2 TextureToDrawing { get; }

    /// <summary>
    /// Gets the wrap mode.
    /// </summary>
    public WrapMode Wrap { get; }

    /// <summary>
    /// Gets a value indicating whether the brush samples the nearest texel.
    /// </summary>
    public bool Nearest { get; }

    /// <summary>
    /// Gets the opacity that multiplies every texel.
    /// </summary>
    public float Opacity { get; }

    /// <inheritdoc/>
    public override Brush Transform(Matrix4x4 matrix, Rectangle sourceInterest, Rectangle preparedInterest)
    {
        // The canvas passes the command transform. Only its affine part can be applied to a texture placement; SVG
        // rendering never produces a projective command transform unless the caller's canvas transform is projective.
        Matrix3x2 affine = new(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M41, matrix.M42);
        return affine.IsIdentity
            ? this
            : new SvgTextureBrush(this.Texture, this.TextureToDrawing * affine, this.Wrap, this.Nearest, this.Opacity);
    }

    /// <inheritdoc/>
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
        => new Renderer<TPixel>(configuration, options, canvasWidth, this);

    /// <inheritdoc/>
    public override bool Equals(Brush? other)
        => other is SvgTextureBrush brush
        && ReferenceEquals(brush.Texture, this.Texture)
        && brush.TextureToDrawing == this.TextureToDrawing
        && brush.Wrap == this.Wrap
        && brush.Nearest == this.Nearest
        && brush.Opacity == this.Opacity;

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(this.Texture, this.TextureToDrawing, this.Wrap, this.Nearest, this.Opacity);

    /// <summary>
    /// Samples the texture for each pixel of a scanline and blends it with the coverage.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class Renderer<TPixel> : BrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        /// <summary>
        /// The largest magnitude of a texel offset and of a pixel coordinate for which <see cref="ApplyTranslated"/> applies.
        /// Every coordinate that the sampling arithmetic forms is then a multiple of one half below 2^23 in magnitude, so
        /// each float sum is exact.
        /// </summary>
        private const int MaxExactCoordinate = 1 << 21;

        // The sampled level. Its pixels are read through a span once per scanline.
        private readonly SvgTexture texture;
        private readonly int width;
        private readonly int height;
        private readonly bool repeat;
        private readonly bool nearest;
        private readonly bool valid;
        private readonly float opacity;

        // Drawing space to texel space of the sampled level.
        private readonly Matrix3x2 inverse;

        // Whether the inverse is a translation by whole texels (see ApplyTranslated), and its offsets: the texel of the
        // pixel (x, y) is (x + offsetX, y + offsetY) before the wrap mode applies.
        private readonly bool translated;
        private readonly int offsetX;
        private readonly int offsetY;

        /// <summary>
        /// Initializes a new instance of the <see cref="Renderer{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width.</param>
        /// <param name="brush">The brush.</param>
        public Renderer(Configuration configuration, GraphicsOptions options, int canvasWidth, SvgTextureBrush brush)
            : base(configuration, options, canvasWidth)
        {
            this.repeat = brush.Wrap == WrapMode.Repeat;
            this.nearest = brush.Nearest;
            this.opacity = brush.Opacity;
            this.valid = Matrix3x2.Invert(brush.TextureToDrawing, out Matrix3x2 inverse)
                && float.IsFinite(inverse.M11 + inverse.M12 + inverse.M21 + inverse.M22 + inverse.M31 + inverse.M32);

            // A device pixel step moves (M11, M12) along x and (M21, M22) along y in texel space. When the longer step
            // spans more than one texel, the texture is minified: sample the box-filtered level whose texels are about
            // one device pixel in size, so that the bilinear filter does not skip texels and alias. Nearest neighbor
            // sampling asks for hard texel edges, which only matter when the texture is magnified, so it uses the same
            // levels.
            int level = 0;
            if (this.valid)
            {
                float step = MathF.Sqrt(MathF.Max(
                    (inverse.M11 * inverse.M11) + (inverse.M12 * inverse.M12),
                    (inverse.M21 * inverse.M21) + (inverse.M22 * inverse.M22)));

                if (step >= 2F)
                {
                    level = (int)MathF.Floor(MathF.Log2(step));
                }
            }

            SvgTexture texture = brush.Texture.GetLevel(level);
            if (texture != brush.Texture)
            {
                // The texel space of the level is the texel space of the texture scaled by the size ratio.
                inverse *= Matrix3x2.CreateScale(
                    texture.Width / (float)brush.Texture.Width,
                    texture.Height / (float)brush.Texture.Height);
            }

            this.inverse = inverse;
            this.texture = texture;
            this.width = texture.Width;
            this.height = texture.Height;

            // A filter result drawn at the device scale, or an image at its own size on whole pixels, places every texel
            // on one device pixel: the inverse is a translation by whole texels.
            this.translated = this.valid
                && inverse.M11 == 1F && inverse.M22 == 1F && inverse.M12 == 0F && inverse.M21 == 0F
                && MathF.Abs(inverse.M31) <= MaxExactCoordinate && MathF.Abs(inverse.M32) <= MaxExactCoordinate
                && MathF.Floor(inverse.M31) == inverse.M31 && MathF.Floor(inverse.M32) == inverse.M32;

            if (this.translated)
            {
                this.offsetX = (int)inverse.M31;
                this.offsetY = (int)inverse.M32;
            }
        }

        /// <inheritdoc/>
        public override void Apply(Span<TPixel> destinationRow, ReadOnlySpan<float> scanline, int x, int y, BrushWorkspace<TPixel> workspace)
        {
            Span<TPixel> overlays = workspace.GetOverlays(scanline.Length);
            Span<float> amounts = workspace.GetAmounts(scanline.Length);
            scanline.CopyTo(amounts);

            if (!this.valid)
            {
                // A singular placement maps the texture to a line or a point, which covers no area.
                amounts.Clear();
                overlays.Clear();
            }
            else if (this.translated
                && x >= -MaxExactCoordinate && x <= MaxExactCoordinate - scanline.Length
                && y >= -MaxExactCoordinate && y <= MaxExactCoordinate)
            {
                this.ApplyTranslated(this.texture.Pixels, overlays, scanline, x, y, workspace);
            }
            else
            {
                // Map the first pixel center, then step one pixel along x for each sample. In texel space a sample
                // value lies at the texel center, so the bilinear filter works on coordinates shifted by half a texel.
                Matrix3x2 m = this.inverse;
                float px = x + 0.5F;
                float py = y + 0.5F;
                float u = (px * m.M11) + (py * m.M21) + m.M31;
                float v = (px * m.M12) + (py * m.M22) + m.M32;
                float scale = this.opacity / 255F;
                ReadOnlySpan<Rgba32> pixels = this.texture.Pixels;

                if (typeof(TPixel) == typeof(Rgba32))
                {
                    // One bulk conversion instead of one per pixel. For Rgba32 the bulk associated conversion of
                    // ImageSharp 5 applies the same lane-wise division by alpha and the same clamp, then the byte
                    // conversion clamp(x, 0, 1) * 255 + 0.5 truncated, which gives the byte of the per-pixel
                    // FromAssociatedScaledVector4 (scale, add one half, clamp to [0, 255], truncate) for every float.
                    // The zero vector of an uncovered pixel converts to the pixel of zeros, as default gives. The first
                    // blend scratch row is free until the blend below fills it.
                    Span<Vector4> colors = workspace.GetBlendScratch(scanline.Length, 3)[..scanline.Length];
                    for (int i = 0; i < scanline.Length; i++, u += m.M11, v += m.M12)
                    {
                        colors[i] = scanline[i] <= 0F
                            ? Vector4.Zero
                            : (this.nearest ? this.SampleNearest(pixels, u, v) : this.SampleBilinear(pixels, u - 0.5F, v - 0.5F)) * scale;
                    }

                    PixelOperations<TPixel>.Instance.FromAssociatedScaledVector4Destructive(this.Configuration, colors, overlays);
                }
                else
                {
                    for (int i = 0; i < scanline.Length; i++, u += m.M11, v += m.M12)
                    {
                        if (scanline[i] <= 0F)
                        {
                            overlays[i] = default;
                            continue;
                        }

                        // The sample is premultiplied. The associated conversion divides by alpha for a format that
                        // stores straight alpha and keeps the channels for a format that stores associated alpha.
                        Vector4 color = this.nearest ? this.SampleNearest(pixels, u, v) : this.SampleBilinear(pixels, u - 0.5F, v - 0.5F);
                        overlays[i] = TPixel.FromAssociatedScaledVector4(color * scale);
                    }
                }
            }

            this.Blender.BlendWithCoverage<TPixel>(
                this.Configuration,
                destinationRow,
                destinationRow,
                overlays,
                this.Options.BlendPercentage,
                amounts,
                workspace.GetBlendScratch(scanline.Length, 3));
        }

        /// <summary>
        /// Fills the overlays of a scanline when the inverse placement is a translation by whole texels, by reading the
        /// texels of one row instead of sampling.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Why the bytes are the bytes of the sampling path. The inverse has M11 = M22 = 1, M12 = M21 = 0, and integer
        /// offsets (M31, M32), and the caller checked that the offsets and the pixel coordinates are at most 2^21 in
        /// magnitude. In the sampling path the first pixel center (x + 0.5, y + 0.5) maps to
        /// u = ((x + 0.5) * 1 + (y + 0.5) * 0) + M31 and v = ((x + 0.5) * 0 + (y + 0.5) * 1) + M32. A product with 1 is
        /// exact, a product with 0 is a signed zero that leaves the sum unchanged, and the sums of multiples of one half
        /// below 2^23 are exact, so u = x + 0.5 + M31 and v = y + 0.5 + M32 exactly. The steps add M11 = 1 to u (exact)
        /// and M12 = 0 to v (no change), so pixel i has u = x + i + 0.5 + M31 exactly.
        /// </para>
        /// <para>
        /// Bilinear sampling subtracts one half: the coordinates are the integers k = x + i + M31 and m = y + M32, so the
        /// floors are k and m and both weights are exactly 0. Every form of <see cref="Vector4.Lerp(Vector4, Vector4, float)"/>
        /// returns its first argument for an amount of 0 with finite non-negative arguments: a * (1 - 0) + b * 0 is
        /// a + 0, a + (b - a) * 0 is a plus a signed zero, and a fused multiply-add of either form is exact; a signed zero
        /// added to a, which is +0 or positive, gives a. So the sample is the texel at (Index(k), Index(m)). Nearest
        /// sampling floors u and v without the shift, which gives k and m too.
        /// </para>
        /// <para>
        /// <see cref="Index(float, int)"/> of an integer k below 2^23 in magnitude is the clamp of k for
        /// <see cref="WrapMode.Clamp"/>, and the true modulo for <see cref="WrapMode.Repeat"/>: k / size has a rounding
        /// error below 1 / (2 size), less than its distance to the next integer when it is not an integer itself, so
        /// its floor is exact, and the remaining products and differences are integers below 2^24. This method takes
        /// those indexes and forms the same expression, texel times opacity / 255, then converts as the sampling path
        /// does.
        /// </para>
        /// </remarks>
        /// <param name="pixels">The texels of the sampled level.</param>
        /// <param name="overlays">The overlays to fill.</param>
        /// <param name="scanline">The coverage of the scanline.</param>
        /// <param name="x">The first column of the scanline.</param>
        /// <param name="y">The row of the scanline.</param>
        /// <param name="workspace">The workspace whose first blend scratch row holds the colors.</param>
        private void ApplyTranslated(
            ReadOnlySpan<Rgba32> pixels,
            Span<TPixel> overlays,
            ReadOnlySpan<float> scanline,
            int x,
            int y,
            BrushWorkspace<TPixel> workspace)
        {
            int width = this.width;
            int row = this.repeat ? Modulo(y + this.offsetY, this.height) : Math.Clamp(y + this.offsetY, 0, this.height - 1);
            ReadOnlySpan<Rgba32> texels = pixels.Slice(row * width, width);
            float scale = this.opacity / 255F;

            // The first blend scratch row is free until the blend fills it.
            Span<Vector4> colors = workspace.GetBlendScratch(scanline.Length, 3)[..scanline.Length];
            int first = x + this.offsetX;
            if (this.repeat)
            {
                int column = Modulo(first, width);
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = scanline[i] <= 0F ? Vector4.Zero : ToVector(texels[column]) * scale;
                    column = column == width - 1 ? 0 : column + 1;
                }
            }
            else
            {
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = scanline[i] <= 0F ? Vector4.Zero : ToVector(texels[Math.Clamp(first + i, 0, width - 1)]) * scale;
                }
            }

            if (typeof(TPixel) == typeof(Rgba32))
            {
                // The bulk conversion of the sampling path (see Apply).
                PixelOperations<TPixel>.Instance.FromAssociatedScaledVector4Destructive(this.Configuration, colors, overlays);
                return;
            }

            for (int i = 0; i < colors.Length; i++)
            {
                overlays[i] = scanline[i] <= 0F ? default : TPixel.FromAssociatedScaledVector4(colors[i]);
            }
        }

        /// <summary>
        /// Returns the non-negative remainder of an integer division.
        /// </summary>
        /// <param name="value">The dividend.</param>
        /// <param name="size">The divisor, greater than zero.</param>
        /// <returns>The remainder, in the range 0 to <paramref name="size"/> - 1.</returns>
        private static int Modulo(int value, int size)
        {
            int remainder = value % size;
            return remainder < 0 ? remainder + size : remainder;
        }

        /// <summary>
        /// Returns the texel that contains a point.
        /// </summary>
        /// <param name="pixels">The texels of the sampled level.</param>
        /// <param name="u">The horizontal texel coordinate.</param>
        /// <param name="v">The vertical texel coordinate.</param>
        /// <returns>The premultiplied texel in 8-bit units.</returns>
        private Vector4 SampleNearest(ReadOnlySpan<Rgba32> pixels, float u, float v)
        {
            int ix = this.Index(u, this.width);
            int iy = this.Index(v, this.height);
            return ToVector(pixels[(iy * this.width) + ix]);
        }

        /// <summary>
        /// Interpolates the four texels around a point.
        /// </summary>
        /// <param name="pixels">The texels of the sampled level.</param>
        /// <param name="u">The horizontal coordinate, already shifted by half a texel.</param>
        /// <param name="v">The vertical coordinate, already shifted by half a texel.</param>
        /// <returns>The premultiplied color in 8-bit units.</returns>
        private Vector4 SampleBilinear(ReadOnlySpan<Rgba32> pixels, float u, float v)
        {
            float fu = MathF.Floor(u);
            float fv = MathF.Floor(v);
            float tx = u - fu;
            float ty = v - fv;
            int x0;
            int x1;
            int y0;
            int y1;
            if (this.repeat && MathF.Abs(fu) <= MaxExactCoordinate && MathF.Abs(fv) <= MaxExactCoordinate)
            {
                // Index of an integer below 2^23 in magnitude is its true modulo with WrapMode.Repeat (see
                // ApplyTranslated), and fu + 1 is exact, so the modulo of the integer and its successor give the indexes
                // of Index without its float divisions. NaN fails the range test and takes the general path.
                x0 = Modulo((int)fu, this.width);
                x1 = x0 == this.width - 1 ? 0 : x0 + 1;
                int row = Modulo((int)fv, this.height);
                y0 = row * this.width;
                y1 = (row == this.height - 1 ? 0 : row + 1) * this.width;
            }
            else
            {
                x0 = this.Index(fu, this.width);
                x1 = this.Index(fu + 1F, this.width);
                y0 = this.Index(fv, this.height) * this.width;
                y1 = this.Index(fv + 1F, this.height) * this.width;
            }

            Vector4 top = Vector4.Lerp(ToVector(pixels[y0 + x0]), ToVector(pixels[y0 + x1]), tx);
            Vector4 bottom = Vector4.Lerp(ToVector(pixels[y1 + x0]), ToVector(pixels[y1 + x1]), tx);
            return Vector4.Lerp(top, bottom, ty);
        }

        /// <summary>
        /// Maps a coordinate to a texel index along one axis with the wrap mode of the brush.
        /// </summary>
        /// <param name="coordinate">The coordinate in texels.</param>
        /// <param name="size">The texture size along the axis.</param>
        /// <returns>The index, in the range 0 to <paramref name="size"/> - 1.</returns>
        private int Index(float coordinate, int size)
        {
            float floor = MathF.Floor(coordinate);
            if (!float.IsFinite(floor))
            {
                return 0;
            }

            if (this.repeat)
            {
                // A true modulo, so negative coordinates repeat too. The float remainder keeps large coordinates exact
                // enough and cannot overflow an int.
                float wrapped = floor - (MathF.Floor(floor / size) * size);
                return Math.Clamp((int)wrapped, 0, size - 1);
            }

            // Clamping in float first keeps huge values out of the int conversion.
            return (int)Math.Clamp(floor, 0F, size - 1);
        }

        /// <summary>
        /// Converts a premultiplied texel to a vector in 8-bit units.
        /// </summary>
        /// <param name="pixel">The texel.</param>
        /// <returns>The vector.</returns>
        private static Vector4 ToVector(Rgba32 pixel) => new(pixel.R, pixel.G, pixel.B, pixel.A);
    }
}
