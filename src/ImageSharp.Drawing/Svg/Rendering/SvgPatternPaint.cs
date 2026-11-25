// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The paint of a shape filled or stroked with a <c>pattern</c>: a tile, its placement in the user space of the shape,
/// and an opacity.
/// </summary>
/// <remarks>
/// At each draw the paint picks the tile resolution from the device transform: the tile rectangle maps to device space
/// through <c>tile to user × user to device</c>, and the lengths of the mapped tile axes give the pixel size, rounded up
/// to whole pixels. The brush then maps the pixels of that render back to the tile rectangle, so the rounding only
/// changes the sampling density and the tiles meet exactly. Rotation and skew stay in the brush transform, so they are
/// exact; only the tile resolution follows the scale. A tile that would exceed its pixel budget
/// (<see cref="SvgPatternTile.MaxPixels"/>) or a side of 16384 pixels renders at a lower resolution instead.
/// </remarks>
internal sealed class SvgPatternPaint : SvgDeferredPaint
{
    /// <summary>
    /// The largest side of one tile render, in pixels.
    /// </summary>
    private const float MaxSide = 16384F;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgPatternPaint"/> class.
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="tileToUser">
    /// The transform from tile space to the user space of the painted shape: the translation to the tile origin, then the
    /// <c>patternTransform</c>.
    /// </param>
    /// <param name="opacity">The opacity, in the range 0 to 1.</param>
    public SvgPatternPaint(SvgPatternTile tile, Matrix3x2 tileToUser, float opacity)
        : base(opacity)
    {
        this.Tile = tile;
        this.TileToUser = tileToUser;
    }

    /// <summary>
    /// Gets the tile.
    /// </summary>
    public SvgPatternTile Tile { get; }

    /// <summary>
    /// Gets the transform from tile space to the user space of the painted shape.
    /// </summary>
    public Matrix3x2 TileToUser { get; }

    /// <inheritdoc/>
    public override SvgDeferredPaint MultiplyOpacity(float opacity) => new SvgPatternPaint(this.Tile, this.TileToUser, this.Opacity * opacity);

    /// <summary>
    /// Returns the paint placed in another user space: <paramref name="matrix"/> maps the current user space of the paint
    /// into the new one. The resolve stage uses it to move a <c>context-fill</c> or <c>context-stroke</c> pattern from
    /// the context element to the shape that paints with it.
    /// </summary>
    /// <param name="matrix">The transform from the current user space to the new one. Only its affine part applies.</param>
    /// <param name="sourceInterest">The source interest, which a placement does not use.</param>
    /// <param name="preparedInterest">The prepared interest, which a placement does not use.</param>
    /// <returns>The paint.</returns>
    public override Brush Transform(Matrix4x4 matrix, Rectangle sourceInterest, Rectangle preparedInterest)
        => new SvgPatternPaint(
            this.Tile,
            this.TileToUser * new Matrix3x2(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M41, matrix.M42),
            this.Opacity);

    /// <inheritdoc/>
    protected override Brush? Resolve(Matrix3x2 transform)
    {
        Matrix3x2 tileToDevice = this.TileToUser * transform;
        SizeF size = this.Tile.Size;

        // The device lengths of the tile sides. A degenerate transform collapses the tile, which paints nothing.
        float width = size.Width * MathF.Sqrt((tileToDevice.M11 * tileToDevice.M11) + (tileToDevice.M12 * tileToDevice.M12));
        float height = size.Height * MathF.Sqrt((tileToDevice.M21 * tileToDevice.M21) + (tileToDevice.M22 * tileToDevice.M22));
        if (!(width > 0F) || !(height > 0F) || !float.IsFinite(width) || !float.IsFinite(height))
        {
            return null;
        }

        // Lower the resolution uniformly when the tile would exceed the pixel limit.
        double pixels = Math.Ceiling(width) * Math.Ceiling(height);
        int maxPixels = this.Tile.MaxPixels;
        if (pixels > maxPixels)
        {
            float factor = (float)Math.Sqrt(maxPixels / pixels);
            width *= factor;
            height *= factor;
        }

        // A long, thin tile can stay within the pixel limit and still need a canvas whose row buffers exceed what the
        // allocator gives, so each side is also limited; that axis alone loses resolution. Clamping in float keeps huge
        // values out of the int conversion.
        int pixelWidth = (int)Math.Clamp(MathF.Ceiling(width), 1F, MaxSide);
        int pixelHeight = (int)Math.Clamp(MathF.Ceiling(height), 1F, MaxSide);
        if ((long)pixelWidth * pixelHeight > maxPixels)
        {
            // Rounding up can still exceed the limit by a row or a column. Shortening the longer side to the largest
            // length that fits brings the product within the limit, because the shorter side is at most the square root
            // of the limit plus one.
            if (pixelWidth >= pixelHeight)
            {
                pixelWidth = Math.Max(1, maxPixels / pixelHeight);
            }
            else
            {
                pixelHeight = Math.Max(1, maxPixels / pixelWidth);
            }
        }

        SvgTexture texture = this.Tile.GetTexture(pixelWidth, pixelHeight);

        // Texel space to tile space, then to the user space of the shape. The canvas adds the device transform.
        Matrix3x2 textureToUser = Matrix3x2.CreateScale(size.Width / pixelWidth, size.Height / pixelHeight) * this.TileToUser;
        return new SvgTextureBrush(texture, textureToUser, WrapMode.Repeat, false, this.Opacity);
    }
}
