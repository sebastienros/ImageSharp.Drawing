// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of an <see cref="SvgRenderNode"/>.
/// </summary>
internal enum SvgRenderNodeKind : byte
{
    /// <summary>
    /// Fills and then strokes one path.
    /// </summary>
    Draw,

    /// <summary>
    /// Opens an isolated group that composites with an opacity when the matching <see cref="EndLayer"/> closes it.
    /// </summary>
    BeginLayer,

    /// <summary>
    /// Closes the group of the matching <see cref="BeginLayer"/>.
    /// </summary>
    EndLayer,

    /// <summary>
    /// Narrows the clip to a viewport rectangle until the matching <see cref="EndClip"/>.
    /// </summary>
    BeginClip,

    /// <summary>
    /// Restores the clip of the matching <see cref="BeginClip"/>.
    /// </summary>
    EndClip,

    /// <summary>
    /// Does nothing. The resolve stage removes these nodes before it builds the document.
    /// </summary>
    None,

    /// <summary>
    /// Opens a mask layer inside a layer. Its content draws the mask; when the matching <see cref="EndMask"/> closes it,
    /// <see cref="SvgRenderNode.MaskMode"/> turns the content into coverage and the layer below keeps only that coverage
    /// (<see cref="PixelAlphaCompositionMode.DestIn"/>).
    /// </summary>
    BeginMask,

    /// <summary>
    /// Closes the mask layer of the matching <see cref="BeginMask"/>.
    /// </summary>
    EndMask,

    /// <summary>
    /// Draws an element through a filter: the emitter renders the content of the element off-screen, runs the filter
    /// graph, and draws the result as an image. <see cref="SvgRenderNode.FilterIndex"/> selects the filter in
    /// <see cref="SvgDocument.Filters"/>; <see cref="SvgRenderNode.TransformIndex"/> maps the user space of the filtered
    /// element to document user space; <see cref="SvgRenderNode.Opacity"/> multiplies the result;
    /// <see cref="SvgRenderNode.Bounds"/> is the filter region in document user space.
    /// </summary>
    Filter
}
