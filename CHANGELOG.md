# 0.1.1

- Rounded native switch with checked/value aliases, label activation, silent reconciliation, resize-safe knob motion and reduced-motion support.

# Changelog

All notable changes to UnityHTML are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
