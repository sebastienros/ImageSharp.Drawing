// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A resolved filter for one filtered element: the filter region and the primitives in order, in the user space of the
/// element. It is immutable and safe to share between concurrent renders.
/// </summary>
/// <remarks>
/// The result of the last primitive is the result of the filter. <see cref="LastUse"/> lets the engine release a result
/// as soon as its last consumer has run.
/// </remarks>
internal sealed class SvgFilterGraph
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterGraph"/> class.
    /// </summary>
    /// <param name="region">The filter region in the user space of the filtered element. Its width and height are greater than zero.</param>
    /// <param name="primitives">The primitives in order; at least one. Every input refers to an earlier primitive or a standard input.</param>
    public SvgFilterGraph(RectangleF region, SvgFilterPrimitive[] primitives)
    {
        this.Region = region;
        this.Primitives = primitives;

        // The last consumer of each result. The result of the last primitive is consumed by the output, which counts as
        // a consumer after every primitive.
        int[] lastUse = new int[primitives.Length];
        lastUse.AsSpan().Fill(-1);
        lastUse[^1] = primitives.Length;
        for (int i = 0; i < primitives.Length; i++)
        {
            foreach (int input in primitives[i].Inputs)
            {
                if (input >= 0)
                {
                    lastUse[input] = Math.Max(lastUse[input], i);
                }
                else
                {
                    this.UsesSource = true;
                }
            }
        }

        this.LastUse = lastUse;
    }

    /// <summary>
    /// Gets the filter region in the user space of the filtered element.
    /// </summary>
    public RectangleF Region { get; }

    /// <summary>
    /// Gets the primitives in order.
    /// </summary>
    public SvgFilterPrimitive[] Primitives { get; }

    /// <summary>
    /// Gets, per primitive, the index of the last primitive that reads its result, <see cref="Primitives"/>.Length for the
    /// last primitive, or -1 for a result that nothing reads.
    /// </summary>
    public int[] LastUse { get; }

    /// <summary>
    /// Gets a value indicating whether a primitive reads <c>SourceGraphic</c> or <c>SourceAlpha</c>, so the content of the
    /// element must render.
    /// </summary>
    public bool UsesSource { get; }
}
