// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The geometry attributes of <c>path</c>.
/// </summary>
internal struct SvgPathAttributes
{
    /// <summary>
    /// Gets or sets the path built from <c>d</c> in user space, or <see langword="null"/> when <c>d</c> is not specified.
    /// When the data has an error the path holds the commands before the first error, as SVG requires.
    /// </summary>
    public IPath? Path { readonly get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>d</c> parsed without error.
    /// </summary>
    public bool IsComplete { readonly get; set; }
}
