# SVG Rendering: Implementation Plan (Phases 0 and 1)

This document turns `SVG_DESIGN.md` into work packages. Read `SVG_DESIGN.md` and the repository `AGENTS.md` first. `AGENTS.md` rules apply to every line: no `record` types, documentation on every member, simple technical English, Release builds, no reference image edited to silence a failure.

Accepted decisions:

- The feature lives in the core package: `src/ImageSharp.Drawing/Svg/`, namespace `SixLabors.ImageSharp.Drawing.Svg`.
- Phase 1 scope is the list in `SVG_DESIGN.md` section 11.
- No ImageSharp decoder in these phases.
- No new package reference in `src/`. Test and benchmark projects may add test-only packages.
- Work stays in this repository. Do not open issues or pull requests, and do not push.

Where this plan says "verify", the statement comes from reading source or from memory. Check it against the code or with a test before relying on it. When the code contradicts the plan, follow the code and record the difference in the hand-back report.

## 1. Work Packages

| Package | Content | Depends on |
|---|---|---|
| A | Value parsers, XML reader, element tree, CSS parser, cascade. Unit tests. | — |
| H | Benchmark harness for the reference renderers, benchmark corpus, conformance corpus fetch, independent render oracle. | — |
| B | Resolve stage, render model, public API, emit stage. Render tests. | A, H (oracle) |
| C | ImageSharp.Drawing benchmark methods, conformance runner and expected-results list, first measured results. | B, H |

## 2. Layout

```
src/ImageSharp.Drawing/Svg/
  SvgDocument.cs            public
  SvgLoadOptions.cs         public
  SvgLimits.cs              public
  Parsing/                  internal: value parsers, SvgReader, element tree
  Css/                      internal: stylesheet parser, selectors, cascade
  Resolve/                  internal: resolver, viewport, shapes, paint servers
  Rendering/                internal: render model, emitter
src/ImageSharp.Drawing/Processing/DrawingCanvas.Svg.cs    public DrawSvg overloads
tests/ImageSharp.Drawing.Tests/Svg/                       unit and render tests
tests/Images/Input/Svg/                                   input files
tests/ImageSharp.Drawing.Benchmarks/Svg/                  benchmarks
```

## 3. Public API (Package B)

```csharp
public sealed class SvgDocument
{
    public static SvgDocument Load(Stream stream);
    public static SvgDocument Load(Stream stream, SvgLoadOptions options);
    public static SvgDocument Load(string path);
    public static SvgDocument Load(string path, SvgLoadOptions options);
    public static SvgDocument Parse(ReadOnlySpan<char> svg);
    public static SvgDocument Parse(ReadOnlySpan<char> svg, SvgLoadOptions options);

    public SizeF Size { get; }           // Intrinsic size in pixels.
    public RectangleF? ViewBox { get; }
}

public sealed class SvgLoadOptions
{
    public float Dpi { get; set; } = 96F;
    public Color CurrentColor { get; set; } = Color.Black;
    public SvgLimits Limits { get; set; } = new();
}

public sealed class SvgLimits
{
    public int MaxElements { get; set; } = 1_000_000;
    public int MaxNodes { get; set; } = 1_000_000;        // Render nodes after use expansion.
    public int MaxNestingDepth { get; set; } = 512;
    public long MaxCharactersFromEntities { get; set; } = 1 << 20;
}

public abstract partial class DrawingCanvas
{
    public void DrawSvg(SvgDocument document, PointF origin);
    public void DrawSvg(SvgDocument document, RectangleF destination);
}
```

- `Fonts`, `UserStylesheet`, and `ResourceResolver` from the design sketch belong to later phases. Do not add them now.
- Malformed XML, a root element that is not `svg` in the SVG namespace, and an exceeded limit throw. Reuse the ImageSharp exception for invalid image content if its constructor is accessible (verify); otherwise raise the question in the hand-back report before adding a new exception type.
- An invalid attribute value never throws. Follow SVG error handling: the attribute counts as unspecified, and an element with invalid required geometry does not render.
- `SvgDocument` is immutable after load and safe for concurrent `DrawSvg` calls.

## 4. Package A: Parsing and Cascade

### 4.1 Value parsers

All parsers take `ReadOnlySpan<char>`, never throw, and report failure with a `bool`. All use invariant culture.

| Parser | Grammar notes |
|---|---|
| Number | Sign, digits, fraction, exponent. Must stop at the end of a number without a separator: `10-20`, `1.5.5`, `1e2e3`. Check the number scanner inside `Path.TryParseSvgPath`; share it if it fits. |
| Length | Number plus unit: none, `px`, `pt`, `pc`, `mm`, `cm`, `in`, `em`, `ex`, `%`. Keep value and unit; conversion happens in the resolve stage. |
| Color | `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`, the CSS named colors, `transparent`, `rgb()`, `rgba()`, `hsl()`, `hsla()` with comma or space syntax, percentages, and an alpha value. Case-insensitive. Use `Color.TryParse` for the forms it covers (verify which). |
| Paint | `none`, `currentColor`, a color, or `url(#id)` with an optional fallback (`none`, `currentColor`, or a color). |
| Transform list | `matrix`, `translate`, `scale`, `rotate(a [cx cy])`, `skewX`, `skewY`, in any number, separated by spaces or commas. The result is one `Matrix3x2`. The first transform in the list is the outermost. An invalid list makes the attribute unspecified. |
| `viewBox` | Four numbers. Negative or zero width or height disables the viewBox. |
| `preserveAspectRatio` | `none` or `x(Min|Mid|Max)Y(Min|Mid|Max)`, then optional `meet` or `slice`. |
| Number and length lists | For `points` and `stroke-dasharray`. An odd count in `points` drops the last value. |

### 4.2 XML reader

- `XmlReader` over the input, forward only. Settings: `DtdProcessing.Parse`, `XmlResolver = null`, `MaxCharactersFromEntities` from the limits, comments and processing instructions ignored, input not closed.
- Pre-seed the `NameTable` with every known element and attribute name. Map names to `SvgElementKind` and attribute ids by reference comparison.
- Recognize SVG-namespace elements only. Keep unknown elements as `Unknown` nodes so selectors and structure still work; they do not render and their children do not render.
- Accept `href` and `xlink:href`. `href` wins when both exist.
- Enforce `MaxElements` and `MaxNestingDepth` while reading. Do not recurse on the XML depth without the depth limit.

### 4.3 Element tree

A flat store in document order. Avoid one heap object per element.

- Per element: kind, parent index, subtree end index, `id`, the class list, and indexes into side tables for geometry attributes and style.
- Geometry and reference attributes are parsed eagerly into typed fields per element kind (for example `x`, `y`, `width`, `height`, `rx`, `ry` as lengths; `d` as an `IPath`; `points` as a point array; gradient attributes; `transform`; `viewBox`).
- Parse `d` with `Path.TryParseSvgPath`. SVG requires rendering a path up to the first error. Check what `TryParseSvgPath` does on a late error. If it rejects the whole path, record that as a known gap; do not write a second path parser.
- An `id` index (dictionary from id to element index). The first element with a given id wins.

### 4.4 Style properties

Phase 1 properties:

| Property | Inherited | Initial |
|---|---|---|
| `color` | Yes | `SvgLoadOptions.CurrentColor` |
| `display` | No | `inline` |
| `visibility` | Yes | `visible` |
| `opacity` | No | `1` |
| `fill` | Yes | `black` |
| `fill-opacity` | Yes | `1` |
| `fill-rule` | Yes | `nonzero` |
| `stroke` | Yes | `none` |
| `stroke-opacity` | Yes | `1` |
| `stroke-width` | Yes | `1` |
| `stroke-linecap` | Yes | `butt` |
| `stroke-linejoin` | Yes | `miter` |
| `stroke-miterlimit` | Yes | `4` |
| `stroke-dasharray` | Yes | `none` |
| `stroke-dashoffset` | Yes | `0` |
| `font-size` | Yes | `16px` (needed for `em` and `ex` only) |
| `stop-color` | No | `black` |
| `stop-opacity` | No | `1` |

- Store specified values in a typed struct with a "specified" bit mask and an "inherit keyword" bit mask. No dictionary per element. Keep the struct in a side table so elements without style cost nothing.
- Unknown properties and invalid values are dropped at parse time.
- Design the property list so later phases can add `clip-path`, `mask`, `filter`, markers, and font properties without a rewrite.

### 4.5 CSS

- Sources: every `<style>` element (text and CDATA), in document order. An absent `type` or `type="text/css"` only.
- Stylesheet grammar: comments, rule sets, declaration blocks, `!important`. Skip at-rules, including their blocks, without losing sync.
- Selectors for phase 1: type, universal, class, and id selectors, combined into compound selectors; the descendant and child combinators; selector lists. A selector list with any unsupported selector drops the whole rule, as CSS requires.
- Specificity is (ids, classes, types). Order of appearance breaks ties.
- Index rules by the rightmost compound selector (id, then class, then type, then universal) so matching does not test every rule against every element.
- When the document has no rule, skip matching entirely.
- The Jint project (`sebastienros/jint`, directory `Jint.HtmlParser/Css/`, BSD 2-Clause) has a CSS Syntax Level 3 tokenizer and a Selectors Level 4 compiler. The repository owner permits reuse. Phase 1 may adapt its tokenizing and error-recovery logic. Phase 2 ports its selector grammar and matching logic for the full selector set. Its matcher is bound to an HTML DOM and to browser state, so the port targets the flat element tree and leaves out CSSOM, media queries, pseudo-elements, and form states. Adapted code follows this repository's conventions and gets a notice in `THIRD-PARTY-NOTICES`.

### 4.6 Cascade

`<style>` can appear after the elements it styles, so the cascade runs after the whole document is read. For each element, apply declarations in ascending precedence onto the specified-style struct:

1. presentation attributes
2. stylesheet rules, normal, by specificity then order
3. the inline `style` attribute, normal
4. stylesheet rules, `!important`
5. the inline `style` attribute, `!important`

The output of package A is the element tree with one cascaded specified style per element. Inheritance is **not** applied here. Content referenced by `use` inherits from the `use` element, not from its original parent, so inheritance runs in the resolve stage.

### 4.7 Tests for package A

Unit tests under `tests/ImageSharp.Drawing.Tests/Svg/`. Expectations come from the SVG and CSS specifications, not from the implementation. Cover: number boundaries, every length unit, every color form, transform composition order, `preserveAspectRatio` values, selector matching, specificity, the five cascade levels, `!important`, malformed XML, entity expansion limit, element and depth limits, and malformed attribute values that must not throw.

## 5. Package B: Resolve, Render Model, Emit

### 5.1 Resolve walk

One top-down walk from the root. The walk carries the computed style of the parent, the current transform, the current viewport size, and the `use` chain for cycle detection.

- **Computed style.** Specified value, else the parent value for inherited properties, else the initial value. The `inherit` keyword takes the parent value for any property.
- **Skips.** `display: none` skips the subtree. `visibility: hidden` skips the element but not its children. `defs`, gradients, `symbol` outside `use`, `style`, and unknown elements do not render.
- **Units.** Absolute units convert with `Dpi` pixels per inch; `px` and unitless are 1. `em` is the computed `font-size`; `ex` is half of it. A percentage is relative to the viewport width for x-axis values, the height for y-axis values, and `sqrt((w² + h²) / 2)` for other lengths.
- **Limits.** Count emitted nodes against `MaxNodes`. Nested `use` can expand exponentially.

### 5.2 Viewports

- Root size: `width` and `height` when they are absolute lengths. A missing or percentage value takes the viewBox dimension, keeping the aspect ratio when the other dimension is given. With no viewBox the fallback is 300 × 150, as in browsers.
- `viewBox` plus `preserveAspectRatio` give a transform. Implement every alignment, `meet`, `slice`, and `none`.
- A nested `svg` adds a translation, an optional viewBox transform, and a clip to its viewport rectangle.
- `symbol` renders only through `use`. It behaves as a nested `svg` sized by the `use` `width` and `height`.

### 5.3 Shapes

| Element | Geometry |
|---|---|
| `rect` | `rx`/`ry` auto rules and clamping to half the size. Zero or negative size does not render. Use the existing rectangle and rounded rectangle types (verify that the rounded corners are elliptical arcs with separate radii). |
| `circle`, `ellipse` | The existing ellipse polygon. Zero or negative radius does not render. |
| `line` | An open path. It has no fill area. |
| `polyline` | An open path. Fill closes it implicitly. |
| `polygon` | A closed path. |
| `path` | The `IPath` from package A. |

Build each `IPath` once per source element and share it across all `use` instances. Verify that filling an open path closes each figure implicitly, as SVG requires.

### 5.4 `use`

- Resolve the target by id through `href`. A missing target, a target that is an ancestor, and any reference cycle render nothing.
- `x` and `y` add a translation after the `use` transform.
- The referenced content inherits style from the `use` element.
- Expansion produces render nodes per instance. The nodes share geometry and paint objects. This replaces the "instances do not copy nodes" statement in the design: inherited style differs per instance, so node records cannot be shared.

### 5.5 Paint

- `fill` and `stroke` resolve to a `Brush` or to nothing. `currentColor` uses the computed `color`. `fill-opacity` and `stroke-opacity` multiply the color alpha, or every stop alpha for gradients.
- A `url(#id)` that does not resolve to a paint server uses the fallback. Without a fallback, the paint is `none`.
- Deduplicate solid brushes by color and pens by value in a per-document cache.

Gradients:

- `linearGradient`: `x1`, `y1`, `x2`, `y2` default to 0%, 0%, 100%, 0%. `radialGradient`: `cx`, `cy`, `r` default to 50%; `fx`, `fy` default to `cx`, `cy`; `fr` defaults to 0.
- `gradientUnits` (default `objectBoundingBox`), `gradientTransform`, `spreadMethod` (`pad` → `None`, `repeat` → `Repeat`, `reflect` → `Reflect`).
- `href` inheritance: an attribute that is not specified comes from the referenced gradient, through the whole chain. Stops come from the first gradient in the chain that has any. Detect cycles.
- Stops: `offset` as a number or percentage, clamped to 0..1, and never less than the previous offset. `stop-color` and `stop-opacity`.
- Edge cases from the SVG specification: no stops paints nothing; one stop paints a solid color; a linear gradient with equal end points or a radial gradient with zero radius paints the last stop color.
- `objectBoundingBox` units: the gradient geometry is in the unit square. The brush transform is the gradient transform followed by the mapping from the unit square to the fill bounding box of the element. A bounding box with zero width or height paints nothing. Cache brushes by gradient and bounding box.
- The radial gradient maps to the two-circle form of `RadialGradientBrush`: the start circle is the focal circle, the end circle is `cx`, `cy`, `r`.
- Canvas preparation bakes the command transform into brush coordinates, so brushes stay in user space. Verify with a transformed gradient test.

### 5.6 Strokes

- `stroke-width` zero or negative: no stroke.
- Caps map directly. Joins: `round` and `bevel` map directly. SVG `miter` falls back to a bevel when the limit is exceeded; the expected match is `LineJoin.MiterRevert` (verify against the join documentation and with a test). `miter-clip` and `arcs` fall back to `miter`.
- `stroke-dasharray`: an odd count repeats the list; a negative value or an all-zero list means a solid stroke. Verify the unit of `Pen.StrokePattern` and `StrokePatternOffset` (user units or multiples of the stroke width) and convert.
- Stroke width scales with the transform. Do not bake transforms into geometry.

### 5.7 Opacity

- A shape with only a fill or only a stroke: multiply the brush alpha. No layer.
- A group with exactly one renderable child: push the opacity down to the child.
- A shape with fill and stroke, or a group with several renderable children: a layer through `SaveLayer` with `GraphicsOptions.BlendPercentage`. Bound the layer to the conservative bounding box of the subtree, including the stroke extent (half the width, times the miter limit for miter joins, times √2 for square caps). Use an unbounded layer when the box is unknown.
- `opacity: 0` skips the subtree.

### 5.8 Render model

`SvgDocument` holds flat arrays:

- nodes in paint order: draw nodes (path, fill brush, fill rule, pen, transform index) and scope nodes (layer begin and end, viewport clip begin and end)
- a table of distinct absolute transforms (`Matrix3x2`), indexed by the nodes
- the intrinsic size, viewBox, and `preserveAspectRatio` of the root

Transforms usually come from groups, so the table is much smaller than the node list.

### 5.9 Emit

`DrawSvg(document, destination)`:

1. Compute the viewport transform from the root viewBox, or the intrinsic size, to `destination`.
2. `Save`, clip to `destination`, walk the nodes, `Restore`.
3. For a draw node, the active `DrawingOptions` must carry `node transform × viewport transform × canvas transform at the call` and the node fill rule. Create one `DrawingOptions` per distinct (transform index, fill rule) pair used, lazily, per call. Switch state only when the pair changes between consecutive nodes.
4. Record `Fill`, then `Draw`, for each draw node. SVG paints the fill before the stroke.

The emitter needs the current canvas options to compose the transform and keep the caller's `GraphicsOptions`. Follow how `DrawBarcode` and its emitters reach the canvas state; add an internal accessor only if none exists.

`DrawSvg(document, origin)` uses the intrinsic size.

### 5.10 Tests for package B

- **Render tests** with the existing reference-image pattern (`TestImageProvider`, `RunValidatingProcessorTest` or the closest fit, tolerant comparer). One small SVG per feature: each shape, fill rules, each stroke property, transforms, viewBox alignments, nested `svg`, `use` and `symbol` with style inheritance, both gradient kinds with both unit types, spread methods, `href` chains, opacity strategies, CSS selectors, `currentColor`.
- **Reference images are new files, so they need independent evidence.** Package H provides an oracle that renders the same SVG in Chromium. Accept a new reference image only after comparing it with the oracle output. Report the differences found. A mismatch means a defect in the renderer or a documented gap, not a tolerance change.
- **Existing inputs.** The Ghostscript Tiger and Paris files must render through `DrawSvg`. Compare the Tiger with the oracle.
- **Robustness.** Reference cycles, deep `use` expansion that hits `MaxNodes`, and concurrent `DrawSvg` calls on one document.

## 6. Package H: Harness

This package does not depend on the renderer.

### 6.1 Render oracle

A script under `tests/` that renders an SVG file to a PNG with a transparent background at a given pixel size in headless Chromium through Playwright. Both are preinstalled in the development container. The script is a development tool. It is not part of the test run and not a build dependency. Add a short README with the command line.

Add a second command that compares two PNG files and prints the difference metrics, so a developer can compare a renderer output with the oracle.

### 6.2 Benchmark dependencies

- Add `Svg.Skia`, `NetVips`, and `NetVips.Native` to the benchmark project, with versions in `tests/Directory.Build.targets`.
- The tests pin SkiaSharp 2.88.9. Pick the Svg.Skia version built for that SkiaSharp line. If no version fits, put the SVG benchmarks in their own project, and say so.
- Confirm that NetVips loads its native library in the container and reports its librsvg version.

### 6.3 Reference renderer benchmarks

Under `tests/ImageSharp.Drawing.Benchmarks/Svg/`, with `MemoryDiagnoser`:

| Class | Svg.Skia | NetVips |
|---|---|---|
| `LoadSvg` | `SKSvg.Load` | — |
| `RenderSvg` | `DrawPicture` on a raster surface | — |
| `LoadAndRenderSvg` | Both | `SvgloadBuffer` forced with `WriteToMemory` |

- Parameters: the corpus file, and the long-edge size (100, 1000, 4000).
- Follow the fairness rules in `SVG_DESIGN.md` section 9.3: no encoding, libvips operation cache off, a single-thread configuration and a default configuration.
- Leave clear places for the ImageSharp.Drawing methods of package C. Use the public API names from section 3.
- Follow `FillTiger` for structure and setup.

### 6.4 Benchmark corpus

Add the files from `SVG_DESIGN.md` section 9.2 that fit phase 1: an icon set, an illustration with gradients and group opacity, and an export with a `<style>` block. Use permissively licensed sources only and add the notices the same way the repository records the existing third-party files (check `THIRD-PARTY-NOTICES`). Keep the total small. Register the files in `TestImages.Svg`.

### 6.5 Conformance corpus

- A script that fetches the resvg test suite (SVG files and reference PNGs) into a directory that Git ignores. Find where the reference PNGs live now; the design could not confirm it.
- Record the license of the suite and of its bundled fonts in the README.
- Report the directory layout and the number of tests per category, so package C can write the runner.

### 6.6 Smoke run

Run each reference renderer benchmark once in a short job to prove it works. Report the numbers with the machine description. These are harness checks, not results.

## 7. Package C: Measurements and Conformance

- Add the ImageSharp.Drawing methods to the three benchmark classes: CPU backend, fresh canvas and retained scene. Add WebGPU methods only if the existing benchmarks show a pattern that works without a GPU in the container; otherwise leave them out and say so.
- Check output comparability for each corpus file before reporting numbers (design section 9.3).
- Conformance runner: one test that returns early with a clear message when the corpus is absent, and otherwise renders every corpus file, compares with the reference PNG, and checks the result against a checked-in expected-results list. A pass that becomes a failure fails the test. A listed failure that passes fails the test until the list is updated. Write a per-category summary.
- Create the first expected-results list from a real run. Every listed failure outside the phase 1 scope gets its category as the reason. Failures inside the phase 1 scope are defects: fix them or list them with a specific reason.
- Report: benchmark tables with the environment, allocation figures, pass counts per category, and the list of defects found.

## 8. Build and Test Commands

```
export PATH=/root/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
git submodule update --init --recursive      # once per work tree
git lfs pull                                 # once per work tree
dotnet build src/ImageSharp.Drawing/ImageSharp.Drawing.csproj -c Release
dotnet test tests/ImageSharp.Drawing.Tests/ImageSharp.Drawing.Tests.csproj -c Release --filter "FullyQualifiedName~Svg"
```

The build treats analyzer warnings in `src/` seriously. Finish with zero warnings in `src/`.

## 9. Hand-back Report

Each package ends with a commit on its own branch and a report that lists:

- what changed, by area
- the commands run and their results
- every "verify" item, with the answer found
- every difference from this plan, with the reason
- known gaps and remaining risks
