// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A static SVG document that is ready to render. Load it once and draw it with
/// <see cref="Processing.DrawingCanvas.DrawSvg(SvgDocument, RectangleF)"/> as many times as needed, or render it into a
/// new image with <see cref="Render{TPixel}(Configuration, Size, Color)"/>.
/// </summary>
/// <remarks>
/// <para>
/// A document does not change after it loads, does not depend on the output size, and is safe to draw from several
/// threads at the same time.
/// </para>
/// <para>
/// Loading never reads an external resource. A document that is not well-formed XML, whose root element is not an
/// <c>svg</c> element in the SVG namespace, or that exceeds a limit of <see cref="SvgLoadOptions.Limits"/>, does not load.
/// An invalid attribute value does not stop the load: as SVG requires, the attribute counts as unspecified, and an
/// element whose required geometry is invalid does not render.
/// </para>
/// </remarks>
public sealed partial class SvgDocument
{
    private readonly SvgRenderNode[] nodes;
    private readonly Matrix3x2[] transforms;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgDocument"/> class.
    /// </summary>
    /// <param name="nodes">The render nodes in paint order.</param>
    /// <param name="transforms">The distinct transforms that the nodes index, to document user space.</param>
    /// <param name="size">The intrinsic size.</param>
    /// <param name="viewBox">The root view box, or <see langword="null"/>.</param>
    /// <param name="mappedBox">The rectangle of document user space that maps into the destination of a render.</param>
    /// <param name="preserveAspectRatio">The <c>preserveAspectRatio</c> of the root element.</param>
    /// <param name="maxScopeDepth">The deepest nesting of layer and clip scopes in the nodes.</param>
    /// <param name="maxLayerPixels">The largest total number of pixels of the layers that one draw may keep open, from <see cref="SvgLimits.MaxLayerPixels"/>.</param>
    /// <param name="filters">The filters that the filter nodes index, or <see langword="null"/> for none.</param>
    internal SvgDocument(
        SvgRenderNode[] nodes,
        Matrix3x2[] transforms,
        SizeF size,
        RectangleF? viewBox,
        RectangleF mappedBox,
        SvgPreserveAspectRatio preserveAspectRatio,
        int maxScopeDepth,
        long maxLayerPixels,
        SvgFilterNode[]? filters = null)
    {
        this.Filters = filters ?? [];
        this.MaxLayerPixels = maxLayerPixels;
        this.nodes = nodes;
        this.transforms = transforms;
        this.Size = size;
        this.ViewBox = viewBox;
        this.MappedBox = mappedBox;
        this.PreserveAspectRatio = preserveAspectRatio;
        this.MaxScopeDepth = maxScopeDepth;
    }

    /// <summary>
    /// Gets the intrinsic size of the document in pixels. It comes from the <c>width</c> and <c>height</c> of the root
    /// element; a missing or percentage side follows the aspect ratio of the <c>viewBox</c>, and a document with neither
    /// has the size 300 x 150.
    /// </summary>
    public SizeF Size { get; }

    /// <summary>
    /// Gets the <c>viewBox</c> of the root element, or <see langword="null"/> when the root element has no valid one.
    /// </summary>
    public RectangleF? ViewBox { get; }

    /// <summary>
    /// Gets the render nodes in paint order.
    /// </summary>
    internal ReadOnlySpan<SvgRenderNode> Nodes => this.nodes;

    /// <summary>
    /// Gets the distinct transforms that the nodes index. Each maps a node user space to document user space.
    /// </summary>
    internal ReadOnlySpan<Matrix3x2> Transforms => this.transforms;

    /// <summary>
    /// Gets the rectangle of document user space that maps into the destination of a render: the view box, or the
    /// rectangle of the intrinsic size at the origin.
    /// </summary>
    internal RectangleF MappedBox { get; }

    /// <summary>
    /// Gets the <c>preserveAspectRatio</c> of the root element.
    /// </summary>
    internal SvgPreserveAspectRatio PreserveAspectRatio { get; }

    /// <summary>
    /// Gets the deepest nesting of layer and clip scopes in <see cref="Nodes"/>, so a render can size its scope stack.
    /// </summary>
    internal int MaxScopeDepth { get; }

    /// <summary>
    /// Gets the largest total number of pixels of the layers that one draw of this document may keep open at the same
    /// time: <see cref="SvgLimits.MaxLayerPixels"/> of the load options.
    /// </summary>
    internal long MaxLayerPixels { get; }

    /// <summary>
    /// Gets the filters that the <see cref="SvgRenderNodeKind.Filter"/> nodes of this document index.
    /// </summary>
    internal SvgFilterNode[] Filters { get; }

    /// <summary>
    /// Loads a document from a stream with the default options. The encoding comes from the XML declaration or the byte
    /// order mark. The stream is not closed.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">The input is not a valid SVG document, or it exceeds a limit.</exception>
    public static SvgDocument Load(Stream stream) => Load(stream, new SvgLoadOptions());

    /// <summary>
    /// Loads a document from a stream. The encoding comes from the XML declaration or the byte order mark. The stream is
    /// not closed.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="options">The load options.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">The input is not a valid SVG document, or it exceeds a limit.</exception>
    public static SvgDocument Load(Stream stream, SvgLoadOptions options)
    {
        Guard.NotNull(stream, nameof(stream));
        Guard.NotNull(options, nameof(options));

        SvgReaderOptions readerOptions = CreateReaderOptions(options);
        return SvgResolver.Resolve(SvgReader.Read(stream, in readerOptions), options).WithLoadLimits(options);
    }

    /// <summary>
    /// Loads a document from a file with the default options.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">The file is not a valid SVG document, or it exceeds a limit.</exception>
    public static SvgDocument Load(string path) => Load(path, new SvgLoadOptions());

    /// <summary>
    /// Loads a document from a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="options">The load options.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">The file is not a valid SVG document, or it exceeds a limit.</exception>
    public static SvgDocument Load(string path, SvgLoadOptions options)
    {
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(options, nameof(options));

        using FileStream stream = File.OpenRead(path);
        return Load(stream, options);
    }

    /// <summary>
    /// Parses a document from its text with the default options.
    /// </summary>
    /// <param name="svg">The document text.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidImageContentException">The text is not a valid SVG document, or it exceeds a limit.</exception>
    public static SvgDocument Parse(ReadOnlySpan<char> svg) => Parse(svg, new SvgLoadOptions());

    /// <summary>
    /// Parses a document from its text.
    /// </summary>
    /// <param name="svg">The document text.</param>
    /// <param name="options">The load options.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">The text is not a valid SVG document, or it exceeds a limit.</exception>
    public static SvgDocument Parse(ReadOnlySpan<char> svg, SvgLoadOptions options)
    {
        Guard.NotNull(options, nameof(options));

        SvgReaderOptions readerOptions = CreateReaderOptions(options);
        return SvgResolver.Resolve(SvgReader.Read(svg, in readerOptions), options).WithLoadLimits(options);
    }

    /// <summary>
    /// Creates the transform from document user space to canvas coordinates for a destination rectangle: the mapped box
    /// fits the destination following the <c>preserveAspectRatio</c> of the root element.
    /// </summary>
    /// <param name="destination">The destination rectangle. Its width and height are greater than zero.</param>
    /// <returns>The transform. It only scales and translates.</returns>
    internal Matrix3x2 CreateViewportTransform(RectangleF destination)
        => SvgViewBoxTransform.Create(this.MappedBox, this.PreserveAspectRatio, destination);

    /// <summary>
    /// Creates the reader options from the load options.
    /// </summary>
    /// <param name="options">The load options.</param>
    /// <returns>The reader options.</returns>
    private static SvgReaderOptions CreateReaderOptions(SvgLoadOptions options)
    {
        SvgLimits limits = options.Limits;
        return new SvgReaderOptions(
            limits.MaxElements,
            limits.MaxNestingDepth,
            limits.MaxCharactersFromEntities,
            options.UserStyleSheet,
            limits.MaxTextCharacters);
    }
}
