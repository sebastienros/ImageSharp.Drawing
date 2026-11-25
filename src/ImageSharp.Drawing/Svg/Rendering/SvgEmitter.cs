// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The emit stage: records the render nodes of an <see cref="SvgDocument"/> as ordinary canvas commands.
/// </summary>
/// <remarks>
/// <para>
/// The emitter keeps all per-render state on the stack of <see cref="Draw"/> and in arrays it allocates for the call, so
/// one document can render on several canvases at the same time. For the common case it allocates no SVG objects:
/// paths, brushes, and pens come from the document.
/// </para>
/// <para>
/// Canvas state follows a fixed pattern. Each open scope (the root clip, a layer, a viewport clip) records the save count
/// at which it starts, its base. A draw node needs the canvas transform <c>node transform × viewport transform × caller
/// transform</c> and its fill rule. The emitter creates one <see cref="DrawingOptions"/> per distinct (transform, fill
/// rule, inside a layer) key that the call uses, lazily, and switches the canvas to it with one restore to the scope base
/// and one save, only when the key changes between consecutive draw nodes. Opening or closing a scope restores to the
/// scope base, so a scope never leaks options into its parent.
/// </para>
/// <para>
/// Content inside a layer draws with normal source-over compositing at full blend percentage, because the layer is an
/// isolated group: the caller's blend mode, composition mode, and blend percentage apply once, when the outermost layer
/// composites onto the canvas. Content outside every layer draws with the caller's graphics options.
/// </para>
/// <para>
/// The canvas strokes a path in device space with the stroke width scaled by the square root of the transform
/// determinant. That equals the SVG stroke, which is computed in user space and then transformed, only when the
/// transform is conformal: a rotation, a uniform scale, a reflection, and a translation. For any other transform (a
/// non-uniform scale or a skew) the emitter builds the stroke outline itself, in a copy of user space scaled uniformly by
/// the largest scale of the transform so that curves flatten at device precision, scales the outline back to user space,
/// and fills it under the node transform. This costs an outline per such stroke per render.
/// </para>
/// </remarks>
internal static class SvgEmitter
{
    /// <summary>
    /// The scope stack depth that the emitter keeps on the stack. Deeper documents allocate an array.
    /// </summary>
    private const int StackScopeDepth = 64;

    /// <summary>
    /// The relative tolerance of the conformal transform test. A transform within it changes a stroke width by less than
    /// about 0.1%.
    /// </summary>
    private const float ConformalTolerance = 1E-3F;

    /// <summary>
    /// Draws a document into a destination rectangle.
    /// </summary>
    /// <param name="canvas">The canvas to record into.</param>
    /// <param name="document">The document.</param>
    /// <param name="destination">The destination rectangle, in local canvas coordinates.</param>
    public static void Draw(DrawingCanvas canvas, SvgDocument document, RectangleF destination)
    {
        ReadOnlySpan<SvgRenderNode> nodes = document.Nodes;
        if (nodes.IsEmpty || !(destination.Width > 0F) || !(destination.Height > 0F))
        {
            return;
        }

        // The viewport transform only scales and translates, which keeps the bounds tests below exact and cheap.
        Matrix3x2 viewport = document.CreateViewportTransform(destination);
        EmitState state = new(canvas, document.Transforms, viewport);

        int scopeDepth = document.MaxScopeDepth;
        Span<int> scopes = scopeDepth <= StackScopeDepth ? stackalloc int[StackScopeDepth] : new int[scopeDepth];

        int restoreCount = canvas.SaveCount;
        _ = canvas.Save();

        // The root viewport clips the document. The clip is a no-op, and is left out, when the destination already covers
        // the canvas without a caller transform.
        if (!state.CallerTransform.IsIdentity || !destination.Contains(canvas.Bounds))
        {
            canvas.Clip(new RectanglePolygon(destination));
        }

        state.ScopeBase = canvas.SaveCount;
        int depth = 0;

        for (int i = 0; i < nodes.Length; i++)
        {
            ref readonly SvgRenderNode node = ref nodes[i];
            switch (node.Kind)
            {
                case SvgRenderNodeKind.Draw:
                    // The root clip removes everything outside the destination, so a node outside it is skipped.
                    if (Intersects(MapBounds(node.Bounds, viewport), destination))
                    {
                        state.DrawNode(in node);
                    }

                    break;

                case SvgRenderNodeKind.BeginLayer:
                {
                    Rectangle bounds = ToLayerBounds(MapBounds(node.Bounds, viewport), destination);
                    if (bounds.Width <= 0 || bounds.Height <= 0)
                    {
                        // Nothing of the layer is inside the destination.
                        i = node.End;
                        continue;
                    }

                    canvas.RestoreTo(state.ScopeBase);
                    scopes[depth++] = (state.ScopeBase << 1) | 1;
                    state.BeginLayer(bounds, node.Opacity);
                    break;
                }

                case SvgRenderNodeKind.BeginClip:
                {
                    if (!Intersects(MapBounds(node.Bounds, viewport), destination))
                    {
                        i = node.End;
                        continue;
                    }

                    canvas.RestoreTo(state.ScopeBase);
                    scopes[depth++] = state.ScopeBase << 1;

                    // The clip rectangle is in the user space of its transform, which the canvas applies to the clip path.
                    _ = canvas.Save(state.GetOptions(state.GetKey(node.TransformIndex, IntersectionRule.NonZero)));
                    canvas.Clip(node.Clip!);
                    state.ScopeBase = canvas.SaveCount;
                    state.CurrentKey = -1;
                    break;
                }

                case SvgRenderNodeKind.EndLayer:
                case SvgRenderNodeKind.EndClip:
                {
                    // Restoring to the base of the parent scope closes the scope and every state saved inside it.
                    int scope = scopes[--depth];
                    state.ScopeBase = scope >> 1;
                    if ((scope & 1) != 0)
                    {
                        state.LayerDepth--;
                    }

                    canvas.RestoreTo(state.ScopeBase);
                    state.CurrentKey = -1;
                    break;
                }
            }
        }

        canvas.RestoreTo(restoreCount);
    }

    /// <summary>
    /// Maps a rectangle in document user space through the viewport transform, which only scales by positive factors
    /// and translates.
    /// </summary>
    /// <param name="bounds">The rectangle in document user space.</param>
    /// <param name="viewport">The viewport transform.</param>
    /// <returns>The rectangle in canvas coordinates before the caller transform.</returns>
    private static RectangleF MapBounds(RectangleF bounds, Matrix3x2 viewport)
        => new(
            (bounds.X * viewport.M11) + viewport.M31,
            (bounds.Y * viewport.M22) + viewport.M32,
            bounds.Width * viewport.M11,
            bounds.Height * viewport.M22);

    /// <summary>
    /// Returns whether two rectangles overlap or touch. A zero-width or zero-height rectangle can still overlap.
    /// </summary>
    /// <param name="a">The first rectangle.</param>
    /// <param name="b">The second rectangle.</param>
    /// <returns><see langword="true"/> if the rectangles overlap or touch; otherwise, <see langword="false"/>.</returns>
    private static bool Intersects(RectangleF a, RectangleF b)
        => a.Left <= b.Right && b.Left <= a.Right && a.Top <= b.Bottom && b.Top <= a.Bottom;

    /// <summary>
    /// Converts the bounds of a layer to the integer rectangle that the canvas layer takes: clipped to the destination,
    /// rounded outwards, and grown by one pixel for antialiased edges.
    /// </summary>
    /// <param name="bounds">The layer bounds in canvas coordinates before the caller transform.</param>
    /// <param name="destination">The destination rectangle.</param>
    /// <returns>The layer rectangle, empty when the layer is outside the destination.</returns>
    private static Rectangle ToLayerBounds(RectangleF bounds, RectangleF destination)
    {
        float left = MathF.Max(bounds.Left, destination.Left);
        float top = MathF.Max(bounds.Top, destination.Top);
        float right = MathF.Min(bounds.Right, destination.Right);
        float bottom = MathF.Min(bounds.Bottom, destination.Bottom);
        if (!(right >= left) || !(bottom >= top))
        {
            return Rectangle.Empty;
        }

        return Rectangle.FromLTRB(
            (int)MathF.Floor(left) - 1,
            (int)MathF.Floor(top) - 1,
            (int)MathF.Ceiling(right) + 1,
            (int)MathF.Ceiling(bottom) + 1);
    }

    /// <summary>
    /// Returns whether the linear part of a transform is conformal: it scales every direction by the same factor, so a
    /// stroke width transforms by one number.
    /// </summary>
    /// <remarks>
    /// With row vectors the linear part is <c>[M11 M12; M21 M22]</c>. A rotation with a uniform scale has
    /// <c>M11 = M22</c> and <c>M12 = -M21</c>; with a reflection, <c>M11 = -M22</c> and <c>M12 = M21</c>.
    /// </remarks>
    /// <param name="transform">The transform.</param>
    /// <returns><see langword="true"/> if the transform is conformal; otherwise, <see langword="false"/>.</returns>
    private static bool IsConformal(in Matrix4x4 transform)
    {
        float a = transform.M11;
        float b = transform.M12;
        float c = transform.M21;
        float d = transform.M22;
        float tolerance = ConformalTolerance * (MathF.Abs(a) + MathF.Abs(b) + MathF.Abs(c) + MathF.Abs(d));

        return (MathF.Abs(a - d) <= tolerance && MathF.Abs(b + c) <= tolerance)
            || (MathF.Abs(a + d) <= tolerance && MathF.Abs(b - c) <= tolerance);
    }

    /// <summary>
    /// Returns the largest factor by which the linear part of a transform scales a length: its largest singular value.
    /// </summary>
    /// <param name="transform">The transform.</param>
    /// <returns>The factor.</returns>
    private static float GetMaxScale(in Matrix4x4 transform)
    {
        // For a 2x2 matrix M the singular values are the square roots of the eigenvalues of M^T M, whose sum is the
        // squared Frobenius norm and whose difference is the square root below.
        float a = transform.M11;
        float b = transform.M12;
        float c = transform.M21;
        float d = transform.M22;
        float sum = (a * a) + (b * b) + (c * c) + (d * d);
        float p = (a * a) + (b * b) - (c * c) - (d * d);
        float q = (a * c) + (b * d);
        return MathF.Sqrt((sum + MathF.Sqrt((p * p) + (4F * q * q))) / 2F);
    }

    /// <summary>
    /// The per-render state of the emitter: the canvas, the transforms, the drawing options created so far, and the
    /// state of the scope that receives the next command.
    /// </summary>
    private ref struct EmitState
    {
        private readonly DrawingCanvas canvas;
        private readonly ReadOnlySpan<Matrix3x2> transforms;
        private readonly Matrix3x2 viewport;
        private readonly GraphicsOptions outerGraphics;
        private readonly GraphicsOptions innerGraphics;
        private readonly float textContrast;

        // One slot per key: ((transform * 2) + even-odd bit) * 2 + layer bit.
        private readonly DrawingOptions?[] options;
        private DrawingOptions? layerContentOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmitState"/> struct.
        /// </summary>
        /// <param name="canvas">The canvas.</param>
        /// <param name="transforms">The transforms of the document.</param>
        /// <param name="viewport">The viewport transform of this render.</param>
        public EmitState(DrawingCanvas canvas, ReadOnlySpan<Matrix3x2> transforms, Matrix3x2 viewport)
        {
            DrawingOptions callerOptions = canvas.GetActiveOptions();
            this.canvas = canvas;
            this.transforms = transforms;
            this.viewport = viewport;
            this.CallerTransform = callerOptions.Transform;
            this.textContrast = callerOptions.TextContrast;
            this.outerGraphics = callerOptions.GraphicsOptions;
            this.innerGraphics = this.outerGraphics.DeepClone();
            this.innerGraphics.BlendPercentage = 1F;
            this.innerGraphics.ColorBlendingMode = PixelColorBlendingMode.Normal;
            this.innerGraphics.AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver;
            this.options = new DrawingOptions?[transforms.Length * 4];
            this.layerContentOptions = null;
            this.ScopeBase = 0;
            this.LayerDepth = 0;
            this.CurrentKey = -1;
        }

        /// <summary>
        /// Gets the transform of the caller's active state.
        /// </summary>
        public readonly Matrix4x4 CallerTransform { get; }

        /// <summary>
        /// Gets or sets the save count at which the scope that receives the next command starts.
        /// </summary>
        public int ScopeBase { readonly get; set; }

        /// <summary>
        /// Gets or sets the number of open layers.
        /// </summary>
        public int LayerDepth { readonly get; set; }

        /// <summary>
        /// Gets or sets the key of the options that the canvas holds above the scope base, or -1 when it holds none.
        /// </summary>
        public int CurrentKey { readonly get; set; }

        /// <summary>
        /// Returns the key of a transform and fill rule in the current layer state.
        /// </summary>
        /// <param name="transformIndex">The transform index.</param>
        /// <param name="rule">The fill rule.</param>
        /// <returns>The key.</returns>
        public readonly int GetKey(int transformIndex, IntersectionRule rule)
            => (((transformIndex * 2) + (rule == IntersectionRule.EvenOdd ? 1 : 0)) * 2) + (this.LayerDepth > 0 ? 1 : 0);

        /// <summary>
        /// Records the fill and then the stroke of a draw node, as SVG paints them.
        /// </summary>
        /// <param name="node">The draw node.</param>
        public void DrawNode(in SvgRenderNode node)
        {
            int key = this.GetKey(node.TransformIndex, node.FillRule);
            Brush? fill = node.Fill;
            Pen? stroke = node.Stroke;
            if (fill is SvgDeferredPaint || stroke?.StrokeFill is SvgDeferredPaint)
            {
                // Pattern and image paints build their brush for the device transform of this render.
                Matrix4x4 transform = this.GetOptions(key).Transform;
                fill = SvgDeferredPaint.Resolve(fill, in transform);
                stroke = SvgDeferredPaint.Resolve(stroke, in transform);
            }

            if (fill is not null)
            {
                this.SetState(key);
                this.canvas.Fill(fill, node.Path!);
            }

            if (stroke is null)
            {
                return;
            }

            DrawingOptions drawOptions = this.GetOptions(key);
            if (IsConformal(drawOptions.Transform))
            {
                // A stroke does not use the fill rule, so the active options serve when only the fill rule differs.
                if (this.CurrentKey >= 0 && (this.CurrentKey ^ key) == 2)
                {
                    key = this.CurrentKey;
                }

                this.SetState(key);
                this.canvas.Draw(stroke, node.Path!);
                return;
            }

            // Build the outline in a copy of user space scaled by the largest scale of the transform, so curves and round
            // joins flatten at device precision, then scale the outline back to user space and fill it under the node
            // transform. The dash pattern and the width scale together, so dashes keep their user space lengths, and
            // the brush stays in user space.
            float scale = MathF.Max(GetMaxScale(drawOptions.Transform), float.Epsilon);
            IPath scaled = node.Path!.Transform(Matrix4x4.CreateScale(scale, scale, 1F));
            IPath outline = stroke.GeneratePath(scaled, stroke.StrokeWidth * scale)
                .Transform(Matrix4x4.CreateScale(1F / scale, 1F / scale, 1F));

            this.SetState(this.GetKey(node.TransformIndex, IntersectionRule.NonZero));
            this.canvas.Fill(stroke.StrokeFill, outline);
        }

        /// <summary>
        /// Opens a layer. The canvas must be at the scope base.
        /// </summary>
        /// <param name="bounds">The layer bounds in canvas coordinates before the caller transform.</param>
        /// <param name="opacity">The layer opacity.</param>
        public void BeginLayer(Rectangle bounds, float opacity)
        {
            // The outermost layer composites with the caller's graphics options; nested layers composite inside their
            // isolated parent. The layer opacity scales the blend percentage.
            GraphicsOptions layerGraphics = (this.LayerDepth == 0 ? this.outerGraphics : this.innerGraphics).DeepClone();
            layerGraphics.BlendPercentage *= opacity;

            // The layer bounds are in canvas coordinates before the caller transform, so the layer state carries the
            // caller transform, which maps them to the target.
            this.layerContentOptions ??= new DrawingOptions(this.innerGraphics, IntersectionRule.NonZero, this.CallerTransform, this.textContrast);
            _ = this.canvas.SaveLayer(layerGraphics, bounds, this.layerContentOptions);

            this.LayerDepth++;
            this.ScopeBase = this.canvas.SaveCount;
            this.CurrentKey = -1;
        }

        /// <summary>
        /// Switches the canvas to the options of a key, unless it already holds them.
        /// </summary>
        /// <param name="key">The key.</param>
        public void SetState(int key)
        {
            if (key == this.CurrentKey)
            {
                return;
            }

            this.canvas.RestoreTo(this.ScopeBase);
            _ = this.canvas.Save(this.GetOptions(key));
            this.CurrentKey = key;
        }

        /// <summary>
        /// Returns the drawing options of a key, creating them on first use in this render.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The options.</returns>
        public DrawingOptions GetOptions(int key)
        {
            DrawingOptions? result = this.options[key];
            if (result is not null)
            {
                return result;
            }

            GraphicsOptions graphics = (key & 1) == 0 ? this.outerGraphics : this.innerGraphics;
            IntersectionRule rule = ((key >> 1) & 1) == 0 ? IntersectionRule.NonZero : IntersectionRule.EvenOdd;

            // Compose in 2D first, then lift to the 4x4 canvas transform, so the caller can still use a projective
            // transform.
            Matrix4x4 transform = new Matrix4x4(this.transforms[key >> 2] * this.viewport) * this.CallerTransform;

            result = new DrawingOptions(graphics, rule, transform, this.textContrast);
            this.options[key] = result;
            return result;
        }
    }
}
