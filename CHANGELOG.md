# 0.2.1

- Completed exit animations release their ownership and recyclable DOTween references before reconciliation. Closing a page can no longer cancel a newly mounted header in another host and leave controls invisible.
- Preserve exactly-once exit completion on normal finish, cancellation, reduced motion and teardown.
- Regression tests cover completed exit cleanup and sequence reuse across hosts.

# 0.2.0

- Safe native remote document reader: UTF-8 text, scoped HTML or versioned JSON API, retaining original words and body.
- Exact HTTPS origin allow-list, redirects disabled, bounded downloads, timeouts and cancellable request ownership.
- Automatic approved-locale selection, source-language reporting, network refresh, version/content hashes, verified age-limited cache and bundled fallback.
- Native text blocks escape markup and disable TMP rich text; local CSS styles new paragraphs without executing website scripts.
- Reusable content binding with latest-request ownership and explicit original-source links.
- Importable Remote documents sample and complete quick start covering localization, legal acknowledgement, offline operation and limitations.
- Editor regression coverage for origin rejection, HTML/API fidelity, updates, cache corruption, limits and cancellation.

# 0.1.3

- Optional typed Unity Input System backend: touch, mouse, gamepad and back detection work in Input System-only players and survive IL2CPP stripping.
- Mount and application resume repair disabled stock UI modules, missing point/press references and disabled UI actions. Valid custom maps and shared action assets remain intact.
- The legacy input module is disabled when the new backend takes ownership; custom XR/multiplayer input modules are respected.
- Touch states now exercise the full EventSystem route in regression tests, rather than only invoking button callbacks.

# 0.1.2

- Automatically refresh container media, CSS viewport variables and full layout after window, container, Canvas-scale or safe-area changes; preserve inputs, controls and scroll state.
- Shared reversible orientation of screen-space ScaleWithScreenSize Canvas references, with an explicit opt-out.
- Safe-area padding in layout units, additive authored padding, native-safe parent and nested-edge deduplication; scroll resizing follows the final safe-area pass.
- Container-based `layout: wide` media feature, `data-layout="adaptive"`, viewport snapshot and post-layout change event.
- Reusable resize observer survives same-frame unmount/remount; zero-sized startup windows wait for valid geometry.
- Native C# callback resolver supports static documents without starting a JavaScript VM, including IL2CPP targets.
- Authored switch dimensions now override package defaults.
- Editor and PlayMode regression coverage for resize, state retention, safe area, Canvas ownership and native events.

# 0.1.1

- Rounded native switch with checked/value aliases, label activation, silent reconciliation, resize-safe knob motion and reduced-motion support.

# Changelog

All notable changes to UnityHTML are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## 0.1.4

- Mark the optional Input System assembly `AlwaysLinkAssembly` so UnityLinker processes its runtime initializer even when no scene types reference the assembly. This closes an Android IL2CPP stripping gap detected after the 0.1.3 Editor checks.

## [Unreleased]

### Added
- `LICENSE` (MIT) and this changelog.
- `FEATURES.md` — executable specification for the built-in feature surface
  (F1–F100 components/attributes, I1–I12 input capabilities, N1–N18
  navigation templates) with dependency-ordered implementation waves.
- `ROADMAP.md` — phased improvement plan.
- W1 feature surface: `<backdrop>`/`data-bg`, `<panel>`, `<spacer>`,
  `<divider>`, `data-anchor`, `data-stretch`, `data-center`,
  `data-platform`, `data-orientation`, `data-safe-area`, `data-raycast`,
  CanvasGroup attributes (`data-alpha`, `data-interactable`,
  `data-blocks-raycasts`), `data-shadow`/`data-outline`, explicit
  navigation (`data-nav-*`, `data-nav-wrap`), `data-first-selected`,
  `data-return-focus`, and a built-in `Globals.ui.Back()` bridge with
  `IUnityHtmlHost.BackRequested`.
- Parse diagnostics: malformed markup reports the document source name
  together with the XML line/position.
- Analyzer: configurable scan roots via `ProjectSettings/UnityHTMLAnalyzer.json`,
  dirty-scene guard before traversal, and inbound reference reporting.
- W2 uGUI coverage: `<mask>`/`show-graphic`, `<rectmask softness>`,
  `<scrollbar data-for>` linking into ScrollRects, `data-gradient`,
  `data-slice`, `data-tiled`, `data-fill` family (amount/origin/clockwise),
  and `<select>` upgrades (`max-height`, `item-height`, `searchable`
  template with an in-popup filter field, `option-icons` resolved through
  the media provider; `onChange` keeps reporting original indices while
  filtered).
- W3 input plumbing: zero-config `EventSystem` provisioning picking
  `InputSystemUIInputModule` when the Input System package is present
  (reflection — stays optional), `UnityHtmlInput.ActiveDevice`/
  `DeviceChanged`, `BackRequested` for Escape/Android-back/gamepad-B
  routed into every mounted host, and `Globals.haptics.Play()` +
  `data-haptic` with a swappable `IUnityHtmlHaptics` provider.
- W4 widgets: `<progress>` and `<radial>` filled bars (track/fill colors,
  low-threshold tint, smooth tweened fill, fill origin) and `<switch>`
  (toggle with sliding knob, color attrs, `onChange(bool)`).

### Fixed
- Removed the unused `DOTweenPro.dll` assembly reference — only free-tier
  DOTween API is used.
- README: corrected the `Mount` lifecycle claim (it reconciles before
  replacing) and documented the XHTML markup contract up front.

## [0.1.0] - 2026-09-30

### Added
- Initial public release: HTML/CSS mounting over ReactUnity UGUI,
  reconciliation with `id`/`data-key` stability, motion bridge with roles
  and reduced-motion support, tooltip layer, smooth scroll fixes, project
  UI inventory analyzer.
