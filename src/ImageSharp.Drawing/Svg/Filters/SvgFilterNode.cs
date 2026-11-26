// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The data of one <see cref="SvgRenderNodeKind.Filter"/> node: the content of the filtered element and its resolved
/// filter. It is immutable and shared by every render of the document.
/// </summary>
internal sealed class SvgFilterNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterNode"/> class.
    /// </summary>
    /// <param name="content">The content, or <see langword="null"/> when the element has nothing to draw.</param>
    /// <param name="graphs">The filters, in the order in which they apply; at least one.</param>
    /// <param name="maxPixels">The largest number of pixels of one result, from <see cref="SvgLimits.MaxFilterPixels"/>.</param>
    public SvgFilterNode(SvgDocument? content, SvgFilterGraph[] graphs, int maxPixels)
    {
        this.Content = content;
        this.Graphs = graphs;
        this.MaxPixels = maxPixels;
    }

    /// <summary>
    /// Gets the content of the filtered element as a nested document whose node transforms map to the user space of the
    /// element, without its filter, clip, mask, and opacity; or <see langword="null"/> when the element has nothing to
    /// draw. It is what <c>SourceGraphic</c> renders. Filters inside it render recursively.
    /// </summary>
    public SvgDocument? Content { get; }

    /// <summary>
    /// Gets the resolved filters in the order in which they apply: one for a <c>url()</c> reference, one per entry for a
    /// filter list. The first filter reads the content; each further filter reads the sRGB result of the one before as
    /// its <c>SourceGraphic</c>, clipped to its own filter region. The result of the last filter is the result of the node.
    /// </summary>
    public SvgFilterGraph[] Graphs { get; }

    /// <summary>
    /// Gets the largest number of pixels of one result. A filter that would need more runs at a lower resolution. Filters
    /// run at draw time, so the node carries the limit of the document.
    /// </summary>
    public int MaxPixels { get; }
}
