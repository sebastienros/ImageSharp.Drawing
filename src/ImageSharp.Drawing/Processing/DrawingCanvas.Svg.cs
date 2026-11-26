// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <content>
/// SVG drawing on the canvas.
/// </content>
public abstract partial class DrawingCanvas
{
    /// <summary>
    /// Draws an SVG document at its intrinsic size, with the top left corner of its viewport at <paramref name="origin"/>.
    /// </summary>
    /// <remarks>
    /// The document renders through the regular fill, stroke, clip, and layer commands of this canvas, under the active
    /// transform and graphics options. Content outside the viewport of the document is clipped.
    /// </remarks>
    /// <param name="document">The document to draw.</param>
    /// <param name="origin">The top left corner of the viewport, in local canvas coordinates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">
    /// Drawing the document needs more layer pixels at the same time than <see cref="SvgLimits.MaxLayerPixels"/> of its
    /// load options allows. The canvas is not changed.
    /// </exception>
    public void DrawSvg(SvgDocument document, PointF origin)
    {
        Guard.NotNull(document, nameof(document));

        this.DrawSvg(document, new RectangleF(origin, document.Size));
    }

    /// <summary>
    /// Draws an SVG document into a destination rectangle. The <c>viewBox</c> of the document, or the rectangle of its
    /// intrinsic size when it has none, fits the destination following the <c>preserveAspectRatio</c> of the root element.
    /// </summary>
    /// <remarks>
    /// The document renders through the regular fill, stroke, clip, and layer commands of this canvas, under the active
    /// transform and graphics options. Content outside the destination is clipped. A destination with no area draws
    /// nothing.
    /// </remarks>
    /// <param name="document">The document to draw.</param>
    /// <param name="destination">The destination rectangle, in local canvas coordinates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">
    /// Drawing the document at this size needs more layer pixels at the same time than
    /// <see cref="SvgLimits.MaxLayerPixels"/> of its load options allows. The canvas is not changed.
    /// </exception>
    public void DrawSvg(SvgDocument document, RectangleF destination)
    {
        Guard.NotNull(document, nameof(document));

        SvgEmitter.Draw(this, document, destination);
    }
}
