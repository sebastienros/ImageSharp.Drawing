# SVG Rendering: Design Proposal

Status: accepted. Phases 0 to 3 are implemented; section 14 lists the phase 2 results and section 15 the phase 3 (text) results, each with its known gaps. The implementation plans are in `SVG_IMPLEMENTATION.md` (phases 0 and 1), `SVG_IMPLEMENTATION_PHASE2.md`, and `SVG_IMPLEMENTATION_PHASE3.md`.

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

"Phase" is the phase in section 11 where this proposal adds the feature. "Implemented" is the state after phase 3. "—" means not supported.

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
| `text`, `tspan`, anchors, font properties | Yes | Yes | Yes | 3 | Yes (glyph outlines at load; section 15) |
| `textPath` | — | Yes | Yes | 3 | Yes (`path` elements and the `path` attribute, as Chromium) |
| Per-glyph `x`/`y`/`dx`/`dy`/`rotate` lists | Single values only | Yes | Yes | 3 | Yes |
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
| Text-heavy document | Shaping and glyph drawing | `text-heavy-synthetic.svg` and `text-decorations-synthetic.svg`, synthetic |
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
- Layer memory: each nested mask, blend mode, opacity group, or coverage clip is a layer of the size of its content, so memory grows with nesting depth times layer area. 400 nested masks with coverage clips need about 1.8 GB at 1000 pixels; the nesting depth is bounded by `SvgLimits.MaxNestingDepth`, the layer area is not bounded.
- Pattern tiles render on first use per output size and stay cached in the document (four sizes per tile), so a document drawn at large sizes keeps the tile pixels alive. Tiles of patterns nested more than three levels deep get half the pixel budget per further level.
- Arcs in path data (`A` commands) and the core `RoundedRectanglePolygon` flatten their arcs once, in their own coordinate space, so they look polygonal when a small shape is scaled up. SVG rounded rectangles avoid this by using cubic Bézier corners.
- The deprecated CSS `clip` property, external style sheets, and network references are not supported; file references load only from `SvgLoadOptions.ResourceBaseDirectory`.

## 15. Phase 3 Results (Text)

### 15.1 What renders

Text resolves to glyph outlines when the document loads, as in resvg: each glyph is an ordinary draw node that shares one outline per font and glyph, so fill, stroke, paint servers, `paint-order`, clips, masks, opacity, and both backends work as for shapes. The layout follows Chromium where the specifications leave a choice.

| Feature | Implemented |
|---|---|
| `text`, `tspan`, `a` in text, position lists, `letter-spacing`, `word-spacing`, `text-anchor`, white space | Yes |
| Font matching (CSS weight, style, stretch; typographic families) and per-character fallback | Yes, from `SvgLoadOptions.FontCollection` only |
| `textPath` (`startOffset`, `side`; `path` elements and the `path` attribute) | Yes; `method` and `spacing` have no effect, as in Chromium |
| `textLength`, `lengthAdjust` | Yes, also in vertical and right-to-left text and on a path |
| `text-decoration` | Solid lines with the metrics of the primary font, as resvg; no other styles, no skip-ink |
| `dominant-baseline`, `alignment-baseline`, `baseline-shift` | Yes, across the line in every writing mode; the `BASE` table is not used, as in Chromium |
| `font-variant` (caps), `font-kerning`, `kerning` | Yes, through OpenType features; no synthesized small capitals |
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
- A way to turn off default features: `ShapeRun` only turns features on, so ligatures under `letter-spacing` are broken by shaping each grapheme again.
- `FontMetrics.TryGetBaselineCoordinate` (the `BASE` table) is internal. Chromium does not use the table either, so nothing renders differently today.
- The OS/2 width class: the font matcher reads `usWidthClass` from the raw table data for `font-stretch`.
- Family grouping by typographic family name (name ID 16): `FontCollection` groups faces by their legacy family name, so the font matcher also collects the families whose faces name the requested family as their typographic family.

### 15.5 Known gaps

- Decorations follow the font metrics (resvg): thickness, line-through, and overline position differ from Chromium, which uses a tenth of the font size; only solid lines; no skip-ink; on a path one band per glyph, where Chromium draws one curved line.
- Paint servers in bounding box units on upright glyphs of vertical text map the box in user space, as SVG defines; Chromium maps them in a turned frame.
- Colour glyphs (COLR, CBDT) draw monochrome; `text-rendering` has no effect; no synthesized small capitals; a `kerning` length adds no spacing.
- `textPath` references `path` elements only, as Chromium; `method` and `spacing` have no effect. The cross-axis direction of `x` and `dx` for `sideways-lr` text on a path was not checked against Chromium.
- Text in an SVG file that an `image` element references draws nothing (an open decision).
- Arabic letters do not join across a `tspan` boundary, and `letter-spacing` does not turn off Arabic ligatures (Chromium does both). These are limits of the public shaping API (section 15.4), not of the bidirectional algorithm; `SvgTextDirectionTests` holds a test of each.
- Bidirectional text costs time linear in its length: 200,000 bidirectional control characters with 100,000 letters load and draw in about 0.3 s (6 s before package T6, which also removed a quadratic search for the end of a shaping run), and 100,000 matched isolates or 100,000 numbers after an Arabic letter in about 0.3 s to 0.8 s (shared 4-core machine, noisy).
- WebGPU backend: not verified (no GPU in the test container).

## Sources

- librsvg features: https://gnome.pages.gitlab.gnome.org/librsvg/devel-docs/features.html
- libvips `svgload`: https://www.libvips.org/API/current/ctor.Image.svgload.html
- Skia SVG module: https://github.com/google/skia/tree/main/modules/svg
- SkiaSharp `SkSVGDOM` binding request: https://github.com/mono/SkiaSharp/issues/2689
- Svg.Skia: https://github.com/wieslawsoltes/Svg.Skia
- resvg and its conformance chart: https://github.com/linebender/resvg
- resvg test suite: https://github.com/linebender/resvg-test-suite
- resvg support table: https://linebender.org/resvg-test-suite/svg-support-table.html
