// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The layer memory limit.
/// </content>
internal static partial class SvgEmitter
{
    /// <summary>
    /// Throws when a draw would keep more layer pixels open at the same time than the document allows
    /// (<see cref="SvgLimits.MaxLayerPixels"/>). It runs before the draw records anything, so a refused draw leaves the
    /// canvas unchanged.
    /// </summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="document">The document. It has at least one scope node.</param>
    /// <param name="viewport">The viewport transform of the draw.</param>
    /// <param name="destination">The destination rectangle of the draw.</param>
    /// <exception cref="InvalidImageContentException">The draw needs more layer pixels than the limit.</exception>
    private static void CheckLayerPixels(DrawingCanvas canvas, SvgDocument document, Matrix3x2 viewport, RectangleF destination)
    {
        Matrix4x4 callerTransform = canvas.GetActiveOptions().Transform;
        long pixels = GetLargestLayerPixels(document.Nodes, document.MaxScopeDepth, viewport, destination, in callerTransform, canvas.Bounds);
        if (pixels > document.MaxLayerPixels)
        {
            throw new InvalidImageContentException(
                $"Drawing the SVG document at this size needs {pixels} pixels of layers at the same time, which exceeds the layer limit of {document.MaxLayerPixels}.");
        }
    }

    /// <summary>
    /// Returns the largest total number of pixels of the layers that a draw keeps open at the same time.
    /// </summary>
    /// <remarks>
    /// The walk follows the scopes as <see cref="Draw(DrawingCanvas, SvgDocument, Matrix3x2, RectangleF)"/> opens them,
    /// with the same culling, and sizes each layer as the canvas does: the layer rectangle of the emitter (a mask layer
    /// grows by the one pixel of reach that the canvas gives an effect layer), mapped through the caller transform,
    /// rounded outward, and clipped to the layer that encloses it, or to the canvas. Clip scopes open no layer. The work is
    /// one pass over the nodes without allocation for up to 64 nested scopes.
    /// </remarks>
    /// <param name="nodes">The render nodes.</param>
    /// <param name="maxScopeDepth">The deepest nesting of scopes in the nodes.</param>
    /// <param name="viewport">The viewport transform of the draw.</param>
    /// <param name="destination">The destination rectangle of the draw, in local canvas coordinates.</param>
    /// <param name="callerTransform">The transform of the canvas state that the draw starts in.</param>
    /// <param name="canvasBounds">The bounds of the canvas in local coordinates.</param>
    /// <returns>The largest total, in pixels.</returns>
    internal static long GetLargestLayerPixels(
        ReadOnlySpan<SvgRenderNode> nodes,
        int maxScopeDepth,
        Matrix3x2 viewport,
        RectangleF destination,
        in Matrix4x4 callerTransform,
        Rectangle canvasBounds)
    {
        if (maxScopeDepth == 0)
        {
            return 0;
        }

        // Per open scope: its pixels, and the rectangle that its content is clipped to.
        Span<long> areas = maxScopeDepth <= StackScopeDepth ? stackalloc long[StackScopeDepth] : new long[maxScopeDepth];
        Span<Rectangle> targets = maxScopeDepth <= StackScopeDepth ? stackalloc Rectangle[StackScopeDepth] : new Rectangle[maxScopeDepth];
        Rectangle target = canvasBounds;
        bool transformed = !callerTransform.IsIdentity;
        long open = 0;
        long largest = 0;
        int depth = 0;
        for (int i = 0; i < nodes.Length; i++)
        {
            ref readonly SvgRenderNode node = ref nodes[i];
            switch (node.Kind)
            {
                case SvgRenderNodeKind.BeginLayer:
                case SvgRenderNodeKind.BeginMask:
                {
                    Rectangle bounds = ToLayerBounds(MapBounds(node.Bounds, viewport), destination);
                    if (bounds.Width <= 0 || bounds.Height <= 0)
                    {
                        i = node.End;
                        continue;
                    }

                    if (node.Kind == SvgRenderNodeKind.BeginMask)
                    {
                        bounds.Inflate(1, 1);
                    }

                    RectangleF mapped = transformed ? RectangleF.Transform(bounds, callerTransform) : bounds;
                    Rectangle layer = Rectangle.Intersect(
                        target,
                        Rectangle.FromLTRB(
                            (int)MathF.Floor(mapped.Left),
                            (int)MathF.Floor(mapped.Top),
                            (int)MathF.Ceiling(mapped.Right),
                            (int)MathF.Ceiling(mapped.Bottom)));

                    long area = (long)layer.Width * layer.Height;
                    areas[depth] = area;
                    targets[depth] = target;
                    depth++;
                    target = layer;
                    open += area;
                    largest = Math.Max(largest, open);
                    break;
                }

                case SvgRenderNodeKind.BeginClip:
                    if (!Intersects(MapBounds(node.Bounds, viewport), destination))
                    {
                        i = node.End;
                        continue;
                    }

                    areas[depth] = 0;
                    targets[depth] = target;
                    depth++;
                    break;

                case SvgRenderNodeKind.EndLayer:
                case SvgRenderNodeKind.EndMask:
                case SvgRenderNodeKind.EndClip:
                    depth--;
                    open -= areas[depth];
                    target = targets[depth];
                    break;
            }
        }

        return largest;
    }
}
