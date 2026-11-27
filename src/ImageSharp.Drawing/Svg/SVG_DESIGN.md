# SVG Rendering: Design Proposal

Status: accepted. Phases 0 to 5 are implemented; section 14 lists the phase 2 results, section 15 the phase 3 (text) results, section 16 the phase 4 (filters) results, and section 17 the phase 5 (decoder, CSS, and SVG 2) results, each with its known gaps. Section 20 holds Upstream Sync 1: the merge of upstream `main` (ImageSharp 5, the gradient alpha representation, `Fill(Pen, IPath)`), the removal of the decoder at the request of the upstream owner, and `init` accessors on the option classes. The implementation plans are in `SVG_IMPLEMENTATION.md` (phases 0 and 1), `SVG_IMPLEMENTATION_PHASE2.md` to `SVG_IMPLEMENTATION_PHASE5.md`; phase 6 (performance) is planned in `SVG_IMPLEMENTATION_PHASE6.md` and in progress: section 18 holds its baseline (the comparison matrix, peak memory, the ranked targets, and their profiles).

This document proposes static SVG rendering on top of `DrawingCanvas`. It covers scope, architecture, public API, performance design, benchmarks, and conformance tests. It compares the target feature set with the two renderers .NET users reach for today: NetVips (librsvg) and Skia.

## 1. Goals

- Render static SVG 1.1 documents, plus the commonly used parts of SVG 2, through the existing `DrawingCanvas` API. The same code path must work on the CPU backend and the WebGPU backend.
- Parse once, render many times. A loaded document is immutable, thread-safe, and resolution independent.
- Add no new package dependencies. The feature uses `System.Xml`, SixLabors.Fonts, and ImageSharp only.
- Treat SVG as untrusted input. Loading must be bounded in time and memory and must not read external resources by default.
- Measure everything. Parse time, render time, and allocations have benchmarks against NetVips and Svg.Skia before any optimization claim.

## 2. Non-goals

- Animation (SMIL), scripting, interactivity, and `foreignObject`.
- SVG fonts (`<font>`, `<glyph>`).
- An editable DOM. The loaded document is a render model, not an object model for mutation.
- Writing SVG. An SVG export backend is a separate feature.

## 3. What Exists Today

| Piece | Location | Notes |
|---|---|---|
| Path data parser | `Path.TryParseSvgPath` in `Path.cs` | Parses the `d` attribute from a `ReadOnlySpan<char>`. Hardened against malformed input. |
| Minimal SVG reader | `tests/.../TestUtilities/SvgBenchmarkHelper.cs` | Test-only. Reads `<path>` elements with fill, stroke, opacity, and transforms through `XDocument`. Drives the Tiger and Paris benchmarks. |
| Retained scenes | `DrawingCanvas.CreateScene()` and `RenderScene(...)` | A recorded command range becomes a reusable `DrawingBackendScene`. This is the equivalent of an `SKPicture`. |
| Layers and effects | `SaveLayer(...)`, `LayerEffect` | Isolated groups with `GraphicsOptions` and effects such as `BlurLayerEffect` and `DropShadowLayerEffect`. |
| Paint | `LinearGradientBrush`, `RadialGradientBrush` (two-circle), `ImageBrush`, `GradientRepetitionMode` | Cover SVG gradient geometry and spread methods. |
| Strokes | `Pen`, `StrokeOptions` | Width, dash pattern, dash offset, caps, joins, and miter limit. |
| Clips | `Clip(params IPath[])` | Paths combine as a union, then intersect the current clip. |
| Text | `DrawText`, `DrawGlyphs`, `TextBuilder` | Shaping through SixLabors.Fonts, including text on a path. |
| In-tree precedent | `Barcodes/`, `DrawingCanvas.DrawBarcode(...)` | A high-level feature that lowers itself into ordinary canvas commands. |

SixLabors.Fonts issues #568, #570, and #572 show that Fonts has its own SVG interpreter for OpenType SVG glyphs. This design has not inspected that code. Sharing the parser between the two libraries is a question for the maintainers.

## 4. Reference Renderers

### 4.1 What "NetVips" and "Skia" mean for SVG

- **NetVips** calls libvips `svgload`, which renders with librsvg on a cairo surface. The feature set is the feature set of librsvg. Options are `dpi`, `scale`, `unlimited`, `stylesheet`, and `high_bitdepth`. Output is straight-alpha RGBA, 8-bit sRGB by default. libvips re-renders the vector at the target scale for thumbnails. libvips flags the loader as untrusted because librsvg has not been fuzzed.
- **Skia** has a native SVG module (`SkSVGDOM`). SkiaSharp does not bind it (mono/SkiaSharp#2689 is open). `SkiaSharp.Extended.Svg` is deprecated. In practice, .NET users render SVG on Skia with **Svg.Skia**, which parses with a fork of SVG.NET and ExCSS, builds a retained scene, and records an `SKPicture`.

### 4.2 Feature comparison

"Phase" is the phase in section 11 where this proposal adds the feature. "Implemented" is the state after phase 5. "—" means not supported.

| Feature | librsvg (NetVips) | Skia `SkSVGDOM` | Svg.Skia | Phase | Implemented |
|---|---|---|---|---|---|
| `svg`, `g`, `defs`, `use` | Yes | Yes (`xlink:href` only) | Yes | 1 | Yes |
| `symbol` | Yes | — | Yes | 1 | Yes |
| `switch` | Partial | — | Yes | 2 | Yes |
| Nested `svg`, `viewBox`, `preserveAspectRatio` | Yes | Yes | Yes | 1 | Yes |
| Basic shapes and paths | Yes | Yes | Yes | 1 | Yes |
| Fill, stroke, dashes, caps, joins, miter limit | Yes | Yes | Yes | 1 | Yes (`miter-clip` and `arcs` fall back to `miter`); `pathLength` scales dashes (phase 5) |
| `paint-order` | Yes | — | Partial | 2 | Yes |
| `vector-effect: non-scaling-stroke` | Paths only | — | Partial | 2 | Yes |
| Markers | Yes | — | Partial | 2 | Yes (path, line, polyline, polygon, as Chromium) |
| Linear and radial gradients, spread, transform, href | Yes | Yes | Yes | 1 | Yes |
| Patterns | Yes | Yes | Yes | 2 | Yes (CPU backend only) |
| `clipPath`, `clip-rule` | Partial | Yes | Partial | 2 | Yes; CSS basic shapes and geometry boxes in `clip-path` since phase 5 (no `shape()`) |
| `mask`, `mask-type` | Yes | Luminance only | Yes | 2 | Yes |
| `opacity`, `fill-opacity`, `stroke-opacity` | Yes | Yes | Yes | 1 | Yes |
| `mix-blend-mode`, `isolation` | Yes (style only) | — | Yes (style only) | 2 | Yes |
| Presentation attributes and inline `style` | Yes | Yes | Yes | 1 | Yes |
| `<style>` element with selectors | Yes (full selector engine) | — | Yes (ExCSS) | 1 (simple), 2 (full) | Yes (full selectors, `media`, `:lang()` since phase 5) |
| User stylesheet | Yes | — | Yes | 2 | Yes |
| Custom properties and `var()` | Yes | — | Not stated | 5 | Yes: substitution at computed-value time, in CSS and in presentation attributes as Chromium accepts them; cycles, fallbacks, shorthands |
| Geometry properties and `d` in CSS | Partial | — | Not stated | 5 | Yes: `rect`, `circle`, `ellipse`, `path`, `use` (`x`, `y`), `image`; not on a nested `svg`, as Chromium |
| CSS `transform` syntax, `transform-box` | Partial | — | Not stated | 5 | Yes: CSS units, percentages of the reference box, math functions; 3D functions flattened to the plane |
| CSS Color 4 and 5 functions | Partial | — | Not stated | 5 | Yes: `hwb()`, `lab()`, `lch()`, `oklab()`, `oklch()`, `color()`, `none`, `color-mix()`, relative colors, `light-dark()`; clipped to sRGB as Chromium; not `currentColor` inside `color-mix()` and relative colors |
| `calc()`, `min()`, `max()`, `clamp()` | Partial | — | Not stated | 5 | Yes: lengths, numbers, angles, color channels, radii of basic shapes, `stroke-dasharray` lists, `transform-origin`; percentages mixed with lengths resolve against their reference; not in text position lists |
| `ch` from the font | — | — | Not stated | 5 | Yes, in shapes, basic shapes, text positions, markers, patterns, `use`, and `image` (not yet in the other attributes, 17.7); `ex` stays half the font size: the x-height, which Chromium uses, fails three conformance tests whose references use half the font size |
| `image` (raster, data URIs) | Yes | Yes | Yes | 2 | Yes (CPU backend only) |
| `image` (nested SVG file) | Yes | — | Yes | 2 | Yes; its text draws with the font settings of the outer load options since phase 5 |
| `text`, `tspan`, anchors, font properties | Yes | Yes | Yes | 3 | Yes (glyph outlines at load; section 15) |
| `textPath` | — | Yes | Yes | 3 | Yes (`path` elements and the `path` attribute, as Chromium) |
| Per-glyph `x`/`y`/`dx`/`dy`/`rotate` lists | Single values only | Yes | Yes | 3 | Yes |
| Filters | 17 primitives and `feDropShadow` | 14 primitives | All primitives modelled, partial parity | 4 | Yes: all primitives of Filter Effects Level 1 (section 16); no `BackgroundImage`, `BackgroundAlpha`, `FillPaint`, `StrokePaint`, as in Chromium; CPU backend only |
| CSS filter functions | Yes | — | Not stated | 4 | Yes (the ten functions and filter lists) |
| Image decoder (`Image.Load`, `Image.Identify`, `svgz`) | Yes (libvips `svgload`) | — | — | 5 | No: built in phase 5 (opt-in through `SvgConfigurationModule`), removed in Upstream Sync 1 at the request of the upstream owner (section 20); `SvgDocument.Render` and `DrawSvg` remain |
| Layer memory limit | — | — | — | 5 | Yes (`SvgLimits.MaxLayerPixels`, section 10) |
| Animation, scripting | — | — | Opt-in runtime | Never | — |
| SVG fonts | — | — | Not stated | Never | — |

### 4.3 Published conformance numbers

The resvg test suite has 1616 tests. The resvg project publishes these pass counts:

| Renderer | Tests passed |
|---|---|
| resvg 0.45.1 | 1520 |
| Chrome 123 | 1423 |
| Firefox 124 | 1385 |
| librsvg 2.58.0 | 1168 |
| Inkscape 1.3.2 | 985 |
| SVG.NET 3.2.3 | 530 |

No Skia-based renderer is in that chart. The SVG.NET number does not transfer to Svg.Skia, which has its own renderer.

The librsvg number is the practical target for this proposal: after phase 4 the pass count should be in the librsvg range. That is a goal, not a measured result.

## 5. Architecture

The pipeline has four stages. Each stage has one job and a reusable output.

```
bytes ──parse──> element tree ──resolve──> SvgDocument ──emit──> canvas commands ──(optional)──> DrawingBackendScene
                  (transient)              (immutable,            (per render)                    (retained, per target)
                                            cached by caller)
```

This follows the split that resvg uses between `usvg` and its renderer. The resolve stage does the SVG-specific work one time. The emit stage stays small and fast.

### 5.1 Parse

- Read with `XmlReader`, forward only. Do not build an `XDocument`.
- Pre-seed the `NameTable` with the known element and attribute names, so name matching is a reference comparison.
- Parse attribute values from `ReadOnlySpan<char>`: numbers, lengths, colors, transforms, and path data. Reuse `Path.TryParseSvgPath` for path data and `Color.TryParse` for hex and named colors. Extend the owning APIs when they lack a required form such as `rgb()` or `hsl()`.
- Store presentation properties in a compact struct with a "specified" bit mask. Do not use a dictionary per element. Since phase 5 the masks hold 128 properties (`SvgPropertySet`): the bits of the properties below 64 stay inline in `SvgSpecifiedStyle`, and the bits of the properties from 64 on live in its `SvgExtendedStyle` with their values, so the style table keeps its size and documents without the newer properties pay nothing for them.
- Collect `<style>` rules. When the document has no `<style>` element, skip the cascade engine.

### 5.2 Resolve

The resolve stage turns the element tree into a flat render model. After this stage, no string parsing and no reference lookup remains.

- Apply the CSS cascade and inheritance. Index rules by id, class, and tag so matching is not rules × elements.
- Resolve units to user-space pixels against the viewport and DPI.
- Convert every shape to an `IPath` one time. The path is in user space and does not depend on the output size.
- Resolve `href` chains for gradients and patterns. Build each brush one time and share it. Deduplicate solid brushes and pens by value.
- Resolve `use` and `symbol` by instancing. Each instance gets its own render nodes, because referenced content inherits style from the `use` element. The instances share geometry and paint objects.
- Expand markers into ordinary nodes.
- Resolve `objectBoundingBox` units where the bounding box is static.
- Remove nodes that cannot render: `display: none`, zero-size shapes, invalid references, and reference cycles.
- Decide the group strategy for each node (see section 7.1).

The output is `SvgDocument`: flat arrays of nodes in paint order, with subtree ranges. It is immutable and safe to share between threads.

### 5.3 Emit

`DrawingCanvas.DrawSvg(...)` walks the node array and records ordinary canvas commands: `Save`, `Clip`, `SaveLayer`, `Fill`, `Draw`, `DrawGlyphs`, `DrawImage`, and `Restore`. It follows the `DrawBarcode` pattern. No backend learns about SVG.

The viewport transform (`viewBox` and `preserveAspectRatio` mapped to the destination rectangle) composes with the active canvas transform. Path flattening stays scale-aware in the backend, so curves are smooth at every output size.

### 5.4 Retained scene

A caller that renders the same document repeatedly at the same size records it once and calls `CreateScene()`. `RenderScene(...)` then replays the backend-ready scene. This uses the existing API with no change.

## 6. Public API Sketch

Names are provisional.

```csharp
namespace SixLabors.ImageSharp.Drawing.Svg;

public sealed class SvgDocument
{
    public static SvgDocument Load(Stream stream);
    public static SvgDocument Load(Stream stream, SvgLoadOptions options);
    public static SvgDocument Parse(ReadOnlySpan<char> svg, SvgLoadOptions options);

    // Intrinsic size in pixels, from width/height or the viewBox.
    public SizeF Size { get; }

    public RectangleF? ViewBox { get; }
}

public sealed class SvgLoadOptions
{
    // The properties are init accessors since Upstream Sync 1 (section 20); the sketch of phase 1 had setters.
    public float Dpi { get; init; } = 96F;
    public IReadOnlyFontCollection? Fonts { get; init; }
    public string? DefaultFontFamily { get; init; }
    public string? UserStylesheet { get; init; }
    public Color CurrentColor { get; init; } = Color.Black;

    // Null means data URIs only. No file or network access.
    public ISvgResourceResolver? ResourceResolver { get; init; }

    // Input limits. See section 10.
    public SvgLimits Limits { get; init; }
}

public abstract partial class DrawingCanvas
{
    // Draws the document at its intrinsic size.
    public void DrawSvg(SvgDocument document, PointF origin);

    // Fits the document viewport into the destination, following preserveAspectRatio.
    public void DrawSvg(SvgDocument document, RectangleF destination);
}
```

Phase 5 added rendering to a new image, an ImageSharp decoder, and a layer memory limit. Upstream Sync 1 (section 20) removed the decoder at the request of the upstream owner: `Image.Load` of an SVG file would set expectations that SVG cannot meet, so a user creates an image of the size that is needed and calls `DrawSvg`, or calls `SvgDocument.Render`. The removed public types were `SvgDecoder`, `SvgDecoderOptions`, `SvgFormat`, `SvgImageFormatDetector`, and `SvgConfigurationModule`. Upstream Sync 1 also made every property of `SvgLoadOptions` and `SvgLimits` an `init` accessor, so the options of a loaded document cannot change. This is the public API that phase 5 added and that remains, all in `SixLabors.ImageSharp.Drawing.Svg` (`where TPixel : unmanaged, IPixel<TPixel>` on every generic method):

```csharp
public sealed partial class SvgDocument
{
    // Intrinsic size rounded up to whole pixels, drawn as DrawSvg(document, PointF.Empty) draws it, over a transparent
    // background, with Configuration.Default. Throws InvalidImageContentException for an unusable intrinsic size, more
    // than SvgLimits.MaxImagePixels pixels, a side over 65,535 pixels, or a draw over SvgLimits.MaxLayerPixels.
    public Image<TPixel> Render<TPixel>();
    public Image<TPixel> Render<TPixel>(Configuration configuration, Color backgroundColor);

    // The view box fits the whole image with the preserveAspectRatio of the root, as DrawSvg(document, destination)
    // fits it. The size is the caller's choice, so MaxImagePixels does not apply; ArgumentOutOfRangeException for a side
    // that is not positive.
    public Image<TPixel> Render<TPixel>(Size size);
    public Image<TPixel> Render<TPixel>(Configuration configuration, Size size, Color backgroundColor);
}

public sealed class SvgLimits
{
    // The largest total of the pixels of the layers that one draw keeps open at the same time.
    public long MaxLayerPixels { get; init; }                         // 268,435,456 (16 × 4096 × 4096); must be > 0
}
```

The options are set with an object initializer; the accessors validate their values as before:

```csharp
SvgLoadOptions options = new()
{
    Dpi = 144,
    FontCollection = fonts,
    Limits = new SvgLimits { MaxImagePixels = 16 * 1024 * 1024 },
};

SvgDocument document = SvgDocument.Load("drawing.svg", options);
using Image<Rgba32> image = new(800, 600);
image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 800, 600))));
```

## 7. Mapping SVG to the Canvas

| SVG | Canvas feature | Notes |
|---|---|---|
| `transform`, viewport | `DrawingOptions.Transform` | Composed at resolve time into one matrix per node. |
| `fill`, `fill-rule` | `Fill(brush, path)`, `DrawingOptions.IntersectionRule` | |
| `stroke-*` | `Draw(pen, path)`, `SolidPen`, `PatternPen`, `StrokeOptions` | `miter-clip` and `arcs` joins have no match and fall back to `miter`. Under a non-uniform scale or a skew the stroke is the outline of the user space stroke: `Fill(pen, path)` (upstream, Upstream Sync 1) when the transform does not enlarge, an outline built at device precision by the emitter when it enlarges (section 20). `vector-effect: non-scaling-stroke` builds its outline in host space. |
| `linearGradient` | `LinearGradientBrush` | `gradientTransform` maps to `GradientTransform`. SVG gradients interpolate unassociated colors: `AlphaRepresentation = PixelAlphaRepresentation.Unassociated` (the upstream form of the gradient option, Upstream Sync 1). |
| `radialGradient` | `RadialGradientBrush` two-circle form | Covers `fx`, `fy`, and `fr`. |
| `spreadMethod` | `GradientRepetitionMode.None`, `Repeat`, `Reflect` | |
| `pattern` | Tile rendered to an image, then `ImageBrush` with `WrapMode.Repeat` | Tile resolution follows the output scale. |
| `opacity` | Brush alpha or `SaveLayer` with `BlendPercentage` | See 7.1. |
| `mix-blend-mode` | `SaveLayer` with `GraphicsOptions.ColorBlendingMode` | ImageSharp has all 16 CSS blend modes. |
| `clipPath` | `Clip(paths)` | Complex clips fall back to the mask path. |
| `mask` | Nested `SaveLayer` with `DestIn` and a luminance-to-alpha `LayerEffect` | Needs a prototype. |
| `filter`, every filter primitive, CSS filter functions | An engine in `Svg/Filters` that renders the element off-screen, runs the primitives on buffers it owns, and draws the result as an image (`DrawImage` through the internal texture brush) | The planned mapping to `BlurLayerEffect`, `DropShadowLayerEffect`, `ColorMatrixLayerEffect`, and a filter-graph `LayerEffect` was dropped; see 7.3. |
| `text`, `tspan`, `textPath` | SixLabors.Fonts shaping, `DrawGlyphs`, `TextBuilder` path overloads | |
| `image` | `DrawImage` | Nested SVG files load as a child `SvgDocument` with the font settings of the outer document. |

### 7.1 Group opacity without layers

A layer costs an extra buffer and a composite pass. Most SVG files use `opacity` on elements where a layer is not needed. The resolve stage picks the cheapest correct strategy:

1. Opacity on a leaf with only a fill, or only a stroke: multiply the brush alpha. No layer.
2. Opacity on a leaf with fill and stroke, or on a group whose children can overlap: use `SaveLayer` bounded to the node bounding box.
3. Blend mode or mask: always a bounded layer. A filter is not a layer: the filter node renders its content off-screen and draws one image (7.3), and the layers around it are bounded by its filter region.

### 7.2 Known gaps to confirm with prototypes

These points came from reading the source before phase 1. The results after phase 2:

- Layer compositing with `PixelAlphaCompositionMode.DestIn` inside a parent layer, for masks: works on the CPU backend (`SvgMaskLayerPrototypeTests`). Masks use it with a luminance `LayerEffect`.
- A custom CPU `LayerEffect` on the WebGPU backend: not verified; the test container has no GPU.
- Per-path `clip-rule` when several clip paths combine in one `Clip` call: one call combines its paths with the non-zero rule, so a clip with several children, or with an even-odd child that is not a basic shape, draws as an alpha mask layer instead.
- Stroke bounding boxes for bounded layers: the resolve stage widens the bounds of stroked nodes itself.
- The cost of `Save`/`Restore` per node: not measured separately.

### 7.3 Filters do not use the canvas layer effects

Phase 4 planned to map `feGaussianBlur`, `feDropShadow`, and `feColorMatrix` to the public layer effects and to add a filter-graph `LayerEffect` for the rest. Reading the canvas code showed that a layer effect cannot give SVG results (`SVG_IMPLEMENTATION_PHASE4.md` section 2):

- A layer effect works on a snapshot of the layer region that the CPU backend intersects with the canvas bounds, and it gets no origin. Filter primitives need their position in user space (subregions, `feTile`, `feTurbulence`, `feImage`, light positions), and a blur or an offset near the canvas edge must read content outside the canvas.
- The snapshot is in device space; filter primitives work along the axes of the user space of the element, which under rotation or skew is not the device grid.
- The layer effects cannot clip to the filter region, cannot compute in linear RGB (the default of `color-interpolation-filters`), and have no primitive subregions.

Decision: one engine (`Svg/Filters/SvgFilterEngine.cs`) runs every filter. The filter node carries the content of the element as a nested document in the user space of the element, the way pattern tiles and markers hold theirs. At draw time the engine splits the transform to device space into a scale along the user axes and a remainder, renders the content into a buffer of that filter space, runs the primitive graph on premultiplied 8-bit RGBA buffers from the memory allocator of the canvas configuration, and draws the result through the remainder with the internal texture brush that `image` uses. For a transform that only scales and translates, filter pixels are device pixels and the result draws without resampling. The engine needs no change to the canvas and works the same on both backends, apart from the texture brush gap of the WebGPU backend (14.3). Phase 4 measured no case where a public layer effect would be both exact and faster, so no such path exists.

## 8. Performance Design

The design treats three costs separately, because callers pay them at different times.

| Cost | When | Design response |
|---|---|---|
| Parse and resolve | Once per document | Forward-only reader, span parsing, struct properties, flat arrays, no LINQ. |
| Emit | Once per render | Pre-built paths, brushes, pens, and options. The emit loop allocates no SVG objects. |
| Rasterize | Once per render | Existing backends. Fewer layers and culled nodes reduce the work. |

Specific decisions:

- **Path data dominates file size.** Parse `d` directly from the reader into the path builder. Measure whether reading the attribute in chunks into a pooled buffer beats `reader.Value` before adopting it.
- **Share geometry.** One `IPath` per shape, shared by every `use` instance. The per-path geometry cache then serves repeated renders at the same scale.
- **Share paint.** Many elements use the same few colors. Deduplicate brushes and pens at resolve time.
- **Cull early.** Skip nodes whose transformed bounds do not intersect the canvas bounds. This matters when a caller renders a region or a tile of a large document.
- **Avoid layers.** See 7.1.
- **No CSS cost for files without CSS.** Files with only presentation attributes skip selector matching.
- **Thread-safe documents.** A service loads an icon once and renders it from many requests without locks.
- **Pooled scratch memory.** Use the existing `MemoryAllocator` and pooling patterns for temporary buffers.

No number in this section is a measured result. Each decision has a benchmark in section 9 that confirms or rejects it.

## 9. Benchmarks

### 9.1 What to measure

Three BenchmarkDotNet classes under `tests/ImageSharp.Drawing.Benchmarks/Svg/`, all with `MemoryDiagnoser`:

| Benchmark | Measures | ImageSharp.Drawing | Svg.Skia | NetVips |
|---|---|---|---|---|---|
| `LoadSvg` | Bytes to render-ready document | `SvgDocument.Load` | `SKSvg.Load` (parse, scene, picture) | Not separable |
| `RenderSvg` | Render-ready document to pixels | `DrawSvg`, CPU and WebGPU, fresh and retained scene | `DrawPicture` | Not separable |
| `LoadAndRenderSvg` | Bytes to pixels | Both stages | Both stages | `SvgloadBuffer(...)` forced with `WriteToMemory()` |

NetVips renders lazily and cannot split parsing from rendering, so it takes part in the end-to-end benchmark only.

Sizes: 100, 1000, and 4000 pixels on the long edge. Small sizes expose fixed overhead. Large sizes expose rasterizer throughput.

### 9.2 Corpus

| File | Stresses | Status |
|---|---|---|
| Ghostscript Tiger | Path fills and strokes | In `tests/Images/Input/Svg` |
| Paris 30k | 30,000 paths, parse throughput, culling | In `tests/Images/Input/Svg` |
| Icon set | Per-document overhead | 40 Feather and Bootstrap icons, MIT |
| Illustration with gradients and group opacity | Layers and paint servers | Fluent Emoji Moose and Soft Ice Cream, MIT |
| Design-tool export with a `<style>` block | CSS cascade | `styled-landscape-synthetic.svg`, synthetic |
| Clips, masks, and blend modes | Phase 2 compositing layers | `compositing-dashboard-synthetic.svg`, synthetic |
| Patterns, markers, and an embedded image | Phase 2 paint and content | `pattern-chart-synthetic.svg`, synthetic |
| Text-heavy document | Shaping and glyph drawing | `text-heavy-synthetic.svg` and `text-decorations-synthetic.svg`, synthetic |
| Filter-heavy documents | Phase 4 filters | `filter-shadows-synthetic.svg` (96 drop shadows), `filter-blur-synthetic.svg` (one large blur), `filter-chain-synthetic.svg` (chains of several primitive kinds), synthetic |
| Custom properties and CSS functions | Phase 5 CSS | `css-features-synthetic.svg` (48 cards), synthetic |
| Chart with text | Thin strokes, text labels, line series | `chart-dashboard-synthetic.svg` (phase 6), synthetic |
| Map with many strokes | Stroker and stroke rasterization | `street-map-synthetic.svg` (phase 6, 3,600 strokes), synthetic |
| Icon sprite | `symbol` and `use` | `icon-sheet-synthetic.svg` (phase 6, 400 instances of the 40 corpus icons, MIT) |

### 9.3 Fairness rules

- Exclude image encoding. Every renderer writes to raw pixels in memory.
- Disable the libvips operation cache (`Cache.Max = 0`), or repeated loads of the same buffer return a cached result.
- Report two thread configurations: one thread, and the default. Set `MaxDegreeOfParallelism` for ImageSharp and `NetVips.Concurrency` for libvips. librsvg renders one image on one thread, so the default configuration favors a parallel rasterizer and the single-thread configuration shows the per-core cost.
- Check that the outputs are comparable. A renderer that skips an unsupported element does less work. Each corpus file gets an image comparison across the renderers before its numbers count.
- Run on the same machine and report the runtime, operating system, and CPU, as BenchmarkDotNet prints them.

### 9.4 Dependencies

The benchmark project gains `Svg.Skia`, `NetVips`, and `NetVips.Native`. These are test-only references. The tests pin SkiaSharp 2.88.9; the Svg.Skia version must match that SkiaSharp line, or the SVG benchmarks move to their own project.

### 9.5 Existing benchmarks

`FillTiger` and `FillParis` stay as they are. They measure the rasterizer with pre-built paths and remain the baseline for backend work. The throughput tool in `ImageSharp.Drawing.ManualBenchmarks` gains an SVG scenario for concurrent service load.

The benchmark harness lands before the renderer features, so each phase reports its numbers.

## 10. Security and Limits

SVG arrives from untrusted sources. Issue #385 showed crashes in the path parser on malformed input, and libvips marks its SVG loader as untrusted.

- **XML.** No external entities and no external DTD (`XmlResolver = null`). Internal entities stay enabled with a hard cap on expansion (`MaxCharactersFromEntities`), because Adobe Illustrator exports use them.
- **External resources.** Off by default. `data:` URIs work. Files and network access need a caller-supplied resolver.
- **Reference cycles.** `use`, gradient `href`, pattern, mask, clip, and filter references detect cycles at resolve time and drop the node.
- **Expansion.** Nested `use` can multiply nodes exponentially. Limits on instance count, element count, and nesting depth live in `SvgLimits`.
- **Buffers.** Limits on layer, pattern tile, and filter region sizes. Blur radius is clamped. `SvgLimits` has `MaxElements`, `MaxNodes`, `MaxNestingDepth`, `MaxCharactersFromEntities`, `MaxImagePixels`, `MaxPatternTilePixels`, `MaxTextCharacters`, `MaxFilterPixels`, and, since phase 5, `MaxLayerPixels`: the largest total of the pixels of the layers that one draw keeps open at the same time (default 16 × 4096 × 4096). A pass over the render nodes with the transform of the draw computes the total before anything is recorded, and a draw over the limit throws `InvalidImageContentException` without changing the canvas. Nested SVG images are part of the draw that shows them. Filter content and pattern tiles draw on canvases of their own, which are checked when they draw; filter buffers count against `MaxFilterPixels`. At 4000 pixels on the long edge the largest total of the benchmark corpus is 2.2 million pixels (StyledLandscape) and of the resvg suite 41 million (`masking/mask/mask-on-child`, 2.6 canvases), a margin of 6.5 to the default.
- **Numbers.** NaN, infinity, and overflow in lengths and transforms make the node invalid. They do not reach the rasterizer.
- **Decoder (phase 5, removed in Upstream Sync 1).** The decoder was removed at the request of the upstream owner (section 20); `SvgDocument.Render` at the intrinsic size keeps the size checks below, and `image` elements keep the 64 MiB bound of `svgz` data. As built in phase 5: `SvgDecoder` checked the size of the image before it allocated pixel memory: a size that the document determines (the intrinsic size, or the size that fits a target size) may have at most `SvgLimits.MaxImagePixels` pixels and at most 65,535 pixels on a side (an internal constant: the scratch rows of each rasterizer worker grow with the width of the canvas, about 128 bytes per pixel of width), and a document with a side that is zero, not finite, or larger than `int.MaxValue` pixels is refused; each throws `InvalidImageContentException`. `MaxImagePixels` is reused for the output size instead of a separate public limit (decided by the user), so one setting bounds both the decoded pixels of embedded `image` elements and the rendered output of the decoder and of `SvgDocument.Render` at the intrinsic size; `SvgDocument.Render(size)` with a caller's size is not limited. Gzip data (`svgz`) inflates into memory up to 64 MiB (67,108,864 bytes, the internal bound that `image` elements already used for `svgz` files), and more throws when the limit is reached. Detection reads 512 bytes: the root element, or a `DOCTYPE svg`, must start within them, so a document with a longer prolog is not detected and needs the decoder passed explicitly (the root element of every corpus and suite file starts within 277 bytes); ImageSharp reads every header into a stack buffer when no registered detector asks for more than 512 bytes, so a larger window would make the detection of every format allocate.
- **CSS values.** Internal limits, each of which makes the value invalid when exceeded: a `var()` substitution longer than 32,768 characters (a chain of custom properties that doubles at each step stops after about 12 steps), `var()` references and fallbacks nested deeper than 32, and 16 million characters produced by all substitutions of one document (`use` instances substitute again); math functions nested deeper than 32 or with more than 256 values and operators, and 65,536 math functions that mix units per document; color functions nested deeper than 16.
- **Fuzzing.** A fuzz target for `SvgDocument.Load` belongs in the test plan.

## 11. Phases

Each phase ships with conformance results and benchmark numbers.

| Phase | Content | Roughly comparable to |
|---|---|---|
| 0 | Agree scope and location upstream. Add the benchmark and conformance harness. | — |
| 1 | Structure (`svg`, `g`, `defs`, `use`, `symbol`), shapes, paths, fill and stroke properties, transforms, viewport, units, colors, opacity, gradients, presentation attributes, inline `style`, `<style>` with type, class, and id selectors. | Most icons and logos |
| 2 | `clipPath`, `mask`, `pattern`, markers, `image`, `switch`, blend modes, `paint-order`, full selectors, user stylesheet. | Beyond `SkSVGDOM` in CSS, markers, and blending |
| 3 | Text: `text`, `tspan`, `textPath`, anchors, font matching and fallback. | Text layout parity is the hardest area in every renderer |
| 4 | Filters: the `filter` property, the `filter` element, every primitive of Filter Effects Level 1, and the CSS filter functions, in one off-screen engine (7.3). | librsvg static feature list |
| 5 | Optional: ImageSharp decoder, more SVG 2 features. The decoder was removed in Upstream Sync 1 at the request of the upstream owner (section 20). | — |

## 12. Conformance Tests

- **Corpus.** The resvg test suite: about 1600 small SVG files with reference PNGs, MIT licensed, organized by structure, shapes, paint servers, painting, masking, text, and filters. Its README states that it exists to help other SVG libraries. The exact location of the reference PNGs and the licenses of the bundled fonts need a check before use.
- **Integration.** Keep the corpus out of the main tree (submodule or test-time download). The test images already use Git LFS.
- **Expected results.** A checked-in list records the expected status of every test: pass, or a known failure with a reason. A passing test that starts to fail breaks the build. A known failure that starts to pass must be promoted. This gives a pass count per category that can only go up, and it never hides a mismatch by editing a reference image.
- **Comparison.** Use the existing reference-image comparison in the test project with a tolerance. Anti-aliasing differs between rasterizers, so a strict pixel match is not the bar.
- **Second corpus.** The W3C SVG 1.1 test suite, later.

## 13. Open Questions

1. **Location.** Decided: the core package under `src/ImageSharp.Drawing/Svg/`, like `Barcodes/`. It adds no dependency and the assembly is trimmable. The upstream owner confirmed it in his reply to the proposal (discussion 435 of `SixLabors/ImageSharp.Drawing`, 27 November 2026): the SVG code stays in the existing package.
2. **Decoder integration.** Decided in phase 5: an SVG decoder in the core package, not registered by default. Answered by the upstream owner: no `.svg` decoder, because `Image.Load` of an SVG file would set expectations that SVG cannot meet; a user creates an image of the size that is needed and calls `DrawSvg`. Upstream Sync 1 removed the decoder (section 20). The owner also accepted the gradient option in the form that upstream merged (`AlphaRepresentation` of type `PixelAlphaRepresentation`), added `DrawingCanvas.Fill(Pen, IPath)`, and required `init` accessors on `SvgLoadOptions` and `SvgLimits`.
3. **Shared parser with Fonts.** Should the OpenType SVG glyph interpreter in SixLabors.Fonts and this feature share code?
4. **CSS depth for phase 1.** Simple selectors cover most design-tool exports. A full selector engine is a larger piece of work.
5. **Text fonts.** Decided in phase 3: the caller gives the fonts (`SvgLoadOptions.FontCollection`, default the system fonts), the generic families, the default family (`serif`), and the fallback families, with a fixed, documented fallback order. A document never causes a font file to be read.
6. **Phase 1 cut.** Decided: the phase 1 list in section 11 is the first release.

## 14. Phase 2 Results

### 14.1 Conformance

`SvgResvgConformanceTests` over the resvg test suite (1618 tests that are not undefined behavior). The renderer columns are the manual results in `results.csv`, which use a different criterion.

| Category | Run | Phase 1 | After package D | Phase 2 | resvg | Chrome | librsvg |
|---|---|---|---|---|---|---|---|
| filters | 380 | 65 | 65 | 55 | 337 | 303 | 280 |
| masking | 91 | 4 | 4 | 82 | 86 | 87 | 68 |
| paint-servers | 141 | 113 | 113 | 139 | 139 | 141 | 125 |
| painting | 298 | 148 | 150 | 258 | 294 | 264 | 268 |
| shapes | 128 | 118 | 120 | 120 | 123 | 126 | 118 |
| structure | 240 | 143 | 158 | 212 | 233 | 225 | 169 |
| text | 340 | 5 | 5 | 5 | 310 | 279 | 142 |
| total | 1618 | 596 | 615 | 871 | 1522 | 1425 | 1170 |

The phase 1 and package D columns count the tests listed as passing at those commits. Ten `feConvolveMatrix` tests passed in phase 1 only because pattern fills drew nothing; they are now listed as failing (phase 4). Every failing test has a reason in `ResvgExpectedResults.txt`.

### 14.2 Measured performance

BenchmarkDotNet short job on a shared 4-core Intel Xeon 2.10 GHz container, .NET 10.0.15. The error columns are wide; a difference counts only where the error intervals do not overlap.

- Phase 1 documents (Tiger, Paris 30k, Icons, Moose, Soft Ice Cream, Styled Landscape; `LoadSvg`, `RenderSvg`, and `LoadAndRenderSvg` at 100, 1000, and 4000 pixels): every mean is within the error interval of the phase 1 build (commit f3524dd) measured on the same machine. A medium-job rerun of the cases that moved most (Tiger and Soft Ice Cream render at 4000 pixels, Moose load and render at 4000 pixels, Paris load) gave equal means within 5%. Allocations are equal or lower; `LoadSvg` of the icon set allocates 17.5% less and is faster (654 ± 36 µs before, 589 ± 29 µs now).
- Phase 2 documents, load and render, against Svg.Skia and NetVips (librsvg):

| Document | Long edge | Svg.Skia | NetVips | ImageSharp.Drawing | ImageSharp.Drawing, one thread |
|---|---|---|---|---|---|
| Dashboard (clips, masks, blend modes) | 100 | 6.4 ± 13.9 ms | 14.7 ± 9.5 ms | 8.5 ± 6.7 ms | 6.6 ± 7.0 ms |
| Dashboard | 1000 | 14.4 ± 14.0 ms | 1034 ± 1587 ms | 102 ± 79 ms | 70 ± 53 ms |
| PatternChart (patterns, markers, image) | 100 | 12.9 ± 10.0 ms | 7.7 ± 5.9 ms | 3.4 ± 4.5 ms | 3.2 ± 6.5 ms |
| PatternChart | 1000 | 91 ± 668 ms | 195 ± 131 ms | 17.1 ± 3.2 ms | 38 ± 42 ms |

The Dashboard renders about 240 layers (each mask, blend mode, and coverage clip is a layer). ImageSharp.Drawing renders it about 7 times slower than Svg.Skia at 1000 pixels, and the multi-threaded render is slower than the single-threaded one. `--profile svg` puts 75% of the Dashboard time at 1000 pixels in the scene render and 16% in the scene build; for the PatternChart the scene build (46%) and the scene render (43%) dominate. Loading alone is 5 to 10 times faster than Svg.Skia for both documents. Before the numbers were taken, the outputs of all three renderers were compared with the Chromium oracle at 1000 pixels: total differences of 0.09% to 0.50%.

### 14.3 Known gaps

- WebGPU backend: the scene encoder lowers only the brush types it knows (`IsSupportedBrush` in `WebGPUSceneEncoder`) and skips the others without an error. The SVG pattern and image paints use an internal texture brush, so patterns and raster images do not draw on that backend. Masks use a custom CPU `LayerEffect`, whose behavior on that backend is not verified.
- Text, filters, and CSS basic shapes in `clip-path` are not supported (phases 3 and 4).
- Markers apply to `path`, `line`, `polyline`, and `polygon` only, as in Chromium; SVG 2 adds every basic shape.
- The SVG 2 keywords of `refX` and `refY` (`left`, `center`, `right`, `top`, `bottom`) act as 0, as in Chromium.
- A mask under a rotated or skewed canvas transform: the luminance effect runs over the layer rectangle, so mask content between the rotated rectangle and its bounding box can skip the effect (reported by the mask implementation; not reproduced here).
- Layer memory: each nested mask, blend mode, opacity group, or coverage clip is a layer of the size of its content, so memory grows with nesting depth times layer area. 400 nested masks with coverage clips need about 1.8 GB at 1000 pixels; the nesting depth is bounded by `SvgLimits.MaxNestingDepth`, the layer area was not bounded before `SvgLimits.MaxLayerPixels` (phase 5, section 10).
- Pattern tiles render on first use per output size and stay cached in the document (four sizes per tile), so a document drawn at large sizes keeps the tile pixels alive. Tiles of patterns nested more than three levels deep get half the pixel budget per further level.
- Arcs in path data (`A` commands) and the core `RoundedRectanglePolygon` flatten their arcs once, in their own coordinate space, so they look polygonal when a small shape is scaled up. SVG rounded rectangles avoid this by using cubic Bézier corners.
- The deprecated CSS `clip` property, external style sheets, and network references are not supported; file references load only from `SvgLoadOptions.ResourceBaseDirectory`.

## 15. Phase 3 Results (Text)

### 15.1 What renders

Text resolves to glyph outlines when the document loads, as in resvg: each glyph is an ordinary draw node that shares one outline per font and glyph, so fill, stroke, paint servers, `paint-order`, clips, masks, opacity, and both backends work as for shapes. The layout follows Chromium where the specifications leave a choice.

| Feature | Implemented |
|---|---|
| `text`, `tspan`, `a` in text, position lists, `letter-spacing`, `word-spacing`, `text-anchor`, white space | Yes; `white-space-collapse` and the CSS Text 4 forms of `white-space` since phase 5 |
| Font matching (CSS weight, style, stretch; typographic families) and per-character fallback | Yes, from `SvgLoadOptions.FontCollection` only |
| `textPath` (`startOffset`, `side`; `path` elements and the `path` attribute) | Yes; `method` and `spacing` have no effect, as in Chromium; `pathLength` of the path scales `startOffset` (phase 5) |
| `textLength`, `lengthAdjust` | Yes, also in vertical and right-to-left text and on a path |
| `text-decoration` | Solid lines with the metrics of the primary font, as resvg; no other styles, no skip-ink |
| `dominant-baseline`, `alignment-baseline`, `baseline-shift` | Yes, across the line in every writing mode; the `BASE` table is not used, as in Chromium |
| `font-variant` (caps), `font-kerning`, `kerning` | Yes, through OpenType features; no synthesized small capitals |
| `font-feature-settings`, `font-variant-ligatures`, `-caps`, `-numeric`, `-east-asian`, `-position` (phase 5) | Yes, resolved in the order of CSS Fonts 4; features turn on with value 1 only, and of the default features only `liga` and `kern` turn off (section 15.4) |
| Vertical `writing-mode` (upright and sideways glyphs, `sideways-rl`, `sideways-lr`) | Yes |
| `direction`, `unicode-bidi`, bidirectional reordering, complex script shaping | Yes; the Unicode bidirectional algorithm is a temporary local copy of the SixLabors.Fonts implementation (`Svg/Text/Bidi`), shaping is through SixLabors.Fonts |
| `tref` | Yes (resvg supports it; Chromium does not) |
| Text in `clipPath`, `mask`, `pattern`, `marker`, `use`, `switch` | Yes |
| Colour glyphs (COLR, CBDT) | Monochrome outlines only |
| `text-rendering` | No effect: the canvas has no hinting control |

### 15.2 Conformance

`SvgResvgConformanceTests`, criterion unchanged. Outside the text category, phase 3 changed outcomes only where those tests contain text. Package T6 (the bidirectional algorithm) changed no outcome and no mismatched-pixel count, and the saved renders of the failing text tests are pixel-identical: the suite has no case that the earlier workaround ordered or mirrored differently.

| Category | Run | Phase 1 | Phase 2 | Phase 3 | resvg | Chrome | librsvg |
|---|---|---|---|---|---|---|---|
| filters | 380 | 65 | 55 | 55 | 337 | 303 | 280 |
| masking | 91 | 4 | 82 | 86 | 86 | 87 | 68 |
| paint-servers | 141 | 113 | 139 | 140 | 139 | 141 | 125 |
| painting | 298 | 148 | 258 | 273 | 294 | 264 | 268 |
| shapes | 128 | 118 | 120 | 120 | 123 | 126 | 118 |
| structure | 240 | 143 | 212 | 220 | 233 | 225 | 169 |
| text | 340 | 5 | 5 | 45 | 310 | 279 | 142 |
| total | 1618 | 596 | 871 | 939 | 1522 | 1425 | 1170 |

Text, three numbers with the same criterion and the fonts of the suite:

- This renderer passes 45 of the 340 text tests.
- 139 more fail only on the pixels of the 0.5 wide crosshair line that most text tests draw (182 with at most 40 mismatched pixels elsewhere), so 184 (227) pass apart from that line.
- Chromium with the same fonts passes 59 (2 more fail only on the line, 5 with at most 40 elsewhere).

The crosshair line: at 500 px its horizontal stroke covers y = 249.375 to 250.625, so rows 249 and 250 each have an exact coverage of 0.625 (alpha 159). This renderer gives 159 and 159. The reference images (tiny-skia) and Chromium (Skia) give 128 and 192: both sample coverage on four sub-rows per pixel, which rounds each edge to a quarter pixel (249.5 and 250.75). Across columns all three are exact (159 or 160). The same quantization shows on a 0.5 wide path in `painting/stroke/control-points-clamping-1` (exact 32/255/32, reference and Chromium 0/255/64), where the full middle row keeps it inside the criterion. This renderer is exact; the references are quantized.

The remaining text failures by reason (`ResvgExpectedResults.txt`): rasterization of the crosshair 184, path placement within a pixel of the reference 26, vertical text where the reference differs from Chromium 12, defects of the reference image 9, fonts that the suite does not ship 8, Chromium behavior that the reference does not follow 7, initial font size 5, sub-pixel glyph positions 4, filters 4, and 1 to 3 each for white space, anchors, shaping across tspans, font faces, colour emoji, `tref`, `textLength`, spacing percentages, `glyph-orientation`, decoration thickness with `textLength`, and seven single cases.

### 15.3 Measured performance

BenchmarkDotNet 0.15.8 short job (3 warmup, 3 measured iterations, 1 launch) on a shared 4-core Intel Xeon 2.30 GHz container, .NET 10.0.15, Ubuntu 24.04. Means with the error column (99.9% interval); a difference counts only where the intervals do not overlap.

- Phase 1 and phase 2 documents (the eight entries from `Tiger` to `PatternChart`; every ImageSharp.Drawing method of `LoadSvg`, `RenderSvg`, and `LoadAndRenderSvg` at 100, 1000, and 4000 pixels; 152 cases): against the phase 2 build (commit 3c9c877) measured on the same machine, every mean lies within the error interval of the other. Allocations are equal within 0.5% (`LoadSvg` +0.1% to +0.3%, a few dozen bytes per document); only retained-scene renders, which allocate a few KB, vary by up to 3% in both directions.
- Text documents, with the same font file (Open Sans) for every renderer: ImageSharp.Drawing loads it into its font collection, Svg.Skia gets a typeface provider for it, and NetVips finds it through `fonts.conf` (`tests/SvgTools/README.md`). Before the numbers were taken, each renderer's output at 1000 pixels was compared with the Chromium oracle with the same font: all draw the same glyphs at the same positions; the pixel differences (7% to 12% total difference, about the same for every renderer) come from glyph antialiasing at 16 px, the continuous lines that Chromium draws for decorations on a path, and Svg.Skia's `textLength`.
- Text resolves to glyph outlines when the document loads, so `LoadSvg` includes font matching, shaping, and outline extraction, and `RenderSvg` draws prepared outlines. Svg.Skia also prepares at load (it records an `SKPicture`); NetVips cannot separate the stages.

| Document | Benchmark | Long edge | Svg.Skia | NetVips | ImageSharp.Drawing | ImageSharp.Drawing, one thread |
|---|---|---|---|---|---|---|
| TextHeavy (41 texts, 4,800 characters) | Load | — | 88.8 ± 66.7 ms, 23.7 MB | — | 3.5 ± 2.5 ms, 4.9 MB | — |
| | Render | 100 | 0.43 ± 0.10 ms | — | 6.8 ± 1.2 ms (scene 1.5 ± 1.9 ms) | 11.1 ± 15.5 ms |
| | Render | 1000 | 1.1 ± 1.3 ms | — | 14.8 ± 13.6 ms (scene 5.5 ± 10.3 ms) | 32.0 ± 83.0 ms |
| | Load and render | 100 | 85 ± 133 ms, 23.6 MB | 53 ± 42 ms | 10.2 ± 2.9 ms, 11.4 MB | 13.0 ± 10.2 ms |
| | Load and render | 1000 | 86 ± 140 ms, 23.7 MB | 83 ± 19 ms | 16.2 ± 9.8 ms, 13.0 MB | 30.6 ± 13.1 ms |
| TextDecorations (35 texts, 2,750 characters) | Load | — | 133 ± 63 ms, 33.5 MB | — | 2.7 ± 3.0 ms, 4.0 MB | — |
| | Render | 100 | 0.90 ± 0.09 ms | — | 5.1 ± 7.8 ms (scene 1.2 ± 1.8 ms) | 9.2 ± 2.4 ms |
| | Render | 1000 | 1.5 ± 1.7 ms | — | 11.8 ± 7.9 ms (scene 6.6 ± 3.1 ms) | 28.0 ± 17.0 ms |
| | Load and render | 100 | 138 ± 309 ms, 33.4 MB | 31.5 ± 10.3 ms | 10.2 ± 9.6 ms, 11.2 MB | 13.9 ± 0.8 ms |
| | Load and render | 1000 | 130 ± 51 ms, 33.4 MB | 60.2 ± 2.8 ms | 17.7 ± 14.5 ms, 11.8 MB | 29.9 ± 2.4 ms |

Where ImageSharp.Drawing is slower: drawing a loaded text document takes 5 to 16 times as long as Svg.Skia replaying its picture (the intervals do not overlap for TextHeavy at 100 px and TextDecorations at 1000 px; they overlap in the other two cases), and a fresh render allocates 6.6 to 8.5 MB against none for Svg.Skia, because every render builds the scene again; a retained scene allocates about 5 KB. Where it is faster: loading takes 1/25 to 1/50 of the Svg.Skia time with 5 to 8 times less managed memory, and load and render is 5 to 14 times faster than Svg.Skia and 3 to 5 times faster than NetVips by the means. The intervals do not overlap for NetVips at 1000 px for both documents and at 100 px for TextDecorations, and for Svg.Skia only for TextDecorations at 1000 px; the Svg.Skia error intervals are wide. NetVips allocates almost no managed memory; its native memory is not measured.

`--profile svg` (50 iterations, default threads) splits a TextHeavy load and render at 1000 px into the XML read 4%, resolve 24%, emit 15%, scene build 24%, and scene render 34%; for TextDecorations the scene build (22% at 1000 px, 39% at 100 px) and the scene render (29% to 34%) weigh more, because each decoration band and each glyph on a path is a node of its own. Inside the resolve stage, shaping takes about three quarters of the text layout and glyph outline extraction about a fifth; character building, font matching, and positioning take the rest (stage timings over 40 loads of each document in one thread).

### 15.4 SixLabors.Fonts

The text pipeline shapes one directional run at a time with `TextShaper.ShapeRun` and a `TextShapingBuffer`. What it would need public or added upstream (each checked against SixLabors.Fonts 3.2.1):

- The bidirectional algorithm and its resolved levels (`SixLabors.Fonts.Unicode.BidiAlgorithm` is internal). Until it is public, `src/ImageSharp.Drawing/Svg/Text/Bidi` holds a copy of `BidiAlgorithm`, `BidiData`, and their helper types from SixLabors.Fonts 3.2.1 (package T6); the copy must be removed when SixLabors.Fonts exposes the levels. The copy changes three places of the source that took quadratic time without changing the results (rule W2, the joining of level runs into isolating run sequences, and the search for the run after an isolate); it gives the levels of all 91,707 cases of the Unicode `BidiCharacterTest.txt`. Each text chunk is one paragraph; its level runs are shaped one at a time, in their direction, and the shaper mirrors the characters of right-to-left runs (rule L4), so a lone bracket at an odd level now draws mirrored.
- A bidirectional mode (`TextBidiMode` exists for `TextOptions` only) and context characters before and after a run on `TextShapingBuffer`, so that Arabic joins across a `tspan` boundary as in Chromium.
- A way to turn off default features, and feature values: `ShapeRun` only turns features on, with the value 1, so ligatures under `letter-spacing`, `font-variant-ligatures: none`, and `"liga" 0` are broken by shaping each grapheme again; `calt`, `clig`, and the other default features cannot be turned off, and `"salt" 2` selects the first alternate.
- `FontMetrics.TryGetBaselineCoordinate` (the `BASE` table) is internal. Chromium does not use the table either, so nothing renders differently today.
- The OS/2 width class: the font matcher reads `usWidthClass` from the raw table data for `font-stretch`.
- Family grouping by typographic family name (name ID 16): `FontCollection` groups faces by their legacy family name, so the font matcher also collects the families whose faces name the requested family as their typographic family.

### 15.5 Known gaps

- Decorations follow the font metrics (resvg): thickness, line-through, and overline position differ from Chromium, which uses a tenth of the font size; only solid lines; no skip-ink; on a path one band per glyph, where Chromium draws one curved line.
- Paint servers in bounding box units on upright glyphs of vertical text map the box in user space, as SVG defines; Chromium maps them in a turned frame.
- Colour glyphs (COLR, CBDT) draw monochrome; `text-rendering` has no effect; no synthesized small capitals; a `kerning` length adds no spacing.
- `textPath` references `path` elements only, as Chromium; `method` and `spacing` have no effect. The cross-axis direction of `x` and `dx` for `sideways-lr` text on a path was not checked against Chromium.
- Text in an SVG file that an `image` element references draws nothing (an open decision). Phase 5: the user decided that it draws, with the fonts of the outer load options (section 17.4).
- Arabic letters do not join across a `tspan` boundary, and `letter-spacing` does not turn off Arabic ligatures (Chromium does both). These are limits of the public shaping API (section 15.4), not of the bidirectional algorithm; `SvgTextDirectionTests` holds a test of each.
- Bidirectional text costs time linear in its length: 200,000 bidirectional control characters with 100,000 letters load and draw in about 0.3 s (6 s before package T6, which also removed a quadratic search for the end of a shaping run), and 100,000 matched isolates or 100,000 numbers after an Arabic letter in about 0.3 s to 0.8 s (shared 4-core machine, noisy).
- WebGPU backend: not verified (no GPU in the test container).

## 16. Phase 4 Results (Filters)

### 16.1 What renders

The `filter` property (`url()` references, the ten CSS filter functions, and lists of both), the `filter` element with its regions and units, and every primitive of Filter Effects Level 1: `feBlend`, `feColorMatrix`, `feComponentTransfer`, `feComposite`, `feConvolveMatrix`, `feDiffuseLighting`, `feDisplacementMap`, `feDropShadow`, `feFlood`, `feGaussianBlur`, `feImage`, `feMerge`, `feMorphology`, `feOffset`, `feSpecularLighting`, `feTile`, `feTurbulence`, and the light sources `feDistantLight`, `fePointLight`, `feSpotLight`. The properties `color-interpolation-filters`, `flood-color`, `flood-opacity`, and `lighting-color` work as presentation attributes, in `style`, and in style sheets. They filled the 64-bit property masks of `SvgSpecifiedStyle` (`lighting-color` is the 64th property); phase 5 widened the masks to 128 properties (section 5.1).

Filters work on every element that Filter Effects names (containers, shapes, `image`, `text`, `use`, the root `svg`), inside `pattern`, `marker`, and `mask` content, nested, and with every compositing feature on the same element (filter first, then clip, mask, opacity, blend mode). Section 7.3 describes the engine. Where the specification leaves a choice the engine follows Chromium; where the specification settles a question it follows the specification, also against Chromium and the resvg references (16.4).

### 16.2 Conformance

`SvgResvgConformanceTests`, criterion unchanged. Phase 4 changed outcomes only in the filters category. The "Chromium render" column applies the same criterion to Chromium renders of the suite (`tests/SvgTools/render-svg.js`): the calibration of the filter and text work. The last three columns are the manual results of `results.csv`, which use another criterion.

| Category | Run | Phase 1 | Phase 2 | Phase 3 | Phase 4 | Chromium render | resvg | Chrome | librsvg |
|---|---|---|---|---|---|---|---|---|---|
| filters | 380 | 65 | 55 | 55 | 296 | 297 | 337 | 303 | 280 |
| masking | 91 | 4 | 82 | 86 | 86 | — | 86 | 87 | 68 |
| paint-servers | 141 | 113 | 139 | 140 | 140 | — | 139 | 141 | 125 |
| painting | 298 | 148 | 258 | 273 | 273 | — | 294 | 264 | 268 |
| shapes | 128 | 118 | 120 | 120 | 120 | — | 123 | 126 | 118 |
| structure | 240 | 143 | 212 | 220 | 220 | — | 233 | 225 | 169 |
| text | 340 | 5 | 5 | 45 | 45 | 59 | 310 | 279 | 142 |
| total | 1618 | 596 | 871 | 939 | 1180 | — | 1522 | 1425 | 1170 |

The 296 passing filter tests and the passing test elsewhere that involves a filter (`masking/clipPath/filter-has-no-effect`) were checked against the Chromium render with the same criterion: 288 match it; the other 9 differ from Chromium where this renderer and the reference both follow the specification (16.4: the filter region clip, the `feImage` cycles, the zero-sum kernel, `limitingConeAngle="0"`, a spot light exponent below 1). 80 of the 297 also pass when the filter is replaced by nothing or by its unfiltered source; each was read and is an error case whose correct result is exactly that (an invalid attribute that makes a primitive a pass-through or transparent, a filter value that the rules ignore, a region outside the view). `feDisplacementMap/simple-case` contains no filter and passes trivially.

The 84 failing filter tests by reason group: reference 39 (the reference image differs from the specification and from Chromium, 21 of them lighting tests), specification 36 (16.4), text 7 (8 px labels; the filtered squares match), image 2 (files outside `ResourceBaseDirectory`).

### 16.3 Measured performance

BenchmarkDotNet 0.15.8 short job (3 warmup, 3 measured iterations, 1 launch) on a 4-core Intel Xeon 2.30 GHz container (a shared virtual machine), .NET 10.0.15, Ubuntu 24.04.5. Means with the error column (99.9% interval); a difference counts only where the intervals do not overlap. The default-thread runs of ImageSharp.Drawing vary from run to run more than their error columns show (the same code path measured 57 to 69 ms on the shadow document in three runs of 15 iterations).

Comparability, checked before the numbers were taken: each renderer's output at 1000 pixels against the Chromium oracle (`--verify svg 1000`, `compare-png.js`), total difference: shadows Svg.Skia 0.04%, NetVips 0.16%, ImageSharp.Drawing 0.14%; blur 1.14%, 0.36%, 0.09%; chain 0.57%, 0.27%, 0.13%. All three draw the same content, with one exception: Svg.Skia draws the paper tile of the chain document flat, without its turbulence texture, so it does less work there.

Load and render, bytes to pixels. Managed allocation is given for ImageSharp.Drawing only, as an absolute value; Svg.Skia and NetVips allocate mostly native memory, so their managed allocation is not comparable.

| Document | Long edge | Svg.Skia | NetVips | NetVips, one thread | ImageSharp.Drawing | ImageSharp.Drawing, one thread | ImageSharp.Drawing allocated |
|---|---|---|---|---|---|---|---|
| FilterShadows (96 drop shadows) | 100 | 22.7 ± 22.8 ms | 9.8 ± 8.4 ms | 9.6 ± 6.2 ms | 8.7 ± 6.3 ms | 5.1 ± 1.5 ms | 4.6 MB |
| | 1000 | 42.4 ± 44.3 ms | 956 ± 625 ms | 796 ± 1381 ms | 56.6 ± 36.2 ms | 61.9 ± 40.8 ms | 8.0 MB |
| FilterBlur (one large blur) | 100 | 1.9 ± 7.4 ms | 3.7 ± 2.5 ms | 3.6 ± 0.7 ms | 1.3 ± 0.8 ms | 1.4 ± 1.5 ms | 0.4 MB |
| | 1000 | 26.3 ± 13.2 ms | 143 ± 77 ms | 137 ± 39 ms | 28.6 ± 4.1 ms | 81.5 ± 108.7 ms | 3.4 MB |
| FilterChain (six primitive chains) | 100 | 2.2 ± 1.7 ms | 4.3 ± 1.1 ms | 4.2 ± 2.2 ms | 1.4 ± 1.0 ms | 1.3 ± 1.4 ms | 0.4 MB |
| | 1000 | 62.7 ± 4.4 ms | 415 ± 412 ms | 410 ± 565 ms | 41.1 ± 86.6 ms | 71.3 ± 7.9 ms | 3.2 MB |

Loading alone takes 1.48 ± 1.61 ms against 15.3 ± 1.6 ms for Svg.Skia (shadows; the intervals do not overlap), and 0.13 ms against 1.5 to 2.0 ms for blur and chain (overlapping intervals); by the means 10 to 15 times less. Drawing a loaded document (`RenderSvg`, fresh render) takes longer than Svg.Skia's replay of its recorded picture, by the means 4 to 25 times at 1000 pixels: shadows 55 ± 117 ms against 2.2 ± 0.9 ms, blur 35.7 ± 42.6 ms against 9.3 ± 8.5 ms, chain 52 ± 89 ms against 2.3 ± 1.0 ms (overlapping intervals); whether Skia keeps filter results between replays was not checked. A retained scene of ImageSharp.Drawing (9.5 to 10.5 ms at 1000 pixels) holds the filter results as textures computed when the scene was recorded, so it measures the replay only. Load and render, where the intervals do not overlap: ImageSharp.Drawing is faster than NetVips for FilterShadows and FilterBlur at 1000 pixels and FilterChain at 100 pixels (by the means 5 to 17 times at 1000 pixels); against Svg.Skia every interval overlaps.

Optimizations of this phase, each measured with 15 iterations of `RenderSvg` at 1000 pixels unless stated (before → after):

| Change | Measurement | Kept |
|---|---|---|
| Box blur: an exact 64-bit multiply and shift instead of the integer division per channel | FilterBlur one thread 100.7 ± 7.3 → 77.5 ± 1.2 ms, default 77.3 ± 14.6 → 49.9 ± 5.0 ms; FilterShadows one thread 83.5 ± 6.1 → 59.3 ± 1.6 ms | Yes |
| Vertical blur pass on strips of 16 columns (a blocked transpose) instead of one strided column at a time | FilterBlur one thread 77.5 ± 1.2 → 73.0 ± 0.8 ms; default and FilterShadows within the intervals | Yes |
| Blur passes in bands of rows and strips on the threads of the configuration | FilterBlur default 49.3 ± 6.8 → 31.1 ± 1.4 ms; one thread within the intervals; small images stay on the calling thread | Yes |
| Every other primitive fills its output in bands of rows on the threads of the configuration | FilterChain default 49.4 ± 1.5 → 33.5 ± 1.2 ms; FilterShadows within the intervals; a 16 × 16 convolution at 4000 pixels 4.3 to 5.1 s → 1.4 to 2.1 s (three runs each) | Yes |
| The engine uses the configuration of the canvas (allocator, degree of parallelism, nested canvases) instead of `Configuration.Default` | A correctness change: a one-thread configuration now also applies to filter sources | Yes |
| The source of a small element renders through a one-thread nested canvas | FilterShadows 60.8 ± 9.7 → 51.3 ± 7.8 ms (overlap), FilterChain 33.5 ± 1.2 → 38.5 ± 2.3 ms (slower) | Reverted |

Against the start of the phase 4 integration (commit ecb160d, short job): load and render of FilterBlur at 1000 pixels 70.5 ± 19.3 → 28.6 ± 4.1 ms; the other cases moved by the means (FilterShadows 199 → 57 ms, FilterChain 69 → 41 ms) within overlapping intervals. Managed allocation changed by −6.5% to +6.9% (the band views and delegates of a run).

`--profile svg` with `dotnet-trace` at 1000 pixels, before the changes: the blur took 25% (chain), 37% (shadows), and 59% (blur) of the filter time, the render of the source content 11%, 35%, and 38%, turbulence 15% of the chain, and allocating and clearing buffers 14% of the shadows. After them, in one thread, the shadows split into source render 31%, blur 30%, and buffer allocation 17%.

Regression check against the phase 3 merge (commit a02a935) built in a separate tree, same machine, short job: the ten phase 1 to 3 entries, `LoadSvg`, `RenderSvg` (default and one thread), and `LoadAndRenderSvg` (default and one thread) at 100, 1000, and 4000 pixels, 130 cases: no case is slower (the intervals overlap in 129 cases; `LoadAndRenderSvg` of StyledLandscape at 100 pixels is faster), and allocations differ by −0.8% to +1.3%. The retained-scene methods replay backend scenes, which phase 4 did not change, and were not run.

### 16.4 Where the renderer follows the specification against Chromium or the references

These are decisions for the maintainers; the behavior follows the specification today. During phase 5 the user confirmed these choices: the rules that follow the specification over Chromium stay. "Fail" counts the conformance tests that fail because of the rule; "If Chromium" is the change in passing tests if the rule followed the Chromium render instead, counting a test as passing when the Chromium render passes it. FE1 is Filter Effects Module Level 1.

| Rule | Specification | Chromium renders | References show | Fail | If Chromium |
|---|---|---|---|---|---|
| The filter region clips the input of every primitive, `SourceGraphic` included | FE1, "Filter Region": a hard clip on the input and the output of the filter | No clip on the inputs | A clip on the input, also to the primitive subregion | 1 | −3 (3 passing tests fail; the failing one fails in Chromium too) |
| `filter` has no effect on `tspan` and `textPath` | FE1, the `filter` property: in SVG it applies to container elements, graphics elements, and `use` | Applies it | Apply it | 2 | +1 (`tspan/with-filter`; Chromium fails `textPath/with-filter` on the crosshair) |
| `feConvolveMatrix` with an unfulfilled precondition (entry count not `orderX * orderY`, an order below 1, a target outside the kernel) is a pass-through | FE1, `feConvolveMatrix`, `kernelMatrix` (and the change list for the other preconditions) | Transparent black | The element not drawn (an error in SVG 1.1) | 9 | +9 |
| `divisor="0"` uses the default divisor | FE1, `feConvolveMatrix`, `divisor` | As this renderer | The element not drawn (SVG 1.1) | 1 | 0 |
| A kernel that sums to 0 without `divisor` divides by 1 | FE1, `feConvolveMatrix`, `divisor` | Another result (68,986 mismatched pixels) | As this renderer | 0 | −1 |
| A missing `url()` inside a list ignores the whole list | FE1, the `filter` property: a reference to a missing object or a non-filter element ignores the whole filter chain | Skips that entry | Skip that entry | 1 | +1 |
| A single missing `url()` draws the element without a filter | FE1, as above | As this renderer | The element not drawn (SVG 1.1) | 1 | 0 |
| `href` on `filter` is not followed | FE1 has no `href` on `filter` | Not followed | Followed | 3 | 0 |
| `BackgroundImage`, `BackgroundAlpha`, and `enable-background` are not supported | FE1 dropped `enable-background` | As this renderer | Supported | 19 | 0 |
| An `feImage` cycle drops the element that closes it | Reference cycles are errors in SVG; FE1 does not define the result | Renders the content (32,041 to 90,000 mismatched pixels) | As this renderer | 0 | −3 |
| Lighting uses the edge and corner kernels at the subregion edge | FE1, `feDiffuseLighting`, the nine Sobel kernels | Repeats the edge pixels and uses the interior kernel | Not distinguishable in the suite | 0 | 0 |
| `limitingConeAngle="0"` lights nothing | FE1, `feSpotLight`: a cone of the given angle | No cone | As this renderer | 0 | −1 |
| A spot light `specularExponent` below 1 is used as given | FE1, `feSpotLight` (no range) | Clamps to 1 to 128 | As this renderer | 0 | −1 |
| `specularConstant="0"` gives transparent black | FE1, the specular lighting equation | Opaque black | No test | 0 | 0 |
| `surfaceScale` and the light heights are in user units | FE1, the lighting equations in the user space of the element | As this renderer | Flatter, as if in device pixels at 500 px | 21 | 0 |
| A primitive subregion clips only the result, not the input | FE1, "Filter Primitive Subregion" | As this renderer | The input clipped too | 5 | 0 |
| `feDisplacementMap` does not convert `in` and reads nothing beyond the filter region | FE1, `feDisplacementMap`: `color-interpolation-filters` applies to `in2` only | Converts `in` to the space of the primitive and back, which loses dark colors | No test (the one suite test has no filter) | 0 | 0 |
| A length without a unit in a filter function (`blur(4)`) is invalid | CSS Values and Units, FE1 filter functions | Refuses it | Accept it | 1 | 0 |
| The specular exponent clamps to 1 to 128 | SVG 1.1 gives that range; FE1 gives no error rule | Clamps | The element not drawn for 0; another highlight edge for 256 | 2 | 0 |

The lighting count is 21, not the 20 reported by the lighting work: 9 tests at 53,525 mismatched pixels, 2 at 25,024, 2 at 518, and 8 single cases (`ResvgExpectedResults.txt`). The `feConvolveMatrix` pass-through count is 9; the tenth convolution failure (`divisor=0`) is a rule of its own.

### 16.5 Limits

- `SvgLimits.MaxFilterPixels` (default 4096 × 4096) bounds each buffer of a filter run; a larger working area runs at a lower resolution and draws scaled up. One buffer takes 4 bytes per pixel; a run keeps `SourceGraphic`, `SourceAlpha`, the results that later primitives still read, the output being computed, and converted copies of its inputs alive. No side of a buffer exceeds 16,384 pixels.
- Filters nested more than three levels deep get half the pixel budget per further level.
- A standard deviation is limited to 8,192 filter pixels, a morphology radius to the working area, a convolution kernel to 256 entries (a larger one gives transparent black), and `numOctaves` to 9.
- A filter list of more than 32 entries does not draw. The limit stays an internal constant (`SvgResolver.MaxFilterListEntries`), not a property of `SvgLimits`: no real document comes near it, and the other bounds on primitive parameters (kernel entries, octaves, deviation) are constants too.
- `feImage` content counts against `SvgLimits.MaxNodes` and `MaxNestingDepth`, so a fan-out through `feImage` fails at load.
- Hostile documents (`SvgFilterHostileDocumentTests`, 1000 × 1000 pixels): a region of 2 × 10^9 units, a deviation of 10^30, a morphology radius of 10^30, a 16 × 16 kernel, 10^9 octaves, 400 primitives, a list of 20,000 entries, 504 nested filters, an `feImage` fan-out to 4^20 elements, and 1,600 small filtered elements all finish or fail at a limit. Each case compares two input sizes instead of an absolute time, so the tests hold on a loaded machine; `SvgTextHostileDocumentTests` and the text path robustness test now work the same way.

### 16.6 Known gaps

- WebGPU backend: not verified (no GPU in the container). The filter result draws through the internal texture brush, which the WebGPU scene encoder does not lower (14.3), so filtered elements are expected not to draw on that backend. The engine itself runs on the CPU and needs nothing from the backend.
- The engine converts a copy of an input to the color space of a primitive even when the primitive reads only alpha (lighting). It costs one copy and conversion of that input, only when the input and the primitive differ in color space; no benchmark document has the case, so it is not changed.
- `feImage` content resolves again for every element that uses the filter: it resolves in the user space of each filtered element, so sharing it would need a cache keyed by that space.
- `MaxFilterPixels` applies per buffer: a huge region with a huge reach lowers the resolution of the whole run even when the visible part is small, because the reach makes the input area large.
- The deviation cap is in filter pixels, so a blur far above it (`stdDeviation="1e30"`) gives a result that depends on the output size.
- Under a projective caller transform the filter runs at one scale, the largest scale of the linear part of the transform, in the space before the transform, and the result draws through the transform; where the perspective enlarges it more, it is resampled.
- Pattern tiles still render with `Configuration.Default`; the filter engine uses the configuration of the canvas.

## 17. Phase 5 Results (Decoder and More SVG 2)

The decoder of this phase (`SvgDecoder`, `SvgDecoderOptions`, `SvgFormat`, the two format detectors, and `SvgConfigurationModule`) was removed in Upstream Sync 1 at the request of the upstream owner (section 20). The descriptions and the measured numbers below stay as the history of the phase; `SvgDocument.Render`, the layer memory limit, and the CSS and SVG 2 features remain.

### 17.1 What renders

| Area | Implemented |
|---|---|
| Decoder (removed in Upstream Sync 1; `SvgDocument.Render` remains) | `SvgFormat`, `SvgImageFormatDetector`, `SvgDecoder`, `SvgDecoderOptions`, `SvgConfigurationModule` (opt-in), and `SvgDocument.Render` (section 6): UTF-8, UTF-16, and gzip input; `Identify`; `Decode` at the intrinsic size or directly at the target size; the limits of section 10. |
| Layer memory | `SvgLimits.MaxLayerPixels`, checked before a draw changes the canvas. |
| Custom properties | `var()` with fallbacks, inheritance, cycles, the guaranteed-invalid value, shorthands, and presentation attributes where Chromium accepts it, with bounded substitution (section 10). |
| Geometry properties | `x`, `y`, `width`, `height`, `cx`, `cy`, `r`, `rx`, `ry`, `d: path()` and `none` from CSS, on `rect`, `circle`, `ellipse`, `path`, `use` (`x`, `y`), and `image`. |
| CSS transform | CSS syntax with units, percentages of the reference box, `transform-box`, math functions, 3D functions flattened to the plane; on the root `svg` and on a path that `textPath` references, also with percentages. |
| Colors | CSS Color 4 and 5 functions (section 4.2); no `currentColor` inside `color-mix()`, relative colors, and `light-dark()` (17.7). |
| Math functions | `calc()`, `min()`, `max()`, `clamp()` in lengths, numbers, angles, color channels, `transform-origin` of elements and paint servers, the radii of basic shapes, and `stroke-dasharray` lists. |
| `ch` | The advance of "0" in the font of the element, in shapes, basic shapes, text positions, markers, patterns, `use`, and `image`. |
| Basic shapes in `clip-path` | `inset()`, `circle()`, `ellipse()`, `polygon()`, `path()`, `xywh()`, `rect()`, and boxes alone, with `fill-box`, `stroke-box`, `view-box` (and the CSS box names), as attribute and in CSS. |
| SVG 2 items | `pathLength` on dashes and `textPath`; the font feature and font variant properties; `white-space-collapse` and the CSS Text 4 forms of `white-space`; `:lang()`; `image-rendering` `pixelated` and `crisp-edges`; `paint-order`, `vector-effect`, and `mix-blend-mode` on text and through `use`. |

Where the features meet (the cross-feature tests of `SvgCrossFeatureRenderTests`, 15 documents compared with the Chromium oracle at the test size: total differences 0 to 0.95%, all of them antialiasing of edges and glyphs and the decisions of 17.4): `var()` and math functions inside basic shapes and inside `d: path()`, `ch` in basic shapes, a CSS `d` with `pathLength`, dashes, markers, and `textPath`, geometry from CSS with clip shapes and their boxes, markers, patterns and masks in `objectBoundingBox` units, filter regions, and `use`, the CSS transform with `transform-box` and clip shapes, `var()` in paint, geometry, filters, and the font feature properties, the new colors in gradient stops, `flood-color`, `lighting-color`, and `currentColor`, `:lang()` rules that set geometry and custom properties, clip shapes with masks, filters, opacity, blend modes, patterns, and `use`, text inside embedded SVG images, and the decoder with text, an image, filters, custom properties, and clip shapes in one document (`SvgDecoderTests.Decode_DocumentWithEveryFeatureKind_EqualsDrawSvg`, removed with the decoder in Upstream Sync 1; the document keeps its reference image in `SvgCrossFeatureRenderTests`: equal to `DrawSvg` at the intrinsic size and at a target size; 0.40% and 0.15% from the oracle).

Defects that the cross-feature tests found, each fixed with a reference image: `round` with a math function in `inset()`, `xywh()`, and `rect()` was split at its spaces and dropped the shape; `ch` in basic shapes used half the font size; `textPath` on a path whose geometry comes from CSS `d` used the `d` attribute (and `pathLength` scaled `startOffset` by the length of the attribute path).

### 17.2 Conformance

`SvgResvgConformanceTests`, criterion unchanged.

| Category | Run | Phase 4 | Phase 5 | resvg | Chrome | librsvg |
|---|---|---|---|---|---|---|
| filters | 380 | 296 | 296 | 337 | 303 | 280 |
| masking | 91 | 86 | 87 | 86 | 87 | 68 |
| paint-servers | 141 | 140 | 140 | 139 | 141 | 125 |
| painting | 298 | 273 | 273 | 294 | 264 | 268 |
| shapes | 128 | 120 | 121 | 123 | 126 | 118 |
| structure | 240 | 220 | 217 | 233 | 225 | 169 |
| text | 340 | 45 | 45 | 310 | 279 | 142 |
| total | 1618 | 1180 | 1179 | 1522 | 1425 | 1170 |

Outcome transitions of the phase:

| Test | Phase 4 | Phase 5 | Reason |
|---|---|---|---|
| `masking/clipPath/circle-shorthand.svg` | fail | pass | `circle()` in `clip-path`; the render differs from the Chromium render by 0.012% (antialiasing). |
| `shapes/rect/ch-values.svg` | fail | pass | `ch` from the Noto Sans advance of "0" (18.3 px at 32 px). The only difference to the Chromium render is the rounding of Chromium to whole pixels (18 px), which moves the right edge of the rectangle by 7.6 px at 500 px; the reference image has the exact length. |
| `structure/style/non-presentational-attribute.svg` | pass | fail | The SVG 2 geometry properties apply CSS `height` to a `rect`, as Chromium, Firefox, and Safari do; the reference follows the SVG 1.1 rule. Decided by the user: listed `fail specification:` (17.4). |
| `structure/style-attribute/non-presentational-attribute.svg` | pass | fail | The same rule through the `style` attribute; listed `fail specification:` as well. |
| `structure/image/embedded-svg-with-text.svg` | pass | fail | Text in the embedded SVG image draws, as the user decided and as Chromium renders it (0.25% from the Chromium render); the reference image draws none (7,040 mismatched pixels; 4 before). Listed `fail reference:`. |

`masking/clipPath/circle-shorthand-with-stroke-box.svg` and `-with-view-box.svg` still fail, now on the reference image (an opaque white background) instead of a missing feature.

### 17.3 Measured performance

BenchmarkDotNet 0.15.8 on a shared 4-core Intel Xeon 2.30 GHz container, .NET 10.0.15, Ubuntu 24.04.5. Means with the error column (99.9% interval). The short job is 3 warmup and 3 measured iterations in one launch; where more iterations were run, it is stated. The machine is noisy: the same case varies by more than its error column between runs, so alternating rounds of two trees were used to compare them.

Decoder (`DecodeSvg`, a benchmark removed with the decoder in Upstream Sync 1; 5 warmup and 15 measured iterations, default threads, default load options): `Image.Load` with `SvgConfigurationModule` against `SvgDocument.Load` plus `DrawSvg` into a new image of the same size, and `Image.Identify` against `SvgDocument.Load`.

| Document | Long edge | `Image.Load` | Load and `DrawSvg` | `Image.Identify` | `SvgDocument.Load` | Allocated (`Image.Load`) |
|---|---|---|---|---|---|---|
| Tiger | 100 | 3.91 ± 0.34 ms | 4.05 ± 0.22 ms | 1.80 ± 0.15 ms | 1.76 ± 0.11 ms | 3.55 MB |
| | 1000 | 12.6 ± 0.5 ms | 13.0 ± 0.6 ms | 1.74 ± 0.12 ms | 1.71 ± 0.12 ms | 6.88 MB |
| Icons (40) | 100 | 4.31 ± 0.35 ms | 4.56 ± 0.59 ms | 0.56 ± 0.00 ms | 0.66 ± 0.11 ms | 1.83 MB |
| | 1000 | 52.0 ± 3.7 ms | 72.1 ± 16.0 ms | 0.63 ± 0.06 ms | 0.66 ± 0.13 ms | 4.29 MB |
| TextHeavy | 100 | 16.0 ± 0.8 ms | 15.5 ± 1.2 ms | 5.99 ± 0.54 ms | 6.26 ± 0.54 ms | 18.1 MB |
| | 1000 | 26.7 ± 2.0 ms ¹ | 26.1 ± 1.9 ms ¹ | 7.27 ± 0.99 ms | 6.42 ± 0.70 ms | 19.8 MB |
| FilterShadows | 100 | 9.98 ± 0.63 ms | 10.9 ± 0.5 ms | 1.51 ± 0.16 ms | 1.68 ± 0.13 ms | 4.55 MB |
| | 1000 | 60.1 ± 5.3 ms ¹ | 61.9 ± 5.0 ms ¹ | 1.42 ± 0.12 ms | 1.55 ± 0.13 ms | 8.06 MB |
| CssFeatures | 100 | 21.7 ± 1.3 ms | 21.0 ± 1.7 ms | 14.2 ± 0.7 ms | 14.4 ± 1.3 ms | 17.7 MB |
| | 1000 | 47.1 ± 6.2 ms | 49.2 ± 7.9 ms | 13.9 ± 0.7 ms | 14.3 ± 0.8 ms | 20.0 MB |

¹ 10 warmup and 30 measured iterations; the 15-iteration run gave 58.4 ± 15.4 against 21.6 ± 1.2 ms (TextHeavy) and 76.2 ± 15.4 against 64.9 ± 5.9 ms (FilterShadows), which the longer run did not repeat.

The decoder adds no measurable cost: every pair of intervals overlaps except Icons at 1000 pixels, where `Image.Load` is the faster one, and managed allocation is equal within 0.5% (`Identify` allocates 3.5% more than the load for Icons, 31 KB for 40 documents, the metadata and the detection buffers). `Identify` costs as much as a full load (17.7).

CSS features document (`css-features-synthetic.svg`, 48 cards, short job). Svg.Skia and NetVips render it black: neither supports custom properties, so its background and every card are unpainted (67% total difference from the Chromium render for both, against 0.02% for ImageSharp.Drawing at 1000 pixels). Their times are given for completeness and are not comparable.

| Benchmark | Long edge | ImageSharp.Drawing | ImageSharp.Drawing, one thread | ImageSharp.Drawing allocated | Svg.Skia (renders nothing) | NetVips (renders nothing) |
|---|---|---|---|---|---|---|
| Load | — | 14.1 ± 19.1 ms | — | 14.4 MB | 31.8 ± 10.5 ms | — |
| Render | 100 | 4.55 ± 6.03 ms | 7.25 ± 8.95 ms | 2.49 MB | 0.03 ms | — |
| | 1000 | 28.6 ± 89.6 ms | 68.7 ± 159.0 ms | 4.32 MB | 0.41 ms | — |
| Load and render | 100 | 50.7 ± 277.4 ms | 24.4 ± 9.5 ms | 17.7 MB | 28.7 ± 3.0 ms | 4.5 ± 6.7 ms |
| | 1000 | 57.0 ± 31.0 ms | 86.4 ± 45.1 ms | 20.0 MB | 30.8 ± 29.1 ms | 6.6 ± 4.9 ms |

Loading this 56 KB document takes about eight times as long as the Tiger and allocates 14.4 MB: every element substitutes its `var()` declarations and parses them again. It is the document for phase 6 to measure that path.

Regression check against the phase 4 merge (commit 68408c9, built from `git archive` on the same machine):

- `LoadSvg`, 13 documents, alternating rounds of 5 warmup and 15 measured iterations. Against the start of the integration (commit 7da1390), 8 rounds: the mean of the per-round ratios lies within its 95% interval of 1 for every document (widest Styled Landscape +1.2 ± 18.0%, narrowest Tiger +0.5 ± 3.8%), and the ratio pooled over the documents is +0.5% (95% interval −2.8% to +3.9%). Against the final tree, 6 rounds: every document within its interval (Tiger +4.5 ± 5.2%, Text Heavy +8.8 ± 12.5%, Filter Shadows +10.3 ± 15.6%), pooled +0.4% (−2.6% to +3.6%). Loading is therefore not slower by more than about 4% overall; the earlier spread of the means (−5% to +9%) is noise of the machine. Allocation changes by +0.02% to +0.14%, except the icon set, +1.0% (873.5 → 882.2 KB for 40 documents, about 220 bytes per document). Documents without the new features pay nothing measurable.
- `RenderSvg` and `LoadAndRenderSvg`, ImageSharp default and one thread, at 100 and 1000 pixels (104 cases, short job): every interval overlaps; allocation changes by −0.5% to +0.7%. Five alternating rounds of 10 iterations at 1000 pixels for the three filter documents, Tiger, and the icon set gave a pooled ratio of +5.2% (+2.6% to +7.8%), with Filter Blur on one thread +10.4 ± 8.4%; a stage profile of Filter Blur (`--profile svg`, 60 iterations, three alternating rounds) did not repeat it: emit 41.0 to 48.5 ms before against 41.9 to 42.4 ms after, scene render 30.5 to 41.2 ms against 30.6 to 30.9 ms. The only change on the render path is the layer limit pass before a draw of a document with scopes. 4000 pixels were not measured.

### 17.4 Decisions

"Fail" counts the conformance tests that fail because of the rule; "If changed" the change in passing tests if the rule followed the other side.

| Rule | Specification | Chromium renders | This renderer | Fail | If changed |
|---|---|---|---|---|---|
| CSS geometry properties apply to `rect` (decided by the user) | SVG 2 makes `x` … `ry` geometry properties; SVG 1.1 had none | Applies them | Applies them; the two tests whose references follow SVG 1.1 are listed `fail specification:` (`results.csv` records Chrome, Firefox, Safari, and librsvg as failing them) | 2 | +2 with the SVG 1.1 rule, which no browser follows |
| `ex` (decided by the user) | CSS Values 4: the x-height of the first available font | The x-height | Half the font size, the fallback of CSS Values 4: the x-height rule fails three passing suite tests, whose references use 0.5 em | 0 | −3 |
| `ch` | CSS Values 4: the advance of "0" | Rounded to whole pixels (10ch of Open Sans at 20px is 110px, not 114.4px) | The exact advance | 0 | 0 |
| `rec2020` in `color()` | CSS Color 4, the sample code of section 19 (`lin_2020`: a power of 2.4) | Another curve: `color(rec2020 0.5 0.3 0.2)` is (163, 83, 61) there, (144, 56, 32) here | Power 2.4 | 0 | 0 |
| Gradient interpolation of non-legacy colors | SVG 2 and `color-interpolation`: sRGB | Oklab (an `oklch()` gradient differs by up to 24 levels in the middle) | sRGB, for every color | 0 | 0 |
| Division by zero in `calc()` | CSS Values 4: infinity, clamped to the range of the property | The largest width it supports | Drops the declaration (the attribute applies) | 0 | 0 |
| `hsl()` saturation above 100% | CSS Color 4: clamped | As this renderer for the cases checked (`hsl(120 150% 40%)`, `hsl(200 200% 70%)`) | Clamped | 0 | 0 |
| `:lang()` | Selectors 4: lists, quoted ranges, wildcards, extended filtering (`de-DE` matches `de-Latn-DE`) | The Level 3 form only | Selectors 4; both agree on every Level 3 selector | 0 | 0 |
| `pathLength="0"` with a pattern that starts with a zero dash | SVG 2: every distance but 0 becomes infinite, so only the caps of the zero dash draw | A solid stroke | The specification | 0 | 0 |
| `pathLength` with an infinite dash offset | Undefined | Ignores the pattern | As Chromium | 0 | 0 |
| `pathLength` that is not a number | SVG 2: an invalid attribute has no effect | Treated as zero | The specification | 0 | 0 |
| `image-rendering="optimizeSpeed"` | CSS Images 3: as `crisp-edges` | Smooth sampling in `image` | Nearest neighbor | 0 | 0 |
| Default reference box of a basic shape | CSS Masking: `border-box`, the stroke box on SVG elements | The stroke box | The stroke box (the plan named the object bounding box) | 0 | 0 |
| A basic shape with `stroke-box` on a `clipPath` element | CSS Masking: the box of the element the clip applies to | The fill box of the clipped element, also with a stroke | As Chromium | 0 | 0 |
| `light-dark()` | CSS Color 5: by the color scheme | The light color (the oracle renders an SVG document as an image) | The first color | 0 | 0 |
| Math functions in text position lists | CSS Values 4 does not cover SVG number lists | Accepts a function without spaces (`calc(10px)`), not `calc(100px + 10px)` or `max(5px, 10px)` | No math functions in these lists | 0 | 0 |
| Default of `SvgLimits.MaxLayerPixels` (decided by the user) | — | — | 16 × 4096 × 4096 = 268,435,456 pixels, which allows about 1 GiB of layer pixels at the limit with `Rgba32` (4 bytes per pixel); a margin of 6.5 to the largest total of the suite at 4000 px (the tighter option, half of it, would keep a margin of 3.2) | 0 | 0 |
| Output size limit of the decoder (decided by the user; the decoder was removed in Upstream Sync 1, the limit still bounds `SvgDocument.Render` at the intrinsic size) | — | — | Reuses `SvgLimits.MaxImagePixels` (plus 65,535 pixels on a side) instead of a separate public limit: the one limit bounds the embedded images and the rendered output of the decoder and of `Render` at the intrinsic size | 0 | 0 |
| `Identify` (removed with the decoder in Upstream Sync 1) | — | — | Loads the whole document with default `SvgLoadOptions` (the decoder contract passes general options only) | 0 | 0 |
| `font-feature-settings` with a value above 1, `"calt" 0` | CSS Fonts 4 | Applied | The first alternate; `calt` and the other default features stay on (17.6) | 0 | 0 |
| `currentColor` inside a custom property that `color-mix()` uses | CSS Color 5 | Resolved at the element | The declaration is invalid (17.7) | 0 | 0 |
| Text in an SVG document that an `image` element embeds (decided by the user) | SVG 2 does not restrict it | Draws it with the fonts of the page | Draws it with the font settings of the outer load options (`FontCollection`, generic families, default and fallback families); the nested document still reads no file, `@font-face` is skipped as in every document, and its characters count against the text limit of the outer document | 1 (`structure/image/embedded-svg-with-text`, whose reference draws no text, listed `fail reference:`) | +1 |
| The filter rules of 16.4 that follow the specification over Chromium (decided by the user during phase 5) | Filter Effects 1 | Differs (16.4) | Unchanged: they stay | As in 16.4 | As in 16.4 |

### 17.5 Limits

Section 10 lists them: `SvgLimits.MaxLayerPixels`, the decoder bounds (the decoder was removed in Upstream Sync 1; `SvgDocument.Render` keeps the size bounds and `image` elements the gzip bound) (`MaxImagePixels` and 65,535 pixels on a side for sizes the document determines, 64 MiB of inflated gzip data, the 512-byte detection window), and the internal limits of CSS values (32,768 characters per substitution, nesting of 32 for `var()` and for math functions, 256 values and operators per math function, 16 million substituted characters and 65,536 mixed math functions per document, color nesting of 16). Each internal limit makes the value invalid; none throws.

Hostile documents that combine the features (`SvgCrossFeatureHostileDocumentTests`): a doubling chain of custom properties inside `calc()` inside a basic shape stops at the substitution limits, twice the steps cost no more, and the shape is dropped; a document that reaches the substitution budget, the math function limits, the color nesting limit, and the layer limit at once loads and then fails at the layer limit with `InvalidImageContentException` in linear time; and every hostile document of the CSS, SVG 2, and style tests decodes through `SvgDecoder` (and through detection) or throws `InvalidImageContentException`, with the growth that its original test allows. Since Upstream Sync 1 the same documents load with `SvgDocument.Load` and render with `SvgDocument.Render`.

### 17.6 SixLabors.Fonts

Phase 5 adds two needs to section 15.4: a way to turn off the default features that `ShapeRun` applies (`calt` and `clig` among them), so that `font-variant-ligatures: no-contextual` and `"calt" 0` work; and feature values, so that `"salt" 2` and `"swsh" 3` select the second and third alternates. `TextShaper.ShapeRun` takes tags only, each turned on with the value 1.

### 17.7 Known gaps

- `currentColor` inside `color-mix()`, as the origin of a relative color, and inside `light-dark()`: the declaration is invalid; Chromium renders it. It needs the declaration kept unresolved until the computed-value stage, as a `var()` declaration is (pending, then parsed again), with `currentColor` replaced by the computed `color` of the element (of the parent for `color` itself), and, for a child that inherits the value but has another `color`, a paint kind that keeps the function so that the child resolves it with its own color.
- `ch` in the attributes of nested `svg`, `mask`, `clipPath` and `filter` regions, filter primitives, and gradient geometry still uses half the font size (each a `SvgUnitBasis.WithFont` call at the place that converts the length).
- Math functions in text position lists (17.4).
- `shape()` in `clip-path` is not parsed.
- The new colors interpolate in sRGB in gradients (17.4).
- `Identify` loads and resolves the whole document, text included, to read the root element; reading the root attributes alone would make it far cheaper, but percentages and font-relative units of the root size need the root style. (Gone with the decoder in Upstream Sync 1.)
- No synthesized small capitals, superscripts, or subscripts for a font without the feature (Chromium synthesizes them).

### 17.8 Features that stay out

| Feature | Reason |
|---|---|
| Auto-wrapped text (`inline-size`, `shape-inside`) | No browser renders it in SVG (selection rule 2 of the phase 5 plan). |
| Mesh gradients, hatches | No browser renders them. |
| `stroke-linejoin: miter-clip` and `arcs` | No browser renders them; they fall back to `miter`. |
| Markers on `rect`, `circle`, and `ellipse` | SVG 2 allows them; no browser renders them. |
| Text on basic shapes | SVG 2 allows a `textPath` that references a shape; no browser renders it. |
| The deprecated `clip` property | Chromium renders it on no element (`svg` with any `overflow`, `g`, the attribute); the suite test `masking/clip/simple-case` fails in Chromium too. |
| `refX` and `refY` on `symbol` | Chromium applies neither numbers nor keywords. |
| The attributes `font-feature-settings` and `font-variant-*` | Chromium gives the attributes no effect: the properties are CSS only. |
| `white-space` as an attribute | Chromium gives it no effect (only `xml:space` acts); the reader keeps accepting it, as before phase 5. |
| `white-space` values `preserve-spaces`, `discard`, `text-wrap-mode`, `white-space-trim` | Chromium renders no difference in SVG text. |
| `font-variant-east-asian` | Parsed and applied, but not verified: no font of the suite or of the test fonts has the features. |
| Animation, scripting, `foreignObject`, SVG fonts | Non-goals (section 2). |

## 18. Phase 6 Baseline

This section is the measured state at the start of phase 6 (commit a8614c9): the comparison matrix, the peak memory of the processes, the ranked targets, a profile of each target, the answers to the open items of section 1 of `SVG_IMPLEMENTATION_PHASE6.md`, and the patterns of the ImageSharp source that apply. No product code changed for it.

### 18.1 Method

- Machine: a shared 4-core Intel Xeon 2.30 GHz container (AVX-512), .NET 10.0.15, Ubuntu 24.04.5, BenchmarkDotNet 0.15.8, concurrent workstation GC. The machine is noisy: the same case moves by more than its error column between runs.
- Matrix: `LoadSvg`, `RenderSvg` (`SvgSkia`, `ImageSharp`, `ImageSharp_SingleThreaded`), and `LoadAndRenderSvg` (all five methods) for every corpus entry at 100, 1000, and 4000 pixels, short job (3 warmup, 3 measured iterations, 1 launch). The error column is the 99.9% interval of BenchmarkDotNet; with 3 iterations it is wide. A difference counts only where the intervals do not overlap.
- Deciding runs: every cell that could rank a target ran again with 5 warmup and 15 measured iterations (`LoadAndRenderSvg` and `RenderSvg` at 1000 pixels for every entry, `LoadAndRenderSvg` at 4000 pixels for twelve entries and at 100 pixels for four). Only these runs rank targets.
- Comparability: before the timings counted, every renderer's output at 1000 pixels was compared with the Chromium oracle (`--verify svg 1000`, `tests/SvgTools/render-svg.js` with the benchmark font, `compare-png.js` with a pixel threshold of 32). Every renderer draws every document, with one exception: Svg.Skia and NetVips render `CssFeatures` black (67.2% total difference for both, 0.02% for ImageSharp.Drawing), so its cells are not comparable. The three new documents differ from the oracle by 0.02% to 0.43% total difference for every renderer (antialiasing of thin lines and glyphs).
- `RenderSvg` of Svg.Skia replays its recorded picture. For the three filter documents at 100 and 1000 pixels the replay is far cheaper than the first draw, because Skia keeps the results of image filters between draws of the same picture. This was verified after the baseline (section 18.9). The render cells of Svg.Skia for these documents are a cache hit; they are marked not comparable in 18.2, and 18.9 has the comparable numbers. `LoadAndRenderSvg` loads a new picture in every operation and is not affected.
- Peak memory: `--peak <renderer> <entry> <long edge> [single]` (`SvgPeakMemory` in the benchmark project) loads and renders every document of an entry once in a fresh process and prints the high-water mark of the resident set, `VmHWM` of `/proc/self/status`; the script that starts the processes also reads `ru_maxrss` through `wait4` and both agree within 0.6 MB. The baseline of a renderer is the same run with a document of one pixel (the runtime, the renderer library, and its fonts loaded); the net peak is the median of three processes minus that baseline (Paris: one process). The output image is part of the peak (64 MB at 4000 × 4000 pixels).
- Profiles: `--profile svg <iterations> <entry or file> <long edge> [single]` prints the stage times, the allocation per stage, the spread of the iteration times, the collections of each generation, and the GC pause time per iteration. CPU samples come from `perf` (cpu-clock, 999 Hz, frame-pointer call graphs, `DOTNET_PerfMapEnabled=1` and `DOTNET_EnableWriteXorExecute=0` so that JIT frames resolve), about 20 s of work per profile. The EventPipe thread-time sampler of `dotnet-trace` is not used for shares inside hot loops: it suspends threads at GC polls, so it put 30% to 80% of the render samples into a `BulkMoveWithWriteBarrier` call under `WorkerScratch.CreateContext`, which runs once per band of an item and does no loop of its own; `perf` puts less than 2% there. Allocation by type and call site comes from the GC allocation tick events of `dotnet-trace --profile gc-verbose` (one sample per about 100 KB), with stacks.
- Three documents were added to the corpus, because common kinds of SVG were missing: `Chart` (`Svg/Charts/chart-dashboard-synthetic.svg`, a 2 × 2 dashboard of charts as a charting library exports it: grid lines, ticks, about 110 text labels, line series of 365 points, an area, bars, legends), `StreetMap` (`Svg/Maps/street-map-synthetic.svg`, 665 KB, water and parks, 600 buildings, and 1,792 roads drawn as a casing and a fill stroke with round caps and joins: a map with many strokes, where Paris has fills only), and `IconSheet` (`Svg/Icons/icon-sheet-synthetic.svg`, the 40 icons of the corpus as `symbol` elements of one sprite, drawn 400 times through `use` with `currentColor`). The first two are synthetic; the third is derived from the Feather and Bootstrap icons of the corpus (MIT, licenses in `Svg/Icons`).

### 18.2 Matrix

Short job, times in ms. "Allocated" is the managed allocation of ImageSharp.Drawing per operation, an absolute value; the managed allocation of the native renderers is not comparable and is not shown (section 2 of the phase 6 plan). "Faster reference" names a reference renderer that is faster than the faster ImageSharp.Drawing column with separated intervals, and, separately, one that is faster than the one-thread column.

Load (`LoadSvg`, no size):

| Document | ImageSharp.Drawing | Allocated | Svg.Skia |
|---|---|---|---|
| Tiger | 1.59 ± 0.137 | 1.9 MB | 5.76 ± 3.55 |
| Paris30k | 593 ± 397 | 264.2 MB | 2428 ± 2067 |
| Icons | 0.559 ± 0.074 | 882 KB | 3.99 ± 1.47 |
| SoftIceCream | 0.296 ± 0.036 | 268 KB | 1.65 ± 4.91 |
| Moose | 1.17 ± 0.685 | 999 KB | 5.26 ± 2.00 |
| StyledLandscape | 0.162 ± 0.193 | 212 KB | 1.72 ± 5.17 |
| Dashboard | 1.22 ± 4.05 | 1.4 MB | 5.29 ± 3.43 |
| PatternChart | 0.488 ± 0.521 | 530 KB | 7.87 ± 12.7 |
| TextHeavy | 4.34 ± 2.55 | 4.8 MB | 114 ± 462 |
| TextDecorations | 3.98 ± 2.52 | 4.0 MB | 147 ± 433 |
| FilterShadows | 2.10 ± 4.65 | 1.8 MB | 18.7 ± 7.68 |
| FilterBlur | 0.147 ± 0.097 | 208 KB | 1.86 ± 9.75 |
| FilterChain | 0.142 ± 0.088 | 201 KB | 1.40 ± 1.93 |
| CssFeatures | 13.4 ± 8.48 | 14.4 MB | 31.0 ± 12.9 |
| Chart | 2.16 ± 3.98 | 2.4 MB | 31.4 ± 87.1 |
| StreetMap | 19.1 ± 28.0 | 10.2 MB | 75.6 ± 40.8 |
| IconSheet | 1.88 ± 1.40 | 1.6 MB | 35.2 ± 12.1 |

Render of a loaded document (`RenderSvg`; Svg.Skia replays its picture, NetVips cannot render a loaded document):

| Document | Long edge | ImageSharp.Drawing | One thread | Allocated | Svg.Skia (replay) | Faster reference |
|---|---|---|---|---|---|---|
| Tiger | 100 | 1.99 ± 0.250 | 3.25 ± 9.70 | 569 KB | 1.00 ± 0.933 |  |
| Tiger | 1000 | 9.70 ± 3.16 | 24.8 ± 14.0 | 1.9 MB | 6.62 ± 0.238 | one thread: Svg.Skia faster ×3.8 |
| Tiger | 4000 | 64.4 ± 39.4 | 241 ± 976 | 5.3 MB | 44.9 ± 16.1 |  |
| Paris30k | 100 | 160 ± 524 | 211 ± 92.5 | 71.2 MB | 73.5 ± 11.1 | one thread: Svg.Skia faster ×2.9 |
| Paris30k | 1000 | 235 ± 255 | 418 ± 1383 | 84.6 MB | 2308 ± 4917 |  |
| Paris30k | 4000 | 461 ± 372 | 1346 ± 953 | 129.1 MB | 50917 ± 28289 |  |
| Icons | 100 | 2.66 ± 2.23 | 4.53 ± 0.904 | 523 KB | 1.56 ± 2.13 |  |
| Icons | 1000 | 53.2 ± 26.7 | 164 ± 291 | 3.3 MB | 17.1 ± 4.54 | Svg.Skia faster ×3.1 |
| Icons | 4000 | 645 ± 245 | 2279 ± 2316 | 12.7 MB | 254 ± 178 |  |
| SoftIceCream | 100 | 0.284 ± 0.379 | 0.489 ± 1.17 | 71 KB | 0.159 ± 0.025 |  |
| SoftIceCream | 1000 | 11.6 ± 5.74 | 22.6 ± 2.46 | 299 KB | 6.19 ± 2.16 | one thread: Svg.Skia faster ×3.7 |
| SoftIceCream | 4000 | 105 ± 43.8 | 356 ± 360 | 1.3 MB | 102 ± 12.1 |  |
| Moose | 100 | 1.36 ± 0.605 | 3.49 ± 0.239 | 268 KB | 1.09 ± 0.017 | one thread: Svg.Skia faster ×3.2 |
| Moose | 1000 | 48.5 ± 38.0 | 167 ± 14.3 | 1.2 MB | 51.0 ± 0.652 | one thread: Svg.Skia faster ×3.3 |
| Moose | 4000 | 520 ± 470 | 1574 ± 1700 | 4.1 MB | 542 ± 48.1 |  |
| StyledLandscape | 100 | 0.245 ± 0.204 | 0.368 ± 0.193 | 100 KB | 0.073 ± 0.066 | one thread: Svg.Skia faster ×5.1 |
| StyledLandscape | 1000 | 6.76 ± 10.00 | 17.8 ± 23.7 | 306 KB | 3.98 ± 5.73 |  |
| StyledLandscape | 4000 | 78.0 ± 43.2 | 273 ± 1010 | 1.2 MB | 92.2 ± 51.0 |  |
| Dashboard | 100 | 5.52 ± 2.63 | 4.71 ± 11.4 | 1.9 MB | 0.590 ± 0.212 |  |
| Dashboard | 1000 | 53.1 ± 115 | 65.8 ± 181 | 4.5 MB | 9.32 ± 0.508 |  |
| Dashboard | 4000 | 353 ± 227 | 715 ± 61.5 | 11.6 MB | 182 ± 620 |  |
| PatternChart | 100 | 1.82 ± 0.771 | 1.98 ± 0.737 | 1.0 MB | 0.546 ± 0.164 | Svg.Skia faster ×3.3, one thread: Svg.Skia faster ×3.6 |
| PatternChart | 1000 | 15.7 ± 11.3 | 33.0 ± 2.16 | 2.0 MB | 25.0 ± 26.5 |  |
| PatternChart | 4000 | 185 ± 144 | 406 ± 29.6 | 5.4 MB | 546 ± 1498 |  |
| TextHeavy | 100 | 8.67 ± 5.06 | 11.6 ± 2.26 | 6.3 MB | 0.501 ± 1.67 | Svg.Skia faster ×17.3, one thread: Svg.Skia faster ×23.2 |
| TextHeavy | 1000 | 14.7 ± 7.55 | 26.7 ± 2.81 | 8.1 MB | 0.961 ± 0.971 | Svg.Skia faster ×15.3, one thread: Svg.Skia faster ×27.7 |
| TextHeavy | 4000 | 57.5 ± 59.1 | 178 ± 289 | 12.6 MB | 10.5 ± 5.46 |  |
| TextDecorations | 100 | 6.68 ± 5.09 | 14.1 ± 1.54 | 6.8 MB | 1.01 ± 1.72 | one thread: Svg.Skia faster ×14.0 |
| TextDecorations | 1000 | 12.9 ± 9.32 | 27.6 ± 28.7 | 7.4 MB | 1.61 ± 3.68 |  |
| TextDecorations | 4000 | 54.6 ± 26.0 | 181 ± 200 | 11.9 MB | 19.8 ± 25.0 |  |
| FilterShadows | 100 | 5.62 ± 13.6 | 3.27 ± 7.27 | 2.3 MB | 0.453 ± 1.02 | not comparable (replay ≪ first draw) |
| FilterShadows | 1000 | 59.8 ± 70.5 | 63.3 ± 5.38 | 5.3 MB | 2.07 ± 2.14 | not comparable (replay ≪ first draw) |
| FilterShadows | 4000 | 495 ± 263 | 823 ± 2459 | 40.2 MB | 397 ± 576 |  |
| FilterBlur | 100 | 1.28 ± 0.953 | 1.08 ± 0.032 | 161 KB | 0.125 ± 0.397 | not comparable (replay ≪ first draw) |
| FilterBlur | 1000 | 28.1 ± 21.4 | 74.2 ± 25.5 | 3.2 MB | 7.81 ± 1.99 | not comparable (replay ≪ first draw) |
| FilterBlur | 4000 | 544 ± 459 | 1372 ± 411 | 47.5 MB | 223 ± 146 | one thread: Svg.Skia faster ×6.2 |
| FilterChain | 100 | 1.25 ± 3.15 | 1.23 ± 0.125 | 176 KB | 0.064 ± 0.016 | not comparable (replay ≪ first draw) |
| FilterChain | 1000 | 38.4 ± 78.6 | 69.0 ± 15.0 | 3.0 MB | 2.52 ± 3.85 | not comparable (replay ≪ first draw) |
| FilterChain | 4000 | 526 ± 544 | 1375 ± 2210 | 41.4 MB | 1232 ± 1049 |  |
| CssFeatures | 100 | 4.48 ± 6.48 | 7.04 ± 6.11 | 2.5 MB | 0.034 ± 0.037 | not comparable |
| CssFeatures | 1000 | 25.8 ± 35.3 | 58.8 ± 30.0 | 4.3 MB | 0.309 ± 0.086 | not comparable |
| CssFeatures | 4000 | 134 ± 136 | 358 ± 32.9 | 11.8 MB | 4.76 ± 10.3 | not comparable |
| Chart | 100 | 2.03 ± 3.15 | 4.15 ± 1.60 | 1.6 MB | 0.383 ± 0.626 | one thread: Svg.Skia faster ×10.8 |
| Chart | 1000 | 8.27 ± 3.81 | 16.0 ± 14.5 | 2.4 MB | 3.56 ± 0.151 | Svg.Skia faster ×2.3 |
| Chart | 4000 | 62.7 ± 60.4 | 188 ± 743 | 5.2 MB | 19.2 ± 41.5 |  |
| StreetMap | 100 | 17.7 ± 8.76 | 30.3 ± 110 | 8.6 MB | 3.41 ± 8.50 |  |
| StreetMap | 1000 | 49.1 ± 80.0 | 118 ± 35.1 | 16.7 MB | 59.6 ± 1.11 | one thread: Svg.Skia faster ×2.0 |
| StreetMap | 4000 | 237 ± 173 | 652 ± 1743 | 48.8 MB | 370 ± 110 |  |
| IconSheet | 100 | 3.29 ± 2.79 | 5.09 ± 3.63 | 2.4 MB | 1.37 ± 4.91 |  |
| IconSheet | 1000 | 9.34 ± 8.97 | 21.3 ± 12.1 | 2.9 MB | 6.45 ± 1.59 | one thread: Svg.Skia faster ×3.3 |
| IconSheet | 4000 | 51.1 ± 17.4 | 153 ± 116 | 5.3 MB | 42.6 ± 50.0 |  |

Load and render (`LoadAndRenderSvg`, a new output per document inside the measured time):

| Document | Long edge | ImageSharp.Drawing | One thread | Allocated | Svg.Skia | NetVips | NetVips, one thread | Faster reference |
|---|---|---|---|---|---|---|---|---|
| Tiger | 100 | 4.34 ± 3.16 | 5.00 ± 0.450 | 3.6 MB | 6.06 ± 0.102 | 6.32 ± 2.06 | 6.59 ± 2.34 |  |
| Tiger | 1000 | 13.1 ± 13.1 | 24.9 ± 10.9 | 6.9 MB | 12.2 ± 11.4 | 28.8 ± 25.4 | 27.2 ± 13.0 |  |
| Tiger | 4000 | 80.6 ± 190 | 191 ± 271 | 13.0 MB | 69.4 ± 140 | 164 ± 80.0 | 172 ± 97.1 |  |
| Paris30k | 100 | 873 ± 1499 | 910 ± 1054 | 392.1 MB | 2232 ± 1345 | 1278 ± 2438 | 1123 ± 493 |  |
| Paris30k | 1000 | 1007 ± 1430 | 1170 ± 2319 | 428.7 MB | 4718 ± 3266 | 2366 ± 5198 | 2355 ± 1926 |  |
| Paris30k | 4000 | 1443 ± 782 | 2445 ± 5875 | 509.4 MB | 87473 ± 56106 | 36744 ± 4773 | 36587 ± 11910 |  |
| Icons | 100 | 4.43 ± 9.33 | 4.72 ± 4.04 | 1.8 MB | 5.56 ± 0.634 | 20.3 ± 13.3 | 20.2 ± 46.4 |  |
| Icons | 1000 | 80.1 ± 52.2 | 72.1 ± 2.61 | 4.3 MB | 24.0 ± 9.25 | 153 ± 36.7 | 128 ± 103 | Svg.Skia faster ×3.0 |
| Icons | 4000 | 614 ± 980 | 1403 ± 2705 | 18.2 MB | 995 ± 533 | 2807 ± 1395 | 3464 ± 628 |  |
| SoftIceCream | 100 | 0.771 ± 0.623 | 1.13 ± 3.03 | 424 KB | 2.11 ± 4.84 | 2.28 ± 5.00 | 2.43 ± 2.16 |  |
| SoftIceCream | 1000 | 9.81 ± 5.62 | 22.0 ± 18.4 | 753 KB | 9.59 ± 7.09 | 31.7 ± 13.6 | 31.1 ± 11.1 |  |
| SoftIceCream | 4000 | 106 ± 212 | 332 ± 652 | 1.8 MB | 160 ± 512 | 521 ± 351 | 515 ± 223 |  |
| Moose | 100 | 2.87 ± 3.90 | 4.49 ± 5.88 | 1.8 MB | 5.31 ± 3.22 | 8.55 ± 4.90 | 8.34 ± 16.3 |  |
| Moose | 1000 | 38.0 ± 84.4 | 106 ± 84.9 | 3.6 MB | 42.3 ± 4.34 | 198 ± 377 | 194 ± 19.6 |  |
| Moose | 4000 | 466 ± 140 | 1474 ± 410 | 7.8 MB | 606 ± 367 | 2399 ± 610 | 2426 ± 1340 | one thread: Svg.Skia faster ×2.4 |
| StyledLandscape | 100 | 0.726 ± 0.106 | 0.821 ± 2.26 | 352 KB | 1.43 ± 1.87 | 1.35 ± 2.35 | 1.32 ± 0.926 |  |
| StyledLandscape | 1000 | 7.15 ± 10.8 | 16.3 ± 11.0 | 606 KB | 6.41 ± 6.90 | 9.69 ± 6.20 | 8.53 ± 7.29 |  |
| StyledLandscape | 4000 | 73.9 ± 52.6 | 225 ± 87.6 | 1.5 MB | 146 ± 339 | 283 ± 134 | 282 ± 65.2 |  |
| Dashboard | 100 | 7.12 ± 6.46 | 5.48 ± 0.518 | 3.5 MB | 6.28 ± 9.76 | 13.7 ± 3.97 | 13.2 ± 3.63 |  |
| Dashboard | 1000 | 109 ± 276 | 65.7 ± 74.7 | 6.6 MB | 14.2 ± 18.9 | 979 ± 2037 | 1126 ± 2546 |  |
| Dashboard | 4000 | 372 ± 110 | 712 ± 333 | 14.4 MB | 466 ± 3758 | 64014 ± 37210 | 59793 ± 8282 |  |
| PatternChart | 100 | 2.85 ± 6.25 | 3.23 ± 4.00 | 1.6 MB | 14.3 ± 22.7 | 6.85 ± 7.34 | 7.52 ± 10.0 |  |
| PatternChart | 1000 | 16.2 ± 5.87 | 47.1 ± 12.1 | 2.6 MB | 62.3 ± 221 | 184 ± 96.4 | 200 ± 519 |  |
| PatternChart | 4000 | 215 ± 559 | 393 ± 85.6 | 6.1 MB | 667 ± 2252 | 3868 ± 2995 | 3860 ± 1342 |  |
| TextHeavy | 100 | 11.6 ± 20.5 | 15.8 ± 19.6 | 11.4 MB | 106 ± 414 | 53.4 ± 5.78 | 56.3 ± 39.9 |  |
| TextHeavy | 1000 | 73.1 ± 808 | 44.5 ± 4.10 | 13.0 MB | 89.0 ± 284 | 89.4 ± 77.4 | 84.1 ± 80.7 |  |
| TextHeavy | 4000 | 144 ± 165 | 159 ± 877 | 18.0 MB | 109 ± 100 | 417 ± 134 | 383 ± 175 |  |
| TextDecorations | 100 | 10.9 ± 10.3 | 15.3 ± 13.6 | 11.2 MB | 140 ± 116 | 35.0 ± 15.3 | 37.4 ± 67.6 |  |
| TextDecorations | 1000 | 61.7 ± 74.6 | 42.3 ± 17.8 | 11.8 MB | 187 ± 1229 | 61.4 ± 56.5 | 56.8 ± 15.5 |  |
| TextDecorations | 4000 | 186 ± 1977 | 129 ± 39.7 | 16.6 MB | 168 ± 28.6 | 279 ± 172 | 315 ± 405 |  |
| FilterShadows | 100 | 10.5 ± 3.26 | 5.25 ± 7.75 | 4.6 MB | 25.6 ± 20.0 | 10.1 ± 4.78 | 10.8 ± 11.4 |  |
| FilterShadows | 1000 | 90.7 ± 215 | 77.2 ± 405 | 8.0 MB | 51.1 ± 166 | 1048 ± 1576 | 994 ± 877 |  |
| FilterShadows | 4000 | 341 ± 273 | 693 ± 195 | 43.5 MB | 442 ± 1198 | 19163 ± 5803 | 19214 ± 1054 |  |
| FilterBlur | 100 | 1.52 ± 3.25 | 1.31 ± 0.689 | 390 KB | 1.99 ± 6.29 | 3.75 ± 2.12 | 3.76 ± 4.21 |  |
| FilterBlur | 1000 | 36.0 ± 48.6 | 114 ± 690 | 3.4 MB | 32.1 ± 12.8 | 139 ± 50.4 | 138 ± 43.3 |  |
| FilterBlur | 4000 | 608 ± 370 | 1319 ± 1919 | 47.4 MB | 685 ± 529 | 8656 ± 3596 | 8608 ± 792 |  |
| FilterChain | 100 | 2.60 ± 5.46 | 1.63 ± 1.58 | 409 KB | 1.98 ± 1.88 | 4.17 ± 1.90 | 4.21 ± 5.00 |  |
| FilterChain | 1000 | 49.1 ± 169 | 76.6 ± 106 | 3.2 MB | 84.6 ± 175 | 424 ± 312 | 432 ± 565 |  |
| FilterChain | 4000 | 506 ± 140 | 1098 ± 151 | 41.7 MB | 1072 ± 288 | 46006 ± 12388 | 44072 ± 13565 |  |
| CssFeatures | 100 | 54.8 ± 123 | 35.2 ± 244 | 17.7 MB | 34.7 ± 57.6 | 4.14 ± 6.20 | 4.26 ± 6.53 | not comparable |
| CssFeatures | 1000 | 75.4 ± 224 | 79.7 ± 91.9 | 20.1 MB | 29.8 ± 20.6 | 6.91 ± 6.80 | 6.82 ± 14.8 | not comparable |
| CssFeatures | 4000 | 217 ± 244 | 334 ± 142 | 28.3 MB | 49.4 ± 29.6 | 54.8 ± 17.1 | 65.1 ± 140 | not comparable |
| Chart | 100 | 4.82 ± 0.679 | 5.17 ± 2.84 | 4.3 MB | 39.1 ± 78.3 | 13.8 ± 4.99 | 15.2 ± 16.3 |  |
| Chart | 1000 | 9.75 ± 5.97 | 17.3 ± 21.7 | 5.3 MB | 36.6 ± 62.2 | 26.1 ± 7.87 | 27.2 ± 21.2 |  |
| Chart | 4000 | 56.1 ± 44.1 | 144 ± 45.8 | 8.4 MB | 53.8 ± 18.6 | 139 ± 55.5 | 143 ± 108 | one thread: Svg.Skia faster ×2.7 |
| StreetMap | 100 | 40.8 ± 58.9 | 107 ± 219 | 20.0 MB | 125 ± 335 | 85.4 ± 170 | 95.6 ± 59.1 |  |
| StreetMap | 1000 | 69.6 ± 133 | 146 ± 399 | 28.1 MB | 239 ± 414 | 207 ± 157 | 195 ± 117 |  |
| StreetMap | 4000 | 205 ± 125 | 536 ± 142 | 60.1 MB | 388 ± 279 | 630 ± 646 | 670 ± 332 |  |
| IconSheet | 100 | 5.39 ± 5.72 | 6.92 ± 2.04 | 4.0 MB | 47.2 ± 57.2 | 21.0 ± 0.687 | 15.4 ± 14.7 |  |
| IconSheet | 1000 | 11.3 ± 12.5 | 27.7 ± 46.2 | 4.7 MB | 43.1 ± 79.5 | 35.7 ± 24.4 | 32.7 ± 9.24 |  |
| IconSheet | 4000 | 46.4 ± 14.4 | 119 ± 10.6 | 7.1 MB | 97.1 ± 32.7 | 167 ± 119 | 193 ± 137 |  |

Deciding runs (5 warmup, 15 measured iterations), load and render:

| Document | Long edge | ImageSharp.Drawing | One thread | Allocated | Svg.Skia | NetVips | NetVips, one thread | Faster reference |
|---|---|---|---|---|---|---|---|---|
| Tiger | 1000 | 14.0 ± 0.892 | 24.8 ± 1.27 | 6.9 MB | 12.5 ± 0.530 | — | — | Svg.Skia faster ×1.1, one thread: Svg.Skia faster ×2.0 |
| Tiger | 4000 | 60.1 ± 1.23 | 166 ± 7.36 | 13.0 MB | 67.7 ± 9.75 | — | — | one thread: Svg.Skia faster ×2.5 |
| Paris30k | 1000 | 844 ± 35.7 | 1033 ± 51.6 | 428.7 MB | 4093 ± 262 | — | — |  |
| Icons | 100 | 4.20 ± 0.440 | 4.40 ± 0.238 | 1.8 MB | 5.76 ± 0.237 | — | — |  |
| Icons | 1000 | 69.4 ± 6.39 | 73.6 ± 2.08 | 4.3 MB | 19.4 ± 2.02 | — | — | Svg.Skia faster ×3.6, one thread: Svg.Skia faster ×3.8 |
| Icons | 4000 | 619 ± 38.4 | 1233 ± 61.0 | 18.2 MB | 858 ± 19.2 | — | — | one thread: Svg.Skia faster ×1.4 |
| SoftIceCream | 1000 | 8.81 ± 0.278 | 21.1 ± 0.697 | 753 KB | 9.29 ± 0.283 | — | — | one thread: Svg.Skia faster ×2.3 |
| SoftIceCream | 4000 | 102 ± 4.45 | 306 ± 7.62 | 1.8 MB | 134 ± 10.4 | — | — | one thread: Svg.Skia faster ×2.3 |
| Moose | 1000 | 36.2 ± 1.61 | 98.8 ± 0.491 | 3.6 MB | 42.0 ± 0.093 | — | — | one thread: Svg.Skia faster ×2.4 |
| Moose | 4000 | 435 ± 19.8 | 1535 ± 48.7 | 7.8 MB | 576 ± 22.8 | — | — | one thread: Svg.Skia faster ×2.7 |
| StyledLandscape | 1000 | 6.37 ± 0.281 | 15.2 ± 0.617 | 606 KB | 4.65 ± 0.183 | — | — | Svg.Skia faster ×1.4, one thread: Svg.Skia faster ×3.3 |
| StyledLandscape | 4000 | 74.8 ± 4.56 | 234 ± 14.1 | 1.5 MB | 135 ± 24.1 | — | — | one thread: Svg.Skia faster ×1.7 |
| Dashboard | 100 | 6.55 ± 0.523 | 5.61 ± 0.229 | 3.5 MB | 5.78 ± 0.201 | — | — |  |
| Dashboard | 1000 | 87.8 ± 16.5 | 59.5 ± 1.92 | 6.7 MB | 13.8 ± 0.419 | — | — | Svg.Skia faster ×4.3 |
| Dashboard | 4000 | 325 ± 23.1 | 702 ± 25.7 | 14.4 MB | 206 ± 2.39 | — | — | Svg.Skia faster ×1.6, one thread: Svg.Skia faster ×3.4 |
| PatternChart | 1000 | 15.3 ± 0.319 | 33.4 ± 1.68 | 2.6 MB | 41.9 ± 10.8 | — | — |  |
| TextHeavy | 100 | 10.5 ± 0.320 | 13.9 ± 0.351 | 11.4 MB | 82.4 ± 5.23 | 55.8 ± 3.35 | 53.9 ± 2.29 |  |
| TextHeavy | 1000 | 18.7 ± 1.36 | 29.7 ± 1.02 | 13.0 MB | 84.9 ± 9.87 | 88.4 ± 5.73 | 85.0 ± 5.97 |  |
| TextHeavy | 4000 | 54.5 ± 3.65 | 123 ± 1.24 | 18.0 MB | 99.1 ± 0.965 | — | — | one thread: Svg.Skia faster ×1.2 |
| TextDecorations | 100 | 11.3 ± 0.863 | 13.8 ± 0.713 | 11.1 MB | 131 ± 12.1 | 32.8 ± 1.48 | 33.7 ± 2.95 |  |
| TextDecorations | 1000 | 19.2 ± 1.97 | 33.8 ± 5.87 | 11.8 MB | 137 ± 15.1 | 60.2 ± 4.73 | 58.9 ± 3.36 |  |
| FilterShadows | 100 | 10.9 ± 1.19 | 5.01 ± 0.138 | 4.6 MB | 25.0 ± 1.90 | 9.72 ± 0.616 | 9.65 ± 0.356 |  |
| FilterShadows | 1000 | 85.3 ± 16.0 | 65.6 ± 5.62 | 8.0 MB | 41.3 ± 1.79 | 978 ± 90.8 | 877 ± 67.8 | Svg.Skia faster ×1.6 |
| FilterShadows | 4000 | 460 ± 41.1 | 681 ± 4.40 | 43.5 MB | 438 ± 15.5 | — | — | one thread: Svg.Skia faster ×1.6 |
| FilterBlur | 1000 | 30.8 ± 1.73 | 72.9 ± 0.512 | 3.4 MB | 26.2 ± 1.15 | — | — | Svg.Skia faster ×1.2, one thread: Svg.Skia faster ×2.8 |
| FilterBlur | 4000 | 513 ± 21.0 | 1250 ± 37.0 | 47.4 MB | 663 ± 21.1 | — | — | one thread: Svg.Skia faster ×1.9 |
| FilterChain | 100 | 1.69 ± 0.187 | 1.24 ± 0.088 | 408 KB | 2.19 ± 0.167 | — | — |  |
| FilterChain | 1000 | 44.0 ± 5.96 | 68.2 ± 0.389 | 3.2 MB | 68.2 ± 6.36 | — | — |  |
| Chart | 1000 | 9.21 ± 0.682 | 16.0 ± 0.173 | 5.3 MB | 28.9 ± 0.991 | — | — |  |
| Chart | 4000 | 40.7 ± 1.46 | 118 ± 2.16 | 8.4 MB | 46.9 ± 1.58 | — | — | one thread: Svg.Skia faster ×2.5 |
| StreetMap | 1000 | 62.2 ± 5.44 | 120 ± 1.26 | 28.1 MB | 114 ± 2.23 | — | — | one thread: Svg.Skia faster ×1.0 |
| StreetMap | 4000 | 215 ± 14.0 | 578 ± 38.4 | 60.1 MB | 424 ± 13.9 | — | — | one thread: Svg.Skia faster ×1.4 |
| IconSheet | 1000 | 11.2 ± 0.385 | 20.5 ± 0.415 | 4.7 MB | 35.1 ± 1.42 | — | — |  |
| IconSheet | 4000 | 45.9 ± 3.19 | 122 ± 7.60 | 7.1 MB | 82.4 ± 6.14 | — | — | one thread: Svg.Skia faster ×1.5 |

FilterShadows at 1000 pixels ran twice with the default threads in this job: 48.5 ± 3.0 ms and 85.3 ± 16.0 ms (the table shows the second); the one-thread runs gave 61.5 ± 1.7 and 65.6 ± 5.6 ms. The default-thread configuration of this document varies between runs by more than its intervals.

Deciding runs, render of a loaded document at 1000 pixels:

| Document | Long edge | ImageSharp.Drawing | One thread | Allocated | Svg.Skia (replay) | Faster reference |
|---|---|---|---|---|---|---|
| Tiger | 1000 | 10.4 ± 0.785 | 22.8 ± 0.132 | 1.9 MB | 6.91 ± 0.223 | Svg.Skia faster ×1.5, one thread: Svg.Skia faster ×3.3 |
| Paris30k | 1000 | 229 ± 16.7 | 365 ± 65.2 | 84.6 MB | 1985 ± 99.8 |  |
| Icons | 1000 | 51.6 ± 4.62 | 150 ± 3.20 | 3.3 MB | 14.2 ± 2.02 | Svg.Skia faster ×3.6, one thread: Svg.Skia faster ×10.6 |
| SoftIceCream | 1000 | 8.59 ± 0.369 | 22.7 ± 0.251 | 299 KB | 6.09 ± 0.284 | Svg.Skia faster ×1.4, one thread: Svg.Skia faster ×3.7 |
| Moose | 1000 | 37.5 ± 1.41 | 103 ± 5.17 | 1.2 MB | 39.6 ± 1.53 | one thread: Svg.Skia faster ×2.6 |
| StyledLandscape | 1000 | 5.99 ± 0.204 | 16.4 ± 0.371 | 306 KB | 3.55 ± 0.040 | Svg.Skia faster ×1.7, one thread: Svg.Skia faster ×4.6 |
| Dashboard | 1000 | 55.2 ± 8.43 | 59.9 ± 1.99 | 4.5 MB | 9.92 ± 0.513 | Svg.Skia faster ×5.6, one thread: Svg.Skia faster ×6.0 |
| PatternChart | 1000 | 14.9 ± 0.634 | 34.4 ± 1.25 | 2.0 MB | 25.0 ± 1.58 | one thread: Svg.Skia faster ×1.4 |
| TextHeavy | 1000 | 14.8 ± 0.653 | 28.6 ± 1.19 | 8.1 MB | 1.01 ± 0.041 | Svg.Skia faster ×14.6, one thread: Svg.Skia faster ×28.3 |
| TextDecorations | 1000 | 11.9 ± 0.715 | 26.0 ± 0.107 | 7.4 MB | 1.40 ± 0.025 | Svg.Skia faster ×8.5, one thread: Svg.Skia faster ×18.6 |
| FilterShadows | 1000 | 53.1 ± 9.24 | 59.9 ± 0.461 | 5.3 MB | 1.92 ± 0.043 | not comparable (replay ≪ first draw) |
| FilterBlur | 1000 | 31.9 ± 2.93 | 80.1 ± 1.29 | 3.2 MB | 9.51 ± 0.333 | not comparable (replay ≪ first draw) |
| FilterChain | 1000 | 33.3 ± 1.07 | 69.2 ± 1.28 | 3.0 MB | 2.52 ± 0.203 | not comparable (replay ≪ first draw) |
| Chart | 1000 | 7.12 ± 0.473 | 15.6 ± 0.354 | 2.4 MB | 3.59 ± 0.017 | Svg.Skia faster ×2.0, one thread: Svg.Skia faster ×4.3 |
| StreetMap | 1000 | 46.0 ± 3.24 | 112 ± 0.877 | 16.7 MB | 63.9 ± 2.24 | one thread: Svg.Skia faster ×1.7 |
| IconSheet | 1000 | 8.53 ± 0.392 | 19.7 ± 0.166 | 2.9 MB | 6.36 ± 0.046 | Svg.Skia faster ×1.3, one thread: Svg.Skia faster ×3.1 |

Loading is faster than Svg.Skia for every document (2.3 to 37 times by the means); the CSS features document is the slowest load per byte (13.4 ms and 14.4 MB for 56 KB). For load and render, NetVips is not faster with separated intervals in any comparable cell (the closest is FilterShadows at 100 pixels: NetVips 9.72 ± 0.62 ms against 10.9 ± 1.2 ms with the default threads, overlapping, and 5.01 ± 0.14 ms with one thread); where Svg.Skia is faster, section 18.4 lists it. Rendering a loaded document is slower than the Svg.Skia replay almost everywhere, because every `DrawSvg` emits the commands and builds the scene again (section 18.4, rank 5).

### 18.3 Peak Memory

Net peak resident set in MB of one load and render in a fresh process (median of three processes minus the baseline of the same configuration; Paris one process). "Largest" is the configuration with the largest net peak.

| Document | Long edge | ImageSharp.Drawing | One thread | Svg.Skia | NetVips | NetVips, one thread | Largest |
|---|---|---|---|---|---|---|---|
| Paris30k | 1000 | 457 | 456 | 483 | 223 | 223 | Svg.Skia |
| StreetMap | 1000 | 35 | 37 | 61 | 11 | 11 | Svg.Skia |
| FilterBlur | 1000 | 22 | 20 | 32 | 18 | 18 | Svg.Skia |
| FilterChain | 1000 | 22 | 17 | 34 | 19 | 19 | Svg.Skia |
| FilterShadows | 1000 | 14 | 12 | 49 | 19 | 19 | Svg.Skia |
| Dashboard | 1000 | 13 | 13 | 17 | 12 | 12 | Svg.Skia |
| PatternChart | 1000 | 10 | 10 | 26 | 6 | 6 | Svg.Skia |
| Moose | 1000 | 8 | 8 | 17 | 3 | 3 | Svg.Skia |
| TextHeavy | 1000 | 19 | 19 | 45 | 3 | 3 | Svg.Skia |
| Icons | 1000 | 10 | 9 | 19 | 25 | 2 | NetVips |
| Tiger | 1000 | 11 | 11 | 17 | 2 | 2 | Svg.Skia |
| CssFeatures | 1000 | 27 | 35 | 25 | 1 | 1 | ImageSharp.Drawing, one thread (not comparable) |
| Paris30k | 4000 | 591 | 589 | 537 | 340 | 333 | ImageSharp.Drawing |
| StreetMap | 4000 | 130 | 129 | 118 | 126 | 126 | ImageSharp.Drawing |
| FilterBlur | 4000 | 238 | 240 | 253 | 348 | 347 | NetVips |
| FilterChain | 4000 | 146 | 145 | 217 | 210 | 209 | Svg.Skia |
| FilterShadows | 4000 | 98 | 100 | 198 | 126 | 126 | Svg.Skia |
| Dashboard | 4000 | 84 | 76 | 63 | 213 | 213 | NetVips |
| PatternChart | 4000 | 66 | 65 | 110 | 150 | 127 | NetVips |
| Moose | 4000 | 79 | 79 | 63 | 117 | 117 | NetVips |
| TextHeavy | 4000 | 70 | 75 | 92 | 95 | 95 | NetVips |
| Icons | 4000 | 90 | 86 | 70 | 274 | 179 | NetVips |
| Tiger | 4000 | 80 | 79 | 67 | 117 | 117 | NetVips |
| CssFeatures | 4000 | 78 | 75 | 68 | 87 | 87 | NetVips (not comparable) |

Baselines (MB): none default 42.0, imagesharp default 62.3, imagesharp single 62.4, svgskia default 59.8, netvips default 54.5, netvips single 53.9

ImageSharp.Drawing has the largest peak only for Paris at 4000 pixels (591 MB against 537 MB for Svg.Skia and 340 MB for NetVips) and StreetMap at 4000 pixels (130 against 118 and 126 MB; a margin of 4 MB to NetVips). CssFeatures is not comparable (the others draw nothing). At 1000 pixels Svg.Skia has the largest peak for every comparable document but Icons. Paris loads 14 MB of path data into 264 MB of managed allocation, and a load and render allocates 429 to 509 MB (section 18.4, rank 6).

### 18.4 Ranked Targets

Threshold for managed allocation: an operation that allocates more than 10 MB, or a fresh render that allocates more than the bytes of its output image (4 bytes per pixel), or steady-state allocation that triggers full (generation 2) collections. No allocation grows with the number of draws of a loaded document: every render allocates the same amount.

Gaps are from the deciding runs (15 iterations, 99.9% intervals, all separated unless stated). Rank follows the size of the gap and how common the content is.

| Rank | Target | Where a reference is faster, or the cost | Content | Hot spot (perf, one thread) |
|---|---|---|---|---|
| 1 | Solid fills and strokes at large sizes | Icons load and render at 1000 px: Svg.Skia 19.4 ± 2.0 ms, ours 69.4 ± 6.4 (×3.6), one thread 73.6 ± 2.1 (×3.8); at 4000 px one thread ×1.4. One thread, 4000 px: Chart ×2.5, Tiger ×2.5, IconSheet ×1.5, StreetMap ×1.4; Tiger at 1000 px ×1.1 and one thread ×2.0 | Icons, logos, charts, maps, line art: the most common SVG | `PixelBlender.BlendWithCoverage` with a solid color: 43.6% self plus 16% for the conversions of the destination row to and from `Vector4` (Icons, 1000 px); 20% in Tiger, 13% in Chart and IconSheet, 21% in StreetMap with the scalar remainders |
| 2 | Gradient fills | One thread: Moose ×2.4 (1000 px) and ×2.7 (4000 px), SoftIceCream ×2.3, StyledLandscape ×3.3 (default threads ×1.4) | Design-tool exports, emoji, illustrations | `GradientBrushRenderer.this[x, y]`: 56% (Moose) to 60% (SoftIceCream at 4000 px) self, per pixel a virtual position call, a stop search, a `Vector4.Lerp`, and an encode to `Rgba32` that the blender converts back |
| 3 | Layers, masks, coverage clips | Dashboard at 1000 px: Svg.Skia 13.8 ± 0.4 ms, ours 87.8 ± 16.5 (×6.4), one thread 59.5 ± 1.9 (×4.3); at 4000 px ×1.6 and ×3.4; at 100 px the default threads are slower than Svg.Skia (6.55 ± 0.52 against 5.78 ± 0.20 ms) and than one thread (5.61 ± 0.23) | Masks and clips of design exports; less common, largest gap | Path clip coverage `ApplyPathClip` 59% inclusive (9.5% self), gradients 20%, mask effects 12% (`ExecuteApplyItem`: a new `Image` and a processor per mask band), layer composite 7%; with the default threads 7% kernel time and 6% semaphore waits from one `Parallel.For` per scene segment |
| 4 | Filters | FilterShadows at 1000 px: Svg.Skia 38.7 to 41.3 ms, ours 48.5 to 85.3 (×1.3 to ×2.1), one thread 61.5 to 65.6 (×1.6); at 4000 px one thread ×1.6. FilterBlur at 1000 px ×1.2, one thread ×2.8; at 4000 px one thread ×1.9 | Drop shadows of design tools: common in exports | Drawing the filter result through `SvgTextureBrush` (bilinear sample and encode per pixel): 25% (shadows) and 31% (blur) of all samples; blur passes 15% to 17%; the copy of every result into a new array: 57% (shadows) to 94% (blur at 4000 px) of the allocated bytes |
| 5 | Fresh render of a loaded document | Svg.Skia replays a picture: one thread TextHeavy ×28, TextDecorations ×19, Icons ×11, Dashboard ×6, Chart ×4.3, Tiger ×3.3 | A service that renders the same document many times; `DrawingBackendScene` already replays without this cost | Every render emits the commands and builds the scene again: the scene build is 11% (Icons) to 47% (IconSheet) of a load and render in the stage profiles, Tiger 40%, TextHeavy 34% (`FlushScene.Create`, one `LinearizerX16Y16` object, line blocks, and band arrays per path); the text documents allocate 6.5 to 13 MB per render (a retained scene about 5 KB, section 15.3) |
| 6 | Managed allocation and collections | Paris load and render 429 MB (1000 px) and 509 MB (4000 px), with 784 ms of GC pause per 1709 ms iteration (46%), and the largest peak at 4000 px; nested SVG images 129 MB per load and draw with 36% GC pause; StreetMap render 8.8, 17, and 49 MB at 100, 1000, 4000 px (220, 4, and 0.8 times the output); filter documents 40 to 48 MB per render at 4000 px; CssFeatures load 14.4 MB | Large documents and maps; embedded SVG images | `PointF[]` of Bézier flattening (28% of Paris), done three times per path (bounds, vertex count, geometry); `ILineSegment` objects of path data; nested node arrays; see 18.5 |
| 7 | Load of custom properties and `Identify` | CssFeatures load 13.4 ± 8.5 ms (still faster than Svg.Skia, 31 ms); `Identify` costs a full load (1.8 to 18.0 ms against 1.8 to 16.8 ms for `SvgDocument.Load`; a root-only read takes 0.02 to 0.04 ms) | Design systems with custom properties; image services that read sizes | See 18.6 |

### 18.5 Profiles

Stage shares are from `--profile svg`; CPU shares from `perf` with one thread unless stated; allocation shares from the allocation ticks. Shares are of all samples of the profile, which also loads the document and creates the output image.

- **Icons, 1000 px, one thread** (load and render 84 ms per iteration of 40 icons): scene render 83%, scene build 11%, read 4%. Self: `PixelBlender<Rgba32, NormalSrcOver>.BlendWithCoverageFunction` 43.6%, `SimdUtils.ByteToNormalizedFloat` 6.7%, `ConvertFloatToByteRemainder` 4.4%, `FloatToByteSaturate` 3.0%, `ConvertByteToNormalizedFloatRemainder` 2.2% (60% in the solid-color blend path that `SolidBrushRenderer.Apply` calls; the frame-pointer call graph loses frames inside the blender, so the inclusive shares of its callers are too low and are not used), `ImageFrameCollection` constructor 5.3% (clearing the new output), coverage rasterization (`EmitRows`, `LineUp`/`LineDown`) about 6%. Allocation 4.0 MB per iteration: `LineArrayX32Y16Block` 30% (stroke linearizer), `PointF[]` 20% (curve flattening).
- **Moose, 1000 px, one thread** (138 ms): scene render 92%. Self: `GradientBrushRenderer.this[x, y]` 56.2%, `BlendWithCoverageFunction` 14.2%, row conversions 6.5%, `FlushBufferedRun` 3.7%. SoftIceCream at 4000 px: `this[x, y]` 60.5%, blend 13.3%.
- **Tiger, 1000 px, one thread** (37 ms): scene build 40%, scene render 52%. Self: blend 18.9%, scalar conversion remainders 6.7%, `CubicBezierLineSegment.SubdivideAndAppend` 3.9%, rasterizer line walking 9%, `Linearizer.ProcessContained` 2.4%, `Path.BuildLinearGeometry` 1.5%, stroke linearizer 2.6%. Allocation 7.0 MB per load and render: `PointF[]` 61% (`FlattenedPointBuilder.EnsureCapacity` 18%, `BuildLinearGeometry` 11%, `SubdivideAndAppend` 10%, `FlattenCurve` 9%), line blocks 21%.
- **Dashboard, 1000 px** (83 ms with either thread count): scene render 68%, scene build 19 to 23%. One thread inclusive: `ApplyPathClip` 59.2% (self 9.5%), `ApplyClipDescriptors` 31%, `ApplyRectangleClip` 15.8%, gradients 20.0% self, `ExecuteApplyItem` 12%, `CompositeLayerBand` 7.4%, `ImageBrush` 5%. Default threads: the same work plus `[kernel]` 7.2% (scheduling), `LowLevelLifoSemaphore.Wait` 6.4%, `libcoreclr` 8.2%; the scene runs one `Parallel.For` per segment (`ExecuteSceneSegments`), each mask and layer barrier ends a segment, and the document has about 240 layers (14.2), so many small loops wake the workers. Load and render with the default threads is slower than with one thread (87.8 ± 16.5 against 59.5 ± 1.9 ms); the render of a loaded document alone overlaps (55.2 ± 8.4 against 59.9 ± 2.0 ms). Allocation 6.5 MB per iteration: `FlushScene.SceneOperationBlock` byte arrays 12%, line blocks 16%, `PointF[]` 19%.
- **FilterShadows, 1000 px, one thread** (88 ms): emit (filter engine) 49%, scene render 52% (the two overlap: the engine renders the source of each element on a nested canvas). Self: `SvgTextureBrush.SampleBilinear` 10.5%, `UnassociatedGradientPixelEncoder.Encode` 7.7%, `SvgTextureBrush.Index` 3.8% and `ToVector` 3.5% (25.5% to draw the 96 results), blur 15.4% (`Passes.Apply` 7.6%, the horizontal pass 7.8%), `SvgFeComposite.PorterDuff` 3.6%, `SvgFeOffset.Mix` 3.2%, `SvgFilterColor.Convert` 2.5%. Allocation 7.6 MB per iteration, 57% of it `Rgba32[]` from `SvgFilterEngine.TryRender` (line 248: each result is copied into a new array for the texture).
- **FilterBlur, 1000 px, one thread** (77 ms): `SvgTextureBrush.Apply` 31.2% self (the result is drawn at the scale of the device, so every sample is a bilinear lookup of a texel that a copy would give), gradients of the source 11.1%, blur passes 18%, blend 8.7%. At 4000 px 94.5% of the 48 MB per render is the result copy, and every iteration runs four generation 2 collections (20 in 5 iterations).
- **FilterChain, 1000 px, one thread** (106 ms): `SvgTextureBrush.Apply` 24.2%, `SvgFeTurbulence.Render` 11.7%, `SvgFeColorMatrix` 3.5%, blur 5%, `SvgFeComponentTransfer` 2.6%, lighting 2.3%, `powf` 1.7%.
- **TextHeavy, 1000 px, one thread** (40 ms): resolve 13% (text layout and shaping 12.5% of the CPU samples), emit 10%, scene build 34%, scene render 47%. Self: `Linearizer.ProcessContained` 11.5%, `ExecuteRasterizableItem` 8.5%, blend 7.3%. Inclusive: `FlushScene.ProcessPathCommand` 23.7%, `CreateRasterizableGeometry` 20.2%: thousands of small glyph paths, each with its own linearizer and band arrays. Allocation 13.3 MB per load and render: line blocks 22%, `SvgRenderNode[]` 8%, `SvgTextCluster[]` 7%, `DrawingCanvasState` 6% (one per `Save`), `LinearizerX16Y16` 5.5%; 17 generation 2 collections in 50 iterations.
- **Paris, 1000 px, one thread** (1709 ms): read 41%, resolve 11%, scene build 33%, scene render 6%; GC pause 784 ms per iteration (46%), and `libcoreclr` holds 30% of the CPU samples. Inclusive: `SvgReader.ReadAttributes` 33%, `Path.TryAppendSvgPath` 12%, `FlushScene.ProcessPathCommand` 23%, `ComplexPolygon.ToLinearGeometry` 5.5%. Allocation: `PointF[]` 28% (`CubicBezierLineSegment.FlattenCurve` 12%: called from `LinearVertexCount`, `Path.CalculateBounds`, and `BuildLinearGeometry`), `string` 7.6%, `SvgSpecifiedStyle[]` 7.5% (one per element from `ReadAttributes`), `ILineSegment[]` 6.5%, `LinearLineSegment` 6.1%, line blocks 5.6%.
- **StreetMap, 1000 px, one thread** (193 ms): read 21%, scene build 18%, scene render 55%. Self: blend 8.9%, `ConvertFloatToByteRemainder` 7.1% and `ConvertByteToNormalizedFloatRemainder` 4.6% (short stroke spans make the scalar tails of the row conversions expensive), `EmitRows` 7.0%, `RetainedStrokeRasterData.ExecuteBand` 4.9%. At 4000 px the render allocates 49 MB: `LineArrayX32Y16Block` 41% (`AppendLine` and `SplitAcrossBands`), `RasterizableBandInfo[]` 7%.
- **Chart, 1000 px, one thread** (35 ms): scene render 43%, scene build 19%, resolve 22%. Self: blend 13.3%, `ApplyRectangleClip` 7.3% (23.7% inclusive). The rectangle is the root viewport clip of `SvgEmitter.Draw`, which applies because the destination is 666.67 pixels high on a canvas of 667: every row of every draw then walks `ApplyRectangleInterior`, which computes the axis coverage of each column. IconSheet (17% inclusive) has the same cost from the viewport clips of its 400 `use` instances of symbols.
- **CssFeatures** and **nested SVG images**: section 18.6.

### 18.6 Open Items of Phase 5

**The render signal against 68408c9.** The phase 5 signal is not real. The tree of commit 68408c9 (`git archive`, built in Release on the same machine) and the current tree ran `RenderSvg.ImageSharp` and `ImageSharp_SingleThreaded` at 1000 pixels for the three filter documents, Tiger, and the icon set, in 10 alternating rounds (68408c9 first in each round) of 5 warmup and 20 measured iterations. The mean of the per-round ratios lies within its 95% interval of 1 for every case: Filter Blur one thread −1.9 ± 4.9% (phase 5: +10.4 ± 8.4%), default threads +0.1 ± 9.2%; Filter Shadows one thread −3.8 ± 4.5%; Filter Chain one thread −0.2 ± 6.9%; Tiger one thread +0.4 ± 3.2%, default +4.4 ± 10.8%; Icons one thread +1.2 ± 4.1%. The ratio pooled over the ten cases is −0.3% (95% interval −4.3% to +3.8%; after 6 rounds it was +0.9%, −4.9% to +7.0%). The five rounds of phase 5 (+5.2%, +2.6% to +7.8%) overlap this interval only above +2.6%, so any change is smaller than about 4%. Allocation is equal, or 10 KB lower for Filter Blur and Filter Chain. This agrees with the source: the layer limit pass (`SvgEmitter.GetLargestLayerPixels`) runs only for documents with scope nodes and is one pass over the render nodes without allocation for up to 64 nested scopes, against a render that takes 10 to 155 ms. No fix is needed.

**The load cost of custom properties.** The CSS features document (56 KB, 48 cards) loads in 13.4 ± 8.5 ms with 14.4 MB (short job; `DecodeSvg.DocumentLoad` with default options, 15 iterations: 16.8 ± 2.7 ms, 14.7 MB). A stage profile at 10 pixels (600 iterations, one thread, so the render is negligible) splits it into read and cascade 6.8 ms (1.5 MB) and resolve 12.5 ms (13.2 MB); every fifth iteration runs a generation 2 collection, 5.2 ms of GC pause per iteration. CPU (`perf`): resolve 50% of the samples, of which `SvgComputedStyle.SubstituteVariables` 30% inclusive, the boxes of the `clip-path` basic shapes 19% (`GetClipShapeBox` → `TryGetChildrenStrokeBoundingBox`, which strokes each child with `PolygonStroker` and `StrokedShapeGenerator` to find its stroke box, 6.4%), and object bounding boxes 13% (`Path.CalculateBounds` through `CubicBezierLineSegment.get_Bounds`, which flattens the curve); read 24%, nearly all in `SvgPropertyParser.TryParse` (21%: `SvgCalc` parsing 10%, color functions 5%); the runtime (`libcoreclr`, the collector) 12%. Allocation: `SvgCalc.Parser.TryParseValue` 17.6% (a `double[]` per parsed value) plus `Term` and `SvgCalcOperation[]` 7%; curve flattening for bounds 13% (`FlattenCurve` from `get_Bounds`); `SubstituteVariables` 10% (`StringBuilder`, `char[]`, `string`). Two thirds of the substitution allocation comes from the bounding box walks (`TryGetChildrenBoundingBox` 3.8%, `TryGetChildrenStrokeBoundingBox` 3.7%, against 2.9% from `ResolveElement`): the computed style of an element, with its `var()` substitution, is computed again for every box query of an ancestor. So the first things to try, in this order: compute the style of an element once per resolve and reuse it in the box walks (up to two thirds of the 30% substitution share); a parsed `calc()` expression without a heap array per value (25% of the allocation); bounds of curves from their control-point hull or their extrema instead of a flattening (13% of the allocation); a stroke box that does not build the stroke outline, where that gives the same box (19% of the CPU), and the stroke box cached per element once computed. The cache of substituted values per declaration that the plan names comes after these; the profile does not show the substitution itself (`SvgVariableSubstitution.Substitute` 1.1% of the allocation) as the main cost.

**The cost of `Image.Identify`.** This item is gone: Upstream Sync 1 removed the decoder (section 20). As measured here: `Image.Identify` through `SvgDecoder` loads the whole document with default load options. `DecodeSvg` (5 warmup, 15 measured iterations, default options): Tiger 1.80 ± 0.19 ms against `SvgDocument.Load` 1.77 ± 0.19 ms, Icons (40 documents) 0.77 ± 0.15 against 0.63 ± 0.10 ms, TextHeavy 8.19 ± 1.02 against 8.16 ± 0.98 ms (11.9 MB; with the system fonts the text load costs twice as much as with the benchmark font, 4.34 ms and 4.8 MB), FilterShadows 1.83 ± 0.33 against 1.94 ± 0.35 ms, CssFeatures 18.0 ± 1.6 against 16.8 ± 2.7 ms (14.7 MB); every pair of intervals overlaps and the allocation is equal within 3.5% (the Icons metadata). The floor of a cheaper path is a document that keeps only the prolog and the root start tag (plus one pixel of content, so that the profile can render): its read and resolve take 0.025 ms (Tiger), 0.029 ms (CssFeatures), 0.021 ms (FilterShadows), and 0.037 ms (TextHeavy), with 9 to 11 KB (stage profile, 2,000 iterations, one thread). An `Identify` that reads the root element and its style only would therefore cost 70 to 600 times less for these documents. It must give the same size as the full load: the root size can use font-relative units (`em`, `ex`, `ch`) whose font size can come from a `<style>` rule anywhere in the document, and percentages, so the cheap path needs the root style (17.7); a root whose `width`, `height`, and `viewBox` are absolute lengths could take the attributes alone.

**Drawing an embedded SVG image many times.** The document of `SvgNestedImageTextTests.NestingWithTextAtEveryLevel_IsBounded` (each level a 100 × 100 image with one text line and ten instances of the next level; the image nesting limit stops at the fifth level, so 11,110 nested documents draw, 10,000 of them at the deepest level) was profiled with Open Sans named on the text. A load and draw at 100 pixels allocates 129 MB and takes 87 to 397 ms on one thread (median 150 to 171 ms in three runs of 50 iterations) and 59 to 367 ms with the default threads; 10 levels cost the same as 5 (median 187 ms), as the test expects. The stages: resolve 34 to 43 ms (50 MB), emit 43 to 65 ms (47 MB), scene build 53 to 82 ms (32 MB), scene render 5 ms. The variation comes from the garbage collector: the 50 iterations run 41 to 43 generation 2 collections, and the GC pause averages 56 to 69 ms per iteration, which is the difference between the fastest iteration (87 ms) and the median. Server GC narrows the spread to 130 to 208 ms with the same median, and a large-object threshold of 16 MB narrows it to 101 to 207 ms, so the full collections are triggered by large-object allocations. Where the bytes come from: `SvgResolver.AddNestedDocument` 31% (the `SvgRenderNode[]` of the growing node list 16% and the dictionary of transforms 8%, both large arrays that grow by doubling), `CreateRasterizableGeometry` 20% (a linearizer, band arrays, and line blocks per glyph path), `SvgEmitter.EmitState.DrawNode` 15% (a `DrawingCanvasState` and a `DrawingOptions` per instance), `PrepareCompositionCore` 7.5%, `DrawingCanvas.Clip` 4% (one clip descriptor per instance). CPU: scene build 43%, emit 27%, resolve 21%; `libcoreclr` (the collector) 25% of the samples and page faults 6%. A draw of a nested document is a scope with a clip for each instance, and every instance repeats the canvas state, the options, and the geometry of its glyph paths although the 10,000 deepest instances share one nested document.

### 18.7 ImageSharp Patterns

From the SixLabors.ImageSharp 4.1.2 source (`/home/user/sixlabors/imagesharp`, tag v4.1.2, the version that `ImageSharp.Drawing.csproj` references). "Public" means the helper is public API of the package; an internal helper needs an equivalent of our own.

| Pattern | ImageSharp file | Where it applies here | Helper |
|---|---|---|---|
| Row operations as structs (`IRowOperation`, `IRowIntervalOperation<TBuffer>`) run by `ParallelRowIterator`, which uses a minimum of 4,096 pixels per task (`ParallelExecutionSettings.DefaultMinimumPixelsProcessedPerTask`) and runs inline when there is one step | `Advanced/ParallelRowIterator.cs`, `Advanced/ParallelExecutionSettings.cs`, used by `Processing/Processors/Transforms/Linear/AffineTransformProcessor{TPixel}.cs` | `DefaultDrawingBackend.ExecuteSceneRows` (a `Parallel.For` with `localInit` and body delegates for every segment, without a minimum of work: the Dashboard default-thread slowdown), `ExecuteApplyItemParallel`, `FlushScene.Create`, `SvgFilterEngine.ForEachBand` (an `Action<int, int>` with a closure per call, visible as `<Blur>b__0`) | Public |
| One scratch buffer per task, sized by the operation (`IRowOperation<TBuffer>.GetRequiredBufferLength`) | `Advanced/IRowOperation{TBuffer}.cs`, `Advanced/ParallelRowIterator.Wrappers.cs` | Per-geometry objects of the scene build: one `LinearizerX16Y16` per path (5% of TextHeavy and 11% of the nested document allocation), line blocks, `RasterizableBandInfo[]` | Public |
| Bulk pixel conversion with modifiers: `PixelOperations<TPixel>.ToVector4` and `FromVector4Destructive` with `PixelConversionModifiers.Scale \| Premultiply` per row | `PixelFormats/PixelOperations{TPixel}.cs`, used by `Processing/Processors/Convolution/ConvolutionProcessor{TPixel}.cs` | `SvgTextureBrush.Renderer.Apply` and `GradientBrushRenderer.Apply` encode each sample to `TPixel` one at a time, then the blender converts the row back to `Vector4`; `PixelBlender<TPixel>.BlendWithCoverage<TPixelSrc>` takes any source pixel type, so a row of `RgbaVector` (four floats) skips the byte round trip | Public (`RgbaVector`, `BlendWithCoverage<TPixelSrc>`); the SIMD converters (`SimdUtils`) are internal |
| Row blending through `PixelBlender<TPixel>` with caller scratch | `PixelFormats/PixelBlender{TPixel}.cs` | Already used by every brush renderer; `DefaultDrawingBackend.ComposeLayer` still calls the overload that rents a buffer per row | Public |
| Lookup tables for transfer functions (`CompandingUtilities` caches a table per function) | `ColorProfiles/Companding/CompandingUtilities.cs`, `ColorProfiles/Companding/SRgbCompanding.cs` | A color ramp per gradient brush instead of the stop search, the `Lerp`, and the encode per pixel (`GradientBrush.GradientBrushRenderer.this[x, y]`); `SvgFilterColor` already uses tables | Public, but the table is built for one function of one channel; the ramp needs its own table |
| `Vector128`/`Vector256`/`Vector512` paths with a scalar fallback for premultiplication and clamping | `Common/Helpers/Numerics.cs` (`Premultiply(Span<Vector4>)`, `Clamp`) | `SvgTextureBrush` samples and premultiplies per pixel; `GradientBrush` positions per pixel | Internal (`Numerics`); the public route is `PixelOperations` with the `Premultiply` modifier |
| Pooled memory from `MemoryAllocator`, `IMemoryOwner<T>`, and `Buffer2D<T>` instead of new arrays | `Memory/Allocators/MemoryAllocator.cs`, `Memory/Buffer2D{T}.cs` | `SvgFilterEngine.TryRender` copies each filter result into `new Rgba32[]` (94% of the FilterBlur allocation at 4000 px, large-object allocations that trigger full collections); `SvgTexture` of pattern tiles | Public |
| Bounds checks removed with `Unsafe.Add(ref MemoryMarshal.GetReference(span), i)` in loops proven in range | `PixelFormats/PixelOperations{TPixel}.Generated.cs`, `Processing/Processors/Convolution/Convolution2DRowOperation{TPixel}.cs` | `GradientBrushRenderer.Apply` (its own comment: "TODO: Remove bounds checks"), `SvgTextureBrush.Renderer.Apply`, `ApplyRectangleInterior` | Language feature |
| No delegate, closure, or interface call per pixel | `Processing/Processors/Convolution/Convolution2PassProcessor{TPixel}.cs` (the operations are structs passed by `in`) | `GradientBrushRenderer.this[x, y]` calls the virtual `PositionOnGradient` per pixel; the filter primitives run through `Action<int, int>` closures | — |
| Pools trimmed on generation 2 collections only after a trim period or under memory pressure (`TrimSettings`: 60 s, half the buffers) | `Memory/Allocators/Internals/UniformUnmanagedMemoryPool.cs` | `DefaultDrawingBackend.WorkerState.Gen2TrimSentinel` trims the worker pool on every generation 2 collection; with the large-object allocations of rank 6 these come every iteration (four per FilterBlur render at 4000 px) | Internal (`Gen2GcCallback`); our own sentinel exists |

Not found in the ImageSharp source as a reusable helper: a solid-color fill of a full-coverage span (the blender always converts to `Vector4`); an axis-aligned clip that narrows a span instead of multiplying coverage per pixel.

### 18.8 Order of the Work

From the ranks and the shares above (each gain is the measured share of the hot spot, an upper bound, not a promise):

1. Core fill and stroke first (ranks 1, 2, and the path allocations of rank 6), because it serves every document and every canvas user:
   - a full-coverage, opaque solid-color run written directly instead of blended (the solid blend path is 60% of Icons at 1000 px, 20% of Tiger, 13% of Chart; how much of it is full coverage is not measured);
   - a color ramp per gradient brush and per-row evaluation without a virtual call per pixel (56% to 60% of the gradient documents);
   - an axis-aligned rectangle clip that narrows the span once per row (7.3% self in Chart; also every `use` of a symbol);
   - curves flattened once per path at a scale and reused for the bounds, the vertex count, and the geometry (12% of the Paris allocation, `SubdivideAndAppend` 3.9% of Tiger).
2. Layers and compositing second (rank 3): the path clip coverage (59% of Dashboard), mask effects without a new image per band (12%), a minimum of work per parallel loop as in `ParallelRowIterator` (load and render of the Dashboard with the default threads is 28 ms slower than with one thread; kernel scheduling and semaphore waits are 14% of its samples).
3. Filters third (rank 4): draw a result at the device scale with a row copy and blend instead of bilinear samples (25% of FilterShadows, 31% of FilterBlur), and keep the result in pooled memory until the canvas has drawn it (57% to 94% of their allocation).
4. Emit, scene build, and text fourth (ranks 5 and 6): the per-path objects of the scene build, the canvas state and options per node, the nested document instances (129 MB per draw), and the text scene build.

### 18.9 Corrections to the Method

Two assumptions of the baseline were checked after it, with `--curve <entry> <long edge> <draws>` of the benchmark project (`SvgInvocationCurve`), which times every single draw of a sequence in one process. The runs shared the machine with other work, so the numbers are indicative; the effects are far larger than that noise.

**Skia caches filter results between draws of one picture.** Median time of a draw at 1000 pixels over 200 draws (500 for Tiger and Dashboard); the output pixels are equal in all three modes:

| Document | Replay of the same picture | Replay after `SKGraphics.PurgeAllCaches()` | A newly loaded picture per draw |
|---|---|---|---|
| FilterChain | 2.3 ms | 57 ms | 58 ms |
| FilterBlur | 5.8 ms | 25 ms | 25 ms |
| FilterShadows | 2.0 ms | 30 ms | 30 ms |
| Tiger | 6.9 ms | 6.9 ms | 7.1 ms |
| Dashboard | 9.5 ms | 9.5 ms | 9.9 ms |

The first replay of a picture costs as much as a purged one (FilterChain 56 ms), and every later one the cached time. Documents without filters are not affected, and the purge itself costs nothing measurable. `RenderSvg` therefore has two Svg.Skia methods: `SvgSkia` (the repeated draw of one document; its counterpart is `ImageSharpRetainedScene`, whose scene also holds the filter results) and `SvgSkia_ColdCaches` (the caches are emptied before every draw: the first draw of a loaded document; its counterpart is `ImageSharp`, which computes every filter again).

**A job with few warmup iterations can measure managed code that the runtime is still optimizing.** Every benchmark case runs in a new process. The default-thread render of the Dashboard at 1000 pixels took 678 ms on the first draw, about 50 ms on draw 30, and about 36 ms from draw 100; Tiger settled at draw 30 (252 ms, then 9.5 ms). When the first call is slow, BenchmarkDotNet runs one operation per iteration, so a job with 3 or 5 warmup iterations starts to measure after 5 to 7 draws. In the deciding runs of the baseline, 33 of the 88 ImageSharp.Drawing cells were measured after fewer than 25 draws. Three of them were clearly still getting faster while they were measured (last third of the measured iterations against the first third): the Dashboard with the default threads at 1000 pixels, load and render 89.5 to 76.9 ms and render 60.0 to 48.5 ms, and TextDecorations on one thread, 57.5 to 42.3 ms. Over all cells the measured iterations fell by 2% on average. The short jobs of the matrix in 18.2 are affected more: their means are 5% to 12% above the deciding runs for ImageSharp.Drawing (median per size) and 5% to 23% for Svg.Skia load and render, whose loading code is managed code too. Native rendering code (Skia, libvips) has no such warmup, so the effect makes ImageSharp.Drawing look slower than it is in the affected cells. The setup of `RenderSvg` and `LoadAndRenderSvg` now runs the Svg.Skia and ImageSharp.Drawing methods for 60 rounds and at least 3 seconds (at most 10 seconds) before BenchmarkDotNet starts (`SvgBenchmarkWarmup`). With it the first measured call of the Dashboard render takes 36 ms instead of 426 ms, and BenchmarkDotNet runs 13 operations per iteration instead of one.

Render of a loaded document at 1000 pixels with both corrections (5 warmup and 15 measured iterations, 99.9% intervals; the machine was shared with other work):

| Document | Svg.Skia, cold caches | ImageSharp.Drawing | One thread | Svg.Skia, replay | Retained scene | Retained scene, one thread |
|---|---|---|---|---|---|---|
| FilterChain | 58.6 ± 2.1 ms | 35.9 ± 4.0 ms | 70.1 ± 2.1 ms | 2.68 ± 0.31 ms | 10.9 ± 0.6 ms | 33.2 ± 0.2 ms |
| FilterBlur | 30.1 ± 1.9 ms | 35.9 ± 4.4 ms | 82.8 ± 10.6 ms | 8.20 ± 0.94 ms | 21.1 ± 3.7 ms | 36.5 ± 2.7 ms |
| FilterShadows | 33.2 ± 3.2 ms | 59.5 ± 8.5 ms | 65.3 ± 3.6 ms | 2.60 ± 0.48 ms | 17.5 ± 1.8 ms | 31.9 ± 0.6 ms |
| Dashboard | 9.76 ± 0.28 ms | 40.1 ± 4.2 ms | 58.5 ± 0.2 ms | 9.92 ± 0.68 ms | 31.4 ± 2.4 ms | 56.8 ± 0.3 ms |

What this changes in the baseline:

- First draw of a loaded filter document: ImageSharp.Drawing with the default threads is faster than Svg.Skia for FilterChain (35.9 against 58.6 ms, separated intervals), overlaps it for FilterBlur, and is 1.8 times slower for FilterShadows. On one thread it is 1.2 (FilterChain), 2.8 (FilterBlur), and 2.0 (FilterShadows) times slower. These agree with the load and render gaps that rank the filter target in 18.4, so the rank stands.
- Repeated draw of a filter document: Svg.Skia replays from its cache in 2.6 to 8.2 ms; the retained scene of ImageSharp.Drawing takes 10.9 to 21.1 ms (2.6 to 6.7 times as long) and 31.9 to 36.5 ms on one thread. The retained scene holds each filter result as a texture and draws it through `SvgTextureBrush`, the hot spot that rank 4 names.
- Dashboard render with the default threads: 40.1 ± 4.2 ms, not 55.2 ± 8.4 ms, so the gap to Svg.Skia is 4.1 times, not 5.6. The default threads are faster than one thread for the render of a loaded document (40.1 against 58.5 ms); the statement of rank 3 that the default threads are slower holds for the under-warmed load and render cell only and must be measured again.
- The cells of 18.2 and the gaps of 18.4 were not measured again as a whole. The work that follows measures before and after with the corrected benchmark, and the final matrix of phase 6 replaces 18.2. Until then, a gap in 18.2 or 18.4 that rests on a short job, or on a cell with one operation per iteration, is an upper bound for ImageSharp.Drawing.

## 19. Phase 6 Results

Each package of phase 6 adds a subsection here. The numbers are from the corrected benchmark of 18.9 (commit 820861e) unless a subsection says otherwise.

### 19.1 Core Fill and Stroke

**What changed.** Four changes were kept. None of them changes a pixel: every reference image and expected result is unchanged, the 2,242 images that the test suite writes are byte for byte equal before and after, and `results.tsv` of the resvg suite is equal line for line (1179 conformance passes).

1. **Opaque solid fills** (`SolidBrush`). A new renderer, `OpaqueSolidBrushRenderer`, is chosen when the color is opaque, the blend percentage is 1, the blender is the straight source-over normal blender, and the pixel format stores 8-bit or 16-bit integer channels with straight or no alpha (`Rgba32`, `Bgra32`, `Argb32`, `Abgr32`, `Rgb24`, `Bgr24`, `Rgba64`, `Rgb48`). For a row of 32 pixels or more it finds the runs of coverage exactly 1 (an integer search for the bit pattern of 1.0) and writes the color into them with `Span.Fill`; it blends only the other pixels. Two facts make the result equal to one blend of the whole row. First, with an opaque source, amount 1, and coverage 1, the equations of the blender reduce to the source color, and the rounding error of every step stays below 1e-6, far below half a level of a 16-bit channel (7.6e-6), so the stored channel is the color's channel. Second, the blender of ImageSharp 4.1.2 runs a vector loop over the first multiple of 4 pixels (512-bit registers; 2 with 256-bit registers) of each call and a scalar loop over the rest, and the two loops differ in the last bit because only the vector loop fuses its multiply-adds; so every blended piece starts at a multiple of that group, and the last piece is never 2 or 3 pixels long on a 512-bit machine (that length takes the 256-bit loop). A test confirmed that the rule matters: with a float pixel format admitted, a split at multiples of 1 or 2 changed pixels on this machine, a split at multiples of 4 did not. The plain `SolidBrushRenderer` keeps its size (a flag in it added 8 bytes per renderer, 7 KB per Chart render).
2. **Gradients** (`GradientBrush`, `LinearGradientBrush`, `RadialGradientBrush`). `Apply` computes the positions of the whole span with one call (`PositionsOnGradient`; the linear and radial renderers override it with the same expression per pixel and no virtual call), handles the repetition mode outside the pixel loop, finds the bracketing stops by index instead of copying tuples of stops, and, for `Rgba32` with the straight encoder, packs the straight colors of the row with one bulk `FromVector4Destructive` instead of one `FromScaledVector4` per pixel. The per-pixel and the bulk conversion both scale by 255, add one half, and truncate (one clamps first, the other saturates), so they give the same byte for every value within the range of a 32-bit integer.
3. **Rectangle clips** (`DefaultDrawingBackend.ApplyRectangleInterior` and its difference variant). The columns whose axis coverage is exactly 1 by the comparisons of `GetAxisCoverage` alone (from `Ceiling(left)` to `Floor(right) - 1` for area coverage; the interval of centre tests for hard edges) are skipped in one step; columns outside are compared one by one as before, so the runs passed on are the same. The root viewport clip of `SvgEmitter.Draw` for the Chart (destination 666.67 pixels high on a canvas of 667) cannot be dropped: it gives the last row a coverage of 0.67. With the skip it costs a constant per span.
4. **Curve flattening** (`CubicBezierLineSegment`, `FlattenedPointBuilder`). The bounds of a cubic segment are taken from the points of a flattening at scale 1 as they are produced, without keeping an array, unless the cached flattening is at scale 1. The bounds are the same values as before (same points, same order). The builder appends into a scratch array that the thread reuses and returns one array of the exact length, instead of a growing array that was trimmed at the end. A render at a scale other than 1 now flattens each curve once into a kept array instead of twice; a render at scale 1 flattens twice where it flattened once (the bounds pass no longer leaves its array in the cache).

**Measured gain.** Alternating rounds of commit 820861e (built from `git archive`) and the final tree, 5 warmup and 15 measured iterations per case, 3 rounds; the value is the mean of the round means in ms, then the mean of the per-round ratios with its 95% t interval over the rounds. A ratio whose interval excludes 0 is a separated change. Allocation is the managed allocation of ImageSharp.Drawing per operation. Svg.Skia ran once in the same session from the final tree (`SvgSkia_ColdCaches` for render, `SvgSkia` for load and render; 99.9% interval of BenchmarkDotNet); the gap is ImageSharp.Drawing after the change divided by Svg.Skia (below 1: ImageSharp.Drawing is faster).

Render of a loaded document:

| Document | Long edge | ImageSharp.Drawing | One thread | Allocated (one thread) | Svg.Skia | Gap after, default threads / one thread |
|---|---|---|---|---|---|---|
| Icons | 1000 | 46.3 → 39 (-16 ± 20%) | 151 → 118 (-22 ± 10%) | 3.18 MB → 3.18 MB | 13.5 ± 2.16 | ×2.88 / ×8.73 |
| Icons | 4000 | 641 → 481 (-25 ± 12%) | 2223 → 1668 (-25 ± 2%) | 12.22 MB → 12.22 MB | 221 ± 23.8 | ×2.17 / ×7.53 |
| Tiger | 1000 | 12.1 → 8.23 (-31 ± 28%) | 26.2 → 19.7 (-24 ± 15%) | 1.84 MB → 1.84 MB | 6.93 ± 0.385 | ×1.19 / ×2.85 |
| Tiger | 4000 | 69.6 → 37.3 (-45 ± 22%) | 212 → 116 (-45 ± 4%) | 4.82 MB → 4.82 MB | 44.3 ± 4.71 | ×0.84 / ×2.63 |
| Chart | 1000 | 6.96 → 6.55 (-6 ± 36%) | 16.2 → 13.8 (-15 ± 17%) | 2.37 MB → 2.37 MB | 4.95 ± 0.29 | ×1.32 / ×2.79 |
| Chart | 4000 | 45 → 28.5 (-36 ± 18%) | 140 → 86.2 (-38 ± 17%) | 5.03 MB → 5.03 MB | 22.2 ± 1.57 | ×1.28 / ×3.88 |
| IconSheet | 1000 | 8.71 → 7.83 (-10 ± 11%) | 20.3 → 17.7 (-13 ± 12%) | 2.92 MB → 2.92 MB | 6.79 ± 0.422 | ×1.15 / ×2.60 |
| IconSheet | 4000 | 44.6 → 31.7 (-29 ± 11%) | 149 → 95 (-36 ± 1%) | 5.09 MB → 5.09 MB | 41.3 ± 3.25 | ×0.77 / ×2.30 |
| StreetMap | 1000 | 55.7 → 52.8 (-5 ± 22%) | 118 → 117 (-1 ± 8%) | 16.60 MB → 16.60 MB | 61.3 ± 1.22 | ×0.86 / ×1.91 |
| StreetMap | 4000 | 219 → 198 (-9 ± 18%) | 579 → 498 (-14 ± 20%) | 48.45 MB → 48.45 MB | 307 ± 8.14 | ×0.64 / ×1.62 |
| Moose | 1000 | 38.6 → 28.8 (-25 ± 7%) | 102 → 79.9 (-21 ± 2%) | 1.08 MB → 1.08 MB | 39 ± 0.152 | ×0.74 / ×2.05 |
| Moose | 4000 | 537 → 357 (-33 ± 26%) | 1556 → 1141 (-27 ± 1%) | 3.66 MB → 3.66 MB | 561 ± 25.3 | ×0.64 / ×2.03 |
| SoftIceCream | 1000 | 10.5 → 7.25 (-30 ± 30%) | 24.7 → 17 (-31 ± 7%) | 249 KB → 249 KB | 5.87 ± 0.0778 | ×1.23 / ×2.89 |
| SoftIceCream | 4000 | 111 → 72.7 (-34 ± 19%) | 346 → 239 (-31 ± 3%) | 944 KB → 944 KB | 101 ± 9.85 | ×0.72 / ×2.37 |
| StyledLandscape | 1000 | 6.72 → 4.49 (-33 ± 13%) | 16.4 → 10.2 (-38 ± 5%) | 286 KB → 286 KB | 3.97 ± 0.361 | ×1.13 / ×2.56 |
| StyledLandscape | 4000 | 79 → 51.9 (-34 ± 38%) | 250 → 146 (-42 ± 9%) | 964 KB → 964 KB | 100 ± 6.71 | ×0.52 / ×1.45 |

Load and render:

| Document | Long edge | ImageSharp.Drawing | One thread | Allocated (one thread) | Svg.Skia | Gap after, default threads / one thread |
|---|---|---|---|---|---|---|
| Icons | 1000 | 52.7 → 39.1 (-26 ± 16%) | 76.1 → 42.5 (-44 ± 12%) | 4.00 MB → 3.65 MB | 19.5 ± 1.87 | ×2.00 / ×2.18 |
| Icons | 4000 | 585 → 319 (-45 ± 25%) | 1225 → 541 (-56 ± 20%) | 14.66 MB → 14.03 MB | 823 ± 26.1 | ×0.39 / ×0.66 |
| Tiger | 1000 | 12.7 → 10.2 (-19 ± 18%) | 25.3 → 21 (-17 ± 19%) | 6.80 MB → 4.01 MB | 12.3 ± 0.932 | ×0.83 / ×1.70 |
| Tiger | 4000 | 68.5 → 37.4 (-44 ± 23%) | 173 → 87.5 (-49 ± 10%) | 12.51 MB → 8.15 MB | 61.6 ± 2.43 | ×0.61 / ×1.42 |
| Chart | 1000 | 9.85 → 7.68 (-22 ± 19%) | 17.2 → 13.4 (-22 ± 8%) | 5.23 MB → 4.69 MB | 27.5 ± 2.33 | ×0.28 / ×0.49 |
| Chart | 4000 | 49.1 → 28.6 (-41 ± 16%) | 125 → 70.9 (-44 ± 12%) | 8.16 MB → 7.41 MB | 46.1 ± 0.662 | ×0.62 / ×1.54 |
| IconSheet | 1000 | 11.6 → 10.1 (-13 ± 8%) | 21.2 → 17.8 (-16 ± 3%) | 4.63 MB → 4.52 MB | 40.6 ± 1.96 | ×0.25 / ×0.44 |
| IconSheet | 4000 | 48.9 → 31.5 (-35 ± 22%) | 123 → 71.8 (-42 ± 8%) | 6.80 MB → 6.62 MB | 81.2 ± 0.396 | ×0.39 / ×0.88 |
| StreetMap | 1000 | 71.2 → 71.7 (+1 ± 13%) | 136 → 133 (-2 ± 14%) | 27.97 MB → 27.97 MB | 165 ± 4.75 | ×0.44 / ×0.81 |
| StreetMap | 4000 | 217 → 210 (-3 ± 8%) | 548 → 490 (-10 ± 12%) | 59.72 MB → 59.72 MB | 397 ± 18.1 | ×0.53 / ×1.23 |
| Moose | 1000 | 41.3 → 29.4 (-28 ± 19%) | 103 → 79.7 (-23 ± 12%) | 3.59 MB → 2.30 MB | 41.7 ± 0.233 | ×0.70 / ×1.91 |
| Moose | 4000 | 451 → 338 (-25 ± 8%) | 1521 → 1219 (-20 ± 25%) | 7.48 MB → 5.24 MB | 571 ± 11.9 | ×0.59 / ×2.13 |
| SoftIceCream | 1000 | 10.3 → 7.81 (-24 ± 29%) | 21.7 → 14.8 (-32 ± 1%) | 732 KB → 537 KB | 9.1 ± 0.329 | ×0.86 / ×1.62 |
| SoftIceCream | 4000 | 110 → 76.7 (-30 ± 23%) | 337 → 209 (-37 ± 18%) | 1.49 MB → 1.15 MB | 134 ± 21 | ×0.57 / ×1.56 |
| StyledLandscape | 1000 | 7.04 → 4.25 (-40 ± 3%) | 16.1 → 9.34 (-42 ± 17%) | 589 KB → 446 KB | 6.7 ± 0.136 | ×0.63 / ×1.39 |
| StyledLandscape | 4000 | 75 → 44.9 (-40 ± 9%) | 221 → 138 (-38 ± 37%) | 1.26 MB → 1.06 MB | 112 ± 4.37 | ×0.40 / ×1.24 |

The render of a loaded document allocates the same as before; load and render allocates less wherever the document has curves, because the load no longer keeps a flattening at scale 1 per curve (Tiger 6.80 to 4.01 MB at 1000 pixels, Moose 3.59 to 2.30 MB). Paris30k load and render at 1000 pixels: 870 → 874 ms (+1 ± 17%) with the default threads, 1135 → 1079 ms (−4.9 ± 2.8%) on one thread, allocation 428.7 → 364.0 MB; Svg.Skia 4242 ± 295 ms. The measured iterations show no trend: over all 198 cases the mean ratio of the last third of the iterations to the first third is 0.997 to 1.011 per method and size.

Per attempt (each against the tree of the attempt before it, 3 alternating rounds of `RenderSvg` or `LoadAndRenderSvg` at 1000 pixels, corrected benchmark, mean ratio and 95% interval; one thread unless stated):

| Attempt | Kept | Documents | Change |
|---|---|---|---|
| Opaque solid fill | yes | Icons −23.9 ± 2.3%, Tiger −15.7 ± 14.1%, Chart −10.5 ± 8.0%, IconSheet −12.6 ± 9.9%, StreetMap −4.8 ± 12.0% (not separated) | render allocation equal |
| Gradient positions per row | yes | SoftIceCream −11.1 ± 8.4% (default threads −4.0 ± 2.2%), StyledLandscape −8.3 ± 3.1% (−14.1 ± 5.0%), Moose −7.9 ± 12.9% (not separated) | equal |
| Rectangle clip interior | yes | Chart −13.0 ± 3.7%, IconSheet +1.2 ± 10.7% (not separated) | equal |
| Curve flattening | yes | load and render allocation Tiger 6.88 → 4.10 MB, Paris 428.7 → 364.0 MB, StreetMap equal; Paris default threads −6.8 ± 4.9%, Tiger and StreetMap not separated | −40% Tiger, −15% Paris |
| Padded blend of short tails | no | profile only (perf, Icons 1000 px, 250 iterations): scene render 31.2 → 33.5 ms, StreetMap 99.2 → 99.3 ms | reverted |
| Gradient bulk pack (`Rgba32`) | yes | Moose −12.7 ± 11.5% (default −13.4 ± 8.8%), SoftIceCream −17.1 ± 3.3% (−19.6 ± 9.5%), StyledLandscape −24.7 ± 7.7% (−25.2 ± 14.1%) | equal |

The reverted attempt blended the end of a short row in a padded copy, so that the bulk conversions of ImageSharp ran their vector loop over it (their scalar remainders were 8.9% and 8.9% of the Icons samples after the solid fill change). It kept the output equal, but the copies (`Memmove` 8.3% of the samples) and the blend of the padding (blend 8.2% → 12.2%) cost more than the remainders.

**Non-SVG benchmarks.** `DrawBeziers`, `EllipseStressTest`, `FillRectangle` (both methods), and `FillWithPattern`, 15 warmup and 15 measured iterations, 5 alternating rounds: Draw Beziers +2.4 ± 6.7%, ellipse stress +7.0 ± 12.8%, fill with pattern −0.2 ± 6.0% (none separated), fill rectangle −27.1 ± 8.5% and as polygon −22.6 ± 22.1% (faster). `DrawPolygon`, `FillPolygon`, `FillParis`, and `FillTiger` cannot run on this machine: their setup creates System.Drawing objects, which throw `PlatformNotSupportedException` on Linux; the SVG Tiger and Paris cells above measure the same fill and stroke code.

**Gap that remains to Svg.Skia.** Render of a loaded document on one thread is still 1.5 to 8.7 times slower than Svg.Skia; with the default threads ImageSharp.Drawing is now faster at 4000 pixels for Tiger, IconSheet, StreetMap, Moose, SoftIceCream, and StyledLandscape, and 1.1 to 2.9 times slower at 1000 pixels except StreetMap and Moose. The largest gap is the Icons document (×2.9 default threads, ×8.7 one thread at 1000 pixels). Where the time goes now (perf, one thread, 1000 pixels): in Icons, clearing the new output (`ImageFrameCollection` constructor, 10%), the scalar remainders of the row conversions (18%), and the blend of the edge pixels (8%); in Moose the gradient loop (27%), the radial position (18%), and the blend (18%); in StreetMap, whose roads are rows of 4 to 15 pixels, the blend and its scalar remainders (19%) and the rasterizer. The next steps that keep the output equal would be a blend of a short row that does not convert the whole row twice (it needs the blend functions of ImageSharp, which are internal) and a cheaper radial position. The per-draw scene build (rank 5) and the layers (rank 3) belong to later packages.

**The clear in the render benchmark.** `RenderSvg` clears the target area of each document before it draws the document (`canvas.Clear` with a transparent brush; the Svg.Skia method does the same with `SKCanvas.Clear`). The Icons entry holds 40 documents, so one operation clears the whole canvas 40 times. Measured after this package with `--curve Icons 1000 80` and a scratch build that can leave the clear out (median of draws 31 to 80, one run of each, shared machine):

| Icons, 1000 pixels | With the clear | Without the clear |
|---|---|---|
| ImageSharp.Drawing, one thread | 113.7 ms | 28.6 ms |
| ImageSharp.Drawing, default threads | 51.0 ms | 12.5 ms |
| Svg.Skia, replay | 13.9 ms | 7.2 ms |

Three quarters of the Icons render time of ImageSharp.Drawing is the clear: about 2.1 ms per megapixel on one thread, against about 0.17 ms for Skia. The drawing of the icons alone is 1.7 times (default threads) and 4.0 times (one thread) slower than Svg.Skia, not 2.9 and 8.7 times. The baseline profile of rank 1 in 18.4 (about 60% of the Icons samples in the blend and its conversions) therefore measured the clear for the most part. The clear blends with the `Src` composition mode, so the opaque solid fill path of this package does not apply to it. Every `RenderSvg` cell holds one clear per document, and at 4000 pixels one clear is 16 megapixels; `LoadAndRenderSvg` draws into a new image and has no clear, which is why the Tiger at 4000 pixels on one thread takes 116 ms to render and 87.5 ms to load and render. A direct write for a clear at full coverage is the first item of the next package.

### 19.2 Layers, Masks, Clips, and the Clear

The numbers are from the corrected benchmark of 18.9 at commit 4f25f0b (the state after 19.1).

**What changed.** Four changes were kept. None of them changes a pixel: every reference image and expected result is unchanged, the 2,242 images that the test suite writes are byte for byte equal before and after, and `results.tsv` of the resvg suite is equal line for line (1179 conformance passes).

1. **The clear at full coverage** (`SolidBrush`). A profile of the Icons render at 1000 pixels on one thread (BenchmarkDotNet in process, `perf`) put 47% of the samples in `BlendWithCoverageFunction` and 20% in the row conversions to and from `Vector4`, nearly all of it the clear; the rasterization of the cleared rectangle was about 6%. The renderer of 19.1, now `FullCoverageSolidBrushRenderer`, also takes the composition of `Clear`: normal color blending, `Src` alpha composition, and amount 1. It writes one pixel into the runs of coverage exactly 1 and blends the other pixels with the split rule of 19.1. Why the blender gives that pixel for every backdrop: `Src` at amount 1 returns the source color `s` with its alpha `sA`, and the coverage mix at coverage 1 computes `(sA * s - bA * b) * 1 + bA * b` and `(sA - bA) * 1 + bA`, three roundings of values in 0 to 1, so the mixed alpha is within `2u` of `sA` and the mixed color within `3u` of `sA * s` (`u = 2^-24`), with or without fused multiply-add. For `sA = 0` the result is +0 exactly for every backdrop, the blender does not divide a zero alpha, and it writes the pixel of zeros, whatever the color channels of the brush (a clear with transparent white gives transparent black, as before). For `sA > 0` the division gives a color within `5u / sA + u` of `s`: below 7.7e-5 for an 8-bit format (`sA` at least 1/255, half a level 2e-3) and below 4e-7 for an opaque color (half a 16-bit level 7.6e-6). The renderer therefore admits any color for `Rgba32`, `Bgra32`, `Argb32`, `Abgr32`, `Rgb24`, and `Bgr24`, and an opaque or a transparent color for `Rgba64` and `Rgb48`. A translucent color in a 16-bit format is not admitted, and the test below shows why: with alpha 1/65535 the blender gives channels that depend on the backdrop (green 30016 instead of 30000).
2. **Path clip bands** (`DefaultDrawingBackend.Helpers.cs`). `ApplyPathClip` rasterized the whole band of 16 rows of the clip geometry for every subject span and kept one row of it, so a subject item rasterized each band of its clip at least 16 times. The worker now keeps, per clip index, the rows of the last band that the rasterizer emitted (`PathClipBand`), keyed by the retained geometry and the band index, and rasterizes again only for another geometry or band. The coverage of a span is a copy of its row: the emitted values are stored in a dense row, the columns between emitted spans hold 0, and the span is filled with 0 (intersection) or 1 (difference) before the copy, so it gets the values that the direct copy of the emitted spans gave (`1 - 0` is exactly 1). The rasterizer is deterministic and the retained geometry does not change while its scene exists, so the stored rows are the rows it would emit again. A pooled worker state forgets the keys when it is returned.
3. **Run search in path clips.** The split of a span into runs of equal clip coverage uses `IndexOfAnyExcept` on the bit patterns instead of a comparison per pixel. Bit equality is float equality for these values: they are the fill values +0 and 1, emitted coverage (positive), +0, and `1 - c`; none is -0 or NaN.
4. **A minimum of work per parallel task** (`ParallelExecutionHelper.GetPartitionCountForPixels`, used by `ExecuteSceneRows`, `ExecuteApplyItemParallel`, and `CompositeLayerTarget`). The row loops plan their tasks as `ParallelRowIterator` of ImageSharp does (18.7): at most one task per band and per 4,096 pixels, and at most the configured degree. When that gives one task, the bands run on the calling thread with one rented worker state instead of a `Parallel.For`; the one-thread configuration always takes that path. The bands are disjoint and run the same operations either way, so the pixels are the same. A Dashboard render with one thread allocates 8% less (4.18 to 3.83 MB at 1000 pixels), because the loop delegates, closures, and options of each segment are gone.

Tests: `SolidBrushRendererTests` gained `Clear_MatchesWholeRowBlend_*` for ten pixel formats (the eight admitted ones, `RgbaVector`, and `L8`) and the six option sets of the fill test (a clear forces its own composition, so all six must give the same result as a blend of each whole row through `Clear`). The scene clears rectangles on whole and fractional edges, rows of every length from 1 to 79 pixels at every start column modulo 16, and ellipses, over a backdrop with a different color and alpha in every pixel. The color list of both tests gained transparent white, an alpha of 2/255, and two 16-bit colors with alpha 1/65535 and 300/65535. Sensitivity checks, each a temporary change of the renderer: writing the brush color for a transparent color under `Src` makes 38 tests fail (every format with alpha); admitting translucent colors in 16-bit formats makes 8 `Rgba64` tests fail (with the 8-bit colors alone the tests still passed, which is why the 16-bit colors were added); admitting `RgbaVector` makes 10 tests fail; splitting the rows at every pixel instead of every group of four makes no test fail on this machine for the admitted formats, as in 19.1, where the split rule mattered for float formats only. The path clip, run search, and parallel changes have no new test: they keep the output of the 2,242 images of the suite (many of them with nested path clips, layers, and masks) byte for byte equal, and no requirement changed.

**Measured gain.** Alternating rounds of commit 4f25f0b (built from `git archive`) and the final tree, 5 warmup and 15 measured iterations per case, 3 rounds; the value is the mean of the round means in ms, then the mean of the per-round ratios with its 95% t interval over the rounds; "(n.s.)" marks a ratio whose interval includes 0. Allocation is the managed allocation of ImageSharp.Drawing per operation. Svg.Skia ran once from the final tree (`SvgSkia_ColdCaches` for the render of FilterShadows, `SvgSkia` otherwise and for load and render; 99.9% interval of BenchmarkDotNet); the gap is ImageSharp.Drawing after the change divided by Svg.Skia.

Render of a loaded document:

| Document | Long edge | ImageSharp.Drawing | One thread | Allocated (one thread) | Svg.Skia | Gap after, default threads / one thread |
|---|---|---|---|---|---|---|
| Dashboard | 1000 | 30.9 → 23 (-25 ± 25%) (n.s.) | 56 → 33.5 (-40 ± 21%) | 4.18 MB → 3.83 MB | 10.3 ± 0.811 | ×2.23 / ×3.25 |
| Dashboard | 4000 | 298 → 246 (-18 ± 16%) | 569 → 444 (-20 ± 44%) (n.s.) | 11.14 MB → 10.80 MB | 211 ± 17.4 | ×1.17 / ×2.11 |
| PatternChart | 1000 | 15.6 → 14 (-10 ± 11%) (n.s.) | 33.5 → 30.1 (-10 ± 22%) (n.s.) | 1.92 MB → 1.86 MB | 25.5 ± 1.25 | ×0.55 / ×1.18 |
| PatternChart | 4000 | 143 → 132 (-7 ± 36%) (n.s.) | 391 → 350 (-10 ± 19%) (n.s.) | 5.12 MB → 5.06 MB | 449 ± 9.25 | ×0.29 / ×0.78 |
| CssFeatures | 1000 | 27.5 → 8.75 (-68 ± 2%) | 63.5 → 18.1 (-71 ± 9%) | 4.31 MB → 4.31 MB | 0.377 ± 0.0681 (renders it black, 18.1) | not comparable |
| CssFeatures | 4000 | 115 → 30.4 (-73 ± 6%) | 303 → 74.5 (-75 ± 4%) | 11.68 MB → 11.68 MB | 9.96 ± 2.09 (renders it black, 18.1) | not comparable |
| FilterShadows | 1000 | 52.8 → 55.6 (+5 ± 4%) | 61 → 63.3 (+4 ± 33%) (n.s.) | 4.85 MB → 4.70 MB | 36.7 ± 4.8 | ×1.52 / ×1.72 |
| FilterShadows | 4000 | 439 → 408 (-7 ± 23%) (n.s.) | 704 → 699 (-1 ± 18%) (n.s.) | 38.06 MB → 37.91 MB | 434 ± 43.4 | ×0.94 / ×1.61 |
| Icons | 1000 | 41.6 → 16.2 (-61 ± 7%) | 130 → 42.9 (-67 ± 6%) | 3.18 MB → 3.18 MB | 16 ± 2.87 | ×1.01 / ×2.69 |
| Icons | 4000 | 506 → 97.4 (-81 ± 6%) | 1757 → 312 (-82 ± 4%) | 12.22 MB → 12.22 MB | 460 ± 80.5 | ×0.21 / ×0.68 |
| Tiger | 1000 | 9.56 → 8.76 (-8 ± 15%) (n.s.) | 19.4 → 19.5 (+0 ± 23%) (n.s.) | 1.84 MB → 1.84 MB | 7.96 ± 0.65 | ×1.10 / ×2.44 |
| Tiger | 4000 | 39.4 → 31.2 (-20 ± 25%) (n.s.) | 113 → 83 (-26 ± 14%) | 4.82 MB → 4.82 MB | 50.2 ± 2.44 | ×0.62 / ×1.65 |
| Chart | 1000 | 6.38 → 6.03 (-5 ± 32%) (n.s.) | 13.4 → 12.5 (-7 ± 24%) (n.s.) | 2.37 MB → 2.38 MB | 4.47 ± 0.773 | ×1.35 / ×2.80 |
| Chart | 4000 | 28.9 → 22.6 (-22 ± 17%) | 87.8 → 60.9 (-30 ± 14%) | 5.03 MB → 5.04 MB | 22.5 ± 5.65 | ×1.00 / ×2.71 |
| IconSheet | 1000 | 8.46 → 7.7 (-9 ± 12%) (n.s.) | 17.7 → 16.4 (-7 ± 14%) (n.s.) | 2.92 MB → 2.92 MB | 7.44 ± 0.973 | ×1.04 / ×2.20 |
| IconSheet | 4000 | 33.4 → 24.4 (-27 ± 14%) | 97 → 65.5 (-32 ± 10%) | 5.09 MB → 5.10 MB | 52.3 ± 3.1 | ×0.47 / ×1.25 |

Load and render:

| Document | Long edge | ImageSharp.Drawing | One thread | Allocated (one thread) | Svg.Skia | Gap after, default threads / one thread |
|---|---|---|---|---|---|---|
| Dashboard | 100 | 7.78 → 4.77 (-38 ± 24%) | 5.57 → 4.31 (-22 ± 28%) (n.s.) | 2.76 MB → 2.42 MB | 6.03 ± 0.408 | ×0.79 / ×0.72 |
| Dashboard | 1000 | 35.5 → 24.5 (-30 ± 27%) | 55.9 → 36.5 (-34 ± 20%) | 5.22 MB → 4.88 MB | 15.8 ± 1.04 | ×1.55 / ×2.31 |
| PatternChart | 100 | 2.79 → 2.72 (-3 ± 22%) (n.s.) | 2.8 → 3.04 (+8 ± 13%) (n.s.) | 1.60 MB → 1.54 MB | 19.8 ± 2.5 | ×0.14 / ×0.15 |
| PatternChart | 1000 | 15.7 → 15.4 (-2 ± 11%) (n.s.) | 35.2 → 32.3 (-6 ± 41%) (n.s.) | 2.48 MB → 2.42 MB | 34.5 ± 3.25 | ×0.45 / ×0.94 |
| CssFeatures | 100 | 21.2 → 20.5 (-3 ± 25%) (n.s.) | 24.7 → 21.6 (-13 ± 5%) | 15.12 MB → 15.13 MB | 30.5 ± 2.95 (renders it black, 18.1) | not comparable |
| CssFeatures | 1000 | 44.7 → 25.3 (-43 ± 17%) | 78.1 → 34.7 (-55 ± 16%) | 17.10 MB → 17.10 MB | 34.2 ± 4.01 (renders it black, 18.1) | not comparable |
| FilterShadows | 100 | 9.63 → 10.2 (+7 ± 48%) (n.s.) | 5.25 → 5.2 (-1 ± 20%) (n.s.) | 3.63 MB → 3.49 MB | 23.9 ± 0.976 | ×0.43 / ×0.22 |
| FilterShadows | 1000 | 55.8 → 55 (-1 ± 15%) (n.s.) | 59.6 → 58.1 (-2 ± 34%) (n.s.) | 6.43 MB → 6.28 MB | 46.7 ± 8.25 | ×1.18 / ×1.24 |

The FilterShadows render at 1000 pixels with the default threads is the one cell with a separated slowdown (+5.4 ± 3.8%). The document has no clip and no mask, so the clip changes do not run for it. Five more alternating rounds of the same pair gave −2.5 ± 7.9%, and five rounds of the parallel attempt alone −3.1 ± 14.9%; the eight rounds of the final pair together give +0.5 ± 5.3%, so it is not a slowdown. The clear does not show for the documents whose time the clear did not dominate at 1000 pixels (Tiger, Chart, IconSheet, PatternChart: −5% to −10%, not separated); at 4000 pixels, where one clear is 16 megapixels, it does (−20% to −32%). The documents with clip paths gain from the path clip bands: CssFeatures (basic shape clips on 48 cards) −68% to −75%, the Dashboard −18% to −40%. The render allocates the same as before except for the parallel change (one thread: Dashboard −0.35 MB, FilterShadows −0.15 MB, PatternChart −0.06 MB; with the default threads most loops still run in parallel and allocate as before).

Per attempt (each against the tree of the attempt before it, alternating rounds of `RenderSvg` with the corrected benchmark, mean ratio and 95% interval over the rounds; "default" is the default threads, "one" one thread):

| Attempt | Kept | Rounds and documents | Result | Allocation |
|---|---|---|---|---|
| Clear at full coverage | yes | 3 rounds, Icons and Tiger at 1000 and 4000 | Icons 1000 default −59.6 ± 10.0%, one −66.0 ± 1.7%; Icons 4000 default −80.2 ± 4.2%, one −82.5 ± 0.9%; Tiger 4000 default −28.6 ± 5.8%, one −28.8 ± 11.4%; Tiger 1000 default −5.7 ± 20.8%, one −11.4 ± 12.5% (not separated) | equal |
| Path clip bands | yes | 3 rounds, Dashboard, CssFeatures, FilterShadows at 1000 | CssFeatures default −63.7 ± 10.3%, one −67.0 ± 8.1%; Dashboard default −30.2 ± 28.1%, one −22.6 ± 16.2%; FilterShadows +10.8 ± 29.0% and +8.6 ± 62.1% (not separated) | equal |
| Run search in path clips | yes | 3 rounds, same documents | Dashboard one −3.3 ± 3.0%; the other five cells not separated (−10.5% to +7.9%) | equal |
| Image brush rows without wrapping copied as spans | no | 7 rounds Dashboard, 3 rounds the others, at 1000 | Dashboard default +1.1 ± 4.6%, one +3.5 ± 12.0%; nothing separated | equal; reverted |
| Minimum of work per parallel task | yes | 7 rounds Dashboard at 1000, 4 rounds at 100, 3 rounds the others | Dashboard 100 default −24.4 ± 21.7% (one −9.5 ± 19.8%, not separated); Dashboard 1000 default −2.5 ± 4.2%, one +4.4 ± 18.8% (not separated); CssFeatures and FilterShadows not separated | Dashboard one thread 4.18 → 3.83 MB (1000 px) and 1.87 → 1.53 MB (100 px); FilterShadows one thread 4.86 → 4.70 MB |

The reverted attempt replaced the per-pixel `TryWrap` loop of `ImageBrush` (1.6% and 1.9% of the Dashboard samples, in the write-back of the mask effects) with two span copies when the brush does not wrap horizontally. It kept the output equal but gave no measurable gain. The clear, the bands, and the parallel change also kept every pixel; the clear was the only change with a gain on documents without clips or layers.

**Default threads against one thread.** Rank 3 of 18.4 says that load and render of the Dashboard with the default threads is slower than with one thread; 18.9 found that this held for an under-warmed cell only. Measured again with the corrected benchmark (the ratio is default threads over one thread within each round, mean and 95% interval over the 3 rounds of the tables above):

| Dashboard | Long edge | Start commit | After |
|---|---|---|---|
| Load and render | 100 | 7.78 against 5.57 ms, +40.0 ± 14.5% (default slower) | 4.77 against 4.31 ms, +10.5 ± 20.7% (not separated) |
| Load and render | 1000 | 35.5 against 55.9 ms, −36.2 ± 13.0% (default faster) | 24.5 against 36.5 ms, −32.9 ± 11.5% (default faster) |
| Render | 1000 | 30.9 against 56.0 ms, −44.9 ± 11.8% | 23.0 against 33.5 ms, −31.4 ± 6.5% |
| Render | 4000 | 298 against 569 ms, −46.6 ± 19.8% | 246 against 444 ms, −44.6 ± 12.1% |

At 1000 and 4000 pixels the default threads are faster than one thread, before and after: the statement of rank 3 is not true for those sizes. At 100 pixels it was true at the start commit (40% slower), and it is not separated after the change: the loops of the many small segments now run on the calling thread (the render of the Dashboard at 100 pixels with the default threads: −24.4 ± 21.7% in the attempt table).

**Layers that the emitter can avoid.** None was found that keeps every pixel. The resolver already folds the opacity of a group with one draw into its paint, the opacity of a group with one filter into the filter result, and the opacity of a group into a normal layer that spans the group (`SvgResolver.cs`). The layers that remain carry a blend mode, a mask, a coverage clip (a layer and an alpha mask), or an opacity over several draws. Drawing such content directly differs: a layer quantizes its content to the pixel format before it composites, so a layer with one draw at opacity 1 and a direct draw differ in the low bits, and a coverage clip composites with `DestIn` where a geometry clip multiplies coverage. No change was made.

**The coverage restore of `ApplyCoverageRun` (a check, no change).** `ApplyCoverageRun` scales the borrowed coverage of a run by the clip multiplier `m`, passes it on, and divides it by `m` afterwards. In single precision `(c * m) / m` is not always `c`: for `c = k / 4096` and 327 multipliers (`k / 256`, fractions such as 0.3, and random values), 2.3% of the pairs come back one unit in the last place off, and 11.4% for random `c` (largest relative change 1.2e-7). Who reads the coverage after the restore: the area rasterizer writes the next span into its scanline before it reads it, and the stroke rasterizer reads only the columns after the run and clears its row before the next one, so no rasterizer reads it. The runs of a rectangle clip, a rectangle difference, a path clip, and an integer region partition their span, so they pass no pixel twice. The one reader is `ApplyRegionClip` for a floating-point region: it passes the whole span to each of its rectangles, so a pixel in the columns of two rectangles of the same row goes through the clip stack twice, and the second pass reads what the deeper clips restored. Such regions come only from an integer region under a scale, and they are hard-edged, so the region itself multiplies by exactly 1; a deeper antialiased clip in the stack can divide. Two rectangles share a row when the transformed boundary of two bands passes through the centre of the row, because the centre test of `GetAxisCoverage` accepts a centre on either edge (`>=` and `<=`). Then the pixel is drawn twice, which is a defect of its own: a region of the rectangles (0, 0, 20, 7) and (10, 7, 20, 7) under a scale of 1.5, filled with `#0000FF80` over white, gives (127, 127, 255) in rows 9 and 11 and (63, 63, 255) in row 10 at x = 20, where both rectangles cover the column. The restore error can change a pixel only inside that double draw, by one unit of the coverage. The full test suite, run once with an exact restore (a saved copy) and once with no restore at all, writes the same 2,242 images and the same `results.tsv` as the division, so no existing image depends on it. Neither the restore nor the double draw was changed.

**Non-SVG benchmarks.** `DrawBeziers`, `EllipseStressTest`, `FillRectangle` (both methods), and `FillWithPattern`, 15 warmup and 15 measured iterations, 3 alternating rounds of 4f25f0b and the final tree: Draw Beziers −0.9 ± 15.3%, ellipse stress −8.4 ± 15.1%, fill rectangle −4.3 ± 39.9%, fill rectangle as polygon +7.0 ± 22.2%, fill with pattern −2.9 ± 5.2%; none is separated, so none regressed. These benchmarks do not clear their image and use no clip or layer. `DrawPolygon`, `FillPolygon`, `FillParis`, and `FillTiger` still cannot run on Linux (19.1).

**Gap that remains to Svg.Skia.** With the clear at full coverage the Icons render is as fast as Svg.Skia at 1000 pixels with the default threads (×1.01) and faster at 4000 pixels (×0.21; one thread ×0.68); one thread at 1000 pixels is still ×2.7. PatternChart is faster than Svg.Skia with the default threads, and FilterShadows at 4000 pixels. The largest gaps of comparable documents at 1000 pixels are the Dashboard (×2.2 default threads, ×3.3 one thread; before ×3.0 and ×5.4), Chart (×1.35, ×2.8), Tiger (×1.1, ×2.4), and FilterShadows (×1.5, ×1.7). Where the time goes now (`perf`, one thread, 1000 pixels): in the Icons render, the clear still costs up to a quarter of the samples, but no longer in the blender: the rasterizer writes a coverage of 1 into every pixel of the rectangle (`BufferSpan` 5.8%, `EmitRows` 5.2%), the renderer searches it for runs of 1 (`IndexOf` 6.5%) and writes the pixels (`ApplySplit` 10.8%), about 0.3 ms per megapixel by these shares (an estimate; Skia about 0.17 ms). A clear of a whole-pixel rectangle could skip the coverage altogether. The rest of Icons is the blend of edge pixels and the scalar remainders of the row conversions (`ConvertFloatToByteRemainder` 9.8%, blend 9.5%). In the Dashboard, the scene render is 32 ms of 37 ms per load and render (45 ms before the path clip bands): gradients 11.9%, the blends and row conversions about 20%, the write-back of the mask effects through `ImageBrush` with `Src` (`Apply` and `TryWrap` 4.3%, plus its share of the blend), and the path clip 1.3% self. Not done in this package, and the next steps for layers: a write-back of a mask effect that copies the processed pixels where the coverage is 1 (the same proof as the clear, per pixel: `Src` at coverage 1 gives the source pixel for every alpha above 0 in 8-bit formats and zeros for alpha 0), a mask effect that runs on the layer rows in place instead of a copy into a new `Image` and a processor, worker-local layer buffers instead of an `Allocate2D` per layer band, and a composite loop that blends with one amount instead of a span of equal amounts.

**Test suite.** Release build of `src/`: 0 warnings. Full test suite: 6,568 passed, 153 skipped (6,508 before, plus the 60 new clear tests). Two of six full runs with the changes failed `SvgTextHostileDocumentTests.TrefFanOut_StopsAtTheCharacterLimit`, a timing test of the parser (47 ms grew to 226 ms, and 77 ms to 488 ms, against a limit of four times while the other test classes ran in parallel); the two full runs at the start commit passed it, and it passed in three runs of its class alone. The test only parses and does not draw, and no parser code changed; the cause is not verified (the 60 new tests add CPU load to the parallel run).

### 19.3 Filters and Off-screen Content

The numbers are from the corrected benchmark of 18.9 at commit 03b13e9 (the state after section 20).

**Profiles of the start tree.** `perf` (cpu-clock, 999 Hz) of `--profile svg` with 200 iterations at 1000 pixels on one thread; shares of all samples. FilterShadows (59.6 ms per load and render): emit (the filter engine) 33.6 ms, scene render 23.7 ms; self time in the bilinear sampling of `SvgTextureBrush` 10.9%, its `Index` 4.0% and `ToVector` 3.6%, the blur 15.9% (`Passes.Apply` 8.6%, the horizontal pass 7.3%), `PorterDuff` 4.1%, `SvgFeOffset` 3.7%, `SvgFilterColor.Convert` 2.6%. FilterBlur (64.2 ms): emit 37.1 ms, scene render 26.6 ms; `SvgTextureBrush.Renderer.Apply` 28.5% self (the sampling inlined), the blur 18%, the gradients of the source 6.4%; 67 generation 2 collections in 200 iterations. FilterChain (66.5 ms): emit 42.6 ms, scene render 23.4 ms; `SvgTextureBrush.Renderer.Apply` 23.4%, turbulence 10.2%. So the shares of 18.5 still held: drawing the results was a quarter to a third of the samples.

**What changed.** Five changes were kept. None of them changes a pixel: every reference image and expected result is unchanged, the 2,245 images that the test suite writes are byte for byte equal to those of the start commit (with the output directory emptied before the run, see the note on the test output below), and `results.tsv` of the resvg suite is equal line for line (1179 conformance passes).

1. **Results drawn on whole pixels** (`SvgTextureBrush`). A filter result under a transform that only scales by positive factors and translates, and an image or a tile placed at its own size on whole pixels, maps every device pixel center to a texel center: the inverse placement is a translation by whole texels. The renderer detects that once (M11 = M22 = 1, M12 = M21 = 0, integer offsets of at most 2^21) and then reads one texel row per scanline (`ApplyTranslated`) instead of sampling. Why the bytes are equal: with those coefficients and with pixel coordinates of at most 2^21, every sum that the sampling path forms is a multiple of one half below 2^23, so it is exact. Pixel `i` of a scanline maps to `u = x + i + 0.5 + M31` and `v = y + 0.5 + M32` exactly: a product with 1 is exact, a product with 0 is a signed zero that leaves a sum unchanged, and the step along `v` adds +0. Bilinear sampling subtracts one half and takes the floor, so both weights are exactly 0, and `Vector4.Lerp(a, b, 0)` returns `a` in every form of the interpolation (`a * (1 - 0) + b * 0`, `a + (b - a) * 0`, and their fused forms) for non-negative finite texels; nearest sampling floors `u` and `v` and finds the same texel. The index of an integer below 2^23 is its clamp, or its true modulo for a repeated tile: the float quotient `k / size` errs by less than half of `1 / size`, so its floor is exact. The fast path then forms the same expression, texel times opacity / 255, and converts with the same bulk call.
2. **Result buffers stay pooled** (`SvgFilterEngine.TryRender`, `SvgFilterImage.MoveToTexture`, `SvgTexture`, `DrawingCanvas.TryTakeOwnership`). The engine copied each result into a new `Rgba32[]`, because the canvas defers its commands and a retained scene keeps them. The result now keeps its buffer from the allocator of the configuration: the texture takes it over without a copy, and the emitter hands the texture to the canvas through a new internal virtual method of `DrawingCanvas` (no public API). `DrawingCanvas<TPixel>` adds it to the deferred resources that already hold the temporary images of `DrawImage` (the list changed from `List<Image<TPixel>>` to `List<IDisposable>`). The root canvas disposes them after the timeline has rendered, and `CreateScene` moves them to the scene, which disposes them with itself, so a buffer lives as long as a command that samples it can run. A canvas implementation that does not override the method returns `false`, and the emitter then draws from a managed copy, as before. Pattern tiles and decoded images stay managed arrays, because the document keeps them and needs no disposal. A retained scene now holds pooled memory until it is disposed; with an `AccumulativeAllocationLimitBytes` set on the allocator, its filter results count against that limit for that time (as the images of `DrawImage` already did).
3. **Alpha-only images** (`SvgFilterImage.IsAlphaOnly`, `SvgFilterPrimitive.PreservesAlphaOnly`). `SourceAlpha`, the alpha copy inside `feDropShadow`, and the results of blur, offset, merge, tile, morphology, and pass-through of such images have zero color channels, and the engine marks them. The blur then computes the alpha channel alone (`BoxAlpha`, `ConvolveAlpha`), the fractional offset mixes the alpha channel alone, and a color space conversion becomes a change of tag: an input in another color space gets a view with the tag of the primitive instead of a converted copy, and `SvgFilterColor.Convert` of such an image only tags it. This removes most of the cost of the gap noted in 16.6 for inputs that only carry alpha. Why the bytes are equal: a box sum of zeros is 0 and `(0 + size / 2) / size` is 0; a kernel sum of zeros plus 0.5 truncates to 0; the offset mix of zeros is `(0 + 32768) >> 16 = 0`; both conversion tables map 0 to 0, the straight value of a zero channel is `(0 + a / 2) / a = 0`, and 0 premultiplies to `(128 + 0) >> 8 = 0`. The alpha channel is computed with the same operations in the same order as in the four-channel loops; for the convolution that rests on the runtime evaluating a vector product and sum lane by lane with the rounding of the scalar product and sum (it does not fuse a multiply and an add that the code writes separately).
4. **Porter-Duff rows** (`SvgFeComposite`). The operator is chosen once per row instead of once per pixel, and `in` and `out` compute the product of A alone: the product of B with weight 0 is `(128 + 0) >> 8 = 0`, and a product of two bytes is at most 255, so the clamp does nothing.
5. **Repeated tiles** (`SvgTextureBrush.SampleBilinear`). For a repeated texture whose floors are at most 2^21 in magnitude, the four texel indexes come from an integer modulo of the floor and of its successor instead of four calls of `Index`, each with a float division. `Index` of such an integer is its true modulo (item 1), and `floor + 1` is exact. Rotated or scaled pattern tiles take this path; the hatched patterns of PatternChart are rotated.

**Tests.** `SvgPaintContentTests.DrawSvg_RetainedSceneMatchesDirectRender` covers the lifetime: it now also replays `filter-drop-shadow` and `filter-blur` (results on whole pixels) and `filter-transform` (a rotation) after the canvas that recorded the scene is disposed, after a full garbage collection, and after renting, filling, and returning buffers of every size up to the largest result. With the resource disposed when the canvas is disposed instead of moved to the scene (a temporary change), the replay fails with an out-of-range read of the returned buffer. `SvgFilterTests.Offset_OfSourceAlphaEqualsTheOffsetOfTheSamePixels` (two cases) compares a fractional offset of `SourceAlpha` with the same offset of black flood composited in `SourceAlpha`: no image of the suite went through the alpha-only offset (a change of one level in it changed no written image and no test), and the new test fails with that change. Other sensitivity checks, each a temporary change: the fast path of item 1 reading the next texel fails 111 tests (conformance 1161); the alpha-only box pass one level higher fails 4 SVG tests, the alpha-only convolution 4; the successor index of item 5 replaced by the same index fails 10 SVG tests (conformance 1178).

**Measured gain.** Alternating rounds of commit 03b13e9 (built from `git archive`) and the final tree (a copy of the work tree in its own directory), 5 warmup and 15 measured iterations per case, 3 rounds; the value is the mean of the round means in ms, then the mean of the per-round ratios with its 95% t interval over the rounds; "(n.s.)" marks a ratio whose interval includes 0. Allocation is the managed allocation of ImageSharp.Drawing per operation, as an absolute value.

Render of a loaded document (`RenderSvg`, fresh render):

| Document | Long edge | Default threads (ms) | One thread (ms) | Allocated, one thread |
|---|---|---|---|---|
| FilterShadows | 1000 | 60.5 → 36.3 (−39.4 ± 18.3%) | 54.9 → 30.4 (−44.1 ± 19.4%) | 4.70 MB → 2.61 MB |
| FilterShadows | 4000 | 437 → 247 (−43.5 ± 11.6%) | 636 → 302 (−52.5 ± 19.9%) | 37.91 MB → 4.93 MB |
| FilterBlur | 1000 | 33.2 → 22.9 (−30.9 ± 20.8%) | 63.8 → 44.8 (−29.5 ± 21.3%) | 3.18 MB → 324 KB |
| FilterBlur | 4000 | 525 → 424 (−19.2 ± 3.3%) | 1219 → 785 (−35.6 ± 2.0%) | 46.90 MB → 1.14 MB |
| FilterChain | 1000 | 40.8 → 30.3 (−25.6 ± 12.0%) | 76.8 → 47.6 (−37.2 ± 25.8%) | 2.84 MB → 376 KB |
| FilterChain | 4000 | 455 → 373 (−17.6 ± 19.0%) (n.s.) | 1106 → 826 (−25.2 ± 7.8%) | 40.77 MB → 1.41 MB |
| PatternChart | 1000 | 16.6 → 13.6 (−18.4 ± 12.1%) | 35.5 → 27.8 (−21.3 ± 14.5%) | 1.86 MB → 1.86 MB |
| PatternChart | 4000 | 142 → 111 (−21.3 ± 14.6%) | 379 → 319 (−15.7 ± 18.6%) (n.s.) | 5.06 MB → 5.06 MB |
| Tiger (control) | 1000 | 8.64 → 8.89 (+3.4 ± 34.8%) (n.s.) | 19.9 → 19.8 (+0.6 ± 58.9%) (n.s.) | 1.84 MB → 1.84 MB |
| Icons (control) | 1000 | 17.0 → 17.9 (+5.3 ± 14.0%) (n.s.) | 39.2 → 44.7 (+14.1 ± 33.1%) (n.s.) | 3.18 MB → 3.18 MB |

Replay of a retained scene (`ImageSharpRetainedScene`, 1000 pixels): FilterShadows 8.35 → 3.15 ms (−62.3 ± 7.0%), one thread 25.1 → 7.85 ms (−68.7 ± 2.7%); FilterBlur 8.66 → 2.99 ms (−65.4 ± 7.0%), one thread 27.1 → 7.01 ms (−74.2 ± 2.0%); FilterChain 7.87 → 2.42 ms (−69.2 ± 5.1%), one thread 22.8 → 5.61 ms (−75.5 ± 2.5%); 2 KB per replay before and after.

Load and render (`LoadAndRenderSvg`, 1000 pixels): FilterShadows 57.6 → 36.8 ms (−35.5 ± 29.4%), one thread 65.1 → 29.1 ms (−54.1 ± 22.3%), allocated 6.94 → 4.87 MB (one thread 6.28 → 4.19 MB); FilterBlur 30.0 → 21.6 ms (−27.2 ± 30.4%, n.s.), one thread 65.7 → 46.1 ms (−30.2 ± 22.7%), allocated 3.39 MB → 533 KB (one thread 3.36 MB → 509 KB); FilterChain 36.2 → 30.0 ms (−17.3 ± 18.5%, n.s.), one thread 69.8 → 45.9 ms (−34.2 ± 1.3%), allocated 3.12 MB → 658 KB (one thread 3.00 MB → 533 KB).

**Non-SVG benchmarks.** The change under `Processing/` (the internal method and the type of the list of deferred resources) runs for every canvas, so the non-SVG benchmarks of 19.1 ran too: 15 warmup and 15 measured iterations, 3 alternating rounds of 03b13e9 and the final tree. Draw Beziers −4.4 ± 2.9% (faster), ellipse stress −2.9 ± 23.5%, fill rectangle +7.8 ± 25.2% (0.178 → 0.192 ms), fill rectangle as polygon +9.4 ± 28.8%, fill with pattern +3.9 ± 11.1%; only Draw Beziers is separated, none is separated as slower. They draw no image, so the changed list is never used. `DrawPolygon`, `FillPolygon`, `FillParis`, and `FillTiger` still cannot run on Linux (19.1).

The controls use none of the changed code except the type of the list of deferred resources; their intervals hold 0, but they are wide (Icons on one thread ±33%), so a change of less than that is not excluded by this measurement.

**Garbage collections.** Stage profile, the same work per iteration (`gc-base.txt`, `gc-final.txt`; 100 iterations at 1000 pixels, 20 at 4000). At 1000 pixels FilterBlur and FilterChain now run no collection at all (before: one generation 2 collection every three renders); FilterShadows runs 4 generation 2 collections in 100 iterations as before (its 96 small results go to the shared array pool, which keeps only a few arrays of a size, so most of them are new arrays). At 4000 pixels FilterShadows went from 4 to 0 per 20 iterations, but FilterBlur still runs 4 per iteration and FilterChain 25 per iteration on one thread (before 9): every buffer above 4 MB comes from `UnmanagedMemoryAllocator`, outside the pool, and each such allocation calls `GC.AddMemoryPressure` (`UnmanagedMemoryHandle` of ImageSharp). The filter buffers at 4000 pixels are 16 to 64 MB, so those collections come from the size of the working buffers, not from managed allocation, and the result that now stays alive until the canvas renders adds to the pressure. The time still fell (table above).

**Pool trim.** `DefaultDrawingBackend.WorkerState.Gen2TrimSentinel` runs on every generation 2 collection and empties the worker pool when no worker state was rented since the previous one; at 4000 pixels that happens while a long primitive runs. A scratch build without the trim call ran FilterChain and FilterBlur at 4000 pixels (stage profile, 20 iterations, 3 alternating rounds against the final tree): no separation (FilterChain one thread 792, 838, 791 ms with the trim, 756, 804, 827 ms without), and the allocation fell only from 1434 to 1421 KB per iteration. The trim costs nothing measurable, so the core code was not changed.

**Per attempt.** "Before" is the tree of the attempt before (3 alternating rounds unless stated; mean ratio and 95% interval over the rounds), or a stage profile (200 iterations, one thread, one run each) where stated.

| Attempt | Kept | Measurement | Output |
|---|---|---|---|
| 1. Results on whole pixels | yes | retained scene −60.5 ± 8.5% to −75.6 ± 5.8% (all six cells separated); fresh render on one thread FilterBlur −22.8 ± 2.6%, FilterShadows −21.7 ± 15.8%, FilterChain −26.8 ± 27.8% (n.s.); default threads FilterBlur −19.7 ± 16.5%, the others n.s. Stage profile: scene render 23.7 → 7.9 ms (FilterShadows), 26.6 → 7.7 (FilterBlur), 23.4 → 6.9 (FilterChain) | equal |
| 2. Pooled result buffers | yes | allocation per fresh render: FilterBlur 3.30 MB → 369 KB (1000 px), 48.6 → 1.7 MB (4000 px); FilterChain 3.03 MB → 500 KB, 42.5 → 2.1 MB; FilterShadows 5.46 → 3.33 MB, 41.2 → 7.4 MB. Time, 2 rounds against the tree of attempt 1: FilterBlur default threads at 1000 px +10.1 ± 3.2% (separated, slower), FilterShadows one thread −3.4 ± 2.5% (faster), the other ten cells n.s. | equal |
| 3. Alpha-only images | yes | stage profile emit: FilterShadows 34.3 → 27.4 ms, FilterChain 53.2 → 45.9 ms | equal |
| 4. Porter-Duff rows | yes | stage profile emit: FilterShadows 27.4 → 21.8 ms, FilterChain 45.9 → 43.1 ms. Attempts 3 and 4 together, 3 rounds: FilterShadows default −23.9 ± 22.9%, one thread −21.3 ± 5.5%; FilterBlur and FilterChain n.s. | equal |
| 5. Repeated tiles | yes | stage profile scene render: PatternChart 30.2 → 26.5 ms (1000 px), 383 → 328 ms (4000 px); a document of 60 different rotated patterns at 4000 px 503 → 364 ms | equal |
| Pool trim removed | no | see above: no separation | — |

The slower cell of attempt 2 is not understood. FilterBlur on one thread at 1000 pixels gave stable but different times in different processes after attempt 2 (39.4, 41.6, 46.1, 48.8, 51.5, 52.9, 64.9, and 66.6 ms over eight processes, each with a small spread), while the tree of attempt 1 gave 40.6 to 49.5 ms with a large spread inside each process; `perf` of eight processes did not catch the slow mode. The tree of attempt 2 built in its own directory later gave 38.9 to 40.2 ms in three processes, and the work tree measured in place was noisier in every comparison, so part of the effect may come from the measured tree and not from the code. The final measurement against the start commit, where both trees are copies in their own directories, shows the cell faster (−30.9 ± 20.8% with the default threads, −29.5 ± 21.3% on one thread).

**Pattern tiles and their cache.** In PatternChart the tiles are tiny (8 user units at 5 pixels per unit at 4000 pixels: 40 × 40 texels, 6.4 KB), and their render is in the scene build stage (2.0 ms at 1000 pixels, 6.3 ms at 4000 pixels); the time is the sampling of the rotated tiles (36% self at 1000 pixels and 59% at 4000 pixels before attempt 5). A document of 60 different patterns, each a rotated 20-unit tile (`many-patterns.svg` of the package scratch directory), keeps 60 tiles of 25 × 25 texels at 1000 pixels (150 KB) and of 100 × 100 texels at 4000 pixels (2.4 MB) in its pattern cache. The cache holds at most 4 sizes per tile (`SvgPatternTile.CacheSize`), each bounded by `SvgLimits.MaxPatternTilePixels` (4096 × 4096 by default, 64 MB), plus the smaller levels when a tile is drawn smaller (a third more). The bound per tile is therefore large in theory (about 340 MB), but no measured document comes near it, so no bound is proposed.

**Gap to Svg.Skia after the work.** Svg.Skia ran once from the final tree (short job, 5 warmup and 15 measured iterations, 99.9% interval of BenchmarkDotNet), 1000 pixels. First draw (`ImageSharp` against `SvgSkia_ColdCaches`), default threads / one thread: FilterShadows 30.6 ± 1.2 ms, ×1.19 / ×0.99; FilterBlur 30.7 ± 3.4 ms, ×0.75 / ×1.46; FilterChain 60.8 ± 3.6 ms, ×0.50 / ×0.78. Repeated draw (`ImageSharpRetainedScene` against `SvgSkia`): FilterShadows 2.21 ± 0.25 ms, ×1.43 / ×3.55; FilterBlur 8.93 ± 1.09 ms, ×0.33 / ×0.79; FilterChain 2.51 ± 0.20 ms, ×0.96 / ×2.23. PatternChart (no filter; `SvgSkia` 27.0 ± 1.9 ms): ×0.50 / ×1.03. Svg.Skia is still faster for the first draw of FilterShadows with the default threads (the 96 small results run on the calling thread; on one thread the two are even), for FilterBlur on one thread, and for the replay on one thread. Where the time of a replay goes now (`perf`, FilterShadows, one thread, 8.3 ms): the texture brush 38.6% self (the row of texels to floats, the scale, and the bulk conversion to straight colors), and the scalar remainders of the row conversions of the blender 21% (rows of about 100 pixels). A replay converts the same texels to straight colors every time; a cache of the converted row per texture and opacity would remove that step with equal bytes (the conversion is a function of the texel alone), at the cost of a second buffer per result. It was not tried.

**Test output.** The test utilities overwrite an output image without truncating the file. After a run that wrote a larger image, a smaller correct image keeps the tail of the old file (6 bytes in one case), so the bytes differ while the decoded pixels are equal. The comparisons above emptied the output directory before each run.

### 19.4 Emit, Scene Build, and Text

The numbers are from the corrected benchmark of 18.9 at commit 5ad0230 (the state after 19.3).

**Profiles of the start tree.** `perf` (cpu-clock, 999 Hz) of `RenderSvg.ImageSharp_SingleThreaded` at 1000 pixels, BenchmarkDotNet in process (30 measured iterations), shares of all samples. The profiles of 18.5 were of load and render and from before Z2, Z3, Z5, and the sync; these are of the render of a loaded document, which is what a fresh render costs.

- TextHeavy (24.1 ms): the scene build 21% inclusive (`CreateRasterizableGeometry` 17.5%), the scene render 40%, the emit 3%. The largest self cost was the point loop of the linearizer, `Linearizer.ProcessContained`, at 14.7%: for every segment it read both endpoints through the segment enumerator of `LinearGeometry` (interface calls on the contour and point lists), transformed both with the 4x4 residual (`Vector4.Transform` and a division by W), and converted both to fixed point, so every point was transformed and converted twice. Then `ExecuteRasterizableItem` 11.9% (the band scan, the line rasterization inlined), `EmitCoverageRows` 3.9%, the row conversions of the blender 5%, the garbage collector about 5%.
- TextDecorations (23.5 ms): `ProcessContained` 12.2%, `ExecuteRasterizableItem` 9.6%.
- Chart (11.7 ms): `FillCoverageRowHandler.ApplyClipDescriptors` 8.3% self and 24.6% inclusive, `ApplyRectangleClip` 3.2%: the root viewport clip of the document (18.5) runs its rectangle tests for every span of every draw, although it changes only the last row. IconSheet (17.1 ms): `ApplyClipDescriptors` 7.1% self, 18.1% inclusive, from the viewport clips of its 400 `use` instances.
- Tiger (18.2 ms): the blend and the row conversions (20%), the band scan and the line walk; `ProcessContained` 3.6%.

Allocation (gc-verbose trace of `--profile svg`, load and render, TextHeavy, 1000 pixels, one thread; 13.1 MB per iteration): `LineArrayX16Y16Block` 21.9%, `DrawingCanvasState` 6.8% (two per glyph: `DrawingCanvas.Save(DrawingOptions)` pushed a state and replaced it with a second one), `SvgTextCluster[]` 6.5% and `SvgShapedGlyph[]` 2.2% (list growth in the shaper), `SvgRenderNode[]` 6.5%, `LinearizerX16Y16` 5.7%, the dictionary of transforms 4.6%, `PathCompositionSceneCommand` 3.5%, `FillSceneItem` 3.5%, `RasterizableBandInfo[]` 3.2%, `RasterizableGeometry` 2.9%, `int[]` 2.8% (two of the three arrays of each linearizer, one of them written and never read), `DrawingOptions` 2.2%, `DrawingOptions[]` 1.7% (the options table of the emitter, 150 KB for this document, a large-object allocation on every render), the line collectors and their arrays 2.2%. 67 generation 2 collections in 200 iterations.

**What changed.** Seven changes were kept. None of them changes a pixel: every reference image and expected result is unchanged, the 2,245 images that the test suite writes are byte for byte equal to those of the start commit (output directory emptied before each run), and `results.tsv` of the resvg suite is equal line for line (1179 conformance passes).

1. **The point loop of the linearizer** (`DefaultRasterizer.Linearizer.cs`, `ProcessContained`, `ProcessUncontained`, `LocalMapping`). The loops walk the contour and point spans of the geometry directly and map each point once: the end of one segment is the start of the next, and the closing segment of a closed contour ends at the mapped first point. The segments are those of `LinearGeometry.GetSegments()` in the same order, the profile tag of each segment is drawn in the same order, and the mapping of a point is a function of the point alone, so every segment gets the endpoints that a mapping per segment gives. A residual that only translates (unit diagonal; zero shear, perspective, and Z terms; `M44 = 1`) maps a finite point with one addition per axis. That is the value of `PointF.Transform(PointF, Matrix4x4)`: it computes `x * M11 + y * M21 + 0 * M31 + 1 * M41` and divides by `x * M14 + y * M24 + 0 * M34 + 1 * M44`; with these coefficients every product but the translation is exact (the coordinate itself or a signed zero), so in any order of evaluation, fused or not, the sum rounds once, to `x + M41`, and the divisor is exactly 1. A zero result can have the other sign, but the next step adds the whole-pixel translation and `-0 + 0` is `+0`, so the local value is the same. A coordinate that is not finite takes the general path, because its products with the zero coefficients are not zero. The glyphs of SVG text are placed by such residuals. A temporary change that adds a quarter pixel in the fast path makes 119 tests fail and changes 583 written images (conformance 1170), so the suite runs through it.
2. **Rectangle clips that cannot change a span are skipped** (`DefaultDrawingBackend.Helpers.cs`, `FillCoverageRowHandler.GetSkippedClips`). The row handler of one band of one item intersects the band target with the interest of the item, the rectangle that holds every span it passes on, and marks each intersecting rectangle clip among the first 64 descriptors whose interior rows and interior columns (`GetInteriorColumns`, the pixels whose axis coverage is exactly 1 by the comparisons of `GetAxisCoverage`) contain that rectangle. For a span inside that rectangle the clip computes a row coverage of exactly 1, starts its column walk at the start of the span (the first interior column is at least the floor of the left edge) and ends it at the end of the span (the last interior column is below the ceiling of the right edge), finds one run of axis coverage 1 over the whole span, and passes the same slices with the multiplier 1 * 1 = 1 to the next descriptor; `ApplyCoverageRun` scales and restores only for a multiplier other than 1. Skipping the descriptor makes the same call. `ApplyRegionClip` passes sub-spans of the span, which lie in the same rectangle. In the Chart profile `ApplyClipDescriptors` fell out of the top self costs (8.3% before); in IconSheet it fell from 7.1% to 4.5% (some icons touch the edge of their viewport).
3. **No line collector per band** (`DefaultRasterizer.RetainedTypes.cs`, the linearizers). A band kept a collector object with its front block and the count of lines in it, in a per-geometry array of collectors, and the finalize step copied both into the retained arrays. The linearizers now append straight into the retained arrays (`LineArrayX16Y16.AppendLine` and `LineArrayX32Y16.AppendLine` take the front block and the count by reference); an empty band has no block and a count of 0 where the collector had none and a count of 32, and both allocate the first block on the first line. The blocks, their order in the chain, and the lines in them are the same. The array of line counts per band that every linearizer allocated and incremented but never read is gone.
4. **One canvas state per `Save(DrawingOptions)`** (`DrawingCanvas{TPixel}.cs`). `Save(DrawingOptions)` pushed the state of `Save()` and replaced it with a state that carries the options; it now pushes that state directly: the same options, clip, bounds, destination offset, and layer, not a layer itself. The emitter saves once per change of transform, which is once per glyph.
5. **The options table of the emitter is rented** (`SvgEmitter.cs`). The table of drawing options per key (four slots per transform of the document) was a new array per render: 150 KB for TextHeavy, an allocation on the large-object heap. It is now rented from `ArrayPool<DrawingOptions?>.Shared`, emptied over its used length before use and before it is returned, and the key count is kept separately because a rented array can be longer.
6. **The lists of the shaper start at the character count** (`SvgTextShaper.cs`). Most characters make one cluster of one glyph; the arrays that `Shape` returns are copies of the lists as before.
7. **The coefficients of a parsed math function live in the term** (`SvgCalc.cs`). A term of the `calc()` parser held its slot coefficients in a separate `double[]`; they are an inline array of the term now. The arithmetic is the same; this removes one object per parsed value, not the coefficient bytes.

**Measured gain.** Alternating rounds of commit 5ad0230 (built from `git archive`) and the final tree (a copy of the work tree in its own directory), 5 warmup and 15 measured iterations per case; 3 rounds, 6 for Dashboard, Tiger, and Icons at 1000 pixels and for `LoadSvg`. The value is the mean of the round means in ms, then the mean of the per-round ratios with its 95% t interval over the rounds; "(n.s.)" marks a ratio whose interval includes 0. Allocation is the managed allocation of ImageSharp.Drawing per operation (one thread), as an absolute value. The container was noisier than in the earlier packages (load average about 2 with no process of this package running, and intervals of ±10% to ±50% for cells that did not change), so most cells are not separated.

Render of a loaded document (`RenderSvg`, fresh render):

| Document | Long edge | Default threads (ms) | One thread (ms) | Allocated, one thread |
|---|---|---|---|---|
| Chart | 1000 | 5.71 → 5.35 (-6.5 ± 15.1%) (n.s.) | 11.6 → 10.8 (-7.0 ± 13.9%) (n.s.) | 2.38 MB → 2.14 MB |
| Dashboard | 1000 | 20.5 → 22.1 (+7.4 ± 9.9%) (n.s.) | 33.7 → 33.9 (+1.2 ± 12.4%) (n.s.) | 3.83 MB → 3.66 MB |
| FilterShadows | 1000 | 35 → 31.5 (-10.3 ± 27.8%) (n.s.) | 26.8 → 25.9 (-3.2 ± 2.9%) | 2.61 MB → 2.47 MB |
| IconSheet | 1000 | 7.3 → 7.36 (+0.9 ± 9.2%) (n.s.) | 16.7 → 14.8 (-10.5 ± 22.9%) (n.s.) | 2.92 MB → 2.66 MB |
| Icons | 1000 | 16.6 → 15.5 (-6.1 ± 7.9%) (n.s.) | 39.7 → 39.8 (+0.2 ± 4.2%) (n.s.) | 3.18 MB → 2.93 MB |
| StreetMap | 1000 | 51.9 → 47.9 (-7.1 ± 32.8%) (n.s.) | 116 → 112 (-3.5 ± 4.9%) (n.s.) | 16.64 MB → 15.32 MB |
| TextDecorations | 1000 | 12 → 11.2 (-6.1 ± 25.4%) (n.s.) | 23.2 → 22 (-4.8 ± 6.4%) (n.s.) | 7.05 MB → 6.31 MB |
| TextHeavy | 100 | 7.49 → 4.38 (-41.1 ± 12.4%) | 11.1 → 7.28 (-34.3 ± 11.0%) | 6.32 MB → 5.42 MB |
| TextHeavy | 1000 | 14.6 → 10.1 (-30.7 ± 5.3%) | 26.4 → 19.9 (-24.4 ± 11.2%) | 8.04 MB → 7.05 MB |
| TextHeavy | 4000 | 33.7 → 27.8 (-17.4 ± 3.5%) | 79.2 → 76.2 (-3.8 ± 3.0%) | 12.40 MB → 11.13 MB |
| Tiger | 100 | 1.6 → 1.52 (-5.3 ± 1.7%) | 2.58 → 2.4 (-7.2 ± 4.9%) | 562 KB → 536 KB |
| Tiger | 1000 | 7.99 → 7.77 (-2.9 ± 6.4%) (n.s.) | 17.7 → 17.7 (+0.4 ± 8.6%) (n.s.) | 1.84 MB → 1.76 MB |
| Tiger | 4000 | 27.3 → 26.6 (-2.6 ± 16.1%) (n.s.) | 76.8 → 76.1 (-0.9 ± 4.8%) (n.s.) | 4.82 MB → 4.57 MB |

Replay of a retained scene (`ImageSharpRetainedScene`, 1000 pixels, 3 rounds): TextHeavy 4.69 → 4.33 ms (−7.4 ± 24.1%, n.s.), one thread 12.8 → 13.2 ms (+3.0 ± 11.3%, n.s.); Tiger 5.10 → 5.15 ms (+1.1 ± 3.3%, n.s.), one thread 13.7 → 13.8 ms (+1.0 ± 4.1%, n.s.); 2 to 5 KB per replay before and after. The changes are in the scene build and the emit, which a replay does not run.

Load and render (`LoadAndRenderSvg`, 1000 pixels):

| Document | Default threads (ms) | One thread (ms) | Allocated, one thread |
|---|---|---|---|
| CssFeatures | 23.9 → 23.2 (-2.9 ± 3.6%) (n.s.) | 31.2 → 31.9 (+2.2 ± 16.9%) (n.s.) | 17.10 MB → 16.12 MB |
| TextHeavy | 16.5 → 15.6 (-5.3 ± 10.1%) (n.s.) | 28.7 → 25.8 (-10.2 ± 11.7%) (n.s.) | 12.78 MB → 11.38 MB |
| Tiger | 9.72 → 9.65 (-0.7 ± 19.6%) (n.s.) | 19.3 → 19.6 (+1.5 ± 10.1%) (n.s.) | 4.01 MB → 3.94 MB |

Load (`LoadSvg`, 6 rounds): CssFeatures 12.6 → 13.1 ms (+4.6 ± 9.0%, n.s.), 11.96 → 11.40 MB; TextHeavy 3.54 → 3.36 ms (−4.9 ± 9.6%, n.s.), 4.56 → 4.14 MB; Tiger 1.29 → 1.39 ms (+7.4 ± 12.4%, n.s.), 789 KB before and after. The load of the Tiger has no text, and its allocation is equal to the byte, so none of the changed load code runs in it; its +7.4% is not separated and is not explained by a change.

The nested image document of 18.6 (the method of 18.6: `--profile svg`, 50 iterations at 100 pixels, three runs of each tree, alternating; means over the runs):

| | Base, one thread | Final, one thread | Base, default threads | Final, default threads |
|---|---|---|---|---|
| Load and draw | 175 ms (runs 168 to 180) | 130 ms (123 to 137) | 161 ms (147 to 183) | 126 ms (102 to 145) |
| Median iteration | 146 to 178 ms | 111 to 118 ms | 142 to 173 ms | 95 to 125 ms |
| Resolve | 36.4 ms, 50.2 MB | 35.3 ms, 50.2 MB | 42.1 ms | 34.8 ms |
| Emit | 54.6 ms, 47.1 MB | 45.8 ms, 35.6 MB | 52.7 ms | 47.7 ms |
| Scene build | 78.6 ms, 31.6 MB | 43.1 ms, 26.2 MB | 60.0 ms | 35.2 ms |
| Allocated per iteration | 129.3 MB | 112.3 MB | 129.3 MB | 112.3 MB |
| Generation 2 collections in 50 iterations | 36 to 41 | 32 to 50 | 37 to 40 | 34 to 47 |
| GC pause per iteration | 59 to 63 ms | 23 to 43 ms | 60 to 75 ms | 24 to 46 ms |

The emit allocates 11.5 MB less (one canvas state per glyph instance instead of two, and the rented options table instead of an array of 2.4 MB per draw), and the scene build 5.4 MB less (no collectors); the full collections still come every one or two iterations, because the node list and the transform table of the resolve stage (50 MB) are unchanged.

FilterShadows, the control, renders 3.2 ± 2.9% faster on one thread (separated) and is not separated with the default threads; it has no text, so only changes 1 to 5 run for it.

Per attempt (each against the tree of the attempt before, 3 alternating rounds of `RenderSvg` or `LoadSvg` at 1000 pixels, mean ratio and 95% interval; "default" is the default threads, "one" one thread):

| Attempt | Kept | Measurement | Output |
|---|---|---|---|
| 1. Linearizer point loop | yes | TextHeavy one −6.3 ± 12.9%, default −8.7 ± 15.1% (n.s.; two of three rounds after below every round before); Tiger, Chart n.s.; stage profile TextHeavy scene build 9.6 → 7.4 ms (three runs of 150 iterations, one thread); perf of TextHeavy `ProcessContained` 14.7% → 10.4% self | equal |
| 2. Rectangle clip skip | yes | Chart default −6.5 ± 17.7%, one −9.5 ± 52.0%; IconSheet −0.3 ± 11.2%, +1.1 ± 20.2% (all n.s.); perf: `ApplyClipDescriptors` self in Chart 8.3% → below the top 8 (about 1%), in IconSheet 7.1% → 4.5% | equal |
| 3 and 4. No collector per band, one state per `Save` | yes | TextHeavy one −15.3 ± 40.2%, default −2.1 ± 13.8%; TextDecorations −12.5 ± 30.1%, −10.4 ± 19.8%; Tiger n.s. (all n.s.); allocation TextHeavy 8.04 → 7.18 MB, TextDecorations 7.05 → 6.38 MB, Tiger 1.84 → 1.76 MB | equal |
| 5 and 6. Rented options table, presized shaper lists | yes | TextHeavy default −18.7 ± 5.2% (separated), one −7.4 ± 11.2% (n.s.); TextDecorations +2.5 ± 19.1%, +5.8 ± 21.9% (n.s.); `LoadSvg` time n.s.; allocation of the render TextHeavy 7.18 → 7.05 MB, of the load TextHeavy 4.56 → 4.14 MB, TextDecorations 3.79 → 3.39 MB | equal |
| 7. Inline calc coefficients | yes | CssFeatures `LoadSvg` +2.9 ± 60.9%, load and render one thread +0.8 ± 49.0% (n.s.); allocation 11.96 → 11.40 MB and 16.68 → 16.12 MB | equal |

**Non-SVG benchmarks.** `DrawBeziers`, `EllipseStressTest`, `FillRectangle` (both methods), and `FillWithPattern`, 15 warmup and 15 measured iterations, 6 alternating rounds of 5ad0230 and the final tree: Draw Beziers +1.9 ± 7.8%, ellipse stress +4.1 ± 16.5%, fill rectangle −8.0 ± 9.1%, fill rectangle as polygon +11.2 ± 17.1% (0.169 → 0.185 ms), fill with pattern −3.0 ± 3.7%; none is separated, so none regressed by this measurement, but a change below the width of the intervals (up to ±17%) is not excluded. They fill and stroke through the changed linearizer and the canvas `Save`. `DrawPolygon`, `FillPolygon`, `FillParis`, and `FillTiger` still cannot run on Linux (19.1).

**What a fresh render still allocates** (TextHeavy, 1000 pixels, one thread; gc-verbose trace of 120 iterations of `--profile svg`, frames under each stage). The scene build allocates 5.9 MB per render (6.3 MB at the start commit): line blocks 47% (2.8 MB; each block holds 32 lines, and every band of every glyph has at least one), `LinearizerX16Y16` objects 12.5%, `FillSceneItem` 7.6%, `RasterizableGeometry` 6.9%, `RasterizableBandInfo[]` 6.7%, `LinearizedRasterData` 5.1%, the per-band arrays of the retained geometry (start covers, front blocks, line counts) 5.6%, the row blocks of the scene 1.4%. The emit allocates 1.2 MB (1.9 MB at the start commit): `PathCompositionSceneCommand` 38% (one per glyph), `DrawingCanvasState` 32% (one per glyph), `DrawingOptions` 25% (one per glyph, because every glyph has its own transform). The scene render allocates 0.2 MB: one `FullCoverageSolidBrushRenderer` per item. All of it is one or a few objects per glyph; nothing grows with the number of renders. A replay of a retained scene allocates 2 to 5 KB.

**A cache of prepared scenes in the document (proposal, not built).** What a fresh render costs beyond a replay is the emit and the scene build, and both depend on the device transform, the caller's options, and the target, not only on the document. A document-level cache would hold the scene of the last render (or one per size) and replay it when those are equal. Its gain is the difference between the two existing methods: TextHeavy at 1000 pixels 10.1 → 4.33 ms with the default threads and 19.9 → 13.2 ms on one thread, Tiger 7.77 → 5.15 ms and 17.7 → 13.8 ms (the final tree; the retained-scene method of `RenderSvg` is the prototype, without the cost of comparing the key). Its memory is the retained scene, at most what a fresh render allocates: 7.05 MB for TextHeavy at 1000 pixels and 11.1 MB at 4000 pixels on one thread (the retained bytes were not measured separately; the allocation is an upper bound). It needs a key over the transform, the graphics options by value (the caller can change an options object after a render), the target size and pixel format, and the backend, thread safety for a document that renders on several canvases at once, and an eviction rule; and a retained scene holds pooled filter buffers (19.3) until it is disposed. The public API already offers the same gain to a caller who keeps the scene (`DrawingCanvas.CreateScene`), so no API is proposed.

**Gap to Svg.Skia after the work.** Svg.Skia ran once from the final tree (5 warmup and 15 measured iterations, 99.9% interval of BenchmarkDotNet). First draw (`ImageSharp` against `SvgSkia_ColdCaches`), 1000 pixels, default threads / one thread: TextHeavy 6.47 ± 0.20 ms, ×1.56 / ×3.07; TextDecorations 10.7 ± 0.9 ms, ×1.04 / ×2.06; Tiger 6.64 ± 0.04 ms, ×1.17 / ×2.67; Icons 12.7 ± 0.9 ms, ×1.22 / ×3.12; Chart 4.73 ± 0.11 ms, ×1.13 / ×2.28; Dashboard 9.66 ± 0.53 ms, ×2.29 / ×3.51; StreetMap 66.6 ± 4.1 ms, ×0.72 / ×1.68; IconSheet 6.28 ± 0.07 ms, ×1.17 / ×2.36; FilterShadows 29.0 ± 0.3 ms, ×1.09 / ×0.89. Repeated draw (`ImageSharpRetainedScene` against `SvgSkia`): TextHeavy 0.95 ± 0.09 ms, ×4.6 / ×13.9; Tiger 6.73 ± 0.22 ms, ×0.77 / ×2.05. At 100 and 4000 pixels only the replay of Svg.Skia ran: TextHeavy 0.43 and 14.8 ± 4.5 ms, Tiger 0.91 and 38.8 ms; the fresh render of ImageSharp.Drawing is ×10 / ×17 (TextHeavy 100), ×1.9 / ×5.1 (TextHeavy 4000), ×1.7 / ×2.6 (Tiger 100), and ×0.69 / ×2.0 (Tiger 4000) of those replays. Load: CssFeatures 29.1 ± 1.3 ms (×0.45), TextHeavy 74.2 ± 2.1 ms (×0.05), Tiger 4.52 ± 0.05 ms (×0.31). Load and render at 1000 pixels: CssFeatures 28.1 ± 0.9 ms, ×0.83 / ×1.14; TextHeavy 102 ± 5 ms, ×0.15 / ×0.25; Tiger 13.7 ± 1.7 ms, ×0.70 / ×1.43.

Where Svg.Skia is still faster for the first draw of text: the glyph masks that Skia caches across pictures are not a scene of the document (its cold-caches time for TextHeavy is 6.8 times its replay), and ImageSharp.Drawing linearizes and scans every glyph instance as a path. On one thread the scan of the glyph bands (`ExecuteRasterizableItem` 13.5% self) and the linearizer (10.4%) are now the largest costs of TextHeavy (perf of the tree after change 4); the per-glyph objects of the emit and the scene build are the rest.

**What was not done, and why.**

- *Prepared geometry of a glyph shared between its instances.* The flattened outline of a glyph at a scale is already shared: every instance of a glyph of one font and size draws the same `IPath`, whose `LinearGeometry` is cached per scale on the path (`DEFAULT_DRAWING_BACKEND.md`, "Retain raster geometry per partition"), and the scene build of the start tree did no curve flattening for text in a repeated render. What each instance still pays for is its linearization into fixed-point band lines, which depends on the fractional position of the instance: the fixed-point coordinates are `round(256 * (x + M41 + tx - minX))`, so two instances give the same band lines, shifted by whole pixels, only when their translations have the same fraction to the last bit and the same vertical position relative to the 16-row band grid. Glyph positions in laid-out text have arbitrary fractions, so such sharing would almost never apply with equal bytes; it was not built.
- *A run of glyphs with equal paint drawn as one fill.* A pixel that two glyphs cover gets one blend of the summed coverage instead of two blends, so a merged fill is equal only where no device pixel receives coverage from two glyphs (the condition of the plan). A second condition comes from the blender (19.1, 20.2): the solid renderer blends a span in pieces that start at multiples of the vector width of the blender, and the vector loop and the scalar loop round differently in the last bit, so a pixel can change when the span that holds it starts elsewhere. A merged fill emits one span where two glyphs touch without a gap, so the runs of the rasterizer also have to stay the same: there must be a pixel column of zero coverage between any two glyphs that share a row (sufficient: `ceil(right of A) + 1 <= left of B` in device space), and the residual of every glyph must be the same up to translation, with all coordinates non-negative so that the subtraction of the interest origin is exact. Under those conditions the band lines of each glyph in the merged item are its own lines shifted by whole pixels (the fixed-point rounding is round half to even, and the shift is a multiple of 256), and the coverage of each pixel comes from one glyph's cells only, because the cover of a closed contour sums to exactly zero to its right. Building it needs a fill command that carries several geometries with their own residuals through `FlushScene` and the linearizer (core code), and the gap condition removes most pairs at 100 pixels and many at 1000 pixels (side bearings of about one pixel). It was not attempted in this package; it is a proposal, with the conditions above and a test that compares the merged and the per-glyph scene byte for byte.
- *Per-node objects of the text layout.* The shaper lists now start at their usual final size (change 6). The node list and the transform table of the resolver grow by doubling; their final size is not known before the document is resolved, and the resolver edits the node list in place (decorations are inserted before the glyphs of their group), so a list with another growth policy would touch every user of it. Not changed.
- *The embedded SVG image drawn many times.* Changes 1 and 3 to 5 apply to each instance (see the measurement below). The structure stays: every instance is a copy of the nested nodes under its own transform, with its own clip. Drawing the nested nodes from the nested document under an instance transform, instead of copying them, would remove the node copies and the transform dictionary entries of the resolve stage (31% of the bytes in 18.6), but the emit and the scene build would still run once per glyph instance, the limits count instances, and the emitter would need a second level of transforms. It was not attempted.
- *Custom properties.* Change 7 is the only one. Caching the computed style of an element for the box walks, and caching substituted declarations, both change how often `var()` substitution runs, and every substitution draws from the substitution budget of the load (`SvgStyleContext.Budget`); a cache would let a document that exceeds the budget today load, so it needs a rule for what a cached result costs before it can keep the limit equal. The stroke box of `clip-path` basic shapes without building the stroke outline was not attempted: the box of a stroked curve with joins and caps needs the outline, or a proof that a cheaper bound gives the same float values.

## 20. Upstream Sync 1

The upstream owner answered the proposal on 27 November 2026 (discussion 435 of `SixLabors/ImageSharp.Drawing`), and the user decided to follow every request. Package U1 (`SVG_IMPLEMENTATION_UPSTREAM.md`) merged upstream `main` at `a4e94b8` into the branch (merge base `6f792ac`), moved the library to SixLabors.ImageSharp `5.0.0-alpha.0.38` (source commit `c6d486d7beecec05a8bc73b36cf4179c94fab82d`), replaced the gradient option of the branch with the one that upstream merged, removed the SVG decoder, made the option classes `init`-only, and uses `DrawingCanvas.Fill(Pen, IPath)` in the emitter where the output allows it.

### 20.1 What changed

- **Merge.** Six files conflicted. `WebGPUSceneEncoder.cs`, `EllipticGradientBrush.cs`, and `SweepGradientBrush.cs` take the upstream version (the branch had changed them only for its gradient option). `LinearGradientBrush.cs` and `RadialGradientBrush.cs` take the upstream version plus the row position overrides of 19.1 (`PositionsOnGradient`). `GradientBrush.cs` takes the upstream public part (`AlphaRepresentation`) and the renderer of 19.1, adapted: the stops are stored in the representation of the brush and go to the encoder of that representation without the associate-then-divide of the branch.
- **Gradient option.** `GradientAlphaInterpolation` and the `AlphaInterpolation` overloads are gone. SVG gradients use `PixelAlphaRepresentation.Unassociated`; the WebGPU encoder, the ramp code, the shader, and the pixel encoders are the upstream versions.
- **ImageSharp 5.** API changes met, and what the branch does:
  - `PixelOperations<TPixel>.GetPixelBlender` is `PixelBlenders<TPixel>.GetPixelBlender`; the solid brush compares the blender with the named instances `PixelBlenders<TPixel>.NormalSrcOver` and `NormalSrc`.
  - The pixel encoders of the gradients now name the representation of their input, not of the destination: `AssociatedGradientPixelEncoder` calls `TPixel.FromAssociatedScaledVector4`, `UnassociatedGradientPixelEncoder` `TPixel.FromUnassociatedScaledVector4`, and the brush selects one by its own representation. The internal texture brush of the SVG code (filter results, `image`, pattern tiles) chose the encoder by the destination, as the old encoders were meant; with the new ones its premultiplied texels were stored as straight colors (63 failing tests, 89 conformance tests lost in the first run). It now always calls `TPixel.FromAssociatedScaledVector4`, which divides by alpha for a straight-alpha format and keeps the channels for an associated one: the bytes of the old path.
  - The bulk conversion with `PixelConversionModifiers.Scale` of the gradient row is `FromAssociatedScaledVector4Destructive` or `FromUnassociatedScaledVector4Destructive`, after the encoder.
  - `DecoderOptions` takes a `Configuration`; `image` elements pass `Configuration.Default`, as before. Its default `SegmentIntegrityHandling` changed from `IgnoreAncillary` to `IgnoreImageData`, which returns a partly decoded image for damaged data; `image` elements set `IgnoreAncillary`, so a broken raster image still draws nothing.
  - `RgbaVector.FromScaledVector4` bounds only the alpha to [0, 1] (4.1.2 clamped every channel); `feBlend` clamps its unpremultiplied inputs itself.
  - The mask luminance effect converts its rows with `PixelConversionModifiers.UnPremultiply` (no change for straight formats; explicit for the associated formats of ImageSharp 5).
  - The PNG encoder writes other bytes for the same pixels.
- **Decoder removed.** Deleted: `SvgDecoder`, `SvgDecoderOptions`, `SvgFormat`, `SvgImageFormatDetector`, `SvgConfigurationModule` (public), `SvgShortDataFormatDetector` (internal), the tests `SvgDecoderTests`, `SvgDecoderHostileInputTests`, `SvgImageFormatDetectorTests`, and the benchmark `DecodeSvg`. `SvgDocument.Render`, the `MaxImagePixels` bound of a render at the intrinsic size, and the 64 MiB bound of `svgz` data in `image` elements stay. Tests of document behaviour that only the decoder tests covered were rewritten on `SvgDocument.Load` and `Render` (`SvgDocumentRenderTests`, `SvgDocumentHostileInputTests`): the render size rules, the bounds before allocation, UTF-16 input, truncated, malformed, and garbage input, long prologs, the layer limit, and the gzip limit (now through an `image` element).
- **Options.** Every property of `SvgLoadOptions` and `SvgLimits` is an `init` accessor with its validation. The same holds for `SvgGenericFontFamilies`, which the options refer to, so no value that a load reads can change after the options are created. Both classes have an explicit public parameterless constructor and an internal copy constructor; the options of an embedded SVG image are a copy of the outer options with the remaining limits and no resource directory.
- **Stroke as a fill.** See 20.3.
- **Texture brush.** The first measurement of the merged tree found FilterShadows slower on one thread (+6.4 ± 5.9%). A profile (`perf`, 300 iterations at 1000 pixels, one thread) put it in the texture brush that draws the filter results: scene render 29.2 → 37.7 ms, and the per-pixel `TPixel.FromAssociatedScaledVector4` of ImageSharp 5 runs `UnassociateOperator.Apply` and `Rgba32.Pack` as two calls that are not inlined (8.75% and 7.37% of the samples, against 8.84% for the inlined straight encoder of 4.1.2). For `Rgba32` the brush now samples the row into vectors and converts it with one `FromAssociatedScaledVector4Destructive`, which gives the same bytes (the proof of the gradient bulk encode in 20.2); the SVG outputs of the test suite are byte for byte equal. Three alternating rounds against the tree before this change: FilterShadows render on one thread −26.2 ± 7.8%; the render with the default threads (−14.2 ± 17.3%), load and render (−7.3%, −9.4%), and the Dashboard (−8% to +6%) are not separated.

### 20.2 Fast paths on ImageSharp 5

The fast paths of 19.1 and 19.2 that rest on how ImageSharp blends and converts were proven again on the source of `5.0.0-alpha.0.38`:

- **Full-coverage solid renderer** (`SolidBrush.cs`, `TryGetFullCoveragePixel` and `ApplySplit`). `PixelBlenders<TPixel>.GetPixelBlender` returns shared instances (`PixelBlender<TPixel, TOperator>.Instance`); for a format with straight alpha, `NormalSrcOver` and `NormalSrc` are the straight-alpha operators of `DefaultPixelBlenders`, so the reference comparison still selects exactly those equations. The equations (`PorterDuffFunctions.Over`, `Src`, and `BlendWithCoverage`, with the fused multiply-add of the coverage mix in every width) and `Numerics.UnPremultiply` (a division; zero alpha keeps the channels) are the same as in 4.1.2, so the error bounds of 19.1 and 19.2 hold unchanged. `PixelBlenders.BlendWithCoverage<TOperator>` (constant source) keeps the loop structure: a 512-bit loop over the first multiple of 4 pixels when a call has at least 4, otherwise a 256-bit loop over the first multiple of 2 when it has at least 2, then a scalar loop; so the split rule (pieces start at multiples of the group; a last piece of 2 or 3 pixels is avoided) still applies. The conversions give the same value in their vector and scalar parts and in the per-pixel path that a short row of a three-channel format takes below `Vector4ConversionThreshold`: an 8-bit channel reads as `byte / 255` (the vector path restores the correctly rounded quotient) and writes as `(byte)clamp(255 x + 0.5)`; a 16-bit channel writes with rounding half to even (`MathF.Round` and the vector path). The tests that compare the renderer with a blend of each whole row (`SolidBrushRendererTests`, fill and clear, ten formats) pass unchanged.
- **Gradient bulk encode** (`GradientBrush.cs`, `EncodesInBulk`). The per-pixel encoders call `TPixel.FromAssociatedScaledVector4` or `FromUnassociatedScaledVector4`. For `Rgba32` the second is `Pack` (scale by 255, add one half, clamp to [0, 255] with NaN to 0, truncate), and the first applies `UnassociateOperator` (alpha clamped to [0, MaxValue], a division by alpha, zero alpha keeps the channels, `ClampToPixelRange`) before `Pack`. The bulk forms are `FromUnassociatedScaledVector4Destructive` (the `Rgba32` override: `NormalizedFloatToByteSaturate`) and `FromAssociatedScaledVector4Destructive` (`Pack<UnassociateOperator>`: the same operator lane by lane in every width, then the same byte conversion). The byte conversion clamps to [0, 1] first (NaN to 0), scales, adds one half, truncates, and saturates, which gives the byte of `Pack` for every float. So the bulk path covers `Rgba32` with both representations (in 4.1.2 it covered the straight destination encoder, which every `Rgba32` gradient used). `GradientBrushRendererTests` passes unchanged in its expectations.

ImageSharp 5 does not make them unnecessary. Three alternating rounds of the work tree against a copy without the fast path (`RenderSvg`, mean ratio and 95% interval):

| Fast path | Document | Default threads | One thread |
|---|---|---|---|
| Full-coverage solid renderer | Icons 1000 | 53.1 → 16.4 ms (−69.2 ± 5.6%) | 168.3 → 40.8 ms (−75.7 ± 2.5%) |
| | Icons 4000 | 733 → 91.5 ms (−87.5 ± 2.2%) | 2491 → 284 ms (−88.6 ± 1.2%) |
| Gradient bulk encode | SoftIceCream 1000 | 7.87 → 5.85 ms (−25.5 ± 10.0%) | 16.8 → 14.3 ms (−14.7 ± 2.7%) |
| | StyledLandscape 1000 | 4.63 → 3.53 ms (−23.6 ± 9.0%) | 10.4 → 7.97 ms (−23.4 ± 7.5%) |

The other changes of 19.1 and 19.2 (rectangle clip interior, curve flattening, path clip bands, run search, parallel partitioning) work on coverage and geometry only and do not depend on ImageSharp.

### 20.3 `Fill(Pen, IPath)`

`DrawingCanvas.Fill(Pen, IPath)` fills `pen.GeneratePath(path)`, an outline built in local (user) space, under the drawing transform. That is the SVG stroke under a non-conformal transform, which the emitter built itself before. The emitter built its outline in a copy of user space scaled by the largest singular value of the transform, so that curves and round joins flatten at device precision. A probe document (a cubic path, a polyline, and a dashed circle with round caps and joins, and a miter path) under six non-conformal transforms compared the two methods pixel by pixel:

| Transform | Image | Differing pixels | Largest channel difference |
|---|---|---|---|
| matrix(20 0 0 2 0 0) | 1200 x 120 | 4,165 | 245 |
| matrix(2 0 0 20 0 0) | 120 x 1200 | 4,105 | 225 |
| matrix(50 0 0 5 0 0) | 3000 x 300 | 11,611 | 255 |
| matrix(8 0 6 8 0 0) | 900 x 480 | 3,543 | 236 |
| matrix(14.1 14.1 -1.41 1.41 400 10) | 1000 x 1000 | 4,295 | 247 |
| matrix(0.5 0 0 0.05 0 0) | 40 x 10 | 22 | 1 |

Under an enlarging transform the outline of `Fill(Pen, IPath)` shows its flattening: round caps and joins become visibly polygonal. Used for every non-conformal stroke, it fails the reference image `DrawSvg_Feature root-stretch` (0.0301%, one alpha level along the round joins of a 3 x 1.2 stretch); the resvg results stay equal. A transform that does not enlarge flattens at least as finely as the device needs (one level in 22 pixels of the probe). So the emitter calls `canvas.Fill(stroke, path)` when the largest scale of the transform is at most 1 and keeps its own outline when the transform enlarges; no image of the test suite changes. A reproduction for the upstream owner (document, both outputs, the table above) is with the package report; a fix would be a flattening scale for `Pen.GeneratePath`, or an outline built at the largest scale of the drawing transform inside `Fill(Pen, IPath)`. No core code was changed.

`vector-effect: non-scaling-stroke` keeps its outline in host space. A form with `Fill(pen, hostPath)` under a state with only the caller transform gave equal bytes for the test suite (whose non-scaling strokes are solid straight lines), but differed by one level in up to 117 pixels of the probe with curves, and the brush would need a copy transformed into host space (a rebuilt pen) to stay in user space.

### 20.4 Output

Every image that the test suite writes was compared with the images of the start commit `d7b70b3`: bytes first, then the decoded pixels of every PNG whose bytes differ. Of 2,242 images, 4 are byte-equal and 2,236 differ only in their PNG bytes, with equal pixels (the PNG encoder of ImageSharp 5); the other two belong to an upstream test whose image grew from 20 x 20 to 200 x 200 and was renamed. Four images of new upstream tests are new. No pixel of any existing image changed, so neither the gradient encoding of upstream nor the arithmetic of ImageSharp 5 changed an output of the test suite, and no reference image of the branch was made again. `results.tsv` of the resvg suite is equal line for line: 1179 of 1618.

### 20.5 Measured

The method of 18.9: alternating rounds of a tree built from the start commit `d7b70b3` (`git archive`, ImageSharp 4.1.2) and of the work tree (ImageSharp 5.0.0-alpha.0.38), each built in its own directory, 5 warmup and 15 measured iterations per case, 3 rounds; the value is the mean of the round means in ms, then the mean of the per-round ratios with its 95% t interval over the rounds; "(n.s.)" marks a ratio whose interval includes 0. Allocation is the managed allocation of ImageSharp.Drawing per operation, as an absolute value. The 1000-pixel render and the load and render cells ran against the final tree; the 4000-pixel cells and the non-SVG benchmarks ran against the tree before the texture brush change of 20.1, which they do not use.

Render of a loaded document (`RenderSvg`):

| Document | Long edge | Default threads (ms) | One thread (ms) | Allocated, one thread |
|---|---|---|---|---|
| Tiger | 1000 | 8.53 → 8.86 (+3.8 ± 9.7%) (n.s.) | 19.1 → 18.8 (−1.3 ± 15.4%) (n.s.) | 1.84 MB → 1.84 MB |
| Icons | 1000 | 18.0 → 18.1 (+0.5 ± 6.4%) (n.s.) | 45.9 → 42.1 (−8.5 ± 11.0%) (n.s.) | 3.18 MB → 3.18 MB |
| Moose | 1000 | 34.0 → 32.7 (−4.0 ± 25.3%) (n.s.) | 84.7 → 81.5 (−3.1 ± 20.1%) (n.s.) | 1.08 MB → 1.08 MB |
| StyledLandscape | 1000 | 4.35 → 4.25 (−1.4 ± 32.6%) (n.s.) | 10.1 → 10.0 (−1.5 ± 46.9%) (n.s.) | 284 KB → 284 KB |
| Dashboard | 1000 | 23.7 → 21.9 (−7.3 ± 15.2%) (n.s.) | 37.7 → 36.0 (−3.9 ± 33.0%) (n.s.) | 3.83 MB → 3.83 MB |
| FilterShadows | 1000 | 58.2 → 54.2 (−6.3 ± 34.8%) (n.s.) | 56.9 → 53.7 (−5.7 ± 8.1%) (n.s.) | 4.70 MB → 4.70 MB |
| TextHeavy | 1000 | 15.5 → 15.5 (−0.0 ± 15.9%) (n.s.) | 25.5 → 24.7 (−2.7 ± 29.6%) (n.s.) | 8.04 MB → 8.04 MB |
| Tiger | 4000 | 26.6 → 28.3 (+6.2 ± 21.3%) (n.s.) | 78.9 → 77.4 (−1.9 ± 6.5%) (n.s.) | 4.82 MB → 4.82 MB |
| Icons | 4000 | 88.5 → 85.7 (−3.1 ± 5.5%) (n.s.) | 285 → 273 (−4.2 ± 26.4%) (n.s.) | 12.22 MB → 12.22 MB |

Load and render (`LoadAndRenderSvg`):

| Document | Long edge | Default threads (ms) | One thread (ms) | Allocated, default threads | Allocated, one thread |
|---|---|---|---|---|---|
| Tiger | 1000 | 11.3 → 11.2 (−0.9 ± 5.2%) (n.s.) | 21.5 → 22.1 (+3.8 ± 34.6%) (n.s.) | 4.10 MB → 4.10 MB | 4.01 MB → 4.01 MB |
| FilterShadows | 1000 | 63.0 → 58.8 (−6.5 ± 13.9%) (n.s.) | 65.6 → 59.8 (−8.8 ± 12.1%) (n.s.) | 6.94 MB → 6.95 MB | 6.28 MB → 6.28 MB |

Non-SVG benchmarks of 19.1 (15 warmup and 15 measured iterations, 3 rounds): Draw Beziers −5.4 ± 2.8% (faster), ellipse stress −5.5 ± 9.7%, fill rectangle +16.1 ± 20.6% (0.165 → 0.192 ms), fill rectangle as polygon −8.9 ± 11.3%, fill with pattern −7.4 ± 7.7%; only Draw Beziers is separated. The fill rectangle case is the one cell with a large positive mean; it is not separated, it uses no SVG code, and its cost is the core fill and the blender of ImageSharp; it was not profiled.

No cell is separated as slower. The sync costs nothing measurable in time or managed allocation, once the texture brush converts in bulk; without that change FilterShadows on one thread was 6.4 ± 5.9% slower (20.1).

### 20.6 Tests

Release build: 0 warnings in the four projects under `src/` (the library, the WebGPU backend, the shader generator, the font generator); the test and benchmark projects keep the warnings that they had before the sync and add none. Full test suite: 6,513 passed, 155 skipped, 0 failed (before: 6,568 passed, 153 skipped). The 74 test cases of the decoder and of format detection are gone (three of them rewritten under new names, nine moved with their names into the new classes); 18 test cases came from upstream (the gradient alpha representation for every brush and its transform, `Fill(Pen, IPath)` with a self-intersecting stroke, and two WebGPU tests that skip without an adapter, as the other WebGPU tests do in the test container); the gradient tests of the branch were ported to `AlphaRepresentation` with their expectations, and two assertions on `WebGPUSceneEncoder.EvaluateGradientColor` were removed, because upstream changed what it returns (the sample in the representation of the brush; the ramp associates it later) and the upstream WebGPU tests compare the ramp output with the CPU. Tests that set options after construction now use object initializers; two assertions that a failed setter keeps the previous value went, because an `init` accessor that throws leaves no object. The timing test `TrefFanOut_StopsAtTheCharacterLimit` (19.2) now takes the best of three runs of each case; alone it never failed in ten runs, and four busy processes on the four cores stretched one run of the large case from 59 ms to 147 ms, which a single measurement cannot tell from growth. The bound is unchanged. The double draw of a float region clip (19.2) still occurs on the merged tree.

## Sources

- librsvg features: https://gnome.pages.gitlab.gnome.org/librsvg/devel-docs/features.html
- libvips `svgload`: https://www.libvips.org/API/current/ctor.Image.svgload.html
- Skia SVG module: https://github.com/google/skia/tree/main/modules/svg
- SkiaSharp `SkSVGDOM` binding request: https://github.com/mono/SkiaSharp/issues/2689
- Svg.Skia: https://github.com/wieslawsoltes/Svg.Skia
- resvg and its conformance chart: https://github.com/linebender/resvg
- resvg test suite: https://github.com/linebender/resvg-test-suite
- resvg support table: https://linebender.org/resvg-test-suite/svg-support-table.html
