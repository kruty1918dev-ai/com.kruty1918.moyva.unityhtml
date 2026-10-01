using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ReactUnity;
using ReactUnity.Helpers;
using ReactUnity.Scheduling;
using ReactUnity.Scripting;
using ReactUnity.Styling;
using ReactUnity.Styling.Rules;
using ReactUnity.UGUI;
using ReactUnity.UGUI.Behaviours;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityHTML.Runtime
{
    public sealed class UnityHtmlHost : IUnityHtmlHost
    {
        private UGUIContext _context;
        private RectTransform _root;
        private string _mountedCss = string.Empty;
        private readonly UnityHtmlMotionBridge _motion = new UnityHtmlMotionBridge();
        private UnityHtmlDocumentTree _tree;
        private UnityHtmlTooltipLayer _tooltips;
        private Vector2 _layoutSize;
        private readonly HashSet<ScrollRect> _seenScrollRects = new();
        private string _autofocusedElementId;
        private GameObject _selectionBeforeAutofocus;
        private UnityHtmlScrollSettings _scrollSettings = UnityHtmlScrollSettings.Default;
        private UnityHtmlEnvironmentWatcher _environmentWatcher;
        private readonly Dictionary<string, GameObject> _returnFocusSelections = new();
        private readonly UnityHtmlUiBridge _uiBridge;
        private readonly UnityHtmlHapticsBridge _hapticsBridge = new UnityHtmlHapticsBridge();

        public event Action BackRequested;

        public UnityHtmlHost()
        {
            _uiBridge = new UnityHtmlUiBridge(() => BackRequested?.Invoke());
        }

        public IUnityHtmlMotion Motion => _motion;

        public UnityHtmlScrollSettings ScrollSettings
        {
            get => _scrollSettings;
            set
            {
                if (_scrollSettings.Equals(value))
                    return;
                _scrollSettings = value;
                ApplyScrollSettings();
            }
        }

        public UnityHtmlMountResult Mount(
            RectTransform root,
            UnityHtmlDocument document,
            IReadOnlyDictionary<string, object> globals = null)
        {
            if (root == null)
                return UnityHtmlMountResult.Failure("Mount root is not assigned.");

            if (!document.HasHtml)
                return UnityHtmlMountResult.Failure($"HTML document '{document.SourceName}' is empty or missing.");

            if (CanUpdateMountedDocument(root, document))
            {
                UnityHtmlMountResult updated = UpdateMountedDocument(document, globals);
                if (updated.Succeeded)
                    return updated;
                // The reconcile path can die on stale TMP font/material references
                // still held by pooled components; retry once on a fresh context
                // instead of reporting a mount failure.
            }

            Unmount();
            ClearDetachedEditorElements();

            try
            {
                RegisterMoyvaComponents();
                if (Application.isPlaying)
                {
                    UnityHtmlInput.Ensure();
                    UnityHtmlInput.BackRequested += OnGlobalBackRequested;
                }
                _root = root;
                var guard = _root.gameObject.GetComponent<UnityHtmlReplacedElementGuard>();
                if (guard == null)
                    guard = _root.gameObject.AddComponent<UnityHtmlReplacedElementGuard>();
                _environmentWatcher = _root.gameObject.GetComponent<UnityHtmlEnvironmentWatcher>();
                if (_environmentWatcher == null)
                    _environmentWatcher = _root.gameObject.AddComponent<UnityHtmlEnvironmentWatcher>();
                _environmentWatcher.Bind(this);
#if UNITY_EDITOR
                // Edit-mode preview roots must not gain a serialized component.
                if (!Application.isPlaying)
                {
                    guard.hideFlags = HideFlags.DontSaveInEditor;
                    _environmentWatcher.hideFlags = HideFlags.DontSaveInEditor;
                }
#endif
                _mountedCss = document.Css ?? string.Empty;
                ClearRootChildren(_root);
                var globalRecord = CreateGlobals(globals);
                var source = ScriptSource.Text(string.Empty, ScriptSourceLanguage.Html);

                _context = new UGUIContext(new UGUIContext.Options
                {
                    HostElement = root,
                    Globals = globalRecord,
                    Source = source,
                    Timer = UnscaledTimer.Instance,
                    MediaProvider = DefaultMediaProvider.CreateMediaProvider(document.SourceName, "ugui", false),
                    EngineType = ResolveEngineType(),
                    Pooling = ReactContext.PoolingType.Basic,
                    UnknownPropertyHandling = ReactContext.UnknownPropertyHandling.Exception
                });
                _motion.Attach(_root);
                DetachUnsafeEditorAssemblyReloadDispose(_context);

                if (!string.IsNullOrWhiteSpace(document.Css))
                    _context.InsertStyle(document.Css);

                _context.Start();
                _tree = new UnityHtmlDocumentTree(_context);
                _tree.ComponentRemoved = _motion.HandleComponentRemoved;
                _tree.Update(document.Html);
                DetachUnsafeEditorAssemblyReloadDispose(_context);
                CompleteLayoutPass();
                _motion.ApplyDeclaredMotions();
                return UnityHtmlMountResult.Success();
            }
            catch (Exception exception)
            {
                Unmount();
                ClearDetachedEditorElements();
                var baseException = exception.GetBaseException();
                return UnityHtmlMountResult.Failure(
                    $"{document.SourceName}: {baseException.Message}\n{baseException.StackTrace}");
            }
        }

        public void Unmount()
        {
            UnityHtmlInput.BackRequested -= OnGlobalBackRequested;
            var context = _context;
            var root = _root;
            _context = null;
            _root = null;
            _mountedCss = string.Empty;
            _tree = null;
            _seenScrollRects.Clear();
            if (_tooltips != null)
            {
                _tooltips.enabled = false;
                if (!EditorDomainUnloadInProgress())
                {
                    if (ShouldDestroyDeferred()) UnityEngine.Object.Destroy(_tooltips);
                    else UnityEngine.Object.DestroyImmediate(_tooltips);
                }
                _tooltips = null;
            }
            _motion.Detach();
            _returnFocusSelections.Clear();
            if (_environmentWatcher != null)
            {
                if (!EditorDomainUnloadInProgress())
                {
                    if (ShouldDestroyDeferred()) UnityEngine.Object.Destroy(_environmentWatcher);
                    else UnityEngine.Object.DestroyImmediate(_environmentWatcher);
                }
                _environmentWatcher = null;
            }

            try
            {
                DisposeContext(context);
            }
            finally
            {
                if (!EditorDomainUnloadInProgress())
                {
                    ClearRootChildren(root);
                    RemoveMountLeftovers(root);
                }
            }
        }

        // HostComponent attaches ResponsiveElement (and on styled roots,
        // BorderAndBackground / MaskAndImage) to the mount root itself. Its
        // DestroySelf calls Object.Destroy, which is a silent no-op in edit
        // mode — the leftover keeps the entire UGUIContext graph, JS engine
        // included, rooted on the still-alive root object.
        private static void RemoveMountLeftovers(RectTransform root)
        {
            if (root == null)
                return;

            bool deferred = ShouldDestroyDeferred();
            foreach (var element in root.GetComponents<ReactUnity.UGUI.Behaviours.ResponsiveElement>())
            {
                if (element == null)
                    continue;
                // Sever the root→context edge first — a leftover holding
                // Context roots the whole disposed graph even while its
                // destruction waits for a safe moment.
                element.Context = null;
                element.Layout = null;
                DestroyLeftover(element, deferred);
            }

            foreach (var background in root.GetComponents<ReactUnity.UGUI.Internal.BorderAndBackground>())
            {
                if (background == null)
                    continue;
                BorderContextField?.SetValue(background, null);
                DestroyLeftover(background, deferred);
            }

            foreach (var mask in root.GetComponents<ReactUnity.UGUI.Internal.MaskAndImage>())
            {
                if (mask == null)
                    continue;
                MaskContextField?.SetValue(mask, null);
                DestroyLeftover(mask, deferred);
            }
        }

        private static readonly System.Reflection.FieldInfo BorderContextField =
            typeof(ReactUnity.UGUI.Internal.BorderAndBackground)
                .GetField("Context", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private static readonly System.Reflection.FieldInfo MaskContextField =
            typeof(ReactUnity.UGUI.Internal.MaskAndImage)
                .GetField("Context", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private static void DestroyLeftover(Component component, bool deferred)
        {
            if (component == null)
                return;
            if (deferred)
            {
                UnityEngine.Object.Destroy(component);
                return;
            }
#if UNITY_EDITOR
            if (!component.gameObject.scene.isLoaded)
                return; // scene teardown destroys the whole root anyway
            // Edit-mode unmount can run while the root is mid-deactivation or
            // mid-destruction, where DestroyImmediate on a component errors —
            // the references are already severed, so deferring the cleanup to
            // the next editor tick is safe.
            Component captured = component;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (captured != null)
                    UnityEngine.Object.DestroyImmediate(captured);
            };
#else
            UnityEngine.Object.DestroyImmediate(component);
#endif
        }

        private void OnGlobalBackRequested() => BackRequested?.Invoke();

        public void Dispose() => Unmount();
        public bool UpdateRegion(string elementId, string html)
            => UpdateRegions(new Dictionary<string, string> { [elementId] = html });

        public bool UpdateRegions(
            IReadOnlyDictionary<string, string> regions,
            IReadOnlyDictionary<string, object> globals = null)
        {
            if (_tree == null || regions == null) return false;
            try
            {
                UpdateGlobals(globals);
                if (!_tree.UpdateRegions(regions, out bool changed)) return false;
                if (changed || _layoutSize != _root.rect.size)
                {
                    CompleteLayoutPass();
                    _motion.ApplyDeclaredMotions();
                }
                return true;
            }
            catch (Exception)
            {
                // Reconcile/layout may touch a destroyed TMP resource (e.g. a font
                // asset reimported by the editor while mounted text references it).
                // Report failure so the caller can fall back to a full Mount
                // instead of surfacing a frame-loop exception.
                return false;
            }
        }
        public bool SetValue(string elementId, string value) => _tree?.SetValue(elementId, value) == true;

        public void SetWorldTooltip(string text, Vector2 screenPosition)
            => _tooltips?.SetWorldTooltip(text, screenPosition);

        private bool CanUpdateMountedDocument(RectTransform root, UnityHtmlDocument document)
        {
            return _context != null &&
                   !_context.IsDisposed &&
                   _root == root &&
                   string.Equals(_mountedCss, document.Css ?? string.Empty, StringComparison.Ordinal);
        }

        private UnityHtmlMountResult UpdateMountedDocument(
            UnityHtmlDocument document,
            IReadOnlyDictionary<string, object> globals)
        {
            try
            {
                UpdateGlobals(globals);
                if (_tree.Update(document.Html) || _layoutSize != _root.rect.size)
                {
                    CompleteLayoutPass();
                    _motion.ApplyDeclaredMotions();
                }
                return UnityHtmlMountResult.Success();
            }
            catch (Exception exception)
            {
                Unmount();
                ClearDetachedEditorElements();
                var baseException = exception.GetBaseException();
                return UnityHtmlMountResult.Failure(
                    $"{document.SourceName}: {baseException.Message}\n{baseException.StackTrace}");
            }
        }

        private void UpdateGlobals(IReadOnlyDictionary<string, object> globals)
        {
            if (_context?.Globals == null)
                return;

            if (globals != null)
            {
                foreach (var pair in globals)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                        continue;

                    if (!_context.Globals.TryGetValue(pair.Key, out object previous)
                        || !Equals(previous, pair.Value))
                        _context.Globals[pair.Key] = pair.Value;
                }
            }

            if (!_context.Globals.TryGetValue("motion", out object motion) || !ReferenceEquals(motion, _motion))
                _context.Globals["motion"] = _motion;
            if (!_context.Globals.TryGetValue("ui", out object ui) || !ReferenceEquals(ui, _uiBridge))
                _context.Globals["ui"] = _uiBridge;
            if (!_context.Globals.TryGetValue("haptics", out object haptics) || !ReferenceEquals(haptics, _hapticsBridge))
                _context.Globals["haptics"] = _hapticsBridge;
        }

        private void CompleteLayoutPass()
        {
            var scrollPositions = CaptureRenderedScrollPositions(_root);
            if (Application.isPlaying && _tooltips == null)
            {
                _tooltips = _root.gameObject.AddComponent<UnityHtmlTooltipLayer>();
                _tooltips.Bind(_context, _root);
            }
            if (_tooltips != null)
                _tooltips.ReducedMotion = _motion.ReducedMotion;
            _context.UpdateElementsRecursively();
            _layoutSize = _root.rect.size;
            _context.Host.Layout.Width = _layoutSize.x;
            _context.Host.Layout.Height = _layoutSize.y;
            _context.CalculateLayoutRecursively();
            _context.LateUpdateElementsRecursively();
            FlushReactElementLayout(_root);
            // FlushReactElementLayout consumes HasNewLayout on every element, so the
            // ScrollContentResizer LateUpdate gate never fires — resize scroll content
            // here instead of waiting for a flag that is already cleared.
            var resizers = _root.GetComponentsInChildren<ScrollContentResizer>(true);
            for (var i = 0; i < resizers.Length; i++)
            {
                if (resizers[i] != null && resizers[i].Layout != null)
                    resizers[i].RecalculateSize();
            }
            ConfigureRenderedInputs(_root);
            Canvas.ForceUpdateCanvases();
            ApplyScrollSettings();
            RestoreRenderedScrollPositions(scrollPositions);
            InitializeNewScrollPositions();
            ApplyAutofocus();
            ApplyReturnFocus();
            UnityHtmlElementEffects.ApplyNavigation(_root);
            // Safe-area padding writes into Yoga after sizes are computed —
            // one extra pass applies them without another full layout cycle.
            if (UnityHtmlElementEffects.Apply(_context, _root))
            {
                _context.CalculateLayoutRecursively();
                FlushReactElementLayout(_root);
            }
            _tooltips?.RefreshTargets();
#if UNITY_EDITOR
            MarkEditorPreviewObjectsDontSave(_root);
#endif
        }

#if UNITY_EDITOR
        // Edit-mode preview mounts build a live DOM under the host root. Those
        // generated objects must never serialize into the scene — saving with
        // them present leaks the whole preview tree into the .unity file.
        private static void MarkEditorPreviewObjectsDontSave(RectTransform root)
        {
            if (Application.isPlaying || root == null)
                return;
            var children = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child == null || child == root)
                    continue;
                child.gameObject.hideFlags |= HideFlags.DontSaveInEditor;
            }
        }
#endif

        private static List<RenderedScrollPosition> CaptureRenderedScrollPositions(RectTransform root)
        {
            // ScrollRect caches its content/bounds in Awake/OnEnable, which never
            // run for edit-mode mounts; normalizedPosition throws there.
            if (root == null || !Application.isPlaying)
                return null;

            var scrollRects = root.GetComponentsInChildren<ScrollRect>(true);
            if (scrollRects == null || scrollRects.Length == 0)
                return null;

            var result = new List<RenderedScrollPosition>(scrollRects.Length);
            for (var i = 0; i < scrollRects.Length; i++)
            {
                var scrollRect = scrollRects[i];
                if (scrollRect == null)
                    continue;

                result.Add(new RenderedScrollPosition(
                    scrollRect,
                    scrollRect.normalizedPosition,
                    scrollRect.velocity));
            }

            return result;
        }

        private static void RestoreRenderedScrollPositions(List<RenderedScrollPosition> positions)
        {
            if (positions == null)
                return;

            for (var i = 0; i < positions.Count; i++)
                positions[i].Restore();
        }

        // Push the host's scroll configuration into every mounted scroll control.
        // Settings are applied per control instance, so a regional update that
        // remounts some scrolls leaves the surviving ones configured identically.
        private void ApplyScrollSettings()
        {
            if (_root == null)
                return;
            var scrollRects = _root.GetComponentsInChildren<ScrollRect>(true);
            for (var i = 0; i < scrollRects.Length; i++)
            {
                var scrollRect = scrollRects[i];
                if (scrollRect == null)
                    continue;

                if (scrollRect is MoyvaSmoothScrollRect smooth)
                    smooth.ApplySettings(_scrollSettings);
                else
                {
                    var binding = scrollRect.GetComponent<MoyvaScrollSensitivity>();
                    if (binding == null)
                        binding = scrollRect.gameObject.AddComponent<MoyvaScrollSensitivity>();
                    binding.Apply(_scrollSettings.WheelSensitivity);
                }
            }
        }

        // ScrollRect defaults normalizedPosition to (0,0) — the bottom for vertical
        // content — and nothing on the mount path initializes it, so a fresh scroll
        // container would open scrolled to the end. Positions captured at pass start
        // are restored above for scrolls that already existed; scrolls seen for the
        // first time (new mount or added by a refresh) start at the top.
        private void InitializeNewScrollPositions()
        {
            if (_root == null)
                return;

            _seenScrollRects.RemoveWhere(static scrollRect => scrollRect == null);
            var scrollRects = _root.GetComponentsInChildren<ScrollRect>(true);
            for (var i = 0; i < scrollRects.Length; i++)
            {
                var scrollRect = scrollRects[i];
                if (scrollRect != null && scrollRect.vertical && scrollRect.content != null
                    && _seenScrollRects.Add(scrollRect))
                    scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        // Re-runs environment-conditional effects after rotation/resize —
        // called by UnityHtmlEnvironmentWatcher; no document rebuild happens.
        internal void RefreshEnvironment()
        {
            if (_root == null || _context == null)
                return;
            UnityHtmlElementEffects.ApplyNavigation(_root);
            if (UnityHtmlElementEffects.Apply(_context, _root))
            {
                _context.CalculateLayoutRecursively();
                FlushReactElementLayout(_root);
                Canvas.ForceUpdateCanvases();
            }
        }

        // data-return-focus marks a container (typically a modal/dialog) whose
        // disappearance restores the selection held when it appeared — the
        // opener keeps focus without markup doing the bookkeeping.
        private void ApplyReturnFocus()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                _returnFocusSelections.Clear();
                return;
            }

            var seen = new HashSet<string>();
            var elements = _root.GetComponentsInChildren<ReactElement>(true);
            for (var i = 0; i < elements.Length; i++)
            {
                UGUIComponent component = elements[i] != null ? elements[i].Component : null;
                if (component?.Id == null
                    || !component.Data.TryGetValue("return-focus", out object flag)
                    || string.Equals(flag?.ToString(), "false", StringComparison.OrdinalIgnoreCase))
                    continue;

                seen.Add(component.Id);
                if (!_returnFocusSelections.ContainsKey(component.Id))
                    _returnFocusSelections[component.Id] = eventSystem.currentSelectedGameObject;
            }

            var gone = new List<string>();
            foreach (var pair in _returnFocusSelections)
                if (!seen.Contains(pair.Key))
                    gone.Add(pair.Key);
            foreach (string id in gone)
            {
                GameObject restore = _returnFocusSelections[id];
                _returnFocusSelections.Remove(id);
                if (restore != null && restore.activeInHierarchy)
                    eventSystem.SetSelectedGameObject(restore);
            }
        }

        // Focus the first element carrying data-autofocus when it is newly
        // mounted; when that element unmounts, selection returns to whatever
        // had it before. Focus is handed over only once per mount so a rerender
        // never steals it back while the user is typing.
        private void ApplyAutofocus()
        {
            // EventSystem.current may resolve to a destroyed instance left in
            // the static registry (editor/test scenes); fall back to a live one.
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
                return;

            Selectable target = null;
            string targetId = null;
            var elements = _root.GetComponentsInChildren<ReactElement>(true);
            for (var i = 0; i < elements.Length; i++)
            {
                UGUIComponent component = elements[i] != null ? elements[i].Component : null;
                if (component == null
                    || !IsFocusFlagSet(component, "autofocus") && !IsFocusFlagSet(component, "first-selected"))
                    continue;

                target = elements[i].GetComponent<Selectable>();
                if (target == null)
                    target = elements[i].GetComponentInChildren<Selectable>(true);
                if (target == null)
                    continue;
                targetId = component.Id;
                break;
            }

            if (target == null)
            {
                if (_autofocusedElementId != null)
                {
                    _autofocusedElementId = null;
                    if (_selectionBeforeAutofocus != null)
                        eventSystem.SetSelectedGameObject(_selectionBeforeAutofocus);
                    _selectionBeforeAutofocus = null;
                }
                return;
            }

            if (string.Equals(targetId, _autofocusedElementId, StringComparison.Ordinal))
                return;

            _autofocusedElementId = targetId;
            _selectionBeforeAutofocus = eventSystem.currentSelectedGameObject;
            if (target.IsActive() && target.interactable)
                target.Select();
        }

        private static bool IsFocusFlagSet(UGUIComponent component, string key)
        {
            return component.Data.TryGetValue(key, out object flag) &&
                   !string.Equals(flag?.ToString(), "false", StringComparison.OrdinalIgnoreCase);
        }

        private readonly struct RenderedScrollPosition
        {
            private readonly ScrollRect _scrollRect;
            private readonly Vector2 _normalizedPosition;
            private readonly Vector2 _velocity;

            public RenderedScrollPosition(
                ScrollRect scrollRect,
                Vector2 normalizedPosition,
                Vector2 velocity)
            {
                _scrollRect = scrollRect;
                _normalizedPosition = normalizedPosition;
                _velocity = velocity;
            }

            public void Restore()
            {
                if (_scrollRect == null)
                    return;

                _scrollRect.normalizedPosition = _normalizedPosition;
                _scrollRect.velocity = _velocity;
            }
        }

        private GlobalRecord CreateGlobals(IReadOnlyDictionary<string, object> globals)
        {
            var record = new GlobalRecord();
            if (globals != null)
            {
                foreach (var pair in globals)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                        continue;

                    record[pair.Key] = pair.Value;
                }
            }

            record["motion"] = _motion;
            record["ui"] = _uiBridge;
            record["haptics"] = _hapticsBridge;
            return record;
        }

        private static void ClearRootChildren(RectTransform root)
        {
            if (root == null)
                return;

            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null)
                    continue;

                var childObject = child.gameObject;
                if (ShouldDestroyDeferred())
                    UnityEngine.Object.Destroy(childObject);
                else
                    UnityEngine.Object.DestroyImmediate(childObject);
            }
        }

        private static bool ShouldDestroyDeferred()
        {
#if UNITY_EDITOR
            return Application.isPlaying && UnityEditor.EditorApplication.isPlaying;
#else
            return Application.isPlaying;
#endif
        }

#if UNITY_EDITOR
        // uGUI tracks live Selectables in statics (s_Selectables/s_SelectableCount)
        // that reset on every domain load. When a domain reload tears down the
        // [ExecuteAlways] preview anchors, OnDisable -> Unmount -> DestroyImmediate
        // cascades Selectable.OnDisable into that already-drained registry
        // (IndexOutOfRangeException). Skipping explicit destruction is safe:
        // play-mode unloads drop these DontSave objects, and after plain
        // compilation reloads the remount's cleanup destroys survivors whose
        // non-serialized m_EnableCalled reset makes their OnDisable a no-op.
        // Subscribed in the static constructor: it runs before any Unmount in
        // this domain can run (RuntimeInitializeOnLoadMethod does not fire on
        // plain script-compilation reloads).
        private static bool s_editorDomainUnloadInProgress;

        static UnityHtmlHost()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeEditorAssemblyReload;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeEditorAssemblyReload;
        }

        private static void OnBeforeEditorAssemblyReload()
            => s_editorDomainUnloadInProgress = true;

        private static bool EditorDomainUnloadInProgress()
            => s_editorDomainUnloadInProgress && !Application.isPlaying;
#else
        private static bool EditorDomainUnloadInProgress() => false;
#endif

        private static void ClearDetachedEditorElements()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                return;

            var elements = UnityEngine.Object.FindObjectsByType<ReactElement>(
                FindObjectsInactive.Include);
            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                if (element == null || element.transform.parent != null)
                    continue;

                UnityEngine.Object.DestroyImmediate(element.gameObject);
            }
#endif
        }

        private static JavascriptEngineType ResolveEngineType()
        {
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
#pragma warning disable CS0612
            return JavascriptEngineType.Jint;
#pragma warning restore CS0612
#else
            return JavascriptEngineType.QuickJS;
#endif
        }

        private static void FlushReactElementLayout(RectTransform root)
        {
            if (root == null)
                return;

            var elements = root.GetComponentsInChildren<ReactElement>(true);
            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                var layout = element != null ? element.Layout : null;
                var transform = element != null ? element.transform as RectTransform : null;
                if (layout == null || !layout.HasNewLayout || !element.enabled || transform == null || float.IsNaN(layout.LayoutWidth))
                    continue;

                var pivotDiff = transform.pivot - Vector2.up;
                transform.anchoredPosition = new Vector2(
                    layout.LayoutLeft + pivotDiff.x * layout.LayoutWidth,
                    -layout.LayoutTop + pivotDiff.y * layout.LayoutHeight);
                transform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, layout.LayoutWidth);
                transform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, layout.LayoutHeight);
                layout.MarkLayoutSeen();
            }
        }

        private static void RegisterMoyvaComponents()
        {
            UGUIContext.ComponentCreators["input"] = (_, text, context) => new UnityHtmlInputComponent(text, context);
            if (!UGUIContext.ComponentCreators.ContainsKey("slider"))
                UGUIContext.ComponentCreators["slider"] = (_, _, context) => new UnityHtmlSliderComponent(context);

            if (!UGUIContext.ComponentCreators.ContainsKey("select"))
                UGUIContext.ComponentCreators["select"] = (_, _, context) => new UnityHtmlSelectComponent(context);

            // Upstream ships ScrollbarComponent as a pseudo-element ("_scrollbar")
            // for input/scroll internals — register it as a usable tag.
            if (!UGUIContext.ComponentCreators.ContainsKey("scrollbar"))
                UGUIContext.ComponentCreators["scrollbar"] =
                    (_, _, context) => new ScrollbarComponent(context, "scrollbar");

            if (!UGUIContext.ComponentCreators.ContainsKey("mask"))
                UGUIContext.ComponentCreators["mask"] = (_, _, context) => new UnityHtmlMaskComponent(context);

            if (!UGUIContext.ComponentCreators.ContainsKey("rectmask"))
                UGUIContext.ComponentCreators["rectmask"] = (_, _, context) => new UnityHtmlRectMaskComponent(context);

            if (!UGUIContext.ComponentCreators.ContainsKey("progress"))
                UGUIContext.ComponentCreators["progress"] = (_, _, context) => new UnityHtmlProgressComponent(context, false);

            if (!UGUIContext.ComponentCreators.ContainsKey("radial"))
                UGUIContext.ComponentCreators["radial"] = (_, _, context) => new UnityHtmlProgressComponent(context, true);

            if (!UGUIContext.ComponentCreators.ContainsKey("switch"))
                UGUIContext.ComponentCreators["switch"] = (_, _, context) => new UnityHtmlSwitchComponent(context);

            PatchScrollComponentCreator();
        }

        // Wraps the scroll component creator: installs the accumulation-fixed
        // MoyvaSmoothScrollRect, and in editor fixes ScrollContentResizer's
        // RectTransform cache (it is filled in OnEnable, which Unity never invokes
        // for components created outside play mode — the script has no
        // ExecuteAlways).
        private static void PatchScrollComponentCreator()
        {
            if (!UGUIContext.ComponentCreators.TryGetValue("scroll", out var creator))
                return;
            if (creator.Method?.DeclaringType == typeof(UnityHtmlHost))
                return;

            UGUIContext.ComponentCreators["scroll"] = (tag, text, context) =>
            {
                var component = creator(tag, text, context);
                ReplaceScrollRect(component);
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    InitializeEditorScrollResizers(component);
#endif
                return component;
            };
        }

        // Swaps the upstream SmoothScrollRect for the accumulation-fixed
        // MoyvaSmoothScrollRect. Upstream computes each scroll event's target from
        // the mid-animation position, so a continuous wheel/touchpad stream
        // collapses to roughly a single step.
        private static void ReplaceScrollRect(IReactComponent component)
        {
            if (component is not ScrollComponent scroll || scroll.ScrollRect == null)
                return;

            var oldRect = scroll.ScrollRect;
            var go = oldRect.gameObject;

            var content = oldRect.content;
            var viewport = oldRect.viewport;
            var horizontal = oldRect.horizontal;
            var vertical = oldRect.vertical;
            var scrollSensitivity = oldRect.scrollSensitivity;
            var movementType = oldRect.movementType;
            var elasticity = oldRect.elasticity;
            var inertia = oldRect.inertia;
            var decelerationRate = oldRect.decelerationRate;
            var horizontalScrollbarVisibility = oldRect.horizontalScrollbarVisibility;
            var verticalScrollbarVisibility = oldRect.verticalScrollbarVisibility;
            var smoothness = oldRect.Smoothness;
            var wheelDirectionTransposed = oldRect.WheelDirectionTransposed;
            var horizontalScrollbar = oldRect.horizontalScrollbar;
            var verticalScrollbar = oldRect.verticalScrollbar;

            // ScrollRect is DisallowMultipleComponent — detach its scrollbar
            // listeners first (so the dead instance does not stay subscribed to
            // onValueChanged), then remove it before adding the replacement.
            oldRect.horizontalScrollbar = null;
            oldRect.verticalScrollbar = null;
            UnityEngine.Object.DestroyImmediate(oldRect);

            var fresh = go.AddComponent<MoyvaSmoothScrollRect>();
            fresh.content = content;
            fresh.viewport = viewport;
            fresh.horizontal = horizontal;
            fresh.vertical = vertical;
            fresh.scrollSensitivity = scrollSensitivity;
            fresh.movementType = movementType;
            fresh.elasticity = elasticity;
            fresh.inertia = inertia;
            fresh.decelerationRate = decelerationRate;
            fresh.horizontalScrollbarVisibility = horizontalScrollbarVisibility;
            fresh.verticalScrollbarVisibility = verticalScrollbarVisibility;
            fresh.Smoothness = smoothness;
            fresh.WheelDirectionTransposed = wheelDirectionTransposed;
            fresh.horizontalScrollbar = horizontalScrollbar;
            fresh.verticalScrollbar = verticalScrollbar;

            typeof(ScrollComponent)
                .GetField("<ScrollRect>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(scroll, fresh);
        }

#if UNITY_EDITOR
        private static void InitializeEditorScrollResizers(IReactComponent component)
        {
            if (component is not UGUIComponent ugui || ugui.GameObject == null)
                return;

            var field = typeof(ScrollContentResizer)
                .GetField("rt", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                return;

            foreach (var resizer in ugui.GameObject
                .GetComponentsInChildren<ScrollContentResizer>(true))
            {
                if (resizer != null && field.GetValue(resizer) == null)
                    field.SetValue(resizer, resizer.GetComponent<RectTransform>());
            }
        }
#endif

        private static void ConfigureRenderedInputs(RectTransform root)
        {
            if (root == null)
                return;

            var inputs = root.GetComponentsInChildren<TMP_InputField>(true);
            for (var i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                if (input == null)
                    continue;
                var component = input.GetComponent<ReactElement>()?.Component as UnityHtmlInputComponent;
                bool configureStyle = component == null || !component.NativeLayoutConfigured;
                Vector2 size = ((RectTransform)input.transform).rect.size;
                if (configureStyle || component.NativeLayoutSize != size)
                {
                    ConfigureInputViewport(input);
                    ConfigureInputText(input);
                    input.ForceLabelUpdate();
                    if (component != null)
                    {
                        component.NativeLayoutSize = size;
                        component.NativeLayoutConfigured = true;
                    }
                }

                if (!configureStyle)
                    continue;

                input.customCaretColor = true;
                input.caretColor = new Color(0.98f, 0.91f, 0.56f, 1f);
                input.selectionColor = new Color(0.86f, 0.68f, 0.23f, 0.35f);
                input.lineType = TMP_InputField.LineType.SingleLine;
                input.caretWidth = 2;
                input.resetOnDeActivation = false;

                var bubbling = input.GetComponent<ScrollEventBubbling>();
                if (bubbling != null)
                    bubbling.Bubble = false;

                var graphic = input.targetGraphic != null ? input.targetGraphic : input.GetComponent<Graphic>();
                if (graphic == null)
                {
                    var image = input.gameObject.AddComponent<Image>();
                    image.color = new Color(0f, 0f, 0f, 0.001f);
                    graphic = image;
                }

                graphic.raycastTarget = true;
                input.targetGraphic = graphic;
            }
        }

        private static void ConfigureInputText(TMP_InputField input)
        {
            if (input == null)
                return;

            if (input.textComponent != null)
            {
                input.textComponent.color = new Color(0.98f, 0.96f, 0.89f, 1f);
                input.textComponent.alignment = TextAlignmentOptions.Center;
                input.textComponent.textWrappingMode = TextWrappingModes.NoWrap;
                input.textComponent.overflowMode = TextOverflowModes.Masking;
                input.textComponent.margin = Vector4.zero;
                input.textComponent.raycastTarget = false;
                input.textComponent.ForceMeshUpdate(true);
            }

            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.color = new Color(0.72f, 0.70f, 0.64f, 0.78f);
                placeholder.alignment = TextAlignmentOptions.Center;
                placeholder.textWrappingMode = TextWrappingModes.NoWrap;
                placeholder.margin = Vector4.zero;
                placeholder.raycastTarget = false;
                placeholder.ForceMeshUpdate(true);
            }
        }

        private static void ConfigureInputViewport(TMP_InputField input)
        {
            var viewport = input.textViewport;
            if (viewport != null)
            {
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.pivot = new Vector2(0.5f, 0.5f);
                viewport.offsetMin = new Vector2(10f, 0f);
                viewport.offsetMax = new Vector2(-10f, 0f);
                viewport.localScale = Vector3.one;
            }

            StretchTextRect(input.textComponent != null ? input.textComponent.rectTransform : null);
            if (input.placeholder is TMP_Text placeholder)
                StretchTextRect(placeholder.rectTransform);
        }

        private static void StretchTextRect(RectTransform rect)
        {
            if (rect == null)
                return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void DisposeContext(UGUIContext context)
        {
            if (context == null)
                return;

            // Every mounted/pooled YogaNode holds a rooted GCHandle that would
            // otherwise keep this whole context graph (JS engine included) alive
            // forever — release before disposal.
            UnityHtmlDocumentTree.ReleaseContextNodes(context);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DetachUnsafeEditorAssemblyReloadDispose(context);

                try
                {
                    context.Dispatcher?.Dispose();
                }
                catch
                {
                }

                try
                {
                    context.Globals?.Dispose();
                }
                catch
                {
                }

                try
                {
                    foreach (var disposable in context.Disposables)
                        disposable?.Invoke();
                    context.Disposables.Clear();
                }
                catch
                {
                }

                try
                {
                    context.Script?.Dispose();
                }
                catch
                {
                }

                return;
            }
#endif

            context.Dispose();
        }

        private static void DetachUnsafeEditorAssemblyReloadDispose(UGUIContext context)
        {
#if UNITY_EDITOR
            if (context != null && !Application.isPlaying)
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= context.Dispose;
#endif
        }
    }

    internal sealed class UnityHtmlSliderComponent : UGUIComponent, IActivatableComponent
    {
        private readonly Image _backgroundImage;
        private readonly Image _fillImage;
        private readonly Image _handleImage;
        private readonly TMP_Text _valueText;
        private readonly SliderInteractionGuard _interactionGuard;
        private string _format = "decimal1";
        private string _suffix = string.Empty;

        public UnityHtmlSliderComponent(UGUIContext context) : base(context, "slider")
        {
            var raycastGraphic = AddComponent<Image>();
            raycastGraphic.color = new Color(0f, 0f, 0f, 0.001f);
            raycastGraphic.raycastTarget = true;

            Slider = AddComponent<Slider>();
            Slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            Slider.transition = Selectable.Transition.ColorTint;
            Slider.navigation = new Navigation { mode = Navigation.Mode.None };

            CreateTrack(context, out _backgroundImage, out var fillRect, out _fillImage, out var handleRect, out _handleImage, out _valueText);
            Slider.fillRect = fillRect;
            Slider.handleRect = handleRect;
            Slider.targetGraphic = _handleImage;
            Slider.minValue = 0f;
            Slider.maxValue = 1f;
            Slider.value = 0f;

            _interactionGuard = AddComponent<SliderInteractionGuard>();
            _interactionGuard.Initialize(Slider);
            Slider.onValueChanged.AddListener(UpdateValueText);
            UpdateValueText(Slider.value);
        }

        public Slider Slider { get; }

        public bool Disabled
        {
            get => !Slider.interactable;
            set => Slider.interactable = !value;
        }

        public void Activate()
        {
            Slider.Select();
        }

        public override Action AddEventListener(string eventName, Callback callback)
        {
            switch (eventName)
            {
                case "onChange":
                case "onValueChanged":
                    var listener = new UnityEngine.Events.UnityAction<float>(value => callback.CallWithPriority(EventPriority.Continuous, value, this));
                    Slider.onValueChanged.AddListener(listener);
                    return () => Slider.onValueChanged.RemoveListener(listener);
                case "onBeginChange":
                    Action<float> beginListener = value => callback.CallWithPriority(EventPriority.Discrete, value, this);
                    _interactionGuard.BeginChange += beginListener;
                    return () => _interactionGuard.BeginChange -= beginListener;
                case "onEndChange":
                    Action<float> endListener = value => callback.CallWithPriority(EventPriority.Discrete, value, this);
                    _interactionGuard.EndChange += endListener;
                    return () => _interactionGuard.EndChange -= endListener;
                default:
                    return base.AddEventListener(eventName, callback);
            }
        }

        public override void SetProperty(string propertyName, object value)
        {
            switch (propertyName)
            {
                case "value":
                    Slider.SetValueWithoutNotify(ToSingle(value, Slider.value));
                    UpdateValueText(Slider.value);
                    return;
                case "min":
                case "minValue":
                    Slider.minValue = ToSingle(value, Slider.minValue);
                    UpdateValueText(Slider.value);
                    return;
                case "max":
                case "maxValue":
                    Slider.maxValue = ToSingle(value, Slider.maxValue);
                    UpdateValueText(Slider.value);
                    return;
                case "wholeNumbers":
                    Slider.wholeNumbers = Convert.ToBoolean(value);
                    UpdateValueText(Slider.value);
                    return;
                case "format":
                    _format = value?.ToString()?.Trim().ToLowerInvariant() ?? "decimal1";
                    UpdateValueText(Slider.value);
                    return;
                case "suffix":
                    _suffix = value?.ToString() ?? string.Empty;
                    UpdateValueText(Slider.value);
                    return;
                case "disabled":
                    Disabled = Convert.ToBoolean(value);
                    return;
                default:
                    base.SetProperty(propertyName, value);
                    return;
            }
        }

        protected override void ApplyStylesSelf()
        {
            base.ApplyStylesSelf();
            _backgroundImage.color = new Color(0.10f, 0.11f, 0.16f, 1f);
            _fillImage.color = new Color(0.86f, 0.68f, 0.23f, 1f);
            _handleImage.color = Disabled
                ? new Color(0.46f, 0.43f, 0.36f, 1f)
                : new Color(0.96f, 0.83f, 0.38f, 1f);
            _valueText.color = Disabled
                ? new Color(0.52f, 0.50f, 0.45f, 1f)
                : new Color(0.94f, 0.92f, 0.86f, 1f);
        }

        private void UpdateValueText(float value)
        {
            string formatted;
            switch (_format)
            {
                case "percent":
                    formatted = $"{Mathf.RoundToInt(value * 100f)}%";
                    break;
                case "integer":
                    formatted = Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
                    break;
                case "decimal2":
                    formatted = value.ToString("0.00", CultureInfo.InvariantCulture);
                    break;
                default:
                    formatted = value.ToString("0.0", CultureInfo.InvariantCulture);
                    break;
            }

            _valueText.text = formatted + _suffix;
        }

        private static float ToSingle(object value, float fallback)
        {
            if (value == null)
                return fallback;

            if (value is string stringValue)
            {
                if (float.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantValue))
                    return invariantValue;

                if (float.TryParse(stringValue, NumberStyles.Float, CultureInfo.CurrentCulture, out var currentValue))
                    return currentValue;

                return fallback;
            }

            try
            {
                return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private void CreateTrack(
            UGUIContext context,
            out Image backgroundImage,
            out RectTransform fillRect,
            out Image fillImage,
            out RectTransform handleRect,
            out Image handleImage,
            out TMP_Text valueText)
        {
            var background = CreateChild("Background", RectTransform, typeof(Image));
            backgroundImage = background.GetComponent<Image>();
            backgroundImage.color = new Color(0.10f, 0.11f, 0.16f, 1f);
            backgroundImage.raycastTarget = false;
            StretchMiddle(background.GetComponent<RectTransform>(), 10f, 72f, 8f);

            var fillArea = CreateChild("Fill Area", RectTransform);
            var fillAreaRect = fillArea.GetComponent<RectTransform>();
            Stretch(fillAreaRect, 10f, 72f);

            var fill = CreateChild("Fill", fillAreaRect, typeof(Image));
            fillImage = fill.GetComponent<Image>();
            fillImage.raycastTarget = false;
            fillRect = fill.GetComponent<RectTransform>();
            StretchMiddle(fillRect, 0f, 0f, 8f);

            var handleArea = CreateChild("Handle Slide Area", RectTransform);
            var handleAreaRect = handleArea.GetComponent<RectTransform>();
            Stretch(handleAreaRect, 10f, 72f);

            var handle = CreateChild("Handle", handleAreaRect, typeof(Image));
            handleImage = handle.GetComponent<Image>();
            handleImage.raycastTarget = true;
            handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(20f, 20f);
            handleRect.anchoredPosition = Vector2.zero;

            var valueObject = CreateChild("Value", RectTransform, typeof(TextMeshProUGUI));
            valueText = valueObject.GetComponent<TextMeshProUGUI>();
            valueText.font = ResolveFont(context);
            valueText.fontSize = 13f;
            valueText.fontStyle = FontStyles.Bold;
            valueText.alignment = TextAlignmentOptions.MidlineRight;
            valueText.textWrappingMode = TextWrappingModes.NoWrap;
            valueText.overflowMode = TextOverflowModes.Truncate;
            valueText.raycastTarget = false;
            var valueRect = valueObject.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = Vector2.one;
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(58f, 0f);
            valueRect.anchoredPosition = new Vector2(-2f, 0f);
        }

        private static TMP_FontAsset ResolveFont(UGUIContext context)
        {
            if (context?.Globals != null &&
                context.Globals.TryGetValue("moyvaFont", out var font) &&
                font is TMP_FontAsset fontAsset)
            {
                return fontAsset;
            }

            return TMP_Settings.defaultFontAsset;
        }

        private static GameObject CreateChild(string name, Transform parent, params Type[] components)
        {
            var types = new Type[components.Length + 1];
            types[0] = typeof(RectTransform);
            Array.Copy(components, 0, types, 1, components.Length);
            var child = new GameObject(name, types);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void Stretch(RectTransform rect, float left, float right)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.up;
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
        }

        private static void StretchMiddle(RectTransform rect, float left, float right, float height)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, -height * 0.5f);
            rect.offsetMax = new Vector2(-right, height * 0.5f);
        }
    }

    internal sealed class SliderInteractionGuard : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IEndDragHandler, ICancelHandler, IScrollHandler
    {
        private readonly List<ScrollState> _scrollStates = new List<ScrollState>(2);
        private Slider _slider;
        private bool _active;

        public event Action<float> BeginChange;
        public event Action<float> EndChange;

        public void Initialize(Slider slider) => _slider = slider;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_active || _slider == null || !_slider.IsInteractable())
                return;

            _active = true;
            LockParentScrolling();
            BeginChange?.Invoke(_slider.value);
        }

        public void OnPointerUp(PointerEventData eventData) => CompleteInteraction();

        public void OnEndDrag(PointerEventData eventData) => CompleteInteraction();

        public void OnCancel(BaseEventData eventData) => CompleteInteraction();

        public void OnScroll(PointerEventData eventData)
        {
            if (_active)
                eventData.Use();
        }

        private void OnDisable() => CompleteInteraction();

        private void LockParentScrolling()
        {
            _scrollStates.Clear();
            var scrollRects = GetComponentsInParent<ScrollRect>(true);
            for (var i = 0; i < scrollRects.Length; i++)
            {
                var scrollRect = scrollRects[i];
                if (scrollRect == null)
                    continue;

                _scrollStates.Add(new ScrollState(scrollRect, scrollRect.horizontal, scrollRect.vertical));
                scrollRect.horizontal = false;
                scrollRect.vertical = false;
            }
        }

        private void CompleteInteraction()
        {
            if (!_active)
                return;

            _active = false;
            for (var i = 0; i < _scrollStates.Count; i++)
                _scrollStates[i].Restore();
            _scrollStates.Clear();
            EndChange?.Invoke(_slider != null ? _slider.value : 0f);
        }

        private readonly struct ScrollState
        {
            private readonly ScrollRect _scrollRect;
            private readonly bool _horizontal;
            private readonly bool _vertical;

            public ScrollState(ScrollRect scrollRect, bool horizontal, bool vertical)
            {
                _scrollRect = scrollRect;
                _horizontal = horizontal;
                _vertical = vertical;
            }

            public void Restore()
            {
                if (_scrollRect == null)
                    return;

                _scrollRect.horizontal = _horizontal;
                _scrollRect.vertical = _vertical;
            }
        }
    }

    internal sealed class UnityHtmlSelectComponent : UGUIComponent, IActivatableComponent
    {
        private readonly Image _backgroundImage;
        private readonly Image _itemBackgroundImage;
        private readonly Image _checkmarkImage;
        private readonly TMP_Text _captionText;
        private readonly TMP_Text _itemText;
        private readonly RectTransform _templateRect;
        private readonly RectTransform _itemRect;
        private int _requestedValue;
        private float _itemHeight = 34f;
        private List<TMP_Dropdown.OptionData> _allOptions;
        private List<int> _indexMap;

        public UnityHtmlSelectComponent(UGUIContext context) : base(context, "select")
        {
            _backgroundImage = AddComponent<Image>();
            _backgroundImage.color = new Color(0.035f, 0.045f, 0.07f, 1f);
            _backgroundImage.raycastTarget = true;

            Dropdown = AddComponent<TMP_Dropdown>();
            Dropdown.targetGraphic = _backgroundImage;
            Dropdown.transition = Selectable.Transition.ColorTint;
            Dropdown.navigation = new Navigation { mode = Navigation.Mode.None };
            Dropdown.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1f, 0.93f, 0.72f, 1f),
                pressedColor = new Color(0.90f, 0.76f, 0.42f, 1f),
                selectedColor = new Color(1f, 0.93f, 0.72f, 1f),
                disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.65f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };

            BuildHierarchy(context, out _captionText, out _itemText, out _itemBackgroundImage,
                out _checkmarkImage, out _templateRect, out _itemRect);
            Dropdown.template = _templateRect;
            Dropdown.captionText = _captionText;
            Dropdown.itemText = _itemText;
            Dropdown.options.Add(new TMP_Dropdown.OptionData("Option"));
            Dropdown.SetValueWithoutNotify(0);
            Dropdown.RefreshShownValue();
            Dropdown.onValueChanged.AddListener(value => _requestedValue = OriginalIndex(value));
            // Lets the cloned search field reach back to this component.
            GameObject.AddComponent<UnityHtmlSelectHandle>().Owner = this;

            var bubbling = AddComponent<ScrollEventBubbling>();
            bubbling.Bubble = false;
        }

        public TMP_Dropdown Dropdown { get; }

        public bool Disabled
        {
            get => !Dropdown.interactable;
            set => Dropdown.interactable = !value;
        }

        public void Activate()
        {
            Dropdown.Select();
            Dropdown.Show();
        }

        public override Action AddEventListener(string eventName, Callback callback)
        {
            switch (eventName)
            {
                case "onChange":
                case "onValueChanged":
                    var listener = new UnityEngine.Events.UnityAction<int>(value =>
                        callback.CallWithPriority(EventPriority.Discrete, OriginalIndex(value), this));
                    Dropdown.onValueChanged.AddListener(listener);
                    return () => Dropdown.onValueChanged.RemoveListener(listener);
                default:
                    return base.AddEventListener(eventName, callback);
            }
        }

        public override void SetProperty(string propertyName, object value)
        {
            switch (propertyName)
            {
                case "options":
                    SetOptions(value?.ToString());
                    return;
                case "option-icons":
                    SetOptionIcons(value?.ToString());
                    return;
                case "value":
                    _requestedValue = ToInt(value, _requestedValue);
                    ApplyRequestedValue();
                    return;
                case "disabled":
                    Disabled = Convert.ToBoolean(value);
                    return;
                case "max-height":
                    var maxHeight = ToFloat(value, _templateRect.sizeDelta.y);
                    _templateRect.sizeDelta = new Vector2(_templateRect.sizeDelta.x, maxHeight);
                    return;
                case "item-height":
                    _itemHeight = ToFloat(value, _itemHeight);
                    _itemRect.sizeDelta = new Vector2(_itemRect.sizeDelta.x, _itemHeight);
                    return;
                case "searchable":
                    if (Convert.ToBoolean(value))
                        EnsureSearchField();
                    return;
                default:
                    base.SetProperty(propertyName, value);
                    return;
            }
        }

        protected override void ApplyStylesSelf()
        {
            base.ApplyStylesSelf();
            _backgroundImage.color = new Color(0.035f, 0.045f, 0.07f, 1f);
            _captionText.color = Disabled
                ? new Color(0.52f, 0.51f, 0.48f, 1f)
                : new Color(0.97f, 0.95f, 0.89f, 1f);
            _itemText.color = new Color(0.97f, 0.95f, 0.89f, 1f);
            _itemBackgroundImage.color = new Color(0.075f, 0.08f, 0.12f, 1f);
            _checkmarkImage.color = new Color(0.88f, 0.71f, 0.27f, 1f);
        }

        private void SetOptions(string value)
        {
            _indexMap = null;
            var labels = string.IsNullOrWhiteSpace(value)
                ? new[] { "Option" }
                : value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            _allOptions = new List<TMP_Dropdown.OptionData>(labels.Length);
            for (var i = 0; i < labels.Length; i++)
                _allOptions.Add(new TMP_Dropdown.OptionData(labels[i].Trim()));
            Dropdown.ClearOptions();
            Dropdown.AddOptions(_allOptions);
            ApplyRequestedValue();
        }

        // option-icons="res1,res2" — sprite per option index, resolved through
        // the context media provider.
        private void SetOptionIcons(string value)
        {
            if (_allOptions == null || string.IsNullOrWhiteSpace(value))
                return;
            var icons = value.Split('|', ',');
            for (var i = 0; i < icons.Length && i < _allOptions.Count; i++)
            {
                string icon = icons[i].Trim();
                if (string.IsNullOrEmpty(icon))
                    continue;
                var option = _allOptions[i];
                if (ReactUnity.Styling.Converters.AllConverters.SpriteSourceConverter
                        .TryGetConstantValue<ReactUnity.Types.SpriteReference>(icon, out var spriteRef))
                    spriteRef.Get(Context, sprite =>
                    {
                        option.image = sprite;
                        Dropdown.RefreshShownValue();
                    });
            }
        }

        // searchable — add a filter input above the dropdown viewport. The
        // template is cloned per open, so the field is built once here; the
        // cloned copy resolves its owner through the serialized Dropdown ref.
        private void EnsureSearchField()
        {
            if (_templateRect.Find("Search") != null)
                return;
            var search = CreateChild("Search", _templateRect,
                typeof(Image), typeof(TMP_InputField), typeof(UnityHtmlSelectSearch));
            var searchRect = search.GetComponent<RectTransform>();
            searchRect.anchorMin = Vector2.up;
            searchRect.anchorMax = Vector2.one;
            searchRect.pivot = new Vector2(0.5f, 1f);
            searchRect.sizeDelta = new Vector2(0f, 30f);
            searchRect.anchoredPosition = Vector2.zero;
            search.GetComponent<Image>().color = new Color(0.05f, 0.055f, 0.085f, 1f);
            var input = search.GetComponent<TMP_InputField>();
            var inputText = CreateChild("Text", searchRect, typeof(TMPro.TextMeshProUGUI))
                .GetComponent<TMPro.TextMeshProUGUI>();
            ConfigureText(inputText, Context, 13f, TextAlignmentOptions.MidlineLeft);
            Stretch(inputText.GetComponent<RectTransform>(), 10f, 10f, 2f, 2f);
            input.textComponent = inputText;
            input.pointSize = 13f;
            var hook = search.GetComponent<UnityHtmlSelectSearch>();
            hook.Dropdown = Dropdown;

            // Viewport slides below the search row.
            var viewport = _templateRect.Find("Viewport") as RectTransform;
            if (viewport != null)
            {
                viewport.offsetMax = new Vector2(viewport.offsetMax.x, -34f);
            }
        }

        internal int OriginalIndex(int filtered)
            => _indexMap != null && filtered >= 0 && filtered < _indexMap.Count
                ? _indexMap[filtered]
                : filtered;

        internal void ApplyFilter(string query)
        {
            if (_allOptions == null)
                return;
            _indexMap = new List<int>();
            var filtered = new List<TMP_Dropdown.OptionData>();
            for (var i = 0; i < _allOptions.Count; i++)
            {
                if (string.IsNullOrEmpty(query) ||
                    _allOptions[i].text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filtered.Add(_allOptions[i]);
                    _indexMap.Add(i);
                }
            }
            Dropdown.ClearOptions();
            Dropdown.AddOptions(filtered);
            Dropdown.SetValueWithoutNotify(_indexMap.IndexOf(_requestedValue));
        }

        internal void RestoreOptions()
        {
            if (_indexMap == null)
                return;
            _indexMap = null;
            Dropdown.ClearOptions();
            Dropdown.AddOptions(_allOptions);
            ApplyRequestedValue();
        }

        private void ApplyRequestedValue()
        {
            // _requestedValue is always an ORIGINAL index; the dropdown shows
            // the filtered position while a search filter is active.
            int shown = _indexMap != null ? _indexMap.IndexOf(_requestedValue) : _requestedValue;
            var maximum = Mathf.Max(0, Dropdown.options.Count - 1);
            Dropdown.SetValueWithoutNotify(Mathf.Clamp(shown < 0 ? 0 : shown, 0, maximum));
            Dropdown.RefreshShownValue();
        }

        private void BuildHierarchy(
            UGUIContext context,
            out TMP_Text captionText,
            out TMP_Text itemText,
            out Image itemBackgroundImage,
            out Image checkmarkImage,
            out RectTransform templateRect,
            out RectTransform itemRectOut)
        {
            var label = CreateChild("Label", RectTransform, typeof(TextMeshProUGUI));
            captionText = label.GetComponent<TextMeshProUGUI>();
            ConfigureText(captionText, context, 14f, TextAlignmentOptions.MidlineLeft);
            Stretch(label.GetComponent<RectTransform>(), 12f, 38f, 5f, 5f);

            var arrow = CreateChild("Arrow", RectTransform, typeof(TextMeshProUGUI));
            var arrowText = arrow.GetComponent<TextMeshProUGUI>();
            ConfigureText(arrowText, context, 14f, TextAlignmentOptions.Center);
            arrowText.text = "v";
            arrowText.color = new Color(0.88f, 0.71f, 0.27f, 1f);
            var arrowRect = arrow.GetComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1f, 0.5f);
            arrowRect.anchorMax = new Vector2(1f, 0.5f);
            arrowRect.pivot = new Vector2(0.5f, 0.5f);
            arrowRect.sizeDelta = new Vector2(28f, 28f);
            arrowRect.anchoredPosition = new Vector2(-18f, 0f);

            var template = CreateChild("Template", RectTransform, typeof(Image), typeof(ScrollRect));
            templateRect = template.GetComponent<RectTransform>();
            templateRect.anchorMin = Vector2.zero;
            templateRect.anchorMax = Vector2.right;
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -3f);
            templateRect.sizeDelta = new Vector2(0f, 176f);
            var templateImage = template.GetComponent<Image>();
            templateImage.color = new Color(0.035f, 0.04f, 0.065f, 1f);
            templateImage.raycastTarget = true;

            var viewport = CreateChild("Viewport", templateRect, typeof(Image), typeof(RectMask2D));
            var viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect, 2f, 2f, 2f, 2f);
            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.001f);
            viewportImage.raycastTarget = true;

            var content = CreateChild("Content", viewportRect);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.up;
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 36f);

            var item = CreateChild("Item", contentRect, typeof(Toggle));
            var itemRect = item.GetComponent<RectTransform>();
            itemRectOut = itemRect;
            itemRect.anchorMin = new Vector2(0f, 0.5f);
            itemRect.anchorMax = new Vector2(1f, 0.5f);
            itemRect.pivot = new Vector2(0.5f, 0.5f);
            itemRect.sizeDelta = new Vector2(0f, _itemHeight);

            var itemIcon = CreateChild("Item Icon", itemRect, typeof(Image));
            var itemIconImage = itemIcon.GetComponent<Image>();
            itemIconImage.raycastTarget = false;
            itemIconImage.preserveAspect = true;
            var iconRect = itemIcon.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(20f, 20f);
            iconRect.anchoredPosition = new Vector2(18f, 0f);

            var itemBackground = CreateChild("Item Background", itemRect, typeof(Image));
            itemBackgroundImage = itemBackground.GetComponent<Image>();
            itemBackgroundImage.color = new Color(0.075f, 0.08f, 0.12f, 1f);
            itemBackgroundImage.raycastTarget = true;
            Stretch(itemBackground.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            var checkmark = CreateChild("Item Checkmark", itemRect, typeof(Image));
            checkmarkImage = checkmark.GetComponent<Image>();
            checkmarkImage.color = new Color(0.88f, 0.71f, 0.27f, 1f);
            checkmarkImage.raycastTarget = false;
            var checkmarkRect = checkmark.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0f, 0.5f);
            checkmarkRect.anchorMax = new Vector2(0f, 0.5f);
            checkmarkRect.pivot = new Vector2(0.5f, 0.5f);
            checkmarkRect.sizeDelta = new Vector2(4f, 18f);
            checkmarkRect.anchoredPosition = new Vector2(10f, 0f);

            var itemLabel = CreateChild("Item Label", itemRect, typeof(TextMeshProUGUI));
            itemText = itemLabel.GetComponent<TextMeshProUGUI>();
            ConfigureText(itemText, context, 13f, TextAlignmentOptions.MidlineLeft);
            Stretch(itemLabel.GetComponent<RectTransform>(), 22f, 10f, 2f, 2f);

            var itemToggle = item.GetComponent<Toggle>();
            itemToggle.targetGraphic = itemBackgroundImage;
            itemToggle.graphic = checkmarkImage;
            itemToggle.transition = Selectable.Transition.ColorTint;
            itemToggle.isOn = true;

            // Dropdown.itemImage points at the template item's image slot —
            // TMP_Dropdown swaps in each option's sprite on build.
            Dropdown.itemImage = itemIconImage;

            var scrollRect = template.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 28f;
            template.SetActive(false);
        }

        private static void ConfigureText(TMP_Text text, UGUIContext context, float size, TextAlignmentOptions alignment)
        {
            if (context?.Globals != null &&
                context.Globals.TryGetValue("moyvaFont", out var font) &&
                font is TMP_FontAsset fontAsset)
            {
                text.font = fontAsset;
            }
            else
            {
                text.font = TMP_Settings.defaultFontAsset;
            }

            text.fontSize = size;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
        }

        private static float ToFloat(object value, float fallback)
        {
            if (value == null)
                return fallback;
            if (float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return fallback;
        }

        private static int ToInt(object value, int fallback)
        {
            if (value == null)
                return fallback;
            if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static GameObject CreateChild(string name, Transform parent, params Type[] components)
        {
            var types = new Type[components.Length + 1];
            types[0] = typeof(RectTransform);
            Array.Copy(components, 0, types, 1, components.Length);
            var child = new GameObject(name, types);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }

    // ReactReplacedElement.LateUpdate dereferences Layout before the vendor
    // component's Start can disable it (Layout == null), and keeps throwing
    // every frame once the assigned YogaNode is disposed by DOM pooling while
    // the GameObject survives. Runs before the vendor component (-20) and
    // disables elements whose layout access would throw.
    [ExecuteAlways]
    [DefaultExecutionOrder(-30)]
    [AddComponentMenu("")]
    internal sealed class UnityHtmlReplacedElementGuard : MonoBehaviour
    {
        private readonly List<ReactReplacedElement> _elements = new List<ReactReplacedElement>();

        private void LateUpdate()
        {
            _elements.Clear();
            GetComponentsInChildren(false, _elements);
            for (var i = 0; i < _elements.Count; i++)
            {
                var element = _elements[i];
                if (element == null || !element.enabled)
                    continue;
                try
                {
                    var layout = element.Layout;
                    if (layout == null || element.Measurer == null)
                        element.enabled = false;
                    else
                        _ = layout.HasNewLayout;
                }
                catch (Exception)
                {
                    element.enabled = false;
                }
            }
        }
    }
}
