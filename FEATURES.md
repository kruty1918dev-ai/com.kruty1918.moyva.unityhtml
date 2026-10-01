# UnityHTML Feature Specification (executable)

This is the build spec for ROADMAP.md Phase 5 + Annexes B/C. Each item is
written so an executor (human or AI) can implement it **without design
decisions**: API, behavior contract, implementation location, and required
tests are all fixed here. If a spec is genuinely ambiguous or contradicts
runtime reality, stop and surface it — do not silently redesign.

## Executor rules (apply to every item)

1. **Component registration.** New tags register in
   `RegisterMoyvaComponents()` (`Runtime/UnityHtmlHost.cs`) via
   `UGUIContext.ComponentCreators["tag"] = ...`. Always guard with
   `ContainsKey` and, when extending an upstream tag, wrap the existing
   creator — follow `PatchScrollComponentCreator` (host.cs ~L689).
2. **New components** are `UGUIComponent` subclasses named
   `UnityHtml{Name}Component` in `Runtime/UnityHtml{Name}.cs` — pattern:
   `UnityHtmlSliderComponent` / `UnityHtmlSelectComponent` (events via
   `AddEventListener`, props via `SetProperty`, `IActivatableComponent` for
   label activation).
3. **`data-*` attributes** land in `component.Data` already — spec below
   names the *consumer* (existing subsystem or new file) that must read them.
   `on*` attributes are already routed as event listeners.
4. **Files/tests naming:** implementation `Runtime/UnityHtml{Name}.cs`;
   tests `Tests/Editor/UnityHtml{Name}Tests.cs` using the existing
   EditMode harness patterns (see `UnityHtmlHostTests.cs`).
5. **Docs:** every shipped item adds a row to the README elements/attrs
   table and a one-line contract note — same style as existing entries.
6. **No new hard dependencies.** Anything needing an optional package
   (Input System, Addressables) degrades gracefully via `#if` /
   reflection probe — see items marked `[gated]`.
7. **Order.** Waves below encode dependencies — implement in wave order.
   Items inside a wave are parallel-safe unless marked `dep:`.

Wave map:
- **W1 Foundations**: F1–F10 layout aliases + attr-expander, F14–F18,
  Annex A host settings, spec'd infra (focus/nav, safe-area, platform)
- **W2 Coverage**: F11–F13, F19–F22, select enhancements F28–F31
- **W3 Input**: I1–I12, then F49, F59, F88 (glyph/haptic/rebind consumers)
- **W4 Widgets**: F23–F48, F50–F52
- **W5 Behaviors**: F53–F77
- **W6 Screens & navigation**: F78–F92, N1–N18, Annex A remainder

---

## W1 — Layout aliases & platform primitives

> **Status: implemented** (unreleased). Delivered: `UnityHtmlAttributeExpander`
> (panel/spacer/divider/backdrop/data-bg/data-anchor/data-stretch/data-center/
> data-platform), `UnityHtmlElementEffects` (data-raycast, CanvasGroup attrs,
> data-shadow/data-outline, data-orientation, data-safe-area via Yoga padding),
> `ApplyNavigation` (data-nav-*, data-nav-wrap), `data-first-selected`,
> `data-return-focus`, `Globals.ui.Back()` + `IUnityHtmlHost.BackRequested`,
> `UnityHtmlEnvironmentWatcher` (rotation/resize re-apply), environment
> providers in `UnityHtmlEnvironment` (test-overridable).

### F1 — `<backdrop>` / `data-bg="screen"`
- **API:** `<backdrop src="atlas:ui/menu_bg" dim="0.55" close="true"/>`;
  `data-bg="screen"` on any element injects a fullscreen sibling `Image`
  *behind* that element.
- **Behavior:** anchors 0,0→1,1, ignores layout; `src` via media provider;
  `dim` = black overlay alpha (default 0); `close="true"` → click emits
  `onBackdrop` event and calls `UINav.Back()` (F92/N2).
- **Why:** fullscreen menu background is the most duplicated setup in game
  UI; today requires manual CSS per screen.
- **Impl:** `Runtime/UnityHtmlBackdropComponent.cs`; `data-bg` handled by
  an attribute→tree expander in `UnityHtmlDocumentTree` (new hook point:
  attribute expander pass before `Reconcile`).
- **Tests:** mounts behind siblings; `dim` alpha applied; click routes to
  back; removed on unmount; survives `UpdateRegion` of the panel.

### F2 — `<panel>`
- **API:** `<panel role="dialog|panel|toast" bg="screen">` = `<view>` +
  `data-motion-role` + optional backdrop.
- **Impl:** attribute expander (W1 shared) rewrites to `view` + attrs.
  **Tests:** expands correctly; motion role applied. **Dup:** alias — fine.

### F3 — `data-safe-area="all|top|bottom|left|right"`
- **Behavior:** computes padding from `Screen.safeArea` vs `Screen` rects,
  reapplied on orientation change. **Impl:** `UnityHtmlSafeArea.cs`
  component reading data attr; listener on resolution change.
- **Tests:** padding math from mocked safe area; update on resize.

### F4 — `data-anchor="top-left|top|top-right|left|center|right|bottom-left|bottom|bottom-right"`
- **Behavior:** expands to position+anchor CSS. **Impl:** attr expander →
  `position:absolute` + offsets. **Tests:** correct anchors per value.

### F5 — `data-stretch` / `data-center`
- **Behavior:** `stretch` → fill parent; `center` → centered both axes.
  Same expander. **Tests:** layout result.

### F6 — `<spacer size="8|8x16|flex">`
- **Behavior:** fixed or flex gap element. **Impl:** expander → `view` w/
  width/height/flex. **Tests:** sizes.

### F7 — `<divider orientation="h|v" inset="8"/>`
- **Behavior:** 1px (scaled) line w/ optional inset + `color` attr.
  **Impl:** `UnityHtmlDividerComponent` (Image). **Tests:** renders,
  orientation, color.

### F8 — `<overlay>`
- **Behavior:** sibling of tooltip layer, sorted above it; for loading
  veils/blockers. **Impl:** host-owned layer like `UnityHtmlTooltipLayer`.
  **Tests:** sorts above tooltip; teardown.

### F9 — `data-platform="mobile|desktop|console|editor"`
- **Behavior:** element + subtree skipped at build time when platform
  doesn't match (`Application.isMobilePlatform` etc. + device-class API
  from I2). **Impl:** expander (build-time removal, not hide).
  **Tests:** each platform branch (inject platform provider).

### F10 — `data-orientation="landscape|portrait"`
- **Behavior:** same as F9 keyed on `Screen.orientation` + live rebuild on
  change. **Impl:** expander + orientation watcher on host.
  **Tests:** show/hide on simulated rotation.

### F14 — `data-raycast="off|on"` (per-element `Graphic.raycastTarget`)
- **Impl:** attr consumer walking Graphics under element.
  **Tests:** raycastTarget set recursively; restored on unmount.

### F15 — CanvasGroup attrs: `data-alpha`, `data-interactable`, `data-blocks-raycasts`
- **Impl:** consumer adds/uses CanvasGroup on element. **Tests:** all three
  props; interplay with motion alpha (documented: motion wins during tween).

### F16 — `data-nav-up/down/left/right="#id"` + `data-nav-wrap`
- **Behavior:** sets `Selectable.navigation` explicit targets; `wrap`
  cycles last→first within parent. **Impl:** `UnityHtmlNavigation.cs`
  post-mount pass resolving ids → Selectables. **Tests:** explicit links;
  wrap behavior; fallback to automatic when absent.

### F17 — `data-first-selected` / `data-return-focus`
- **Behavior:** mount → `EventSystem.SetSelectedGameObject`; modal close →
  restore prior selection. **Impl:** `UnityHtmlNavigation.cs` + focus
  stack. **Tests:** selection on mount; restore after dialog close.

### F18 — `data-shadow="dx,dy,color"` / `data-outline="size,color"`
- **Impl:** adds `Shadow`/`Outline` to Graphics. **Tests:** component
  added w/ parsed values.

### Annex A — host-level settings (W1)
- `UnityHtmlHostSettings`: canvas sort order (`data-order` →
  `Canvas.sortingOrder` on root Canvas), CanvasScaler mode binding
  (ScaleWithScreenSize default, reference res + match attrs).
  **Tests:** scaler config applied on mount.

---

## W2 — uGUI coverage

> **Status: partially implemented** (unreleased). Delivered: `<scrollbar>`
> (tag wraps upstream `ScrollbarComponent`, `data-for` linking into the
> target ScrollRect), `<mask>`/`show-graphic`, `<rectmask softness>`,
> `data-gradient`, `data-slice`, `data-tiled`, `data-fill` family, select
> `max-height`/`item-height`/`searchable`/`option-icons`.
> Deferred: select grouped options and edge auto-flip (needs a TMP_Dropdown
> subclass — note for a later pass). Upstream already provides `rawimage`,
> `video`, `svg` — F12/F100 marked covered, not rebuilt.

### F11 — `<scrollbar>`
- **API:** `<scrollbar for="#scrollId" direction="v|h" size="8"/>`;
  auto-created when `<scroll>` declares `scrollbar="true"`.
- **Impl:** `UnityHtmlScrollbarComponent` (uGUI Scrollbar wired to the
  ScrollRect). **Tests:** pairing, drag moves scroll, teardown.

### F12 — `<rawimage>`
- **API:** `source` + `uv="x,y,w,h"`. **Impl:** `UnityHtmlRawImageComponent`
  (RawImage; source via media provider incl. RenderTexture).
  **Tests:** texture + uvRect applied.

### F13 — `<mask>` / `<rectmask>`
- **API:** wrapper elements; `mask` uses child Image as stencil,
  `rectmask` = soft-edge RectMask2D (`softness` attr).
- **Impl:** two components; children rendered normally inside.
  **Tests:** clipping flags; softness values.

### F19 — `data-gradient="c1,c2[,c3,c4]"` (TMP vertex gradient)
- **Impl:** consumer → `TMP_Text.enableVertexGradient`/`colorGradient`.
  **Tests:** 2- and 4-color parse.

### F20 — `data-slice` · F21 — `data-tiled` · F22 — `data-fill="linear|radial"` + `data-fill-amount`, `data-fill-origin`
- **Impl:** set `Image.type` Sliced/Tiled/Filled (+`fillMethod`,
  `fillOrigin`, `fillClockwise`) on `image`. F22 backs F24/F53.
  **Tests:** each Image.type path; fillAmount binding via `SetValue`.

### Select deep-configuration (F28–F31) — extends existing `select`
- **F28 `searchable`**: adds filter input above option list, `onSearch`.
- **F29 option templates**: `option-icon`, `option-template` attrs → item
  prefab pattern inside dropdown.
- **F30 `data-group` on options**: grouped headers in option list.
- **F31 `max-height`, `item-height`, `scroll`**: dropdown panel sizing +
  internal scroll; `anchor="auto"` flips up when near screen edge.
- **Impl:** extend `UnityHtmlSelectComponent`; dropdown panel becomes a
  styled sub-tree (own mini doc or built manually — choose built manually:
  template via HTML string constant, overridable via `option-template`).
- **Tests:** filter reduces options; icons bind; groups render headers;
  panel clamps to screen bounds; keyboard/gamepad nav inside list.

---

## W3 — Input

> **Status: partially implemented** (unreleased). Delivered: auto
> EventSystem + best-module selection (I1), active-device tracking (I2),
> back routing Escape/Android/gamepad-B → host `BackRequested` (I6, legacy
> polling), haptics bridge + `data-haptic` (I8, Handheld.Vibrate default,
> swappable `IUnityHtmlHaptics` provider). Deferred: glyph provider (I3),
> virtual cursor (I5), rich gesture set beyond back (I7), touch keyboard
> (I9), rebinding (I10), simultaneous-source rules (I11), IME (I12). (Annex B)

### I1 — Auto EventSystem + input module
- **Behavior:** on host mount: no EventSystem → create; probe
  `UnityEngine.InputSystem` assembly → `InputSystemUIInputModule`, else
  `StandaloneInputModule`. `[gated]` via reflection/`#if`.
- **Impl:** `Runtime/Input/UnityHtmlInputBootstrap.cs`.
- **Tests:** creation when absent; reuse when present; correct module type
  (both branches via compile variants).

### I2 — Active-device detection
- **API:** `host.Input.Device` ∈ `{KeyboardMouse, Gamepad, Touch}`;
  `DeviceChanged` event. Detects via last-event source (Input System
  `onActionTriggered`/event polling; legacy: key/mouse vs touch counts).
- **Impl:** `Runtime/Input/UnityHtmlDeviceWatcher.cs`.
- **Tests:** simulated events flip device; event fires once per change.

### I3 — Glyph sets
- **API:** `Globals.input.Glyph("submit")` → text/sprite per active device;
  `<glyph action="submit"/>` element auto-updates on device change.
  Binding paths from Input System actions asset injected via
  `host.Input.BindActionMap(asset)`; legacy fallback maps KeyCode names.
- **Impl:** `Runtime/Input/UnityHtmlGlyphProvider.cs` + component (F49
  consumes). **Tests:** returns right glyph per mocked device; updates on
  switch; missing action → key name fallback.

### I4 — Gamepad nav defaults
- **Behavior:** dpad/stick = `Move`, south button = `Submit`, east =
  `Cancel`→back; `data-nav-wrap` honored; works via I1 module config.
- **Tests:** synthesized move/submit events.

### I5 — Virtual cursor
- **API:** `host.Input.EnableVirtualCursor()` → cursor GameObject driven by
  stick, emits pointer events. For map-style free pointing.
- **Tests:** cursor moves, click dispatches to element under it.

### I6 — Back routing
- **Behavior:** `Escape`, Android `KeyCode.Escape`/system back, gamepad
  east button → `UINav.Back()` (topmost handler: dialog → screen stack →
  `onBackBlocked`). Consumed-or-propagate result.
- **Impl:** `Runtime/Input/UnityHtmlBackRouter.cs` + `UINav` (N1).
- **Tests:** each source routes; consumed when dialog open; propagates when
  stack empty.

### I7 — Touch gestures `[mobile]`
- **Recognizers:** `EdgeSwipe` (zone `edgeWidth`, commit threshold,
  `onEdgeSwipe="left|right"`), `LongPress` (dur attr, used by F63 +
  tooltip), `DoubleTap`, `Pinch` (`onPinch(scale,delta)`).
- **Impl:** `Runtime/Input/UnityHtmlGestures.cs` on host root; dispatches
  as events. **Tests:** synthetic touch streams trigger each recognizer;
  thresholds respected.

### I8 — Haptics
- **API:** `Globals.haptics.Play("light|medium|heavy|selection")`;
  `data-haptic="light"` on elements auto-plays on their primary event.
  `Handheld.Vibrate` fallback + Input System `Gamepad.SetMotorSpeeds`;
  no-op where unsupported. `[gated]`
- **Tests:** preset mapping; no-op path doesn't throw; data-haptic wiring.

### I9 — TouchScreenKeyboard auto-open
- **Behavior:** `input` focus on touch device → `TouchScreenKeyboard.Open`
  (respecting `contentType`); close syncs value back. Mobile only.
- **Tests:** opens with right keyboard type; value sync on done.

### I10 — Rebinding `[gated: InputSystem]`
- **API:** `Globals.input.Rebind("jump", onDone)` — listen-for-input flow
  w/ cancel + conflict callback. Powers F88.
- **Tests:** rebind applies to action map; cancel path.

### I11 — Simultaneous pointer sources
- **Behavior:** touch doesn't suppress mouse hover; device watcher treats
  them independently. **Tests:** mixed event stream → correct Device
  transitions (touch only while touching).

### I12 — IME/caret correctness
- **Behavior:** composition string handling documented + caret/selection
  preserved through `SetValue` (existing tests cover base; add CJK string
  cases). **Tests:** composition-safe SetValue.

---

## W4 — Widgets

> **Status: started** (unreleased). Delivered: `<progress>` (F23),
> `<radial>` (F24), `<switch>` (F25). Deferred in this wave: segmented,
> radio, search, badge, spinner, skeleton, empty-state, virtualized
> list/grid, slot, hotbar, avatar, status-dot, counter, currency, chat,
> carousel, accordion, context/radial menus, joystick, glyph, rich
> tooltip, coachmark, ticker. (C3)

Component specs — all follow `UnityHtml{Name}Component` + registration +
tests pattern. Reuse column lists what it composes.

| # | Tag | API essentials | Behavior contract | Reuses |
| --- | --- | --- | --- | --- |
| F23 | `<progress>` | `value max smooth="0.25" gradient="a,b" low-color` | Animated fill toward value; `low-color` threshold tint | F22 fill |
| F24 | `<radial>` | `value max clockwise thickness` | Ring fill | F22 radial |
| F25 | `<switch>` | `value onChange` | Switch-styled toggle; same event contract | `toggle` |
| F26 | `<segmented>` | `options="A&#124;B" value onChange` | Exclusive segment select; sliding indicator via motion | `toggle`-group logic |
| F27 | `<radio>` | `name value checked onChange` | Grouped exclusivity via shared `name` | `toggle` |
| F28–F31 | select cfg | see W2 | — | `select` |
| F30' | `<search>` | `placeholder debounce="0.3" onSearch onClear` | Clear ✕ appears when non-empty; debounced callback | `input` |
| F31' | `<badge>` | `value max="99"` | Corner bubble; `value=0` hides; `max+` overflow | `text` |
| F32 | `<spinner>` | `size speed` | Rotating element; honors ReducedMotion | motion infra |
| F33 | `<skeleton>` | `lines="3"` | Shimmer blocks; ReducedMotion → static gray | `view`+anim |
| F34 | `<empty-state>` | `icon title hint action-text onAction` | Standard empty slot | `view`/`text`/`button` |
| F35 | `<list>` | `<template>` child + `data-bind-list="items"` + `item-height` | Recycled rows; only visible±buffer instantiated; keyed on row `id`/`data-key` | `scroll`, reconciler keys |
| F36 | `<grid>` | `columns` or `cell-size` | Grid variant of F35 | F35 core |
| F37 | `<slot>` | `icon count rarity empty-icon` | Inventory cell: icon, count text, rarity border | `image`/`text` |
| F38 | `<hotbar>` | `<slot>` children + `keys="1-9"` | Slots + glyph labels auto from I3 | F37, F49 |
| F39 | `<avatar>` | `src frame status rounded` | Masked image + frame + status-dot + initials fallback | F13, F40 |
| F40 | `<status-dot>` | `status="online|away|busy|offline"` | Colored dot | `image` |
| F41 | `<counter>` | `value duration format` | Number rolls toward value | F57 format |
| F42 | `<currency>` | `icon amount delta` | Icon+amount; positive/negative flash | F41 |
| F43 | `<chat>` | `channels fade-after input` | Scroll log + entry field + old-msg fade; autoscroll pin | `scroll`,`input` |
| F44 | `<carousel>` | `snap dots auto` | Paged scroll w/ dot indicators | `scroll` |
| F45 | `<accordion>` | `header`/`body` slots, `open` | Collapse/expand animated | motion |
| F46 | `<context-menu>` | `<item>` children, `for="#id"` | Right-click/long-press popup at cursor, screen-clamped | overlay, tooltip infra |
| F47 | `<radial-menu>` | `<item>` children | Pie slice select, pointer-angle pick | overlay |
| F48 | `<joystick>` | `radius float onMove(dx,dy) onEnd` | Virtual stick; `float` = appears at touch point | — |
| F49 | `<glyph>` | `action` | Per-device binding display | I3 |
| F50 | rich tooltip | `data-tooltip-title/-body/-icon/-footer` | Structured tooltip layout | existing layer |
| F51 | `<coachmark>` | `for="#id" text placement` | Dim + hole-punch + anchored hint | F8 overlay |
| F52 | `<ticker>` | `speed pause-on-hover` | Marquee scroll for overflow text | `scroll`-like anim |

**Per-widget tests (template):** mount+props applied · event callbacks ·
disabled state · unmount clean · ReducedMotion where animated · screen
clamp where floating · (F35/F36) recycling count + no per-frame allocs.

---

## W5 — Behavior & binding attributes (C4)

Consumers noted; all read `component.Data` (existing routing).

| # | Attr | Contract | Consumer |
| --- | --- | --- | --- |
| F53 | `data-cooldown="8"` (+`data-cooldown-remaining` via SetValue) | Radial sweep + input block until done; `onCooldownEnd` | F22 fill + Selectable |
| F54 | `data-typewriter="30"` (chars/s) | Reveal animation; click skips; ReducedMotion → instant | `text` post-mount pass |
| F55 | `data-countdown="90"`/`data-elapsed` | Self-updating `mm:ss` (or `format` attr) | host tick service |
| F56 | `data-l10n="key"` | Resolves via `Globals.l10n.Get(key)`; re-render on locale change event | l10n bridge interface |
| F57 | `data-format="compact|percent|currency:X"` | Number formatting in `text`/`counter` | shared formatter |
| F58 | `data-sfx="click:hover=submit"` | Sound hooks → `Globals.audio.Play(id)` if exposed; silent no-op otherwise | event pipeline |
| F59 | `data-haptic="light"` | On primary event → I8 | event pipeline |
| F60 | `data-disabled="reason text"` | Disabled style + reason via tooltip | Selectable + tooltip |
| F61 | `data-loading` | Button: spinner replaces label, input blocked, restored after `onClick` async/SetValue release | F32 + button |
| F62 | `data-confirm` | First tap → armed state (label swap/tint); second within 2s fires `onClick` | button wrapper |
| F63 | `data-longpress="0.5"` | Alt action `onLongPress` | I7 recognizer |
| F64 | `data-draggable` + `data-drop="type"` | Drag ghost follows pointer; `onDrop(payload)` on matching targets | drag events (exist) |
| F65 | `data-swipe-actions` | Row swipe reveals action cluster (children `<action>`); touch only | I7 + scroll row |
| F66 | `data-pull-refresh` | Overdrag past threshold → `onRefresh`; spinner while pending | `scroll` |
| F67 | `data-sortable` | Long-press-drag reorders siblings; `onReorder(from,to)` | F63 + reconciler |
| F68 | `data-scroll-into-view` | Focused/selected element auto-scrolled visible | F16 + `scroll` |
| F69 | `data-visible`/`data-hidden` | Toggle `CanvasGroup.alpha`+raycasts (`hidden` keeps layout; `visible=false` removes) | F15 |
| F70 | `data-theme="dark"` | CSS var token set swap at root; cascades | CSS vars (verify ReactUnity support → else style-sheet swap) |
| F71 | `data-contrast="high"` | High-contrast token preset | same mechanism as F70 |
| F72 | `data-bind="global.prop"` | Two-way: element ↔ global object property; removes onChange+SetValue boilerplate | `UpdateGlobals` + SetProperty |
| F73 | `data-bind-list="items"` | Source for F35/F36 templates | F35 |
| F74 | `data-if="expr"`/`data-else` | Conditional subtree render w/o rebuild; **first check ReactUnity `c-if`/conditional support — if present, expose alias only** | document-tree pass |
| F75 | `data-testid="x"` | Queryable via `host.FindByTestId("x")` for automation + own tests | host query API |
| F76 | `data-analytics="evt:key=val"` | Fires `Globals.analytics.Track(...)` if exposed; no-op otherwise | event pipeline |
| F77 | `data-preload`/`data-lazy` | Preload assets during mount; lazy images load at scroll-into-view | media provider |

---

## W6 — Screens, media, navigation

### Screen-level components (C5)

| # | API | Contract | Reuses |
| --- | --- | --- | --- |
| F78 | `<dialog scrim="0.6" close-on-scrim="true">` | Modal: overlay scrim, focus trap (N5), Escape→close (I6), exit motion then unmount | F8, F17, motion roles |
| F79 | `<toast text duration="3" priority>` | `Globals.ui.Toast(...)` queue: stacking, dedup, auto-expire | F8, motion `toast` role |
| F80 | `<snackbar>` = toast + `action-text onAction` | Undo-style action button | F79 |
| F81 | `<header title back="true">` | Title + auto-wired back → `UINav.Back()` | I6, N1 |
| F82 | `<tabbar><tab for="#page">` | Tab switch preserves per-tab scroll/focus/data | N6 |
| F83 | `<drawer side="left" width>` | Edge slide-in; scrim; gesture+button open | F8, motion `panel` |
| F84 | `<wizard>` + `<step>` | next/back guards, `onValidate` per step | N8 |
| F85 | `<tutorial>` + `<tutorial-step for="#id">` | Sequenced coachmarks, progress, skip | F51 |
| F86 | `<notification-center>` | Persistent inbox list w/ read state | F35, F79 store |
| F87 | `Globals.ui.Confirm(title,body)` → `Task<bool>`-style callback | Promise-ish confirm dialog; kills per-screen clones | F78 |
| F88 | `<rebind-row action="jump">` | Label + current glyph + capture flow | I3, I10 |
| F89 | `<master-detail>` | Split view; collapses to push-nav under width threshold | F92, N12 |
| F90 | `Globals.ui.Alert/Prompt(...)` | Quick dialogs family | F78 |
| F91 | `<splash min-duration skip>` | Boot logo sequence | F8 |
| F92 | `<screen name="settings" transition="slide">` | Named screen unit owned by nav stack (N1): lifecycle, transitions, state | N1 core |

### Media (C6)

| # | API | Contract |
| --- | --- | --- |
| F93 | `source="resources://x" / "atlas://a:s" / "addressables://k" / "url://u"` | Unified media provider chain; unknown scheme → error diagnostic |
| F94 | `url://` | Remote image fetch + mem/disk cache + `placeholder`/`error` attrs; offline fallback |
| F95 | `data-sprite-normal/hover/pressed/disabled` | Sprite swap per state (button sprite transition path) |
| F96 | `<world-label for="transformPath|id">` | Screen-space element tracking a world transform each frame (nameplates, damage numbers) |
| F97 | `dir="rtl"` | RTL text + mirrored layout pass |
| F98 | `data-a11y-label` | Label for accessibility where supported; stored for future Unity a11y APIs |
| F99 | subtree `data-motion` inherit | `data-motion="none"` gates descendants unless overridden |
| F100 | `<video>` | RawImage+VideoPlayer: `src`, `loop`, `muted`, `onEnd`; **stretch — may slip** |

### Navigation templates (Annex C) — all built on `UINav` service + F92

| # | Pattern | Contract |
| --- | --- | --- |
| N1 | Back stack | `Globals.nav.Push/Pop/Replace("name", params)`; state restore on pop |
| N2 | Hardware back | I6 router → Pop; dialog closes before screen pops; result consumed/propagated |
| N3 | Edge swipe-back | I7 EdgeSwipe + drag-follow preview of previous screen + commit threshold + I8 tick |
| N4 | Modal stack+queue | Priority queue; only top modal interactive; scrim stack |
| N5 | Focus trap | Modal clips `Selectable` nav; close restores prior focus (F17) |
| N6 | Tabs w/ state | Per-tab scroll position, focus, region data preserved (F82) |
| N7 | Drawer nav | F83 wired into stack (back closes drawer first) |
| N8 | Wizard | F84 + nav guards |
| N9 | Toast queue | F79/F80 manager: position, priority, dedup |
| N10 | Transitions | `transition="fade|slide|crossfade"` on `<screen>`/Push; ReducedMotion → instant |
| N11 | Deep links | `ui://settings/audio` parses → push chain; unit-testable sans UI |
| N12 | Master-detail | F89 responsive collapse ↔ push nav |
| N13 | Pause menu | Template screen: resume/settings/quit + confirm-guard; gamepad-first focus |
| N14 | Onboarding | First-run carousel + skip + persisted completion flag |
| N15 | Tri-state slots | `data-state="loading|empty|error"` regions per screen w/ default visuals |
| N16 | HUD ↔ menu | Input context switch: suspends gameplay input map, enables UI map, restores on exit |
| N17 | Dirty-form guard | `data-dirty` screen → back/close intercepts with F87 confirm |
| N18 | State restore | Reopen screen → tab/scroll/selection/pending dialog restored |

**Nav tests (template):** push/pop state integrity · back consumed
ordering (toast < dialog < drawer < screen) · focus trap/restore ·
transition honors ReducedMotion · deep-link resolution unit tests ·
restore round-trip (N18).

---

## Coverage claim after completion

- Every uGUI component reachable via tag/attr or documented Yoga alias
  (Annex A matrix in ROADMAP.md stays the checklist — flip ☐→✅ as done).
- Input: zero-config on Input System & legacy; gestures+haptics packaged.
- Navigation: N1–N18 templates cover the standard game flows; bespoke
  flows compose `<screen>` + `UINav` primitives.
- Test contract (Annex D, ROADMAP) applies to every row above.
