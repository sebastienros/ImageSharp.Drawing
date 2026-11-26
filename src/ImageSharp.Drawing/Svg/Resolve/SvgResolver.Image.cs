// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.IO.Compression;
using System.Numerics;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The <c>image</c> element.
/// </content>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// The deepest nesting of SVG documents inside <c>image</c> elements. Nested documents never read files, so only
    /// <c>data:</c> URIs can nest, and this bounds their recursion.
    /// </summary>
    private const int MaxImageNesting = 4;

    /// <summary>
    /// The largest size of a decompressed <c>svgz</c> resource, so that a small compressed resource cannot expand into
    /// an unbounded amount of memory.
    /// </summary>
    internal const int MaxDecompressedSvgBytes = 64 * 1024 * 1024;

    /// <summary>
    /// The font collection of nested SVG documents: a collection without fonts, so their text draws nothing.
    /// </summary>
    private static readonly SixLabors.Fonts.FontCollection NestedDocumentFonts = new();

    /// <summary>
    /// The loaded resources by <c>href</c>, so each distinct source decodes once. A failed or refused load is kept as
    /// <see langword="null"/>. Created on first use, so documents without the feature allocate nothing for it.
    /// </summary>
    private Dictionary<string, LoadedImage?>? loadedImages;

    /// <summary>
    /// The load options of the document, which nested SVG documents take over with their own limits.
    /// </summary>
    private readonly SvgLoadOptions loadOptions;

    /// <summary>
    /// The decoded pixels of all raster images so far, counted against <see cref="SvgLimits.MaxImagePixels"/>, nested
    /// documents included.
    /// </summary>
    private long decodedImagePixels;

    /// <summary>
    /// The elements of all nested SVG documents so far, counted against <see cref="SvgLimits.MaxElements"/> with the
    /// elements of this document.
    /// </summary>
    private int nestedElements;

    /// <summary>
    /// The nesting level of this document: 0 for the loaded document, 1 for an SVG document inside one of its
    /// <c>image</c> elements, and so on.
    /// </summary>
    private int imageDepth;

    /// <summary>
    /// Resolves an <c>image</c> element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A raster image becomes one draw node that fills the visible image rectangle with an <see cref="SvgImagePaint"/>.
    /// The <c>preserveAspectRatio</c> of the element maps the image rectangle into the viewport (<c>x</c>, <c>y</c>,
    /// <c>width</c>, <c>height</c>); with <c>slice</c> the image covers the viewport and the visible rectangle is the
    /// viewport, which clips it. <c>image-rendering</c> values that ask for hard edges (<c>optimizeSpeed</c>,
    /// <c>pixelated</c>, <c>crisp-edges</c>) sample the nearest pixel.
    /// </para>
    /// <para>
    /// An SVG image is resolved at load time into its own document, and its nodes are copied into this document under the
    /// placement transform, inside a clip to the visible viewport. Its mapped box (its view box, or the rectangle of its
    /// intrinsic size) takes the place of the image rectangle, and the <c>preserveAspectRatio</c> of the element applies
    /// instead of the one of the referenced root, as SVG specifies.
    /// </para>
    /// <para>
    /// An image that does not load, is refused, or has a zero size draws nothing; the document still renders.
    /// </para>
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    private void ResolveImage(int index, in SvgResolveContext context)
    {
        SvgComputedStyle style = context.Style;
        if (style.Visibility != SvgVisibility.Visible || !this.TryGetImageLayout(index, in context, out LoadedImage? image, out RectangleF viewport))
        {
            return;
        }

        SvgPreserveAspectRatio preserveAspectRatio = this.tree.GetImage(index).PreserveAspectRatio;
        if (image!.Document is SvgDocument document)
        {
            this.AddNestedDocument(document, preserveAspectRatio, viewport, in context);
            return;
        }

        SvgTexture texture = image.Texture!;
        RectangleF source = new(0F, 0F, texture.Width, texture.Height);
        Matrix3x2 placement = SvgViewBoxTransform.Create(source, preserveAspectRatio, viewport);
        RectangleF visible = RectangleF.Intersect(TransformBounds(source, placement), viewport);
        if (!(visible.Width > 0F) || !(visible.Height > 0F))
        {
            return;
        }

        bool nearest = style.ImageRendering is SvgImageRendering.OptimizeSpeed or SvgImageRendering.Pixelated or SvgImageRendering.CrispEdges;

        // One draw node with only a fill, so the opacity of the element multiplies the paint, as for a shape.
        this.nodes.Add(new SvgRenderNode
        {
            Kind = SvgRenderNodeKind.Draw,
            Path = new RectanglePolygon(visible),
            Fill = new SvgImagePaint(texture, placement, nearest, style.Opacity),
            FillRule = IntersectionRule.NonZero,
            TransformIndex = this.GetTransformIndex(context.Transform),
            Bounds = TransformBounds(visible, context.Transform),
        });
    }

    /// <summary>
    /// Returns the object bounding box of an <c>image</c> element: its viewport rectangle, with <c>auto</c> sizes taken
    /// from the image.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    /// <param name="bounds">When this method returns <see langword="true"/>, contains the box in the user space of the element.</param>
    /// <returns><see langword="true"/> if the image loads and has a size; otherwise, <see langword="false"/>.</returns>
    private bool TryGetImageBoundingBox(int index, in SvgResolveContext context, out RectangleF bounds)
        => this.TryGetImageLayout(index, in context, out _, out bounds);

    /// <summary>
    /// Loads the image of an element and computes its viewport.
    /// </summary>
    /// <remarks>
    /// <c>width</c> and <c>height</c> default to <c>auto</c>: the intrinsic size of the image. When only one is given,
    /// the other follows the intrinsic aspect ratio.
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    /// <param name="image">When this method returns <see langword="true"/>, contains the loaded image.</param>
    /// <param name="viewport">When this method returns <see langword="true"/>, contains the viewport in the user space of the element.</param>
    /// <returns><see langword="true"/> if the image loads and the viewport has an area; otherwise, <see langword="false"/>.</returns>
    private bool TryGetImageLayout(int index, in SvgResolveContext context, out LoadedImage? image, out RectangleF viewport)
    {
        viewport = default;
        SvgImageAttributes attributes = this.GetImageAttributes(index, context.Style);
        image = attributes.Href is null ? null : this.LoadImage(attributes.Href);
        if (image is null)
        {
            return false;
        }

        SizeF parent = context.Viewport;
        float fontSize = context.Style.FontSize;
        SvgUnitBasis units = this.units.WithFont(context.Style.Text);
        SizeF intrinsic = image.Size;
        float x = SvgUnits.ToUser(attributes.X, parent.Width, fontSize, units, 0F);
        float y = SvgUnits.ToUser(attributes.Y, parent.Height, fontSize, units, 0F);
        float width = SvgUnits.ToUser(attributes.Width, parent.Width, fontSize, units, 0F);
        float height = SvgUnits.ToUser(attributes.Height, parent.Height, fontSize, units, 0F);
        bool hasWidth = attributes.Width.IsSpecified;
        bool hasHeight = attributes.Height.IsSpecified;

        if (!hasWidth && !hasHeight)
        {
            width = intrinsic.Width;
            height = intrinsic.Height;
        }
        else if (!hasWidth)
        {
            width = intrinsic.Height > 0F ? height * intrinsic.Width / intrinsic.Height : 0F;
        }
        else if (!hasHeight)
        {
            height = intrinsic.Width > 0F ? width * intrinsic.Height / intrinsic.Width : 0F;
        }

        if (!(width > 0F) || !(height > 0F) || !float.IsFinite(width) || !float.IsFinite(height) || !float.IsFinite(x) || !float.IsFinite(y))
        {
            return false;
        }

        viewport = new RectangleF(x, y, width, height);
        return true;
    }

    /// <summary>
    /// Copies the nodes of a nested SVG document into this document.
    /// </summary>
    /// <param name="document">The nested document.</param>
    /// <param name="preserveAspectRatio">The <c>preserveAspectRatio</c> of the <c>image</c> element.</param>
    /// <param name="viewport">The viewport of the element.</param>
    /// <param name="context">The context of the element.</param>
    private void AddNestedDocument(SvgDocument document, SvgPreserveAspectRatio preserveAspectRatio, RectangleF viewport, in SvgResolveContext context)
    {
        ReadOnlySpan<SvgRenderNode> source = document.Nodes;
        RectangleF mappedBox = document.MappedBox;
        if (source.IsEmpty || !(mappedBox.Width > 0F) || !(mappedBox.Height > 0F))
        {
            return;
        }

        // The nested nodes count against the node limit of this document. An image that would exceed it is refused,
        // like an image that exceeds the pixel limit, instead of failing the whole document.
        if (source.Length > this.maxNodes - this.instanceCount)
        {
            return;
        }

        this.instanceCount += source.Length;

        // The nested root clips to its viewport, which is the mapped box under the placement, and the image clips to its
        // own viewport. Both are axis-aligned rectangles in the user space of the element, so one clip covers both.
        Matrix3x2 placement = SvgViewBoxTransform.Create(mappedBox, preserveAspectRatio, viewport);
        RectangleF clip = RectangleF.Intersect(viewport, TransformBounds(mappedBox, placement));
        if (!(clip.Width > 0F) || !(clip.Height > 0F))
        {
            return;
        }

        Matrix3x2 outer = placement * context.Transform;
        ReadOnlySpan<Matrix3x2> transforms = document.Transforms;
        float opacity = context.Style.Opacity;
        int group = this.BeginGroup(opacity);
        this.nodes.Add(new SvgRenderNode
        {
            Kind = SvgRenderNodeKind.BeginClip,
            TransformIndex = this.GetTransformIndex(context.Transform),
            Clip = [new RectanglePolygon(clip)],
            Bounds = TransformBounds(clip, context.Transform),
        });

        // Every node keeps its paths, paints, and clip geometry; only its transform and its document-space bounds move
        // into this document. Begin nodes are linked to their end nodes again when this document is built.
        foreach (SvgRenderNode node in source)
        {
            SvgRenderNode copy = node;
            copy.TransformIndex = this.GetTransformIndex(transforms[node.TransformIndex] * outer);
            copy.Bounds = TransformBounds(node.Bounds, outer);
            this.nodes.Add(copy);
        }

        this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndClip });
        this.EndGroup(group, opacity);
    }

    /// <summary>
    /// Returns the image of an <c>href</c>, loading it on first use.
    /// </summary>
    /// <param name="href">The whole trimmed URL.</param>
    /// <returns>The image, or <see langword="null"/> when it does not load or is refused.</returns>
    private LoadedImage? LoadImage(string href)
    {
        this.loadedImages ??= new Dictionary<string, LoadedImage?>(StringComparer.Ordinal);
        if (this.loadedImages.TryGetValue(href, out LoadedImage? image))
        {
            return image;
        }

        if (SvgImageSource.TryOpen(href, this.ResourceBaseDirectory, out Stream? stream, out string mediaType))
        {
            using (stream)
            {
                image = this.DecodeImage(stream!, mediaType);
            }
        }

        this.loadedImages.Add(href, image);
        return image;
    }

    /// <summary>
    /// Decodes an image resource: an SVG document, compressed or not, or a raster format that the ImageSharp decoders
    /// read.
    /// </summary>
    /// <remarks>
    /// The format comes from the content. A resource is SVG when its media type says <c>image/svg+xml</c>, when it is
    /// gzip-compressed, or when it has no media type and starts with markup. A raster image is identified before it is
    /// decoded, so a resource that declares a huge size is refused by <see cref="SvgLimits.MaxImagePixels"/> without
    /// allocating its pixels. Only the first frame decodes, and the EXIF orientation applies, as in browsers.
    /// </remarks>
    /// <param name="stream">The resource, readable and seekable.</param>
    /// <param name="mediaType">The media type of a <c>data:</c> URI, or an empty string.</param>
    /// <returns>The image, or <see langword="null"/> when the resource is broken, refused, or of an unknown format.</returns>
    private LoadedImage? DecodeImage(Stream stream, string mediaType)
    {
        try
        {
            Span<byte> header = stackalloc byte[64];
            int length = stream.ReadAtLeast(header, header.Length, false);
            header = header[..length];
            stream.Position = 0;

            if (length >= 2 && header[0] == 0x1F && header[1] == 0x8B)
            {
                using MemoryStream decompressed = Decompress(stream);
                return this.LoadNestedDocument(decompressed);
            }

            if (mediaType == "image/svg+xml" || (mediaType.Length == 0 && StartsWithMarkup(header)))
            {
                return this.LoadNestedDocument(stream);
            }

            DecoderOptions options = new() { MaxFrames = 1 };
            ImageInfo info = Image.Identify(options, stream);
            long pixels = (long)info.Width * info.Height;
            if (info.Width <= 0 || info.Height <= 0 || pixels > this.MaxImagePixels - this.decodedImagePixels)
            {
                return null;
            }

            stream.Position = 0;
            using Image<Rgba32> decoded = Image.Load<Rgba32>(options, stream);
            decoded.Mutate(x => x.AutoOrient());

            // Copy the pixels into an array that the document owns, then release the decoder buffers.
            int width = decoded.Width;
            int height = decoded.Height;
            Rgba32[] data = new Rgba32[width * height];
            decoded.CopyPixelDataTo(data);
            this.decodedImagePixels += pixels;
            return new LoadedImage(SvgTexture.FromStraight(data, width, height), null, new SizeF(width, height));
        }
        catch (Exception exception)
            when (exception is ImageFormatException or IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads and resolves an SVG document inside an <c>image</c> element.
    /// </summary>
    /// <remarks>
    /// The nested document takes the load options of this document with three changes: it reads no file, its limits are
    /// what remains of the element, node, and image pixel limits of this document, and it is one level deeper, up to
    /// <see cref="MaxImageNesting"/>.
    /// </remarks>
    /// <param name="stream">The document.</param>
    /// <returns>The image, or <see langword="null"/> when the document is invalid or exceeds a limit.</returns>
    private LoadedImage? LoadNestedDocument(Stream stream)
    {
        SvgLimits limits = this.loadOptions.Limits;
        int elements = limits.MaxElements - this.tree.Count - this.nestedElements;
        int nodes = this.maxNodes - this.instanceCount;
        if (this.imageDepth >= MaxImageNesting || elements <= 0 || nodes <= 0)
        {
            return null;
        }

        SvgLimits nestedLimits = new()
        {
            MaxElements = elements,
            MaxNodes = nodes,
            MaxNestingDepth = limits.MaxNestingDepth,
            MaxCharactersFromEntities = limits.MaxCharactersFromEntities,
            MaxImagePixels = Math.Max(1, this.MaxImagePixels - this.decodedImagePixels),
            MaxPatternTilePixels = limits.MaxPatternTilePixels,
            MaxFilterPixels = limits.MaxFilterPixels,
            MaxLayerPixels = limits.MaxLayerPixels,
            MaxTextCharacters = (int)Math.Max(1, limits.MaxTextCharacters - this.TextCharacters),
        };

        // Text in a nested document draws nothing: the nested document gets a font collection without fonts. This keeps
        // the behavior of resvg and of the reference image of the resvg suite (structure/image/embedded-svg-with-text);
        // Chromium draws such text with the system fonts. See the phase 3 hand-back report.
        SvgLoadOptions options = new()
        {
            Dpi = this.loadOptions.Dpi,
            CurrentColor = this.loadOptions.CurrentColor,
            Limits = nestedLimits,
            Languages = this.loadOptions.Languages,
            UserStyleSheet = this.loadOptions.UserStyleSheet,
            ResourceBaseDirectory = null,
            FontCollection = NestedDocumentFonts,
        };

        try
        {
            SvgReaderOptions readerOptions = new(
                nestedLimits.MaxElements,
                nestedLimits.MaxNestingDepth,
                nestedLimits.MaxCharactersFromEntities,
                options.UserStyleSheet,
                nestedLimits.MaxTextCharacters);
            SvgElementTree tree = SvgReader.Read(stream, in readerOptions);
            SvgResolver resolver = new(tree, options) { imageDepth = this.imageDepth + 1 };
            SvgDocument document = resolver.ResolveDocument();
            this.nestedElements += tree.Count + resolver.nestedElements;
            this.decodedImagePixels += resolver.decodedImagePixels;
            if (resolver.TextCharacters > 0)
            {
                this.TextState.Characters += resolver.TextCharacters;
            }

            return new LoadedImage(null, document, document.Size);
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decompresses a gzip resource into memory, up to <see cref="MaxDecompressedSvgBytes"/>.
    /// </summary>
    /// <param name="stream">The compressed resource.</param>
    /// <returns>The decompressed resource, positioned at its start.</returns>
    /// <exception cref="InvalidDataException">The data is not valid gzip, or it decompresses past the limit.</exception>
    internal static MemoryStream Decompress(Stream stream)
    {
        using GZipStream gzip = new(stream, CompressionMode.Decompress, true);
        MemoryStream result = new();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (result.Length + read > MaxDecompressedSvgBytes)
            {
                result.Dispose();
                throw new InvalidDataException("The compressed SVG image exceeds the decompressed size limit.");
            }

            result.Write(buffer, 0, read);
        }

        result.Position = 0;
        return result;
    }

    /// <summary>
    /// Returns whether a resource starts with markup: an optional UTF-8 byte order mark and white space, then <c>&lt;</c>.
    /// </summary>
    /// <param name="header">The first bytes of the resource.</param>
    /// <returns><see langword="true"/> if the resource starts with markup; otherwise, <see langword="false"/>.</returns>
    private static bool StartsWithMarkup(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            header = header[3..];
        }

        int i = 0;
        while (i < header.Length && header[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            i++;
        }

        return i < header.Length && header[i] == (byte)'<';
    }

    /// <summary>
    /// A loaded <c>image</c> resource: decoded raster pixels or a resolved SVG document, and its intrinsic size.
    /// </summary>
    private sealed class LoadedImage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LoadedImage"/> class.
        /// </summary>
        /// <param name="texture">The decoded pixels of a raster image, or <see langword="null"/>.</param>
        /// <param name="document">The resolved document of an SVG image, or <see langword="null"/>.</param>
        /// <param name="size">The intrinsic size in user units.</param>
        public LoadedImage(SvgTexture? texture, SvgDocument? document, SizeF size)
        {
            this.Texture = texture;
            this.Document = document;
            this.Size = size;
        }

        /// <summary>
        /// Gets the decoded pixels of a raster image, or <see langword="null"/> for an SVG image.
        /// </summary>
        public SvgTexture? Texture { get; }

        /// <summary>
        /// Gets the resolved document of an SVG image, or <see langword="null"/> for a raster image.
        /// </summary>
        public SvgDocument? Document { get; }

        /// <summary>
        /// Gets the intrinsic size in user units: the pixel size of a raster image, or the intrinsic size of an SVG
        /// document.
        /// </summary>
        public SizeF Size { get; }
    }
}
