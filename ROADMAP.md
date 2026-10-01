# UnityHTML Improvement Roadmap

Baseline: v0.1.0, commit `71a1f72`. Goal: take the package from "early adopter
tool" to something an external team can install, run, and migrate onto with
confidence. Ordered by priority — earlier phases unblock later ones.

## Phase 1 — Installable and runnable by a stranger (P0)

### 1.1 Reproducible installation

- [x] **Fix the DOTween dependency.** *(done: option (a) — free DOTween documented, `DOTweenPro.dll` removed)* `UnityHTML.Runtime.asmdef` references
  `DOTween.dll` and `DOTweenPro.dll`, but neither appears in `package.json`
  dependencies or install docs. Motion code (`UnityHtmlMotionBridge`,
  `UnityHtmlMotionPolicy`) only uses free-tier API (`DOTween.Sequence`,
  `DOTween.To`, `Ease`) — verify and remove the `DOTweenPro.dll` reference.
  Then choose one:
  - **(a) Required dependency (simplest):** document DOTween free install
    (unitypackage into `Assets/Plugins/Demigiant/DOTween`) as a hard
    prerequisite, alongside ReactUnity.
  - **(b) Optional module:** move motion into a separate
    `UnityHTML.Motion` asmdef gated behind a scripting define (e.g.
    `UNITYHTML_DOTWEEN`), so a project without DOTween still compiles and
    runs with `data-motion` no-op'd. More work, but removes a hard
    dependency for non-animated UI.
- [x] **Complete install manifest.** README must show a full
  `Packages/manifest.json` snippet with `com.reactunity.core`,
  `com.reactunity.jint`, `com.reactunity.quickjs` git URLs pinned to tested
  commits, plus the DOTween step. Nobody should have to guess.
- [x] **Add `LICENSE`** (decide MIT vs proprietary) and **`CHANGELOG.md`**
  (Keep a Changelog format, starting at 0.1.0).

### 1.2 First-run experience

- [ ] **`Samples~/` minimal runnable screen** registered in `package.json`
  `"samples"`: bootstrap `MonoBehaviour` that creates `Canvas` +
  `EventSystem` if missing, mounts an HTML/CSS pair with a working
  `button`/`toggle`/`input`/`slider`, wires a bridge object, and disposes
  correctly on teardown. One import → press Play → working screen.
- [x] **README quick start:** "empty project → button calling C#" with
  complete files: container setup, `moyvaFont`/media globals, when to call
  `Mount` vs `UpdateRegion` vs `Dispose`.
- [x] **Troubleshooting section:** blank-screen checklist (missing
  EventSystem, parse failure, wrong root), XML parse errors and where they
  surface, Jint-vs-QuickJS platform note.

### 1.3 Honest markup contract

- [x] **State the XHTML subset up front.** Markup goes through
  `XmlDocument.LoadXml` (`UnityHtmlDocumentTree.Parse`) — closed tags,
  quoted attributes, escaped `&` are required. `<input disabled>` and
  unclosed `<img>` are not valid. Put this at the top of README next to the
  supported-tag table so web developers calibrate expectations immediately.
- [x] **Actionable parse errors.** Catch `XmlException` and rethrow/log with
  `SourceName`, line/position (`XmlException.LineNumber`), and a source
  snippet — today a malformed asset fails with a raw XML error and no
  document context.
- [x] **Fix README lifecycle claim.** `Mount` does not unconditionally
  "replace any live document" — it attempts in-place reconciliation first.
  Document when state is preserved vs discarded.

## Phase 2 — Safe migration tooling (P1)

The analyzer (`Editor/Migration/ProjectUiAnalyzer.cs`) produces a useful
inventory but is Moyva-hardcoded and cannot yet prove a screen is safe to
remove.

- [x] **Configurable scan roots.** *(via `ProjectSettings/UnityHTMLAnalyzer.json`)* `Assets/Moyva` / `Assets/Moyva/Scripts`
  are hardcoded — move to analyzer settings (window fields or a settings
  asset) defaulting to `Assets`.
- [x] **Unsaved-scene guard.** Scenes open in `Single` mode; setup is
  restored in `finally`, but dirty scene *contents* are not protected.
  Call `SaveCurrentModifiedScenesIfUserWantsTo` (or abort with a clear
  message) before opening.
- [x] **Inbound reference scan.** *(scenes + within-prefab; cross-asset still open)* Current check only walks references
  *out of* UI roots. Add a reverse pass — serialized references *into* the
  UI from the rest of the scene/prefabs — so the report answers "what
  still points at this canvas" before deletion.
- [ ] **Migration guide.** Document the side-by-side pattern explicitly:
  `Mount` destroys **all** children of the passed root
  (`ClearRootChildren`), so HTML must own a dedicated container; put both
  implementations behind a flag; fall back to the legacy screen when
  `UnityHtmlMountResult` reports failure; removal checklist.
- [ ] **Ship a presenter scaffold.** A package-provided
  `UnityHtmlScreen`-style component owning container lifecycle
  (mount/unmount/dispose, optional fallback target) so consumers don't
  reimplement the same glue — and can't accidentally hand over a shared
  root.
- [ ] *(stretch)* **Draft-HTML export** from an analyzed canvas — an
  inventory-driven skeleton the user edits, not a finished conversion.

## Phase 3 — Release confidence (P1)

### 3.1 Compatibility

- [ ] **Pin tested ReactUnity version** (currently 0.23.2) in docs and
  `package.json`; publish a compat matrix as versions are verified.
- [ ] **Isolate reflection.** Private-field access is scattered
  (`_ygNode`, `Context`, `rt`, `SmoothCoroutine`, `targetPosition`,
  `<ScrollRect>k__BackingField`). Centralize behind one compat shim that
  feature-detects and fails with an actionable message; extend
  `ReactUnityCompatibilityTests` to cover every shimmed member so a
  ReactUnity bump breaks CI, not production.
- [ ] **Document registry mutation.** Component-factory overrides mutate a
  global registry — either scope the effect or document that other
  ReactUnity screens in the same project are affected.

### 3.2 CI

- [ ] **Run the tests.** `upm-release.yml` only tags. Add GameCI
  `unity-test-runner` for EditMode tests (`Tests/Editor`, ~69 test
  methods) on push/PR — requires a `UNITY_LICENSE` secret and Unity
  6000.3.
- [ ] Then: PlayMode tests, a standalone build smoke test of the Samples
  scene, and a platform matrix (Android/iOS/WebGL/desktop) — recorded in
  README. No platform claims until measured.

### 3.3 Performance baseline

- [ ] **Benchmark harness:** mount time, `UpdateRegion` on representative
  subtrees, allocations, and memory across repeated mount/unmount cycles
  (the package already promises safe repeated unmount — measure it).
  Publish numbers; make no perf claims vs plain UGUI until they exist.

## Phase 4 — API / DX polish (P2)

- [ ] Dev-mode diagnostics: warn on duplicate `id`s, missing `data-key` on
  reordered siblings, unknown tags (fail loudly instead of silent
  fallback).
- [ ] IL2CPP/stripping guidance: Jint/QuickJS + reflection on device —
  ship `link.xml` if needed and document tested build settings.
- [ ] Public API review: `IUnityHtmlHost` is small and clean — keep it
  that way; consider an options object if `Mount` parameters grow.

## Phase 5 — Feature surface: 100 built-in features (P1/P2)

Goal: kill the repetitive glue every game UI rewrites — a feature should be
declared in markup/config in ~one line, not rebuilt per project. Each item
lists its purpose, benefit, and whether it duplicates existing behavior.
**Test contract (applies to all of Phase 5):** no feature merges without an
EditMode test; anything involving input, navigation or animation also gets a
PlayMode test; the Phase 3.2 CI gate runs all of them.

**Executable spec: `FEATURES.md`** contains the fixed API, behavior contract,
implementation location and required tests for every item (F1–F100, I1–I12,
N1–N18) plus dependency-ordered implementation waves W1–W6 — an executor
implements from that file without further design decisions. The tables below
stay as the overview/justification.

### C1. Layout & containers (10)

| # | Feature | Why / benefit | Dupes? |
| --- | --- | --- | --- |
| 1 | `<backdrop>` / `data-bg="screen"` | One line = fullscreen background stretched to the canvas behind a panel — the single most repeated setup in menus. Optional `dim`, `blur`-style tint, `closeOnClick`. | New. `image` can do it today only with manual anchor CSS |
| 2 | `<panel>` | Semantic container = `view` + motion `role` + themed background; declarative "this is a surface". | Thin layer over `view` — intentional alias, not duplication |
| 3 | `data-safe-area` | Per-edge notch/cutout padding auto-computed from `Screen.safeArea`. Hand-rolled in every mobile project today. | New |
| 4 | `data-anchor="top-right"` | 9-point anchor shorthand; saves verbose absolute-position CSS for HUD corners. | Alias over CSS — justified as routine killer |
| 5 | `data-stretch` / `data-center` | Fill-parent and center shorthands. | Alias over CSS |
| 6 | `<spacer size="8">` | Explicit spacing element; clearer than empty views. | Alias over CSS |
| 7 | `<divider>` | Horizontal/vertical separator line. | New |
| 8 | `<overlay>` | Guaranteed topmost layer (above tooltips) for blockers, cinematics, loading veils. | New — tooltip layer exists but is not user-reachable |
| 9 | `data-platform="mobile|desktop|console"` | Show/hide by platform or device class. Every cross-platform game duplicates this. | New |
| 10 | `data-orientation="landscape|portrait"` | Conditional rendering per orientation + rebuild on change. | New |

### C2. uGUI component coverage gaps (12)

| # | Feature | Why / benefit | Dupes? |
| --- | --- | --- | --- |
| 11 | `<scrollbar>` | Standalone `Scrollbar` + auto-pairing with `<scroll>` — required to fully cover uGUI. | Complements `scroll`, not a dupe |
| 12 | `<rawimage>` | `RawImage` with UV rect — camera feeds, render textures, minimaps. | New |
| 13 | `<mask>` / `<rectmask>` | Clipping without a ScrollRect — rounded avatars, soft-edge lists. | New |
| 14 | `data-raycast="off"` | Per-element `raycastTarget` toggle; kills invisible-click-blocker bugs. | New |
| 15 | CanvasGroup attrs | `data-alpha`, `data-interactable`, `data-blocks-raycasts` on any element. | New (motion touches alpha, these are static state) |
| 16 | `data-nav-up/down/left/right` | Explicit `Selectable` navigation wiring + `data-nav-wrap`. | New |
| 17 | `data-first-selected` + `data-return-focus` | Focus entry point and focus restore on close — gamepad UX baseline. | New |
| 18 | `data-shadow` / `data-outline` | uGUI `Shadow`/`Outline` effects — used on nearly every game text. | New |
| 19 | `data-gradient` | TMP vertex gradient (two/four color). | New |
| 20 | `data-slice` | 9-slice sprite borders for panels/buttons. | New |
| 21 | `data-tiled` | Tiled image fill for repeated textures. | New |
| 22 | `data-fill="linear|radial"` + `data-fill-amount` | `Image.fillAmount` — basis of every cooldown/HP/cast indicator. | New; `data-cooldown` (below) builds on it |

### C3. Game widgets (28)

| # | Feature | Why / benefit | Dupes? |
| --- | --- | --- | --- |
| 23 | `<progress>` | HP/XP/loading bar: `value`/`max`, smooth fill animation, gradient/overheal states. | New |
| 24 | `<radial>` | Radial progress ring — cooldowns, cast bars. | New (uses #22) |
| 25 | `<switch>` | iOS-style switch — standard settings control. | Variant of `toggle`; shared logic |
| 26 | `<segmented>` | Segmented control (Graphics: Low/Med/High). | New; logic close to toggle-group |
| 27 | `<radio>` + group | Radio group for mutually exclusive options. | New |
| 28 | `<stepper>` | Numeric −/+ stepper (quantity, difficulty). | New |
| 29 | `<rating>` | Star/pip display+input (level results, store). | New |
| 30 | `<search>` | Input + clear button + debounce + `onSearch`. | Composes `input` |
| 31 | `<badge>` | Counter bubble on icons/tabs (unread, new items). | New |
| 32 | `<spinner>` | Loading indicator presets. | New |
| 33 | `<skeleton>` | Shimmer placeholder while content loads. | New |
| 34 | `<empty-state>` | Icon + title + hint + action slot — every list/screen needs it. | New |
| 35 | `<list>` + `<template>` | Virtualized/recycled item list — the biggest perf trap for HTML UI; data-driven rows. | Uses `scroll` internally; not a dupe |
| 36 | `<grid>` | Grid variant of `<list>` (inventories). | Shares `<list>` core |
| 37 | `<slot>` | Inventory slot: icon + count + rarity frame + empty state. | Composes `image`/`text` |
| 38 | `<hotbar>` | Slot row + keybind labels. | Composes `<slot>` + `<glyph>` |
| 39 | `<avatar>` | Masked image + frame + status dot + fallback initials. | Composes `mask`/`image` |
| 40 | `<status-dot>` | Online/ready/away indicator. | New |
| 41 | `<counter>` | Animated number roll (gold, XP gain, score). | New |
| 42 | `<currency>` | Icon + amount chip with delta flash. | Composes `icon`/`counter` |
| 43 | `<chat>` | Scrolling log + input + fade-old-messages + channels. | Composes `scroll`/`input` |
| 44 | `<carousel>` | Paged scroll with snap + page dots. | Extends `scroll` |
| 45 | `<accordion>` | Collapsible sections (settings categories, quest groups). | New |
| 46 | `<context-menu>` | Right-click/long-press menu. | New; shares tooltip layer infra |
| 47 | `<radial-menu>` | Pie menu — common in action/survival games. | New |
| 48 | `<joystick>` | Virtual stick/floating joystick for touch. | New |
| 49 | `<glyph>` / `<kbd>` | Shows current binding glyph (`E` vs `A`-button) per active device — massive routine killer. | New; depends on input workstream |
| 50 | Rich tooltip template | `data-tooltip-title/-body/-footer/-icon` structured tooltip instead of plain text. | Extends existing tooltip layer |
| 51 | `<coachmark>` | Tutorial highlight: dim screen, hole-punch around target, anchored hint. | New; uses `overlay` |
| 52 | `<ticker>` | Scrolling/marquee text for long strings. | New |

### C4. Behavior & binding attributes (25)

| # | Feature | Why / benefit | Dupes? |
| --- | --- | --- | --- |
| 53 | `data-cooldown` | Timed radial/linear sweep on buttons/slots; blocks input while running. | Builds on #22 |
| 54 | `data-typewriter` | Typewriter text reveal for dialogue. | New; respects ReducedMotion |
| 55 | `data-countdown` / `data-elapsed` | Self-updating timer text. | New |
| 56 | `data-l10n` | Localization key → auto re-render on locale change. | New |
| 57 | `data-format` | Number formatting: `compact` (1.2K), currency, percent. | New |
| 58 | `data-sfx` | Click/hover/submit sound hooks routed to a project audio service. | New |
| 59 | `data-haptic` | Vibration/rumble preset per action — see input workstream. | New |
| 60 | `data-disabled="reason"` | Styled disabled state + reason shown via tooltip. | Extends existing `disabled` |
| 61 | `data-loading` | Busy state on buttons (spinner replaces label, input blocked). | Composes `spinner` |
| 62 | `data-confirm` | Inline "click again to confirm" for destructive actions. | New |
| 63 | `data-longpress` | Long-press alternative action (mobile "secondary click"). | New |
| 64 | `data-draggable` / `data-drop` | Drag-and-drop between elements (inventory rearrange). | New; uses existing drag events |
| 65 | `data-swipe-actions` | Swipe-to-reveal row actions (delete/archive). | New; touch-only |
| 66 | `data-pull-refresh` | Pull-to-refresh on scrolls. | Extends `scroll` |
| 67 | `data-sortable` | Drag-to-reorder lists. | Builds on #64 |
| 68 | `data-scroll-into-view` | Auto-scroll focused/selected element into view — required for gamepad lists. | Extends `scroll` + nav |
| 69 | `data-visible` / `data-hidden` | Binding-friendly visibility toggle (keeps layout option). | New |
| 70 | `data-theme="dark|light"` | Token/theme sets swapped via CSS variables. | New |
| 71 | `data-contrast="high"` | High-contrast mode hook. | New |
| 72 | `data-bind` | Two-way binding element value ↔ global object property — removes manual `onChange`+`SetValue` loops. | Extends globals/`SetValue` |
| 73 | `data-bind-list` | Data-source attribute powering `<list>`/`<grid>` templates. | Required by #35 |
| 74 | `data-if` / `data-else` | Conditional render without rebuild. | ReactUnity may partially cover; verify before building |
| 75 | `data-testid` | Stable automation/testing hooks — and enables our own UI tests. | New |
| 76 | `data-analytics` | Declarative UI analytics events (screen_view, click) routed to a hook. | New |
| 77 | `data-preload` / `data-lazy` | Asset preload hints + lazy image loading for heavy screens. | New |

### C5. Screen-level components (15)

| # | Feature | Why / benefit | Dupes? |
| --- | --- | --- | --- |
| 78 | `<dialog>` | Modal: scrim + close-on-scrim + focus trap + Escape-to-close — all in one tag. | Composes `backdrop`, motion roles, focus |
| 79 | `<toast>` + queue | Non-blocking notifications with priority, dedup, auto-expire. | New |
| 80 | `<snackbar>` | Toast with action button ("Undo"). | Extends `toast` |
| 81 | `<header>` | Title + optional auto-wired back button (feeds navigation stack). | New |
| 82 | `<tabbar>` / `<tab>` | Bottom/top tabs with per-tab state preservation. | New |
| 83 | `<drawer>` | Slide-in side menu (hamburger). | Composes motion `panel` role |
| 84 | `<wizard>` | Stepper/multi-step flow with validation hooks. | New |
| 85 | `<tutorial>` | Sequenced `<coachmark>` flow with progress + skip. | Composes `coachmark` |
| 86 | `<notification-center>` | Persistent notification inbox. | Composes `list`/`toast` |
| 87 | `Globals.ui.Confirm(title, body)` | Promise-style confirm dialog from C#/script — kills per-screen confirm clones. | Uses `dialog` |
| 88 | `<rebind-row>` | "Action → current binding → press any key" row for settings. | Depends on input workstream |
| 89 | `<master-detail>` | Split view for tablets/desktop menus. | New |
| 90 | `Globals.ui.Alert/Input/Prompt` | Standard quick dialogs, same family as Confirm. | Uses `dialog` |
| 91 | `<splash>` / boot sequence | Logo/boot screen template with min-duration + skip rules. | New |
| 92 | `<screen>` | Named screen unit the navigation stack pushes/pops (owns lifecycle + transitions). | New; foundation for Annex C |

### C6. Asset & platform pipeline (8)

| # | Feature | Why / benefit | Dupes? |
| --- | --- | --- | --- |
| 93 | `source` schemes | `resources://`, `atlas://name`, `addressables://`, `url://` — unified media resolution. | Extends existing media provider |
| 94 | `url://` remote images | Remote fetch + disk/memory cache + placeholder/error states. | New |
| 95 | `data-sprite-*` state sprites | `normal/hover/pressed/disabled` sprite swap (button sprite transition). | New |
| 96 | `<world-label>` | Screen-space element anchored to a 3D object — nameplates, damage numbers. | New |
| 97 | `dir="rtl"` | RTL layout/text support. | New |
| 98 | `data-a11y-label` | Accessible label for assistive tech where Unity supports it. | New |
| 99 | Per-element `data-motion="none"` inheritance | Subtree-level reduced-motion opt-out/opt-in. | Extends existing ReducedMotion |
| 100 | `<video>` (stretch) | `RawImage`+`VideoPlayer` binding for menus/cinematics. | New; may slip a milestone |

## Annex A — Full uGUI coverage matrix (beyond the 100)

Every uGUI feature must be reachable through markup/attrs or explicitly
documented as covered-by-Yoga. Status: ✅ exists · ◐ partial · ☐ planned.

| uGUI feature | UnityHTML surface | Status |
| --- | --- | --- |
| Canvas render modes (overlay/camera/world) | host-level config; `world-label` for per-element world anchoring | ◐ |
| Canvas sorting/order | `data-order`, layer attrs | ☐ |
| CanvasScaler modes | host `ScrollSettings`-style settings object | ☐ |
| GraphicRaycaster | `data-raycast` (#14), blocking options | ◐ |
| CanvasGroup | #15 | ☐ |
| RectTransform anchor/pivot/offset | Yoga + #4/#5 shorthands | ◐ |
| Image simple/sliced/tiled/filled | `image` + #20/#21/#22 | ◐ |
| RawImage + UV | #12 | ☐ |
| Text/TMP: align, overflow, autosize, rich tags | `text` + CSS; document TMP tag passthrough | ◐ |
| Button transitions (color/sprite/anim) | CSS `:hover/:active` + #95 | ◐ |
| Toggle + ToggleGroup | `toggle` + `radio` (#27) groups | ◐ |
| Slider full props (direction, handle area) | `slider` + attrs | ◐ |
| Scrollbar | #11 | ☐ |
| Dropdown: template, caption, item, options w/ icons | `select` + enhancements #28–31 | ◐ |
| InputField: content types, validation, caret, selection, mobile keyboard | `input` + attrs + input workstream | ◐ |
| ScrollRect: movement type, inertia, elasticity, scrollbar links | `scroll` + `ScrollSettings` | ◐ |
| Mask / RectMask2D | #13 | ☐ |
| Selectable navigation (explicit/automatic) | #16 | ☐ |
| EventSystem + input modules | input workstream (auto-setup) | ☐ |
| LayoutGroup/ContentSizeFitter/AspectRatioFitter | **Covered by Yoga** — document mapping table, no native wrap needed | ✅ |
| Shadow/Outline/PositionAsUV1 | #18 | ☐ |
| Animator/Animation clips | motion bridge covers; document when to prefer DOTween vs Animator | ✅ |

## Annex B — Input workstream: official Unity input, zero-config

All input comes through official Unity APIs and wires itself automatically;
custom behavior is opt-in, never required setup.

| # | Capability | Why / benefit |
| --- | --- | --- |
| I1 | **Auto EventSystem + module** — detect `com.unity.inputsystem` → `InputSystemUIInputModule`, else `StandaloneInputModule`; create if missing | Today every consumer hand-rolls this; wrong choice = dead UI |
| I2 | **Active-device detection** — keyboard/mouse vs gamepad vs touch, with change events | Drives glyphs (#49), cursor visibility, tooltip behavior |
| I3 | **Glyph sets** — action → device-specific icon/text via Input System binding paths | Removes per-platform hint duplication |
| I4 | **Gamepad navigation defaults** — stick/dpad move, submit/cancel, wrap config, first-selected | Baseline console/controller usability |
| I5 | **Virtual cursor** for gamepad when free pointing is needed | Some UI (maps) needs pointer; ships ready |
| I6 | **Back routing** — Android back button, `Escape`, gamepad B/Circle → `UINav.Back()` | One of the most duplicated snippets in games |
| I7 | **Touch gestures** — edge swipe-back, long-press, double-tap, pinch, drag thresholds | Packaged recognizers instead of per-project rewrites |
| I8 | **Haptics** — preset vibrations (light/medium/heavy/selection) via `Handheld.Vibrate`/Input System rumble, gated by device | Pairs with gestures (e.g. swipe-back tick) |
| I9 | **TouchScreenKeyboard auto-open** for `input` focus on mobile | Currently manual and easy to forget |
| I10 | **Rebinding support** — `RebindAction`-style API powering `<rebind-row>` | Settings screens rebinding is high-effort today |
| I11 | **Simultaneous input** — mouse+touch coexist correctly (hover without stealing) | Hybrid devices (Surface, Steam Deck) |
| I12 | **IME/composition notes** + tested caret/selection on CJK input | Input correctness on real devices |

## Annex C — Ready-made navigation templates

Ship as opt-in building blocks (`Navigation` API + markup components like
`<screen>`, `<dialog>`), each with tests and a sample.

| # | Pattern | What it gives out of the box |
| --- | --- | --- |
| N1 | **Back stack** | `Push/Pop/Replace` of `<screen>`s; state restore on pop |
| N2 | **Hardware/software back** | Escape / Android back / gamepad B / edge-swipe → `Pop()`; emits haptic; closes top dialog first |
| N3 | **Edge swipe-back (mobile)** | Configurable edge zone, drag-follow preview, commit threshold + haptic tick |
| N4 | **Modal stack + priority queue** | Dialogs over current screen; queue by priority; scrim management |
| N5 | **Focus trap + return focus** | Modal confines gamepad focus; on close restores previous element |
| N6 | **Tab navigation** | `<tabbar>` switching with per-tab scroll/focus/data preserved |
| N7 | **Drawer navigation** | Edge slide menu; gesture + button open |
| N8 | **Wizard flow** | Linear steps, next/back guards, validation hooks |
| N9 | **Toast/snackbar queue** | Positioned, prioritized, deduplicated transient messages |
| N10 | **Screen transitions** | Fade/slide/crossfade presets between screens; respects ReducedMotion |
| N11 | **Deep-link routes** | `ui://settings/audio` → push chain resolution; testable without UI |
| N12 | **Master-detail** | List→detail split that collapses to push navigation on narrow screens |
| N13 | **Pause-menu pattern** | Gamepad-first stack: resume/settings/quit with confirm-guard |
| N14 | **Onboarding flow** | First-run paged intro (`carousel` + skip + completion flag) |
| N15 | **Empty/loading/error slots** | Standard tri-state regions every screen can declare |
| N16 | **HUD ↔ menu mode** | Input-context switch (gameplay ↔ UI) with nav suspend/resume |
| N17 | **Confirm-guard on dirty forms** | "Unsaved changes" intercept on back/close |
| N18 | **UI state restore** | Reopen a screen → same tab, scroll, selection, pending dialog |

## Annex D — Test contract for the feature surface

- Every numbered feature: at minimum an EditMode test covering mount,
  attribute parsing and teardown (the existing `UnityHtml*Tests` pattern).
- Input/navigation/animation features: PlayMode test simulating the input
  path (gamepad event, swipe gesture, back press).
- `<list>`/`<grid>`: perf regression test (recycling count, no per-frame
  allocs).
- No feature marked stable until its tests are green in the Phase 3.2 CI.

## Milestones

| Milestone | Scope | Exit criteria |
| --- | --- | --- |
| **v0.2.0** | Phase 1 | Fresh Unity project → working screen in <15 min following only README; no undocumented dependencies |
| **v0.3.0** | Phase 2 | Analyzer runs on any project root with dirty-scene safety + inbound refs; migration guide with dedicated-container pattern |
| **v0.4.0** | Phase 5 C1–C3 + Annex B I1–I8 | One-liner layout/containers, uGUI gaps closed, core widgets + zero-config input shipped with tests |
| **v0.5.0** | Phase 5 C4–C6 + Annex C | Binding attrs, screen-level components, nav templates N1–N10 |
| **v1.0.0-rc** | Phase 3 + Annex A done | EditMode tests green in CI; pinned ReactUnity compat matrix; published perf baseline; full uGUI coverage documented |

## Non-goals for now

- Arbitrary HTML5 parsing (the XML subset is the contract — document it,
  don't expand it).
- Claiming parity across all platforms, or better performance than
  hand-built UGUI, until CI and benchmarks prove it.
- Full automatic UI conversion — the analyzer informs migrations; it does
  not perform them.
