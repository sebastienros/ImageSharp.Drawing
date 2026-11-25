# SVG Rendering: Implementation Plan (Phase 2)

This document continues `SVG_IMPLEMENTATION.md`. Read `SVG_DESIGN.md`, `SVG_IMPLEMENTATION.md`, and the repository `AGENTS.md` first. All rules of `SVG_IMPLEMENTATION.md` still apply: no `record` types, documentation on every member, simple technical English, Release builds, zero warnings in `src/`, no new package reference in `src/`, no reference image or expected result edited to silence a failure, no issue, no pull request, no push.

Where this plan says "verify", the statement comes from reading source or from memory. Check it against the code or with a test before relying on it. When the code contradicts the plan, follow the code and record the difference in the hand-back report.

Phase 2 scope (`SVG_DESIGN.md` section 11): `clipPath`, `mask`, `pattern`, markers, `image`, `switch`, blend modes, `paint-order`, full selectors, user stylesheet. This plan also closes the phase 1 gaps that are cheap here: the `a` element, the root `transform` attribute, `overflow`, and `transform-origin`.

## 1. Work Packages

| Package | Content | Depends on |
|---|---|---|
| D | Parsing of every phase 2 attribute and property, public options, shared resolve helpers. | Phase 1 |
| E | Compositing: `clipPath`, `mask`, blend modes, `isolation`, `paint-order`, `switch`, conditional attributes, `vector-effect`, `overflow`, `a`, root `transform`. | D |
| F | Paint and content: `pattern`, markers, `image`. | D |
| S | CSS: full selectors, `media`, user stylesheet. | D |
| G | Integration: cross-feature tests, conformance totals, benchmarks, document status. | E, F, S |

E, F, and S run at the same time in separate work trees. To keep the merges small:

- D adds all parsing, all new `SvgPropertyId` members, all new public options, and the shared helpers. E and F do not change `Parsing/`, `Css/`, `SvgLoadOptions`, or `SvgLimits`. If E or F finds a parsing defect, it fixes it in the smallest way and reports it.
- S changes `Css/`, `SvgReader` (raw attribute storage only), and the load path of the user stylesheet. S does not change `Resolve/` or `Rendering/`.
- E and F put each feature in its own new file (`partial` classes are fine: `SvgResolver.Clip.cs`, `SvgResolver.Mask.cs`, `SvgResolver.Pattern.cs`, `SvgResolver.Markers.cs`, `SvgResolver.Image.cs`, and the matching emitter files). Changes to shared files (`SvgResolver.cs`, `SvgEmitter.cs`, `SvgRenderNode.cs`, `SvgRenderNodeKind.cs`, `SvgComputedStyle.cs`) are limited to short hooks. Add new enum members and new fields at the end.
- In the conformance expected-results list, a package changes only the lines of its own features.

## 2. Public API Additions (Package D)

```csharp
public sealed class SvgLoadOptions
{
    // CSS text applied as a user stylesheet. Null or empty means none.
    public string? UserStyleSheet { get; set; }

    // Language tags for systemLanguage tests, most preferred first. Default: "en". Never null; may be empty.
    public IReadOnlyList<string> Languages { get; set; }

    // Directory that file references of image elements resolve against.
    // Null (default) means no file is ever read; only data URIs load.
    public string? ResourceBaseDirectory { get; set; }
}

public sealed class SvgLimits
{
    // Largest total number of decoded pixels of all image elements of one document.
    public long MaxImagePixels { get; set; }      // default 64 * 1024 * 1024

    // Largest pixel count of one rendered pattern tile.
    public int MaxPatternTilePixels { get; set; } // default 4096 * 4096
}
```

- `Load(string path)` does not set `ResourceBaseDirectory`. Reading other files must be an explicit choice of the caller.
- The default language is fixed and does not follow the current culture, so that output does not depend on the machine.
- `SvgDocument` stays immutable, thread-safe, and not disposable. See section 5.3 for how decoded images are held.
- D adds the properties with validation, documentation, and tests. D passes them to the reader options and the resolve context. The features that use them come from E, F, and S.

## 3. Package D: Parsing and Shared Helpers

### 3.1 Elements and attributes

Add typed storage and accessors to `SvgElementTree`, in the style of the phase 1 accessors. Lengths stay unresolved (`SvgLength`).

| Element | Attributes |
|---|---|
| `clipPath` | `clipPathUnits`, `transform` |
| `mask` | `x`, `y`, `width`, `height`, `maskUnits`, `maskContentUnits` |
| `pattern` | `x`, `y`, `width`, `height`, `patternUnits`, `patternContentUnits`, `patternTransform`, `viewBox`, `preserveAspectRatio`, `href`, `xlink:href` |
| `marker` | `refX`, `refY`, `markerWidth`, `markerHeight`, `markerUnits`, `orient` (`auto`, `auto-start-reverse`, angle), `viewBox`, `preserveAspectRatio` |
| `image` | `x`, `y`, `width`, `height` (with `auto`), `preserveAspectRatio`, `href`, `xlink:href` |
| any | `systemLanguage`, `requiredExtensions`, `requiredFeatures` (store presence and raw text) |

Record which values were specified. `pattern` inherits unspecified attributes through `href`, like gradients.

### 3.2 Properties

Add to `SvgPropertyId`, `SvgSpecifiedStyle`, `SvgPropertyParser`, and `SvgComputedStyle`, with the correct inheritance flag and initial value:

| Property | Inherited | Values |
|---|---|---|
| `clip-path` | no | `none`, `url(#id)` |
| `clip-rule` | yes | `nonzero`, `evenodd` |
| `mask` | no | `none`, `url(#id)` |
| `mask-type` | no | `luminance`, `alpha` |
| `mix-blend-mode` | no | the 16 CSS blend modes |
| `isolation` | no | `auto`, `isolate` |
| `paint-order` | yes | `normal`, or a list of `fill`, `stroke`, `markers` |
| `vector-effect` | no | `none`, `non-scaling-stroke` |
| `overflow` | no | `visible`, `hidden`, `scroll`, `auto` |
| `marker-start`, `marker-mid`, `marker-end` | yes | `none`, `url(#id)` |
| `marker` | — | CSS shorthand only; it is not a presentation attribute |
| `image-rendering` | yes | `auto`, `optimizeQuality`, `optimizeSpeed`, `pixelated`, `crisp-edges`, `smooth`, `high-quality` |
| `shape-rendering` | yes | `auto`, `optimizeSpeed`, `crispEdges`, `geometricPrecision` |
| `color-interpolation` | yes | `sRGB`, `linearRGB` |
| `transform-origin` | no | one or two lengths, percentages, or keywords |

Also add the paint keywords `context-fill` and `context-stroke` to `SvgPaint`.

`SvgSpecifiedStyle` uses bit masks. Check the mask width before adding 16 properties and widen it if needed. Measure `LoadSvg` for the Tiger, the icon set, and Paris before and after; the phase 1 documents must not load slower or allocate more beyond noise.

### 3.3 `transform-origin`

D implements this fully, because it changes how the phase 1 transform is built. The origin is relative to the reference box. For SVG elements without a CSS layout box the reference box is the nearest viewport (`view-box`), so a percentage needs the viewport size at resolve time. The effective transform is `translate(origin) × transform × translate(-origin)`. Test against the oracle.

### 3.4 Shared helpers

- **Object bounding box.** `SvgResolver` gets one function that returns the object bounding box of an element in its own user space: the fill geometry of a shape without the stroke; the union over the children of a container, each through its own transform; nothing for an element that draws nothing. `use` counts as a container of its referenced content. E needs it for `clipPathUnits` and `maskUnits`; F needs it for `patternUnits` and markers. Phase 1 already computes shape boxes for gradients; reuse that code.
- **Content resolve.** Phase 1 resolves a referenced subtree for `use`. Make that entry point usable for other referenced content (clip children, mask content, pattern content, marker content): the caller supplies the subtree root, the style to inherit from, the transform, and a reference stack for cycle detection. Content of `clipPath`, `mask`, `pattern`, and `marker` inherits style from its own ancestors, not from the referencing element.

### 3.5 Tests for package D

Unit tests for every new attribute, property, and option, with invalid values. A test for the bounding box of shapes, groups, `use`, and empty groups. Oracle-backed render tests for `transform-origin`.

## 4. Package E: Compositing

### 4.1 `clipPath`

Rules that implementations often miss:

- Only shapes, and `use` elements that reference a shape directly, add geometry. (Text joins in phase 3.) Other children are ignored.
- Geometry is the fill region of each child. Fill, stroke, and opacity of the child do not matter. `display: none` and `visibility: hidden` children add nothing.
- Each child uses its own `clip-rule` and its own transform. The `transform` of the `clipPath` applies to all children.
- A `clip-path` on a child intersects that child. A `clip-path` on the `clipPath` element intersects the whole result.
- `clipPathUnits="objectBoundingBox"` maps the unit square to the bounding box of the clipped element. A zero-size box draws nothing.
- A `clipPath` with no valid child clips everything. A `clipPath` that references itself, directly or through children, is invalid.
- For a missing or invalid reference, match Chromium. The resvg tests cover these cases.

Strategy:

1. **Geometry clip.** When the clip resolves to plain geometry, build the paths at resolve time and use `DrawingCanvas.Clip`. Verify whether one `Clip` call can honor a different fill rule per path (`SVG_DESIGN.md` section 7.2). If it cannot, normalize each child to a rule-independent outline at resolve time with the path boolean operations of this library, or use two nested clips when that is enough. Share the resolved geometry between all elements that reference the same `clipPath` with `userSpaceOnUse` units.
2. **Mask fallback.** For nested `clip-path` references that the geometry clip cannot express, draw the clip content as coverage and use the mask machinery of 4.2 with alpha mode.

Verify that canvas clipping is antialiased and compare edge quality with the oracle.

### 4.2 `mask`

- The mask region is `x`, `y`, `width`, `height` (default `-10%`, `-10%`, `120%`, `120%` of the bounding box, `maskUnits="objectBoundingBox"`). Outside the region the element is fully masked.
- `maskContentUnits` selects the coordinate system of the content.
- `mask-type: luminance` (default) uses luminance times alpha with the coefficients 0.2125, 0.7154, 0.0721 on sRGB values. `color-interpolation: linearRGB` on the `mask` converts to linear light first. `mask-type: alpha` uses alpha only.
- A `mask` on the `mask` element applies to the mask result. Recursion is invalid. A zero-size bounding box with `objectBoundingBox` units draws nothing.

Planned mapping (`SVG_DESIGN.md` section 7): a content layer; inside it a second layer that composites with `PixelAlphaCompositionMode.DestIn` and carries a `ColorMatrixLayerEffect` that writes luminance to alpha. This is unproven. Start with a prototype test that checks (a) `DestIn` compositing of a nested layer, (b) that pixels outside the mask region are cleared, and (c) the effect order (effect before compositing). If the canvas cannot do this with public or internal features, report the gap and propose the smallest canvas change; do not work around it with a hidden full-image copy per mask without measuring it.

Bound every layer to the conservative bounding box of the masked content.

### 4.3 Blend modes and isolation

- `mix-blend-mode` other than `normal`: a layer that composites with the matching `PixelColorBlendingMode`. A leaf with only a fill, or only a stroke, may set the mode on the draw options instead of a layer; do this only if a test shows the same pixels.
- `isolation: isolate`: a layer. Opacity, masks, and blend modes already isolate.
- A blended element inside a non-isolated group must blend with what is below the group. Phase 1 draws layer content source-over and applies the caller's blend at the outermost layer; keep that behavior and test the combination.

### 4.4 `paint-order`

The draw node gets the order of fill and stroke. The position of markers relative to fill and stroke belongs to package F; E only stores the full order in the computed style.

### 4.5 `switch` and conditional attributes

- `switch` renders its first direct child whose conditions pass, and nothing else.
- Conditions also apply to elements outside `switch`: a failing element does not render.
- `systemLanguage`: passes when one listed tag equals a tag in `SvgLoadOptions.Languages`, or is a prefix of it followed by `-`. An empty attribute fails.
- `requiredExtensions`: a non-empty value fails. Match Chromium for an empty value.
- `requiredFeatures`: ignored (removed in SVG 2); verify against the resvg tests.

### 4.6 Smaller items

- **`vector-effect: non-scaling-stroke`.** The stroke width is measured in destination units of `DrawSvg` (the host coordinate space), not in user units. The emitter already outlines strokes itself for non-conformal transforms; reuse that path: transform the geometry first, then outline with the unscaled pen. Dashes follow the same space.
- **`overflow`.** `visible` on a nested `svg`, `symbol`, or `marker` viewport removes the viewport clip. The root element always clips.
- **`a`.** Renders as a group.
- **Root `transform`.** Applies outside the viewBox transform (SVG 2); verify with the oracle.
- **`shape-rendering: crispEdges` and `optimizeSpeed`.** Draw without antialiasing if `GraphicsOptions.Antialias` reaches the draw; low priority.

### 4.7 Render model

New scope nodes: clip begin/end with geometry and rule, mask begin/content/end, layer begin with a blend mode. Keep `SvgRenderNode` small: put rarely used data in side tables indexed from the node, and report the size of the struct before and after.

## 5. Package F: Paint and Content

### 5.1 `pattern`

- Attributes inherit through `href`; the content comes from the first pattern in the chain that has children.
- `patternUnits` default `objectBoundingBox`; `patternContentUnits` default `userSpaceOnUse`; a `viewBox` overrides `patternContentUnits`.
- The tile clips its content (`overflow: hidden`). Zero width or height, or a zero-size bounding box with `objectBoundingBox` units, means no paint. A pattern that references itself through its content is invalid.
- Patterns apply to fill and to stroke, combine with `fill-opacity` and `stroke-opacity`, and can nest.

Planned mapping (`SVG_DESIGN.md` section 7): render the tile to an image at the device scale, then paint with an image brush that repeats.

- The tile resolution depends on the output scale, so the tile is rendered at emit time. Cache tiles per document, keyed by pattern instance and scale. `DrawSvg` runs concurrently on one document: the cache must be thread-safe.
- A tile size that is not a whole number of device pixels causes seams. Round the tile to whole pixels and fold the difference into the brush transform.
- The canvas defers commands. A tile image must stay valid until the canvas flushes and for the life of a retained scene. Hold tile pixels in managed arrays owned by the document cache (`Image.WrapMemory` over an array; verify that this leaves no pooled buffer to dispose).
- Verify how an image brush behaves under a rotated or skewed command transform, and with `patternTransform`. If the brush cannot follow the transform, clip to the shape and replay the tile content per tile, or draw the tile image with wrap modes under the transform. Rotated and skewed patterns must be correct either way.
- Respect `SvgLimits.MaxPatternTilePixels`: lower the tile resolution rather than fail.

### 5.2 Markers

- `marker-start`, `marker-mid`, `marker-end` apply to `path`, `line`, `polyline`, and `polygon` only. The properties are inherited.
- Marker content inherits style from the ancestors of the `marker`, not from the shape.
- Vertices and directions follow the SVG path rules: the direction at a middle vertex is the bisector of the incoming and outgoing directions; zero-length segments take the direction of their neighbors; a closed subpath has special rules for its first and last vertex. Follow SVG 2 "Details on how markers are rendered".
- `Path` keeps its line segments (verify `Path.LineSegments`, the cubic and arc segment types, and how closed subpaths are represented). Compute vertices and tangents from the segments. If parsing loses information that markers need (for example dropped zero-length segments), report it and choose the smallest fix.
- `markerUnits="strokeWidth"` scales by the stroke width of the shape. `orient` supports `auto`, `auto-start-reverse`, and angles. `viewBox` and `preserveAspectRatio` work as for `symbol`. The marker viewport clips unless `overflow` is `visible`.
- Markers do not render on shapes inside a `clipPath`. A marker whose content uses the same marker is a cycle.
- `paint-order` decides where markers paint relative to fill and stroke. Package E adds the fill and stroke order to the draw node. To keep the packages independent, F emits marker nodes before or after the shape node according to the computed order; a stroke/markers/fill order that splits fill and stroke uses two draw nodes.
- `context-fill` and `context-stroke` inside marker content take the paint of the shape. Do this after everything else in this section works; listing it as a known gap is acceptable.

### 5.3 `image`

- **Sources.** `data:` URIs (base64 and percent-encoded) always load. A file reference loads only when `SvgLoadOptions.ResourceBaseDirectory` is set: resolve relative to it, and refuse absolute paths, other URL schemes, and any path that leaves the directory after normalization. Never fetch from the network.
- **Formats.** Raster formats through the ImageSharp decoders. `image/svg+xml` (and `.svg` files) load as a nested `SvgDocument` with the same limits, counted against the same element and node limits, with a depth limit, and with file loading disabled inside it.
- **Limits.** Identify before decoding. Count decoded pixels against `SvgLimits.MaxImagePixels`. A broken or refused image draws nothing and does not fail the document.
- **Ownership.** `SvgDocument` is not disposable. Decode, copy the pixels into a managed array owned by the document, dispose the decoder image, and wrap the array (verify `Image.WrapMemory`). Decode each distinct source once.
- **Layout.** `x`, `y`, `width`, `height` with `auto` from the intrinsic size; `preserveAspectRatio` including `slice` with a clip to the image viewport; `transform`, opacity, clip, and mask apply as for any element.
- **Sampling.** `image-rendering: optimizeSpeed`, `pixelated`, and `crisp-edges` use nearest neighbor. Other values use a smooth sampler; verify what `DrawImage` uses by default and how it handles large downscales.
- Verify `DrawImage` under rotated and skewed transforms.
- EXIF orientation: match Chromium if cheap; otherwise list it as a gap.

## 6. Package S: CSS

- **Selectors.** Port the selector compiler and matcher from Jint (`Jint.HtmlParser/Css/`, BSD-2-Clause; the notice is already in `THIRD-PARTY-NOTICES`). Needed: descendant, child, next-sibling, and subsequent-sibling combinators; attribute selectors with all operators and the case flag; `:first-child`, `:last-child`, `:only-child`, `:nth-child()`, `:nth-last-child()`, the `-of-type` forms, `:not()`, `:is()`, `:where()`, `:root`, `:empty`. Pseudo-classes that need interaction state (`:hover`, `:focus`, and so on) never match. Pseudo-elements never match. Selector lists stay forgiving as CSS defines: one invalid selector drops the rule.
- **Matching model.** The tree is flat with parent indexes. Match right to left. Keep the phase 1 fast path: rules indexed by id, class, and type on the rightmost compound, so that documents with simple selectors do not slow down.
- **Attribute values.** Attribute selectors need raw attribute values, which phase 1 does not keep. A `style` element can appear after the elements it selects, so "keep them only when needed" is not known while reading. Requirements: attribute selectors work wherever the `style` element is, and documents without attribute selectors do not load measurably slower or allocate more (measure with `LoadSvg` on the Tiger, the icons, and Paris). Options: keep raw values in a compact side table except for the large geometry attributes (`d`, `points`), and answer selectors on those from presence only; or read the source a second time when a stylesheet turns out to need raw values. Choose from measurements and report the numbers.
- **`media`.** The `media` attribute of `style` and `@media` rules: evaluate `all` and `screen` as true, `print` as false, and simple `min-width`/`max-width` features against the root viewport if cheap; an unknown query is false.
- **User stylesheet.** `SvgLoadOptions.UserStyleSheet` enters the cascade at user origin: below author rules for normal declarations, above them for `!important`. Phase 1 `SvgCascade` has 5 precedence levels; add the user levels.
- **Escapes.** Decode CSS escapes in identifiers and declaration values (a phase 1 gap).
- Geometry properties in CSS (`cx`, `r`, `d`, and so on) stay out of scope.

Tests: unit tests per selector feature, specificity and origin order, and the resvg `structure/style` tests.

## 7. Package G: Integration

- Cross-feature render tests with oracle evidence: a masked group filled with a pattern, markers on a clipped path, an image inside a mask, a blended element inside a pattern, `use` of content that carries clips and masks.
- Run the conformance runner over the whole corpus. Report pass counts per category next to the phase 1 counts and next to the `results.csv` columns of the other renderers. Every phase 2 test that still fails has a specific reason in the list.
- Benchmarks: rerun the phase 1 tables to show that phase 2 did not slow the phase 1 documents. Add benchmark documents that use clips, masks, patterns, and images, with clear licenses, and compare with Svg.Skia and NetVips under the fairness rules of `SVG_DESIGN.md` section 9.3.
- Update the status and the known-gap lists in `SVG_DESIGN.md`.

## 8. Tests and Evidence (all packages)

Same rules as `SVG_IMPLEMENTATION.md` section 5.10:

- One small SVG per feature and per rule in this plan, under `tests/Images/Input/Svg/Features/`.
- A new reference image needs independent evidence. Compare with the Chromium oracle (`tests/SvgTools/`) and report the differences. A mismatch is a defect or a documented gap, never a tolerance change.
- Conformance: promote every resvg test that now passes. A test of a feature in this plan that still fails is a defect to fix or to list with a specific reason. Phase 1 tests must not regress.
- Robustness: reference cycles through every new reference kind, limits, hostile images, concurrent `DrawSvg` on one document.
- Finish with zero warnings in `src/`, the SVG tests green, and the full test suite run once.

## 9. Hand-back Report

As in `SVG_IMPLEMENTATION.md` section 9. Do not commit and do not push; the main session reviews, commits, and merges.
