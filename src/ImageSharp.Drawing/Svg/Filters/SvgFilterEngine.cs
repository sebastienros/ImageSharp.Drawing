// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Runs the filter of one element for one draw: it renders the content of the element off-screen, executes the primitives
/// on buffers that it owns, and returns the result as a texture to draw. A filter list is a chain of filters, each run on
/// the result of the one before.
/// </summary>
/// <remarks>
/// <para>
/// Filter space. The transform from the user space of the element to device space splits into a scale along the user
/// axes and a remainder. Filter primitives work along the user axes, so the filter runs on a pixel grid of the user space
/// scaled by that scale (see <see cref="SvgFilterRenderContext"/>), and the result draws through the remainder. For a
/// transform that only scales by positive factors and translates, the remainder is the identity: filter pixels are
/// device pixels and the result draws without resampling. For a rotation, a skew, or a reflection, the scale is the
/// length of each mapped user axis and the result draws with bilinear sampling.
/// </para>
/// <para>
/// Working area. The filter region, rounded outward to whole pixels, bounds every result: Filter Effects makes it a hard clip
/// on the inputs and the outputs of every primitive. Starting from the visible part of the region (the canvas area
/// mapped into filter space), the engine walks the graph backward and gives each result the smallest rectangle that the
/// output can depend on, through <see cref="SvgFilterPrimitive.GetInputArea"/>, clipped to the subregion of the
/// primitive. A result that nothing visible depends on is never computed, so an element far outside the canvas costs no
/// pixels.
/// </para>
/// <para>
/// Limits. When a rectangle would hold more than the pixel limit (<see cref="SvgLimits.MaxFilterPixels"/>) or have a
/// side longer than <see cref="MaxSide"/>, the filter runs at a lower scale and the result is drawn scaled up, as
/// pattern tiles do. The memory of a run is bounded by 4 bytes per pixel times the limit times the number of results
/// alive at the same time: <c>SourceGraphic</c>, <c>SourceAlpha</c>, the results that later primitives still read, the
/// output being computed, and a converted copy of each of its inputs.
/// </para>
/// <para>
/// Buffers come from the memory allocator of the default configuration and return to it when their last consumer has
/// run. Only the final texture is a managed array, because the canvas defers its commands and a retained scene keeps
/// the texture.
/// </para>
/// </remarks>
internal sealed class SvgFilterEngine
{
    /// <summary>
    /// The longest side of one result in pixels.
    /// </summary>
    internal const int MaxSide = 16384;

    // The filter of the chain that the engine works on.
    private SvgFilterGraph graph;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterEngine"/> class.
    /// </summary>
    /// <param name="graph">The first filter of the chain.</param>
    /// <param name="allocator">The allocator of the buffers.</param>
    private SvgFilterEngine(SvgFilterGraph graph, MemoryAllocator allocator)
    {
        this.graph = graph;
        this.Allocator = allocator;
    }

    /// <summary>
    /// Gets the number of filter pixels per user unit along the x and the y axis.
    /// </summary>
    public Vector2 Scale { get; private set; }

    /// <summary>
    /// Gets the position of the user space origin in filter space.
    /// </summary>
    public Vector2 Translation { get; private set; }

    /// <summary>
    /// Gets the allocator of the buffers.
    /// </summary>
    public MemoryAllocator Allocator { get; }

    /// <summary>
    /// Runs a filter for one draw.
    /// </summary>
    /// <param name="node">The filter node data.</param>
    /// <param name="userToDevice">The transform from the user space of the filtered element to device space.</param>
    /// <param name="visible">The device rectangle that can show the result.</param>
    /// <param name="maxPixels">The largest number of pixels of one result.</param>
    /// <param name="texture">When this method returns <see langword="true"/>, contains the result, premultiplied sRGB.</param>
    /// <param name="textureToDevice">When this method returns <see langword="true"/>, contains the transform from the texels of the result to device space.</param>
    /// <returns><see langword="true"/> if the result has pixels to draw; otherwise, <see langword="false"/>.</returns>
    public static bool TryRender(
        SvgFilterNode node,
        Matrix3x2 userToDevice,
        RectangleF visible,
        int maxPixels,
        out SvgTexture? texture,
        out Matrix3x2 textureToDevice)
    {
        texture = null;
        textureToDevice = default;

        // Split the transform into the scale along the user axes and the remainder. With row vectors, the user x axis
        // maps to (M11, M12) and the user y axis to (M21, M22).
        Matrix3x2 m = userToDevice;
        Vector2 scale;
        Vector2 translation;
        Matrix3x2 filterToDevice;
        if (m.M12 == 0F && m.M21 == 0F && m.M11 > 0F && m.M22 > 0F)
        {
            scale = new Vector2(m.M11, m.M22);
            translation = new Vector2(m.M31, m.M32);
            filterToDevice = Matrix3x2.Identity;
        }
        else
        {
            scale = new Vector2(MathF.Sqrt((m.M11 * m.M11) + (m.M12 * m.M12)), MathF.Sqrt((m.M21 * m.M21) + (m.M22 * m.M22)));
            translation = Vector2.Zero;
            filterToDevice = Matrix3x2.CreateScale(1F / scale.X, 1F / scale.Y) * m;
        }

        // A transform that is not invertible collapses the element, which then draws nothing.
        if (!(scale.X > 0F) || !(scale.Y > 0F) || !float.IsFinite(scale.X) || !float.IsFinite(scale.Y)
            || !Matrix3x2.Invert(filterToDevice, out Matrix3x2 deviceToFilter))
        {
            return false;
        }

        SvgFilterGraph[] graphs = node.Graphs;
        int count = graphs.Length;
        SvgFilterEngine engine = new(graphs[0], Configuration.Default.MemoryAllocator);
        RectangleF visibleFilter = TransformBounds(visible, deviceToFilter);
        Rectangle[][] areas = new Rectangle[count][];
        Rectangle[][] subregions = new Rectangle[count][];
        Rectangle[] regions = new Rectangle[count];
        Rectangle[] sourceAreas = new Rectangle[count];
        for (int g = 0; g < count; g++)
        {
            areas[g] = new Rectangle[graphs[g].Primitives.Length];
            subregions[g] = new Rectangle[graphs[g].Primitives.Length];
        }

        // Lower the scale until every result fits the limits. The reach of the primitives scales with the scale, so the
        // areas shrink about in proportion; a few rounds settle it.
        int first;
        for (int round = 0; ; round++)
        {
            engine.Scale = scale;
            engine.Translation = translation;

            // Walk the chain backward: the source area that a filter reads is the output area that the filter before it
            // must produce. A filter whose output nothing needs, and the filters before it, do not run.
            RectangleF needed = visibleFilter;
            long largest = 0;
            int longestSide = 0;
            first = count;
            for (int g = count - 1; g >= 0; g--)
            {
                engine.graph = graphs[g];
                regions[g] = engine.GetFilterRegion();
                long size = engine.ComputeAreas(regions[g], needed, areas[g], subregions[g], out sourceAreas[g], out int side);
                if (size == 0)
                {
                    break;
                }

                first = g;
                largest = Math.Max(largest, size);
                longestSide = Math.Max(longestSide, side);
                if (sourceAreas[g].IsEmpty)
                {
                    break;
                }

                needed = sourceAreas[g];
            }

            if (first == count)
            {
                return false;
            }

            if ((largest <= maxPixels && longestSide <= MaxSide) || round == 4)
            {
                if (largest > maxPixels || longestSide > MaxSide)
                {
                    // The rounds did not settle, which needs a reach that grows faster than the scale shrinks. The
                    // result is not computed rather than exceeding the limit.
                    return false;
                }

                break;
            }

            float factor = MathF.Min(MathF.Sqrt(maxPixels / (float)largest), MaxSide / (float)longestSide) * 0.99F;
            scale *= factor;
            translation *= factor;
            visibleFilter = new RectangleF(visibleFilter.X * factor, visibleFilter.Y * factor, visibleFilter.Width * factor, visibleFilter.Height * factor);
            filterToDevice = Matrix3x2.CreateScale(1F / factor) * filterToDevice;
        }

        // Run the chain forward. The content of the element is the source of the first filter; the sRGB result of each
        // filter is the source of the next. When the walk stopped at a filter whose output nothing needs, the source of
        // the first filter that runs is transparent.
        SvgFilterImage? result = null;
        for (int g = first; g < count; g++)
        {
            engine.graph = graphs[g];
            SvgFilterImage? source = result;
            result = engine.Execute(g == 0 ? node.Content : null, source, regions[g], areas[g], subregions[g], sourceAreas[g]);
            source?.Dispose();
            if (result is not null)
            {
                SvgFilterColor.Convert(result, SvgColorInterpolation.SRgb);
            }
        }

        if (result is null)
        {
            return false;
        }

        using (result)
        {
            Rectangle area = result.Area;
            Rgba32[] pixels = new Rgba32[area.Width * area.Height];
            result.Pixels.CopyTo(pixels);
            texture = new SvgTexture(pixels, area.Width, area.Height);
            textureToDevice = Matrix3x2.CreateTranslation(area.X, area.Y) * filterToDevice;
        }

        return true;
    }

    /// <summary>
    /// Renders a document into a transparent image of filter space and premultiplies the result. See
    /// <see cref="SvgFilterRenderContext.RenderDocument"/>.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="documentToUser">The transform from the user space of the document to the user space of the filtered element; it only scales and translates.</param>
    /// <param name="target">The transparent image that receives the pixels.</param>
    public void RenderDocument(SvgDocument document, Matrix3x2 documentToUser, SvgFilterImage target)
    {
        if (target.IsEmpty)
        {
            return;
        }

        Rectangle area = target.Area;
        Matrix3x2 viewport = documentToUser
            * new Matrix3x2(this.Scale.X, 0F, 0F, this.Scale.Y, this.Translation.X - area.X, this.Translation.Y - area.Y);

        // The canvas draws straight alpha into the pooled pixels of the image through an image that wraps them; the
        // wrapper does not own the memory, so disposing it leaves the pixels to the target.
        using (Image<Rgba32> image = Image.WrapMemory<Rgba32>(Configuration.Default, target.Memory, area.Width, area.Height))
        {
            using DrawingCanvas canvas = image.Frames.RootFrame.CreateCanvas(Configuration.Default, new DrawingOptions());
            SvgEmitter.Draw(canvas, document, viewport, new RectangleF(0F, 0F, area.Width, area.Height));
        }

        Span<Rgba32> pixels = target.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            ref Rgba32 pixel = ref pixels[i];
            byte a = pixel.A;
            if (a == 255)
            {
                continue;
            }

            pixel.R = SvgFilterColor.Multiply(pixel.R, a);
            pixel.G = SvgFilterColor.Multiply(pixel.G, a);
            pixel.B = SvgFilterColor.Multiply(pixel.B, a);
        }
    }

    /// <summary>
    /// Returns the axis-aligned bounding box of a transformed rectangle.
    /// </summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <param name="transform">The transform.</param>
    /// <returns>The bounding box.</returns>
    private static RectangleF TransformBounds(RectangleF rectangle, Matrix3x2 transform)
    {
        Vector2 a = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Top), transform);
        Vector2 b = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Top), transform);
        Vector2 c = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Bottom), transform);
        Vector2 d = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Bottom), transform);
        Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        return RectangleF.FromLTRB(min.X, min.Y, max.X, max.Y);
    }

    /// <summary>
    /// Rounds a rectangle of filter space outward to whole pixels: a pixel belongs to the rectangle when any part of it
    /// does. Chromium rounds the filter region and the primitive subregions this way. An edge within 1/256 of a pixel
    /// boundary counts as on it, so that float rounding does not add a row or column: the default region of a 100 unit
    /// box ends at 40 + 1.2F * 100 = 160.0000048, which at scale 2.5 would reach into pixel 400.
    /// </summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns>The pixel rectangle, empty for an empty or non-finite rectangle.</returns>
    private static Rectangle ToPixelsOutward(RectangleF rectangle)
    {
        const float limit = 1 << 30;
        const float tolerance = 1F / 256F;
        float left = MathF.Floor(Math.Clamp(rectangle.Left + tolerance, -limit, limit));
        float top = MathF.Floor(Math.Clamp(rectangle.Top + tolerance, -limit, limit));
        float right = MathF.Ceiling(Math.Clamp(rectangle.Right - tolerance, -limit, limit));
        float bottom = MathF.Ceiling(Math.Clamp(rectangle.Bottom - tolerance, -limit, limit));
        if (!(right > left) || !(bottom > top))
        {
            return Rectangle.Empty;
        }

        return Rectangle.FromLTRB((int)left, (int)top, (int)right, (int)bottom);
    }

    /// <summary>
    /// Returns the intersection of two pixel rectangles, or an empty rectangle.
    /// </summary>
    /// <param name="a">The first rectangle.</param>
    /// <param name="b">The second rectangle.</param>
    /// <returns>The intersection.</returns>
    private static Rectangle Intersect(Rectangle a, Rectangle b)
    {
        Rectangle result = Rectangle.Intersect(a, b);
        return result.Width > 0 && result.Height > 0 ? result : Rectangle.Empty;
    }

    /// <summary>
    /// Returns the union of two pixel rectangles, either of which can be empty.
    /// </summary>
    /// <param name="a">The first rectangle.</param>
    /// <param name="b">The second rectangle.</param>
    /// <returns>The union.</returns>
    private static Rectangle Union(Rectangle a, Rectangle b)
        => a.IsEmpty ? b : b.IsEmpty ? a : Rectangle.Union(a, b);

    /// <summary>
    /// Returns the filter region in filter space at the current scale.
    /// </summary>
    /// <returns>The region in whole pixels.</returns>
    private Rectangle GetFilterRegion()
        => ToPixelsOutward(new SvgFilterRenderContext(this, default, default, default, default).ToFilter(this.graph.Region));

    /// <summary>
    /// Walks the graph backward from the visible output and computes the area of every result.
    /// </summary>
    /// <param name="region">The filter region in filter space.</param>
    /// <param name="visible">The visible rectangle in filter space.</param>
    /// <param name="areas">Receives the area of each result; empty for a result that is not needed.</param>
    /// <param name="subregions">Receives the subregion of each primitive in filter space, clipped to the filter region.</param>
    /// <param name="sourceArea">Receives the area of <c>SourceGraphic</c> that the primitives read.</param>
    /// <param name="longestSide">Receives the longest side of all areas.</param>
    /// <returns>The pixel count of the largest area, or 0 when the output is empty.</returns>
    private long ComputeAreas(Rectangle region, RectangleF visible, Rectangle[] areas, Rectangle[] subregions, out Rectangle sourceArea, out int longestSide)
    {
        SvgFilterPrimitive[] primitives = this.graph.Primitives;
        sourceArea = Rectangle.Empty;
        longestSide = 0;
        long largest = 0;
        Array.Clear(areas);
        areas[^1] = Intersect(ToPixelsOutward(visible), region);

        for (int i = primitives.Length - 1; i >= 0; i--)
        {
            SvgFilterPrimitive primitive = primitives[i];
            SvgFilterRenderContext context = new(this, region, default, primitive.ColorSpace, primitive.ColorSpace);
            subregions[i] = Intersect(ToPixelsOutward(context.ToFilter(primitive.Subregion)), region);
            Rectangle area = Intersect(areas[i], subregions[i]);
            areas[i] = area;
            if (area.IsEmpty)
            {
                continue;
            }

            largest = Math.Max(largest, (long)area.Width * area.Height);
            longestSide = Math.Max(longestSide, Math.Max(area.Width, area.Height));
            context = new SvgFilterRenderContext(this, region, subregions[i], primitive.ColorSpace, primitive.ColorSpace);
            ReadOnlySpan<int> inputs = primitive.Inputs;
            for (int k = 0; k < inputs.Length; k++)
            {
                // The filter region clips every input, the standard inputs included.
                Rectangle inputArea = Intersect(primitive.GetInputArea(k, area, in context), region);
                if (inputs[k] >= 0)
                {
                    areas[inputs[k]] = Union(areas[inputs[k]], inputArea);
                }
                else
                {
                    sourceArea = Union(sourceArea, inputArea);
                }
            }
        }

        if (!sourceArea.IsEmpty)
        {
            largest = Math.Max(largest, (long)sourceArea.Width * sourceArea.Height);
            longestSide = Math.Max(longestSide, Math.Max(sourceArea.Width, sourceArea.Height));
        }

        return areas[^1].IsEmpty ? 0 : largest;
    }

    /// <summary>
    /// Renders the source and executes the primitives in order.
    /// </summary>
    /// <param name="content">The content of the filtered element for the first filter of a chain, or <see langword="null"/>.</param>
    /// <param name="previous">The result of the previous filter of a chain, which is the source of this one, or <see langword="null"/>.</param>
    /// <param name="region">The filter region in filter space.</param>
    /// <param name="areas">The area of each result.</param>
    /// <param name="subregions">The subregion of each primitive in filter space.</param>
    /// <param name="sourceArea">The area of <c>SourceGraphic</c> that the primitives read.</param>
    /// <returns>The result of the last primitive, which the caller disposes, or <see langword="null"/> when it is empty.</returns>
    private SvgFilterImage? Execute(
        SvgDocument? content,
        SvgFilterImage? previous,
        Rectangle region,
        Rectangle[] areas,
        Rectangle[] subregions,
        Rectangle sourceArea)
    {
        SvgFilterPrimitive[] primitives = this.graph.Primitives;
        int[] lastUse = this.graph.LastUse;
        SvgFilterImage?[] results = new SvgFilterImage?[primitives.Length];
        SvgFilterImage? sourceGraphic = null;
        SvgFilterImage? sourceAlpha = null;
        SvgFilterImage?[] inputImages = new SvgFilterImage?[4];
        List<SvgFilterImage> temporaries = [];

        // The last primitive that reads each standard input, so its image can be released after it. SourceAlpha derives
        // from SourceGraphic, so SourceGraphic stays until the first reader of SourceAlpha has run.
        int lastGraphic = -1;
        int lastAlpha = -1;
        int firstAlpha = -1;
        for (int i = 0; i < primitives.Length; i++)
        {
            foreach (int input in primitives[i].Inputs)
            {
                lastGraphic = input == SvgFilterPrimitive.SourceGraphic ? i : lastGraphic;
                lastAlpha = input == SvgFilterPrimitive.SourceAlpha ? i : lastAlpha;
                firstAlpha = input == SvgFilterPrimitive.SourceAlpha && firstAlpha < 0 ? i : firstAlpha;
            }
        }

        lastGraphic = Math.Max(lastGraphic, firstAlpha);

        try
        {
            for (int i = 0; i < primitives.Length; i++)
            {
                SvgFilterPrimitive primitive = primitives[i];
                Rectangle area = areas[i];
                ReadOnlySpan<int> inputs = primitive.Inputs;
                if (!area.IsEmpty)
                {
                    if (inputImages.Length < inputs.Length)
                    {
                        inputImages = new SvgFilterImage?[inputs.Length];
                    }

                    for (int k = 0; k < inputs.Length; k++)
                    {
                        int input = inputs[k];
                        if (input == SvgFilterPrimitive.SourceGraphic || input == SvgFilterPrimitive.SourceAlpha)
                        {
                            sourceGraphic ??= this.RenderSource(content, previous, sourceArea);
                            if (input == SvgFilterPrimitive.SourceAlpha)
                            {
                                sourceAlpha ??= this.CreateSourceAlpha(sourceGraphic);
                            }

                            inputImages[k] = input == SvgFilterPrimitive.SourceGraphic ? sourceGraphic : sourceAlpha;
                        }
                        else
                        {
                            inputImages[k] = results[input] ??= new SvgFilterImage(this.Allocator, Rectangle.Empty, primitives[input].ColorSpace);
                        }
                    }

                    // A color filter converts on its own, in floating point, and the result of the last primitive is then
                    // written in sRGB directly, so it needs no final conversion.
                    SvgColorInterpolation space = primitive.IsColorFilter
                        ? (i == primitives.Length - 1 ? SvgColorInterpolation.SRgb : primitive.ColorSpace)
                        : this.PrepareInputs(primitive, inputImages.AsSpan(0, inputs.Length), temporaries);
                    SvgFilterImage output = new(this.Allocator, area, space);
                    results[i] = output;
                    SvgFilterRenderContext context = new(this, region, subregions[i], primitive.ColorSpace, space);
                    primitive.Render(in context, inputImages.AsSpan(0, inputs.Length)!, output);

                    foreach (SvgFilterImage temporary in temporaries)
                    {
                        temporary.Dispose();
                    }

                    temporaries.Clear();
                    Array.Clear(inputImages);
                }

                // Release the results whose last consumer has run.
                foreach (int input in inputs)
                {
                    if (input >= 0 && lastUse[input] == i)
                    {
                        results[input]?.Dispose();
                        results[input] = null;
                    }
                }

                if (i == lastGraphic && sourceGraphic is not null)
                {
                    sourceGraphic.Dispose();
                    sourceGraphic = null;
                }

                if (i == lastAlpha && sourceAlpha is not null)
                {
                    sourceAlpha.Dispose();
                    sourceAlpha = null;
                }
            }

            SvgFilterImage? result = results[^1];
            results[^1] = null;
            if (result is null || result.IsEmpty)
            {
                result?.Dispose();
                return null;
            }

            return result;
        }
        finally
        {
            foreach (SvgFilterImage? image in results)
            {
                image?.Dispose();
            }

            foreach (SvgFilterImage temporary in temporaries)
            {
                temporary.Dispose();
            }

            sourceGraphic?.Dispose();
            sourceAlpha?.Dispose();
        }
    }

    /// <summary>
    /// Brings the inputs of a primitive into the color space it computes in, and returns the color space of its output.
    /// An input that needs another color space is converted into a copy, because other primitives can still read it.
    /// </summary>
    /// <param name="primitive">The primitive.</param>
    /// <param name="inputs">The input images; converted copies replace entries.</param>
    /// <param name="temporaries">Receives the copies, which the caller disposes after the primitive has run.</param>
    /// <returns>The color space of the output.</returns>
    private SvgColorInterpolation PrepareInputs(SvgFilterPrimitive primitive, Span<SvgFilterImage?> inputs, List<SvgFilterImage> temporaries)
    {
        SvgColorInterpolation target = primitive.ColorSpace;
        if (primitive.PreservesColorSpace)
        {
            // A primitive that only moves or stacks pixels works in the space of its inputs, when they agree. Empty
            // inputs have no pixels, so their tag does not matter.
            SvgColorInterpolation? common = null;
            bool mixed = false;
            foreach (SvgFilterImage? input in inputs)
            {
                if (!input!.IsEmpty)
                {
                    mixed |= common is not null && common != input.ColorSpace;
                    common ??= input.ColorSpace;
                }
            }

            if (!mixed)
            {
                return common ?? target;
            }
        }

        for (int k = 0; k < inputs.Length; k++)
        {
            SvgFilterImage input = inputs[k]!;
            if (input.IsEmpty || input.ColorSpace == target)
            {
                continue;
            }

            SvgFilterImage copy = new(this.Allocator, input.Area, input.ColorSpace);
            input.Pixels.CopyTo(copy.Pixels);
            SvgFilterColor.Convert(copy, target);
            temporaries.Add(copy);
            inputs[k] = copy;
        }

        return target;
    }

    /// <summary>
    /// Renders <c>SourceGraphic</c>: the content of the element, or the result of the previous filter of a chain, clipped
    /// to the area that the primitives read, which lies inside the filter region.
    /// </summary>
    /// <param name="content">The content, or <see langword="null"/> when the element has nothing to draw or the filter is not the first of a chain.</param>
    /// <param name="previous">The sRGB result of the previous filter of a chain, or <see langword="null"/>.</param>
    /// <param name="area">The area in filter space.</param>
    /// <returns>The image, premultiplied sRGB.</returns>
    private SvgFilterImage RenderSource(SvgDocument? content, SvgFilterImage? previous, Rectangle area)
    {
        if (content is null && previous is null)
        {
            return new SvgFilterImage(this.Allocator, Rectangle.Empty, SvgColorInterpolation.SRgb);
        }

        SvgFilterImage image = new(this.Allocator, area, SvgColorInterpolation.SRgb);
        if (content is not null)
        {
            this.RenderDocument(content, Matrix3x2.Identity, image);
            return image;
        }

        // The copy clips the previous result to the area, so no primitive reads it outside the filter region.
        for (int y = area.Top; y < area.Bottom; y++)
        {
            previous!.CopyRow(y, area.Left, image.GetRow(y));
        }

        return image;
    }

    /// <summary>
    /// Derives <c>SourceAlpha</c> from <c>SourceGraphic</c>: the same alpha with black color channels.
    /// </summary>
    /// <param name="source">The <c>SourceGraphic</c> image.</param>
    /// <returns>The image.</returns>
    private SvgFilterImage CreateSourceAlpha(SvgFilterImage source)
    {
        SvgFilterImage image = new(this.Allocator, source.Area, SvgColorInterpolation.SRgb);
        Span<Rgba32> target = image.Pixels;
        ReadOnlySpan<Rgba32> pixels = source.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            target[i] = new Rgba32(0, 0, 0, pixels[i].A);
        }

        return image;
    }
}
