// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feBlend</c> filter primitive: blends <c>in</c> (the source) over <c>in2</c> (the backdrop) with one of the 16
/// blend modes of Compositing and Blending Level 1.
/// </summary>
/// <remarks>
/// <para>
/// The result is source-over compositing with the blended color, as Compositing and Blending defines it:
/// <c>co = cs (1 - ab) as + cb (1 - as) ab + as ab B(cb, cs)</c> on straight colors, with the alpha of source-over. The
/// blend functions come from ImageSharp: <see cref="PixelBlender{TPixel}"/> for <see cref="RgbaVector"/> with
/// <see cref="PixelAlphaCompositionMode.SrcOver"/> computes exactly this formula in floating point on straight colors,
/// so the pixels convert from premultiplied 8 bits to straight floats, blend, and convert back.
/// </para>
/// <para>
/// <c>normal</c> is the default, also for an invalid mode. Mode keywords are case-sensitive.
/// </para>
/// </remarks>
internal sealed class SvgFeBlend : SvgFilterPrimitive
{
    private readonly PixelBlender<RgbaVector> blender;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeBlend"/> class.
    /// </summary>
    /// <param name="input">The index of <c>in</c>, the source.</param>
    /// <param name="input2">The index of <c>in2</c>, the backdrop.</param>
    /// <param name="mode">The blend mode.</param>
    private SvgFeBlend(int input, int input2, SvgBlendMode mode)
        : base([input, input2])
        => this.blender = PixelOperations<RgbaVector>.Instance.GetPixelBlender(
            mode == SvgBlendMode.Normal ? PixelColorBlendingMode.Normal : SvgEmitter.ToColorBlendingMode(mode),
            PixelAlphaCompositionMode.SrcOver);

    /// <summary>
    /// Creates the primitive of an <c>feBlend</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        SvgBlendMode mode = SvgBlendMode.Normal;
        if (context.TryGetValue(SvgAttributeId.Mode, out SvgFilterAttributeValue value))
        {
            int index = Array.IndexOf(SvgPropertyParser.BlendModeKeywords, value.Text);
            mode = index >= 0 ? (SvgBlendMode)index : SvgBlendMode.Normal;
        }

        ref readonly SvgFilterPrimitiveAttributes attributes = ref context.Attributes;
        primitive = new SvgFeBlend(context.GetInput(attributes.In), context.GetInput(attributes.In2), mode);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        Rectangle area = output.Area;
        int width = area.Width;
        using IMemoryOwner<Rgba32> rowScratch = context.Allocator.Allocate<Rgba32>(width);
        using IMemoryOwner<RgbaVector> vectorScratch = context.Allocator.Allocate<RgbaVector>(width * 2);
        Span<Rgba32> backdropRow = rowScratch.Memory.Span[..width];
        Span<RgbaVector> source = vectorScratch.Memory.Span[..width];
        Span<RgbaVector> backdrop = vectorScratch.Memory.Span.Slice(width, width);

        for (int y = area.Top; y < area.Bottom; y++)
        {
            Span<Rgba32> row = output.GetRow(y);
            inputs[0].CopyRow(y, area.Left, row);
            inputs[1].CopyRow(y, area.Left, backdropRow);
            for (int x = 0; x < width; x++)
            {
                source[x] = RgbaVector.FromScaledVector4(SvgFilterColor.Unpremultiply(row[x]));
                backdrop[x] = RgbaVector.FromScaledVector4(SvgFilterColor.Unpremultiply(backdropRow[x]));
            }

            // The blend writes over the backdrop, which is the destination of the source-over composite.
            this.blender.Blend(Configuration.Default, backdrop, backdrop, source, 1F);
            for (int x = 0; x < width; x++)
            {
                Vector4 color = backdrop[x].ToScaledVector4();
                row[x] = SvgFilterColor.Premultiply(color);
            }
        }
    }
}
