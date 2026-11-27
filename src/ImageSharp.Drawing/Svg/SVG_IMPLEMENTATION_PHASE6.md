# SVG Rendering: Implementation Plan (Phase 6, Performance and Memory)

This document continues the earlier plans. Read `SVG_DESIGN.md` (the results sections give the measured state per phase), the earlier plans, and the repository `AGENTS.md` first. All their rules still apply: no `record` types, documentation on every member, simple technical English, Release builds, zero warnings in `src/`, no new package reference in `src/`, no existing reference image or expected result edited to silence a failure, no issue, no pull request, no commit, no push.

`AGENTS.md` has a rule that this phase lives by: a speed-up is claimed only with a measurement. Every change in this phase carries numbers from before and after, on the same machine, with the error intervals. A change without a measured gain is reverted.

Goal, as the user set it: improve the scenarios where Svg.Skia or NetVips (librsvg) is faster than ImageSharp.Drawing, and the cases where memory allocation is a concern. The user suggested looking for optimization patterns in the ImageSharp repository.

## 1. What Is Known Before This Phase

From the results sections of `SVG_DESIGN.md` (short BenchmarkDotNet jobs on a shared 4-core container; verify each against the current tree before working on it):

- Loading is faster than Svg.Skia by a wide margin for every measured document (3 to 50 times). The earlier results sections also compare managed allocation between renderers; that comparison is not valid (section 2), and package Z6 rewords those sentences. Loading is not a target unless package Z1 finds a document where a reference renderer loads faster.
- Phase 5 did not make loading slower beyond noise: the phase 5 integration measured `LoadSvg` against commit 68408c9 over 14 alternating rounds and found a pooled change of +0.4% (95% interval −2.6% to +3.6%). `SVG_DESIGN.md` section 17.3 has the numbers. Loading of documents without the new features is not a target.
- The render signal that phase 5 left open is not real: the baseline ran ten alternating rounds against 68408c9 and found a pooled −0.3% (95% interval −4.3% to +3.8%). No fix is needed.
- Custom properties are slow to load: the CSS features document (56 KB, 48 cards) takes about 14 ms and 14.4 MB to load, about eight times the Tiger, because every element substitutes its `var()` declarations and parses the result again. It is a target of package Z4 (profile first: a cache of substituted values per declaration and set of custom property values is the first thing to try).
- `Image.Identify` through the SVG decoder costs a full load of the document (section 17.7 of `SVG_DESIGN.md`). It is a target of package Z4 if the profile of Z1 shows a cheaper path that keeps the result equal.
- Drawing an embedded SVG image many times is slow and varies much between runs: a document that draws ten thousand instances of a small nested document with one text line takes 90 to 360 ms on this machine. Package Z1 profiles it with the other targets.
- Rendering a loaded document on one thread is 2 to 10 times slower than Svg.Skia for the phase 1 documents (all but the large Paris map). The phase 1 profile put the time in the core: gradient brushes, the stroker, fill throughput, and one path object per draw command.
- The Dashboard document (about 240 layers from masks, blend modes, and coverage clips) renders about 7 times slower than Svg.Skia at 1000 pixels, and the multi-threaded render is slower than the single-threaded one.
- Text: rendering a loaded text document is 5 to 16 times slower than Svg.Skia replaying its picture. A fresh render allocates 6.6 to 8.5 MB; a retained scene allocates a few kilobytes.
- Filters: the engine came without optimisation (scalar loops, a strided vertical blur pass); the phase 4 integration package made a first pass. Its results section gives the state.
- Memory: layers are not pooled across draws; pattern tiles stay cached in the document; filter buffers reach hundreds of megabytes at the largest working area; nested layers were unbounded before `SvgLimits.MaxLayerPixels` (phase 5).

## 2. Rules for Every Package

- **Same output.** An optimisation must not change what is drawn. All reference images and the conformance results stay as they are. Where a faster method changes low bits (for example a different summation order), show the largest channel difference and that every existing test passes at its existing tolerance; do not loosen a tolerance.
- **Core changes serve every user.** Changes under `src/ImageSharp.Drawing/Processing/` and the path, brush, and stroker code affect all canvas users. Run the full test suite, and run the existing non-SVG benchmarks in `tests/ImageSharp.Drawing.Benchmarks` that touch the changed code before and after; none may regress.
- **Measure correctly.** One benchmark process at a time, nothing else running (no build, no test, no second agent). Short jobs to explore; a medium job (or more iterations) for the numbers that go in the report, so that the intervals separate. Record allocations with the memory diagnoser. The container can restart without warning: run each stage as its own command and save its result file at once.
- **Managed allocation is an absolute value, not a comparison.** Svg.Skia and NetVips are native libraries: most of their memory is outside the managed heap, so the allocation column of BenchmarkDotNet says little about them. This is a decision of the user. Report managed allocation for ImageSharp.Drawing only, as an absolute number and before and after a change. Do not rank renderers by it and do not write that one renderer "allocates less" than another from it. The only memory comparison between renderers is the peak memory of the process (section 4), and it is reported with how it was measured.
- **Fairness.** The comparison rules of `SVG_DESIGN.md` section 9.3 stay: same document, same output size, same fonts, output compared with the Chromium oracle before timings count, single-thread and multi-thread numbers both reported.
- **No new public API** unless the gain needs it; then report the proposal instead of adding it.
- **Sources.** Reading SixLabors.ImageSharp source for patterns is asked for by the user. Since package U1 the referenced version is `5.0.0-alpha.0.38` (verify in `ImageSharp.Drawing.csproj`); its source is commit `c6d486d7beecec05a8bc73b36cf4179c94fab82d` of `https://github.com/SixLabors/ImageSharp`, cloned into `/home/user/sixlabors/imagesharp-5` with `git init`, `GIT_LFS_SKIP_SMUDGE=1 git fetch --depth 1 https://github.com/SixLabors/ImageSharp c6d486d7beecec05a8bc73b36cf4179c94fab82d`, and `git checkout FETCH_HEAD`. The 4.1.2 clone (`/home/user/sixlabors/imagesharp`, tag v4.1.2) stays for comparison with the earlier packages. Use its public API; when a pattern needs an internal helper, write our own or report the gap, and do not copy files. Do not read source of other renderers (Skia, Svg.Skia, librsvg, resvg, Chromium); running Svg.Skia and NetVips as benchmark references is allowed.

## 3. Work Packages

The packages run one after the other, because each needs the machine alone for its measurements.

| Package | Content |
|---|---|
| Z1 | The baseline: the full comparison matrix, the ranked target list, the profile of each target, and the catalogue of ImageSharp patterns. No product code changes. |
| Z2 | Core fill and stroke path: brushes (gradients first), fill throughput, the stroker, path objects per command. |
| Z3 | Layers and compositing: the Dashboard case, layer buffers, the multi-thread slowdown. |
| Z4 | SVG emit, scene build, and text: per-glyph work, batching, allocations of a fresh render. |
| Z5 | Filters and off-screen content: what the profile still shows after phase 4, buffer reuse, pattern tiles. |
| Z6 | Memory close-out and final numbers: the allocation audit, peak memory, the final matrix, `SVG_DESIGN.md`. |

The order of Z2 to Z5 follows the ranked list of Z1: if Z1 shows that another area costs more, the main session reorders them.

After Z1 the order is Z2, Z3, Z5, Z4, Z6. `SVG_DESIGN.md` section 18 is the baseline: 18.4 ranks the targets, 18.5 and 18.6 hold the profiles, 18.7 the ImageSharp patterns, and 18.8 what each package tries first. Each package starts from those profiles and measures against the numbers of section 18.2. The load of custom properties and `Image.Identify` (18.6) belong to Z4.

Z2 is done; `SVG_DESIGN.md` section 19.1 holds its results. Two facts from it change what follows:

- The benchmark method of section 18.9 of `SVG_DESIGN.md` applies to every package: the setup warm-up of `RenderSvg` and `LoadAndRenderSvg`, `SvgSkia_ColdCaches` or `LoadAndRenderSvg` for a gap to Svg.Skia on a document with filters, and alternating rounds of a tree built from the start commit against the work tree for a before and after claim.
- `RenderSvg` clears the target area of each document with `canvas.Clear` before it draws, and that clear costs about 2.1 ms per megapixel on one thread (Svg.Skia: about 0.17 ms). It is three quarters of the Icons render time (40 documents, so 40 clears) and a fixed part of every cell at 4000 pixels. A direct write for a clear at full coverage is the first item of Z3, because a clear is a composite of a whole area, like a layer.

Z3 is done; `SVG_DESIGN.md` section 19.2 holds its results. It kept the clear at full coverage, the path clip bands, and a minimum of work per parallel task. It did not attempt layer buffers from a pool or the composite loop with one amount, and it found no layer that the emitter can avoid with equal pixels; these stay open for Z6. It also found a defect of the core that it did not change: a float region clip under a transform draws a row twice when a band boundary passes through a row centre.

**Upstream sync.** The upstream owner merged the gradient option and a stroke fill into `main`, moved the library to ImageSharp 5, and asked for `init` accessors on the option classes. `SVG_IMPLEMENTATION_UPSTREAM.md` holds the plan of that work as package U1. It runs after Z3 and before Z5, so the order is Z3, U1, Z5, Z4, Z6. The fast paths of Z2 and Z3 were proven against ImageSharp 4.1.2; U1 proves them again for the new version. U1 also removes the SVG decoder, as the owner asked, so the `Image.Identify` item of Z4 no longer exists.

U1 is done; `SVG_DESIGN.md` section 20 holds its results. The fast paths of Z2 and Z3 hold on ImageSharp 5 and still pay; no cell of the before and after measurement is separated as slower. Two facts for the packages that follow: the per-pixel `FromAssociatedScaledVector4` and `FromUnassociatedScaledVector4` of ImageSharp 5 are not inlined for `Rgba32`, so a row conversion in a hot loop should use the bulk forms (the texture brush and the gradients do); and `DrawingCanvas.Fill(Pen, IPath)` flattens in user space, so the emitter keeps its own outline for an enlarging non-conformal transform.

The profiles of Z1 used `perf` (`/usr/lib/linux-tools/6.8.0-151-generic/perf`, with `DOTNET_PerfMapEnabled=1` and `DOTNET_EnableWriteXorExecute=0`), because the thread-time sampler of `dotnet-trace` put 30% to 80% of the render samples at a GC poll where `perf` shows under 2%. The tool was installed with apt in the container and is gone after a restart; install it again when it is missing.

## 4. Package Z1: Baseline and Targets

- **Matrix.** For every document of `SvgBenchmarkCorpus` (phases 1 to 5) at 100, 1000, and 4000 pixels on the long edge: load, render of a loaded document, and load plus render, for ImageSharp.Drawing with all threads and with one thread, Svg.Skia, and NetVips. Time with error interval for all of them; managed allocation per operation for ImageSharp.Drawing only. Add documents if a common kind of SVG is missing (for example a large icon sheet, a map with many strokes, a chart with text); say why each was added.
- **Peak memory.** This is the memory comparison between renderers. Measure the peak working set of one load-and-render per renderer in a fresh process for the largest documents, and say how it was measured.
- **Targets.** List every cell where a reference renderer is faster with separated intervals, every cell where our peak memory is the largest of the three, and every cell where our managed allocation per operation is large in absolute terms (state the threshold, for example a multiple of the output image size, or any allocation that grows with the number of draws of a loaded document). Rank by the size of the gap and by how common the scenario is.
- **Profiles.** For each target: where the time goes (`--profile svg` stage timings, plus a sampling profile if a tool is available in the container; verify what is installed, do not install system packages without need) and where the allocations come from (allocation by type and call site from the memory diagnoser or an event trace).
- **ImageSharp pattern catalogue.** Read the ImageSharp source and list the patterns that apply to the targets, each with the file where ImageSharp uses it and the place in our code where it would apply. Candidates to look for (verify each in the source): row operations as structs with `ParallelRowIterator` instead of delegates; bulk pixel conversion through `PixelOperations<TPixel>` and its conversion modifiers; `Vector4`, `Vector<T>`, and `Vector128`/`Vector256`/`Vector512` paths with a scalar fallback; `MemoryAllocator`, `IMemoryOwner<T>`, and `Buffer2D<T>` instead of new arrays; bulk `PixelBlender<TPixel>` calls per row instead of per pixel; lookup tables for transfer functions; removal of bounds checks with `Unsafe` and `MemoryMarshal` where a loop is proven in range; `stackalloc` for small fixed buffers; avoiding closures, boxing, interface dispatch, and LINQ in loops. Also read `src/ImageSharp.Drawing/Processing/Backends/DEFAULT_RASTERIZER.MD` for the design rules of the rasterizer.
- Deliver the matrix, the ranked list, the profiles, and the catalogue in the hand-back report and as a new section of `SVG_DESIGN.md`. Propose the order of Z2 to Z5 and what each should attempt first.

## 5. Packages Z2 to Z5: Optimisation

Each package takes its targets from Z1, in order of expected gain, and works in this loop: profile, change one thing, run the tests that cover it, measure, keep or revert. The report lists every attempt, also the reverted ones, with numbers.

Starting points (to confirm with the profile, not to assume):

- **Z2, core fill and stroke.** Gradient brushes: a color ramp table per brush instead of a stop search per pixel, vectorized evaluation per row, premultiplied output written directly. Solid fills: the span fill and blend path per row. The stroker: allocations per segment, flattening tolerance work, reuse of buffers between draws. Path objects: what `DrawSvg` creates per node and whether geometry can be shared or prepared once per document.
- **Z3, layers.** Why the multi-threaded Dashboard render is slower than the single-threaded one (scheduling, contention, or memory bandwidth). Layer buffers from a pool and sized to their content. Layers that can be avoided: a mask or clip that reduces to a geometry clip, an opacity group with one child, a blend mode on a leaf. The composite loop per row.
- **Z4, emit and text.** The cost per glyph node in the emitter and in the scene build; drawing a run of glyphs with equal paint as one fill; sharing prepared geometry of a glyph between its instances; the allocations of a fresh render against the retained scene (6.6 to 8.5 MB against a few kilobytes) and which of them can be pooled or avoided.
- **Z5, filters and off-screen content.** The remaining scalar loops; alpha-only buffers for chains that start at `SourceAlpha`; buffer reuse across filtered elements in one draw; many small filtered elements (cost per element); pattern tile rendering and its cache policy.

## 6. Package Z6: Memory Close-out and Final Numbers

- Allocation audit of load and of a fresh render for each document: bytes per operation by type; remove or pool what is avoidable, and say what remains and why.
- Peak memory for the documents with layers, patterns, and filters, before and after this phase.
- Reword the sentences in the earlier results sections of `SVG_DESIGN.md` that compare managed allocation between renderers: keep our absolute numbers, drop the comparison.
- The final matrix in the same form as Z1, next to the Z1 numbers, with the gap to each reference renderer per cell. State plainly where a reference renderer is still faster, by how much, and what would be needed to close the gap.
- Update `SVG_DESIGN.md`: the performance sections of the phases, the pattern catalogue as applied, known gaps.

## 7. Hand-back Report

As in `SVG_IMPLEMENTATION.md` section 9, plus for each optimisation: the hot spot, the change, the benchmark command, the numbers before and after with intervals and allocation, the tests that cover it, and whether it was kept. Do not commit and do not push; the main session reviews, commits, and merges.
