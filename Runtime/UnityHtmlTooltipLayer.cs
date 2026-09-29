using System.Collections.Generic;
using ReactUnity.UGUI;
using ReactUnity.UGUI.Behaviours;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityHTML.Runtime
{
    [DefaultExecutionOrder(32000)]
    internal sealed class UnityHtmlTooltipLayer : MonoBehaviour
    {
        internal const float DefaultShowDelaySeconds = 0.3f;
        internal const float DefaultFadeSeconds = 0.12f;
        // While a tooltip is hot (was just visible), retargeting to an adjacent
        // control or world object skips the delay so the same tooltip retargets
        // instead of flickering off and on.
        private const float HotWindowSeconds = 0.75f;
        private const int MaxTooltipChars = 240;
        // Cursor clearance for screen-anchored (world) tooltips: the panel must
        // sit clear of the pointer's hotspot, never underneath it.
        private const float CursorGapPixels = 20f;

        private readonly List<ReactElement> _elements = new();
        private readonly Vector3[] _corners = new Vector3[4];
        private RectTransform _root;
        private UGUIComponent _panel;
        private TextComponent _label;
        private CanvasGroup _group;
        private UnityHtmlTooltipTarget _owner;
        private float _showAt;
        private float _hotUntil = float.NegativeInfinity;
        private float _alpha;
        private string _text;
        private Vector2 _size;
        private float _width;
        private TMPro.TMP_FontAsset _font;
        private bool _measureFailed;
        private string _worldText;
        private Vector2 _worldScreenPosition;

        // World-object tooltips feed the same layer instead of a parallel system:
        // SetWorldTooltip supplies text + the pointer's screen position each frame.
        internal string WorldText => _worldText;
        internal float ShowDelaySeconds = DefaultShowDelaySeconds;
        internal float FadeSeconds = DefaultFadeSeconds;
        internal bool ReducedMotion;

        private bool IsHot => Time.unscaledTime < _hotUntil;
        private float CurrentDelay => IsHot ? 0f : ShowDelaySeconds;

        public void Bind(UGUIContext context, RectTransform root)
        {
            _root = root;
            _measureFailed = false;
            _panel = (UGUIComponent)context.CreateComponent("view", string.Empty);
            _panel.Id = "unityhtml-tooltip-layer";
            _panel.Style["position"] = "absolute";
            _panel.Style["backgroundColor"] = "#15171b";
            _panel.Style["borderColor"] = "#b99854";
            _panel.Style["borderWidth"] = 1;
            _panel.Style["pointerEvents"] = "none";
            _panel.SetParent(context.Host);
            _panel.Component.enabled = false;
            var canvas = _panel.RectTransform.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32000;
            _group = _panel.RectTransform.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _label = (TextComponent)context.CreateText("text", string.Empty);
            _label.SetParent((ReactUnity.IContainerComponent)_panel);
            if (context.Globals.TryGetValue("moyvaFont", out object font) && font is TMPro.TMP_FontAsset asset)
                _label.Text.font = asset;
            _label.Style["fontSize"] = 13;
            _label.Style["color"] = "#f6f3ea";
            _label.Style["textAlign"] = "left";
            _label.Component.enabled = false;
            _label.Text.richText = false;
            _panel.RectTransform.gameObject.SetActive(false);
        }

        public void RefreshTargets()
        {
            _root.GetComponentsInChildren(true, _elements);
            foreach (ReactElement element in _elements)
            {
                UGUIComponent component = element.Component;
                if (component?.Data == null || !component.Data.TryGetValue("tooltip", out object value)
                    || string.IsNullOrWhiteSpace(value as string)) continue;
                var target = element.GetComponent<UnityHtmlTooltipTarget>()
                    ?? element.gameObject.AddComponent<UnityHtmlTooltipTarget>();
                target.Layer = this;
                target.Source = component;
            }
            _panel.RectTransform.SetAsLastSibling();
            foreach (Graphic graphic in _panel.RectTransform.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }

        public void Show(UnityHtmlTooltipTarget owner)
        {
            _owner = owner;
            _showAt = Time.unscaledTime + CurrentDelay;
            // Only hide between targets while cold; when hot the panel stays up
            // and retargets, so sweeping across adjacent controls never flickers.
            if (!IsHot)
                SetPanelActive(false);
        }

        public void Hide(UnityHtmlTooltipTarget owner)
        {
            if (_owner != owner) return;
            _owner = null;
            MarkHotIfVisible();
            SetPanelActive(false);
        }

        // text == null/empty clears the world target; the pointer's screen
        // position anchors the panel (converted to root space at layout time).
        public void SetWorldTooltip(string text, Vector2 screenPosition)
        {
            text = ClampText(text);
            if (_worldText != text)
            {
                _worldText = text;
                // Only arm the delay when the world target is (or will be) the
                // shown one — a visible element tooltip keeps its timing.
                if (text != null && _owner == null)
                {
                    _showAt = Time.unscaledTime + CurrentDelay;
                    if (!IsHot) SetPanelActive(false);
                }
            }
            _worldScreenPosition = screenPosition;
        }

        private void MarkHotIfVisible()
        {
            if (_panel != null && _panel.RectTransform != null
                && _panel.RectTransform.gameObject.activeSelf && _alpha > 0.5f)
                _hotUntil = Time.unscaledTime + HotWindowSeconds;
        }

        private void SetPanelActive(bool active)
        {
            if (_panel?.RectTransform == null)
                return;
            if (!active)
            {
                _alpha = 0f;
                if (_group != null) _group.alpha = 0f;
            }
            _panel.RectTransform.gameObject.SetActive(active);
        }

        private static string ClampText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            return text.Length > MaxTooltipChars
                ? text.Substring(0, MaxTooltipChars - 1).TrimEnd() + "…"
                : text;
        }

        private void LateUpdate()
        {
            // Destroyed-reference guard: during a context remount or a font asset
            // reimport the pooled/destroyed components below may still be wired here
            // for a frame or two. Bail out instead of touching dead Unity objects.
            if (_measureFailed || _root == null ||
                _panel == null || _panel.Destroyed || _panel.RectTransform == null ||
                _label == null || _label.Destroyed || _label.Text == null)
            { Hide(_owner); return; }

            // Element target (hover/focus on a data-tooltip control) wins over a
            // world target; the world adapter clears its text while the pointer is
            // over UI anyway, so the precedence is just a safety net.
            string text = null;
            RectTransform anchorRect = null;
            bool screenAnchor = false;
            if (_owner != null)
            {
                bool alive = _owner.isActiveAndEnabled
                    && _owner.Source != null && !_owner.Source.Destroyed
                    && _owner.Source.RectTransform != null;
                text = alive ? ClampText(_owner.Tooltip) : null;
                if (text != null)
                    anchorRect = _owner.Source.RectTransform;
                else
                    Hide(_owner);
            }
            if (text == null && _worldText != null)
            {
                text = _worldText;
                screenAnchor = true;
            }
            if (text == null)
            {
                MarkHotIfVisible();
                SetPanelActive(false);
                return;
            }
            if (Time.unscaledTime < _showAt) return;

            _label.Text.enabled = true;
            SetPanelActive(true);
            _panel.RectTransform.SetAsLastSibling();
            Rect bounds = _root.rect;
            float maxWidth = Mathf.Max(40, Mathf.Min(300, bounds.width - 16));
            if (_text != text || _width != maxWidth || _font != _label.Text.font)
            {
                try
                {
                    _text = text;
                    _width = maxWidth;
                    _font = _label.Text.font;
                    _label.SetText(text);
                    Vector2 preferred = _label.Text.GetPreferredValues(text, maxWidth - 20, Mathf.Infinity);
                    _size = new Vector2(Mathf.Min(maxWidth, preferred.x + 20), preferred.y + 16);
                }
                catch (System.Exception)
                {
                    // A TMP font asset in the fallback chain may have been destroyed
                    // mid-frame (e.g. editor reimport of a persistent dynamic font
                    // asset while its atlas grows). MissingReferenceException inside
                    // TMP_MaterialManager would otherwise repeat every LateUpdate.
                    _measureFailed = true;
                    Hide(_owner);
                    return;
                }
            }
            Vector2 size = _size;

            Vector2 anchorPoint;
            float anchorHeight;
            if (screenAnchor)
            {
                Canvas canvas = _root.GetComponentInParent<Canvas>();
                Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera : null;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _root, _worldScreenPosition, uiCamera, out Vector2 local);
                anchorPoint = local;
                anchorHeight = CursorGapPixels;
            }
            else
            {
                anchorRect.GetWorldCorners(_corners);
                Vector3 bottom = _root.InverseTransformPoint(_corners[0]);
                Vector3 top = _root.InverseTransformPoint(_corners[1]);
                anchorPoint = new Vector2(bottom.x, bottom.y);
                anchorHeight = top.y - bottom.y;
            }

            float gap = screenAnchor ? CursorGapPixels : 8f;
            float y = anchorPoint.y - gap;
            if (y - size.y < bounds.yMin + 8)
                y = anchorPoint.y + anchorHeight + size.y + gap;

            var rect = _panel.RectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
            rect.pivot = Vector2.up;
            rect.sizeDelta = size;
            float x = anchorPoint.x + (screenAnchor ? CursorGapPixels : 0f);
            rect.anchoredPosition = new Vector2(
                Mathf.Clamp(x, bounds.xMin + 8, bounds.xMax - size.x - 8),
                Mathf.Min(y, bounds.yMax - 8)) - bounds.center;
            _label.RectTransform.anchorMin = Vector2.zero;
            _label.RectTransform.anchorMax = Vector2.one;
            _label.RectTransform.offsetMin = new Vector2(10, 8);
            _label.RectTransform.offsetMax = new Vector2(-10, -8);

            // Smooth appearance: alpha eases in over FadeSeconds of unscaled time;
            // reduced motion snaps straight to fully visible.
            float targetAlpha = 1f;
            _alpha = ReducedMotion || FadeSeconds <= 0f
                ? targetAlpha
                : Mathf.MoveTowards(_alpha, targetAlpha, Time.unscaledDeltaTime / FadeSeconds);
            if (_group != null) _group.alpha = _alpha;
        }
    }

    internal sealed class UnityHtmlTooltipTarget : MonoBehaviour, IPointerEnterHandler,
        IPointerExitHandler, ISelectHandler, IDeselectHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        // P078: the same data-tooltip is reachable without a mouse — keyboard
        // focus (OnSelect) and a sustained touch hold both show it. A plain tap
        // never flashes the tooltip; completing the hold clears the press's
        // click eligibility so a long-press explains instead of activating.
        internal const float DefaultTouchHoldSeconds = 0.5f;
        internal float TouchHoldSeconds = DefaultTouchHoldSeconds;

        internal UnityHtmlTooltipLayer Layer;
        internal UGUIComponent Source;
        internal string Tooltip => Source?.Data != null && Source.Data.TryGetValue("tooltip", out object text)
            ? text as string : null;

        private float _touchHoldUntil = float.NegativeInfinity;
        private PointerEventData _touchEvent;

        // uGUI convention: mouse buttons carry negative pointer ids while
        // touches report their fingerId (>= 0) — that is the only reliable
        // mouse-vs-touch split on PointerEventData.
        private static bool IsTouch(PointerEventData data) => data != null && data.pointerId >= 0;

        public void OnPointerEnter(PointerEventData data)
        {
            if (IsTouch(data)) return;
            Layer?.Show(this);
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (!IsTouch(data)) return;
            _touchEvent = data;
            _touchHoldUntil = Time.unscaledTime + TouchHoldSeconds;
        }

        public void OnPointerUp(PointerEventData data)
        {
            _touchHoldUntil = float.NegativeInfinity;
            _touchEvent = null;
            if (IsTouch(data)) Layer?.Hide(this);
        }

        private void Update()
        {
            if (_touchHoldUntil == float.NegativeInfinity
                || Time.unscaledTime < _touchHoldUntil)
                return;
            _touchHoldUntil = float.NegativeInfinity;
            // A completed hold suppresses the tap's click so the same gesture
            // explains the control instead of toggling it.
            if (_touchEvent != null)
                _touchEvent.eligibleForClick = false;
            _touchEvent = null;
            Layer?.Show(this);
        }

        public void OnPointerExit(PointerEventData data)
        {
            _touchHoldUntil = float.NegativeInfinity;
            _touchEvent = null;
            Layer?.Hide(this);
        }

        public void OnSelect(BaseEventData data) => Layer?.Show(this);
        public void OnDeselect(BaseEventData data) => Layer?.Hide(this);

        private void OnDisable()
        {
            _touchHoldUntil = float.NegativeInfinity;
            _touchEvent = null;
            Layer?.Hide(this);
        }
    }
}
