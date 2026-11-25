// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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
/// the XML depth. Element and attribute names are matched by reference against a pre-seeded name table. Attribute values
/// are parsed from spans into typed fields as they are read; <c>style</c> element text is parsed into a style sheet. After
/// the last element, the cascade combines presentation attributes, style sheet rules, and <c>style</c> attributes into one
/// specified style per element.
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
    private readonly SvgStyleSheet styleSheet = new();
    private readonly Tables tables;
    private readonly List<SvgStyleSources> styleSources = [];
    private readonly List<int> openElements = [];
    private int styleElement = -1;
    private string? styleText;
    private StringBuilder? styleBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgReader"/> class.
    /// </summary>
    /// <param name="options">The input limits.</param>
    private SvgReader(in SvgReaderOptions options)
    {
        this.options = options;
        this.tables = new Tables(this.properties.DashLengths);
    }

    /// <summary>
    /// Reads an SVG document from a stream. The XML reader detects the encoding. The stream is not closed.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="options">The input limits.</param>
    /// <returns>The element tree.</returns>
    /// <exception cref="InvalidImageContentException">The input is not a valid SVG document, or a limit is exceeded.</exception>
    public static SvgElementTree Read(Stream stream, in SvgReaderOptions options)
    {
        NameTable nameTable = SvgNames.CreateNameTable();
        using XmlReader xml = XmlReader.Create(stream, CreateSettings(in options, nameTable));
        return new SvgReader(in options).ReadDocument(xml);
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
        NameTable nameTable = SvgNames.CreateNameTable();
        using StringReader text = new(svg);
        using XmlReader xml = XmlReader.Create(text, CreateSettings(in options, nameTable));
        return new SvgReader(in options).ReadDocument(xml);
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
    /// <param name="nameTable">The pre-seeded name table.</param>
    /// <returns>The settings.</returns>
    private static XmlReaderSettings CreateSettings(in SvgReaderOptions options, NameTable nameTable)
        => new()
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null,
            MaxCharactersFromEntities = options.MaxCharactersFromEntities,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = false,
            NameTable = nameTable,
        };

    /// <summary>
    /// Reads every node, then runs the cascade.
    /// </summary>
    /// <param name="xml">The XML reader.</param>
    /// <returns>The element tree.</returns>
    private SvgElementTree ReadDocument(XmlReader xml)
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
                        // Only the text directly inside a style element is read.
                        if (this.styleElement >= 0 && this.openElements[^1] == this.styleElement)
                        {
                            this.AppendStyleText(xml.Value);
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

        this.styleSheet.Complete();
        SvgCascade.Apply(
            CollectionsMarshal.AsSpan(this.tables.Elements),
            CollectionsMarshal.AsSpan(this.tables.ClassNames),
            CollectionsMarshal.AsSpan(this.styleSources),
            this.tables.Styles,
            this.styleSheet);

        return new SvgElementTree(this.tables, this.styleSheet.Count);
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

        if (xml.IsEmptyElement)
        {
            this.EndElement(index);
        }
        else
        {
            this.openElements.Add(index);
        }
    }

    /// <summary>
    /// Ends the innermost open element.
    /// </summary>
    private void CloseElement()
    {
        int index = this.openElements[^1];
        this.openElements.RemoveAt(this.openElements.Count - 1);
        this.EndElement(index);
    }

    /// <summary>
    /// Records the end of an element's subtree and parses the text of a completed style element.
    /// </summary>
    /// <param name="index">The element index.</param>
    private void EndElement(int index)
    {
        List<SvgElement> elements = this.tables.Elements;
        CollectionsMarshal.AsSpan(elements)[index].SubtreeEnd = elements.Count;
        if (index == this.styleElement)
        {
            if (this.styleBuilder?.Length > 0)
            {
                this.styleText = this.styleBuilder.ToString();
                _ = this.styleBuilder.Clear();
            }

            if (this.styleText is not null)
            {
                SvgCssParser.ParseStyleSheet(this.styleText, this.styleSheet, this.properties, this.names);
            }

            this.styleElement = -1;
            this.styleText = null;
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
        bool isCss = true;
        ref SvgElement element = ref CollectionsMarshal.AsSpan(this.tables.Elements)[index];
        SvgElementKind kind = element.Kind;

        if (xml.MoveToFirstAttribute())
        {
            do
            {
                string name = xml.LocalName;
                string ns = xml.NamespaceURI;
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
                                // A style element applies only for an absent or empty type, or text/css.
                                isCss = value.Length == 0 || value.Equals("text/css", StringComparison.OrdinalIgnoreCase);
                                break;
                            default:
                                this.tables.SetGeometry(kind, element.DataIndex, attribute, value);
                                break;
                        }
                    }
                    else if (SvgNames.PresentationAttributes.TryGetValue(name, out SvgPropertyId property))
                    {
                        _ = this.properties.TryParse(property, xml.Value, ref presentation);
                    }
                }
                else if (ReferenceEquals(ns, SvgNames.XLinkNamespace) && ReferenceEquals(name, SvgNames.Href))
                {
                    xlinkHref = xml.Value;
                }
            }
            while (xml.MoveToNextAttribute());

            _ = xml.MoveToElement();
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

        this.styleSources.Add(new SvgStyleSources(
            this.AddStyle(in presentation),
            this.AddStyle(in inline),
            this.AddStyle(in inlineImportant)));
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
        /// Gets the styles that elements index into.
        /// </summary>
        public List<SvgSpecifiedStyle> Styles { get; } = [];

        /// <summary>
        /// Gets the dash lengths that <see cref="SvgDashArray"/> values index into.
        /// </summary>
        public List<SvgLength> DashLengths { get; }

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
        /// Gets the map from id to the first element with that id.
        /// </summary>
        public Dictionary<string, int> Ids { get; } = new(StringComparer.Ordinal);

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
            // Only a same-document fragment names an element. External references never resolve in phase 1.
            ReadOnlySpan<char> reference = SvgNumberParser.Trim(value);
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
            }
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
                    gradient.Units = SvgNumberParser.Trim(value) switch
                    {
                        "userSpaceOnUse" => SvgGradientUnits.UserSpaceOnUse,
                        "objectBoundingBox" => SvgGradientUnits.ObjectBoundingBox,
                        _ => SvgGradientUnits.Unspecified
                    };

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
