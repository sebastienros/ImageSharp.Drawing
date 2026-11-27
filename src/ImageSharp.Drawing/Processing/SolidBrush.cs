// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides an implementation of a solid brush for painting solid color areas.
/// </summary>
public sealed class SolidBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SolidBrush"/> class.
    /// </summary>
    /// <param name="color">The color.</param>
    public SolidBrush(Color color) => this.Color = color;

    /// <summary>
    /// Gets the color.
    /// </summary>
    public Color Color { get; }

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
    {
        TPixel color = this.Color.ToPixel<TPixel>();
        if (OpaqueSolidBrushRenderer<TPixel>.CanWriteFullCoverage(options, color))
        {
            return new OpaqueSolidBrushRenderer<TPixel>(configuration, options, canvasWidth, color);
        }

        return new SolidBrushRenderer<TPixel>(configuration, options, canvasWidth, color);
    }

    /// <inheritdoc/>
    public override bool Equals(Brush? other)
    {
        if (other is SolidBrush sb)
        {
            return sb.Color.Equals(this.Color);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => this.Color.GetHashCode();

    /// <summary>
    /// The solid brush applicator.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private class SolidBrushRenderer<TPixel> : BrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SolidBrushRenderer{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="color">The color.</param>
        public SolidBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            TPixel color)
            : base(configuration, options, canvasWidth)
            => this.Color = color;

        /// <summary>
        /// Gets the color.
        /// </summary>
        protected TPixel Color { get; }

        /// <inheritdoc />
        public override void Apply(
            Span<TPixel> destinationRow,
            ReadOnlySpan<float> scanline,
            int x,
            int y,
            BrushWorkspace<TPixel> workspace)
        {
            // Constrain the spans to each other
            if (destinationRow.Length > scanline.Length)
            {
                destinationRow = destinationRow[..scanline.Length];
            }
            else
            {
                scanline = scanline[..destinationRow.Length];
            }

            this.Blend(destinationRow, scanline, workspace);
        }

        /// <summary>
        /// Blends the color into a row slice with the coverage of each pixel.
        /// </summary>
        /// <param name="destinationRow">The destination row slice to shade.</param>
        /// <param name="scanline">The coverage values, of the same length as <paramref name="destinationRow"/>.</param>
        /// <param name="workspace">The worker-local scratch workspace for temporary blending buffers.</param>
        protected void Blend(Span<TPixel> destinationRow, ReadOnlySpan<float> scanline, BrushWorkspace<TPixel> workspace)

            // The single-color overlay overload only needs two Vector4 scratch rows,
            // unlike the per-pixel overlay path which needs three.
            => this.Blender.BlendWithCoverage(
                this.Configuration,
                destinationRow,
                destinationRow,
                this.Color,
                this.Options.BlendPercentage,
                scanline,
                workspace.GetBlendScratch(scanline.Length, 2));
    }

    /// <summary>
    /// The solid brush applicator for an opaque color drawn with normal source-over blending at full opacity.
    /// It writes the color into runs of full coverage and blends only the other pixels.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class OpaqueSolidBrushRenderer<TPixel> : SolidBrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        /// <summary>
        /// The bit pattern of a coverage value of exactly one. Comparing the bits finds full coverage
        /// with the vectorized integer search of the runtime.
        /// </summary>
        private const int FullCoverageBits = 0x3F800000;

        /// <summary>
        /// The shortest row for which the renderer looks for full-coverage runs. Shorter rows go to the
        /// blender whole, because the search and the extra blender calls would cost more than they save.
        /// </summary>
        private const int MinimumSplitLength = 32;

        /// <summary>
        /// The shortest run of full coverage that the renderer writes directly.
        /// </summary>
        private const int MinimumFillLength = 16;

        /// <summary>
        /// The number of pixels that the vector loops of <see cref="PixelBlender{TPixel}"/> process together.
        /// See <see cref="ApplySplit"/> for why the renderer must split rows at multiples of it.
        /// </summary>
        private static readonly int BlendGroup = Vector512.IsHardwareAccelerated ? 4 : Vector256.IsHardwareAccelerated ? 2 : 1;

        /// <summary>
        /// Whether <typeparamref name="TPixel"/> stores each color channel as an unsigned integer of 8 or 16 bits
        /// with straight alpha or no alpha, which the proof in <see cref="ApplySplit"/> needs.
        /// </summary>
        private static readonly bool IsQuantizedStraightAlphaFormat =
            typeof(TPixel) == typeof(Rgba32)
            || typeof(TPixel) == typeof(Bgra32)
            || typeof(TPixel) == typeof(Argb32)
            || typeof(TPixel) == typeof(Abgr32)
            || typeof(TPixel) == typeof(Rgb24)
            || typeof(TPixel) == typeof(Bgr24)
            || typeof(TPixel) == typeof(Rgba64)
            || typeof(TPixel) == typeof(Rgb48);

        /// <summary>
        /// Initializes a new instance of the <see cref="OpaqueSolidBrushRenderer{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options, for which <see cref="CanWriteFullCoverage"/> is true.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="color">The opaque color.</param>
        public OpaqueSolidBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            TPixel color)
            : base(configuration, options, canvasWidth, color)
        {
        }

        /// <summary>
        /// Gets a value indicating whether a full-coverage pixel takes the color unchanged under these options,
        /// so that this renderer gives the same pixels as <see cref="SolidBrushRenderer{TPixel}"/>.
        /// </summary>
        /// <param name="options">The graphics options.</param>
        /// <param name="color">The color.</param>
        /// <returns><see langword="true"/> when the proof in <see cref="ApplySplit"/> holds.</returns>
        public static bool CanWriteFullCoverage(GraphicsOptions options, TPixel color)

            // The proof holds for straight source-over with normal blending at full opacity, an opaque color,
            // and the pixel formats listed above. The blender is compared with the shared instance that
            // PixelOperations returns for that mode, which also excludes any pixel format whose operations
            // would choose another blender.
            => IsQuantizedStraightAlphaFormat
            && options.BlendPercentage == 1F
            && color.ToScaledVector4().W == 1F
            && ReferenceEquals(
                PixelOperations<TPixel>.Instance.GetPixelBlender(options),
                PixelOperations<TPixel>.Instance.GetPixelBlender(PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver));

        /// <inheritdoc />
        public override void Apply(
            Span<TPixel> destinationRow,
            ReadOnlySpan<float> scanline,
            int x,
            int y,
            BrushWorkspace<TPixel> workspace)
        {
            // Constrain the spans to each other
            if (destinationRow.Length > scanline.Length)
            {
                destinationRow = destinationRow[..scanline.Length];
            }
            else
            {
                scanline = scanline[..destinationRow.Length];
            }

            if (scanline.Length >= MinimumSplitLength)
            {
                this.ApplySplit(destinationRow, scanline, workspace);
                return;
            }

            this.Blend(destinationRow, scanline, workspace);
        }

        /// <summary>
        /// Writes the color into the runs of full coverage and blends the rest of the row.
        /// </summary>
        /// <param name="destinationRow">The destination row slice to shade.</param>
        /// <param name="scanline">The coverage values, of the same length as <paramref name="destinationRow"/>.</param>
        /// <param name="workspace">The worker-local scratch workspace for temporary blending buffers.</param>
        /// <remarks>
        /// <para>
        /// The result is the same as one blend of the whole row, for two reasons.
        /// </para>
        /// <para>
        /// A full-coverage pixel gets the color itself. With an opaque source (alpha exactly 1), an amount of 1,
        /// and coverage 1, the straight source-over equation of the blender reduces in exact arithmetic to the
        /// source color: the backdrop weight is <c>dA - 1 * dA = 0</c>, the result alpha is 1, and the coverage
        /// mix <c>b + (s - b) * 1</c> returns the composed value. In single precision each step rounds once, on
        /// values of at most 1 in magnitude, and the divisions are by alphas within a few units in the last place
        /// of 1, so every channel stays within 1e-6 of <c>c / (2^n - 1)</c>, where <c>c</c> is the channel of the
        /// color. The conversion back rounds each channel to the nearest of the <c>2^n</c> levels, with <c>n</c>
        /// 8 or 16, and half a level is at least 7.6e-6, so it returns <c>c</c>. This holds in the vector loop and
        /// in the scalar loop of the blender, with or without fused multiply-add.
        /// </para>
        /// <para>
        /// Every other pixel goes through the same blender loop as in a blend of the whole row. The blender of
        /// ImageSharp 4.1.2 runs its vector loop over the first <c>BlendGroup * floor(length / BlendGroup)</c>
        /// pixels of each call (four pixels per 512-bit register, two per 256-bit register) and a scalar loop over
        /// the rest. The two loops can differ in the last bit, because only the vector loop fuses its
        /// multiply-adds, and a call of 2 or 3 pixels on a machine with 512-bit vectors takes the 256-bit loop.
        /// The conversions to and from <see cref="System.Numerics.Vector4"/> give the same value in their vector
        /// and scalar parts. So every piece that this method blends starts at a multiple of <c>BlendGroup</c>,
        /// each piece but the last has a length that is a multiple of it, and the last piece, which ends at the
        /// end of the row, is at least <c>BlendGroup</c> pixels long or exactly one pixel long.
        /// </para>
        /// </remarks>
        private void ApplySplit(Span<TPixel> destinationRow, ReadOnlySpan<float> scanline, BrushWorkspace<TPixel> workspace)
        {
            int length = scanline.Length;
            int group = BlendGroup;

            // In a blend of the whole row, the pixels before vectorEnd take the vector loop and the rest the scalar loop.
            int remainder = length % group;
            int vectorEnd = length - remainder;

            // A last piece of 2 or 3 pixels would take the 256-bit loop instead of the scalar loop, so then the
            // fills stop one group before vectorEnd and leave that group to the last piece.
            int fillLimit = remainder >= 2 ? vectorEnd - group : vectorEnd;

            ReadOnlySpan<int> coverageBits = MemoryMarshal.Cast<float, int>(scanline);
            TPixel color = this.Color;
            int blendStart = 0;
            int position = 0;
            while (position < fillLimit)
            {
                int runStart = coverageBits[position..fillLimit].IndexOf(FullCoverageBits);
                if (runStart < 0)
                {
                    break;
                }

                runStart += position;
                int runLength = coverageBits[runStart..fillLimit].IndexOfAnyExcept(FullCoverageBits);
                int runEnd = runLength < 0 ? fillLimit : runStart + runLength;
                position = runEnd;

                // Only whole groups are written, so every blended piece starts at a multiple of the group.
                int fillStart = runStart + ((group - (runStart % group)) % group);
                int fillEnd = runEnd - (runEnd % group);
                if (fillEnd - fillStart < MinimumFillLength)
                {
                    continue;
                }

                if (fillStart > blendStart)
                {
                    this.Blend(destinationRow[blendStart..fillStart], scanline[blendStart..fillStart], workspace);
                }

                destinationRow[fillStart..fillEnd].Fill(color);
                blendStart = fillEnd;
            }

            if (blendStart < length)
            {
                this.Blend(destinationRow[blendStart..], scanline[blendStart..], workspace);
            }
        }
    }
}
