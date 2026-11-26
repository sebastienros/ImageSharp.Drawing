// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feDisplacementMap</c> filter primitive: moves the pixels of its first input by amounts read from its second
/// input, the map.
/// </summary>
/// <remarks>
/// <para>
/// Filter Effects Level 1: <c>P'(x, y) = P(x + scale * (XC(x, y) - 0.5), y + scale * (YC(x, y) - 0.5))</c>, where
/// <c>P</c> is <c>in</c> and <c>XC</c> and <c>YC</c> are the channels of <c>in2</c> that <c>xChannelSelector</c> and
/// <c>yChannelSelector</c> name (<c>R</c>, <c>G</c>, <c>B</c>, or <c>A</c>; the initial value <c>A</c> also for an
/// invalid keyword). <c>scale</c> is one number for both axes in <c>primitiveUnits</c>; the specification does not say
/// which box dimension a bounding box fraction of it refers to, and Chromium uses the box width for both axes (measured
/// on a tall and a wide box), so this primitive does too. A missing or invalid scale is the initial 0, with which the
/// primitive has no effect: it is a pass-through.
/// </para>
/// <para>
/// Color spaces: the map is read unpremultiplied in the color space of the primitive (<c>color-interpolation-filters</c>
/// applies to <c>in2</c> only), and <c>in</c> keeps its own color space. The engine has no rule for a primitive whose
/// inputs need different treatment, so the primitive declares itself a color filter
/// (<see cref="SvgFilterPrimitive.IsColorFilter"/>), as <c>feFlood</c> does: the engine then converts nothing, the
/// primitive converts each map value to its color space in floating point, and it copies the pixels of <c>in</c>
/// unchanged when the output space is the space of <c>in</c>, which is the case when the result goes to the screen
/// (the output space is then sRGB) and <c>in</c> is <c>SourceGraphic</c>. Otherwise the copied pixels convert to the
/// output space, as the engine would convert them for the next primitive. Chromium converts <c>in</c> to the space of
/// the primitive and back, which loses dark colors; the specification says that <c>in</c> must not be converted.
/// </para>
/// <para>
/// Sampling: the source position of output pixel <c>i</c> is <c>i + 0.5 + d</c> in filter pixels, and the pixel that
/// contains it is copied (nearest neighbor), which is what Chromium renders; the specification leaves the interpolation
/// open. A position outside the input area reads transparent black, so the filter region clips the source.
/// </para>
/// </remarks>
internal sealed class SvgFeDisplacementMap : SvgFilterPrimitive
{
    private readonly Vector2 scale;
    private readonly int xChannel;
    private readonly int yChannel;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeDisplacementMap"/> class.
    /// </summary>
    /// <param name="input">The input index of the image to displace.</param>
    /// <param name="map">The input index of the map.</param>
    /// <param name="scale">The scale along x and y in user units; both are the same.</param>
    /// <param name="xChannel">The map channel of the x displacement: 0 to 3 for R, G, B, A.</param>
    /// <param name="yChannel">The map channel of the y displacement.</param>
    private SvgFeDisplacementMap(int input, int map, Vector2 scale, int xChannel, int yChannel)
        : base([input, map])
    {
        this.scale = scale;
        this.xChannel = xChannel;
        this.yChannel = yChannel;
    }

    /// <inheritdoc/>
    public override bool IsColorFilter => true;

    /// <summary>
    /// Creates the primitive of an <c>feDisplacementMap</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>, or <see cref="SvgFilterBuildResult.PassThrough"/> for a zero scale.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        primitive = null;
        float scale = context.TryGetValue(SvgAttributeId.Scale, out SvgFilterAttributeValue value) && value.NumberCount == 1
            ? context.GetNumbers(in value)[0]
            : 0F;
        if (scale == 0F)
        {
            return SvgFilterBuildResult.PassThrough;
        }

        primitive = new SvgFeDisplacementMap(
            context.GetInput(context.Attributes.In),
            context.GetInput(context.Attributes.In2),
            new Vector2(context.ToUserX(scale)),
            GetChannel(context, SvgAttributeId.XChannelSelector),
            GetChannel(context, SvgAttributeId.YChannelSelector));
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override Rectangle GetInputArea(int input, Rectangle area, in SvgFilterRenderContext context)
    {
        if (input == 1)
        {
            // The map is read at the output pixel only.
            return area;
        }

        // A channel value c in [0, 1] displaces by scale * (c - 0.5), at most half the scale either way; the pixel that
        // contains the position i + 0.5 + d is at most ceil(|scale| / 2) pixels away.
        Size reach = GetReach(this.GetPixelScale(in context));
        return Rectangle.Inflate(area, reach.Width, reach.Height);
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        SvgFilterImage input = inputs[0];
        SvgFilterImage map = inputs[1];
        if (input.IsEmpty)
        {
            return;
        }

        Vector2 scale = this.GetPixelScale(in context);
        Size reach = GetReach(scale);
        Rectangle area = output.Area;
        SvgColorInterpolation mapSpace = map.ColorSpace;
        SvgColorInterpolation space = context.ColorSpace;
        SvgColorInterpolation inputSpace = input.ColorSpace;
        SvgColorInterpolation outputSpace = context.OutputColorSpace;
        using IMemoryOwner<Rgba32> rowBuffer = context.Allocator.Allocate<Rgba32>(area.Width);
        Span<Rgba32> mapRow = rowBuffer.Memory.Span;

        for (int y = area.Top; y < area.Bottom; y++)
        {
            map.CopyRow(y, area.Left, mapRow);
            Span<Rgba32> destination = output.GetRow(y);
            for (int i = 0; i < destination.Length; i++)
            {
                // The map unpremultiplied, in the color space of the primitive.
                Vector4 value = SvgFilterColor.Convert(SvgFilterColor.Unpremultiply(mapRow[i]), mapSpace, space);
                float dx = scale.X * (GetChannel(value, this.xChannel) - 0.5F);
                float dy = scale.Y * (GetChannel(value, this.yChannel) - 0.5F);

                // The displacement is at most the reach, so the clamp changes nothing but keeps the conversion defined
                // for a non-finite scale.
                int x = area.Left + i;
                int sx = x + (int)MathF.Floor(Math.Clamp(0.5F + dx, -reach.Width, reach.Width + 1));
                int sy = y + (int)MathF.Floor(Math.Clamp(0.5F + dy, -reach.Height, reach.Height + 1));
                Rgba32 pixel = input.GetPixel(sx, sy);
                if (inputSpace != outputSpace && pixel.A != 0)
                {
                    pixel = SvgFilterColor.Premultiply(SvgFilterColor.Convert(SvgFilterColor.Unpremultiply(pixel), inputSpace, outputSpace));
                }

                destination[i] = pixel;
            }
        }
    }

    /// <summary>
    /// Returns how far the displacement reaches in filter pixels.
    /// </summary>
    /// <param name="scale">The scale in filter pixels.</param>
    /// <returns>The reach along x and y.</returns>
    private static Size GetReach(Vector2 scale)
    {
        // A scale larger than any image moves everything out; the clamp keeps the conversion defined.
        Vector2 reach = Vector2.Min(Vector2.Abs(scale) * 0.5F, new Vector2(1 << 28));
        return new Size((int)MathF.Ceiling(reach.X), (int)MathF.Ceiling(reach.Y));
    }

    /// <summary>
    /// Returns one channel of a color.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="channel">The channel: 0 to 3 for R, G, B, A.</param>
    /// <returns>The channel value.</returns>
    private static float GetChannel(Vector4 color, int channel)
        => channel switch
        {
            0 => color.X,
            1 => color.Y,
            2 => color.Z,
            _ => color.W
        };

    /// <summary>
    /// Reads a channel selector attribute.
    /// </summary>
    /// <param name="context">The build context.</param>
    /// <param name="attribute">The attribute.</param>
    /// <returns>The channel: 0 to 3 for R, G, B, A; 3 for a missing or invalid value.</returns>
    private static int GetChannel(SvgFilterBuildContext context, SvgAttributeId attribute)
    {
        if (!context.TryGetValue(attribute, out SvgFilterAttributeValue value))
        {
            return 3;
        }

        return value.Text switch
        {
            "R" => 0,
            "G" => 1,
            "B" => 2,
            _ => 3
        };
    }

    /// <summary>
    /// Returns the scale in filter pixels.
    /// </summary>
    /// <param name="context">The context of the run.</param>
    /// <returns>The scale along x and y.</returns>
    private Vector2 GetPixelScale(in SvgFilterRenderContext context) => this.scale * context.Scale;
}
