// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests the style sheet features of phase 2 through <see cref="SvgReader"/>: raw attribute values for attribute
/// selectors wherever the <c>style</c> element is, the <c>media</c> attribute and <c>@media</c> rules, the user style
/// sheet origin of CSS Cascade Level 4, and CSS escapes in selectors and values.
/// </summary>
public class SvgStyleSheetTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"";

    // A style element can follow the elements it selects, and the root always precedes it.
    [Fact]
    public void AttributeSelectors_MatchElementsBeforeTheStyleElement()
    {
        SvgElementTree tree = Read(
            SvgOpen + " width=\"10\" data-root=\"yes\">"
            + "<rect id=\"a\" fill=\"red\" data-k=\"1\"/>"
            + "<style>[fill=\"red\"] { stroke: blue } svg[width=\"10\"] { opacity: 0.5 } [data-k] { stroke-width: 3 }</style>"
            + "<rect id=\"b\" fill=\"red\"/>"
            + "<rect id=\"c\" fill=\"blue\" data-k=\"2\"/>"
            + "</svg>");

        Assert.True(StyleOf(tree, "a").IsSpecified(SvgPropertyId.Stroke));
        Assert.True(StyleOf(tree, "b").IsSpecified(SvgPropertyId.Stroke));
        Assert.False(StyleOf(tree, "c").IsSpecified(SvgPropertyId.Stroke));
        Assert.Equal(0.5F, tree.GetStyle(0).Opacity);
        Assert.True(StyleOf(tree, "a").IsSpecified(SvgPropertyId.StrokeWidth));
        Assert.True(StyleOf(tree, "c").IsSpecified(SvgPropertyId.StrokeWidth));
    }

    // A second style element starts recording a new name; the elements before it come from the second read.
    [Fact]
    public void AttributeSelectors_FromLaterStyleElementsMatchEarlierElements()
    {
        SvgElementTree tree = Read(
            SvgOpen + ">"
            + "<rect id=\"a\" x=\"1\" y=\"2\"/>"
            + "<style>[x=\"1\"] { fill: blue }</style>"
            + "<rect id=\"b\" x=\"1\" y=\"2\"/>"
            + "<style>[y=\"2\"] { stroke: blue }</style>"
            + "<rect id=\"c\" x=\"1\" y=\"2\"/>"
            + "</svg>");

        foreach (string id in new[] { "a", "b", "c" })
        {
            Assert.True(StyleOf(tree, id).IsSpecified(SvgPropertyId.Fill), id);
            Assert.True(StyleOf(tree, id).IsSpecified(SvgPropertyId.Stroke), id);
        }
    }

    // Without a namespace prefix only attributes in no namespace match; "*|" matches xlink:href too.
    [Fact]
    public void AttributeSelectors_HonorTheNamespacePrefix()
    {
        SvgElementTree tree = Read(
            SvgOpen + ">"
            + "<use id=\"x\" xlink:href=\"#t\"/><use id=\"h\" href=\"#t\"/>"
            + "<style>[href=\"#t\"] { fill: blue } [*|href=\"#t\"] { stroke: blue }</style>"
            + "</svg>");

        Assert.False(StyleOf(tree, "x").IsSpecified(SvgPropertyId.Fill));
        Assert.True(StyleOf(tree, "x").IsSpecified(SvgPropertyId.Stroke));
        Assert.True(StyleOf(tree, "h").IsSpecified(SvgPropertyId.Fill));
        Assert.True(StyleOf(tree, "h").IsSpecified(SvgPropertyId.Stroke));
    }

    // The second read works for every kind of input: a string, a seekable stream at an offset, and a stream that cannot
    // seek.
    [Fact]
    public void AttributeSelectors_WorkForStreams()
    {
        string svg = SvgOpen + "><rect id=\"a\" fill=\"red\"/><style>[fill=\"red\"] { stroke: blue }</style></svg>";
        byte[] bytes = Encoding.UTF8.GetBytes(svg);

        using MemoryStream offset = new();
        offset.Write([1, 2, 3]);
        offset.Write(bytes);
        offset.Position = 3;
        SvgElementTree seekable = SvgReader.Read(offset, SvgReaderOptions.Default);
        Assert.True(StyleOf(seekable, "a").IsSpecified(SvgPropertyId.Stroke));

        using NonSeekableStream forward = new(bytes);
        SvgElementTree buffered = SvgReader.Read(forward, SvgReaderOptions.Default);
        Assert.True(StyleOf(buffered, "a").IsSpecified(SvgPropertyId.Stroke));
    }

    // A legacy encoding decodes the same way in the second read.
    [Fact]
    public void AttributeSelectors_WorkForLegacyEncodings()
    {
        Encoding cyrillic = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;
        string svg = "<?xml version=\"1.0\" encoding=\"windows-1251\"?>" + SvgOpen
            + "><rect id=\"a\" data-name=\"Тигр\"/><style>[data-name=\"Тигр\"] { fill: blue }</style></svg>";

        using MemoryStream stream = new(cyrillic.GetBytes(svg));
        SvgElementTree tree = SvgReader.Read(stream, SvgReaderOptions.Default);

        Assert.True(StyleOf(tree, "a").IsSpecified(SvgPropertyId.Fill));
    }

    [Theory]
    [InlineData("print", false)]
    [InlineData("screen", true)]
    [InlineData("", true)]
    [InlineData("all", true)]
    [InlineData("not print", true)]
    [InlineData("only screen", true)]
    [InlineData("only", false)]
    [InlineData("tv", false)]
    [InlineData("print, screen", true)]
    [InlineData("screen and (min-width: 50px)", true)]
    [InlineData("screen and (max-width: 50px)", false)]
    [InlineData("(min-width: 100px) and (max-width: 100px)", true)]
    [InlineData("(width: 100px)", true)]
    [InlineData("(min-height: 51px)", false)]
    [InlineData("(orientation: landscape)", true)]
    [InlineData("(orientation: portrait)", false)]
    [InlineData("(min-aspect-ratio: 2/1)", true)]
    [InlineData("(min-aspect-ratio: 3/1)", false)]
    [InlineData("(min-width: 6em)", true)]
    [InlineData("(min-width: 7em)", false)]
    [InlineData("(50px <= width <= 150px)", true)]
    [InlineData("(width > 100px)", false)]
    [InlineData("(width >= 100px)", true)]
    [InlineData("(100px = width)", true)]
    [InlineData("(frob: 1)", false)]
    [InlineData("not (frob: 1)", false)]
    [InlineData("(frob: 1), screen", true)]
    [InlineData("(frob: 1) or (width: 100px)", true)]
    [InlineData("not (width: 50px)", true)]
    [InlineData("(width: 100px) and (height: 50px) or (width: 1px)", false)]
    [InlineData("((width: 100px) or (width: 1px)) and (height: 50px)", true)]
    [InlineData("screen and (width: 100px) or (width: 1px)", false)]
    [InlineData("(width)", true)]
    [InlineData("(min-width: 10)", false)]
    public void Media_EvaluatesAgainstTheIntrinsicSize(string query, bool applies)
    {
        // The intrinsic size is 100 x 50 CSS pixels.
        string escaped = query.Replace("<", "&lt;", StringComparison.Ordinal);
        string body = $"<style media=\"{escaped}\">rect {{ fill: blue }}</style><style>@media {escaped} {{ rect {{ stroke: blue }} }}</style><rect id=\"r\"/>";
        SvgElementTree tree = Read(SvgOpen + " width=\"100\" height=\"50\">" + body + "</svg>");

        Assert.Equal(applies, StyleOf(tree, "r").IsSpecified(SvgPropertyId.Fill));
        if (query.Length > 0)
        {
            Assert.Equal(applies, StyleOf(tree, "r").IsSpecified(SvgPropertyId.Stroke));
        }
    }

    [Theory]
    [InlineData(" viewBox=\"0 0 400 200\"", "(width: 400px) and (height: 200px)")]
    [InlineData(" width=\"200\" viewBox=\"0 0 400 200\"", "(width: 200px) and (height: 100px)")]
    [InlineData("", "(width: 300px) and (height: 150px)")]
    [InlineData(" width=\"1in\" height=\"50%\"", "(width: 96px) and (height: 150px)")]
    public void Media_ViewportFollowsTheIntrinsicSizeRules(string attributes, string query)
    {
        SvgElementTree tree = Read(SvgOpen + attributes + $"><style>@media {query} {{ rect {{ fill: blue }} }}</style><rect id=\"r\"/></svg>");

        Assert.True(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Fill));
    }

    [Fact]
    public void Media_NestedRulesNeedEveryCondition()
    {
        SvgElementTree tree = Read(
            SvgOpen + " width=\"100\" height=\"50\"><style>"
            + "@media screen { @media (min-width: 10px) { #a { fill: blue } } @media print { #b { fill: blue } } #c { fill: blue } }"
            + "@media screen; #d { fill: blue }"
            + "</style><rect id=\"a\"/><rect id=\"b\"/><rect id=\"c\"/><rect id=\"d\"/></svg>");

        Assert.True(StyleOf(tree, "a").IsSpecified(SvgPropertyId.Fill));
        Assert.False(StyleOf(tree, "b").IsSpecified(SvgPropertyId.Fill));
        Assert.True(StyleOf(tree, "c").IsSpecified(SvgPropertyId.Fill));
        Assert.True(StyleOf(tree, "d").IsSpecified(SvgPropertyId.Fill));
    }

    // Normal user declarations rank below presentation attributes, important ones above important style attributes.
    [Fact]
    public void UserStyleSheet_HasTheUserOrigin()
    {
        SvgElementTree tree = Read(
            SvgOpen + "><style>#c { fill: red } #e { fill: red !important }</style>"
            + "<rect id=\"a\"/>"
            + "<rect id=\"b\" fill=\"red\"/>"
            + "<rect id=\"c\"/>"
            + "<rect id=\"d\" style=\"stroke: red !important\"/>"
            + "<rect id=\"e\"/>"
            + "</svg>",
            "rect { fill: blue } #c { fill: blue; stroke-width: 7 } #d { stroke: blue !important } rect { opacity: 0.5 !important } #e { fill: blue !important }");

        Assert.Equal(Color.Blue, StyleOf(tree, "a").Fill.Color);
        Assert.Equal(Color.Red, StyleOf(tree, "b").Fill.Color);
        Assert.Equal(Color.Red, StyleOf(tree, "c").Fill.Color);
        Assert.Equal(7F, StyleOf(tree, "c").StrokeWidth.Value);
        Assert.Equal(Color.Blue, StyleOf(tree, "d").Stroke.Color);
        Assert.Equal(Color.Blue, StyleOf(tree, "e").Fill.Color);
        Assert.Equal(0.5F, StyleOf(tree, "b").Opacity);
    }

    [Fact]
    public void UserStyleSheet_OrdersItsRulesBySpecificity()
    {
        SvgElementTree tree = Read(SvgOpen + "><rect id=\"r\" class=\"c\"/></svg>", "#r { fill: blue } .c { fill: red } rect { fill: red }");

        Assert.Equal(Color.Blue, StyleOf(tree, "r").Fill.Color);
    }

    // The user style sheet is known before the first element, so its attribute selectors need no second read, and its
    // media queries see the document size.
    [Fact]
    public void UserStyleSheet_SupportsAttributeSelectorsAndMedia()
    {
        SvgElementTree tree = Read(
            SvgOpen + " width=\"100\" height=\"50\"><rect id=\"r\" fill=\"red\"/></svg>",
            "[fill=\"red\"] { stroke: blue } @media (max-width: 99px) { rect { opacity: 0.5 } } svg[width=\"100\"] { opacity: 0.25 }");

        Assert.True(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Stroke));
        Assert.False(StyleOf(tree, "r").IsSpecified(SvgPropertyId.Opacity));
        Assert.Equal(0.25F, tree.GetStyle(0).Opacity);
    }

    [Theory]
    [WithBlankImage(40, 20, PixelTypes.Rgba32)]
    public void UserStyleSheet_ReachesTheReaderThroughTheLoadOptions<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen + " width=\"40\" height=\"20\"><rect width=\"20\" height=\"20\"/><rect x=\"20\" width=\"20\" height=\"20\" fill=\"#00ff00\"/></svg>",
            new SvgLoadOptions { UserStyleSheet = "rect { fill: blue }" });

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));

        Assert.Equal(Color.Blue.ToPixel<TPixel>(), image[10, 10]);
        Assert.Equal(Color.Lime.ToPixel<TPixel>(), image[30, 10]);
    }

    [Theory]
    [InlineData(".\\31 23 { fill: blue }")]
    [InlineData("#\\72 { fill: blue }")]
    [InlineData("[data-\\6b=\"\\76\"] { fill: blue }")]
    [InlineData("rect:\\6e th-child(2) { fill: blue }")]
    [InlineData("rect { fill: r\\65 d; fill: \\62 lue }")]
    [InlineData("rect { \\66 ill: blue }")]
    [InlineData("rect { fill: blue; fill: \\#ff0000 }")]
    [InlineData("rect { fill: red; fill: blue !imp\\6frtant; fill: red }")]
    public void Escapes_AreDecodedInSelectorsAndValues(string css)
    {
        SvgElementTree tree = Read(SvgOpen + $"><style>{css}</style><rect id=\"r\" class=\"123\" data-k=\"v\"/></svg>");

        Assert.Equal(Color.Blue, StyleOf(tree, "r").Fill.Color);
    }

    [Fact]
    public void Escapes_InUrlsAndLengthsAreDecoded()
    {
        SvgElementTree tree = Read(SvgOpen + "><style>rect { fill: url(#\\67) red; stroke-width: 2p\\78 }</style><rect id=\"r\"/></svg>");

        ref readonly SvgSpecifiedStyle style = ref StyleOf(tree, "r");
        Assert.Equal("g", style.Fill.Reference);
        Assert.Equal(new SvgLength(2, SvgLengthUnit.Px), style.StrokeWidth);
    }

    private static ref readonly SvgSpecifiedStyle StyleOf(SvgElementTree tree, string id)
    {
        Assert.True(tree.TryGetElementById(id, out int index));
        return ref tree.GetStyle(index);
    }

    private static SvgElementTree Read(string svg, string? userStyleSheet = null)
    {
        SvgReaderOptions defaults = SvgReaderOptions.Default;
        return SvgReader.Read(svg, new SvgReaderOptions(defaults.MaxElements, defaults.MaxNestingDepth, defaults.MaxCharactersFromEntities, userStyleSheet));
    }

    /// <summary>
    /// A read-only stream that cannot seek, as a network stream.
    /// </summary>
    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream inner;

        /// <summary>
        /// Initializes a new instance of the <see cref="NonSeekableStream"/> class.
        /// </summary>
        /// <param name="bytes">The content.</param>
        public NonSeekableStream(byte[] bytes) => this.inner = new MemoryStream(bytes);

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => this.inner.Read(buffer, offset, Math.Min(count, 7));

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            this.inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
