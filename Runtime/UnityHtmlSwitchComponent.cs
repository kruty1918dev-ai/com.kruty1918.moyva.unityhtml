using System;
using DG.Tweening;
using ReactUnity;
using ReactUnity.Helpers;
using ReactUnity.Scripting;
using ReactUnity.UGUI;
using UnityEngine;
using UnityEngine.UI;

namespace UnityHTML.Runtime
{
    /// <summary>A native pill switch. Model updates are silent; user activation emits once.</summary>
    internal sealed class UnityHtmlSwitchComponent : UGUIComponent, IToggleComponent
    {
        readonly Toggle _toggle;
        readonly UnityHtmlSwitchVisual _visual;
        public bool Checked => _toggle.isOn;
        public bool Indeterminate => false;
        public bool Disabled => !_toggle.interactable;

        public UnityHtmlSwitchComponent(UGUIContext context) : base(context, "switch")
        {
            var track = AddComponent<Image>();
            track.sprite = UnityHtmlSwitchVisual.Circle;
            track.type = Image.Type.Sliced;
            _toggle = AddComponent<UnityHtmlSwitchToggle>();
            _toggle.targetGraphic = track;
            _toggle.graphic = null;
            _toggle.transition = Selectable.Transition.None;
            var go = new GameObject("Knob", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(GameObject.transform, false);
            var knob = go.GetComponent<RectTransform>();
            knob.anchorMin = knob.anchorMax = new Vector2(0, .5f);
            knob.pivot = new Vector2(0, .5f);
            var image = go.GetComponent<Image>();
            image.sprite = UnityHtmlSwitchVisual.Circle;
            image.raycastTarget = false;
            _visual = AddComponent<UnityHtmlSwitchVisual>();
            _visual.Initialize(_toggle, track, knob, image);
            _toggle.onValueChanged.AddListener(v =>
            {
                MarkForStyleResolvingWithSiblings(true);
                _visual.Refresh(false);
            });
        }

        public void Activate() { if (!Disabled) _toggle.isOn = !_toggle.isOn; }
        protected override void ApplyLayoutStylesSelf()
        {
            base.ApplyLayoutStylesSelf();
            _visual.RefreshLayout();
        }
        public override void SetProperty(string propertyName, object value)
        {
            switch (propertyName)
            {
                case "checked": case "value":
                    var next = ToBool(value);
                    if (next != Checked)
                    {
                        _toggle.SetIsOnWithoutNotify(next);
                        MarkForStyleResolvingWithSiblings(true);
                        _visual.Refresh(true);
                    }
                    return;
                case "disabled": _toggle.interactable = !ToBool(value); _visual.Refresh(true); return;
                case "on-color": if (TryColor(value, out var on)) _visual.OnColor = on; break;
                case "off-color": if (TryColor(value, out var off)) _visual.OffColor = off; break;
                case "knob-color": if (TryColor(value, out var knob)) _visual.KnobColor = knob; break;
                default: base.SetProperty(propertyName, value); return;
            }
            _visual.Refresh(true);
        }
        public override Action AddEventListener(string eventName, Callback callback)
        {
            if (eventName != "onChange" && eventName != "onValueChanged")
                return base.AddEventListener(eventName, callback);
            var handler = new UnityEngine.Events.UnityAction<bool>(
                v => callback.CallWithPriority(EventPriority.Discrete, v, this));
            _toggle.onValueChanged.AddListener(handler);
            return () => _toggle.onValueChanged.RemoveListener(handler);
        }
        static bool ToBool(object value) => value is bool b ? b : value != null
            && !string.Equals(value.ToString(), "false", StringComparison.OrdinalIgnoreCase)
            && value.ToString() != "0";
        static bool TryColor(object value, out Color color)
        {
            var text = value?.ToString() ?? "";
            return ColorUtility.TryParseHtmlString(text.StartsWith("#") ? text : "#" + text, out color);
        }
    }

    // uGUI Toggle.Rebuild emits onValueChanged during editor layout even when
    // the model did not change. A package switch only emits on activation.
    public sealed class UnityHtmlSwitchToggle : Toggle
    {
        public override void Rebuild(CanvasUpdate executing) { }
    }

    /// <summary>Owns knob geometry and interruptible unscaled motion, including resize and teardown.</summary>
    public sealed class UnityHtmlSwitchVisual : MonoBehaviour
    {
        Toggle _toggle;
        Image _track, _image;
        RectTransform _knob;
        Vector2 _size;
        Tween _tween;
        public Func<bool> ReducedMotion;
        public Color OnColor = new Color(.21f, .35f, .29f);
        public Color OffColor = new Color(.14f, .24f, .21f, .18f);
        public Color KnobColor = new Color(.965f, .94f, .875f);
        public bool IsAnimating => _tween != null && _tween.IsActive();
        static Sprite _circle;
        internal static Sprite Circle
        {
            get
            {
                if (_circle != null) return _circle;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                var colors = new Color[n * n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    colors[y*n+x] = new Color(1, 1, 1, Mathf.Clamp01(31.5f -
                        Vector2.Distance(new Vector2(x+.5f,y+.5f), new Vector2(32,32))));
                tex.SetPixels(colors); tex.Apply(false, true);
                _circle = Sprite.Create(tex, new Rect(0,0,n,n), Vector2.one*.5f, 100, 0,
                    SpriteMeshType.FullRect, Vector4.one*31);
                _circle.hideFlags = HideFlags.HideAndDontSave;
                return _circle;
            }
        }
        internal void Initialize(Toggle toggle, Image track, RectTransform knob, Image image)
        { _toggle = toggle; _track = track; _knob = knob; _image = image; Refresh(true); }
        internal void RefreshLayout()
        {
            if (_track == null) return;
            var size = _track.rectTransform.rect.size;
            if (size == _size) return;
            _size = size;
            var k = Mathf.Max(0, Mathf.Min(size.y - 12, size.x - 12));
            _knob.sizeDelta = new Vector2(k, k);
            Refresh(true);
        }
        internal void Refresh(bool instant)
        {
            if (_toggle == null) return;
            _track.color = _toggle.isOn ? OnColor : OffColor;
            _image.color = KnobColor;
            var target = new Vector2(_toggle.isOn ? Mathf.Max(6, _size.x-_knob.sizeDelta.x-6) : 6, 0);
            _tween?.Kill(); _tween = null;
            if (instant || !Application.isPlaying || ReducedMotion?.Invoke() == true)
                _knob.anchoredPosition = target;
            else _tween = DOTween.To(() => _knob.anchoredPosition,
                v => _knob.anchoredPosition = v, target, .24f).SetEase(Ease.OutCubic).SetUpdate(true);
        }
        void LateUpdate()
        {
            RefreshLayout();
            if (IsAnimating && ReducedMotion?.Invoke() == true) Refresh(true);
        }
        void OnDisable() { _tween?.Kill(); _tween = null; }
        void OnDestroy() { _tween?.Kill(); }
    }
}
