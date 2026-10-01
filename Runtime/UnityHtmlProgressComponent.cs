using System;
using System.Globalization;
using DG.Tweening;
using ReactUnity.UGUI;
using ReactUnity.Types;
using UnityEngine;
using UnityEngine.UI;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// <c>&lt;progress&gt;</c> / <c>&lt;radial&gt;</c> — filled bars for HP, XP,
    /// cooldowns and cast bars. Self-contained native hierarchy: a stretched
    /// track <see cref="Image"/> plus a filled <see cref="Image"/> child.
    ///
    /// <para>Attributes: <c>value</c>, <c>max</c>, <c>track-color</c>,
    /// <c>fill-color</c>, <c>low-threshold</c> + <c>low-color</c> (fill tints
    /// below the normalized threshold), <c>smooth</c> (fill tween duration
    /// in seconds — a changing value animates instead of snapping),
    /// <c>origin</c> (<c>left|right|top|bottom</c> for progress,
    /// <c>top|right|bottom|left</c> edge index for radial).</para>
    /// </summary>
    internal sealed class UnityHtmlProgressComponent : UGUIComponent
    {
        private readonly Image _fill;
        private readonly Image _track;
        private readonly bool _radial;
        private float _value;
        private float _max = 1f;
        private float _lowThreshold = -1f;
        private Color _fillColor = Color.white;
        private Color _lowColor = new Color(1f, 0.35f, 0.3f);
        private float _smooth;
        private Tween _tween;

        public UnityHtmlProgressComponent(UGUIContext context, bool radial)
            : base(context, radial ? "radial" : "progress")
        {
            _radial = radial;
            _track = AddComponent<Image>();
            _track.raycastTarget = false;
            _track.color = new Color(1f, 1f, 1f, 0.15f);

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(GameObject.transform, false);
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillRect.localScale = Vector3.one;

            _fill = fillGo.AddComponent<Image>();
            _fill.raycastTarget = false;
            _fill.color = _fillColor;
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = radial ? Image.FillMethod.Radial360 : Image.FillMethod.Horizontal;
            _fill.fillOrigin = radial
                ? (int)Image.Origin360.Top
                : (int)Image.OriginHorizontal.Left;
            _fill.fillAmount = 0f;
        }

        public override void SetProperty(string propertyName, object value)
        {
            switch (propertyName)
            {
                case "value":
                    SetValue(ToFloat(value, 0f));
                    return;
                case "max":
                    _max = Mathf.Max(0.0001f, ToFloat(value, 1f));
                    SetValue(_value);
                    return;
                case "smooth":
                    _smooth = Mathf.Max(0f, ToFloat(value, 0f));
                    return;
                case "low-threshold":
                    _lowThreshold = ToFloat(value, -1f);
                    ApplyColor();
                    return;
                case "low-color":
                    if (TryColor(value, out var low)) { _lowColor = low; ApplyColor(); }
                    return;
                case "fill-color":
                    if (TryColor(value, out var fill)) { _fillColor = fill; ApplyColor(); }
                    return;
                case "track-color":
                    if (TryColor(value, out var track)) _track.color = track;
                    return;
                case "origin":
                    SetOrigin(value?.ToString());
                    return;
                case "clockwise":
                    if (_radial)
                        _fill.fillClockwise = value == null || !IsFalse(value.ToString());
                    return;
                default:
                    base.SetProperty(propertyName, value);
                    return;
            }
        }

        protected override void DestroySelf()
        {
            _tween?.Kill();
            _tween = null;
            base.DestroySelf();
        }

        private void SetValue(float value)
        {
            _value = value;
            var target = Mathf.Clamp01(_value / _max);
            _tween?.Kill();
            if (_smooth > 0f && Application.isPlaying)
            {
                _tween = DOTween.To(() => _fill.fillAmount, v => _fill.fillAmount = v, target, _smooth)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true)
                    .OnUpdate(ApplyColor);
            }
            else
            {
                _fill.fillAmount = target;
                ApplyColor();
            }
        }

        private void ApplyColor()
        {
            var normalized = _fill.fillAmount;
            _fill.color = _lowThreshold >= 0f && normalized <= _lowThreshold
                ? _lowColor
                : _fillColor;
        }

        private void SetOrigin(string origin)
        {
            if (origin == null)
                return;
            if (_radial)
            {
                _fill.fillOrigin = origin.ToLowerInvariant() switch
                {
                    "right" => (int)Image.Origin360.Right,
                    "bottom" => (int)Image.Origin360.Bottom,
                    "left" => (int)Image.Origin360.Left,
                    _ => (int)Image.Origin360.Top,
                };
            }
            else
            {
                _fill.fillOrigin = origin.ToLowerInvariant() switch
                {
                    "right" => (int)Image.OriginHorizontal.Right,
                    _ => (int)Image.OriginHorizontal.Left,
                };
            }
        }

        private static bool TryColor(object value, out Color color)
        {
            color = default;
            var text = value?.ToString();
            if (string.IsNullOrEmpty(text))
                return false;
            return ColorUtility.TryParseHtmlString(text.StartsWith("#") ? text : "#" + text, out color);
        }

        private static float ToFloat(object value, float fallback)
        {
            if (value == null)
                return fallback;
            if (value is IConvertible convertible)
                return convertible.ToSingle(CultureInfo.InvariantCulture);
            return float.TryParse(value.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
        }

        private static bool IsFalse(string value)
            => string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
               value == "0";
    }
}
