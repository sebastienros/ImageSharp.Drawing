// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One entry of the render model of an <see cref="SvgDocument"/>. The nodes of a document are stored in paint order in
/// one array. A draw node paints one path; scope nodes open and close a layer, a mask layer, or a clip, and every begin
/// node has a matching end node later in the array.
/// </summary>
/// <remarks>
/// Paths, brushes, and pens are shared: every <c>use</c> instance of an element refers to the same path, and equal
/// paints refer to the same brush and pen. Nothing in a node changes after the resolve stage, so concurrent renders can
/// read the array without locks.
/// </remarks>
internal struct SvgRenderNode
{
    /// <summary>
    /// Gets or sets the node kind.
    /// </summary>
    public SvgRenderNodeKind Kind { readonly get; set; }

    /// <summary>
    /// Gets or sets the fill rule of a draw node.
    /// </summary>
    public IntersectionRule FillRule { readonly get; set; }

    /// <summary>
    /// Gets or sets the index into the transform table of the document. For a draw node it is the transform of the path;
    /// for a clip node it is the transform of the clip path. Layer and mask nodes do not use it.
    /// </summary>
    public int TransformIndex { readonly get; set; }

    /// <summary>
    /// Gets or sets, for a begin node, the index of the matching end node, so a render can skip a culled scope.
    /// </summary>
    public int End { readonly get; set; }

    /// <summary>
    /// Gets or sets, for a <see cref="SvgRenderNodeKind.Filter"/> node, the index of its filter in
    /// <see cref="SvgDocument.Filters"/>. A filter node is not a begin node, so the index shares the storage of
    /// <see cref="End"/> and the node keeps its size.
    /// </summary>
    public int FilterIndex
    {
        readonly get => this.End;
        set => this.End = value;
    }

    /// <summary>
    /// Gets or sets the path of a draw node, in the user space of <see cref="TransformIndex"/>.
    /// </summary>
    public IPath? Path { readonly get; set; }

    /// <summary>
    /// Gets or sets the fill brush of a draw node, or <see langword="null"/> when the path is not filled.
    /// </summary>
    public Brush? Fill { readonly get; set; }

    /// <summary>
    /// Gets or sets the pen of a draw node, or <see langword="null"/> when the path is not stroked.
    /// </summary>
    public Pen? Stroke { readonly get; set; }

    /// <summary>
    /// Gets or sets the clip path of a clip node, as the one-element array that the canvas clip call takes, so a render
    /// does not allocate it.
    /// </summary>
    public IPath[]? Clip { readonly get; set; }

    /// <summary>
    /// Gets or sets the opacity that a layer node composites its group with, or that multiplies the result of a filter
    /// node.
    /// </summary>
    public float Opacity { readonly get; set; }

    /// <summary>
    /// Gets or sets a conservative bounding box of what the node paints, in document user space (the user space of the
    /// root element, before the viewport transform). For a draw node it includes the stroke; for a layer node it is the
    /// union of its content. A mask node has the bounds of the layer that it masks.
    /// </summary>
    public RectangleF Bounds { readonly get; set; }

    /// <summary>
    /// Gets or sets the blend mode that a layer node composites its group with.
    /// </summary>
    public SvgBlendMode BlendMode { readonly get; set; }

    /// <summary>
    /// Gets or sets how a mask node turns its content into coverage.
    /// </summary>
    public SvgMaskMode MaskMode { readonly get; set; }

    /// <summary>
    /// Gets or sets the paint options of a draw node that differ from the defaults.
    /// </summary>
    public SvgDrawFlags Flags { readonly get; set; }
}
