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
    /// <summary>
    /// <c>&lt;switch&gt;</c> — settings-toggle control: a track
    /// <see cref="Image"/> with a knob child that slides on state change.
    /// Attributes: <c>value</c> (bool), <c>disabled</c>, <c>on-color</c>,
    /// <c>off-color</c>, <c>knob-color</c>. Fires <c>onChange(bool)</c>.
    /// </summary>
    internal sealed class UnityHtmlSwitchComponent : UGUIComponent
    {
        private readonly Toggle _toggle;
        private readonly Image _track;
        private readonly RectTransform _knob;
        private readonly Image _knobImage;
        private Color _onColor = new Color(0.3f, 0.75f, 0.45f);
        private Color _offColor = new Color(1f, 1f, 1f, 0.2f);
        private Tween _tween;

        public UnityHtmlSwitchComponent(UGUIContext context) : base(context, "switch")
        {
            _track = AddComponent<Image>();
            _track.color = _offColor;
            _track.raycastTarget = true;

            var knobGo = new GameObject("Knob", typeof(RectTransform));
            knobGo.transform.SetParent(GameObject.transform, false);
            _knob = (RectTransform)knobGo.transform;
            _knob.anchorMin = new Vector2(0f, 0.5f);
            _knob.anchorMax = new Vector2(0f, 0.5f);
            _knob.pivot = new Vector2(0f, 0.5f);
            _knob.sizeDelta = new Vector2(0f, 0f); // set on layout via anchors
            _knobImage = knobGo.AddComponent<Image>();
            _knobImage.raycastTarget = false;

            _toggle = AddComponent<Toggle>();
            _toggle.targetGraphic = _track;
            _toggle.transition = Selectable.Transition.None;
            _toggle.onValueChanged.AddListener(OnToggled);
            ApplyKnob();
        }

        protected override void ApplyLayoutStylesSelf()
        {
            base.ApplyLayoutStylesSelf();
            ApplyKnob();
        }

        public override void SetProperty(string propertyName, object value)
        {
            switch (propertyName)
            {
                case "value":
                    _toggle.isOn = ToBool(value);
                    return;
                case "disabled":
                    _toggle.interactable = !ToBool(value);
                    return;
                case "on-color":
                    if (TryColor(value, out var on)) { _onColor = on; ApplyKnob(); }
                    return;
                case "off-color":
                    if (TryColor(value, out var off)) { _offColor = off; ApplyKnob(); }
                    return;
                case "knob-color":
                    if (TryColor(value, out var knob)) _knobImage.color = knob;
                    return;
                default:
                    base.SetProperty(propertyName, value);
                    return;
            }
        }

        public override Action AddEventListener(string eventName, Callback callback)
        {
            if (eventName == "onChange" || eventName == "onValueChanged")
            {
                var handler = new UnityEngine.Events.UnityAction<bool>(
                    v => callback.CallWithPriority(EventPriority.Discrete, v, this));
                _toggle.onValueChanged.AddListener(handler);
                return () => _toggle.onValueChanged.RemoveListener(handler);
            }
            return base.AddEventListener(eventName, callback);
        }

        protected override void DestroySelf()
        {
            _tween?.Kill();
            _tween = null;
            base.DestroySelf();
        }

        private void OnToggled(bool isOn)
        {
            ApplyKnob();
            var rect = _knob.rect;
            var size = Mathf.Max(rect.width, 0f);
            var target = isOn ? Mathf.Max(0f, _track.rectTransform.rect.width - size - 2f) : 2f;
            _tween?.Kill();
            if (Application.isPlaying)
                _tween = DOTween.To(() => _knob.anchoredPosition.x,
                    v => _knob.anchoredPosition = new Vector2(v, 0f), target, 0.15f)
                    .SetEase(Ease.OutQuad).SetUpdate(true);
            else
                _knob.anchoredPosition = new Vector2(target, 0f);
        }

        private void ApplyKnob()
        {
            _track.color = _toggle.isOn ? _onColor : _offColor;
            var height = Mathf.Max(0f, _track.rectTransform.rect.height);
            var knobSize = Mathf.Max(12f, height - 4f);
            _knob.sizeDelta = new Vector2(knobSize, knobSize);
            _knob.anchoredPosition = new Vector2(_toggle.isOn
                ? Mathf.Max(0f, _track.rectTransform.rect.width - knobSize - 2f)
                : 2f, 0f);
        }

        private static bool ToBool(object value)
            => value is bool b ? b
                : value != null && !string.Equals(value.ToString(), "false",
                    StringComparison.OrdinalIgnoreCase) && value.ToString() != "0";

        private static bool TryColor(object value, out Color color)
        {
            color = default;
            var text = value?.ToString();
            if (string.IsNullOrEmpty(text))
                return false;
            return ColorUtility.TryParseHtmlString(text.StartsWith("#") ? text : "#" + text, out color);
        }

    }
}
