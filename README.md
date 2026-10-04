# Moyva UnityHTML

![UPM package](https://img.shields.io/badge/UPM-package-blue)
![version](https://img.shields.io/github/v/tag/kruty1918dev-ai/com.kruty1918.moyva.unityhtml?label=version&sort=semver)

UnityHTML is Moyva's thin runtime wrapper around ReactUnity UGUI. It mounts HTML and CSS stored as Unity `TextAsset` files into an existing `RectTransform`.

> **Markup contract:** markup is parsed as XML (`XmlDocument`), not HTML5.
> Every tag must be closed (`<img />`, not `<img>`), every attribute must
> have a quoted value (`disabled="true"`, not `disabled`), and `&` must be
> escaped as `&amp;`. Web UI knowledge transfers; arbitrary web markup does
> not. The supported tag/CSS surface is the table under [Markup](#markup).

## Install (Unity Package Manager)

Three steps, all required:

1. **DOTween (free).** Motion runs on DOTween — install the free version
   into `Assets/Plugins/Demigiant/DOTween` (Asset Store package or the
   downloadable unitypackage from Demigiant). DOTween *Pro* is not needed.
2. **ReactUnity** — commit-pinned git dependencies. Add to
   `Packages/manifest.json` (these exact commits are the tested set):

   ```json
   "com.reactunity.core": "https://github.com/ReactUnity/core.git#8c4caa95f49b45e8fb7bb1f833eaa08adaa31496",
   "com.reactunity.jint": "https://github.com/ReactUnity/core.git#5788df06c9525ffbd24b7f2ef55b9c638386e808",
   "com.reactunity.quickjs": "https://github.com/ReactUnity/core.git#6b28051d738504283bff89bd7584eaf1287debec",
   ```

3. **UnityHTML itself:**

   ```json
   "com.kruty1918.moyva.unityhtml": "https://github.com/kruty1918dev-ai/com.kruty1918.moyva.unityhtml.git#v0.2.0"
   ```

   or Package Manager → **+** → **Add package from git URL** with the same
   URL (no tag = latest `main`).

`com.unity.ugui` and TextMeshPro ship with Unity — no action needed.

CSS presentation assets should be authored as plain CSS text with a Unity text extension, for example `HomeMenuShell.css.txt`. This keeps the file readable while ensuring Unity imports it as a populated `TextAsset`.

The script engine is selected per platform: QuickJS everywhere except Linux (editor and standalone), which uses Jint. Keep `on*` callbacks to short expressions that behave identically on both engines — call into a C# bridge object rather than writing logic in markup.

## Remote text, APIs and legal documents

Load text, a website document region or a JSON API into a native scrollable field.
Approved locale variants follow your localization module; changed content receives
local typography automatically. The reader retains the original body, checks cache
integrity, bounds HTTPS requests and keeps scripts out of the UI. First-run policy
acknowledgement remains separate from optional analytics/advertising consent.

Start with the importable **Remote documents** sample and the
[remote content quick start and complete guide](Documentation~/remote-content.md).
The bundled sample is a labelled draft with no server required.

## Automatic resizing, tablets and safe area

Mounted documents automatically observe their **container**, Canvas scale, window
size and safe area. A resize, orientation change or split-screen resize refreshes
media queries and layout without remounting the DOM. Native controls keep their
identity, typed input and switch state; scroll position and velocity are restored
after content sizing. Unchanged frames only compare a small geometry snapshot.
A temporarily zero-sized window keeps the previous valid layout.

An existing screen-space `CanvasScaler` in `ScaleWithScreenSize` mode automatically
orients its authored reference resolution to the window. Multiple hosts share one
policy; the last unmount restores the authored resolution. Designer changes are
respected. World-space canvases and other scale modes are left alone. Set
`host.AutoOrientCanvas = false` **before mounting** if your project already owns
this policy. This does not change player orientation settings.

Use percentage/flex sizes for fluid layouts. Fixed pixel sizes retain their authored
meaning; the framework cannot infer a game's intended placement from arbitrary
fixed coordinates. Standard width, height and orientation media queries refer to
**container layout units**, not the physical device model. A built-in
`@media (layout: wide)` matches width >= 960 units and aspect > 1.1. The default
`data-layout="adaptive"` container stacks children on compact screens and uses a
wrapping row in wide layouts; authored CSS can override either behavior.

```html
<view class="page" data-safe-area="all">
  <view data-layout="adaptive"><button><text>Play</text></button><button><text>Collection</text></button></view>
  <scroll class="content"><view><text>Scrollable content</text></view></scroll>
</view>
```

```css
.page { width: 100%; height: 100%; padding: 24px; }
.content { flex-grow: 1; }
@media (layout: wide) { .page { padding: 40px; } }
```

`data-safe-area="all"` (or `top|bottom|left|right`) adds insets to authored padding.
Insets are converted to layout units and measured relative to the host: a native
parent already inside the safe area receives no duplicate inset. Nested marked
containers reserve each edge once. Unmarked backgrounds remain full bleed.
World-space canvases and secondary displays have no mobile notch inset.
Root CSS variables are updated automatically: `--uh-viewport-width`,
`--uh-viewport-height`, `--uh-safe-left`, `--uh-safe-bottom`, `--uh-safe-right`,
`--uh-safe-top`. Corresponding `safe-area-*` numeric media features are available.

`UnityHtmlHost.Viewport` exposes `Size`, `ScreenSize`, `ScreenRect`, `SafeInsets`,
`PixelScale`, `IsValid`, `IsLandscape` and `IsWide`. Optional `ViewportChanged` runs
**after** layout, safe padding and scroll restoration. Use it when a game needs to
fit a world camera around its UI; normal UI resizing needs no C# polling.

## Native C# callbacks (IL2CPP-friendly static documents)

Set `host.NativeEventResolver` before mounting to render without starting a
JavaScript VM. The resolver maps an exact expression to a compatible delegate;
reject unknown expressions instead of silently ignoring them.

```csharp
host.NativeEventResolver = expression => expression == "play"
    ? (System.Action)(() => StartGame())
    : throw new System.InvalidOperationException("Unknown UI callback: " + expression);
host.Mount(root, new UnityHtmlDocument("<button onClick='play'><text>Play</text></button>", css, "Menu"));
```

Native mode rejects `<script>`. It is intended for static documents with C# state
and reconciliation. Existing script-driven documents keep their engine behavior.
No JavaScript expressions are evaluated in native mode.

## Quick start — empty scene to a working button

1. Create `Assets/UI/HomeShell.html` (imports as `TextAsset`):

   ```html
   <view className="shell">
       <text>Hello</text>
       <button onClick="Globals.menu.Clicked()"><text>Play</text></button>
   </view>
   ```

2. Create `Assets/UI/HomeShell.css.txt`:

   ```css
   .shell { display: flex; flex-direction: column; gap: 12px;
            width: 400px; height: 300px; margin: auto;
            background-color: rgb(20, 20, 28); }
   ```

3. Bootstrap MonoBehaviour — creates `Canvas` + `EventSystem` if missing,
   mounts, disposes:

   ```csharp
   using System.Collections.Generic;
   using UnityEngine;
   using UnityEngine.EventSystems;
   using UnityEngine.UI;
   using UnityHTML.Runtime;

   public sealed class MenuScreen : MonoBehaviour
   {
       [SerializeField] private TextAsset html;
       [SerializeField] private TextAsset css;

       private UnityHtmlHost _host;

       private void Start()
       {
           // Dedicated container: Mount destroys all children of the root.
           var canvas = new GameObject("MenuCanvas",
               typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
           canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
           if (FindFirstObjectByType<EventSystem>() == null)
               new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

           _host = new UnityHtmlHost();
           var result = _host.Mount(
               canvas.GetComponent<RectTransform>(),
               UnityHtmlDocument.FromTextAssets(html, css, "HomeShell"),
               new Dictionary<string, object> { ["menu"] = new MenuBridge() });
           if (!result.Succeeded) Debug.LogError(result.ErrorMessage);
       }

       private void OnDestroy() => _host?.Dispose();

       private sealed class MenuBridge
       {
           public void Clicked() => Debug.Log("Play pressed");
       }
   }
   ```

4. Assign the assets, press Play — `Play` logs through the C# bridge.

### Troubleshooting

- **Blank screen, no error:** check `UnityHtmlMountResult.Succeeded` /
  `ErrorMessage` — mount failures are returned, not thrown.
- **`...XmlException...` in ErrorMessage:** markup violated the XML
  contract above — unclosed tag, unquoted attribute, raw `&`.
- **Nothing reacts to clicks:** missing `EventSystem` (the bootstrap above
  creates one) or another `Canvas` sorting over the host.
- **Black/invisible text:** TMP font not resolved — expose a font via the
  `moyvaFont` global, or project TMP default font asset.
- **Package doesn't resolve:** `git` must be on `PATH` for UPM git
  dependencies; on Linux the editor uses the Jint engine, not QuickJS.

## Runtime Lifecycle

Create a `UnityHtmlDocument` from HTML and optional CSS text, then mount it through `IUnityHtmlHost`. The host creates a ReactUnity `UGUIContext`, injects a `GlobalRecord`, selects the engine, inserts CSS before `Start()`, and calculates layout.

```csharp
var document = UnityHtmlDocument.FromTextAssets(htmlAsset, cssAsset, "HomeMenuShell");
var result = host.Mount(root, document, new Dictionary<string, object>
{
    ["moyvaMenu"] = bridge
});
```

`Mount` on a live host first attempts an in-place reconciliation of the
mounted document (preserving elements, focus and scroll); when the document
or root is incompatible it falls back to a fresh mount that destroys all
children of the passed root. `Unmount()` (also called by `Dispose()`, mount
failure and scene unload through the owning presenter) tears it down
completely:

- every `on*` listener remover runs — handlers swap, they never stack;
- active motion tweens are killed and an in-flight declarative exit still fires `ExitFinished` exactly once;
- the tooltip layer and its per-element targets are destroyed;
- scroll tracking, the ReactUnity context, every Yoga node handle and the components ReactUnity attaches to the mount root are all released — no managed graph survives, so region removal, locale re-render and exit-to-menu are safe to repeat indefinitely.

### Updating mounted UI

`UpdateRegion(id, html)` / `UpdateRegions(regions)` reconcile a subtree in place instead of remounting — elements keep their GameObjects, focus and scroll state. Reconciliation keys on `id` ?? `data-key`; siblings with neither match positionally, so dynamic rows must carry a stable `data-key` or their focus/tooltip/motion state migrates on reorder. `UpdateRegion` returns `false` when the id is missing, the region sits inside another pending region, or reconciliation throws — callers should fall back to a full `Mount`. `SetValue(id, value)` updates a `text`/`input` value without touching element identity.

## Markup

Attributes route by prefix:

- `onX="..."` — script callback bound through `SetEventListener`; the snippet runs as `function(event, sender)`, so the event value is `event` (e.g. `onChange="Globals.menu.SetVolume(event)"`). Re-assigning the attribute swaps the listener.
- `data-x="..."` — component data (`component.Data["x"]`), consumed by tooltips, motion and reconciliation; never styles.
- anything else — component property or style hook (`className`, `value`, `disabled`, `source`, `placeholder`, ...).

### Elements

The tags below are the tested contract surface (upstream ReactUnity registers more — only these are exercised by Moyva):

| Tag | Renders | Contract notes |
| --- | --- | --- |
| `view` | container `RectTransform` | grouping and motion targets |
| `text` | `TMP_Text` | inner text is the content |
| `button` | `Button` + label | `onClick` routes through `Button.onClick`, not a pointer handler |
| `toggle` | `Toggle` | `onChange(bool)`; pair with a sibling `<label for="#id">` as the click target — never wrap the toggle in its label (clicks propagate to ancestors and would double-toggle) |
| `label` | text | `for="#id"` activates the target control exactly once per click |
| `input` | `TMP_InputField` | centered single-line text; `value`, `placeholder`; `onChange(string)`, `onEndEdit`, `onSubmit` |
| `slider` | `Slider` + value text | `value`, `min`/`max`, `wholeNumbers`, `format` (`decimal1` default, `decimal2`, `integer`, `percent`), `suffix`, `disabled`; `onChange(float)`, `onBeginChange`, `onEndChange` |
| `select` | `TMP_Dropdown` | `options="A&#124;B&#124;C"` (pipe-separated), `value` (index), `disabled`, `max-height`, `item-height`, `searchable`, `option-icons="a.png,b.png"` (per-index, media-provider resolved); `onChange(int)` always reports the original option index even while a search filter is active |
| `scroll` | `MoyvaSmoothScrollRect` | wheel accumulation fixed; configured per host via `IUnityHtmlHost.ScrollSettings` |
| `image`/`img` | `Image` | `source` resolves through the media provider |
| `icon` | icon font text | |
| `a`/`anchor` | link | |
| `backdrop` | fullscreen background stack | `src`, `dim`, `color`, `close="true"` (click → `Globals.ui.Back()`); declared children render above the layers |
| `scrollbar` | `Scrollbar` | `horizontal`, `inverted`; link to a scroll with `data-for="#scrollId"` |
| `mask` | `Mask` | clips children; `show-graphic="true"` draws the stencil |
| `rectmask` | `RectMask2D` | rect clipping; `softness="x,y"` for soft edges |
| `rawimage` | `RawImage` | upstream tag — texture/RenderTexture source |
| `progress` | filled bar | `value`, `max`, `track-color`, `fill-color`, `low-threshold`+`low-color`, `smooth` (tweened fill), `origin="left|right"` |
| `radial` | radial fill | same as `progress` plus `clockwise`, `origin="top|right|bottom|left"` — cooldown/cast bars |
| `switch` | `Toggle` + sliding knob | `value`, `disabled`, `on-color`, `off-color`, `knob-color`; `onChange(bool)` |
| `panel` | `view` alias | `role` → `data-motion-role`, `bg="screen"` → backdrop |
| `spacer` | sized `view` | `size="8"` / `8x16` / `flex` |
| `divider` | 1px separator | `orientation="v"`, `inset`, `color` |

### Layout & behavior attributes

| Attribute | Effect |
| --- | --- |
| `data-bg="screen"` | injects a fullscreen `<backdrop>` sibling behind the element; options: `data-bg-src`, `data-bg-dim`, `data-bg-color`, `data-bg-close` |
| `data-anchor="top-left|top|top-right|left|center|right|bottom-left|bottom|bottom-right"` | absolute positioning shorthand |
| `data-stretch` / `data-center` | fill-parent / centered shorthands |
| `data-platform="mobile|desktop|console"` | subtree is removed at parse time on non-matching platforms — never instantiated |
| `data-orientation="landscape|portrait"` | element activates/deactivates with device orientation, preserving state |
| `data-safe-area="all|top|bottom|left|right"` | notch/cutout padding in pixels, updates on rotation |
| `data-raycast="off"` | disables `raycastTarget` on the element's graphics |
| `data-alpha`, `data-interactable`, `data-blocks-raycasts` | CanvasGroup control without markup-visible wrapper |
| `data-shadow="x,y[,#color]"` / `data-outline="x,y[,#color]"` | uGUI Shadow/Outline on the element's graphics |
| `data-gradient="#top,#bottom"` (or 4 corners) | TMP vertex gradient on the element's texts |
| `data-slice` / `data-tiled` | `Image.type` Sliced/Tiled |
| `data-fill="linear|horizontal|vertical|radial|radial90|radial180"` | `Image.type` Filled — with `data-fill-amount`, `data-fill-origin`, `data-fill-clockwise` (cooldowns, bars) |
| `data-haptic="light|medium|heavy|selection"` | haptic pulse on click — no-op on platforms without haptics; backend swappable via `UnityHtmlHaptics.Provider` |

### Input

Mounting in play mode auto-creates an `EventSystem` with the best input
module — `InputSystemUIInputModule` when the Input System package is
installed (optional dependency, resolved by reflection), otherwise the
legacy `StandaloneInputModule`. While a host is mounted:

- `UnityHtmlInput.ActiveDevice` / `DeviceChanged` track keyboard+mouse,
  gamepad and touch usage.
- `UnityHtmlInput.BackRequested` fires on Escape, Android back and gamepad
  B — every mounted host forwards it to its own `BackRequested`, the same
  event `Globals.ui.Back()` raises.
- `Globals.haptics.Play("medium")` is available in markup scripts.
| `data-nav-up/down/left/right="#id"` | explicit `Selectable` navigation targets |
| `data-nav-wrap` | first/last selectable in the container cycle vertically |
| `data-first-selected` / `data-autofocus` | element takes EventSystem selection on mount (once — rerenders don't steal focus) |
| `data-return-focus` | container remembers the selection when it appears and restores it when removed — a dialog returns focus to its opener |

### Events

Pointer: `onClick`/`onPointerClick`, `onPointerEnter`/`onMouseEnter`, `onPointerExit`/`onMouseLeave`, `onPointerDown`/`onMouseDown`, `onPointerUp`/`onMouseUp`, `onPointerMove`, `onDoubleClick`, `onContextMenu`, `onScroll`, and the drag family (`onDrag`, `onBeginDrag`, `onEndDrag`, `onPotentialDrag`, `onDrop`). Focus and selection: `onSelect`/`onFocus`, `onDeselect`/`onBlur`, `onUpdateSelected`, `onMove`, `onSubmit`, `onCancel`, `onKeyDown`, `onResize`. Per-element value callbacks are listed in the table above.

### data-* attributes

| Attribute | Consumer | Meaning |
| --- | --- | --- |
| `data-key` | reconciler | stable row identity (alongside `id`) for reorder-safe updates |
| `data-tooltip` | tooltip layer | hover/focus/touch-hold tooltip text |
| `data-motion-role` | motion bridge | role preset (below) |
| `data-motion` | motion bridge | preset name, or `exit` / `none` |
| `data-motion-duration`, `-delay`, `-distance`, `-ease` | motion bridge | local overrides of the role tokens |

## Globals

Globals are an explicit allow-list of C# objects exposed to the script engine. Keep bridge objects narrow and route actions through existing application services. Do not expose gameplay stores or mutable domain services directly to HTML.

Two globals are built in: `Globals.motion` (motion bridge) and
`Globals.ui` — `ui.Back()` raises `IUnityHtmlHost.BackRequested`, which the
presenter maps to "close dialog / pop screen / exit".

## Motion

Runtime motion is declared in HTML and executed by DOTween after layout. Every animated element needs a stable `id`:

```html
<view id="inventory" data-motion="slide-left" data-motion-duration="0.16" data-motion-ease="out-cubic"></view>
```

Supported presets are `fade`, `fade-out`, `slide-left`, `slide-right`, `slide-up`, `slide-down`, `scale`, and `pulse`. Optional attributes are `data-motion-delay`, `data-motion-distance`, and `data-motion-ease`. Imperative feedback can call `Globals.motion.Play('inventory', 'pulse', 0.12, 0)` or `Globals.motion.Stop('inventory')`. Motion changes only opacity, anchored position, or scale and never triggers an HTML render.

### Motion roles

Prefer `data-motion-role` over hand-tuned attributes — it applies the shared policy so new UI animates consistently without copying values:

```html
<view id="side-panel" data-motion-role="panel"></view>
```

Roles: `panel`, `dialog`, `scrim`, `toast`, `edge-top`, `edge-bottom`, `none`. Each role owns an entry preset, an exit preset, and duration/distance/easing tokens (`UnityHtmlMotionPolicy`). Any `data-motion`, `data-motion-duration`, `data-motion-delay`, `data-motion-distance`, or `data-motion-ease` attribute overrides the role locally. `data-motion="exit"` plays the role's exit preset — closable surfaces stay mounted with that attribute until their close window elapses. `data-motion="none"` or `data-motion-role="none"` disables motion for the element.

`IUnityHtmlMotion.ExitFinished` fires exactly once per declarative exit — whether the tween completes, is cancelled by a replacement motion, or is torn down mid-exit — so a close flow can settle on the callback instead of guessing a fixed duration.

`IUnityHtmlMotion.ReducedMotion` is the global gate: declared motions snap to their final state — entries start at resting pose and exits fire `ExitFinished` immediately — so close flows keep their callbacks. Setting it mid-motion cancels in-flight tweens. Presenters push the persisted reduce-motion setting here on mount and on settings change.

## Touch and input

On Android, Unity's [Filter Touches When Obscured](https://docs.unity3d.com/6000.0/Documentation/Manual/class-PlayerSettingsAndroid.html) setting discards touches passing through another window before EventSystem receives them. A floating overlay can therefore leave the UI visible but unresponsive. Choose this application policy according to your game's sensitive flows; UnityHTML does not change Android security settings. Enabled actions alone do not prove that native touch events are arriving.

With Unity Input System installed and enabled, an optional `UnityHTML.InputSystem` assembly registers a typed backend before scene load. Mounting a document ensures a stock `InputSystemUIInputModule` has enabled point and press actions; incomplete references are recovered from `UI/Point` and `UI/Click`, or from Unity's standard multi-touch actions. Focus/pause resume repeats the same idempotent check. No reflection is needed for the Input System module in IL2CPP players.

Valid custom action maps are retained. Shared action asset definitions are never rewritten. Custom XR/multiplayer input modules are respected. Without the Input System package, legacy input continues to work when its backend is enabled. Device-family detection and Escape/gamepad-back use the installed backend; UI events still pass through the normal Unity EventSystem, so there is one click per gesture.

The optional assembly uses package version defines and `ENABLE_INPUT_SYSTEM`; installing UnityHTML does not install or force the Input System package. The assembly declares `AlwaysLinkAssembly` so runtime registration survives stripping even with no scene references. Run `UnityHtmlTouchInputTests` together with application tests that enqueue began/ended touch states and cross scene boundaries.

## Tooltips

`data-tooltip="..."` on any element gives it a tooltip through the shared layer: 0.3 s show delay, 0.12 s unscaled fade-in, screen-bounds clamping, and a hot window so retargeting an adjacent control skips the delay. The tooltip is reachable without a mouse — keyboard focus shows it, and a touch long-press shows it while a plain tap never flashes it. Element tooltips take precedence over world tooltips while hovered.

`IUnityHtmlHost.SetWorldTooltip(text, screenPosition)` feeds world objects (buildings, map targets) through the same layer; pass null or empty text to clear.

## Scrolling

`<scroll>` mounts `MoyvaSmoothScrollRect`, which completes any in-flight wheel animation to its target before applying the next delta — continuous wheel streams accumulate instead of collapsing to one step, and abrupt reversals move in the new direction immediately rather than drifting against the input.

`IUnityHtmlHost.ScrollSettings` (`UnityHtmlScrollSettings`) carries the per-host scroll configuration: `WheelSensitivity` (multiplier on the control's own sensitivity baseline), `Inertia`, `DecelerationRate`, `Smoothness` (seconds of easing per wheel step), and `ReducedMotion` (snaps to the final position — no easing, no inertia — and settles an in-flight animation). Settings apply to every mounted scroll control on mount, on regional updates, and when the property is reassigned; scroll position and velocity stay per control and are preserved only for controls that survive the update.

## Requirements

ReactUnity Core and QuickJS stay as commit-pinned UPM git dependencies. Unity must be launched from an environment where `git` is on `PATH`, otherwise Package Manager cannot resolve `com.reactunity.core` or `com.reactunity.quickjs`. Linux editor/standalone runs on Jint instead of QuickJS. Node, npm, and TypeScript are not required for runtime HTML/CSS assets.

## Releasing / updating

`main` is wired to CI that auto-tags releases: bump `"version"` in
`package.json`, push to `main`, and the `UPM release` workflow tags
`v<version>` automatically. Consumers pinned to a tag
(`...git#v0.2.0`) upgrade by changing the tag in `manifest.json`;
consumers on `...git` (HEAD) get the latest `main` on next resolve.
