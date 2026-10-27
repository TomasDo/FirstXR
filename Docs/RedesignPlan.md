# FirstXR visual redesign — glasses HUD + Beam Pro

## Context

FirstXR is a Unity 6000.0.83f1 + XREAL XR Plugin 3.1.0 dental-implant navigation client. Its purpose is to
stop surgeons glancing up at the navigation monitor: the key values (remaining depth, position deviation,
angular deviation, CT target) are shown in the XREAL glasses, and the Beam Pro phone shows Monitor /
Connection / Debug pages. The user built it on a Mac and is unhappy with the aesthetics.

What is wrong today (from the code):
- **HUD** (`HelloMR/DentalHudController.cs`, built procedurally, not in the scene):
  - `CreateImage` sets `preserveAspect = true` on every image, so the 1:1 white sprite collapses the crosshair
    lines, depth track, depth fill, current marker and target tick into small squares/dots (code reading; no glasses
    capture exists).
  - Dark plates (α 0.72) emit almost no light on an additive display; the black TMP outline never renders (no
    `OUTLINE_ON` keyword) but still gives all 21 labels their own material instance (no batching).
  - The CT card covers the centre of view at the default pose (README says the centre stays clear); the CT slice is
    stretched non-uniformly into 390×310 at full brightness; bullseye and depth value sit ~16° apart.
  - Rings are one stretched sprite (strokes 4–14 mm) and decorative, not at the tolerance radii; the tilt triangle is
    filled although README/checklist say outlined; no over-depth region is drawn.
  - Eight near-duplicate font sizes plus auto-size, so sizes jump as text changes; remaining depth (the hero
    value) is grey in normal drilling; red is the dimmest state colour (luminance 0.27 vs green 0.54); alarms are
    filled plates; palette copied in three files.
- **Beam Pro** (`HelloMR/BeamProPagedController.cs`, IMGUI): every style derives from the default `GUI.skin`
  (bevelled grey buttons), field labels are clipped by their inputs, metric cards lack hierarchy, colours are
  re-declared separately from the HUD.

Decisions (user):
- Scope: **both** the glasses HUD and the Beam Pro pages, as one visual system. UI language stays **English**.
- HUD direction: **Luminous band** — no plates, constant-width luminous strokes, compact lower band with
  target + hero remaining-depth + depth gauge side by side, colour + shape only on the deviating channel.
- **Prototype first**: an interactive browser prototype of both surfaces for sign-off before any Unity change.
- Verification: no Unity editor / XREAL SDK on this Linux box, so Unity code is written here without
  compiling and the **user verifies on their Mac** (tests, screenshots, device).

Non-goals: no protocol / gRPC / DICOM / navigation-math change; `DentalNavigationBand` logic and its alarm
strings are untouched (diagnostics compares them verbatim, `DentalPerformanceDiagnostics.cs:129-130`).
Out of scope: the legacy phone overlays (`BeamProOverlayLayout`, `DentalRobotBeamProDisplay.OnGUI`, etc. —
unused in the default configuration), the 3D model renderer, the Engineer-Mode glasses control canvas.

Behaviour changes beyond pure styling (each small; the user can veto any at approval):
1. HUD content moves into the lower part of the canvas (centre clear); canvas size and default pose unchanged.
2. Position rings drawn at the navigation-provided green/red radii (today decorative).
3. HUD channel phrase for Unavailable states tells the real reason; phone shows Stale in grey like the HUD.
4. Phone header shows only user-facing status; raw robot status and `LinkMessage` appear on the Connection page;
   preview debug info only in Engineer Mode.
5. Exit needs a second tap within 3 s.

## Display rules that must survive the redesign (from code + Docs)

R1 freshness tiers (≤200 ms live; 200–500 ms grey + age + "Navigation data delayed"; >500 ms dashes, markers
hidden), evaluated every frame even when hidden, and `DentalHudController.Update` must keep publishing
`DentalHudEvaluation` (phone + diagnostics consume it) · R2/R3 invalid/stop/disconnect clear markers and numbers
immediately · R4 static CT may stay with invalid-state feedback · R5 colours only from current-context
tolerances; "Thresholds not synced", never local defaults · R6 hysteresis / boundary rule on raw values ·
R7 one decimal (age = integer ms) · R8 mm and ° only · R9 fixed B top / L bottom / M left / D right, CT buccal-up
mesial-left, in Follow and Hover · R10 dot = position direction, triangle = tilt direction, hidden at zero, no
direction without `has_*_direction` · R11 per-channel grades, overall = worst · R12 "Over depth" / "To entry
point", depth never green · R13 no substitute imagery (phone preview only real left-eye frames) · R15 v1 →
"Direction unavailable" · R16 default HUD pose (0, −0.13, 1.80) m and clamps, Hover/Follow local only ·
R18 "DentalHud SDF" stays TMP default + fallback; every non-ASCII literal in `HelloMR/*.cs` must be baked
(`DentalHudFontTests`) · R19 phone touch targets ≥56, no overlap, 540-unit short edge.

## Design system (tokens shared by HUD and Beam Pro)

Principles: black is transparent on XREAL One / One Pro (additive micro-OLED, 1920×1080/eye, 50°/57° FOV,
3-level electrochromic dimming) → contrast only from foreground luminance; calm by exception; tabular digits in
fixed positions; centre of view kept clear; sizes defined in degrees (at 1.8 m, 1° ≈ 31.4 mm ≈ 39–44 px).

- **Colour** (starting values, balanced by relative luminance for additive optics; final values set in the
  prototype): `Ink #EEF3F6` (L≈0.89) values · `InkMuted #93A1AB` labels · `InkFaint #5E6E79` scales/structure ·
  `Plan #5CC8FF` planned geometry (centre, target-depth line) · `Ok #34C98E` (L≈0.44, deliberately calmer) ·
  `Caution #FFC23D` (L≈0.60) · `Alert #FF6B81` (L≈0.33; rose-leaning so protanopes still see it, and never
  relied on alone — red states also get heavier strokes, a halo and the alert capsule with `Ink` text) ·
  `Stale #8C979F` · CT tint ≈ 55 % luminance, cool. Phone surfaces: `Bg #0B1015`, `Surface #121A21`,
  `Raised #18222B`, `Stroke #26323D`, accent = `Plan`.
- **Grade → colour** (single mapping, replaces both copies): Green→Ok, Amber→Caution, Red→Alert,
  Stale→Stale, Unavailable→InkFaint, depth Neutral→**Ink** (hero value no longer grey).
- **Redundant shape cues**: Amber adds a thin ring around the position dot / a bracket on the value;
  Red thickens the stroke and shows the outlined alert capsule; never colour alone.
- **Type**: B612 (labels) + B612 Mono (all numerals, so digits never jitter) — OFL fonts designed for cockpit
  displays; Noto Sans CJK stays the fallback. HUD sizes: hero 64 mm (≈1.4° cap), values 30 mm, labels 17 mm
  small caps letter-spaced, status 14 mm. No outlines, no auto-size on numerals.
- **Strokes**: 3 mm primary, 2 mm secondary, optional 25 % halo at 2× width for the luminous look.

## HUD layout (Luminous band)

Canvas contract unchanged (1000×560 mm, `scale 0.001`, default pose (0, −0.13, 1.80) m, Follow/Hover), but all
content moves into the canvas's lower ~230 mm (≈ −4.3° … −11.5° from eye level: centre clear, still inside the
XREAL One's ±12.25° vertical FOV). Left → right:
1. **Target**: dimmed, aspect-correct CT behind (not clipped — the slice isn't centred on the planned entry, so a
   circular crop could hide it); outer angle ring + outlined tilt triangle; position rings at the
   navigation-provided green/red radii when thresholds are valid (single neutral ring otherwise); `Plan` centre
   reticle; solid position dot; B/L/M/D outside the ring; CT status in tiny muted text below.
2. **Readouts**: label (REMAINING / OVER DEPTH / TO ENTRY) + hero value + "mm"; below, `POS 0.6 mm B/M` and
   `ANG 1.8° → D` rows, value coloured by its own grade.
3. **Depth gauge**: vertical ladder (minor 1 mm, major 5 mm labelled), entry 0 at top, `Plan` target line,
   over-depth zone below target, current chevron + fill coloured by depth grade.
4. **Status line** (bottom, muted): link dot + Connected/Connecting/Disconnected · case · tooth · drill · step;
   age in ms only when delayed.
5. **Alert capsule** (top of band, only when `ShowAlarm`): outlined, Caution/Alert stroke, vector warning icon,
   `AlarmText` verbatim.

## Beam Pro layout (same tokens, still IMGUI)

Keep IMGUI, the pure `BeamProPageLayoutCalculator` (540-unit short edge, margin 20, gap 12, touch ≥56) and the
paged structure; replace the look:
- **Skin**: new `BeamProSkin` builds every `GUIStyle` from the tokens — runtime-generated rounded 9-slice
  textures (radius 12, cached per colour), flat buttons (primary = filled `Plan`, secondary = `Raised` +
  `Stroke` border, danger-ghost for Exit), pressed/disabled states, input field with focus ring, B612 for text and
  B612 Mono for values/logs. Nothing derives from `GUI.skin` any more.
- **Header**: status pill (round dot + Connected / Connecting / Disconnected), context line
  `Case · Tooth · Plan · Drill`, alert row with vector icon in Caution/Alert.
- **Monitor**: 16:9 rounded preview with a LIVE badge or a centred "unavailable" state (still only real
  left-eye frames, R13); 2×2 metric cards with fixed hierarchy (small-caps label, B612 Mono value coloured by
  grade, one-line detail) — no per-card font shrinking; HUD / 3D-model as switch rows; Hover / Follow as one
  segmented control; case/asset details as key–value rows.
- **Connection**: labels above inputs (fixes today's clipped labels), primary Connect / Reconnect button, status
  card that also shows `LinkMessage` (today never displayed, e.g. "Threshold units must be mm and °").
- **Debug**: grouped cards, secondary buttons, log in B612 Mono.
- **Tabs**: flat bottom bar, active tab = `Plan` text + top indicator.
- One grade mapping for both surfaces (today the phone shows Stale in amber, the HUD in grey → grey values +
  Caution alert row; a valid `Neutral` depth no longer looks like "no data").
- **Colour fidelity**: the project is in Linear colour space and IMGUI treats colours as linear, which is why the
  declared near-black renders as slate (#06090D → #2A3540) and red turns pink. The skin passes `color.linear` for
  text/tint colours, bakes fills into sRGB textures with `GUI.color` left white, and stops multiplying
  `textColor` by `contentColor` (today secondary text is TextSecondary²). uGUI/TMP (HUD) already convert
  correctly, so HUD tokens are used as-is.
- **Clinical surface hygiene**: header shows only user-facing status (raw robot status moves to the Connection
  page), preview debug info only in Engineer Mode, Exit becomes a danger-ghost button that needs a second tap
  within 3 s (today one tap quits the app mid-procedure).

## Steps

### Phase 1 — Interactive prototype (sign-off gate, nothing in Unity changes yet)
1. Artifact tool `quickstart` → load `artifact-design` → one self-contained HTML page in the scratchpad,
   published as a **private Artifact** for review (and viewed in the built-in browser for my own checks).
2. Contents:
   - **Glasses view** at true angular scale (1.8 m; FOV preset One 50° / One Pro 57°), backdrop "as rendered"
     (black) or a synthetic bright operating-field scene with additive blending (`mix-blend-mode: plus-lighter`)
     and 3 dimming levels; toggles for the centre clear zone and FOV edges.
   - **Beam Pro frame** (1080×2400, portrait) with Monitor / Connection / Debug.
   - **Shared state panel**: every state from R1–R15 (within / near / out per channel, over depth, before entry,
     delayed, interrupted, invalid, thresholds not synced, disconnected, v1) plus "Play drilling sequence" at
     30 Hz using the simulator's numbers (target 10 mm, lateral 0.5/1.0 mm, angle 2/5°, approach 1.0, overrun 0.5).
   - **Synthetic, procedural CT** only — no patient data (the DICOM next to the repo stays local).
   - Font switch (B612 default vs IBM Plex vs Noto Sans) so the typeface is chosen by eye.
3. Self-check screenshots of every state at both FOVs (no jitter during playback, centre zone empty, minimum
   text size met), share the link, iterate until the user signs off; the final values become the token table.

### Phase 2 — Unity port (written here; compiled and verified on the user's Mac)
Work on a branch `redesign/luminous-ui`. All new runtime files live under `HelloMR/Ui/` (same asmdef).
1. **Tokens** — `HelloMR/Ui/DentalUiTheme.cs`: colours, `ColorForGrade`, HUD type/stroke scale, angle↔mm
   helpers, phone spacing. Replace the duplicated colour literals and both `ColorForGrade`/`GradeColor` copies in
   `DentalHudController.cs` and `BeamProPagedController.cs`.
2. **Fonts** — add B612 / B612 Mono (Regular + Bold; Airbus/Intactile cockpit family, OFL 1.1) from the
   google/fonts repository (`ofl/b612`, `ofl/b612mono`: 4 TTFs ≈ 0.5 MB + `OFL.txt`, SHA-256 recorded in a README
   like the Noto one) under `Assets/Resources/Fonts/B612/` — downloaded only at this step. Extend `Assets/Editor/DentalHudFontSetup.cs`
   (`XREAL/Fonts/Rebuild HUD Font`) to also bake `DentalHud Mono SDF` and `DentalHud Label SDF` (mobile SDF
   shader, fallback → `DentalHud SDF`). `DentalHud SDF` stays the TMP default + global fallback (R18). HUD falls back
   to it, with one warning, if the new assets are missing. **No new non-ASCII string literals** — only ASCII plus the
   already-baked `° · ↑ ↓ — …`; icons (warning, chevrons, arrows) are vector shapes, so no glyph re-bake is needed.
3. **Vector shapes** — `HelloMR/Ui/HudVectorGraphic.cs` (`MaskableGraphic`): ring, arc, line, polyline, outlined
   triangle, chevron, tick ladder; constant stroke in canvas mm, 1-unit feather for anti-aliasing, optional halo;
   mesh rebuilt only when a property changes; mesh math in static functions so it is unit-testable.
4. **HUD layout** — `HelloMR/Ui/DentalHudLayout.cs`: every band position/size/stroke/font size as constants derived
   from the angular tokens, plus a pure "fits FOV / keeps centre clear" check used by tests.
5. **HUD rebuild** — `DentalHudController.cs`: keep singleton/`EnsureInstance`, `Awake/Start/Update`, evaluation
   published every frame (R1), Follow/Hover anchoring, `SetHudVisible`/`SetWidgetFrameVisible`, skip-unchanged
   caching, and the `m_Views` labels the tests read (`DepthValue`, `LateralRead`, `CtStatus`, `AlarmText`).
   Replace `BuildTree`/`Build*`/`Apply*` with the luminous band: no plates, no TMP outlines (also removes the
   per-label material instances), no `preserveAspect` on shapes, fixed sizes (no auto-size), numerals via
   `TMP_Text.SetText(format, float)` (no allocation, culture-independent), tolerance rings, outlined tilt
   triangle, drawn over-depth zone, aspect-correct dimmed CT, alert capsule, status line with case/tooth/drill/step,
   and a truthful channel phrase for Unavailable (today "Thresholds not synced" even when disconnected). Build all
   shapes before all labels so uGUI batches them into a few draw calls. Keep at least one `↑` literal (the font
   test requires the scanner to find it; today it exists only in "↑ Before entry").
6. **Beam Pro restyle** — `HelloMR/Ui/BeamProSkin.cs` + draw methods in `BeamProPagedController.cs`. Keep
   `GetLayout` and the `m_BodyStyle` / `m_HeaderContextStyle` / `m_HeaderAlarmStyle` fields (tests inject them by
   reflection); the calculator in `BeamProPageLayout.cs` stays unchanged (the 84-unit input blocks already fit a
   20-unit label + 56-unit field once the label font is sized correctly).
7. **Editor preview for the Mac** — `Assets/Editor/DentalHudPreviewMenu.cs`: `Tools/HUD Preview/Capture All
   States` runs in Edit Mode on temporary objects (as `DentalUiPerformanceTests` does, via an editor-only public
   hook like `BeamProPagedController.SelectEditorPreviewPage`, so no live gRPC client can overwrite the injected
   state) with a temporary camera at the eye pose (FOV 50° and 57°), and drives a real `DentalNavigationState` through each canonical
   state through public APIs only (`ApplyNavigationContext`, `ApplyThresholds` / `NotifyThresholdsUnavailable`,
   `ApplyNavigationFrame`, `ApplyMetadata` for v1, `NotifyDisconnected`; delayed / interrupted = frames whose
   capture time is 300 / 800 ms in the past) and renders the HUD canvas to PNG on black and composited additively
   (CPU, no shader) over a bright backdrop → `Logs/hud-preview/`. Reuse the same state driver in `BeamProPagedPreviewMenu` (Play Mode, gRPC client component
   disabled during capture) so the Monitor capture shows real grade colours instead of dashes, and write captures to `Logs/beampro-preview/` instead of deleting the historical
   `Docs/BeamProScreenshots/*.png` (Game view still set to 1080×2400 by hand).
8. **Tests** — update `DentalUiPerformanceTests` (new label texts, cleanup of removed sprite fields); add
   `DentalUiThemeTests` (grade mapping, minimum luminance of every HUD colour), `HudVectorGraphicTests` (stroke
   width constant across radii, vertex counts), `DentalHudLayoutTests` (band inside the One's FOV at the default
   pose, centre clear, no overlaps), design-font asset tests (exist, shader, fallback, glyph coverage), and a new
   HUD steady-state test (unchanged live frames allocate 0 bytes after warm-up — today no HUD GC test exists).
   `DentalNavigationBandTests` and `BeamProPageLayoutTests` must pass unchanged.
9. **Docs** — README HUD / Beam Pro sections, `Docs/DesignSystem.md` (tokens + rationale), B612 credit, old
   screenshots marked historical.

### Phase 3 — Verification
- **Here**: careful self-review of every C# change against Unity 6000.0 / TMP / uGUI APIs (no compiler available),
  a read-through of every reflection hook the tests use, and the prototype as the visual reference.
- **User's Mac** (handoff checklist delivered with the branch; the branch reaches the Mac by pushing to `origin`
  (`TomasDo/FirstXR`) only with the user's OK, or by copying the folder; `com.xreal.xr.tar.gz` must sit in the
  project root as on the Mac today):
  1. `XREAL/Fonts/Rebuild HUD Font`, commit the generated assets.
  2. EditMode tests via the README command — expect all green (last recorded 160/160); send back
     `Logs/validation/editmode.xml` (+ `editmode.log` on any compile error, which I fix and hand back).
  3. `Tools/HUD Preview/Capture All States` and `Tools/Beam Pro Preview` (1080×2400) → send the PNGs; I compare
     them with the prototype and fix differences.
  4. Device: Beam Pro + XREAL with the simulator (`npm start -- --dicom-dir …`, optional `--faults`): readability
     at a glance, centre of view clear, placement within FOV (a persisted HUD position from PlayerPrefs
     `DentalNavigation.Layout.v2.*` may need "restore default"), no frame-time regression (P95 already ~20 ms).

Known issue to raise separately (not part of this redesign): the CT slice is not centred on the planned entry,
so the bullseye overlay is schematic rather than registered to the image.

## Handoff notes (for whoever implements this on the Mac)

- Status: plan approved; **nothing implemented yet**. Phase 1 (browser prototype) has not been built. If you
  work on the Mac with Unity available, you can still build the prototype first for design sign-off, or go
  straight to Phase 2 and use the editor capture tools for visual review — confirm with the project owner.
- The "written blind / verified on the Mac" constraint above came from the Linux machine this plan was written
  on (no Unity, no XREAL SDK). On the Mac you can compile and run tests directly; the Phase 3 checklist still applies.
- Beam Pro portrait geometry to design against (logical units, 540 wide, 1080×2400 → scale 2), from
  `BeamProPageLayoutCalculator`:
  - Header `(0,0,540,max(72, measured))`; Exit `(408,8,112,56)`; content viewport `(20, header+12, 500, …)`;
    tab bar `(0,1128,540,72)`, buttons 56 high at y 1136 (2 tabs: 244 wide; 3 tabs: 158.67 wide, gap 12).
  - Monitor (viewport-relative): preview `(0,0,500,281.25)`; cards 244×116 at y 293.25 / 421.25 (x 0 / 256);
    HUD|Model toggles y 549.25; Hover|Follow y 617.25; asset panel y 685.25, ≥116 high.
  - Connection: status ≥92, IP 84, Port 84, Connect 56, Result ≥120 (gap 12, width ≤720).
  - Debug: groups with a 56-unit title area, rows 56 high at 68 pitch inset 20, 20 bottom padding; log ≥280.
- Simulator scenario values (Tools/navigation-simulator/server.js): tooth 36, target depth 10 mm, lateral
  0.5/1.0 mm (hyst 0.05), angle 2/5° (hyst 0.2), depth approach 1.0, at-target 0.2, overrun red 0.5 (hyst 0.1),
  inclusive boundaries, 30 Hz frames. It never reaches red position/angle and cannot produce the delayed /
  interrupted tiers; use the editor capture tool (Phase 2 step 7) for those states.
- An open review of Unity API assumptions was not completed before handoff; re-check API names
  (e.g. `TMP_Text.SetText(string, float)`, `MaskableGraphic.OnPopulateMesh`) against the installed packages.
