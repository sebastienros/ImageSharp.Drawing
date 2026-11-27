# SVG Rendering: Implementation Plan (Upstream Sync 1)

This plan brings the SVG branch in line with the upstream repository after the reply of the upstream owner to the proposal (discussion 435 of `SixLabors/ImageSharp.Drawing`, 27 November 2026). It is one work package, U1. It runs after package Z3 of `SVG_IMPLEMENTATION_PHASE6.md` and before Z5, because every later measurement must use the new version of ImageSharp.

## 1. What Changed Upstream

The facts below were read from the upstream `main` branch at commit `a4e94b8` (the merge of pull request 436). Our branch forked at `6f792ac`; upstream `main` is one merge ahead of that commit.

- **ImageSharp 5.** `ImageSharp.Drawing.csproj` references `SixLabors.ImageSharp` `5.0.0-alpha.0.38` instead of `4.1.2`. The package is on the feed that `shared-infrastructure` already configures. Its source is commit `c6d486d7beecec05a8bc73b36cf4179c94fab82d` of `SixLabors/ImageSharp`. The upstream change shows these API differences; there can be more:
  - `PixelOperations<TPixel>.Instance.GetPixelBlender(options)` is now `PixelBlenders<TPixel>.GetPixelBlender(options)`.
  - `TPixel.FromScaledVector4` has the explicit forms `TPixel.FromAssociatedScaledVector4` and `TPixel.FromUnassociatedScaledVector4`.
  - The bulk conversions with `PixelConversionModifiers.Scale` have explicit forms such as `ToUnassociatedScaledVector4` and `FromUnassociatedScaledVector4Destructive`.
  - `DecoderOptions` takes a `Configuration` in its constructor.
- **Gradient alpha.** `GradientBrush` and `PathGradientBrush` have a property `AlphaRepresentation` of type `PixelAlphaRepresentation`, and every gradient brush has a constructor overload that takes it. `Associated` is the default and interpolates premultiplied colors. Every other value interpolates unassociated colors, as SVG does. The brush representation selects the pixel encoder. The WebGPU backend supports it.
- **Stroke as a fill.** `DrawingCanvas.Fill(Pen, IPath)` fills the outline that `Pen.GeneratePath` returns, with the brush of the pen and the non-zero rule. The drawing transform applies to the outline, so a non-uniform scale or a skew changes the stroke width as in SVG. There are overloads for `Rectangle`, `IPathCollection`, and `PathBuilder`. `Draw(Pen, IPath)` is unchanged.
- **Reference images.** Upstream added and changed reference images. Their Git LFS objects can be fetched through our fork: `git lfs fetch origin upstream/main --include "tests/Images/**"`.

The owner also set one requirement and answered the open questions of the proposal:

- **Requirement.** `SvgLoadOptions` and `SvgLimits` must use `init` accessors, not `set`, so that the options of a loaded document cannot change.
- **Package.** The SVG code stays in the existing package.
- **Gradient option.** Accepted, in the form that upstream merged.
- **Decoder.** The owner does not want an `.svg` image decoder: `Image.Load` for an SVG file would set expectations that SVG cannot meet. A user creates an image of the size that is needed and calls `DrawSvg`.

The user decided on 27 November 2026 to follow every request of the owner. So U1 also removes the decoder (2.6), and it uses `Fill(Pen, IPath)` in the emitter where the output allows it (2.4).

## 2. Package U1

### 2.1 Merge

- Merge `upstream/main` (`a4e94b8`) into the work tree with `git merge --no-commit --no-ff upstream/main`. Do not rebase. Leave the merge uncommitted; the main session commits it.
- Outside `src/ImageSharp.Drawing/Svg/` and the SVG tests, the upstream side wins where the two sides did the same thing in different ways. The performance work of packages Z2 and Z3 is kept and adapted (2.3).
- Six files conflict: the four gradient brushes, `GradientBrush.cs`, and `WebGPUSceneEncoder.cs`.

### 2.2 Gradient option

- Remove `GradientAlphaInterpolation`, the `AlphaInterpolation` property, and our constructor overloads. Use `AlphaRepresentation` of upstream everywhere: the brushes, the WebGPU encoder and shader, the SVG resolver, the tests, and the documents.
- Take the upstream version of the shader, of the WebGPU ramp code, and of the encoders in `IGradientPixelEncoder.cs`.
- Our implementation associated the interpolated color and the encoder then divided by alpha again. Upstream encodes the unassociated result directly. Low bits of SVG gradients on translucent stops can differ. Section 3 says how to handle that.

### 2.3 ImageSharp 5

- Make all projects build with zero warnings against `5.0.0-alpha.0.38`: the library, the WebGPU backend, the tests, and the benchmarks. Remove the decoder first (2.6), so that no time goes into a port of it.
- Clone the ImageSharp source at commit `c6d486d` into a new directory (`/home/user/sixlabors/imagesharp-5`); keep the 4.1.2 clone for comparison.
- **Prove the fast paths again.** Each fast path of Z2 and Z3 rests on how ImageSharp 4.1.2 blends and converts: the opaque solid fill and the full-coverage clear in `SolidBrush.cs` (the grouping of pixels in `BlendWithCoverageFunction`, the reference equality with the normal blender), the bulk row encode of gradients (`EncodesInBulk`), and every other direct write. For each one, read the new source, state whether the proof still holds, and adapt the code where it does not. The tests that compare a fast path with the general path must pass without a change to their expectations. If ImageSharp 5 makes a fast path unnecessary, measure and remove it.
- Where an SVG file used a scaled conversion with a modifier, choose the explicit associated or unassociated form that keeps the old meaning, and say which in the report. The filter engine and the layer effects need this care most.

### 2.4 Stroke as a fill

The emitter fills its own outline in two places: `SvgEmitter.StrokeNode` for a transform that is not conformal, and `SvgEmitter.StrokeNonScaling`. The owner suggests `canvas.Fill(pen, path)`.

- Our code for a transform that is not conformal builds the outline in a copy of user space scaled by the largest scale of the transform, so that curves and round joins flatten at device precision. `Fill(Pen, IPath)` builds the outline in user space. Test whether that loses precision when the transform enlarges: try `canvas.Fill(stroke, node.Path)` in `StrokeNode`, run the SVG tests and the conformance suite, and compare the output of a small document with a large non-uniform scale (round joins, round caps, curves) pixel by pixel between the two methods.
- The use of `Fill(pen, path)` is the intended result. If the output is equal, or every test passes with no visible loss, use it and delete our code. Only if precision is lost, keep our outline code for the cases that need it, and write a short reproduction (document, the two outputs, the largest difference) that the user can pass to the owner. Do not change core code to fix it.
- `StrokeNonScaling` builds its outline in host space, which `Fill(Pen, IPath)` cannot do. It stays, unless a form with `Fill(pen, hostPath)` under a host-space state gives the same pixels with the brush still in user space.

### 2.5 Option classes

- Every public setter of `SvgLoadOptions` and of `SvgLimits` becomes an `init` accessor. The validation in the accessors stays.
- Update every caller to an object initialiser: the library, the tests, the benchmarks, and the samples in the documents.
- Code inside the library that needs changed options from existing ones (for example a nested document) gets an internal copy constructor. Do not use a `record`.
- With `init`, the options cannot change after construction. Remove defensive copies of scalar values that existed only for that reason, and keep copies of collections that the caller can still change. Remove or rewrite tests that changed options after a load; say which in the report.

### 2.6 Decoder

- Delete `src/ImageSharp.Drawing/Svg/Decoder/` (`SvgDecoder`, `SvgDecoderOptions`, `SvgFormat`, `SvgConfigurationModule`, and the two format detectors), the tests `SvgDecoderTests`, `SvgDecoderHostileInputTests`, and `SvgImageFormatDetectorTests`, and the benchmark `DecodeSvg`.
- Before a test is deleted, read it. A test that checks the decoder only goes. A test that checks a behaviour of the document through the decoder (a limit, a hostile input, a size rule, a background) is rewritten to use `SvgDocument.Load` and `DrawSvg`, when no other test covers that behaviour. List in the report what was deleted and what was rewritten.
- `SvgNestedImageTextTests`, `SvgCrossFeatureHostileDocumentTests`, and `SvgLimits.cs` refer to the decoder: remove the references and keep the behaviour. An SVG file inside an `<image>` element must still render; it must load through `SvgDocument`, not through an image decoder.
- Remove members that only the decoder used (options, limits, internal helpers). A limit that also guards `DrawSvg` stays. Say in the report which public members went.
- The item "`Image.Identify` costs a full load" of package Z4 is gone with the decoder.

### 2.7 Documents

- `SVG_DESIGN.md`: the API sketch (section 6), the canvas mapping (section 7), the open questions (section 13: write the answers of the owner), and a new section 20 "Upstream Sync 1" with what changed, the results of 2.3 and 2.4, what 2.6 removed, and the numbers of 2.8. Every place that describes the decoder as a feature (sections 6, 11, and 17, the benchmark section, the known gaps) is changed: say that the decoder was built in phase 5 and removed at the request of the upstream owner, and keep the measured numbers of phase 5 as history.
- `SVG_IMPLEMENTATION_PHASE6.md` section 2: the source rule names version 4.1.2; change it to the new version and its source commit.
- `Processing/Backends/DEFAULT_DRAWING_BACKEND.md` where it describes a fast path that changed.

### 2.8 Measurement

ImageSharp 5 changes the code under every blend and conversion, so the numbers of sections 18 and 19 of `SVG_DESIGN.md` are for the old version. Measure what the sync changed, with the method of section 18.9 (alternating rounds of a tree built from the start commit against the work tree):

- `RenderSvg` on one thread and with default threads at 1000 pixels for Tiger, Icons, Moose, StyledLandscape, Dashboard, FilterShadows, and one text document, and at 4000 pixels for Tiger and Icons.
- `LoadAndRenderSvg` for Tiger and FilterShadows at 1000 pixels, with managed allocation as an absolute value.
- The non-SVG benchmarks that Z2 ran.

Report every cell with its interval. A slower cell is not hidden: find the cause with a profile and fix it if the cause is in our code; report it if the cause is in ImageSharp.

## 3. Rules

- The rules of `AGENTS.md` and of `SVG_IMPLEMENTATION_PHASE6.md` section 2 apply, except "same output" as changed here.
- **Output.** The goal is the same output as before the sync. Two causes can change pixels: the gradient encoding of upstream (2.2) and ImageSharp 5 itself. For every test that fails after the sync, find the cause first. A reference image of our branch may be made again only when the cause is one of these two, is shown (for example by the same difference in a build of `upstream/main` alone, or by a minimal case), and the largest channel difference is listed in the report. Keep the old image in the scratch directory for the review. Upstream reference images are taken from upstream and are never made again here. Every other difference is a bug to fix.
- **Conformance.** The resvg count stays at 1179 of 1618. Report every test that changes state, in both directions. Do not move a test that passed to the list of expected failures: the user decides that.
- **Sources.** Read the specifications, the ImageSharp source, and the upstream ImageSharp.Drawing source. Do not read the source of other renderers.
- Do not open issues or pull requests, and do not write to the upstream repository.
