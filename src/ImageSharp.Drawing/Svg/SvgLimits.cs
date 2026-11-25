// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The input limits that bound the time and memory of loading an SVG document. A document that exceeds a limit does not
/// load.
/// </summary>
public sealed class SvgLimits
{
    private int maxElements = 1_000_000;
    private int maxNodes = 1_000_000;
    private int maxNestingDepth = 512;
    private long maxCharactersFromEntities = 1 << 20;
    private long maxImagePixels = 64L * 1024 * 1024;
    private int maxPatternTilePixels = 4096 * 4096;

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
}
