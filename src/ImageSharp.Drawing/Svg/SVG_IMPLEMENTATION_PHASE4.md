# SVG Rendering: Implementation Plan (Phase 4, Filters)

This document continues `SVG_IMPLEMENTATION.md`, `SVG_IMPLEMENTATION_PHASE2.md`, and `SVG_IMPLEMENTATION_PHASE3.md`. Read `SVG_DESIGN.md`, the earlier plans, and the repository `AGENTS.md` first. All their rules still apply: no `record` types, documentation on every member, simple technical English, Release builds, zero warnings in `src/`, no new package reference in `src/`, no existing reference image or expected result edited to silence a failure, no issue, no pull request, no commit, no push.

Where this plan says "verify", the statement comes from reading source or from memory. Check it against the code, the specification, or the oracle before relying on it. When the code contradicts the plan, follow the code and record the difference in the hand-back report.

When a rendering choice is open: follow the specification where it settles the question (Filter Effects Module Level 1, which replaces the filter chapter of SVG 1.1), otherwise Chromium, then Skia and librsvg. This is a decision of the user.

Phase 4 scope: the `filter` property with `url()` references and CSS filter functions, the `filter` element, and all filter primitives of Filter Effects Level 1.

## 1. Work Packages

| Package | Content | Depends on |
|---|---|---|
| X1 | Parsing, properties, filter resolution, regions and units, the filter graph, the execution engine, and the primitives `feFlood`, `feOffset`, `feMerge`, `feGaussianBlur`, `feColorMatrix`, `feComposite`, `feBlend`. The conformance setup for filters. | Phase 3 |
| X2 | `feComponentTransfer`, `feDropShadow`, `feTile`, `feImage`, CSS filter functions, filter lists. | X1 |
| X3 | `feMorphology`, `feConvolveMatrix`, `feDisplacementMap`, `feTurbulence`. | X1 |
| X4 | `feDiffuseLighting`, `feSpecularLighting`, `feDistantLight`, `fePointLight`, `feSpotLight`. | X1 |
| X5 | Integration: cross-feature tests, conformance totals, hostile documents, benchmarks, profiling and optimisation, document status. | X2, X3, X4 |

X2, X3, and X4 run at the same time in separate work trees. X1 therefore parses every element, attribute, and property that all packages need, puts each primitive in its own file behind one internal contract (section 5.4), and creates every later primitive as a documented stub that marks the filter as unsupported. X2, X3, and X4 put their work in new files and keep edits to shared files short, with new enum members and fields at the end. In the conformance expected-results list, a package changes only the lines of its own features.

## 2. Why the Engine Does Not Use the Canvas Layer Effects

`SVG_DESIGN.md` (sections 7 and 11) planned to map `feGaussianBlur`, `feDropShadow`, and `feColorMatrix` to the public `BlurLayerEffect`, `DropShadowLayerEffect`, and `ColorMatrixLayerEffect`, and to add a filter-graph `LayerEffect` for the rest. Reading the canvas code shows that this does not give correct SVG results:

- A layer effect is an operation over a snapshot of the layer region. The CPU backend intersects the region with the canvas bounds (`FlushScene`, the `Apply` command) and passes no origin to the operation. A filter needs the origin: primitive subregions, `feTile`, `feTurbulence`, `feImage`, and the light positions are in user space.
- Content outside the canvas is not in the snapshot. A blur or an offset near the canvas edge must read that content.
- The snapshot is in device space. Filter primitives work along the axes of the user space of the filtered element; under rotation or skew that is not the device grid.
- The existing effects cannot clip to the filter region, cannot work in linear RGB (the default of `color-interpolation-filters`), and have no primitive subregion.

Verify these four points first. If one is wrong, report it before building on this section.

Decision: one engine in `src/ImageSharp.Drawing/Svg/Filters/` runs every filter off-screen, on buffers that it owns, and draws the result as an image. It needs no change to the canvas and works the same on both backends, with the known gap that the WebGPU backend does not draw the internal texture brush (`SVG_DESIGN.md` section 14.3). X5 may add a path through the public layer effects for the cases where they are exact, but only with a measured gain. X5 updates sections 7 and 11 of the design.

## 3. Parsing and Properties (Package X1)

### 3.1 Elements and attributes

- `filter`: `x`, `y`, `width`, `height`, `filterUnits`, `primitiveUnits`, `href`, `xlink:href`. A `filter` inherits missing attributes and, when it has no primitive children, the children of the filter that its `href` names, as gradients do (verify against Filter Effects and Chromium; the specification removed `href` on `filter`, the suite tests it). `filterRes` has no effect.
- Every primitive: `x`, `y`, `width`, `height`, `result`, and `in` where it has an input.
- Per primitive, every attribute of Filter Effects Level 1, including the children `feMergeNode`, `feFuncR`, `feFuncG`, `feFuncB`, `feFuncA`, and the three light sources. X1 parses them all into the element tree; X2, X3, and X4 only read them.

### 3.2 Properties

Add with the correct inheritance and initial value: `filter` (not inherited; `none`, or a list of `url()` references and filter functions), `color-interpolation-filters` (inherited; the initial value is `linearRGB`), `flood-color`, `flood-opacity`, `lighting-color` (not inherited). They work as presentation attributes, in `style`, and in style sheets.

`SvgPropertyId` uses 59 of the 64 bits of the property mask (verify). These five fill it. If the mask must grow, change its representation in X1 and show with `LoadSvg` measurements that the phase 1 to 3 documents do not load slower or allocate more beyond noise.

### 3.3 The `filter` value

X1 parses the whole grammar: `url()` references and the ten functions (`blur`, `brightness`, `contrast`, `drop-shadow`, `grayscale`, `hue-rotate`, `invert`, `opacity`, `saturate`, `sepia`), in a list. X1 executes a value that is one `url()` reference. X2 adds the functions and lists of more than one entry. Until then a value with a function follows the rule for unsupported filters (section 4.4).

## 4. Resolution (Package X1)

### 4.1 Where a filter applies

- Order of effects on one element: filter, then clipping, then masking, then opacity, then the blend mode. The filter sees the element without its clip, mask, and opacity. An element with a filter is an isolated group.
- `filter` applies to container elements (`g`, `svg`, `use`, `a`, `switch`), shapes, `image`, and `text`. It has no effect on a `tspan` or `textPath`, and none on the children of a `clipPath` (the suite has a test that passes today; keep it passing). It applies inside `mask`, `pattern`, and `marker` content.
- An element with a filter renders through the filter even when it has no content of its own to draw, because `feFlood`, `feImage`, and `feTurbulence` produce output without input.

### 4.2 Regions and units

- The filter region: `x`, `y`, `width`, `height` with `filterUnits`. The default is `objectBoundingBox` with -10%, -10%, 120%, 120%. The bounding box is the object bounding box that phases 1 to 3 compute (fill geometry, without stroke and without the filter).
- Primitive subregions with `primitiveUnits`, and the default subregion rules of the specification (the union of the subregions of the inputs, or the filter region when an input is a standard input or there is no input). Match Chromium where the specification leaves a case open, such as `feOffset` and `feFlood` with partial attributes.
- With `primitiveUnits="objectBoundingBox"`, lengths and numbers in primitive attributes (`stdDeviation`, `dx`, `dy`, `radius`, `scale`, light positions) are fractions of the bounding box, as the specification defines per attribute.
- A zero-size bounding box with bounding-box units, a zero or negative region size: follow the specification, then Chromium (verify whether the element then draws without the filter or not at all).

### 4.3 The graph

- Inputs: `SourceGraphic`, `SourceAlpha`, a `result` name, or the result of the previous primitive when `in` is missing (for the first primitive: `SourceGraphic`). A name that matches no earlier `result`: follow the specification (verify; it treats it as the previous result, or `SourceGraphic` for the first primitive).
- `BackgroundImage`, `BackgroundAlpha`, `FillPaint`, `StrokePaint`, and the `enable-background` property: do what Chromium does (verify with the oracle; the expectation is that it does not support them). Do not build background access.
- Resolve the graph once per filtered element into an immutable description in user space: the regions, the primitives in order, the input indices, and the color space of each primitive. Find the last use of each result so the engine can release buffers early. Detect reference cycles through `href` and through `feImage` references to elements (X2) and drop the node, as the other reference kinds do.

### 4.4 Errors

Decide each case from the specification first, then check Chromium with the oracle, and record the rule in the code:

- `filter` names a missing element or an element that is not a `filter`. (Filter Effects: verify the exact text; the expectation is that the whole filter list is ignored and the element draws without a filter. SVG 1.1 and the resvg references do not draw the element.)
- A `filter` element without primitives: the element does not draw.
- A primitive with invalid attribute values: per primitive, the specification says whether the primitive is disabled, gives a transparent result, or uses a default.
- A filter that this build does not support yet (a stub primitive, a function before X2): draw the element without the filter and keep the conformance test listed as failing with the package name as the reason. Do not let a test pass by accident; X5 checks every pass in the filter directories against the oracle.

Where the resvg reference image follows SVG 1.1 and the specification and Chromium differ, follow the specification and list the test as failing with that reason. Report the number of such tests.

### 4.5 Bounds for other features

Phases 2 and 3 bound their layers (opacity groups, masks, blend groups, coverage clips) by the bounding boxes of the nodes inside. A filtered element paints inside its filter region, which is larger than its geometry or unrelated to it. Every such bound must use the filter region for a filtered node. Add a test with a blurred child in an opacity group and in a mask where the blur extends beyond the geometry.

## 5. The Engine (Package X1)

### 5.1 Render node

One new render node kind carries the filter: the content of the element as a nested document in the user space of the element (the way pattern tiles and markers hold their content; reuse that machinery), and the resolved filter list. The emitter draws the node in one step: render the content off-screen, run the graph, draw the result. Nested filters recurse through the nested document; bound the depth with the existing nesting limit.

### 5.2 Filter space

Filter primitives work on a pixel grid along the axes of the user space.

- At draw time, split the transform from user space to device space into a scale `(sx, sy)` and a remainder. For a transform that only scales and translates (the common case), the filter grid is the device grid: include the translation so that filter pixels are device pixels, and draw the result without resampling. Otherwise the filter runs in user space scaled by `(sx, sy)` and the result draws through the remainder with bilinear sampling, as Skia does for image filters under a rotated matrix (verify how Chromium renders `filters/filter` tests with rotated and skewed elements).
- Every length of a primitive scales by `sx` or `sy`: `stdDeviation`, `dx`, `dy`, `radius`, `scale`, `kernelUnitLength`, light positions, `baseFrequency` (inverse).
- A transform that is not invertible, or a scale of zero, draws nothing.

### 5.3 Working area

- The filter region in filter space, rounded outward to whole pixels, is the largest area the filter can touch.
- Do not process pixels that cannot reach the output. Each primitive declares its reach: how far its output depends on its input (three times the standard deviation for the blur, `dx` and `dy` for the offset, the radius for morphology, the kernel size for convolution, half the scale for the displacement map, one pixel for lighting, zero for point operations and generators). A primitive that cannot bound its reach (`feTile`) asks for its whole input subregion. The engine walks the graph backward from the visible area (the canvas bounds mapped into filter space) and gives each result the smallest rectangle that the output needs. An element that is far outside the canvas with all its reach costs nothing.
- `SvgLimits` gets `MaxFilterPixels`: the largest working area of one filter in pixels (propose 4096 by 4096, like `MaxPatternTilePixels`). Above it the filter runs at a lower resolution and the result scales up, as pattern tiles do. Document the memory this allows: bytes per pixel times the number of live buffers.

### 5.4 Buffers and the primitive contract

- A result is premultiplied RGBA with 8 bits per channel, as in Chromium and resvg. Primitives that need more precision compute in floating point per row and store 8 bits. The reasons: one quarter of the memory of a floating point buffer, the same rounding as the reference renderers, and fast integer code for blur, offset, merge, and compositing.
- Buffers come from the memory allocator of the configuration, or a pool, and return to it when the last consumer of the result has run. A primitive may write its output over an input that has no other consumer. Rendering a filter must not leave allocations that grow with the number of renders.
- `SourceGraphic` renders through a nested canvas into the working area, then converts to premultiplied values once. `SourceAlpha` is derived from it without a second render.
- The contract of a primitive (one type per primitive, one file each): its inputs, its reach, whether it needs its input in a given color space, and one method that fills the output rectangle from the input buffers, given the filter space scale and the origin of the working area in user space. The engine clips every result to the primitive subregion (outside it the result is transparent), handles color spaces, and owns the buffers. The primitives of X2, X3, and X4 need nothing beyond this contract; if one does, change the contract in X1's files with a short edit and report it.
- The final result converts to straight alpha and draws through the internal texture brush that `image` uses, clipped to the filter region.

### 5.5 Color spaces

`color-interpolation-filters` is read on each primitive (it inherits from the `filter` element, not from the filtered element). The engine tags each result with its color space and converts an input when the consumer works in the other space, on unpremultiplied values, with tables of 256 entries. `SourceGraphic` is sRGB. The final result converts to sRGB. `feOffset`, `feTile`, `feMerge` of inputs in one space, and other primitives that only move pixels should not force a conversion (verify what Chromium does and match it). CSS filter functions work in sRGB.

### 5.6 The primitives of X1

- `feFlood`: `flood-color`, `flood-opacity`, over the primitive subregion.
- `feOffset`: whole pixels move exactly; a fractional offset: match Chromium (verify whether it rounds or interpolates).
- `feMerge`: source-over in order.
- `feGaussianBlur`: `stdDeviation` with one or two numbers; zero on one axis blurs only the other; zero on both passes the input through; a negative value is an error case (section 4.4). For a standard deviation of 2 or more in filter pixels, use three successive box blurs as the specification describes; below that, a true Gaussian kernel. Both run as separate horizontal and vertical passes, with a cost per pixel that does not grow with the standard deviation for the box form. `edgeMode`: support what Chromium supports.
- `feColorMatrix`: `matrix`, `saturate`, `hueRotate`, `luminanceToAlpha`, on unpremultiplied values.
- `feComposite`: `over`, `in`, `out`, `atop`, `xor`, `lighter`, and `arithmetic` with `k1` to `k4` (clamp the result to valid premultiplied values).
- `feBlend`: the 16 blend modes. Reuse the blend functions of ImageSharp where they give the result of the Compositing specification on premultiplied values (verify).

## 6. Packages X2, X3, X4

### 6.1 X2: transfer, shadow, tile, image, functions

- `feComponentTransfer`: `identity`, `table`, `discrete`, `linear`, `gamma` per channel, as one table of 256 entries per channel on unpremultiplied values.
- `feDropShadow`: the specification defines it as a fixed chain of flood, composite, blur, offset, and merge; produce the same pixels.
- `feTile`: repeats the input subregion over the primitive subregion, anchored at the input subregion in user space. Under a fractional scale the tile edges do not fall on pixels; match Chromium.
- `feImage`: an image reference loads under the same rules as `image` (`data:` URIs; files only inside `SvgLoadOptions.ResourceBaseDirectory`; `SvgLimits.MaxImagePixels`), placed in the primitive subregion with `preserveAspectRatio`. A reference to an element renders that element as `use` would; check the user space rules with `primitiveUnits` against Chromium. `image-rendering` applies. A cycle (the element uses the filter that holds the `feImage`) drops the primitive.
- CSS filter functions: each maps to the primitive chain that Filter Effects defines for it, in sRGB, with a filter region that does not clip (it grows by the reach of the function). A list applies its entries in order, each on the result of the previous one; `url()` entries inside a list use their own filter region.

### 6.2 X3: morphology, convolution, displacement, turbulence

- `feMorphology`: `erode` and `dilate` with one or two radii, per channel on premultiplied values. The cost per pixel must not grow with the radius without bound: use a separable running minimum and maximum, or bound the radius by the working area. A zero or negative radius: section 4.4.
- `feConvolveMatrix`: `order`, `kernelMatrix`, `divisor`, `bias`, `targetX`, `targetY`, `edgeMode` (`duplicate`, `wrap`, `none`), `preserveAlpha`. `kernelUnitLength`: do what Chromium does. Bound the work (pixels times kernel entries) with the caps Chromium uses (verify) or a documented limit of our own. The ten tests that the user had relisted as failing belong here; they pass only when the invalid cases behave correctly.
- `feDisplacementMap`: `scale`, `xChannelSelector`, `yChannelSelector`; the map reads unpremultiplied values in the color space of the primitive; the displaced image is not converted.
- `feTurbulence`: `baseFrequency`, `numOctaves`, `seed`, `stitchTiles`, `type`. Implement the algorithm from the reference code in the specification, evaluated at the user space point of each pixel center, so the noise does not change with the output scale. Bound `numOctaves`.

### 6.3 X4: lighting

- `feDiffuseLighting` and `feSpecularLighting` with `surfaceScale`, `diffuseConstant`, `specularConstant`, `specularExponent`, `lighting-color`, and the surface normal kernels of the specification, including the edge and corner kernels. `kernelUnitLength`: do what Chromium does.
- `feDistantLight`, `fePointLight`, `feSpotLight` (`specularExponent`, `limitingConeAngle`; match Chromium for the cone edge). Light positions are in user space and follow `primitiveUnits`.
- No light source, or more than one: section 4.4.
- The lighting color converts to the color space of the primitive before the computation.

## 7. Conformance and Evidence

- The suite has 397 tests under `filters/` (55 listed as passing, 325 as failing, 17 skipped today), and six tests in other directories that wait for filters. The manual results of the reference renderers for the filter category: resvg 337, Chrome 303, librsvg 280 of 380.
- X1 first re-checks the 55 passes: a test that passes today does so without any filter code. Each must still pass for the right reason once filters render, or get a true reason.
- Calibration: measure how many filter tests the Chromium oracle render passes under the criterion, per directory, and report it next to our counts. Do not change the criterion.
- Every filter test that still fails gets a specific reason. Passes of phases 1 to 3 must not regress.
- Repository tests: one feature document per primitive and per engine rule (regions and units, subregions, color spaces, scale independence at two output sizes, rotated and skewed elements, content outside the canvas, error cases), each with a reference image that was compared with the Chromium oracle before it was added. Unit tests for the graph resolution and for each primitive on small buffers with values worked out by hand.
- Blur accuracy: compare our blur with the oracle on a hard edge and on a point for several standard deviations, and report the largest channel difference.

## 8. Package X5: Integration

- Cross-feature tests: a filter with `clip-path`, `mask`, `opacity`, and a blend mode on one element; filters inside `pattern`, `marker`, and `mask` content; on `text`, `image`, `use`, a nested `svg`, and the root; nested filters; a filtered element under each of the bounded layers of section 4.5.
- Conformance totals per category next to the earlier counts, with the Chromium calibration.
- Hostile documents, each within `SvgLimits` or finished in bounded time and memory: a huge filter region, a huge standard deviation, a huge morphology radius, a large convolution kernel, many octaves, hundreds of primitives, a long filter list, deeply nested filters, a `feImage` cycle, hundreds of small filtered elements.
- Benchmarks against Svg.Skia and NetVips under the fairness rules of `SVG_DESIGN.md` section 9.3, after checking that the outputs are comparable: a document with many small drop shadows (the common use in diagrams and user interface art), a document with one large blur, and a document that chains several primitive kinds. Add them to `SvgBenchmarkCorpus`. Check that the documents of phases 1 to 3 are not slower than at the phase 3 merge commit.
- Profile with `--profile svg` and fix the hot spots that the profile shows, each with a measurement before and after. Candidates, to try only if the profile points at them: single-channel buffers for chains that start at `SourceAlpha`, vectorized box blur, avoiding the nested canvas for a single shape, the public layer effects where they are exact.
- WebGPU: state what works and what does not, without a GPU in the container.
- Update `SVG_DESIGN.md`: sections 7 and 11 (the engine decision), the feature table, status, measured results, known gaps. Remove plan package names from source comments.

## 9. Hand-back Report

As in `SVG_IMPLEMENTATION.md` section 9. Do not commit and do not push; the main session reviews, commits, and merges. A blocked action (for example replacing an existing reference image) is reported, not worked around.
