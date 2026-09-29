using System.Reflection;
using ReactUnity.UGUI.Behaviours;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// SmoothScrollRect with the in-flight accumulation fixed. Upstream computes
    /// each scroll event's target from the mid-animation position, so a
    /// continuous wheel/touchpad stream collapses to roughly a single step.
    /// Completing the current animation to its target first lets deltas
    /// accumulate on the actual target — including on abrupt direction
    /// reversals, where the pending step finishes instantly and the new input
    /// moves from it rather than drifting against the wheel.
    /// </summary>
    public class MoyvaSmoothScrollRect : SmoothScrollRect
    {
        private static readonly FieldInfo SmoothCoroutineField = typeof(SmoothScrollRect)
            .GetField("SmoothCoroutine", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TargetPositionField = typeof(SmoothScrollRect)
            .GetField("targetPosition", BindingFlags.Instance | BindingFlags.NonPublic);

        private UnityHtmlScrollSettings _settings = UnityHtmlScrollSettings.Default;
        private float _baseSensitivity = -1f;

        // The values UnityHtmlHost last pushed through ApplySettings. Settings are
        // per control instance — coroutine, target position and velocity never
        // migrate between scroll controls.
        public UnityHtmlScrollSettings Settings => _settings;

        public void ApplySettings(UnityHtmlScrollSettings settings)
        {
            _settings = settings;
            // The component's serialized sensitivity is the baseline (the scroll
            // creator ships 50, not ScrollRect's 1); WheelSensitivity scales it.
            if (_baseSensitivity < 0f)
                _baseSensitivity = scrollSensitivity;
            scrollSensitivity = _baseSensitivity * settings.WheelSensitivity;
            decelerationRate = settings.DecelerationRate;
            // Reduced motion snaps to the final position: smoothness 0 takes
            // upstream's instant path, and inertia is disabled so a released
            // drag cannot keep gliding.
            inertia = settings.Inertia && !settings.ReducedMotion;
            Smoothness = settings.ReducedMotion ? 0f : settings.Smoothness;

            // A reduced-motion toggle mid-animation settles the pending step
            // immediately instead of letting the lerp play out.
            if (settings.ReducedMotion)
                SettleInFlightScroll();
        }

        private void SettleInFlightScroll()
        {
            var coroutine = SmoothCoroutineField?.GetValue(this) as Coroutine;
            if (coroutine == null)
                return;
            StopCoroutine(coroutine);
            SmoothCoroutineField.SetValue(this, null);
            if (TargetPositionField != null)
                normalizedPosition = (Vector2)TargetPositionField.GetValue(this);
        }

        public override void OnScroll(PointerEventData data)
        {
            SettleInFlightScroll();
            base.OnScroll(data);
        }
    }
}
