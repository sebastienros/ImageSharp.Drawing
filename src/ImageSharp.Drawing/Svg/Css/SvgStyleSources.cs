// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The style declarations that an element carries itself, as indexes into the style table, kept until the cascade runs.
/// </summary>
internal readonly struct SvgStyleSources
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgStyleSources"/> struct.
    /// </summary>
    /// <param name="presentation">The index of the presentation attribute style, or -1.</param>
    /// <param name="inline">The index of the normal <c>style</c> attribute declarations, or -1.</param>
    /// <param name="inlineImportant">The index of the <c>!important</c> <c>style</c> attribute declarations, or -1.</param>
    public SvgStyleSources(int presentation, int inline, int inlineImportant)
    {
        this.Presentation = presentation;
        this.Inline = inline;
        this.InlineImportant = inlineImportant;
    }

    /// <summary>
    /// Gets the sources of an element that carries no style declarations.
    /// </summary>
    public static SvgStyleSources None => new(-1, -1, -1);

    /// <summary>
    /// Gets the index of the presentation attribute style, or -1.
    /// </summary>
    public int Presentation { get; }

    /// <summary>
    /// Gets the index of the normal <c>style</c> attribute declarations, or -1.
    /// </summary>
    public int Inline { get; }

    /// <summary>
    /// Gets the index of the <c>!important</c> <c>style</c> attribute declarations, or -1.
    /// </summary>
    public int InlineImportant { get; }
}
