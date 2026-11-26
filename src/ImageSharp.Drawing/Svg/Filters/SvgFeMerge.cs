// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feMerge</c> filter primitive: composites the inputs of its <c>feMergeNode</c> children with source-over, the
/// first at the bottom.
/// </summary>
/// <remarks>
/// Source-over on premultiplied values is <c>result = source + destination * (1 - source alpha)</c> per channel, a
/// weighted sum that does not depend on the color space, so the primitive works in the space of its inputs. An
/// <c>feMerge</c> without <c>feMergeNode</c> children has a transparent result.
/// </remarks>
internal sealed class SvgFeMerge : SvgFilterPrimitive
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeMerge"/> class.
    /// </summary>
    /// <param name="inputs">The inputs, bottom first.</param>
    private SvgFeMerge(int[] inputs)
        : base(inputs)
    {
    }

    /// <inheritdoc/>
    public override bool PreservesColorSpace => true;

    /// <summary>
    /// Creates the primitive of an <c>feMerge</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>, or <see cref="SvgFilterBuildResult.Transparent"/> without merge nodes.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        primitive = null;
        SvgElementTree tree = context.Tree;
        List<int> inputs = [];
        for (int child = tree.GetFirstChild(context.Element); child >= 0; child = tree.GetNextSibling(child))
        {
            if (tree[child].Kind == SvgElementKind.FeMergeNode)
            {
                inputs.Add(context.GetInput(tree.GetFilterPrimitive(child).In));
            }
        }

        if (inputs.Count == 0)
        {
            return SvgFilterBuildResult.Transparent;
        }

        primitive = new SvgFeMerge([.. inputs]);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        Rectangle area = output.Area;
        foreach (SvgFilterImage input in inputs)
        {
            Rectangle overlap = Rectangle.Intersect(area, input.Area);
            if (overlap.Width <= 0 || overlap.Height <= 0)
            {
                continue;
            }

            for (int y = overlap.Top; y < overlap.Bottom; y++)
            {
                ReadOnlySpan<Rgba32> source = input.GetRow(y).Slice(overlap.Left - input.Area.Left, overlap.Width);
                Span<Rgba32> destination = output.GetRow(y).Slice(overlap.Left - area.Left, overlap.Width);
                SourceOver(source, destination);
            }
        }
    }

    /// <summary>
    /// Composites a row of premultiplied pixels over another with source-over.
    /// </summary>
    /// <param name="source">The pixels on top.</param>
    /// <param name="destination">The pixels below, which receive the result.</param>
    internal static void SourceOver(ReadOnlySpan<Rgba32> source, Span<Rgba32> destination)
    {
        for (int x = 0; x < source.Length; x++)
        {
            Rgba32 s = source[x];
            if (s.A == 255)
            {
                destination[x] = s;
                continue;
            }

            if (s.A == 0 && (s.R | s.G | s.B) == 0)
            {
                continue;
            }

            // A valid premultiplied channel is at most its alpha, so the sum stays within a byte.
            ref Rgba32 d = ref destination[x];
            int inverse = 255 - s.A;
            d.R = (byte)Math.Min(255, s.R + SvgFilterColor.Multiply(d.R, inverse));
            d.G = (byte)Math.Min(255, s.G + SvgFilterColor.Multiply(d.G, inverse));
            d.B = (byte)Math.Min(255, s.B + SvgFilterColor.Multiply(d.B, inverse));
            d.A = (byte)Math.Min(255, s.A + SvgFilterColor.Multiply(d.A, inverse));
        }
    }
}
