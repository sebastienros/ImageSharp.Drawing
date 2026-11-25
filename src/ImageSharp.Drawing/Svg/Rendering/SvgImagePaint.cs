// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The paint of a raster <c>image</c> element: the decoded pixels and their placement in the user space of the element.
/// The draw node fills the visible image rectangle with it.
/// </summary>
/// <remarks>
/// Each draw resolves the paint to an <see cref="SvgTextureBrush"/>, which the canvas places with the command transform
/// through <see cref="Brush.Transform(Matrix4x4, Rectangle, Rectangle)"/>. The paint is a deferred paint so that a group
/// opacity multiplies into it as into a pattern paint. The brush picks the box-filtered level of the texture that matches
/// the device scale, so large downscales do not alias, and samples with clamped edges, because the filled rectangle
/// supplies the antialiased edge of the image.
/// </remarks>
internal sealed class SvgImagePaint : SvgDeferredPaint
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgImagePaint"/> class.
    /// </summary>
    /// <param name="texture">The decoded image.</param>
    /// <param name="textureToUser">The transform from texel space to the user space of the element.</param>
    /// <param name="nearest">Whether to sample the nearest texel, for <c>image-rendering</c> values that ask for hard edges.</param>
    /// <param name="opacity">The opacity, in the range 0 to 1.</param>
    public SvgImagePaint(SvgTexture texture, Matrix3x2 textureToUser, bool nearest, float opacity)
        : base(opacity)
    {
        this.Texture = texture;
        this.TextureToUser = textureToUser;
        this.Nearest = nearest;
    }

    /// <summary>
    /// Gets the decoded image.
    /// </summary>
    public SvgTexture Texture { get; }

    /// <summary>
    /// Gets the transform from texel space to the user space of the element.
    /// </summary>
    public Matrix3x2 TextureToUser { get; }

    /// <summary>
    /// Gets a value indicating whether the paint samples the nearest texel.
    /// </summary>
    public bool Nearest { get; }

    /// <inheritdoc/>
    public override SvgDeferredPaint MultiplyOpacity(float opacity)
        => new SvgImagePaint(this.Texture, this.TextureToUser, this.Nearest, this.Opacity * opacity);

    /// <inheritdoc/>
    protected override Brush? Resolve(Matrix3x2 transform)
        => new SvgTextureBrush(this.Texture, this.TextureToUser, WrapMode.Clamp, this.Nearest, this.Opacity);
}
