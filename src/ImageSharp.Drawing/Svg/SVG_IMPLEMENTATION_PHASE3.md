# SVG Rendering: Implementation Plan (Phase 3, Text)

This document continues `SVG_IMPLEMENTATION.md` and `SVG_IMPLEMENTATION_PHASE2.md`. Read `SVG_DESIGN.md`, both earlier plans, and the repository `AGENTS.md` first. All their rules still apply: no `record` types, documentation on every member, simple technical English, Release builds, zero warnings in `src/`, no new package reference in `src/`, no existing reference image or expected result edited to silence a failure, no issue, no pull request, no push.

Where this plan says "verify", the statement comes from reading source or from memory. Check it against the code or with a test before relying on it. When the code contradicts the plan, follow the code and record the difference in the hand-back report.

When a rendering choice is open, follow what Chromium does, then what Skia and librsvg do. This is a decision of the user.

Phase 3 scope (`SVG_DESIGN.md` section 11): `text`, `tspan`, `textPath`, anchors, font matching and fallback. This plan also covers `tref`, text decoration, baselines, `textLength`, vertical writing, and bidirectional text, because the conformance suite tests them and they share one layout pipeline.

## 1. Work Packages

| Package | Content | Depends on |
|---|---|---|
| T1 | Text content in the tree, all text attributes and properties, font options and font matching, the layout pipeline for horizontal text, rendering as glyph outlines, the conformance setup for fonts. | Phase 2 |
| T2 | `textPath`, `textLength`, `lengthAdjust`. | T1 |
| T3 | `text-decoration`, baselines (`dominant-baseline`, `alignment-baseline`, `baseline-shift`), font features (`font-variant`, `font-kerning`, `kerning`). | T1 |
| T4 | Vertical `writing-mode`, `direction`, `unicode-bidi`, complex scripts, `tref`, `text-rendering`. | T1 |
| T5 | Integration: cross-feature tests, conformance totals, benchmarks with text, hostile documents, document status. | T2, T3, T4 |

T2, T3, and T4 run at the same time in separate work trees. T1 therefore builds the pipeline as separate stages in separate files (section 4), parses everything all packages need, and leaves a documented hook for each later feature. T2, T3, and T4 each put their feature in new files and keep edits to shared files to short hooks, with new enum members and fields at the end. In the conformance expected-results list, a package changes only the lines of its own features.

## 2. Public API Additions (Package T1)

Text needs fonts, and fonts differ per machine. The options make the choice explicit:

- the font collection to search (default: the system fonts)
- the family for each generic name: `serif`, `sans-serif`, `monospace`, `cursive`, `fantasy`
- the default family, used when the document names no family or none of the named families exists
- the families to try, in order, for a character that the chosen font does not have

Design the members in the style of the existing `SvgLoadOptions` and of the SixLabors.Fonts API (verify `IReadOnlyFontCollection`, `FontCollection`, `SystemFonts`, `TextOptions.FallbackFontFamilies`). Requirements:

- Fonts load from the collection only. The document never causes a font file to be read from a path or a URL. `@font-face` rules are ignored.
- The defaults follow browsers: `serif` is the default family. When a configured family is not in the collection, fall back in a fixed, documented order, so that the result depends only on the collection.
- A document whose text finds no font at all draws no text and does not fail.
- `SvgDocument` stays immutable, thread-safe, and not disposable.

Add a limit to `SvgLimits` for the total number of characters laid out per document, counted through `use` instances.

## 3. Parsing (Package T1)

### 3.1 Text content

Phase 1 reads with `IgnoreWhitespace` on and stores elements only. Text needs character data in document order between child elements.

- Keep character data only inside `text` and its descendants, so other documents do not pay for it. Measure `LoadSvg` for the phase 1 documents before and after; they must not load slower or allocate more beyond noise.
- White space: `xml:space="default"` and `"preserve"` as SVG 1.1 defines them, and the CSS `white-space` property. Match Chromium where the two disagree.
- Entities and CDATA sections count as text. Limit the size through `SvgLimits`.
- Package S reads the input a second time for `:empty`, because the first read drops white space. Keep `:empty` correct.

### 3.2 Attributes

| Element | Attributes |
|---|---|
| `text`, `tspan` | `x`, `y`, `dx`, `dy`, `rotate` (each a list), `textLength`, `lengthAdjust` |
| `textPath` | `href`, `xlink:href`, `startOffset`, `method`, `spacing`, `side`, `path` |
| `tref` | `href`, `xlink:href` |

### 3.3 Properties

Add each with the correct inheritance and initial value: `font-family` (a list, with generic names), `font-style`, `font-weight` (keywords, numbers, `bolder`, `lighter`), `font-stretch`, `font-variant`, the `font` shorthand, `font-size-adjust`, `font-kerning`, `text-anchor`, `text-decoration` (with the `-line`, `-style`, `-color` longhands), `letter-spacing`, `word-spacing`, `dominant-baseline`, `alignment-baseline`, `baseline-shift`, `writing-mode` (SVG 1.1 and CSS values), `direction`, `unicode-bidi`, `white-space`, `text-rendering`, and the `kerning` attribute. Phase 1 has `font-size`; add its keywords and `larger` / `smaller` if they are missing.

T1 parses all of them and exposes them in the computed style. T2, T3, and T4 only read them.

## 4. Layout Pipeline (Package T1 builds it; T2, T3, T4 extend it)

SVG text layout is not paragraph layout. It works on one `text` element at a time:

1. **Characters.** Flatten the `text` subtree into one character sequence after white-space processing. Each character keeps its source element, its computed style, and its entries from the `x`, `y`, `dx`, `dy`, `rotate` lists. The lists of an ancestor apply to characters of its descendants by position; a descendant's own list wins.
2. **Fonts.** Resolve a font for each run of equal style with the CSS font matching rules: family list in order, then stretch, style, and weight matching. Split runs where the font lacks a character and use the fallback families.
3. **Shaping.** Shape each run with SixLabors.Fonts to get glyphs, advances, offsets, and the mapping from glyphs to characters (verify which API gives this: the text measuring and layout types, or the glyph renderer callbacks). Ligatures form only where no character inside the cluster has its own position.
4. **Positions.** Walk the clusters: apply absolute `x` and `y` (each starts a new text chunk), `dx` and `dy`, `letter-spacing`, `word-spacing`, and `rotate`. T3 adds baseline shifts here. T4 adds vertical advance and bidi reordering here. T2 adds `textLength` here.
5. **Anchors.** Shift each text chunk for `text-anchor`, using the chunk's advance.
6. **Paths.** T2 maps chunks on a `textPath` to the path here.
7. **Output.** Emit each glyph as its outline under its position and rotation, as ordinary draw nodes, so fill, stroke, paint servers, `paint-order`, opacity, clips, and masks work as for shapes. Decorations (T3) are rectangles in the same order as browsers paint them.

Decisions:

- **Glyph outlines at load.** The document resolves text into paths when it loads, as resvg does. All existing machinery then applies, on both backends. Share one outline per font and glyph in a cache for the load, and place instances with transforms; do not copy geometry per glyph instance. Verify how `TextBuilder` and the glyph builders in `src/ImageSharp.Drawing/Text/` produce outlines and whether they can be reused. Colour glyphs (COLR, emoji) render with their layers if the existing glyph builders provide them; otherwise list them as a gap.
- **Bounding box.** The object bounding box of a `text` element is the union of its glyph cells (advance by ascent and descent), not the ink bounds. Paint servers in bounding-box units on text use the box of the whole `text` element, also for a `tspan`.
- **Text in `clipPath`.** `text` children add their glyph outlines to the clip. Phase 2 left this out; T1 adds it.
- **Paint on `tspan`.** Each `tspan` can set fill, stroke, and opacity. Group consecutive glyphs of equal paint into one draw node where the order of painting allows it.
- **Each stage in its own file** (`Text/` folder under `Svg/`), with internal contracts documented, because three packages extend the stages in parallel.

## 5. Packages T2, T3, T4

### 5.1 T2: `textPath`, `textLength`

- `textPath` places each cluster at the path point of the middle of its advance and rotates it to the path direction. Clusters whose middle is before the start or after the end of the path do not render. `startOffset` takes a length or a percentage of the path length. The referenced element is a `path`, or any basic shape (SVG 2); the `path` attribute too if Chromium supports it. The transform of the referenced path applies.
- `x`, `dx` move along the path; `y`, `dy` move perpendicular to it. `text-anchor` shifts along the path. A closed path wraps as Chromium does. `side`, `method`, and `spacing`: match Chromium.
- Text after a `textPath` continues at the end point of the path.
- `textLength` with `lengthAdjust="spacing"` adjusts the advances; `"spacingAndGlyphs"` also scales the glyphs along the inline direction. It applies per element that sets it, including nested `tspan` elements and text on a path.
- Path length and point-at-length: verify what `IPath` and its internals offer and reuse the code that package F wrote for marker directions where it fits.

### 5.2 T3: decoration, baselines, font features

- `text-decoration`: underline, overline, and line-through, with position and thickness from the font metrics. The paint of a decoration comes from the element that set the decoration, not from the element of the text; decorations of nested elements paint in the order Chromium uses. `text-decoration-style` other than `solid` and `text-decoration-color`: match Chromium if cheap.
- `dominant-baseline` and `alignment-baseline`: use the font baseline tables where the font has them and the CSS fallback rules otherwise. `baseline-shift`: `sub`, `super`, lengths, and percentages, cumulative through nested elements.
- `font-variant: small-caps`, `font-kerning`, and the `kerning` attribute through shaping features (verify what SixLabors.Fonts exposes).

### 5.3 T4: vertical text, bidi, scripts, `tref`

- `writing-mode` vertical: glyphs advance downward, upright or rotated by script as Chromium does, with vertical metrics where the font has them. `glyph-orientation-vertical` is deprecated; support `0` and `90` if Chromium does.
- `direction` and `unicode-bidi`: reorder with the Unicode bidirectional algorithm (verify what SixLabors.Fonts provides). `text-anchor` follows the direction. Position lists apply in logical order.
- Complex scripts (Arabic, Devanagari): shaping comes from SixLabors.Fonts; check the suite's tests and report the differences.
- `tref`: removed from SVG 2 and from Chromium; resvg supports it. Follow Chromium (no rendering) unless the cost of supporting it is small; report the choice and the test counts either way.
- `text-rendering`: map to antialiasing or hinting only if the canvas offers a matching control.

## 6. Conformance and Evidence

- **Fonts of the suite.** The resvg suite ships its fonts in `fonts/` with their licenses. The runner loads them into a font collection and sets the generic families as the suite expects (read the suite documentation and the resvg test harness notes for the mapping; verify, do not guess). Do not copy the suite's fonts into the repository.
- **Oracle with the same fonts.** Chromium must render with the same font files, or the comparison means nothing. Make the suite's fonts available to the oracle (a per-user font directory in the container, or `@font-face` injection in `tests/SvgTools/render-svg.js`) and document how.
- **Calibration.** The phase 1 calibration of the pass criterion left out the text category. With the fonts in place, measure how many text tests the Chromium render passes, and report it. Do not change the pass criterion. Shaping and hinting differ between engines, so some text tests will fail for small position differences; give those a specific reason.
- **Repository tests.** Feature tests use fonts that are already in `tests/ImageSharp.Drawing.Tests/TestFonts/`. Each new reference image needs an oracle comparison with the same font, as before.
- Every `text` test in the list that still fails gets a specific reason. Phase 1 and phase 2 passes must not regress.

## 7. Package T5: Integration

- Cross-feature tests: text with gradients and patterns in both unit types, text in `clipPath` and `mask`, text in `pattern` and `marker` content, `use` of text, text under `switch` with `systemLanguage`, CSS selectors that set font properties.
- Conformance totals per category next to the earlier counts.
- Benchmarks: documents with text against Svg.Skia and NetVips with the same font files available to every renderer (fairness rules: `SVG_DESIGN.md` section 9.3). Check output comparability first. Report load cost separately, because text resolves at load.
- Hostile documents: very long text, very many `tspan` elements, huge position lists, a `use` storm of text, text on a very long path, fonts with very many glyphs. Each stays within `SvgLimits` or finishes in bounded time.
- Update `SVG_DESIGN.md`: status, feature table, known gaps, measured results.

## 8. Hand-back Report

As in `SVG_IMPLEMENTATION.md` section 9. Do not commit and do not push; the main session reviews, commits, and merges.
