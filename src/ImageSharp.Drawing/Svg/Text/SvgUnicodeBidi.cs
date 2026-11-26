// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>unicode-bidi</c> property.
/// </summary>
internal enum SvgUnicodeBidi : byte
{
    /// <summary>
    /// <c>normal</c>, the initial value.
    /// </summary>
    Normal,

    /// <summary>
    /// <c>embed</c>.
    /// </summary>
    Embed,

    /// <summary>
    /// <c>isolate</c>.
    /// </summary>
    Isolate,

    /// <summary>
    /// <c>bidi-override</c>.
    /// </summary>
    BidiOverride,

    /// <summary>
    /// <c>isolate-override</c>.
    /// </summary>
    IsolateOverride,

    /// <summary>
    /// <c>plaintext</c>.
    /// </summary>
    Plaintext
}
