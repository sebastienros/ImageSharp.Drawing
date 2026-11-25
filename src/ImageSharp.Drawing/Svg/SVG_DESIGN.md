# SVG Rendering: Design Proposal

Status: accepted. Phases 0, 1, and 2 are implemented; section 14 lists the phase 2 results and the known gaps. The implementation plans are in `SVG_IMPLEMENTATION.md` (phases 0 and 1) and `SVG_IMPLEMENTATION_PHASE2.md`.

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

"Phase" is the phase in section 11 where this proposal adds the feature. "Implemented" is the state after phase 2. "—" means not supported.

| Feature | librsvg (NetVips) | Skia `SkSVGDOM` | Svg.Skia | Phase | Implemented |
|---|---|---|---|---|---|
| `svg`, `g`, `defs`, `use` | Yes | Yes (`xlink:href` only) | Yes | 1 | Yes |
| `symbol` | Yes | — | Yes | 1 | Yes |
| `switch` | Partial | — | Yes | 2 | Yes |
| Nested `svg`, `viewBox`, `preserveAspectRatio` | Yes | Yes | Yes | 1 | Yes |
| Basic shapes and paths | Yes | Yes | Yes | 1 | Yes |
| Fill, stroke, dashes, caps, joins, miter limit | Yes | Yes | Yes | 1 | Yes (`miter-clip` and `arcs` fall back to `miter`) |
| `paint-order` | Yes | — | Partial | 2 | Yes |
| `vector-effect: non-scaling-stroke` | Paths only | — | Partial | 2 | Yes |
| Markers | Yes | — | Partial | 2 | Yes (path, line, polyline, polygon, as Chromium) |
| Linear and radial gradients, spread, transform, href | Yes | Yes | Yes | 1 | Yes |
| Patterns | Yes | Yes | Yes | 2 | Yes (CPU backend only) |
| `clipPath`, `clip-rule` | Partial | Yes | Partial | 2 | Yes (no CSS basic shapes) |
| `mask`, `mask-type` | Yes | Luminance only | Yes | 2 | Yes |
| `opacity`, `fill-opacity`, `stroke-opacity` | Yes | Yes | Yes | 1 | Yes |
| `mix-blend-mode`, `isolation` | Yes (style only) | — | Yes (style only) | 2 | Yes |
| Presentation attributes and inline `style` | Yes | Yes | Yes | 1 | Yes |
| `<style>` element with selectors | Yes (full selector engine) | — | Yes (ExCSS) | 1 (simple), 2 (full) | Yes (full selectors, `media`) |
| User stylesheet | Yes | — | Yes | 2 | Yes |
| `image` (raster, data URIs) | Yes | Yes | Yes | 2 | Yes (CPU backend only) |
| `image` (nested SVG file) | Yes | — | Yes | 2 | Yes |
| `text`, `tspan`, anchors, font properties | Yes | Yes | Yes | 3 | — |
| `textPath` | — | Yes | Yes | 3 | — |
| Per-glyph `x`/`y`/`dx`/`dy`/`rotate` lists | Single values only | Yes | Yes | 3 | — |
| Filters | 17 primitives and `feDropShadow` | 14 primitives | All primitives modelled, partial parity | 4 | — |
| CSS filter functions | Yes | — | Not stated | 4 | — |
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
- Store presentation properties in a compact struct with a "specified" bit mask. Do not use a dictionary per element.
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
    public float Dpi { get; set; } = 96F;
    public IReadOnlyFontCollection? Fonts { get; set; }
    public string? DefaultFontFamily { get; set; }
    public string? UserStylesheet { get; set; }
    public Color CurrentColor { get; set; } = Color.Black;

    // Null means data URIs only. No file or network access.
    public ISvgResourceResolver? ResourceResolver { get; set; }

    // Input limits. See section 10.
    public SvgLimits Limits { get; set; }
}

public abstract partial class DrawingCanvas
{
    // Draws the document at its intrinsic size.
    public void DrawSvg(SvgDocument document, PointF origin);

    // Fits the document viewport into the destination, following preserveAspectRatio.
    public void DrawSvg(SvgDocument document, RectangleF destination);
}
```

A convenience method that renders a document to a new `Image<TPixel>` at a requested size is a thin wrapper over `Paint(...)`.

An ImageSharp `IImageDecoder` for SVG, so `Image.Load("file.svg")` works and `DecoderOptions.TargetSize` selects the render size, is possible later. It matches how libvips handles thumbnails. It is listed as an open question because it changes format detection for every ImageSharp user who references the package.

## 7. Mapping SVG to the Canvas

| SVG | Canvas feature | Notes |
|---|---|---|
| `transform`, viewport | `DrawingOptions.Transform` | Composed at resolve time into one matrix per node. |
| `fill`, `fill-rule` | `Fill(brush, path)`, `DrawingOptions.IntersectionRule` | |
| `stroke-*` | `Draw(pen, path)`, `SolidPen`, `PatternPen`, `StrokeOptions` | `miter-clip` and `arcs` joins have no match and fall back to `miter`. |
| `linearGradient` | `LinearGradientBrush` | `gradientTransform` maps to `GradientTransform`. |
| `radialGradient` | `RadialGradientBrush` two-circle form | Covers `fx`, `fy`, and `fr`. |
| `spreadMethod` | `GradientRepetitionMode.None`, `Repeat`, `Reflect` | |
| `pattern` | Tile rendered to an image, then `ImageBrush` with `WrapMode.Repeat` | Tile resolution follows the output scale. |
| `opacity` | Brush alpha or `SaveLayer` with `BlendPercentage` | See 7.1. |
| `mix-blend-mode` | `SaveLayer` with `GraphicsOptions.ColorBlendingMode` | ImageSharp has all 16 CSS blend modes. |
| `clipPath` | `Clip(paths)` | Complex clips fall back to the mask path. |
| `mask` | Nested `SaveLayer` with `DestIn` and a luminance-to-alpha `LayerEffect` | Needs a prototype. |
| `feGaussianBlur`, `feDropShadow`, `feColorMatrix` | `BlurLayerEffect`, `DropShadowLayerEffect`, color matrix effect | Direct matches. |
| Other filter primitives | A filter-graph `LayerEffect` built on ImageSharp processors | CPU first. |
| `text`, `tspan`, `textPath` | SixLabors.Fonts shaping, `DrawGlyphs`, `TextBuilder` path overloads | |
| `image` | `DrawImage` | Nested SVG files load as a child `SvgDocument`. |

### 7.1 Group opacity without layers

A layer costs an extra buffer and a composite pass. Most SVG files use `opacity` on elements where a layer is not needed. The resolve stage picks the cheapest correct strategy:

1. Opacity on a leaf with only a fill, or only a stroke: multiply the brush alpha. No layer.
2. Opacity on a leaf with fill and stroke, or on a group whose children can overlap: use `SaveLayer` bounded to the node bounding box.
3. Blend mode, mask, or filter: always a bounded layer.

### 7.2 Known gaps to confirm with prototypes

These points came from reading the source before phase 1. The results after phase 2:

- Layer compositing with `PixelAlphaCompositionMode.DestIn` inside a parent layer, for masks: works on the CPU backend (`SvgMaskLayerPrototypeTests`). Masks use it with a luminance `LayerEffect`.
- A custom CPU `LayerEffect` on the WebGPU backend: not verified; the test container has no GPU.
- Per-path `clip-rule` when several clip paths combine in one `Clip` call: one call combines its paths with the non-zero rule, so a clip with several children, or with an even-odd child that is not a basic shape, draws as an alpha mask layer instead.
- Stroke bounding boxes for bounded layers: the resolve stage widens the bounds of stroked nodes itself.
- The cost of `Save`/`Restore` per node: not measured separately.

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
| Text-heavy document | Shaping and glyph drawing | To add in phase 3 |
| Filter-heavy document | Blur and filter graph | To add in phase 4 |

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
- **Buffers.** Limits on layer, pattern tile, and filter region sizes. Blur radius is clamped.
- **Numbers.** NaN, infinity, and overflow in lengths and transforms make the node invalid. They do not reach the rasterizer.
- **Fuzzing.** A fuzz target for `SvgDocument.Load` belongs in the test plan.

## 11. Phases

Each phase ships with conformance results and benchmark numbers.

| Phase | Content | Roughly comparable to |
|---|---|---|
| 0 | Agree scope and location upstream. Add the benchmark and conformance harness. | — |
| 1 | Structure (`svg`, `g`, `defs`, `use`, `symbol`), shapes, paths, fill and stroke properties, transforms, viewport, units, colors, opacity, gradients, presentation attributes, inline `style`, `<style>` with type, class, and id selectors. | Most icons and logos |
| 2 | `clipPath`, `mask`, `pattern`, markers, `image`, `switch`, blend modes, `paint-order`, full selectors, user stylesheet. | Beyond `SkSVGDOM` in CSS, markers, and blending |
| 3 | Text: `text`, `tspan`, `textPath`, anchors, font matching and fallback. | Text layout parity is the hardest area in every renderer |
| 4 | Filters: direct effects first, then the general filter graph. | librsvg static feature list |
| 5 | Optional: ImageSharp decoder, more SVG 2 features. | — |

## 12. Conformance Tests

- **Corpus.** The resvg test suite: about 1600 small SVG files with reference PNGs, MIT licensed, organized by structure, shapes, paint servers, painting, masking, text, and filters. Its README states that it exists to help other SVG libraries. The exact location of the reference PNGs and the licenses of the bundled fonts need a check before use.
- **Integration.** Keep the corpus out of the main tree (submodule or test-time download). The test images already use Git LFS.
- **Expected results.** A checked-in list records the expected status of every test: pass, or a known failure with a reason. A passing test that starts to fail breaks the build. A known failure that starts to pass must be promoted. This gives a pass count per category that can only go up, and it never hides a mismatch by editing a reference image.
- **Comparison.** Use the existing reference-image comparison in the test project with a tolerance. Anti-aliasing differs between rasterizers, so a strict pixel match is not the bar.
- **Second corpus.** The W3C SVG 1.1 test suite, later.

## 13. Open Questions

1. **Location.** Decided: the core package under `src/ImageSharp.Drawing/Svg/`, like `Barcodes/`. It adds no dependency and the assembly is trimmable.
2. **Decoder integration.** Deferred to phase 5. Should an SVG `IImageDecoder` exist, and in which package?
3. **Shared parser with Fonts.** Should the OpenType SVG glyph interpreter in SixLabors.Fonts and this feature share code?
4. **CSS depth for phase 1.** Simple selectors cover most design-tool exports. A full selector engine is a larger piece of work.
5. **Text fonts.** Which default font and fallback rules apply when the document names a font that is not installed?
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
- Layer memory: each nested mask, blend mode, opacity group, or coverage clip is a layer of the size of its content, so memory grows with nesting depth times layer area. 400 nested masks with coverage clips need about 1.8 GB at 1000 pixels; the nesting depth is bounded by `SvgLimits.MaxNestingDepth`, the layer area is not bounded.
- Pattern tiles render on first use per output size and stay cached in the document (four sizes per tile), so a document drawn at large sizes keeps the tile pixels alive. Tiles of patterns nested more than three levels deep get half the pixel budget per further level.
- Arcs in path data (`A` commands) and the core `RoundedRectanglePolygon` flatten their arcs once, in their own coordinate space, so they look polygonal when a small shape is scaled up. SVG rounded rectangles avoid this by using cubic Bézier corners.
- The deprecated CSS `clip` property, external style sheets, and network references are not supported; file references load only from `SvgLoadOptions.ResourceBaseDirectory`.

## Sources

- librsvg features: https://gnome.pages.gitlab.gnome.org/librsvg/devel-docs/features.html
- libvips `svgload`: https://www.libvips.org/API/current/ctor.Image.svgload.html
- Skia SVG module: https://github.com/google/skia/tree/main/modules/svg
- SkiaSharp `SkSVGDOM` binding request: https://github.com/mono/SkiaSharp/issues/2689
- Svg.Skia: https://github.com/wieslawsoltes/Svg.Skia
- resvg and its conformance chart: https://github.com/linebender/resvg
- resvg test suite: https://github.com/linebender/resvg-test-suite
- resvg support table: https://linebender.org/resvg-test-suite/svg-support-table.html
