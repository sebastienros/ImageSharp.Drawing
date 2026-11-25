// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// How a <see cref="SvgRenderNodeKind.BeginMask"/> node turns the content of its layer into coverage.
/// </summary>
internal enum SvgMaskMode : byte
{
    /// <summary>
    /// The coverage is the alpha of the content: <c>mask-type: alpha</c>, and the coverage of a clip path that cannot be
    /// a geometry clip.
    /// </summary>
    Alpha,

    /// <summary>
    /// The coverage is the luminance of the sRGB values times the alpha: <c>mask-type: luminance</c>.
    /// </summary>
    Luminance,

    /// <summary>
    /// The coverage is the luminance of the linear-light values times the alpha: <c>mask-type: luminance</c> with
    /// <c>color-interpolation: linearRGB</c> on the <c>mask</c> element.
    /// </summary>
    LinearLuminance
}
