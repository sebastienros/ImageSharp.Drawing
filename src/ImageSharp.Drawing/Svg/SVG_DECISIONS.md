# SVG Rendering: Register of Decisions

Date: 28 November 2026. State of the code: commit `80aa272` of the SVG branch of the fork.

This register lists the choices that the SVG work made without an answer from the user. On 28 November 2026 the user asked to "Document the choices you made that were pending for my answers and keep them. So we can decide to revisit in a future session." So every choice in Part A stays as implemented. Each entry stands alone: it says what was decided, why, what the other option would change, where the choice lives in the code and the tests, and what to check to reverse it. A later session can revisit one entry without the others.

The register has four parts:

- **Part A**: choices that the assistant made where the user was asked, or should have been asked, and gave no answer. Each entry has a stable identifier (D1, D2, ...). Identifiers are not reused.
- **Part B**: decisions that the user made. They are not open; they are listed so that a reader can tell the two kinds apart.
- **Part C**: improvements that were proposed and not built.
- **Part D**: defects of upstream code that are tracked as issues in the fork, and what the SVG code does around each one.

Sources: `SVG_DESIGN.md` (the section numbers below refer to it), the plans `SVG_IMPLEMENTATION*.md`, the code under `src/ImageSharp.Drawing/Svg/` and the tests under `tests/ImageSharp.Drawing.Tests/Svg/`, the reports that the assistant gave the user, and the answers of the user. "Not analysed" means that no source states the effect; it was not estimated for this register. A sentence marked "(inference)" is a reading of the sources, not a statement of them.

## Part A: Choices Made Without an Answer from the User

The status of every entry in this part is "chosen by the assistant, not confirmed by the user". The status field gives the date of the choice, or of the report that named it, where the sources give it.

### General rule

#### D1. The rule for open rendering choices

- **Question**: when the specifications leave a rendering case open, or when the specification and the browsers disagree, which behaviour does the renderer follow?
- **Choice**: the specification first; where the specification leaves a choice, the rendered output of Chromium (the oracle in `tests/SvgTools/`, used as a black box). The plans state the rule (for example `SVG_IMPLEMENTATION_PHASE4.md`, "Decide each case from the specification first, then check Chromium with the oracle"), and `SVG_DESIGN.md` 15.1 and 16.1 describe it.
- **Reason**: the user said "follow what skia/netvips do" on 25 November 2026, about the corners of rounded rectangles (Part B). The plans had already chosen Chromium as the oracle for new reference images (`SVG_IMPLEMENTATION.md` section 6.1). Chromium renders the whole feature set, and its output can be produced for every test document; Svg.Skia and librsvg do not render all of it (section 4.2).
- **Alternative**: decide open cases by the output of Svg.Skia and NetVips (librsvg). Where those two disagree with each other or do not support a feature, a further rule would be needed. The effect on output and on the conformance count is not analysed.
- **Where**: the plans `SVG_IMPLEMENTATION_PHASE2.md` to `SVG_IMPLEMENTATION_PHASE5.md`; code comments that say "as Chromium" or "as in Chromium" throughout `src/ImageSharp.Drawing/Svg/`; the tables of `SVG_DESIGN.md` 16.4 and 17.4.
- **To revisit**: confirm or replace the rule. Each later entry of this part that says "as Chromium" would then need a check against the new rule. Not analysed further.
- **Status**: chosen by the assistant, not confirmed by the user. The rule is in the plans from 25 November 2026. The reports to the user referred to it as "your rule" (inference: the assistant read the user's words about rounded corners as a general rule; the user did not object, and did not confirm it).

### Text (phase 3)

#### D2. `baseline-shift` on the `text` element

- **Question**: does `baseline-shift` apply when it is set on the `text` element itself?
- **Choice**: no. The shift adds up through the spans of a cluster, and the span of the `text` element is skipped.
- **Reason**: the specification applies the property to `tspan` and `textPath` only, not to `text`; resvg and the resvg suite follow that. Chromium applies it.
- **Alternative**: apply it as Chromium does. The change in conformance tests is not analysed.
- **Where**: `src/ImageSharp.Drawing/Svg/Text/SvgTextPositioner.Baselines.cs` (`ApplyBaselineShift`: "span 0 is the text element, where the property does not apply"); `SVG_DESIGN.md` 15.1.
- **To revisit**: include span 0 in the sum; run `SvgTextTests` and the text category of `SvgResvgConformanceTests`. Not analysed further.
- **Status**: chosen by the assistant, not confirmed by the user; reported on 26 November 2026 ("Tell me if you want Chromium's behaviour for any of them"), no answer.

#### D3. Paint and metrics of text decorations

- **Question**: which paint does an underline, overline, or line-through take, and how thick is it?
- **Choice**: each line takes the fill and stroke of the nearest span that sets the line (SVG 1.1 section 10.12). Position and thickness come from the metrics of the primary font of the span (`UnderlinePosition`, `UnderlineThickness`, `StrikeoutPosition`, `StrikeoutSize`; the overline on the ascent with the underline thickness), as resvg does. Lines are solid; `text-decoration-color` is not used.
- **Reason**: SVG 1.1 defines the paint rule, and resvg and the resvg suite follow it. Chromium paints every line of a fragment with the paint of the innermost span that sets any line, uses a thickness of a tenth of the font size, and puts the line-through at a third of the ascent.
- **Alternative**: Chromium's paint rule and metrics. The change in conformance tests is not analysed.
- **Where**: `src/ImageSharp.Drawing/Svg/Resolve/SvgResolver.TextDecorations.cs` (class remarks and `GetDecorationMetrics`); `SVG_DESIGN.md` 15.1 and 15.5.
- **To revisit**: change the paint lookup and `GetDecorationMetrics`; the reference images of the decoration feature tests and the TextDecorations benchmark document would change. Not analysed further.
- **Status**: chosen by the assistant, not confirmed by the user; reported on 26 November 2026, no answer.

#### D4. The `kerning` property

- **Question**: what does the SVG 1.1 `kerning` property do?
- **Choice**: a length value turns kerning off, as SVG 1.1 defines it. A `kerning` length adds no spacing (15.5).
- **Reason**: SVG 1.1 defines the property; SVG 2 removed it. Chromium, Firefox, and Safari ignore it.
- **Alternative**: ignore the property, as the browsers do.
- **Where**: `src/ImageSharp.Drawing/Svg/Text/SvgTextShaper.Features.cs` (`style.Kerning.IsSpecified` sets `KerningMode.None`); `SVG_DESIGN.md` 15.5.
- **To revisit**: remove the condition on `style.Kerning`; run `SvgTextTests` and the text conformance tests. Not analysed further.
- **Status**: chosen by the assistant, not confirmed by the user; reported on 26 November 2026, no answer.

#### D5. `tref` renders

- **Question**: does `tref` render?
- **Choice**: yes. A `tref` adds the character data of the `text` element that it references in the same document, as resvg does.
- **Reason**: the phase 3 plan said to follow Chromium (no rendering) unless support was cheap, and to report the choice. SVG 1.1 and resvg render it; the cost was small.
- **Alternative**: ignore `tref`, as SVG 2 and Chromium do. That removes `SvgTextCharacters.Tref.cs` and its limit test.
- **Where**: `src/ImageSharp.Drawing/Svg/Text/SvgTextCharacters.Tref.cs`, the `Tref` cases of `src/ImageSharp.Drawing/Svg/Parsing/SvgReader.cs`; `SvgTextHostileDocumentTests.TrefFanOut_StopsAtTheCharacterLimit`; `SVG_DESIGN.md` 15.1 and 15.2 (1 to 3 text tests fail on `tref`).
- **To revisit**: drop the `tref` characters; the `tref` tests of the resvg suite would change state. Which ones pass today is not analysed here.
- **Status**: chosen by the assistant, not confirmed by the user; listed among the "older choices I made that you never confirmed" on 28 November 2026.

#### D6. Text on a path follows Chromium where SVG 2 allows more

- **Question**: what may `textPath` reference, and do `method`, `spacing`, and closed paths act?
- **Choice**: only `path` elements and the `path` attribute; `method` and `spacing` have no effect; text does not wrap around a closed path. This follows Chromium.
- **Reason**: SVG 2 also allows basic shapes and defines `method` and `spacing`, but no browser renders those (17.8: "Text on basic shapes ... no browser renders it").
- **Alternative**: the SVG 2 rules. This is the opposite direction to D2 to D4, where the specification won over Chromium (inference: here the plan treated a feature that no browser renders as out of scope).
- **Where**: `src/ImageSharp.Drawing/Svg/Text/SvgTextPaths.OnPath.cs` (class remarks); `SVG_DESIGN.md` 15.1, 15.5, 17.8.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; reported on 26 November 2026 ("T2 follows Chromium where the plan said otherwise"), no answer.

#### D7. Language matching of `systemLanguage` and its default

- **Question**: how does `systemLanguage` match the languages of the load options, and what is the default language?
- **Choice**: a tag matches when it is equal to an entry or starts with the entry followed by `-`, in both directions (`en` matches `en-US`, and `en-US` matches `en`). The default of `SvgLoadOptions.Languages` is `en`, not the culture of the machine.
- **Reason**: the stricter one-way rule broke two tests that passed before. A fixed default gives the same output on every machine.
- **Alternative**: one-way prefix matching; or a default from the current culture.
- **Where**: `src/ImageSharp.Drawing/Svg/SvgLoadOptions.cs` (`Languages`), `src/ImageSharp.Drawing/Svg/Parsing/SvgConditionalAttributes.cs`; `SVG_DESIGN.md` 4.2 (`switch`).
- **To revisit**: change the match; the two tests named in the phase 2 report would fail again (their names are not in the sources read for this register).
- **Status**: chosen by the assistant, not confirmed by the user; reported on 25 November 2026 ("One behaviour differs from the plan"), no answer.

### Filters (phase 4)

#### D8. A filter list has at most 32 entries

- **Question**: how long may a `filter` list be?
- **Choice**: a list of more than 32 entries does not draw the element. The limit is an internal constant, not a property of `SvgLimits`.
- **Reason**: before the cap, a list of 400 entries took 99 s. No real document comes near 32 entries, and the other bounds on primitive parameters are constants too (16.5).
- **Alternative**: a higher cap, or a public property in `SvgLimits` (new public API).
- **Where**: `src/ImageSharp.Drawing/Svg/Filters/SvgResolver.Filter.cs` (`MaxFilterListEntries`); `SvgFilterHostileDocumentTests` (a list of 20,000 entries); `SVG_DESIGN.md` 16.5.
- **To revisit**: change the constant; check that the hostile document test still finishes within its growth bound. No conformance test is known to depend on it (not analysed).
- **Status**: chosen by the assistant, not confirmed by the user; reported on 26 November 2026 ("Two limits were added without asking; say if you want them changed"), no answer.

#### D9. A convolution kernel has at most 256 entries

- **Question**: how large may an `feConvolveMatrix` kernel be?
- **Choice**: a valid kernel of more than 256 entries (16 × 16) gives transparent black.
- **Reason**: it bounds the work at 256 multiply-adds of four channels per output pixel; the phase 4 report gives 256 as the limit observed in the Chromium output.
- **Alternative**: a larger bound, or a bound on pixels times kernel entries.
- **Where**: `src/ImageSharp.Drawing/Svg/Filters/SvgFeConvolveMatrix.cs` (`MaxKernelEntries`); `SvgFilterHostileDocumentTests` (the 16 × 16 kernel case); `SVG_DESIGN.md` 16.5.
- **To revisit**: change the constant; run `SvgFilterHostileDocumentTests` and the `filters/feConvolveMatrix` conformance tests. Not analysed further.
- **Status**: chosen by the assistant, not confirmed by the user; reported on 26 November 2026, no answer.

#### D10. An `feImage` reference cycle

- **Question**: what happens when an `feImage` reference closes a cycle?
- **Choice**: the element that closes the cycle is dropped, as the resvg references show.
- **Reason**: reference cycles are errors in SVG, and Filter Effects 1 does not define the result. The phase 4 report said that "by your rule this should arguably be Chromium's behaviour". Chromium renders the content (32,041 to 90,000 mismatched pixels).
- **Alternative**: render as Chromium does: −3 passing tests (16.4).
- **Where**: `src/ImageSharp.Drawing/Svg/Filters/SvgFeImage.cs` and the cycle checks of the resolve stage; `SVG_DESIGN.md` 16.4 (row "An `feImage` cycle drops the element that closes it").
- **To revisit**: three passing `feImage` tests would fail (16.4).
- **Status**: the user decided "keep the spec for the phase 4 filter rules" on 26 November 2026 (Part B). This rule is listed in the same table, but the specification does not settle it, so it is not clear that the decision covers it (inference). Listed here so that it can be confirmed.

#### D11. `feTurbulence` from the W3C reference code

- **Question**: may the turbulence code follow the reference code of the W3C specifications, and which version of it?
- **Choice**: the random number generator, the lattice setup, the noise function, and the turbulence function follow the reference code of Filter Effects 1, translated to C# and changed to compute four channels at a time. The gradient setup follows the reference code of SVG 1.1 (every vector normalized), not the newer code of Filter Effects 1 (a new gradient when a vector is longer than 1), and keeps the guard of the newer code against a zero vector. `THIRD-PARTY-NOTICES` carries a W3C notice that names both specifications.
- **Reason**: the reference code defines the output, so it cannot be rewritten from the text alone. With the SVG 1.1 table every turbulence render of the suite matches the Chromium oracle within one or two 8-bit levels; with the newer table the noise is a different pattern.
- **Alternative**: the setup of Filter Effects 1; the output of every turbulence test and of the FilterChain benchmark document would change (not analysed further).
- **Where**: `src/ImageSharp.Drawing/Svg/Filters/SvgFeTurbulence.cs` (class remarks); `THIRD-PARTY-NOTICES` (section "W3C specifications"); `SVG_DESIGN.md` 16.1.
- **To revisit**: unresolved: SVG 1.1 was published under the older W3C document license, and the notice links the current W3C software and document license. Whether the terms of SVG 1.1 permit this use was not checked. The user's decision on Chromium-derived code (Part B) did not cover W3C reference code.
- **Status**: chosen by the assistant, not confirmed by the user; reported on 26 November 2026 ("check that if the exact terms matter to you"), no answer.

#### D12. Color conversions from the CSS Color 4 sample code

- **Question**: may the conversions between CSS color spaces follow the sample code of CSS Color 4?
- **Choice**: yes. The white points, the conversion matrices, and the transfer functions follow the sample code, translated to C#, with a W3C notice in `THIRD-PARTY-NOTICES`.
- **Reason**: the sample code defines the values; the W3C software license asks only for the notice.
- **Alternative**: derive the conversions from the text of the specification; whether that gives the same values is not analysed.
- **Where**: `src/ImageSharp.Drawing/Svg/Parsing/SvgCssColor.cs`; `THIRD-PARTY-NOTICES`.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; reported on 26 November 2026 as a license note, without a question.

### Phase 5: the specification against Chromium (`SVG_DESIGN.md` 17.4)

The rows of 17.4 that the user decided are in Part B. Of the other rows, these are not entries: `font-feature-settings` with a value above 1 and `"calt" 0` are limits of SixLabors.Fonts (Part D, issue 10); `currentColor` inside `color-mix()` is a known gap (17.7); `Identify` went with the decoder. Each entry below is one row; "Fail" and "If changed" are the counts of 17.4, which are 0 for every row here, so no conformance test changes state for any of them.

#### D13. `ch`

- **Question**: is `ch` the exact advance of "0" or a rounded one?
- **Choice**: the exact advance. Chromium rounds to whole pixels (10ch of Open Sans at 20px is 110px there, 114.4px here).
- **Reason**: CSS Values 4 defines the advance; the reference image of `shapes/rect/ch-values` has the exact length.
- **Alternative**: round as Chromium; `shapes/rect/ch-values` would likely fail (inference from 17.2).
- **Where**: the `ch` conversion (`SvgUnitBasis.WithFont` callers, 17.7); `SVG_DESIGN.md` 17.4.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D14. `rec2020` in `color()`

- **Question**: which transfer curve does `rec2020` use?
- **Choice**: the power 2.4 of the CSS Color 4 sample code. Chromium uses another curve: `color(rec2020 0.5 0.3 0.2)` is (163, 83, 61) there and (144, 56, 32) here.
- **Reason**: the specification.
- **Alternative**: Chromium's curve.
- **Where**: `src/ImageSharp.Drawing/Svg/Parsing/SvgCssColor.cs`, `src/ImageSharp.Drawing/Svg/Parsing/SvgColorSpace.cs`; `SVG_DESIGN.md` 17.4.
- **To revisit**: the color tests of phase 5; not analysed further.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D15. Gradient interpolation of the new color functions

- **Question**: in which space do gradient stops given with `oklch()` and the other new functions interpolate?
- **Choice**: sRGB, for every color. Chromium uses Oklab (an `oklch()` gradient differs by up to 24 levels in the middle).
- **Reason**: SVG 2 and `color-interpolation` define sRGB.
- **Alternative**: Oklab for non-legacy colors.
- **Where**: the gradient paint of the resolve stage; `SVG_DESIGN.md` 17.4 and 17.7.
- **To revisit**: the cross-feature document with new colors in gradient stops (`SvgCrossFeatureRenderTests`) would change. Not analysed further.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D16. Division by zero in `calc()`

- **Question**: what does a `calc()` that divides by zero give?
- **Choice**: the declaration is dropped (a result that is not finite is refused), so the attribute applies.
- **Reason**: 17.4 records the rule without a reason. CSS Values 4 gives infinity, clamped to the range of the property; Chromium gives the largest width it supports. So this rule follows neither the specification nor Chromium.
- **Alternative**: clamp infinity to the range of the property, as the specification says.
- **Where**: `src/ImageSharp.Drawing/Svg/Parsing/SvgCalc.cs` (`TryNarrow`); `SVG_DESIGN.md` 17.4.
- **To revisit**: the math function tests of phase 5. Not analysed further.
- **Status**: chosen by the assistant, not confirmed by the user; reported on 26 November 2026, no answer.

#### D17. `hsl()` saturation above 100%

- **Question**: is a saturation above 100% clamped?
- **Choice**: yes. Chromium does the same for the cases checked.
- **Reason**: CSS Color 4.
- **Alternative**: none known; the specification and Chromium agree.
- **Where**: the color parser under `src/ImageSharp.Drawing/Svg/Parsing/`; `SVG_DESIGN.md` 17.4.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D18. `:lang()`

- **Question**: which form of `:lang()` is supported?
- **Choice**: Selectors 4: lists, quoted ranges, wildcards, and extended filtering (`de-DE` matches `de-Latn-DE`). Chromium has the Level 3 form only; both agree on every Level 3 selector.
- **Reason**: the specification.
- **Alternative**: the Level 3 form.
- **Where**: `src/ImageSharp.Drawing/Svg/Css/SvgCssLanguageRanges.cs`; `SVG_DESIGN.md` 17.4.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D19. `pathLength="0"` with a pattern that starts with a zero dash

- **Question**: what draws?
- **Choice**: as SVG 2: every distance but 0 becomes infinite, so only the caps of the zero dash draw. Chromium draws a solid stroke.
- **Reason**: the specification.
- **Alternative**: a solid stroke, as Chromium.
- **Where**: `src/ImageSharp.Drawing/Svg/Resolve/SvgResolver.PathLength.cs`; `SVG_DESIGN.md` 17.4.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D20. `pathLength` with an infinite dash offset

- **Question**: what draws?
- **Choice**: the pattern is ignored, as Chromium does.
- **Reason**: the result is undefined, so the rule of D1 applies.
- **Alternative**: not stated in the sources.
- **Where**: `src/ImageSharp.Drawing/Svg/Resolve/SvgResolver.PathLength.cs`; `SVG_DESIGN.md` 17.4.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D21. `pathLength` that is not a number

- **Question**: what does an invalid `pathLength` do?
- **Choice**: nothing; an invalid attribute has no effect (SVG 2). Chromium treats it as zero.
- **Reason**: the specification.
- **Alternative**: treat it as zero, as Chromium.
- **Where**: the `pathLength` parsing in `src/ImageSharp.Drawing/Svg/Parsing/SvgReader.cs`; `SVG_DESIGN.md` 17.4.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D22. `image-rendering="optimizeSpeed"`

- **Question**: how does an `image` with `optimizeSpeed` sample?
- **Choice**: nearest neighbor, as `crisp-edges` (CSS Images 3). Chromium samples smoothly.
- **Reason**: the specification.
- **Alternative**: smooth sampling, as Chromium.
- **Where**: `src/ImageSharp.Drawing/Svg/Css/SvgImageRendering.cs`, `src/ImageSharp.Drawing/Svg/Css/SvgPropertyParser.cs`, and the `image` paint; `SVG_DESIGN.md` 17.4.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D23. Default reference box of a basic shape in `clip-path`

- **Question**: which box does a basic shape use when none is named?
- **Choice**: the stroke box (CSS Masking: `border-box`, which is the stroke box on SVG elements). Chromium uses the stroke box too. The phase 5 plan had named the object bounding box.
- **Reason**: the specification and Chromium agree.
- **Alternative**: the object bounding box, as the plan said.
- **Where**: `src/ImageSharp.Drawing/Svg/Resolve/SvgResolver.ClipShape.cs`, `src/ImageSharp.Drawing/Svg/Css/SvgGeometryBox.cs`; `SVG_DESIGN.md` 17.4.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D24. A basic shape with `stroke-box` on a `clipPath` element

- **Question**: which element's box does `stroke-box` mean when the shape is set on a `clipPath` element?
- **Choice**: as Chromium: the fill box of the clipped element, also when it has a stroke.
- **Reason**: 17.4 gives the Chromium output; CSS Masking says the box of the element that the clip applies to (inference: the stroke box would follow from the specification, so this row follows Chromium against it; the sources do not give the reason).
- **Alternative**: the stroke box of the clipped element.
- **Where**: `src/ImageSharp.Drawing/Svg/Resolve/SvgResolver.ClipShape.cs`; `SVG_DESIGN.md` 17.4.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D25. `light-dark()`

- **Question**: which color does `light-dark()` give?
- **Choice**: the first (light) color. The load options have no color scheme; the Chromium oracle renders an SVG document as an image and gives the light color.
- **Reason**: CSS Color 5 selects by the color scheme, and there is none.
- **Alternative**: a color scheme option on `SvgLoadOptions` (new public API).
- **Where**: `src/ImageSharp.Drawing/Svg/Parsing/SvgColorParser.Css4.cs`; `SVG_DESIGN.md` 17.4.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

#### D26. Math functions in text position lists

- **Question**: are `calc()` and the other math functions accepted in `x`, `y`, `dx`, `dy`, and `rotate` lists of text?
- **Choice**: no.
- **Reason**: CSS Values 4 does not cover SVG number lists. Chromium accepts a function without spaces (`calc(10px)`), not `calc(100px + 10px)` or `max(5px, 10px)`.
- **Alternative**: accept them in some form.
- **Where**: the position list parsing in `src/ImageSharp.Drawing/Svg/Parsing/SvgReader.cs`; `SVG_DESIGN.md` 17.4 and 17.7.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026.

### Shapes and reference images (phase 2)

#### D27. Two reference images replaced after the gradient change

- **Question**: may the reference images that change because of the unassociated gradient interpolation be replaced?
- **Choice**: `fluent-moose` and `fluent-soft-ice-cream` were replaced after a comparison with the Chromium oracle: 0.366% → 0.193% and 0.124% → 0.098% from the oracle. (`root-stretch` changed by the corner decision, which the user made.)
- **Reason**: the user ordered the gradient change ("make the gradient option public", Part B); the new images are closer to Chromium.
- **Alternative**: revert the two images; then the gradient change would fail those two tests.
- **Where**: `tests/Images/ReferenceOutput/Svg/SvgRenderTests/DrawSvg_Corpus_fluent-moose.png`, `DrawSvg_Corpus_fluent-soft-ice-cream.png`; `SVG_DESIGN.md` 14.
- **To revisit**: nothing to do unless the user wants the old images; the report said "say if you want those reverted".
- **Status**: chosen by the assistant, not confirmed by the user; 25 November 2026. The user's order of the gradient change implies the new images (inference), so this entry is unlikely to be reversed.

#### D28. Core rounded rectangles and path arcs stay unchanged

- **Question**: should the core `RoundedRectanglePolygon` and the arcs of SVG path data (`A` commands), which flatten once in their own space, be fixed in core code?
- **Choice**: no core change. SVG `rect` builds its corners from cubic curves (Part B); path data arcs still go through `ArcLineSegment` and can look polygonal when a small shape is scaled up.
- **Reason**: the report of 25 November 2026 said "that is a core change for you to decide". Later the defect was filed in the fork (Part D, issue 3), following the user's instruction to file upstream defects there.
- **Alternative**: convert arcs of path data to cubic curves in the SVG code, or fix `ArcLineSegment` in core.
- **Where**: `src/ImageSharp.Drawing/Svg/Resolve/SvgResolver.cs` (`CreateRect`); `SVG_DESIGN.md` 14.3.
- **To revisit**: a change of the arcs would change the reference images of documents with arcs; not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; asked on 25 November 2026, no answer. The fork issue tracks the core part.

### Phase 6 and Upstream Sync 1

#### D29. The curve bounds trade-off of package Z2

- **Question**: may curve flattening change so that a render at scale 1 costs more, when every other scale and every load costs less?
- **Choice**: kept. The bounds of a cubic segment come from a flattening at scale 1 that keeps no array, unless the cached flattening is at scale 1. A render at another scale now flattens each curve once instead of twice; a render at scale 1 flattens twice where it flattened once.
- **Reason**: the load keeps no flattening per curve: load and render of the Tiger at 1000 pixels allocates 6.80 → 4.01 MB, Paris 428.7 → 364.0 MB (19.1). `DrawBeziers`, a non-SVG benchmark at scale 1, measured +2.4 ± 6.7%, not separated.
- **Alternative**: keep the flattening of the bounds pass in the cache: scale-1 renders flatten once again, and loads allocate as before.
- **Where**: `src/ImageSharp.Drawing/CubicBezierLineSegment.cs` (`Bounds`), `FlattenedPointBuilder`; `SVG_DESIGN.md` 19.1 item 4.
- **To revisit**: run `DrawBeziers` and the load and render benchmarks of Tiger and Paris before and after. No pixel changes either way (19.1).
- **Status**: chosen by the assistant, not confirmed by the user; reported on 27 November 2026 ("One trade-off in core code"); listed as unconfirmed on 28 November 2026.

#### D30. The clear in the render benchmark

- **Question**: should `RenderSvg` clear the target area of each document before it draws it?
- **Choice**: it clears, for ImageSharp.Drawing (`canvas.Clear` with a transparent brush) and for Svg.Skia (`SKCanvas.Clear`). The Icons entry has 40 documents, so one operation clears the canvas 40 times.
- **Reason**: both renderers do the same work, and a render must not draw over the previous one. Measured before the fast clear of Z3 (Icons, 1000 pixels): one thread 113.7 ms with the clear, 28.6 ms without; Svg.Skia 13.9 and 7.2 ms (19.1).
- **Alternative**: draw without the clear, or clear once per operation. Every `RenderSvg` cell of sections 19 to 21 would change; `LoadAndRenderSvg` draws into a new image and is not affected.
- **Where**: `tests/ImageSharp.Drawing.Benchmarks/Svg/RenderSvg.cs` (`DrawImageSharp` and the Svg.Skia method), `tests/ImageSharp.Drawing.Benchmarks/Svg/SvgInvocationCurve.cs`; `SVG_DESIGN.md` 19.1 ("The clear in the render benchmark") and 19.2 item 1.
- **To revisit**: change both renderer methods together, then measure the render cells again.
- **Status**: chosen by the assistant, not confirmed by the user. The finding was reported on 27 November 2026 without a question; no answer is recorded.

#### D31. `Fill(Pen, IPath)` only for transforms that do not enlarge

- **Question**: should the emitter use `DrawingCanvas.Fill(Pen, IPath)`, which the upstream owner added, for every stroke under a non-conformal transform?
- **Choice**: only when the largest scale of the transform is at most 1. An enlarging transform keeps the emitter's own outline, built in a copy of user space scaled by the largest scale. `vector-effect: non-scaling-stroke` keeps its outline in host space.
- **Reason**: under an enlarging transform `Fill(Pen, IPath)` shows its flattening: round caps and joins become polygonal (the probe of 20.3: up to 11,611 pixels and a channel difference of 255), and it fails the reference image `DrawSvg_Feature root-stretch` (0.0301%). Below scale 1 it differs by one level in 22 pixels. For non-scaling strokes the canvas form differed by one level in up to 117 pixels of a probe with curves.
- **Alternative**: `Fill(Pen, IPath)` for every case: simpler code, one reference image fails, visible polygons under enlargement. Fork issue 2 holds the reproduction for upstream.
- **Where**: `src/ImageSharp.Drawing/Svg/Rendering/SvgEmitter.cs` (the stroke branch with `GetMaxScale`); `SVG_DESIGN.md` 7 and 20.3.
- **To revisit**: when upstream fixes issue 2, use `Fill(Pen, IPath)` for every case and check `DrawSvg_Feature root-stretch` and the probe of 20.3.
- **Status**: chosen by the assistant, not confirmed by the user; 27 November 2026. The user's rule "follow the requests from the maintainer" asks to report trade-offs such as a loss of quality; this was reported, with no answer.

#### D32. The side of an image whose size the document sets is limited to 65,535 pixels

- **Question**: how wide may an image be that `SvgDocument.Render` creates at the intrinsic size?
- **Choice**: at most 65,535 pixels on a side, in addition to `SvgLimits.MaxImagePixels`. The limit is an internal constant. A caller's own size (`Render(size)`, `DrawSvg` on a caller's canvas) is not limited.
- **Reason**: every rasterizer worker keeps scratch rows of the width of the canvas, about 128 bytes per pixel of width, so a document of 8,000,000 × 8 pixels, within the pixel limit, would make one worker allocate about 1 GB (the code comment; the phase 5 report gives a request of 1.2 GB). At 65,535 pixels a worker needs about 8 MB. Fork issue 6 is the core reason.
- **Alternative**: a public property of `SvgLimits` (new public API), or no limit once issue 6 is fixed in core.
- **Where**: `src/ImageSharp.Drawing/Svg/SvgDocument.Render.cs` (`MaxRenderSide`); `SvgDocumentRenderTests` (65,535 and 65,536 pixels wide); `SVG_DESIGN.md` 6 and 10.
- **To revisit**: change the constant and the two tests.
- **Status**: chosen by the assistant, not confirmed by the user; reported on 26 November 2026 ("The limit is an internal constant"), without a question. The user's decision "reuse MaxImagePixels, no separate MaxOutputPixels" covers the pixel limit, not this side limit.

#### D33. Three changes to the cost of the local copy of the bidirectional algorithm

- **Question**: may the local copy of the SixLabors.Fonts bidirectional algorithm differ from its source?
- **Choice**: the copy changes three places that took quadratic time, without changing the results: rule W2, the joining of level runs into isolating run sequences, and the search for the run after an isolate (a binary search instead of a linear one). It gives the levels of all 91,707 cases of the Unicode `BidiCharacterTest.txt`.
- **Reason**: the user allowed the copy with a comment that names the source (Part B). The unchanged copy made 200,000 bidirectional control characters with 100,000 letters take about 6 s; now about 0.3 s (15.5).
- **Alternative**: an exact copy of the source, so that it can be compared with upstream line by line; the quadratic cost would return.
- **Where**: `src/ImageSharp.Drawing/Svg/Text/Bidi/BidiAlgorithm.cs` (comments at the changed places); `THIRD-PARTY-NOTICES`; `SvgTextDirectionTests`; `SVG_DESIGN.md` 15.4 and 15.5.
- **To revisit**: the copy is to be removed when SixLabors.Fonts exposes the levels (fork issue 9); then the cost depends on the upstream code.
- **Status**: chosen by the assistant, not confirmed by the user; 26 November 2026 (package T6).

#### D34. `SvgGenericFontFamilies` is `init`-only too

- **Question**: the upstream owner asked for `init` accessors on `SvgLoadOptions` and `SvgLimits`. Should `SvgGenericFontFamilies` change too?
- **Choice**: yes. Every property of `SvgGenericFontFamilies` is an `init` accessor.
- **Reason**: the options refer to it, so it would otherwise stay changeable after a load, and the purpose of the request is that the options of a loaded document cannot change.
- **Alternative**: setters on `SvgGenericFontFamilies`, as the owner named only the two option classes.
- **Where**: `src/ImageSharp.Drawing/Svg/SvgGenericFontFamilies.cs`; `SVG_DESIGN.md` 20.1 ("Options").
- **To revisit**: a public API change; the tests that build the families (`SvgDocumentTests`) would need setters again.
- **Status**: chosen by the assistant, not confirmed by the user; reported on 27 November 2026, no answer.

#### D35. Four assertions removed in the upstream sync

- **Question**: what happens to tests whose assertions no longer hold after the sync?
- **Choice**: two assertions on `WebGPUSceneEncoder.EvaluateGradientColor` were removed, because upstream changed what it returns (the sample in the representation of the brush; the ramp associates it later), and the upstream WebGPU tests compare the ramp output with the CPU. Two assertions that a failed setter keeps the previous value were removed, because an `init` accessor that throws leaves no object.
- **Reason**: 20.6.
- **Alternative**: rewrite the gradient assertions against the new return value. The WebGPU tests skip in the test container (no GPU).
- **Where**: `tests/ImageSharp.Drawing.Tests/Processing/ProcessWithDrawingCanvasTests.AssociatedAlpha.cs` (the gradient assertions, removed in commit `03b13e9`), `tests/ImageSharp.Drawing.Tests/Svg/SvgDocumentTests.cs` (`Options_RejectInvalidPhase2Values`); `SVG_DESIGN.md` 20.6.
- **To revisit**: write the replacement assertions; no output changes.
- **Status**: chosen by the assistant, not confirmed by the user; 27 November 2026.

#### D36. A timing test takes the best of three runs

- **Question**: how should `SvgTextHostileDocumentTests.TrefFanOut_StopsAtTheCharacterLimit` measure, after it failed two of six full runs on a loaded machine?
- **Choice**: each of its two cases takes the best of three runs (`SvgGrowth.MeasureBest`). The bound of the test is unchanged.
- **Reason**: alone it never failed in ten runs; four busy processes on the four cores stretched one run of the large case from 59 ms to 147 ms, which a single measurement cannot tell from growth (20.6).
- **Alternative**: one run per case, as before; or a looser bound.
- **Where**: `tests/ImageSharp.Drawing.Tests/Svg/SvgTextHostileDocumentTests.cs`, `tests/ImageSharp.Drawing.Tests/Svg/SvgGrowth.cs`; `SVG_DESIGN.md` 19.2 and 20.6.
- **To revisit**: the test runs three times longer than one run; no output changes.
- **Status**: chosen by the assistant, not confirmed by the user; 27 November 2026.

#### D37. Filter results stay pooled while a retained scene exists

- **Question**: may a filter result keep its pooled buffer until the canvas or the scene that draws it is disposed?
- **Choice**: yes (Z5). The result keeps its buffer from the allocator of the configuration; an internal virtual method `DrawingCanvas.TryTakeOwnership` hands it to the canvas, whose list of deferred resources changed from `List<Image<TPixel>>` to `List<IDisposable>`. `CreateScene` moves the resources to the scene. A retained scene therefore holds pooled memory until it is disposed, and with an `AccumulativeAllocationLimitBytes` on the allocator, its filter results count against that limit for that time.
- **Reason**: the copy of every result into a new array was 57% to 94% of the bytes of the filter documents; FilterBlur at 4000 pixels went from 46.90 MB to 1.14 MB per render (19.3).
- **Alternative**: copy each result into a managed array, as before.
- **Where**: `src/ImageSharp.Drawing/Svg/Filters/SvgFilterEngine.cs` (`TryRender`), `SvgFilterImage.MoveToTexture`, `SvgTexture`, `src/ImageSharp.Drawing/Processing/DrawingCanvas.cs` and `DrawingCanvas{TPixel}.cs` (`TryTakeOwnership`); `SvgPaintContentTests.DrawSvg_RetainedSceneMatchesDirectRender`; `SVG_DESIGN.md` 19.3 item 2.
- **To revisit**: the core canvas change runs for every canvas; the non-SVG benchmarks of 19.3 measured no separated slowdown.
- **Status**: chosen by the assistant, not confirmed by the user; 27 November 2026.

#### D38. Changes kept with a separated slowdown in one measurement

- **Question**: may a change be kept when one of its measurements showed a separated slowdown?
- **Choice**: these were kept:
  - Z5 attempt 2 (pooled result buffers): FilterBlur with the default threads at 1000 pixels +10.1 ± 3.2% in 2 rounds against the tree of attempt 1. The cause is not understood; the final measurement against the start commit shows the cell faster (19.3).
  - Z6 attempt 1 (pooled node list and transform table): Tiger `LoadSvg` +1.1 ± 0.7% (19.5).
  - Z6 attempt 2 (layer band buffers): Dashboard at 1000 pixels on one thread +3.0 ± 2.1% in the per-attempt rounds; the final measurement of the package gives −4.8 ± 3.6% (19.5).
  - Z6 on the nested image document: allocation 109.7 → 70.3 MB per load and draw, but the time did not improve (107.5 → 114.2 ms per iteration with the default threads, GC pause per iteration 24–43 → 45–55 ms) (19.5).
- **Reason**: the gains in allocation or time on the other documents, as reported in each section.
- **Alternative**: revert the attempt.
- **Where**: `SVG_DESIGN.md` 19.3 and 19.5, the per-attempt tables.
- **To revisit**: measure the named cell again with the method of `SVG_IMPLEMENTATION_PHASE7.md` section 2.
- **Status**: chosen by the assistant, not confirmed by the user; 27 and 28 November 2026. The CssFeatures load slowdown of Z6 is not here: the user decided it (Part B).

#### D39. No bound on the memory of the pattern tile cache

- **Question**: should the pattern tile cache of a document have a total bound?
- **Choice**: no bound is proposed. The cache holds at most 4 sizes per tile, each bounded by `SvgLimits.MaxPatternTilePixels` (4096 × 4096 by default, 64 MB), plus the smaller levels (a third more): about 340 MB per tile in theory. The document keeps the tiles; they are managed arrays and need no disposal.
- **Reason**: no measured document comes near the bound (60 tiles of 100 × 100 texels take 2.4 MB at 4000 pixels) (19.3).
- **Alternative**: a bound on the bytes of the cache per document, with eviction.
- **Where**: `src/ImageSharp.Drawing/Svg/Rendering/SvgPatternTile.cs` (`CacheSize`); `SVG_DESIGN.md` 14.3 and 19.3.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 27 November 2026.

#### D40. Damaged raster images keep drawing nothing

- **Question**: ImageSharp 5 changed the default of `DecoderOptions.SegmentIntegrityHandling` from `IgnoreAncillary` to `IgnoreImageData`, which returns a partly decoded image for damaged data. What should `image` elements do?
- **Choice**: they set `IgnoreAncillary`, so a broken raster image still draws nothing, as before the sync.
- **Reason**: keep the output of the branch equal across the sync (20.1, 20.4).
- **Alternative**: the new default: a partly decoded image draws.
- **Where**: `src/ImageSharp.Drawing/Svg/Resolve/SvgResolver.Image.cs`; `SVG_DESIGN.md` 20.1.
- **To revisit**: not analysed.
- **Status**: chosen by the assistant, not confirmed by the user; 27 November 2026.

#### D41. The severity scale of the fork issues

- **Question**: what do the severity labels of the fork issues mean?
- **Choice**: high: a potential for denial of service, or wrong output for common valid input with no workaround for the caller; medium: wrong output under specific conditions, a large performance cost, or an API gap that forces a workaround; low: extreme sizes only, or tests and tools.
- **Reason**: the user asked to "assign severity and target tags for each issue" and named no scale.
- **Alternative**: another scale; the labels of issues 1 to 14 would change.
- **Where**: the labels of the issues in the fork (no file in the repository).
- **To revisit**: relabel the issues.
- **Status**: chosen by the assistant, not confirmed by the user; 27 November 2026 ("the severity scale is my own choice; say so if you want a different one").

### Defaults of the options

#### D42. Default values of `SvgLimits` and `SvgLoadOptions`

- **Question**: which defaults do the public limits and options have?
- **Choice**: the table below. Only the default of `MaxLayerPixels` was decided by the user (Part B); no decision of the user is recorded for the others.
- **Reason**: the documentation of each property; where `SVG_DESIGN.md` gives a reason, it is in the table.
- **Alternative**: other values; each is a public default, so a change is visible to every caller.
- **Where**: `src/ImageSharp.Drawing/Svg/SvgLimits.cs`, `src/ImageSharp.Drawing/Svg/SvgLoadOptions.cs`, `src/ImageSharp.Drawing/Svg/SvgGenericFontFamilies.cs`; `SVG_DESIGN.md` 6, 10, 13 item 5.
- **To revisit**: the limit and hostile document tests (`SvgDocumentHostileInputTests`, `SvgFilterHostileDocumentTests`, `SvgTextHostileDocumentTests`, `SvgCrossFeatureHostileDocumentTests`, `SvgLayerLimitTests`) assert the current values or depend on them. Not analysed further.
- **Status**: chosen by the assistant, not confirmed by the user, except `MaxLayerPixels`.

| Property | Default | Note from the sources |
|---|---|---|
| `SvgLimits.MaxElements` | 1,000,000 | |
| `SvgLimits.MaxNodes` | 1,000,000 | `feImage` content counts against it (16.5) |
| `SvgLimits.MaxNestingDepth` | 512 | |
| `SvgLimits.MaxCharactersFromEntities` | 1,048,576 (2^20) | Internal entities stay enabled with this cap, because Illustrator exports use them (10) |
| `SvgLimits.MaxImagePixels` | 67,108,864 (64 × 1024 × 1024) | Also bounds `SvgDocument.Render` at the intrinsic size (user decision, Part B) |
| `SvgLimits.MaxPatternTilePixels` | 16,777,216 (4096 × 4096) | |
| `SvgLimits.MaxTextCharacters` | 1,000,000 | |
| `SvgLimits.MaxFilterPixels` | 16,777,216 (4096 × 4096) | Per buffer of a filter run (16.5) |
| `SvgLimits.MaxLayerPixels` | 268,435,456 (16 × 4096 × 4096) | Decided by the user (Part B) |
| `SvgLoadOptions.Dpi` | 96 | |
| `SvgLoadOptions.CurrentColor` | `Color.Black` | |
| `SvgLoadOptions.Languages` | `en` | Not the culture of the machine (D7) |
| `SvgLoadOptions.FontCollection` | `null`: the system fonts, looked up only when the document has text | Decided in phase 3 as "the caller gives the fonts" (13 item 5); the default is not recorded as a user decision |
| `SvgLoadOptions.DefaultFontFamily` | `serif` | "as in browsers" |
| `SvgLoadOptions.FallbackFontFamilies` | empty | |
| `SvgLoadOptions.GenericFontFamilies` | serif: Times New Roman, Times, Noto Serif, DejaVu Serif, Liberation Serif; sans-serif: Arial, Helvetica, Noto Sans, DejaVu Sans, Liberation Sans; monospace: Courier New, Courier, Noto Mono, DejaVu Sans Mono, Liberation Mono; cursive: Comic Sans MS, Apple Chancery, URW Chancery L; fantasy: Impact, Papyrus, URW Bookman | |
| `SvgLoadOptions.UserStyleSheet` | `null` | |
| `SvgLoadOptions.ResourceBaseDirectory` | `null`: no file access, `data:` URIs only | The strict contract was kept by the user (Part B) |

Internal limits that are not options (constants in the code; `SVG_DESIGN.md` 10, 16.5, 17.5): a filter list of 32 entries (D8), a kernel of 256 entries (D9), `numOctaves` 9, a filter buffer side of 16,384 pixels and a standard deviation of 8,192 filter pixels, a specular exponent of 1 to 128, pattern and filter nesting with the full budget for 3 levels (half per further level), image nesting of 4 levels, 64 MiB of inflated `svgz` data, a side of 65,535 pixels for a size that the document sets (D32), a `var()` substitution of 32,768 characters and 16,777,216 substituted characters per document, `var()` nesting of 32, math function nesting of 32 with 256 terms and 65,536 math functions that mix units per document, color function nesting of 16.

## Part B: Decisions the User Made

Only text that the user typed counts. The words are quoted where they are recorded.

| Date | The user's words | What follows |
|---|---|---|
| 25 Nov 2026 | "Accepted design as recommended. Don't create issues or pr. Work in isolation." | The design of `SVG_DESIGN.md`; the core package under `src/ImageSharp.Drawing/Svg/`; the phase 1 cut. No issue or pull request in an upstream repository. |
| 25 Nov 2026 | "We can steal the code for css from [the Jint repository] if that helps" | CSS code may come from Jint; it is credited in `THIRD-PARTY-NOTICES`. |
| 25 Nov 2026 | "I commit after review" (an answer to a question) | The main session reviews each package and commits it; agents do not commit. |
| 25 Nov 2026 | "relist the 10 filter lines as recommended, keep the others as is" | The ten `feConvolveMatrix` tests that passed by accident were listed as failing. "The others" kept the strict `ResourceBaseDirectory` contract, so the image file tests of the suite stay refused (inference: this is the item that the same report recommended to keep). |
| 25 Nov 2026 | "make the gradient option public" | A public gradient option in the core library; replaced in Upstream Sync 1 by the upstream form, `AlphaRepresentation` (20). |
| 25 Nov 2026 | "Use bezier if this is the best thing for rounded corners in general (follow what skia/netvips do)" | SVG `rect` corners are cubic curves (`SvgResolver.CreateRect`); `root-stretch` was replaced. See D1 for the general rule. |
| 26 Nov 2026 | "Reimplement the Chromium-derived code so we don't need to license it." | The text-on-path and `textLength` code was rewritten; agents read no source of other renderers (`SVG_IMPLEMENTATION_PHASE4.md`). |
| 26 Nov 2026 | "we can file an issue in SixLabors.Fonts to expose the necessary Bidi algorithm publicly, and in the meantime copy it locally with a comment referencing the source" (recorded in the project notes) | `src/ImageSharp.Drawing/Svg/Text/Bidi` is a temporary copy with a comment that names the source; the issue went to the fork (Part D, issue 9). The changes to the copy are D33. |
| 26 Nov 2026 | "managed allocations might not be a good comparison measurement since skia and netvips are native libraries. Only use these as absolute value reference." | Managed allocation is reported for ImageSharp.Drawing only, as an absolute value; memory between renderers is compared by peak process memory (18.3). |
| 26 Nov 2026 | A request to "schedule ... a task that focus on improving performance for the scenarios where skia and netvips are faster, and the cases where memory allocations are a concern" | Phase 6. |
| 26 Nov 2026 | "relist them as expected failures" | The two CSS geometry tests are listed `fail specification:` (17.2, 17.4). |
| 26 Nov 2026 | "keep ex at half the font size" | `ex` is 0.5 em (17.4). |
| 26 Nov 2026 | "keep MaxLayerPixels default as is" | 268,435,456 pixels (17.4). |
| 26 Nov 2026 | "reuse MaxImagePixels, no separate MaxOutputPixels" | One limit bounds embedded images and the output of `Render` at the intrinsic size (17.4). |
| 26 Nov 2026 | "keep the spec for the phase 4 filter rules" | The rules of 16.4 that follow the specification over Chromium stay (17.4). See D10 for the one rule that the specification does not settle. |
| 26 Nov 2026 | "draw text inside embedded SVG images and relist the test" | Text in embedded SVG images draws with the outer font settings; `structure/image/embedded-svg-with-text` is listed `fail reference:` (17.4). |
| 26 Nov 2026 | "We need to verify this assumption or our numbers are incorrect. We should also validate how many iterations we do of renderings" | The Skia filter cache was verified and the warmup corrected (18.9). |
| 27 Nov 2026 | "follow the requests from the maintainer" | Upstream Sync 1: the upstream gradient option, `Fill(Pen, IPath)`, `init` accessors, no decoder (20). D31 and D34 are the parts that the assistant chose inside it. |
| 27 Nov 2026 | "files issues that you find for upstream in my own fork, include explanation, repro steps, and suggested fixes. Do not create them in the upstream repository" (recorded in the project notes); "file it in the fork like the others, assign severity and target tags for each issue" | Upstream defects are issues 1 to 14 of the fork, with a target and a severity label. The scale is D41. |
| 27 Nov 2026 | (a finding that untrusted input can exploit) | One finding in SixLabors.Fonts was reported privately. |
| 27 Nov 2026 | "If there are performance optimizations to apply in other ImageSharp products file an issue in our fork ..."; "schedule the perf investigations for the related project for later, ideally in a distinct cloud session, or once SVG is done" | A separate session for ImageSharp, Fonts, and PolygonClipper; not part of this register. |
| 28 Nov 2026 | "We shouldn't have to run these samples for so long. I can't believe we get any more valid data." | Benchmark runs are kept short: the partial 4000-pixel matrix of 21, and the budget rule of `SVG_IMPLEMENTATION_PHASE7.md` section 2. |
| 28 Nov 2026 | "Css feature load: keep the pulled array" | The pooled load tables stay, although the CssFeatures load is about 12% slower (19.5, 21.7). |
| 28 Nov 2026 | "Document the choices you made that were pending for my answers and keep them." | This register. |
| 28 Nov 2026 | "Start the performance investigations with the suggested improvements. Decide if they are with keeping (scene cache could be disaled with an option)." | Phase 7 (`SVG_IMPLEMENTATION_PHASE7.md`). The rule for keeping a change (its section 1) was written by the assistant under this instruction. |

## Part C: Improvements Proposed and Not Built

Phase 7 (`SVG_IMPLEMENTATION_PHASE7.md`) is investigating most of these now; this register does not describe its outcome. "Not in phase 7" means that the plan of phase 7 does not name the item.

| Proposal | Described in `SVG_DESIGN.md` | Phase 7 package |
|---|---|---|
| A cache of prepared scenes inside `SvgDocument`, with one option to turn it off | 19.4 ("A cache of prepared scenes in the document"), 19.5 | R1 |
| Mask effects that run on the layer rows in place, without a new `Image` and processor per mask band | 19.2, 19.5, 21.7 | R2 |
| Independent filtered elements rendered in parallel | 19.3, 21.7 | R2 |
| A pool for filter buffers above 4 MiB, beside the pool of ImageSharp | 19.5 ("Can our code avoid the boundary"), 21.7 | R2 |
| A cache of converted texture rows for the replay of filter results | 19.3 ("Gap to Svg.Skia after the work") | R2 |
| A write-back of a mask effect that copies the processed pixels where the coverage is 1 | 19.2 ("Gap that remains to Svg.Skia") | Not named; R2 covers the mask effects |
| A blend of a partly covered row without the round trip through `Vector4` | 19.1, 21.6, 21.7 | R3 |
| One fill per run of glyphs with equal paint | 19.4 ("What was not done, and why") | R3 |
| Prepared geometry or coverage of a glyph shared between its instances (a glyph cache) | 19.4, 21.7 | R3 |
| The computed style of an element reused by the box walks, and cached `var()` substitutions, charged to the substitution budget | 18.6, 19.4 | R4 |
| A stroke box of `clip-path` basic shapes without the stroke outline | 18.6, 19.4 | Not in phase 7 |
| Bounds of curves from their control-point hull or extrema instead of a flattening | 18.6 | Not in phase 7 |
| A clear of a whole-pixel rectangle that skips the coverage | 19.2 | Not in phase 7 |
| A cheaper radial gradient position | 19.1 | Not in phase 7 |
| Drawing the nodes of an embedded SVG image from the nested document instead of copying them per instance | 19.4, 19.5 | Not in phase 7 |
| Pooling only the arrays without references in the load tables | 19.5 | Not in phase 7; the user decided to keep the pooled arrays (Part B) |
| `currentColor` inside `color-mix()`, relative colors, and `light-dark()` | 17.7 | Not in phase 7 (a feature, not performance) |
| A fuzz target for `SvgDocument.Load` | 10 | Not in phase 7 |

## Part D: Defects of Upstream Code Tracked as Issues in the Fork

Each issue has a reproduction against upstream `main` and a suggested fix. Every issue has a target label (`upstream: ImageSharp.Drawing` or `upstream: Fonts`) and a severity label; the severity scale is D41. "Fixed on the branch" means that a commit of the SVG branch changes the core code.

| Issue | Title | What the SVG code does around it |
|---|---|---|
| 1 | A region clip under a scale draws a row twice | Nothing. The double draw and the coverage restore of `ApplyCoverageRun` were checked and not changed; no image of the test suite depends on them (19.2). |
| 2 | `Fill(Pen, IPath)` flattens the stroke in local units | A workaround: the emitter uses `Fill(Pen, IPath)` only when the transform does not enlarge and builds its own outline otherwise (D31, 20.3). |
| 3 | `ArcLineSegment` flattens once at construction | A workaround for `rect` only: the corners are cubic curves (`SvgResolver.CreateRect`). Arcs of path data still use `ArcLineSegment` (D28, 14.3). |
| 4 | Zero-length dashes are dropped | Nothing. `painting/stroke-dasharray/0-n-with-round-caps` and `0-n-with-square-caps` fail for this reason. |
| 5 | A dashed stroke becomes solid above 5,000 dashes | Nothing. |
| 6 | A canvas wider than 8,388,608 pixels fails | A workaround for document sizes: the side limit of 65,535 pixels (D32). A caller's own wide canvas still fails. |
| 7 | Test utilities do not truncate output images | A workaround in the method: the comparisons of phase 6 empty the output directory before each run (19.3, "Test output"). |
| 8 | Four benchmarks cannot run on Linux (`FillTiger`, `FillParis`, `DrawPolygon`, `FillPolygon`) | Nothing in the benchmarks; phase 6 measures the same fill and stroke code with the SVG Tiger and Paris documents (19.1). |
| 9 | Expose the bidirectional algorithm (Fonts) | A workaround: a temporary local copy in `Svg/Text/Bidi`, with three changes to its cost (D33, 15.4). |
| 10 | `TextShaper` cannot turn off default features (Fonts) | A partial workaround: for `letter-spacing`, `font-variant-ligatures: none`, and `"liga" 0` each grapheme is shaped again on its own. `calt`, `clig`, and feature values above 1 are not available (15.4, 17.6). |
| 11 | `ShapeRun` has no context text (Fonts) | Nothing. Arabic letters do not join across a `tspan` boundary; `SvgTextDirectionTests` records the case (15.5). |
| 12 | `TryParseSvgPath` rejects exponents | Fixed on the branch (commit `3596f37`): the path parser uses the number scanner of the SVG code. |
| 13 | `TryParseSvgPath` after `Z` | Fixed on the branch (commit `ae0c851`, in the `Z` case of the parser in `Path.cs`). |
| 14 | A path clip rasterizes a whole band per row | Fixed on the branch (commit `ad5c324`): `PathClipBand` keeps the rows of the last band per clip (19.2 item 2). |
