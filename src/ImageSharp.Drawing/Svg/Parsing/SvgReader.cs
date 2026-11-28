// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Reads an SVG document into an <see cref="SvgElementTree"/>. This is the entry point of the parse stage.
/// </summary>
/// <remarks>
/// <para>
/// The reader makes one forward pass over an <see cref="XmlReader"/>. It does not build an XML DOM and does not recurse on
/// the XML depth. Element and attribute names are matched by reference, through a name table that returns the known names
/// as constants (<see cref="SvgNameTable"/>). Attribute values are parsed from spans into typed fields as they are read;
/// <c>style</c> element text and the user style sheet are parsed into a style sheet. After the last element, the cascade
/// combines presentation attributes, style sheet rules, and <c>style</c> attributes into one specified style per element.
/// </para>
/// <para>
/// Raw attribute values are not kept, except for the attributes whose names attribute selectors test. Those are recorded
/// from the element where the reader learns the name on. A <c>style</c> element can follow the elements it selects, so
/// when the style sheets need raw values of earlier elements, or the text content that <c>:empty</c> tests, the reader
/// reads the input a second time after the first pass: up to the last element it needs for attributes, or whole for
/// <c>:empty</c>. Documents whose style sheets use neither pay nothing for either.
/// </para>
/// <para>
/// The XML reader resolves no external resource: DTD processing is on so that internal entities work, as Adobe
/// Illustrator exports use them, but <see cref="XmlReaderSettings.XmlResolver"/> is <see langword="null"/>, and entity
/// expansion is capped by <see cref="SvgReaderOptions.MaxCharactersFromEntities"/>.
/// </para>
/// <para>
/// Malformed XML, a root element that is not <c>svg</c> in the SVG namespace, and an exceeded limit throw
/// <see cref="InvalidImageContentException"/>. An invalid attribute value never throws: the attribute counts as
/// unspecified, as SVG error handling requires.
/// </para>
/// </remarks>
internal sealed class SvgReader
{
    private readonly SvgReaderOptions options;
    private readonly SvgPropertyParser properties = new();
    private readonly SvgStringPool names = new();
    private readonly Tables tables;
    private readonly List<SvgStyleSources> styleSources = [];
    private readonly List<int> openElements = [];
    private int styleElement = -1;

    // The number of open text elements. Character data is kept while it is above zero.
    private int textDepth;
    private string? styleText;
    private string? styleMedia;
    private StringBuilder? styleBuilder;

    // The raw values of the attributes that attribute selectors test, and for each tested name the first element whose
    // attributes were recorded while reading. Both stay null for documents without attribute selectors.
    private Dictionary<string, int>? attributeCoverage;
    private SvgCssAttributeValues? attributeValues;

    // The style sheet is created by the first style sheet that the document or the options have, so documents without
    // CSS allocate none.
    private SvgStyleSheet? styleSheet;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgReader"/> class.
    /// </summary>
    /// <param name="options">The input limits.</param>
    private SvgReader(in SvgReaderOptions options)
    {
        this.options = options;
        this.tables = new Tables(this.properties.DashLengths);

        // The user style sheet is known before the first element, so its attribute names are recorded from the start.
        if (!string.IsNullOrEmpty(options.UserStyleSheet))
        {
            SvgCalcTable? previous = SvgCalcTable.Enter(this.tables.Calc);
            try
            {
                SvgCssParser.ParseStyleSheet(options.UserStyleSheet, this.StyleSheet, this.properties, this.names, true, -1);
            }
            finally
            {
                SvgCalcTable.Exit(previous);
            }

            this.UpdateAttributeCoverage();
        }
    }

    /// <summary>
    /// Gets the style sheet, creating it on first use.
    /// </summary>
    private SvgStyleSheet StyleSheet => this.styleSheet ??= new SvgStyleSheet();

    /// <summary>
    /// Reads an SVG document from a stream. The XML reader detects the encoding. The stream is not closed.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="options">The input limits.</param>
    /// <returns>The element tree.</returns>
    /// <exception cref="InvalidImageContentException">The input is not a valid SVG document, or a limit is exceeded.</exception>
    public static SvgElementTree Read(Stream stream, in SvgReaderOptions options)
    {
        if (!stream.CanSeek)
        {
            return ReadBuffered(stream, in options);
        }

        SvgNameTable nameTable = new();
        XmlReaderSettings settings = CreateSettings(in options, nameTable);
        Encoding? legacyEncoding = DetectLegacyEncoding(stream);
        Source source = new(null, stream, stream.Position, legacyEncoding);
        if (legacyEncoding is null)
        {
            using XmlReader xml = XmlReader.Create(stream, settings);
            return new SvgReader(in options).ReadDocument(xml, in source);
        }

        // The XML reader ignores the encoding declaration when it reads characters, so the decoder chosen here applies.
        using StreamReader text = new(stream, legacyEncoding, false, -1, true);
        using XmlReader legacyXml = XmlReader.Create(text, settings);
        return new SvgReader(in options).ReadDocument(legacyXml, in source);
    }

    /// <summary>
    /// Reads an SVG document from a stream that cannot seek. The stream is copied into pooled memory first, so that the
    /// reader can read the start of the document a second time when a style sheet needs raw attribute values.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="options">The input limits.</param>
    /// <returns>The element tree.</returns>
    private static SvgElementTree ReadBuffered(Stream stream, in SvgReaderOptions options)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        int length = 0;
        try
        {
            while (true)
            {
                if (length == buffer.Length)
                {
                    byte[] larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, length).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                int read = stream.Read(buffer, length, buffer.Length - length);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            using MemoryStream copy = new(buffer, 0, length, false);
            return Read(copy, in options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Finds a legacy encoding, such as Windows-1251, that the XML declaration of a seekable stream names and that .NET
    /// does not provide without an encoding provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="XmlReader"/> looks the declared encoding up with <see cref="Encoding.GetEncoding(string)"/>, which knows
    /// only the encodings built into .NET unless the application registers <see cref="CodePagesEncodingProvider"/>
    /// globally. Registering it here would change the behavior of the whole process, so the provider is asked directly
    /// instead and the stream is decoded with the result.
    /// </para>
    /// <para>
    /// The method reads the start of the stream and seeks back. A stream that cannot seek is copied into memory before it
    /// gets here (see <see cref="ReadBuffered(Stream, in SvgReaderOptions)"/>). A byte order mark or an encoding that the
    /// provider does not supply, which includes UTF-8 and UTF-16, leaves the stream to the XML reader.
    /// </para>
    /// </remarks>
    /// <param name="stream">The stream, at the start of the document.</param>
    /// <returns>The encoding, or <see langword="null"/> when the XML reader handles the encoding itself.</returns>
    private static Encoding? DetectLegacyEncoding(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return null;
        }

        // The XML declaration comes first and is short, so a small prefix holds it.
        long position = stream.Position;
        Span<byte> buffer = stackalloc byte[256];
        int read = stream.ReadAtLeast(buffer, buffer.Length, false);
        stream.Position = position;

        // Only an encoding that is ASCII compatible can start with the ASCII text of the declaration.
        ReadOnlySpan<byte> prefix = buffer[..read];
        int end = prefix.IndexOf("?>"u8);
        if (!prefix.StartsWith("<?xml"u8) || end < 0)
        {
            return null;
        }

        ReadOnlySpan<byte> declaration = prefix[..end];
        int attribute = declaration.IndexOf("encoding"u8);
        if (attribute < 0)
        {
            return null;
        }

        ReadOnlySpan<byte> rest = declaration[(attribute + "encoding"u8.Length)..].TrimStart(" \t\r\n"u8);
        if (rest.IsEmpty || rest[0] != '=')
        {
            return null;
        }

        rest = rest[1..].TrimStart(" \t\r\n"u8);
        if (rest.IsEmpty || rest[0] is not ((byte)'"' or (byte)'\''))
        {
            return null;
        }

        int close = rest[1..].IndexOf(rest[0]);
        if (close <= 0)
        {
            return null;
        }

        // The common UTF-8 declaration needs no lookup and no string.
        ReadOnlySpan<byte> name = rest.Slice(1, close);
        if (Ascii.EqualsIgnoreCase(name, "utf-8"u8))
        {
            return null;
        }

        return CodePagesEncodingProvider.Instance.GetEncoding(Encoding.ASCII.GetString(name));
    }

    /// <summary>
    /// Reads an SVG document from a string.
    /// </summary>
    /// <param name="svg">The document text.</param>
    /// <param name="options">The input limits.</param>
    /// <returns>The element tree.</returns>
    /// <exception cref="InvalidImageContentException">The input is not a valid SVG document, or a limit is exceeded.</exception>
    public static SvgElementTree Read(string svg, in SvgReaderOptions options)
    {
        SvgNameTable nameTable = new();
        using StringReader text = new(svg);
        using XmlReader xml = XmlReader.Create(text, CreateSettings(in options, nameTable));
        Source source = new(svg, null, 0, null);
        return new SvgReader(in options).ReadDocument(xml, in source);
    }

    /// <summary>
    /// Reads an SVG document from characters. <see cref="XmlReader"/> reads from a <see cref="TextReader"/>, so this
    /// overload copies the characters into one string first.
    /// </summary>
    /// <param name="svg">The document text.</param>
    /// <param name="options">The input limits.</param>
    /// <returns>The element tree.</returns>
    /// <exception cref="InvalidImageContentException">The input is not a valid SVG document, or a limit is exceeded.</exception>
    public static SvgElementTree Read(ReadOnlySpan<char> svg, in SvgReaderOptions options) => Read(svg.ToString(), in options);

    /// <summary>
    /// Creates the XML reader settings for untrusted input.
    /// </summary>
    /// <param name="options">The input limits.</param>
    /// <param name="nameTable">The name table of the read.</param>
    /// <returns>The settings.</returns>
    private static XmlReaderSettings CreateSettings(in SvgReaderOptions options, XmlNameTable nameTable)
        => new()
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null,
            MaxCharactersFromEntities = options.MaxCharactersFromEntities,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,

            // White space between elements is read so that text elements keep the spaces between their children. The
            // reader skips such nodes outside text elements without reading their value.
            IgnoreWhitespace = false,
            CloseInput = false,
            NameTable = nameTable,
        };

    /// <summary>
    /// Reads every node, then runs the cascade. The math functions that mix units go to the table of this document while
    /// it reads (<see cref="SvgCalcTable.Current"/>).
    /// </summary>
    /// <param name="xml">The XML reader.</param>
    /// <param name="source">The input, for a second read.</param>
    /// <returns>The element tree.</returns>
    private SvgElementTree ReadDocument(XmlReader xml, in Source source)
    {
        SvgCalcTable? previous = SvgCalcTable.Enter(this.tables.Calc);
        try
        {
            return this.ReadDocumentCore(xml, in source);
        }
        finally
        {
            SvgCalcTable.Exit(previous);
        }
    }

    /// <summary>
    /// Reads every node, then runs the cascade.
    /// </summary>
    /// <param name="xml">The XML reader.</param>
    /// <param name="source">The input, for a second read.</param>
    /// <returns>The element tree.</returns>
    private SvgElementTree ReadDocumentCore(XmlReader xml, in Source source)
    {
        try
        {
            while (xml.Read())
            {
                switch (xml.NodeType)
                {
                    case XmlNodeType.Element:
                        this.ReadElement(xml);
                        break;
                    case XmlNodeType.EndElement:
                        this.CloseElement();
                        break;
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.SignificantWhitespace:
                        // Character data is kept inside text elements; elsewhere only the text directly inside a style
                        // element is read.
                        if (this.textDepth > 0)
                        {
                            this.AddTextNode(xml.Value);
                        }
                        else if (this.styleElement >= 0 && this.openElements[^1] == this.styleElement)
                        {
                            this.AppendStyleText(xml.Value);
                        }

                        break;
                    case XmlNodeType.Whitespace:
                        // White space between elements matters only inside text elements, where it can separate words.
                        if (this.textDepth > 0)
                        {
                            this.AddTextNode(xml.Value);
                        }

                        break;
                }
            }
        }
        catch (XmlException exception)
        {
            throw new InvalidImageContentException($"The SVG document is not well-formed XML: {exception.Message}", exception);
        }

        if (this.tables.Elements.Count == 0)
        {
            throw new InvalidImageContentException("The SVG document has no root element.");
        }

        SvgStyleSheet? sheet = this.styleSheet;
        sheet?.Complete(sheet.HasMediaQueries ? this.ComputeMediaViewport() : default);
        SvgCssMatcher? matcher = null;
        if (sheet?.Count > 0)
        {
            bool[]? content = null;
            if (sheet.UsesEmpty)
            {
                content = new bool[this.tables.Elements.Count];
            }

            // Elements read before a tested attribute name became known, and the text content that :empty tests, come
            // from a second read of the input. Documents without such selectors never get here with work to do.
            int backfillEnd = 0;
            if (this.attributeCoverage is not null)
            {
                foreach (int from in this.attributeCoverage.Values)
                {
                    backfillEnd = Math.Max(backfillEnd, from);
                }
            }

            if (backfillEnd > 0 || content is not null)
            {
                this.ReadAgain(in source, backfillEnd, content);
            }

            this.attributeValues?.Complete(this.tables.Elements.Count);
            matcher = new SvgCssMatcher(sheet, this.tables.Elements, this.tables.ClassNames, this.attributeValues, content)
            {
                Languages = this.tables.Languages,
            };
        }

        SvgCascade.Apply(
            CollectionsMarshal.AsSpan(this.tables.Elements),
            CollectionsMarshal.AsSpan(this.styleSources),
            this.tables.Styles,
            sheet,
            matcher);

        if (this.properties.HasTransformDeclarations)
        {
            this.ApplyTransformProperty();
        }

        return new SvgElementTree(this.tables, sheet?.Count ?? 0);
    }

    /// <summary>
    /// Replaces the <c>transform</c> attribute of every element whose cascaded style specifies the CSS
    /// <c>transform</c> property.
    /// </summary>
    /// <remarks>
    /// SVG 2 makes the attribute the presentation attribute of the property, so a style sheet rule or a <c>style</c>
    /// attribute always wins over it. Writing the result into the transform table keeps one source of truth for the
    /// resolve stage. The pass runs in document order, so for <c>inherit</c> the parent already holds its final
    /// transform. <c>none</c>, <c>initial</c>, and <c>unset</c> (the property does not inherit) remove the transform.
    /// </remarks>
    private void ApplyTransformProperty()
    {
        Span<SvgElement> elements = CollectionsMarshal.AsSpan(this.tables.Elements);
        ReadOnlySpan<SvgSpecifiedStyle> styles = this.tables.Styles.AsSpan();
        for (int i = 0; i < elements.Length; i++)
        {
            ref SvgElement element = ref elements[i];
            if (element.StyleIndex < 0)
            {
                continue;
            }

            ref readonly SvgSpecifiedStyle style = ref styles[element.StyleIndex];
            if (!style.IsSpecified(SvgPropertyId.Transform))
            {
                continue;
            }

            if (style.IsInherit(SvgPropertyId.Transform))
            {
                element.TransformIndex = element.Parent >= 0 ? elements[element.Parent].TransformIndex : -1;
            }
            else if (style.HasValue(SvgPropertyId.Transform) && !style.Extended!.Geometry!.Transform.IsIdentity)
            {
                element.TransformIndex = this.tables.Transforms.Count;
                this.tables.Transforms.Add(style.Extended.Geometry.Transform);
            }
            else
            {
                element.TransformIndex = -1;
            }
        }
    }

    /// <summary>
    /// Collects a text or CDATA node of the open style element. The common single node is kept as is; further nodes go
    /// to a builder, so many small nodes do not cost quadratic copying.
    /// </summary>
    /// <param name="text">The node text.</param>
    private void AppendStyleText(string text)
    {
        if (this.styleText is null)
        {
            this.styleText = text;
            return;
        }

        this.styleBuilder ??= new StringBuilder();
        if (this.styleBuilder.Length == 0)
        {
            _ = this.styleBuilder.Append(this.styleText);
        }

        _ = this.styleBuilder.Append(text);
    }

    /// <summary>
    /// Adds the element at the reader position, with its attributes, to the tree.
    /// </summary>
    /// <param name="xml">The XML reader, positioned on an element.</param>
    private void ReadElement(XmlReader xml)
    {
        List<SvgElement> elements = this.tables.Elements;
        if (this.openElements.Count >= this.options.MaxNestingDepth)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the nesting depth limit of {this.options.MaxNestingDepth}.");
        }

        if (elements.Count >= this.options.MaxElements)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the element limit of {this.options.MaxElements}.");
        }

        // The name table atomizes names, so known names are the SvgNames constants and compare by reference.
        string localName = xml.LocalName;
        bool isSvgNamespace = ReferenceEquals(xml.NamespaceURI, SvgNames.SvgNamespace);
        SvgElementKind kind = SvgElementKind.Unknown;
        if (isSvgNamespace && SvgNames.ElementKinds.TryGetValue(localName, out SvgElementKind known))
        {
            kind = known;
        }

        if (elements.Count == 0 && kind != SvgElementKind.Svg)
        {
            throw new InvalidImageContentException("The root element of the document is not an svg element in the SVG namespace.");
        }

        int index = elements.Count;
        elements.Add(new SvgElement
        {
            Kind = kind,
            LocalName = localName,
            Parent = this.openElements.Count > 0 ? this.openElements[^1] : -1,
            SubtreeEnd = index + 1,
            ClassStart = this.tables.ClassNames.Count,
            DataIndex = this.tables.AddData(kind),
            TransformIndex = -1,
            StyleIndex = -1,
        });

        this.ReadAttributes(xml, index);

        // ReadAttributes demotes a style element of another type to unknown, so the kind is read again.
        if (elements[index].Kind == SvgElementKind.Style)
        {
            this.styleText = null;
            this.styleElement = index;
        }

        // A text element records where the character data of its subtree starts in the node table.
        bool isText = kind == SvgElementKind.Text;
        if (isText)
        {
            SvgTextTables text = this.tables.Text!;
            CollectionsMarshal.AsSpan(text.TextAttributes)[elements[index].DataIndex].FirstTextNode = text.Nodes.Count;
        }

        if (xml.IsEmptyElement)
        {
            this.EndElement(index);
        }
        else
        {
            this.openElements.Add(index);
            if (isText)
            {
                this.textDepth++;
            }
        }
    }

    /// <summary>
    /// Ends the innermost open element.
    /// </summary>
    private void CloseElement()
    {
        int index = this.openElements[^1];
        this.openElements.RemoveAt(this.openElements.Count - 1);
        if (this.tables.Elements[index].Kind == SvgElementKind.Text)
        {
            this.textDepth--;
        }

        this.EndElement(index);
    }

    /// <summary>
    /// Adds a run of character data inside a text element to the node table of the innermost open element. A run that
    /// directly follows another run of the same element, such as text after a CDATA section, extends that run.
    /// </summary>
    /// <param name="value">The characters, with entities expanded.</param>
    /// <exception cref="InvalidImageContentException">The character data exceeds <see cref="SvgReaderOptions.MaxTextCharacters"/>.</exception>
    private void AddTextNode(string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        // Character data is kept only inside a text element, which created the text tables.
        SvgTextTables tables = this.tables.Text!;
        if ((long)tables.Length + value.Length > this.options.MaxTextCharacters)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the text character limit of {this.options.MaxTextCharacters}.");
        }

        int start = tables.AppendText(value);
        int parent = this.openElements[^1];
        int position = this.tables.Elements.Count;
        List<SvgTextNode> nodes = tables.Nodes;
        if (nodes.Count > 0)
        {
            SvgTextNode last = nodes[^1];
            if (last.Parent == parent && last.Position == position && last.Start + last.Length == start)
            {
                nodes[^1] = new SvgTextNode(parent, position, last.Start, last.Length + value.Length);
                return;
            }
        }

        nodes.Add(new SvgTextNode(parent, position, start, value.Length));
    }

    /// <summary>
    /// Records the end of an element's subtree and parses the text of a completed style element.
    /// </summary>
    /// <param name="index">The element index.</param>
    private void EndElement(int index)
    {
        List<SvgElement> elements = this.tables.Elements;
        ref SvgElement element = ref CollectionsMarshal.AsSpan(elements)[index];
        element.SubtreeEnd = elements.Count;
        if (element.Kind == SvgElementKind.Text)
        {
            SvgTextTables tables = this.tables.Text!;
            ref SvgTextAttributes text = ref CollectionsMarshal.AsSpan(tables.TextAttributes)[element.DataIndex];
            text.TextNodeCount = tables.Nodes.Count - text.FirstTextNode;
        }

        if (index == this.styleElement)
        {
            if (this.styleBuilder?.Length > 0)
            {
                this.styleText = this.styleBuilder.ToString();
                _ = this.styleBuilder.Clear();
            }

            if (this.styleText is not null)
            {
                SvgStyleSheet sheet = this.StyleSheet;
                int media = this.styleMedia is null ? -1 : sheet.AddMedia(this.styleMedia, -1);
                SvgCssParser.ParseStyleSheet(this.styleText, sheet, this.properties, this.names, false, media);
                this.UpdateAttributeCoverage();
            }

            this.styleElement = -1;
            this.styleText = null;
            this.styleMedia = null;
        }
    }

    /// <summary>
    /// Starts recording the attributes whose names the style sheets test and that are not recorded yet. Recording starts
    /// at the next element; the elements read so far get the values from <see cref="ReadAgain(in Source, int, bool[])"/>.
    /// </summary>
    private void UpdateAttributeCoverage()
    {
        IReadOnlyCollection<string>? names = this.styleSheet?.AttributeNames;
        if (names is null || names.Count == this.attributeCoverage?.Count)
        {
            return;
        }

        this.attributeCoverage ??= new Dictionary<string, int>(StringComparer.Ordinal);
        this.attributeValues ??= new SvgCssAttributeValues();
        foreach (string name in names)
        {
            _ = this.attributeCoverage.TryAdd(name, this.tables.Elements.Count);
        }
    }

    /// <summary>
    /// Computes the viewport that media queries test: the intrinsic size of the document in CSS pixels.
    /// </summary>
    /// <remarks>
    /// The rules are those of <see cref="SvgResolver"/> for the intrinsic size: absolute <c>width</c> and <c>height</c>,
    /// a missing or percentage side from the aspect ratio of the view box, and 300 x 150 without a view box. Relative
    /// units use the initial font size, as media queries do. A browser tests the size that the image is drawn at; the
    /// cascade runs once at load, before any drawing size is known, so it uses the intrinsic size, which is what a
    /// browser draws at by default.
    /// </remarks>
    /// <returns>The viewport size.</returns>
    private SizeF ComputeMediaViewport()
    {
        ref readonly SvgViewportAttributes root = ref CollectionsMarshal.AsSpan(this.tables.Viewports)[this.tables.Elements[0].DataIndex];
        SvgUnitBasis units = SvgUnitBasis.ForRoot(96F);
        bool hasWidth = root.Width.IsSpecified && root.Width.Unit != SvgLengthUnit.Percent;
        bool hasHeight = root.Height.IsSpecified && root.Height.Unit != SvgLengthUnit.Percent;
        float width = hasWidth ? SvgUnits.ToPixels(root.Width, SvgUnitBasis.InitialFontSize, units) : 0F;
        float height = hasHeight ? SvgUnits.ToPixels(root.Height, SvgUnitBasis.InitialFontSize, units) : 0F;
        if (root.HasViewBox && root.ViewBox.Width > 0F && root.ViewBox.Height > 0F)
        {
            RectangleF box = root.ViewBox;
            if (!hasWidth && !hasHeight)
            {
                return box.Size;
            }

            if (!hasWidth)
            {
                width = height * box.Width / box.Height;
            }
            else if (!hasHeight)
            {
                height = width * box.Height / box.Width;
            }

            return new SizeF(width, height);
        }

        return new SizeF(hasWidth ? width : 300F, hasHeight ? height : 150F);
    }

    /// <summary>
    /// Reads the input a second time to collect what the first read did not keep: the raw values of tested attributes
    /// of the elements read before their name became known, and, for <c>:empty</c>, which elements have text content.
    /// </summary>
    /// <remarks>
    /// The second read sees the same XML, so its elements come in the same order and the n-th element start has index n.
    /// Without <paramref name="content"/> it stops after the last element it needs, which is usually close to the start:
    /// a <c>style</c> element mostly sits near the top of the document. The text test needs every element and keeps
    /// whitespace nodes, because Chromium counts whitespace text as content for <c>:empty</c>.
    /// </remarks>
    /// <param name="source">The input.</param>
    /// <param name="end">The index after the last element whose attributes are needed.</param>
    /// <param name="content">The array that receives, per element, whether it has text content; or <see langword="null"/>.</param>
    private void ReadAgain(in Source source, int end, bool[]? content)
    {
        XmlReaderSettings settings = CreateSettings(in this.options, new SvgNameTable());
        settings.IgnoreWhitespace = content is null;
        StringReader? stringReader = null;
        StreamReader? streamReader = null;
        XmlReader xml;
        long resume = 0;
        if (source.Text is not null)
        {
            stringReader = new StringReader(source.Text);
            xml = XmlReader.Create(stringReader, settings);
        }
        else
        {
            Stream stream = source.Stream!;
            resume = stream.Position;
            stream.Position = source.Position;
            if (source.Encoding is null)
            {
                xml = XmlReader.Create(stream, settings);
            }
            else
            {
                streamReader = new StreamReader(stream, source.Encoding, false, -1, true);
                xml = XmlReader.Create(streamReader, settings);
            }
        }

        try
        {
            Dictionary<string, int>? coverage = this.attributeCoverage;
            List<int> open = this.openElements;
            open.Clear();
            int next = 0;
            while (xml.Read())
            {
                switch (xml.NodeType)
                {
                    case XmlNodeType.Element:
                        int index = next++;
                        if (index >= end && content is null)
                        {
                            return;
                        }

                        if (index < end && coverage is not null && xml.MoveToFirstAttribute())
                        {
                            do
                            {
                                if (coverage.TryGetValue(xml.LocalName, out int from) && index < from)
                                {
                                    this.attributeValues!.Add(index, xml.LocalName, xml.NamespaceURI.Length != 0, xml.Value);
                                }
                            }
                            while (xml.MoveToNextAttribute());

                            _ = xml.MoveToElement();
                        }

                        if (!xml.IsEmptyElement)
                        {
                            open.Add(index);
                        }

                        break;
                    case XmlNodeType.EndElement:
                        open.RemoveAt(open.Count - 1);
                        break;
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        if (content is not null && open.Count > 0)
                        {
                            content[open[^1]] = true;
                        }

                        break;
                }
            }
        }
        finally
        {
            xml.Dispose();
            stringReader?.Dispose();
            streamReader?.Dispose();
            if (source.Stream is not null)
            {
                source.Stream.Position = resume;
            }
        }
    }

    /// <summary>
    /// Reads the attributes of the element at the reader position.
    /// </summary>
    /// <param name="xml">The XML reader, positioned on the element.</param>
    /// <param name="index">The element index.</param>
    private void ReadAttributes(XmlReader xml, int index)
    {
        SvgSpecifiedStyle presentation = default;
        SvgSpecifiedStyle inline = default;
        SvgSpecifiedStyle inlineImportant = default;
        string? href = null;
        string? xlinkHref = null;
        string? xmlSpace = null;
        string? lang = null;
        string? xmlLang = null;
        bool isCss = true;
        string? media = null;
        ref SvgElement element = ref CollectionsMarshal.AsSpan(this.tables.Elements)[index];
        SvgElementKind kind = element.Kind;
        Dictionary<string, int>? coverage = this.attributeCoverage;

        if (xml.MoveToFirstAttribute())
        {
            do
            {
                string name = xml.LocalName;
                string ns = xml.NamespaceURI;

                // Attribute selectors test raw values. Only documents whose style sheets have them record any.
                if (coverage is not null && coverage.ContainsKey(name))
                {
                    this.attributeValues!.Add(index, name, ns.Length != 0, xml.Value);
                }

                if (ns.Length == 0)
                {
                    if (SvgNames.Attributes.TryGetValue(name, out SvgAttributeId attribute))
                    {
                        string value = xml.Value;
                        switch (attribute)
                        {
                            case SvgAttributeId.Id:
                                if (value.Length > 0)
                                {
                                    element.Id = value;

                                    // The first element with an id wins.
                                    _ = this.tables.Ids.TryAdd(value, index);
                                }

                                break;
                            case SvgAttributeId.Class:
                                element.ClassCount = this.AddClassNames(value);
                                break;
                            case SvgAttributeId.Style:
                                SvgCssParser.ParseDeclarations(value, this.properties, ref inline, ref inlineImportant);
                                break;
                            case SvgAttributeId.Transform:
                                if (SvgTransformParser.TryParse(value, out Matrix3x2 transform))
                                {
                                    element.TransformIndex = this.tables.Transforms.Count;
                                    this.tables.Transforms.Add(transform);
                                }

                                break;
                            case SvgAttributeId.Href:
                                href = value;
                                break;
                            case SvgAttributeId.Type:
                                // A style element applies only for an absent or empty type, or text/css. Filter
                                // primitives and transfer functions have a type attribute of their own.
                                isCss = value.Length == 0 || value.Equals("text/css", StringComparison.OrdinalIgnoreCase);
                                if (SvgFilterTables.IsPrimitiveRecord(kind))
                                {
                                    this.tables.SetGeometry(kind, element.DataIndex, attribute, value);
                                }

                                break;
                            case SvgAttributeId.SystemLanguage:
                            case SvgAttributeId.RequiredExtensions:
                            case SvgAttributeId.RequiredFeatures:
                                this.tables.SetConditional(index, attribute, value);
                                element.HasConditionalAttributes = true;
                                break;
                            case SvgAttributeId.PathLength:
                                this.tables.SetPathLength(index, kind, value);
                                break;
                            case SvgAttributeId.Lang:
                                lang = value;
                                break;
                            default:
                                // Chromium accepts var() in the attributes that are presentation attributes of the
                                // geometry properties. Such a value is no valid attribute value, so the attribute stays
                                // unspecified, and the declaration is kept at presentation attribute precedence.
                                if (TryGetGeometryProperty(kind, attribute, out SvgPropertyId geometry) && SvgPropertyParser.ContainsVar(value))
                                {
                                    presentation.MarkPending(geometry, SvgNumberParser.Trim(value).ToString(), null);
                                }

                                this.tables.SetGeometry(kind, element.DataIndex, attribute, value);
                                break;
                        }
                    }
                    else if (SvgNames.PresentationAttributes.TryGetValue(name, out SvgPropertyId property))
                    {
                        _ = this.properties.TryParse(property, xml.Value, ref presentation);
                    }
                    else if (ReferenceEquals(name, SvgNames.TextDecoration))
                    {
                        // The only shorthand that is also a presentation attribute.
                        _ = this.properties.TryParseShorthand(SvgShorthand.TextDecoration, xml.Value, ref presentation);
                    }
                    else if (kind == SvgElementKind.Style && name == "media")
                    {
                        // The media query list of the style sheet. Only style elements read it, so the name needs no
                        // entry in the name tables.
                        media = xml.Value;
                    }
                }
                else if (ReferenceEquals(ns, SvgNames.XLinkNamespace) && ReferenceEquals(name, SvgNames.Href))
                {
                    xlinkHref = xml.Value;
                }
                else if (ReferenceEquals(ns, SvgNames.XmlNamespace)
                    && ReferenceEquals(name, SvgNames.Space)
                    && kind is SvgElementKind.Text or SvgElementKind.TSpan or SvgElementKind.TextPath or SvgElementKind.Tref)
                {
                    xmlSpace = xml.Value;
                }
                else if (ReferenceEquals(ns, SvgNames.XmlNamespace) && ReferenceEquals(name, SvgNames.Lang))
                {
                    xmlLang = xml.Value;
                }
            }
            while (xml.MoveToNextAttribute());

            _ = xml.MoveToElement();
        }

        // Chromium maps xml:space on text content elements to white-space at presentation attribute precedence: preserve
        // to pre and any other value to nowrap. A white-space attribute on the same element wins. The value inherits
        // through white-space, so a nested element can switch the mode back.
        if (xmlSpace is not null && !presentation.IsSpecified(SvgPropertyId.WhiteSpace))
        {
            _ = this.properties.TryParse(SvgPropertyId.WhiteSpace, xmlSpace == "preserve" ? "pre" : "nowrap", ref presentation);
        }

        // xml:lang wins over lang when both are present, as Chromium matches :lang().
        string? language = xmlLang ?? lang;
        if (language is not null)
        {
            this.tables.SetLanguage(index, language);
        }

        // href wins over xlink:href when both are present.
        string? reference = href ?? xlinkHref;
        if (reference is not null)
        {
            this.tables.SetHref(kind, element.DataIndex, reference);
        }

        if (kind == SvgElementKind.Style && !isCss)
        {
            // A style sheet of another type is ignored: the element keeps its kind but its text is not read.
            element.Kind = SvgElementKind.Unknown;
        }
        else if (kind == SvgElementKind.Style)
        {
            this.styleMedia = media;
        }

        this.styleSources.Add(new SvgStyleSources(
            this.AddStyle(in presentation),
            this.AddStyle(in inline),
            this.AddStyle(in inlineImportant)));
    }

    /// <summary>
    /// Returns the geometry property whose presentation attribute an attribute of an element is (SVG 2, "Geometry
    /// Properties"): <c>x</c> and <c>y</c> on the elements that have them as positions, <c>width</c> and
    /// <c>height</c> on the sized elements, <c>cx</c>, <c>cy</c>, and <c>r</c> on circles and ellipses, and <c>rx</c> and
    /// <c>ry</c> on rectangles and ellipses. <c>d</c> is left out: Chromium does not accept <c>var()</c> in it.
    /// </summary>
    /// <param name="kind">The element kind.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="property">When this method returns <see langword="true"/>, contains the property.</param>
    /// <returns><see langword="true"/> if the attribute is a geometry presentation attribute on the kind; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetGeometryProperty(SvgElementKind kind, SvgAttributeId attribute, out SvgPropertyId property)
    {
        bool sized = kind is SvgElementKind.Rect or SvgElementKind.Image or SvgElementKind.Svg or SvgElementKind.Symbol
            or SvgElementKind.Use or SvgElementKind.ForeignObject;
        property = attribute switch
        {
            SvgAttributeId.X when sized => SvgPropertyId.X,
            SvgAttributeId.Y when sized => SvgPropertyId.Y,
            SvgAttributeId.Width when sized => SvgPropertyId.Width,
            SvgAttributeId.Height when sized => SvgPropertyId.Height,
            SvgAttributeId.Cx when kind is SvgElementKind.Circle or SvgElementKind.Ellipse => SvgPropertyId.Cx,
            SvgAttributeId.Cy when kind is SvgElementKind.Circle or SvgElementKind.Ellipse => SvgPropertyId.Cy,
            SvgAttributeId.R when kind is SvgElementKind.Circle => SvgPropertyId.R,
            SvgAttributeId.Rx when kind is SvgElementKind.Rect or SvgElementKind.Ellipse => SvgPropertyId.Rx,
            SvgAttributeId.Ry when kind is SvgElementKind.Rect or SvgElementKind.Ellipse => SvgPropertyId.Ry,
            _ => SvgPropertyId.Color
        };

        return property != SvgPropertyId.Color;
    }

    /// <summary>
    /// Adds a style to the style table when it specifies anything.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <returns>The index of the style, or -1 when it is empty.</returns>
    private int AddStyle(in SvgSpecifiedStyle style)
    {
        if (style.IsEmpty)
        {
            return -1;
        }

        this.tables.Styles.Add(style);
        return this.tables.Styles.Count - 1;
    }

    /// <summary>
    /// Splits a <c>class</c> attribute on whitespace and appends the interned names to the class table.
    /// </summary>
    /// <param name="value">The attribute value.</param>
    /// <returns>The number of class names added.</returns>
    private int AddClassNames(string value)
    {
        int count = 0;
        ReadOnlySpan<char> rest = value;
        while (true)
        {
            rest = SvgNumberParser.SkipWhitespace(rest);
            if (rest.IsEmpty)
            {
                return count;
            }

            int length = 0;
            while (length < rest.Length && !SvgNumberParser.IsWhitespace(rest[length]))
            {
                length++;
            }

            this.tables.ClassNames.Add(this.names.Get(rest[..length]));
            rest = rest[length..];
            count++;
        }
    }

    /// <summary>
    /// The input of a read, kept so that the reader can read it a second time.
    /// </summary>
    private readonly struct Source
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Source"/> struct.
        /// </summary>
        /// <param name="text">The document text, or <see langword="null"/> for a stream.</param>
        /// <param name="stream">The seekable stream, or <see langword="null"/> for text.</param>
        /// <param name="position">The position of the document in the stream.</param>
        /// <param name="encoding">The legacy encoding that decodes the stream, or <see langword="null"/> when the XML reader detects it.</param>
        public Source(string? text, Stream? stream, long position, Encoding? encoding)
        {
            this.Text = text;
            this.Stream = stream;
            this.Position = position;
            this.Encoding = encoding;
        }

        /// <summary>
        /// Gets the document text, or <see langword="null"/> for a stream.
        /// </summary>
        public string? Text { get; }

        /// <summary>
        /// Gets the seekable stream, or <see langword="null"/> for text.
        /// </summary>
        public Stream? Stream { get; }

        /// <summary>
        /// Gets the position of the document in the stream.
        /// </summary>
        public long Position { get; }

        /// <summary>
        /// Gets the legacy encoding that decodes the stream, or <see langword="null"/> when the XML reader detects it.
        /// </summary>
        public Encoding? Encoding { get; }
    }

    /// <summary>
    /// The tables that the reader fills and the element tree takes over.
    /// </summary>
    internal sealed class Tables
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Tables"/> class.
        /// </summary>
        /// <param name="dashLengths">The dash length table of the property parser.</param>
        public Tables(List<SvgLength> dashLengths) => this.DashLengths = dashLengths;

        /// <summary>
        /// Gets the elements in document order.
        /// </summary>
        public List<SvgElement> Elements { get; } = [];

        /// <summary>
        /// Gets the class names that elements index into.
        /// </summary>
        public List<string> ClassNames { get; } = [];

        /// <summary>
        /// Gets the transforms that elements index into.
        /// </summary>
        public List<Matrix3x2> Transforms { get; } = [];

        /// <summary>
        /// Gets the styles that elements index into. The table is the largest of the reader (a style is several hundred
        /// bytes and most elements have one), so its storage is pooled; the tree that takes it returns the storage when it
        /// is disposed after the resolve.
        /// </summary>
        public SvgPooledList<SvgSpecifiedStyle> Styles { get; } = new();

        /// <summary>
        /// Gets the dash lengths that <see cref="SvgDashArray"/> values index into.
        /// </summary>
        public List<SvgLength> DashLengths { get; }

        /// <summary>
        /// Gets the math functions that mix units, which lengths of unit <see cref="SvgLengthUnit.Calc"/> index into.
        /// </summary>
        public SvgCalcTable Calc { get; } = new();

        /// <summary>
        /// Gets the attributes of <c>svg</c> and <c>symbol</c> elements.
        /// </summary>
        public List<SvgViewportAttributes> Viewports { get; } = [];

        /// <summary>
        /// Gets the attributes of <c>rect</c> elements.
        /// </summary>
        public List<SvgRectAttributes> Rects { get; } = [];

        /// <summary>
        /// Gets the attributes of <c>circle</c> and <c>ellipse</c> elements.
        /// </summary>
        public List<SvgCircleAttributes> Circles { get; } = [];

        /// <summary>
        /// Gets the attributes of <c>line</c> elements.
        /// </summary>
        public List<SvgLineAttributes> Lines { get; } = [];

        /// <summary>
        /// Gets the point ranges of <c>polyline</c> and <c>polygon</c> elements.
        /// </summary>
        public List<SvgPointsAttributes> PointRanges { get; } = [];

        /// <summary>
        /// Gets the points of every <c>polyline</c> and <c>polygon</c> element.
        /// </summary>
        public List<PointF> Points { get; } = [];

        /// <summary>
        /// Gets the attributes of <c>path</c> elements.
        /// </summary>
        public List<SvgPathAttributes> Paths { get; } = [];

        /// <summary>
        /// Gets the attributes of <c>use</c> elements.
        /// </summary>
        public List<SvgUseAttributes> Uses { get; } = [];

        /// <summary>
        /// Gets the attributes of gradient elements.
        /// </summary>
        public List<SvgGradientAttributes> Gradients { get; } = [];

        /// <summary>
        /// Gets the offsets of <c>stop</c> elements.
        /// </summary>
        public List<float> StopOffsets { get; } = [];

        /// <summary>
        /// Gets the <c>clipPathUnits</c> of <c>clipPath</c> elements.
        /// </summary>
        public List<SvgCoordinateUnits> ClipPathUnits { get; } = [];

        /// <summary>
        /// Gets the attributes of <c>mask</c> elements.
        /// </summary>
        public List<SvgMaskAttributes> Masks { get; } = [];

        /// <summary>
        /// Gets the attributes of <c>pattern</c> elements.
        /// </summary>
        public List<SvgPatternAttributes> Patterns { get; } = [];

        /// <summary>
        /// Gets the attributes of <c>marker</c> elements.
        /// </summary>
        public List<SvgMarkerAttributes> Markers { get; } = [];

        /// <summary>
        /// Gets the attributes of <c>image</c> elements.
        /// </summary>
        public List<SvgImageAttributes> Images { get; } = [];

        /// <summary>
        /// Gets the tables of text content elements, or <see langword="null"/> while the document has none.
        /// </summary>
        public SvgTextTables? Text { get; private set; }

        /// <summary>
        /// Gets the tables of filter elements, or <see langword="null"/> while the document has none.
        /// </summary>
        public SvgFilterTables? Filters { get; private set; }

        /// <summary>
        /// Gets the conditional processing attributes by element index, or <see langword="null"/> when no element has
        /// one. Few documents use them, so the map is created on first use.
        /// </summary>
        public Dictionary<int, SvgConditionalAttributes>? Conditions { get; private set; }

        /// <summary>
        /// Gets the map from id to the first element with that id.
        /// </summary>
        public Dictionary<string, int> Ids { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets the valid <c>pathLength</c> values by element index, or <see langword="null"/> when no element has one.
        /// </summary>
        public Dictionary<int, float>? PathLengths { get; private set; }

        /// <summary>
        /// Gets the language of each element that has an <c>xml:lang</c> or a <c>lang</c> attribute, by element index,
        /// as written; or <see langword="null"/> when no element has one.
        /// </summary>
        public Dictionary<int, string>? Languages { get; private set; }

        /// <summary>
        /// Stores the <c>pathLength</c> attribute of a <c>path</c> or a basic shape. A value that is not a number leaves the
        /// attribute unspecified; zero and negative numbers are kept for the resolve stage, which applies the rules of
        /// SVG 2 to them.
        /// </summary>
        /// <param name="index">The element index.</param>
        /// <param name="kind">The element kind.</param>
        /// <param name="value">The attribute value.</param>
        public void SetPathLength(int index, SvgElementKind kind, string value)
        {
            if (kind is SvgElementKind.Path or SvgElementKind.Rect or SvgElementKind.Circle or SvgElementKind.Ellipse
                    or SvgElementKind.Line or SvgElementKind.Polyline or SvgElementKind.Polygon
                && SvgNumberParser.TryParseNumber(value, out float length))
            {
                (this.PathLengths ??= [])[index] = length;
            }
        }

        /// <summary>
        /// Stores the language attribute of an element.
        /// </summary>
        /// <param name="index">The element index.</param>
        /// <param name="language">The value of <c>xml:lang</c>, or of <c>lang</c> when the element has no <c>xml:lang</c>.</param>
        public void SetLanguage(int index, string language) => (this.Languages ??= [])[index] = language;

        /// <summary>
        /// Returns the tables of text content elements, creating them on first use.
        /// </summary>
        /// <returns>The tables.</returns>
        public SvgTextTables GetOrCreateText() => this.Text ??= new SvgTextTables();

        /// <summary>
        /// Adds the default attribute record for a new element of the given kind.
        /// </summary>
        /// <param name="kind">The element kind.</param>
        /// <returns>The index of the record in the table of the kind, or -1 when the kind has no table.</returns>
        public int AddData(SvgElementKind kind)
        {
            switch (kind)
            {
                case SvgElementKind.Svg:
                case SvgElementKind.Symbol:
                    return AddDefault(this.Viewports);
                case SvgElementKind.Rect:
                    return AddDefault(this.Rects);
                case SvgElementKind.Circle:
                case SvgElementKind.Ellipse:
                    return AddDefault(this.Circles);
                case SvgElementKind.Line:
                    return AddDefault(this.Lines);
                case SvgElementKind.Polyline:
                case SvgElementKind.Polygon:
                    this.PointRanges.Add(new SvgPointsAttributes(this.Points.Count, 0, true));
                    return this.PointRanges.Count - 1;
                case SvgElementKind.Path:
                    return AddDefault(this.Paths);
                case SvgElementKind.Use:
                    return AddDefault(this.Uses);
                case SvgElementKind.LinearGradient:
                case SvgElementKind.RadialGradient:
                    return AddDefault(this.Gradients);
                case SvgElementKind.Stop:
                    return AddDefault(this.StopOffsets);
                case SvgElementKind.ClipPath:
                    return AddDefault(this.ClipPathUnits);
                case SvgElementKind.Mask:
                    return AddDefault(this.Masks);
                case SvgElementKind.Pattern:
                    return AddDefault(this.Patterns);
                case SvgElementKind.Marker:
                    return AddDefault(this.Markers);
                case SvgElementKind.Image:
                    return AddDefault(this.Images);
                case SvgElementKind.Text:
                case SvgElementKind.TSpan:
                case SvgElementKind.Tref:
                    return AddDefault(this.GetOrCreateText().TextAttributes);
                case SvgElementKind.TextPath:
                    return AddDefault(this.GetOrCreateText().TextPaths);
                case SvgElementKind.Filter:
                case >= SvgElementKind.FeBlend:
                    return (this.Filters ??= new SvgFilterTables()).Add(kind);
                default:
                    return -1;
            }
        }

        /// <summary>
        /// Stores a reference attribute on an element kind that has one.
        /// </summary>
        /// <param name="kind">The element kind.</param>
        /// <param name="dataIndex">The index of the attribute record.</param>
        /// <param name="value">The <c>href</c> value.</param>
        public void SetHref(SvgElementKind kind, int dataIndex, string value)
        {
            ReadOnlySpan<char> reference = SvgNumberParser.Trim(value);
            if (kind == SvgElementKind.Image)
            {
                // An image references a resource, not an element, so the whole URL is kept for the resolve stage.
                CollectionsMarshal.AsSpan(this.Images)[dataIndex].Href = reference.Length == value.Length ? value : reference.ToString();
                return;
            }

            // Only a same-document fragment names an element. External references never resolve.
            string? id = reference.Length > 1 && reference[0] == '#' ? reference[1..].ToString() : null;
            switch (kind)
            {
                case SvgElementKind.Use:
                    CollectionsMarshal.AsSpan(this.Uses)[dataIndex].Href = id;
                    break;
                case SvgElementKind.LinearGradient:
                case SvgElementKind.RadialGradient:
                    CollectionsMarshal.AsSpan(this.Gradients)[dataIndex].Href = id;
                    break;
                case SvgElementKind.Pattern:
                    CollectionsMarshal.AsSpan(this.Patterns)[dataIndex].Href = id;
                    break;
                case SvgElementKind.TextPath:
                    CollectionsMarshal.AsSpan(this.Text!.TextPaths)[dataIndex].Href = id;
                    break;
                case SvgElementKind.Tref when id is null:
                    _ = this.Text!.TrefReferences.Remove(dataIndex);
                    break;
                case SvgElementKind.Tref:
                    this.Text!.TrefReferences[dataIndex] = id;
                    break;
                case SvgElementKind.Filter:
                case SvgElementKind.FeImage:
                    this.Filters!.SetHref(kind, dataIndex, value);
                    break;
            }
        }

        /// <summary>
        /// Stores a conditional processing attribute of an element as written.
        /// </summary>
        /// <param name="index">The element index.</param>
        /// <param name="attribute">
        /// <see cref="SvgAttributeId.SystemLanguage"/>, <see cref="SvgAttributeId.RequiredExtensions"/>, or
        /// <see cref="SvgAttributeId.RequiredFeatures"/>.
        /// </param>
        /// <param name="value">The attribute value.</param>
        public void SetConditional(int index, SvgAttributeId attribute, string value)
        {
            this.Conditions ??= [];
            _ = this.Conditions.TryGetValue(index, out SvgConditionalAttributes conditions);
            switch (attribute)
            {
                case SvgAttributeId.SystemLanguage:
                    conditions.SystemLanguage = value;
                    break;
                case SvgAttributeId.RequiredExtensions:
                    conditions.RequiredExtensions = value;
                    break;
                default:
                    conditions.RequiredFeatures = value;
                    break;
            }

            this.Conditions[index] = conditions;
        }

        /// <summary>
        /// Parses a geometry attribute into the attribute record of the element. Attributes that do not apply to the kind
        /// are ignored, and invalid values leave the field unspecified.
        /// </summary>
        /// <param name="kind">The element kind.</param>
        /// <param name="dataIndex">The index of the attribute record.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The attribute value.</param>
        public void SetGeometry(SvgElementKind kind, int dataIndex, SvgAttributeId attribute, string value)
        {
            switch (kind)
            {
                case SvgElementKind.Svg:
                case SvgElementKind.Symbol:
                    SetViewport(ref CollectionsMarshal.AsSpan(this.Viewports)[dataIndex], attribute, value);
                    break;
                case SvgElementKind.Rect:
                    SetRect(ref CollectionsMarshal.AsSpan(this.Rects)[dataIndex], attribute, value);
                    break;
                case SvgElementKind.Circle:
                case SvgElementKind.Ellipse:
                    SetCircle(ref CollectionsMarshal.AsSpan(this.Circles)[dataIndex], kind, attribute, value);
                    break;
                case SvgElementKind.Line:
                    SetLine(ref CollectionsMarshal.AsSpan(this.Lines)[dataIndex], attribute, value);
                    break;
                case SvgElementKind.Polyline:
                case SvgElementKind.Polygon:
                    if (attribute == SvgAttributeId.Points)
                    {
                        int start = this.Points.Count;
                        bool complete = SvgNumberParser.TryParsePoints(value, this.Points);
                        this.PointRanges[dataIndex] = new SvgPointsAttributes(start, this.Points.Count - start, complete);
                    }

                    break;
                case SvgElementKind.Path:
                    if (attribute == SvgAttributeId.D)
                    {
                        // SVG renders path data up to the first error, so the prefix is kept even when it is incomplete.
                        bool complete = Path.TryParseSvgPathPrefix(value, out IPath path);
                        CollectionsMarshal.AsSpan(this.Paths)[dataIndex] = new SvgPathAttributes { Path = path, IsComplete = complete };
                    }

                    break;
                case SvgElementKind.Use:
                    SetUse(ref CollectionsMarshal.AsSpan(this.Uses)[dataIndex], attribute, value);
                    break;
                case SvgElementKind.LinearGradient:
                case SvgElementKind.RadialGradient:
                    SetGradient(ref CollectionsMarshal.AsSpan(this.Gradients)[dataIndex], kind, attribute, value);
                    break;
                case SvgElementKind.Stop:
                    if (attribute == SvgAttributeId.Offset && SvgNumberParser.TryParseClampedFraction(value, out float offset))
                    {
                        this.StopOffsets[dataIndex] = offset;
                    }

                    break;
                case SvgElementKind.ClipPath:
                    if (attribute == SvgAttributeId.ClipPathUnits)
                    {
                        this.ClipPathUnits[dataIndex] = Units(value);
                    }

                    break;
                case SvgElementKind.Mask:
                    SetMask(ref CollectionsMarshal.AsSpan(this.Masks)[dataIndex], attribute, value);
                    break;
                case SvgElementKind.Pattern:
                    SetPattern(ref CollectionsMarshal.AsSpan(this.Patterns)[dataIndex], attribute, value);
                    break;
                case SvgElementKind.Marker:
                    SetMarker(ref CollectionsMarshal.AsSpan(this.Markers)[dataIndex], attribute, value);
                    break;
                case SvgElementKind.Image:
                    SetImage(ref CollectionsMarshal.AsSpan(this.Images)[dataIndex], attribute, value);
                    break;
                case SvgElementKind.Text:
                case SvgElementKind.TSpan:
                case SvgElementKind.Tref:
                    SetText(this.Text!, ref CollectionsMarshal.AsSpan(this.Text!.TextAttributes)[dataIndex], attribute, value);
                    break;
                case SvgElementKind.TextPath:
                    SetTextPath(ref CollectionsMarshal.AsSpan(this.Text!.TextPaths)[dataIndex], attribute, value);
                    break;
                case SvgElementKind.Filter:
                case >= SvgElementKind.FeBlend:
                    this.Filters!.Set(kind, dataIndex, attribute, value);
                    break;
            }
        }

        /// <summary>
        /// Parses a <c>text</c>, <c>tspan</c>, or <c>tref</c> attribute. The position lists go to the list tables.
        /// </summary>
        /// <param name="tables">The text tables.</param>
        /// <param name="text">The record.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetText(SvgTextTables tables, ref SvgTextAttributes text, SvgAttributeId attribute, string value)
        {
            switch (attribute)
            {
                case SvgAttributeId.X:
                    text.X = tables.ParseLengthList(value);
                    break;
                case SvgAttributeId.Y:
                    text.Y = tables.ParseLengthList(value);
                    break;
                case SvgAttributeId.Dx:
                    text.Dx = tables.ParseLengthList(value);
                    break;
                case SvgAttributeId.Dy:
                    text.Dy = tables.ParseLengthList(value);
                    break;
                case SvgAttributeId.Rotate:
                    text.Rotate = tables.ParseRotateList(value);
                    break;
                case SvgAttributeId.TextLength:
                    text.TextLength = NonNegativeLength(value);
                    break;
                case SvgAttributeId.LengthAdjust:
                    text.LengthAdjust = LengthAdjust(value);
                    break;
            }
        }

        /// <summary>
        /// Parses a <c>lengthAdjust</c> value. Keywords are case-sensitive, as in the SVG grammar.
        /// </summary>
        /// <param name="value">The attribute value.</param>
        /// <returns>The value; <see cref="SvgLengthAdjust.Spacing"/> for an invalid value.</returns>
        private static SvgLengthAdjust LengthAdjust(string value)
            => SvgNumberParser.Trim(value) is "spacingAndGlyphs" ? SvgLengthAdjust.SpacingAndGlyphs : SvgLengthAdjust.Spacing;

        /// <summary>
        /// Parses a <c>textPath</c> attribute.
        /// </summary>
        /// <param name="textPath">The record.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetTextPath(ref SvgTextPathAttributes textPath, SvgAttributeId attribute, string value)
        {
            switch (attribute)
            {
                case SvgAttributeId.StartOffset:
                    textPath.StartOffset = Length(value);
                    break;
                case SvgAttributeId.Method:
                    textPath.Method = SvgNumberParser.Trim(value) is "stretch" ? SvgTextPathMethod.Stretch : SvgTextPathMethod.Align;
                    break;
                case SvgAttributeId.Spacing:
                    textPath.Spacing = SvgNumberParser.Trim(value) is "auto" ? SvgTextPathSpacing.Auto : SvgTextPathSpacing.Exact;
                    break;
                case SvgAttributeId.Side:
                    textPath.Side = SvgNumberParser.Trim(value) is "right" ? SvgTextPathSide.Right : SvgTextPathSide.Left;
                    break;
                case SvgAttributeId.PathData:
                    // As for the d attribute, the valid prefix of the path data is kept.
                    _ = Path.TryParseSvgPathPrefix(value, out IPath path);
                    textPath.Path = path;
                    break;
                case SvgAttributeId.TextLength:
                    textPath.TextLength = NonNegativeLength(value);
                    break;
                case SvgAttributeId.LengthAdjust:
                    textPath.LengthAdjust = LengthAdjust(value);
                    break;
            }
        }

        /// <summary>
        /// Appends a default record to a table.
        /// </summary>
        /// <typeparam name="T">The record type.</typeparam>
        /// <param name="table">The table.</param>
        /// <returns>The index of the record.</returns>
        private static int AddDefault<T>(List<T> table)
        {
            table.Add(default!);
            return table.Count - 1;
        }

        /// <summary>
        /// Parses a length, or returns the unspecified length.
        /// </summary>
        /// <param name="value">The attribute value.</param>
        /// <returns>The length.</returns>
        private static SvgLength Length(string value) => SvgLength.TryParse(value, out SvgLength length) ? length : default;

        /// <summary>
        /// Parses a non-negative length, or returns the unspecified length.
        /// </summary>
        /// <param name="value">The attribute value.</param>
        /// <returns>The length.</returns>
        private static SvgLength NonNegativeLength(string value) => SvgLength.TryParseNonNegative(value, out SvgLength length) ? length : default;

        /// <summary>
        /// Parses a coordinate system keyword: <c>userSpaceOnUse</c> or <c>objectBoundingBox</c>. Keywords are
        /// case-sensitive, as in the SVG grammar.
        /// </summary>
        /// <param name="value">The attribute value.</param>
        /// <returns>The units, or <see cref="SvgCoordinateUnits.Unspecified"/> for an invalid value.</returns>
        private static SvgCoordinateUnits Units(string value)
            => SvgNumberParser.Trim(value) switch
            {
                "userSpaceOnUse" => SvgCoordinateUnits.UserSpaceOnUse,
                "objectBoundingBox" => SvgCoordinateUnits.ObjectBoundingBox,
                _ => SvgCoordinateUnits.Unspecified
            };

        /// <summary>
        /// Parses a <c>mask</c> attribute. Negative sizes are kept, because SVG makes them an error that disables the
        /// mask, which differs from an unspecified size.
        /// </summary>
        /// <param name="mask">The record.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetMask(ref SvgMaskAttributes mask, SvgAttributeId attribute, string value)
        {
            switch (attribute)
            {
                case SvgAttributeId.X:
                    mask.X = Length(value);
                    break;
                case SvgAttributeId.Y:
                    mask.Y = Length(value);
                    break;
                case SvgAttributeId.Width:
                    mask.Width = Length(value);
                    break;
                case SvgAttributeId.Height:
                    mask.Height = Length(value);
                    break;
                case SvgAttributeId.MaskUnits:
                    mask.Units = Units(value);
                    break;
                case SvgAttributeId.MaskContentUnits:
                    mask.ContentUnits = Units(value);
                    break;
            }
        }

        /// <summary>
        /// Parses a <c>pattern</c> attribute. Negative sizes are kept, as for <c>mask</c>.
        /// </summary>
        /// <param name="pattern">The record.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetPattern(ref SvgPatternAttributes pattern, SvgAttributeId attribute, string value)
        {
            switch (attribute)
            {
                case SvgAttributeId.X:
                    pattern.X = Length(value);
                    break;
                case SvgAttributeId.Y:
                    pattern.Y = Length(value);
                    break;
                case SvgAttributeId.Width:
                    pattern.Width = Length(value);
                    break;
                case SvgAttributeId.Height:
                    pattern.Height = Length(value);
                    break;
                case SvgAttributeId.PatternUnits:
                    pattern.Units = Units(value);
                    break;
                case SvgAttributeId.PatternContentUnits:
                    pattern.ContentUnits = Units(value);
                    break;
                case SvgAttributeId.PatternTransform:
                    pattern.HasTransform = SvgTransformParser.TryParse(value, out Matrix3x2 transform);
                    pattern.Transform = transform;
                    break;
                case SvgAttributeId.ViewBox:
                    pattern.HasViewBox = SvgNumberParser.TryParseViewBox(value, out RectangleF viewBox);
                    pattern.ViewBox = viewBox;
                    break;
                case SvgAttributeId.PreserveAspectRatio:
                    pattern.HasPreserveAspectRatio = SvgPreserveAspectRatio.TryParse(value, out SvgPreserveAspectRatio preserveAspectRatio);
                    pattern.PreserveAspectRatio = preserveAspectRatio;
                    break;
            }
        }

        /// <summary>
        /// Parses a <c>marker</c> attribute. Negative sizes are kept, as for <c>mask</c>.
        /// </summary>
        /// <param name="marker">The record.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetMarker(ref SvgMarkerAttributes marker, SvgAttributeId attribute, string value)
        {
            switch (attribute)
            {
                case SvgAttributeId.RefX:
                    marker.RefX = Length(value);
                    break;
                case SvgAttributeId.RefY:
                    marker.RefY = Length(value);
                    break;
                case SvgAttributeId.MarkerWidth:
                    marker.MarkerWidth = Length(value);
                    break;
                case SvgAttributeId.MarkerHeight:
                    marker.MarkerHeight = Length(value);
                    break;
                case SvgAttributeId.MarkerUnits:
                    marker.Units = SvgNumberParser.Trim(value) switch
                    {
                        "strokeWidth" => SvgMarkerUnits.StrokeWidth,
                        "userSpaceOnUse" => SvgMarkerUnits.UserSpaceOnUse,
                        _ => SvgMarkerUnits.Unspecified
                    };

                    break;
                case SvgAttributeId.Orient:
                    _ = SvgMarkerOrient.TryParse(value, out SvgMarkerOrient orient);
                    marker.Orient = orient;
                    break;
                case SvgAttributeId.ViewBox:
                    marker.HasViewBox = SvgNumberParser.TryParseViewBox(value, out RectangleF viewBox);
                    marker.ViewBox = viewBox;
                    break;
                case SvgAttributeId.PreserveAspectRatio:
                    _ = SvgPreserveAspectRatio.TryParse(value, out SvgPreserveAspectRatio preserveAspectRatio);
                    marker.PreserveAspectRatio = preserveAspectRatio;
                    break;
            }
        }

        /// <summary>
        /// Parses an <c>image</c> attribute.
        /// </summary>
        /// <param name="image">The record.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetImage(ref SvgImageAttributes image, SvgAttributeId attribute, string value)
        {
            switch (attribute)
            {
                case SvgAttributeId.X:
                    image.X = Length(value);
                    break;
                case SvgAttributeId.Y:
                    image.Y = Length(value);
                    break;
                case SvgAttributeId.Width:
                    // The keyword auto and an invalid value both leave the length unspecified, which means auto.
                    image.Width = Length(value);
                    break;
                case SvgAttributeId.Height:
                    image.Height = Length(value);
                    break;
                case SvgAttributeId.PreserveAspectRatio:
                    _ = SvgPreserveAspectRatio.TryParse(value, out SvgPreserveAspectRatio preserveAspectRatio);
                    image.PreserveAspectRatio = preserveAspectRatio;
                    break;
            }
        }

        /// <summary>
        /// Parses a <c>rect</c> attribute.
        /// </summary>
        /// <param name="rect">The record.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetRect(ref SvgRectAttributes rect, SvgAttributeId attribute, string value)
        {
            switch (attribute)
            {
                case SvgAttributeId.X:
                    rect.X = Length(value);
                    break;
                case SvgAttributeId.Y:
                    rect.Y = Length(value);
                    break;
                case SvgAttributeId.Width:
                    rect.Width = NonNegativeLength(value);
                    break;
                case SvgAttributeId.Height:
                    rect.Height = NonNegativeLength(value);
                    break;
                case SvgAttributeId.Rx:
                    rect.Rx = NonNegativeLength(value);
                    break;
                case SvgAttributeId.Ry:
                    rect.Ry = NonNegativeLength(value);
                    break;
            }
        }

        /// <summary>
        /// Parses a <c>circle</c> or <c>ellipse</c> attribute.
        /// </summary>
        /// <param name="circle">The record.</param>
        /// <param name="kind">The element kind.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetCircle(ref SvgCircleAttributes circle, SvgElementKind kind, SvgAttributeId attribute, string value)
        {
            switch (attribute)
            {
                case SvgAttributeId.Cx:
                    circle.Cx = Length(value);
                    break;
                case SvgAttributeId.Cy:
                    circle.Cy = Length(value);
                    break;
                case SvgAttributeId.R when kind == SvgElementKind.Circle:
                    circle.R = NonNegativeLength(value);
                    break;
                case SvgAttributeId.Rx when kind == SvgElementKind.Ellipse:
                    circle.Rx = NonNegativeLength(value);
                    break;
                case SvgAttributeId.Ry when kind == SvgElementKind.Ellipse:
                    circle.Ry = NonNegativeLength(value);
                    break;
            }
        }

        /// <summary>
        /// Parses a <c>line</c> attribute.
        /// </summary>
        /// <param name="line">The record.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetLine(ref SvgLineAttributes line, SvgAttributeId attribute, string value)
        {
            switch (attribute)
            {
                case SvgAttributeId.X1:
                    line.X1 = Length(value);
                    break;
                case SvgAttributeId.Y1:
                    line.Y1 = Length(value);
                    break;
                case SvgAttributeId.X2:
                    line.X2 = Length(value);
                    break;
                case SvgAttributeId.Y2:
                    line.Y2 = Length(value);
                    break;
            }
        }

        /// <summary>
        /// Parses a <c>use</c> attribute.
        /// </summary>
        /// <param name="use">The record.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetUse(ref SvgUseAttributes use, SvgAttributeId attribute, string value)
        {
            switch (attribute)
            {
                case SvgAttributeId.X:
                    use.X = Length(value);
                    break;
                case SvgAttributeId.Y:
                    use.Y = Length(value);
                    break;
                case SvgAttributeId.Width:
                    use.Width = NonNegativeLength(value);
                    break;
                case SvgAttributeId.Height:
                    use.Height = NonNegativeLength(value);
                    break;
            }
        }

        /// <summary>
        /// Parses a gradient attribute.
        /// </summary>
        /// <param name="gradient">The record.</param>
        /// <param name="kind">The element kind.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetGradient(ref SvgGradientAttributes gradient, SvgElementKind kind, SvgAttributeId attribute, string value)
        {
            bool linear = kind == SvgElementKind.LinearGradient;
            switch (attribute)
            {
                case SvgAttributeId.X1 when linear:
                    gradient.X1 = Length(value);
                    break;
                case SvgAttributeId.Y1 when linear:
                    gradient.Y1 = Length(value);
                    break;
                case SvgAttributeId.X2 when linear:
                    gradient.X2 = Length(value);
                    break;
                case SvgAttributeId.Y2 when linear:
                    gradient.Y2 = Length(value);
                    break;
                case SvgAttributeId.Cx when !linear:
                    gradient.Cx = Length(value);
                    break;
                case SvgAttributeId.Cy when !linear:
                    gradient.Cy = Length(value);
                    break;
                case SvgAttributeId.R when !linear:
                    gradient.R = NonNegativeLength(value);
                    break;
                case SvgAttributeId.Fx when !linear:
                    gradient.Fx = Length(value);
                    break;
                case SvgAttributeId.Fy when !linear:
                    gradient.Fy = Length(value);
                    break;
                case SvgAttributeId.Fr when !linear:
                    gradient.Fr = NonNegativeLength(value);
                    break;
                case SvgAttributeId.GradientUnits:
                    gradient.Units = Units(value);
                    break;
                case SvgAttributeId.GradientTransform:
                    gradient.HasTransform = SvgTransformParser.TryParse(value, out Matrix3x2 transform);
                    gradient.Transform = transform;
                    break;
                case SvgAttributeId.SpreadMethod:
                    gradient.SpreadMethod = SvgNumberParser.Trim(value) switch
                    {
                        "pad" => SvgSpreadMethod.Pad,
                        "reflect" => SvgSpreadMethod.Reflect,
                        "repeat" => SvgSpreadMethod.Repeat,
                        _ => SvgSpreadMethod.Unspecified
                    };

                    break;
            }
        }

        /// <summary>
        /// Parses an <c>svg</c> or <c>symbol</c> attribute.
        /// </summary>
        /// <param name="viewport">The record.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">The value.</param>
        private static void SetViewport(ref SvgViewportAttributes viewport, SvgAttributeId attribute, string value)
        {
            switch (attribute)
            {
                case SvgAttributeId.X:
                    viewport.X = Length(value);
                    break;
                case SvgAttributeId.Y:
                    viewport.Y = Length(value);
                    break;
                case SvgAttributeId.Width:
                    viewport.Width = NonNegativeLength(value);
                    break;
                case SvgAttributeId.Height:
                    viewport.Height = NonNegativeLength(value);
                    break;
                case SvgAttributeId.ViewBox:
                    viewport.HasViewBox = SvgNumberParser.TryParseViewBox(value, out RectangleF viewBox);
                    viewport.ViewBox = viewBox;
                    break;
                case SvgAttributeId.PreserveAspectRatio:
                    _ = SvgPreserveAspectRatio.TryParse(value, out SvgPreserveAspectRatio preserveAspectRatio);
                    viewport.PreserveAspectRatio = preserveAspectRatio;
                    break;
            }
        }
    }
}
