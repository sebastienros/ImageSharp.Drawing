# SVG Rendering: Implementation Plan (Phase 5, Decoder and More SVG 2)

This document continues the earlier plans (`SVG_IMPLEMENTATION.md` and the phase 2, 3, and 4 plans). Read `SVG_DESIGN.md`, the earlier plans, and the repository `AGENTS.md` first. All their rules still apply: no `record` types, documentation on every member, simple technical English, Release builds, zero warnings in `src/`, no new package reference in `src/`, no existing reference image or expected result edited to silence a failure, no issue, no pull request, no commit, no push.

Do not read, download, or port source code of another renderer or browser engine. "Match Chromium" means: compare with rendered output from `tests/SvgTools/render-svg.js`. The allowed written sources are the specifications. Reading the public API and the source of SixLabors.ImageSharp and SixLabors.Fonts is allowed, because this repository builds on them; copying their internals is not (report a needed internal as a gap).

Where this plan says "verify", check the statement against the code, the specification, or the oracle before relying on it. When the code contradicts the plan, follow the code and record the difference in the hand-back report.

When a rendering choice is open: follow the specification where it settles the question, otherwise Chromium, then Skia and librsvg.

Phase 5 scope (`SVG_DESIGN.md` section 11): an ImageSharp decoder for SVG, and more SVG 2 and CSS features.

## 1. Selection Rule for Features

A feature enters phase 5 only if all three hold:

1. A specification defines it (SVG 2 or a CSS module that SVG 2 refers to).
2. The Chromium oracle renders it. A feature that no browser renders (auto-wrapped text with `inline-size`, mesh gradients, hatches, `miter-clip` and `arcs` joins, markers on basic shapes, text on basic shapes) stays out and is listed as a non-goal.
3. It does not work today. Each candidate below says "verify": some may already work, and then only a test is added.

## 2. Work Packages

| Package | Content | Depends on |
|---|---|---|
| Y1 | A wider property mask; parsing and computed values for every new property and attribute of this phase; the layer memory limit. | Phase 4 |
| Y2 | The ImageSharp decoder: format, detector, decoder, options, configuration module, and a method that renders a document to a new image. | Y1 |
| Y3 | CSS: custom properties and `var()`, geometry properties and `d` in style sheets, the CSS syntax of `transform`, CSS Color 4 syntax, the `ch` unit. | Y1 |
| Y4 | SVG 2: CSS basic shapes in `clip-path`, `pathLength`, and the smaller items of section 6. | Y1 |
| Y5 | Integration: cross-feature tests, conformance totals, hostile documents, benchmarks, document status. | Y2, Y3, Y4 |

Y2, Y3, and Y4 run at the same time in separate work trees. Y1 therefore adds every property id, attribute id, and parser that the three need, so that they only read computed values and add no enum member in a shared file. Each of Y2, Y3, and Y4 puts its work in new files and keeps edits to shared files short.

## 3. Package Y1: Foundation

### 3.1 Property mask

`SvgPropertyId` has 64 members and the specified-style masks are single `ulong` values (`SvgSpecifiedStyle`), so no property can be added. Widen the representation (for example two `ulong` words in a small struct) with room for at least 128 properties.

- The cascade is on the load path of every document. Measure `LoadSvg` for the whole benchmark corpus before and after, several rounds, and show that no document loads slower or allocates more beyond noise. If the first design costs time, try another before accepting a regression.
- Keep the split between the properties stored inline and the extended ones.

### 3.2 New properties and attributes

Add each with its inheritance and initial value, as a presentation attribute where SVG 2 defines one, in `style`, and in style sheets. Y1 parses and exposes; Y3 and Y4 apply.

- Geometry properties: `x`, `y`, `width`, `height`, `cx`, `cy`, `r`, `rx`, `ry` (with `auto` where SVG 2 allows it), and `d` with the `path()` function and `none`. The attribute stays the fast path: store the CSS value only when a declaration sets it, in the extended style, so documents without such declarations pay nothing.
- Custom properties (`--name`) and values that contain `var()`: keep the token text of a declaration with `var()` until the computed-value stage. Custom properties inherit. Y3 implements the substitution; Y1 provides the storage and the cascade order.
- `clip-path` values: basic shapes `inset()`, `circle()`, `ellipse()`, `polygon()`, `path()`, with an optional geometry box (`fill-box`, `stroke-box`, `view-box`). Parse into a value type; Y4 builds the geometry.
- `pathLength` on `path` and the basic shapes.
- `font-feature-settings`, `font-variant-ligatures`, `font-variant-caps`, `font-variant-numeric`, `font-variant-east-asian`, `font-variant-position`: only those that the oracle shows Chromium applies to SVG text (verify first; drop the others from the list and say so).
- Any other property that sections 5 and 6 need. Read them before you start.

### 3.3 Layer memory limit

Each mask, blend group, opacity group, coverage clip, and filter opens a layer of the size of its content, so memory grows with nesting depth times area (`SVG_DESIGN.md` section 14.3: 400 nested masks need about 1.8 GB at 1000 pixels). A decoder makes untrusted files the normal input, so this needs a bound.

- Add `SvgLimits.MaxLayerPixels`: the largest total number of pixels in layers that are open at the same time during one draw. Count filter working buffers with the layers that enclose them if that is practical; otherwise document that `MaxFilterPixels` bounds them apart.
- Check before drawing: one pass over the render nodes with the transform of the draw computes the largest total. A draw that would exceed the limit throws `InvalidImageContentException` before it changes the canvas. Verify which exception the other limits throw and stay consistent.
- Propose the default from measurements: every document of the benchmark corpus and of the conformance suite must draw at 4000 pixels on the long edge with a wide margin. Report the largest totals you measured.
- The pass must not slow down draws measurably (`RenderSvg` for the corpus, before and after).

## 4. Package Y2: Decoder

Decision: the decoder lives in the core package under `src/ImageSharp.Drawing/Svg/` and is not registered by default. A caller opts in with `configuration.Configure(new SvgConfigurationModule())`. Referencing the package therefore does not change format detection for anyone (`SVG_DESIGN.md`, open question 2).

Verify the decoder API of the referenced SixLabors.ImageSharp version (4.1.2): `IImageFormat`, `IImageFormatDetector`, `ImageDecoder`, `SpecializedImageDecoder<T>`, `ISpecializedDecoderOptions`, `IImageFormatConfigurationModule`, `ImageInfo`, `DecoderOptions` (`TargetSize`, `Configuration`, `SkipMetadata`). Follow the structure of the built-in formats.

- `SvgFormat`: name `SVG`, MIME type `image/svg+xml`, extensions `svg` and `svgz`. No encoder.
- `SvgImageFormatDetector`: recognizes UTF-8 with or without a byte order mark, UTF-16 with a byte order mark, and gzip content whose inflated start is SVG. It accepts a header whose first element is `svg` after an optional XML declaration, comments, processing instructions, and a `DOCTYPE`. Choose the header size from evidence (how far the root element starts in the corpus files and in editor exports with long prologs) and document the trade-off: a document whose root starts after the header window is not detected, and the caller passes the decoder explicitly. The detector must never throw and must not allocate per call beyond a small fixed amount.
- `SvgDecoder` with `SvgDecoderOptions`: the general options, the `SvgLoadOptions`, and a background color (default transparent).
  - `Identify` returns the intrinsic size without rendering: `SvgDocument.Size` rounded up to whole pixels, and the resolution from the load options.
  - `Decode` renders at the intrinsic size. With `DecoderOptions.TargetSize` it renders the vector content directly at the size that fits inside the target with the aspect ratio kept, as the built-in decoders resize (verify their resize mode and match it). It does not render at the intrinsic size and resample.
  - The output size is bounded: a document with a huge intrinsic size throws `InvalidImageContentException` before any allocation. Reuse `SvgLimits.MaxImagePixels` unless a separate limit is clearer; document the choice.
  - A document without a usable size (no `width`, `height`, or `viewBox`): follow what `SvgDocument.Size` gives today and document it.
  - Malformed input surfaces as the exception types that ImageSharp decoders use (`InvalidImageContentException`, `UnknownImageFormatException`); verify what `SvgDocument.Load` throws and map consistently. The cancellation token is checked between load and render.
  - Text uses the fonts of the load options (default: the system fonts). Image references follow the load options; the decoder never sets `ResourceBaseDirectory` by itself, also for `Image.Load(path)`.
- `SvgConfigurationModule` registers the format, the detector, and the decoder.
- `SvgDocument`: add a method that renders the document into a new `Image<TPixel>` at its intrinsic size or at a given size (`SVG_DESIGN.md` section 6 planned it). The decoder uses it.
- Tests: detection (each encoding, prolog forms, gzip, near misses such as HTML and other XML), `Identify`, `Decode` at the intrinsic and at target sizes against `DrawSvg` output, pixel formats, the limits, malformed and truncated input, streams that are not seekable, the async entry points, and that a default `Configuration` does not detect SVG.

## 5. Package Y3: CSS

- **Custom properties and `var()`.** Substitution at computed-value time with fallbacks, inheritance, the guaranteed-invalid value, and cycle detection, as CSS Custom Properties Level 1 defines. Bound the size of a substituted value (a chain of properties that doubles its length at each step must not allocate without limit). `var()` works in every property of the renderer and in presentation attributes only where Chromium accepts it (verify).
- **Geometry properties and `d`.** A CSS declaration wins over the attribute, as for other presentation attributes. `auto` and percentages resolve as SVG 2 defines. Shapes resolve from the computed value.
- **`transform` syntax.** In `style` and style sheets the CSS syntax applies: units on angles and lengths (`rotate(45deg)`, `translate(10px, 5px)`), and the functions Chromium accepts for SVG elements in two dimensions. The attribute keeps the SVG syntax. Verify what works today and what `transform-box` and `transform-origin` do.
- **CSS Color 4 syntax.** Space-separated `rgb()` and `hsl()` with a slash alpha, `#rgba` and `#rrggbbaa`, `hwb()`, and the other color functions only if the oracle shows that Chromium accepts them in SVG paint. Verify what exists; add what is missing.
- **`ch` unit.** Use the advance of "0" in the element's font, now that fonts exist (one suite test waits for it). `ex` likewise from the font's x-height if it still uses a fixed fraction (verify).
- **`calc()`** in lengths and numbers of properties, if the oracle shows Chromium accepts it in SVG presentation properties; otherwise list it as not supported.

## 6. Package Y4: SVG 2 Features

- **CSS basic shapes in `clip-path`.** Build the path from the shape and its reference box (the default is the object bounding box; `stroke-box` and `view-box` as defined), then clip as a geometry clip. Three suite tests wait for it.
- **`pathLength`.** It scales `stroke-dasharray` and `stroke-dashoffset` on the element, and distances along the path for `textPath` (`startOffset`) where SVG 2 says so. Zero and negative values: follow the specification.
- **Smaller items, each "verify, then implement or add a test":**
  - `orient="auto-start-reverse"` on markers.
  - `refX` and `refY` keywords on `symbol`; on `marker` they stay as Chromium renders them.
  - `paint-order`, `vector-effect`, and `mix-blend-mode` on text and inside `use`.
  - `image-rendering` values `pixelated` and `crisp-edges`.
  - `white-space` values of CSS Text 4 that Chromium applies to SVG text.
  - The font feature properties of section 3.2, through the shaping features that phase 3 uses.
  - The deprecated `clip` property on elements that establish a viewport, if Chromium renders it (one suite test).
  - `lang` and `xml:lang` for `systemLanguage` and the `:lang()` selector.

## 7. Evidence

As in the earlier phases:

- Unit tests for parsing and computed values; feature documents with reference images, each compared with the Chromium oracle before it is added.
- The conformance list: tests that start to pass are promoted only when they pass for the right reason; every remaining failure keeps a specific reason. Passes of phases 1 to 4 must not regress.
- Hostile input for each feature: `var()` cycles and expansion, huge `polygon()` lists, extreme `pathLength`, deep `calc()` nesting, gzip input that inflates without end, and a document that needs more layer memory than the limit.

## 8. Package Y5: Integration

- Cross-feature tests: geometry from CSS with markers, patterns, filters, and `use`; `var()` in paint, in geometry, and in filters; basic-shape clips with masks and filters; the decoder with text, images, and filters in one document.
- Conformance totals per category next to the earlier counts.
- Benchmarks: `Image.Load` of an SVG through the decoder against `SvgDocument.Load` plus `DrawSvg` (the decoder must add no measurable cost), and against the existing Svg.Skia and NetVips load-and-render numbers. Check that the corpus documents are not slower than at the phase 4 merge commit.
- Update `SVG_DESIGN.md`: status, the public API section, the feature table, open question 2 as decided, the limits, results, known gaps, and the list of features that stay out with the reason for each.
- Remove plan package names from source comments.

## 9. Not in Phase 5

These came up in earlier phases and need a decision of the user or a different kind of work:

- Changes to the core stroker for the five conformance tests it fails (zero-length dashes with caps, zero-length subpaths with butt caps, joins at curve ends with near-coincident control points). They change core output for every canvas user and may change existing reference images.
- The WebGPU backend skipping the internal texture brush (patterns, images, and filter results do not draw there). It cannot be verified without a GPU.
- Render performance and memory: planned as phase 6 (`SVG_IMPLEMENTATION_PHASE6.md`), which runs after this phase.
- Drawing text inside embedded SVG images (a decision of the user is pending).

## 10. Hand-back Report

As in `SVG_IMPLEMENTATION.md` section 9. Do not commit and do not push; the main session reviews, commits, and merges. A blocked action is reported, not worked around.
