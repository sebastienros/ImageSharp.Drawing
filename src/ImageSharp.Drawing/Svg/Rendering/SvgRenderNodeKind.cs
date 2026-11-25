// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of an <see cref="SvgRenderNode"/>.
/// </summary>
internal enum SvgRenderNodeKind : byte
{
    /// <summary>
    /// Fills and then strokes one path.
    /// </summary>
    Draw,

    /// <summary>
    /// Opens an isolated group that composites with an opacity when the matching <see cref="EndLayer"/> closes it.
    /// </summary>
    BeginLayer,

    /// <summary>
    /// Closes the group of the matching <see cref="BeginLayer"/>.
    /// </summary>
    EndLayer,

    /// <summary>
    /// Narrows the clip to a viewport rectangle until the matching <see cref="EndClip"/>.
    /// </summary>
    BeginClip,

    /// <summary>
    /// Restores the clip of the matching <see cref="BeginClip"/>.
    /// </summary>
    EndClip,

    /// <summary>
    /// Does nothing. The resolve stage removes these nodes before it builds the document.
    /// </summary>
    None
}
