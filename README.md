# Moyva UnityHTML

UnityHTML is Moyva's thin runtime wrapper around ReactUnity UGUI. It mounts HTML and CSS stored as Unity `TextAsset` files into an existing `RectTransform`.

## Install (Unity Package Manager)

Package Manager → **+** → **Add package from git URL**:

```
https://github.com/kruty1918dev-ai/com.kruty1918.moyva.unityhtml.git
```

or in `Packages/manifest.json`:

```json
"com.kruty1918.moyva.unityhtml": "https://github.com/kruty1918dev-ai/com.kruty1918.moyva.unityhtml.git#v0.1.0"
```

The repository is private — Git credentials (PAT / Git Credential Manager)
are required on every machine that resolves the package.

Dependencies: `com.reactunity.core`, `com.reactunity.jint`,
`com.reactunity.quickjs` (install those first from the ReactUnity repo),
`com.unity.ugui`.

CSS presentation assets should be authored as plain CSS text with a Unity text extension, for example `HomeMenuShell.css.txt`. This keeps the file readable while ensuring Unity imports it as a populated `TextAsset`.

The script engine is selected per platform: QuickJS everywhere except Linux (editor and standalone), which uses Jint. Keep `on*` callbacks to short expressions that behave identically on both engines — call into a C# bridge object rather than writing logic in markup.

## Runtime Lifecycle

Create a `UnityHtmlDocument` from HTML and optional CSS text, then mount it through `IUnityHtmlHost`. The host creates a ReactUnity `UGUIContext`, injects a `GlobalRecord`, selects the engine, inserts CSS before `Start()`, and calculates layout.

```csharp
var document = UnityHtmlDocument.FromTextAssets(htmlAsset, cssAsset, "HomeMenuShell");
var result = host.Mount(root, document, new Dictionary<string, object>
{
    ["moyvaMenu"] = bridge
});
```

`Mount` replaces any live document. `Unmount()` (also called by `Dispose()`, mount failure and scene unload through the owning presenter) tears it down completely:

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
| `select` | `TMP_Dropdown` | `options="A&#124;B&#124;C"` (pipe-separated), `value` (index), `disabled`; `onChange(int)` |
| `scroll` | `MoyvaSmoothScrollRect` | wheel accumulation fixed; configured per host via `IUnityHtmlHost.ScrollSettings` |
| `image`/`img` | `Image` | `source` resolves through the media provider |
| `icon` | icon font text | |
| `a`/`anchor` | link | |

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

## Tooltips

`data-tooltip="..."` on any element gives it a tooltip through the shared layer: 0.3 s show delay, 0.12 s unscaled fade-in, screen-bounds clamping, and a hot window so retargeting an adjacent control skips the delay. The tooltip is reachable without a mouse — keyboard focus shows it, and a touch long-press shows it while a plain tap never flashes it. Element tooltips take precedence over world tooltips while hovered.

`IUnityHtmlHost.SetWorldTooltip(text, screenPosition)` feeds world objects (buildings, map targets) through the same layer; pass null or empty text to clear.

## Scrolling

`<scroll>` mounts `MoyvaSmoothScrollRect`, which completes any in-flight wheel animation to its target before applying the next delta — continuous wheel streams accumulate instead of collapsing to one step, and abrupt reversals move in the new direction immediately rather than drifting against the input.

`IUnityHtmlHost.ScrollSettings` (`UnityHtmlScrollSettings`) carries the per-host scroll configuration: `WheelSensitivity` (multiplier on the control's own sensitivity baseline), `Inertia`, `DecelerationRate`, `Smoothness` (seconds of easing per wheel step), and `ReducedMotion` (snaps to the final position — no easing, no inertia — and settles an in-flight animation). Settings apply to every mounted scroll control on mount, on regional updates, and when the property is reassigned; scroll position and velocity stay per control and are preserved only for controls that survive the update.

## Requirements

ReactUnity Core and QuickJS stay as commit-pinned UPM git dependencies. Unity must be launched from an environment where `git` is on `PATH`, otherwise Package Manager cannot resolve `com.reactunity.core` or `com.reactunity.quickjs`. Linux editor/standalone runs on Jint instead of QuickJS. Node, npm, and TypeScript are not required for runtime HTML/CSS assets.
