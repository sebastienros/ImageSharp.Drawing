// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The input limits that bound the time and memory of loading an SVG document. A document that exceeds a limit does not
/// load.
/// </summary>
public sealed class SvgLimits
{
    /// <summary>
    /// The default of <see cref="MaxLayerPixels"/>.
    /// </summary>
    internal const long DefaultMaxLayerPixels = 16L * 4096 * 4096;

    private int maxElements = 1_000_000;
    private int maxNodes = 1_000_000;
    private int maxNestingDepth = 512;
    private long maxCharactersFromEntities = 1 << 20;
    private long maxImagePixels = 64L * 1024 * 1024;
    private int maxPatternTilePixels = 4096 * 4096;
    private int maxTextCharacters = 1_000_000;
    private int maxFilterPixels = 4096 * 4096;
    private long maxLayerPixels = DefaultMaxLayerPixels;

    /// <summary>
    /// Gets or sets the largest number of elements the document can contain. The default is 1,000,000.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public int MaxElements
    {
        get => this.maxElements;
        set
        {
            Guard.MustBeGreaterThan(value, 0, nameof(this.MaxElements));
            this.maxElements = value;
        }
    }

    /// <summary>
    /// Gets or sets the largest number of element instances that rendering the document visits after every <c>use</c>
    /// reference is expanded. Nested <c>use</c> references can multiply the content of a small document. The default is
    /// 1,000,000.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public int MaxNodes
    {
        get => this.maxNodes;
        set
        {
            Guard.MustBeGreaterThan(value, 0, nameof(this.MaxNodes));
            this.maxNodes = value;
        }
    }

    /// <summary>
    /// Gets or sets the largest nesting depth of elements, counted in the document and through <c>use</c> references. The
    /// default is 512.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public int MaxNestingDepth
    {
        get => this.maxNestingDepth;
        set
        {
            Guard.MustBeGreaterThan(value, 0, nameof(this.MaxNestingDepth));
            this.maxNestingDepth = value;
        }
    }

    /// <summary>
    /// Gets or sets the largest number of characters that the expansion of XML entities can produce. The default is
    /// 1,048,576.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public long MaxCharactersFromEntities
    {
        get => this.maxCharactersFromEntities;
        set
        {
            Guard.MustBeGreaterThan(value, 0, nameof(this.MaxCharactersFromEntities));
            this.maxCharactersFromEntities = value;
        }
    }

    /// <summary>
    /// Gets or sets the largest total number of decoded pixels of all <c>image</c> elements of one document. An image
    /// that would exceed the limit does not draw; the rest of the document still loads. The default is 67,108,864
    /// (64 × 1024 × 1024).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public long MaxImagePixels
    {
        get => this.maxImagePixels;
        set
        {
            Guard.MustBeGreaterThan(value, 0, nameof(this.MaxImagePixels));
            this.maxImagePixels = value;
        }
    }

    /// <summary>
    /// Gets or sets the largest number of pixels of one rendered pattern tile. A tile that would be larger renders at a
    /// lower resolution instead. The default is 16,777,216 (4096 × 4096).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public int MaxPatternTilePixels
    {
        get => this.maxPatternTilePixels;
        set
        {
            Guard.MustBeGreaterThan(value, 0, nameof(this.MaxPatternTilePixels));
            this.maxPatternTilePixels = value;
        }
    }

    /// <summary>
    /// Gets or sets the largest number of characters of text that one document lays out, counted after every <c>use</c>
    /// reference is expanded, so text that several <c>use</c> elements show counts once per instance. The same limit
    /// bounds the character data inside <c>text</c> elements that the document contains. The default is 1,000,000.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public int MaxTextCharacters
    {
        get => this.maxTextCharacters;
        set
        {
            Guard.MustBeGreaterThan(value, 0, nameof(this.MaxTextCharacters));
            this.maxTextCharacters = value;
        }
    }

    /// <summary>
    /// Gets or sets the largest number of pixels of one intermediate image of a filter. A filter whose images would be
    /// larger runs at a lower resolution and its result is drawn scaled up. The default is 16,777,216 (4096 × 4096). One
    /// image takes 4 bytes per pixel, and a filter keeps a few images alive at a time: the element itself, the results
    /// that later filter primitives still read, and the result being computed.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public int MaxFilterPixels
    {
        get => this.maxFilterPixels;
        set
        {
            Guard.MustBeGreaterThan(value, 0, nameof(this.MaxFilterPixels));
            this.maxFilterPixels = value;
        }
    }

    /// <summary>
    /// Gets or sets the largest total number of pixels of the layers that one draw of the document keeps open at the same
    /// time. Masks, blend modes, group opacity, and clips that need coverage each draw their content into a layer of the
    /// size of that content on the canvas, and nested layers are all open at once, so their memory adds up. A draw that
    /// would exceed the limit throws <see cref="InvalidImageContentException"/> before it changes the canvas. The default
    /// is 268,435,456 (16 times 4096 × 4096). The canvas keeps every pixel of a layer in the pixel format of the canvas.
    /// </summary>
    /// <remarks>
    /// The total depends on the size of the draw: the same document needs more layer pixels at a larger size. The layers
    /// of a nested SVG image count with the layers of the document that shows it. The content of a filter or of a pattern
    /// tile draws on a canvas of its own, whose layers are not open at the same time as the layers of the outer draw; such
    /// a draw is checked against the limit on its own, when it runs, and its canvas is no larger than
    /// <see cref="MaxFilterPixels"/> or <see cref="MaxPatternTilePixels"/> allow. The working images of a filter count
    /// against <see cref="MaxFilterPixels"/>, not against this limit.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public long MaxLayerPixels
    {
        get => this.maxLayerPixels;
        set
        {
            Guard.MustBeGreaterThan(value, 0, nameof(this.MaxLayerPixels));
            this.maxLayerPixels = value;
        }
    }
}
