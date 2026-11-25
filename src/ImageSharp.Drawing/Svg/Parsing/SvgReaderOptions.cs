// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The input limits of <see cref="SvgReader"/>. The public load options fill this from their limits.
/// </summary>
internal readonly struct SvgReaderOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgReaderOptions"/> struct.
    /// </summary>
    /// <param name="maxElements">The maximum number of elements in the document.</param>
    /// <param name="maxNestingDepth">The maximum element nesting depth; the root element has depth 1.</param>
    /// <param name="maxCharactersFromEntities">The maximum number of characters that entity expansion may produce.</param>
    public SvgReaderOptions(int maxElements, int maxNestingDepth, long maxCharactersFromEntities)
    {
        this.MaxElements = maxElements;
        this.MaxNestingDepth = maxNestingDepth;
        this.MaxCharactersFromEntities = maxCharactersFromEntities;
    }

    /// <summary>
    /// Gets the options with the default limits of the implementation plan: one million elements, a depth of 512, and
    /// one million characters from entities.
    /// </summary>
    public static SvgReaderOptions Default => new(1_000_000, 512, 1 << 20);

    /// <summary>
    /// Gets the maximum number of elements in the document, including unknown and foreign elements.
    /// </summary>
    public int MaxElements { get; }

    /// <summary>
    /// Gets the maximum element nesting depth. The root element has depth 1.
    /// </summary>
    public int MaxNestingDepth { get; }

    /// <summary>
    /// Gets the maximum number of characters that entity expansion may produce, as
    /// <see cref="System.Xml.XmlReaderSettings.MaxCharactersFromEntities"/> applies it.
    /// </summary>
    public long MaxCharactersFromEntities { get; }
}
