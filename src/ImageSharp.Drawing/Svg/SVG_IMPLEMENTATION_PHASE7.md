# SVG Rendering: Implementation Plan (Phase 7, Performance Investigations)

Phase 6 ended with a list of improvements that were proposed and not built (`SVG_DESIGN.md` sections 19.3, 19.4, 19.5, 21.6, and 21.7). The user asked on 28 November 2026 to investigate them, to decide for each one whether it is worth keeping, and to work in packages as in the earlier phases. This plan defines the packages and the rule for that decision.

## 1. Rule for Keeping a Change

A change is kept only when all of these hold:

1. **Same output.** The images that the test suite writes are byte for byte equal to those of the start commit, the conformance count stays at 1179 of 1618, and `results.tsv` stays equal line for line. No reference image, tolerance, expected failure, or expectation of an existing test changes.
2. **A measured gain.** At least one target cell is faster, or allocates less, with separated intervals, in alternating rounds against the start commit.
3. **No measured loss.** No other measured cell is slower with separated intervals. A core change also runs the non-SVG benchmarks.
4. **Bounded cost.** Memory that the change keeps alive has a stated bound, and a test shows the bound holds. A change that keeps pooled or unmanaged memory alive needs a defined point where it is released.

A change that fails rule 1 is not kept, also when it is faster: the package reports the gain and the largest channel difference, and the user decides later. A change that passes rule 1 but fails rule 2 is reverted: code without a measured gain is a cost.

The package decides by this rule and says for each attempt which condition decided it. The main session checks the decision in its review.

## 2. Rules for Every Package

The rules of `SVG_IMPLEMENTATION_PHASE6.md` section 2 apply, with these changes:

- **Measurement has a budget.** The user decided that long benchmark runs give no more valid data on this machine: the spread between processes is larger than the interval inside one process. A before and after claim uses 4 alternating rounds of 3 warmup and 5 measured iterations (`SVG_DESIGN.md` 21.1 and 19.5 give the reasons), on the target cells and a few control cells only. Extra rounds are for a cell that a decision depends on and that is not separated after 4 rounds. Each package has a wall-time budget for all its measurement, stated in its brief; when the budget is used, the package decides with what it has and says what was not measured. The full comparison matrix is not run again.
- **Public API.** Phase 6 allowed none. Phase 7 allows the one option that the user named: a way to turn the scene cache of package R1 off. Any other new public member is reported as a proposal, not added.
- **Defects of upstream code** (ImageSharp.Drawing core, ImageSharp, Fonts, PolygonClipper) are written as issue texts for the fork, not fixed, unless the fix is part of a target. A finding that lets small untrusted input cost much time or memory is marked "report privately".

## 3. Packages

The packages run one after the other, because each needs the machine alone for its measurements.

| Package | Content | Main targets |
|---|---|---|
| R1 | A scene cache inside `SvgDocument`, with an option to turn it off | Repeated draws of one document: TextHeavy, TextDecorations, Tiger, Icons |
| R2 | Layers, masks, and filters: mask effects in place, independent filtered elements in parallel, a pool for filter buffers above the pool limit of ImageSharp, the converted rows of a texture for replays | Dashboard, FilterShadows, FilterBlur, FilterChain |
| R3 | The blend of a partly covered row without the round trip through `Vector4`, and text: one fill per glyph run, a glyph coverage cache | One-thread rendering of every document; TextHeavy, TextDecorations |
| R4 | The load of custom properties: the computed style of an element reused by the box walks, cached substitutions | CssFeatures load |

### 3.1 R1: Scene Cache

`DrawingCanvas.CreateScene` and `RenderScene` already let a caller record a draw and replay it; a replay of TextHeavy takes 4.3 ms where a fresh render takes 10.1 ms (19.4). R1 gives the same to a caller that only calls `DrawSvg` or `SvgDocument.Render` again.

- **What to find out first:** what a retained scene depends on (target bounds, pixel format, configuration and backend, transform, graphics options, the clip and layer state of the canvas at the draw), so that the cache key is complete; whether a scene can be replayed into a canvas that already holds commands, inside a clip or a layer, and inside a canvas that is itself recorded into a scene; whether one scene can be replayed by several threads at once; what a scene holds (managed memory, pooled memory, images) and how much, per document and size.
- **Policy.** A document that is drawn once must not pay: the first draw with a key runs as today, and the cache starts with a later draw with the same key (the package measures whether "the second draw" or another rule is right). The cache has a bound on entries and on bytes per document, and evicts.
- **Lifetime.** `SvgDocument` is immutable and not disposable. A cached scene must not keep pooled or unmanaged memory alive without a release point (19.3: a retained scene holds pooled filter results). If no safe form exists for scenes with such resources, they are not cached, and the package says so.
- **Threads.** Concurrent draws of one document stay correct and do not block each other for long.
- **Option.** One `init` property on `SvgLoadOptions` turns the cache off. The package proposes the default with its measurements; the user named an option to disable, so the expected default is on.
- **Tests.** Cached and fresh draws give equal bytes for every feature document, pixel format, and placement that the existing retained scene test covers, and for draws inside a clip, a transform, a layer, and a recorded scene; concurrent draws; the bound and the eviction; the option; a document with filters and with patterns and images.

**Result (R1, 28 November 2026): kept** (`SVG_DESIGN.md` 22.1). The second draw of a document with the same state records a scene with managed memory only and keeps it (at most 4 scenes and 64 MB per document); later draws replay it. `SvgLoadOptions.CacheScenes` (default on) turns it off. Draws in a clip, a layer, or a region, on another backend, or with an allocator that has an accumulative limit or that ImageSharp does not provide, draw directly. Two core changes: internal recording hooks on `DrawingCanvas`, and one pass for a range with retained scenes and other work on the CPU backend. No pixel changed; the repeated draw at 1000 pixels is 24% (Tiger) to 91% (FilterShadows) faster with separated intervals, TextHeavy −41%, TextDecorations −39%; Icons is not separated (−4.8 ± 19.6%); the first draw, the single draw, and the non-SVG benchmarks show no separated change.

### 3.2 R2: Layers, Masks, and Filters

- **Mask effects in place.** Each mask band creates a new `Image` and a processor for its luminance effect (`ExecuteApplyItem`, 19.2, 19.5). Run the effect on the layer rows directly.
- **Filtered elements in parallel.** FilterShadows renders 96 small filter results one after the other on the calling thread (19.3). Independent filtered elements can render at the same time; the results must be drawn in document order.
- **Filter buffers above 4 MiB** come from outside the pool of ImageSharp and cost 8% to 16% per pixel through page faults and generation 2 collections (19.5). Try buffers per band, or a small cache of large blocks owned by the engine, with a release point.
- **Replay of filter results** converts the same texels to straight colors every time (19.3). Try a cached converted buffer per texture and opacity, with its memory stated.

### 3.3 R3: Blend and Text

- **The blend.** `PixelBlender<TPixel>` converts the destination row to `Vector4` and back for every partly covered span (19.1, 21.6). Find out whether an integer blend can give the bytes of the float path for `Rgba32` and normal source-over; rule 1 decides. If it cannot, report the gain and the largest channel difference of the best variant, and keep nothing.
- **Text.** Z4 analysed one fill per glyph run and a shared glyph geometry and built neither (19.4). First measure how often the conditions for equal bytes hold in the text documents (glyph pairs without a shared pixel, glyph instances with equal fractional positions); build only what the measurement supports.

### 3.4 R4: Custom Properties

The computed style of an element is computed again for every box query of an ancestor (18.6), and each `var()` substitution draws from a budget (`SvgStyleContext.Budget`), so a cache changes which documents stay within the limit unless a cache hit charges the budget as a substitution does. Build the cache with that charge, so that every document that loads today loads with the same result and every document that fails today fails at the same point.

## 4. Hand-back Report

As in `SVG_IMPLEMENTATION_PHASE6.md` section 7. For each attempt: the hot spot, the change, the numbers before and after with intervals and allocation, whether the output stayed equal, and the keep or revert decision with the condition of section 1 that decided it. Then the wall time of the measurement stages against the budget. The package does not commit and does not push; the main session reviews, commits, and merges.
