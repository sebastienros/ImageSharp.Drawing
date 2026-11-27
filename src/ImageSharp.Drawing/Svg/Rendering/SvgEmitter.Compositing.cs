// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// Mask layers, blend modes, strokes that do not scale, and draws without antialiasing.
/// </content>
internal static partial class SvgEmitter
{
    /// <summary>
    /// The bit of an options key that selects the even-odd fill rule.
    /// </summary>
    private const int EvenOddKeyBit = 2;

    /// <summary>
    /// Maps an SVG blend mode to the color blending mode of a layer.
    /// </summary>
    /// <param name="mode">The blend mode. It is not <see cref="SvgBlendMode.Normal"/>.</param>
    /// <returns>The color blending mode.</returns>
    internal static PixelColorBlendingMode ToColorBlendingMode(SvgBlendMode mode)
        => mode switch
        {
            SvgBlendMode.Multiply => PixelColorBlendingMode.Multiply,
            SvgBlendMode.Screen => PixelColorBlendingMode.Screen,
            SvgBlendMode.Overlay => PixelColorBlendingMode.Overlay,
            SvgBlendMode.Darken => PixelColorBlendingMode.Darken,
            SvgBlendMode.Lighten => PixelColorBlendingMode.Lighten,
            SvgBlendMode.ColorDodge => PixelColorBlendingMode.ColorDodge,
            SvgBlendMode.ColorBurn => PixelColorBlendingMode.ColorBurn,
            SvgBlendMode.HardLight => PixelColorBlendingMode.HardLight,
            SvgBlendMode.SoftLight => PixelColorBlendingMode.SoftLight,
            SvgBlendMode.Difference => PixelColorBlendingMode.Difference,
            SvgBlendMode.Exclusion => PixelColorBlendingMode.Exclusion,
            SvgBlendMode.Hue => PixelColorBlendingMode.Hue,
            SvgBlendMode.Saturation => PixelColorBlendingMode.Saturation,
            SvgBlendMode.Color => PixelColorBlendingMode.Color,
            SvgBlendMode.Luminosity => PixelColorBlendingMode.Luminosity,
            _ => PixelColorBlendingMode.Normal
        };

    /// <content>
    /// The per-render state for mask layers, strokes that do not scale, and aliased draws.
    /// </content>
    private ref partial struct EmitState
    {
        /// <summary>
        /// Opens a mask layer inside the layer that it masks. The canvas must be at the scope base.
        /// </summary>
        /// <remarks>
        /// The mask layer composites with <see cref="PixelAlphaCompositionMode.DestIn"/>: the layer below keeps its
        /// pixels scaled by the alpha of the mask layer, and becomes transparent where the mask layer is transparent. For a
        /// luminance mask, the layer effect first replaces the alpha with the luminance times the alpha. The canvas runs
        /// the effect when the layer closes, before the composite.
        /// </remarks>
        /// <param name="bounds">The bounds of the masked layer in canvas coordinates before the caller transform.</param>
        /// <param name="mode">How the content of the mask becomes coverage.</param>
        public void BeginMask(Rectangle bounds, SvgMaskMode mode)
        {
            if (this.maskGraphics is null)
            {
                this.maskGraphics = this.innerGraphics.DeepClone();
                this.maskGraphics.AlphaCompositionMode = PixelAlphaCompositionMode.DestIn;
            }

            this.layerContentOptions ??= new DrawingOptions(this.innerGraphics, IntersectionRule.NonZero, this.CallerTransform, this.textContrast);
            SvgMaskLayerEffect? effect = SvgMaskLayerEffect.Get(mode);
            _ = effect is null
                ? this.canvas.SaveLayer(this.maskGraphics, bounds, this.layerContentOptions)
                : this.canvas.SaveLayer(this.maskGraphics, bounds, effect, this.layerContentOptions);

            this.LayerDepth++;
            this.ScopeBase = this.canvas.SaveCount;
            this.CurrentKey = -1;
        }

        /// <summary>
        /// Returns the options key of an aliased draw from the key of the same draw with antialiasing.
        /// </summary>
        /// <remarks>
        /// Aliased keys follow the regular keys, offset by their count. The count is a multiple of 4, so the two low bits
        /// of a key, the layer bit and the even-odd bit, keep their meaning.
        /// </remarks>
        /// <param name="key">The key with antialiasing.</param>
        /// <returns>The aliased key.</returns>
        public readonly int GetAliasedKey(int key) => key + this.keyCount;

        /// <summary>
        /// Returns the drawing options of an aliased key, creating them on first use in this render.
        /// </summary>
        /// <param name="key">The key without the aliased offset.</param>
        /// <returns>The options.</returns>
        private DrawingOptions GetAliasedOptions(int key)
        {
            if (this.aliasedOptions is null)
            {
                this.aliasedOptions = new DrawingOptions?[this.keyCount];
                this.outerAliasedGraphics = this.outerGraphics.DeepClone();
                this.outerAliasedGraphics.Antialias = false;
                this.innerAliasedGraphics = this.innerGraphics.DeepClone();
                this.innerAliasedGraphics.Antialias = false;
            }

            return this.aliasedOptions[key] ??= this.CreateOptions(key, this.outerAliasedGraphics!, this.innerAliasedGraphics!);
        }

        /// <summary>
        /// Records the stroke of a draw node with <c>vector-effect: non-scaling-stroke</c>.
        /// </summary>
        /// <remarks>
        /// The stroke width and the dash pattern are measured in the destination space of the render: canvas coordinates
        /// before the caller transform, which is the host coordinate space of SVG. The emitter maps the path to that
        /// space, builds the outline there with the unscaled pen, maps the outline back to user space, and fills it under
        /// the node transform, so the brush stays in user space. This costs an outline per such stroke per render.
        /// </remarks>
        /// <param name="node">The draw node.</param>
        /// <param name="stroke">The pen of the node.</param>
        /// <param name="key">The options key of the node.</param>
        private void StrokeNonScaling(in SvgRenderNode node, Pen stroke, int key)
        {
            Matrix3x2 toHost = this.transforms[node.TransformIndex] * this.viewport;
            if (!Matrix3x2.Invert(toHost, out Matrix3x2 toUser))
            {
                // A singular transform collapses the path, which then has nothing to stroke in user space.
                return;
            }

            IPath host = node.Path!.Transform(new Matrix4x4(toHost));
            IPath outline = stroke.GeneratePath(host, stroke.StrokeWidth).Transform(new Matrix4x4(toUser));
            this.SetState(key & ~EvenOddKeyBit);
            this.canvas.Fill(stroke.StrokeFill, outline);
        }
    }
}
