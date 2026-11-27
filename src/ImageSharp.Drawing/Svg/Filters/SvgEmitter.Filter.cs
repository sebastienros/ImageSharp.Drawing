// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// Filter nodes.
/// </content>
internal static partial class SvgEmitter
{
    /// <content>
    /// The per-render state for filter nodes.
    /// </content>
    private ref partial struct EmitState
    {
        /// <summary>
        /// Draws a filter node: runs the filter for the device transform of this render and draws the result as an image.
        /// </summary>
        /// <remarks>
        /// The result is a texture in filter space. It draws as a rectangle filled with a texture brush under the
        /// transform from texels to device space, which is an integer translation when the element is only scaled and
        /// translated, so the texels land on device pixels unchanged. The node opacity multiplies the brush: the result is
        /// one image, so this equals a group opacity. Inside a layer the result composites source-over; outside every
        /// layer it uses the caller's graphics options, as a draw node does.
        /// <para>
        /// A projective caller transform cannot be split into a scale along the user axes, so the filter then runs in the
        /// space before the caller transform, scaled uniformly by the largest scale of the linear part of the caller
        /// transform, and the result draws through the caller transform like any other content. Where the perspective
        /// enlarges the result more than that scale, it is resampled.
        /// </para>
        /// </remarks>
        /// <param name="node">The filter node.</param>
        /// <param name="filter">The filter data of the node.</param>
        /// <param name="destination">The destination rectangle of the render, which clips the document.</param>
        public void DrawFilter(in SvgRenderNode node, SvgFilterNode filter, RectangleF destination)
        {
            Matrix4x4 caller = this.CallerTransform;
            bool projective = caller.M14 != 0F || caller.M24 != 0F || caller.M44 != 1F;
            Matrix3x2 callerAffine;
            RectangleF visible;
            if (projective)
            {
                float scale = GetMaxScale(caller);
                scale = scale > 0F && float.IsFinite(scale) ? scale : 1F;
                callerAffine = Matrix3x2.CreateScale(scale);

                // The canvas bounds do not map back through a projective transform as a rectangle in general, so only the
                // clip of the document bounds what can show.
                visible = TransformRectangle(destination, callerAffine);
            }
            else
            {
                callerAffine = new Matrix3x2(caller.M11, caller.M12, caller.M21, caller.M22, caller.M41, caller.M42);

                // Only the part of the result inside the canvas and inside the clip of the document can show.
                visible = RectangleF.Intersect(this.canvas.Bounds, TransformRectangle(destination, callerAffine));
            }

            Matrix3x2 userToDevice = this.transforms[node.TransformIndex] * this.viewport * callerAffine;
            if (!(visible.Width > 0F) || !(visible.Height > 0F)
                || !SvgFilterEngine.TryRender(
                    filter,
                    userToDevice,
                    visible,
                    filter.MaxPixels,
                    this.canvas.GetConfiguration(),
                    out SvgTexture? texture,
                    out Matrix3x2 textureToDevice))
            {
                return;
            }

            // The texture holds a pooled buffer that the brush samples when the canvas renders its commands, which can be
            // after this draw returns or, through a retained scene, after the canvas is gone. The canvas takes it and
            // disposes it at the right time. A canvas that cannot (a canvas implementation of another library) gets a
            // managed copy, which the garbage collector frees with the last command that refers to it.
            if (!this.canvas.TryTakeOwnership(texture!))
            {
                SvgTexture pooled = texture!;
                texture = pooled.ToManaged();
                pooled.Dispose();
            }

            // For a projective caller transform, textureToDevice ends in the scaled space before the caller transform.
            Matrix4x4 textureTransform = projective
                ? new Matrix4x4(textureToDevice * Matrix3x2.CreateScale(1F / callerAffine.M11)) * caller
                : new Matrix4x4(textureToDevice);

            GraphicsOptions graphics = this.LayerDepth > 0 ? this.innerGraphics : this.outerGraphics;
            this.canvas.RestoreTo(this.ScopeBase);
            _ = this.canvas.Save(new DrawingOptions(graphics, IntersectionRule.NonZero, textureTransform, this.textContrast));
            this.CurrentKey = -1;

            SvgTextureBrush brush = new(texture!, Matrix3x2.Identity, WrapMode.Clamp, false, node.Opacity);
            this.canvas.Fill(brush, new RectanglePolygon(0F, 0F, texture!.Width, texture.Height));
        }

        /// <summary>
        /// Returns the axis-aligned bounding box of a transformed rectangle.
        /// </summary>
        /// <param name="rectangle">The rectangle.</param>
        /// <param name="transform">The transform.</param>
        /// <returns>The bounding box.</returns>
        private static RectangleF TransformRectangle(RectangleF rectangle, Matrix3x2 transform)
        {
            if (transform.IsIdentity)
            {
                return rectangle;
            }

            Vector2 a = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Top), transform);
            Vector2 b = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Top), transform);
            Vector2 c = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Bottom), transform);
            Vector2 d = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Bottom), transform);
            Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            return RectangleF.FromLTRB(min.X, min.Y, max.X, max.Y);
        }
    }
}
