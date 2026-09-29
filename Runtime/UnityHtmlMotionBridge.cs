using System;
using System.Collections.Generic;
using DG.Tweening;
using ReactUnity;
using ReactUnity.UGUI;
using ReactUnity.UGUI.Behaviours;
using UnityEngine;

namespace UnityHTML.Runtime
{
    public sealed class UnityHtmlMotionBridge : IUnityHtmlMotion
    {
        /// <inheritdoc />
        public event Action<string> ExitFinished;

        private bool _reducedMotion;

        /// <inheritdoc />
        public bool ReducedMotion
        {
            get => _reducedMotion;
            set
            {
                _reducedMotion = value;
                if (value)
                    StopAll();
            }
        }

        private readonly Dictionary<string, ActiveMotion> _active = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _declared = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly List<string> _stale = new(8);
        private readonly List<ReactElement> _elements = new(96);
        private RectTransform _root;

        public void Play(string targetId, string preset, float duration, float delay)
        {
            if (!Application.isPlaying || !TryFindTarget(targetId, out RectTransform target))
                return;

            Play(targetId, target, preset, duration, delay,
                UnityHtmlMotionPolicy.DefaultDistance, UnityHtmlMotionPolicy.DefaultEase(preset),
                isExit: false);
        }

        public void Stop(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId) || !_active.Remove(targetId, out ActiveMotion motion))
                return;

            CancelMotion(motion);
        }

        public void RestoreResting(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
                return;

            if (_active.Remove(targetId, out ActiveMotion motion))
            {
                CancelMotion(motion);
                return;
            }

            // No tracked motion: a finished tween (e.g. fade-out) already removed
            // itself from _active but can leave the CanvasGroup at alpha 0.
            if (!TryFindTarget(targetId, out RectTransform target))
                return;
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group != null)
                group.alpha = ActiveMotion.ResolveRestingAlpha(target, group);
        }

        /// <summary>
        /// Called by the document tree before a component is destroyed/pooled during
        /// reconciliation. Stops every motion targeting that element (or its children)
        /// so a live tween can never write into a recycled element owned by a newer page.
        /// </summary>
        internal void HandleComponentRemoved(IReactComponent component)
        {
            if (component is not UGUIComponent ugui || ugui.GameObject == null)
                return;

            foreach (ReactElement element in ugui.GameObject.GetComponentsInChildren<ReactElement>(true))
            {
                string id = element != null && element.Component != null ? element.Component.Id : null;
                if (!string.IsNullOrEmpty(id))
                    Stop(id);
            }
        }

        internal void Attach(RectTransform root)
        {
            _root = root;
        }

        internal void ApplyDeclaredMotions()
        {
            if (!Application.isPlaying || _root == null)
                return;

            _seen.Clear();
            CollectElements();
            for (int index = 0; index < _elements.Count; index++)
            {
                UGUIComponent component = _elements[index] != null ? _elements[index].Component : null;
                if (component == null || string.IsNullOrWhiteSpace(component.Id))
                    continue;

                string roleText = Data(component, "motion-role");
                string presetText = Data(component, "motion");
                if (string.IsNullOrWhiteSpace(roleText) && string.IsNullOrWhiteSpace(presetText))
                    continue;

                string id = component.Id;
                _seen.Add(id);

                string durationText = Data(component, "motion-duration");
                string delayText = Data(component, "motion-delay");
                string distanceText = Data(component, "motion-distance");
                string easeText = Data(component, "motion-ease");
                string signature = $"{component.RectTransform.GetEntityId()}|{roleText}|{presetText}|{durationText}|{delayText}|{distanceText}|{easeText}";

                if (_declared.TryGetValue(id, out string previous) && previous == signature)
                    continue;

                UnityHtmlDeclaredMotion motion = UnityHtmlMotionPolicy.ResolveDeclared(
                    roleText, presetText, durationText, delayText, distanceText, easeText);
                if (!motion.Animated)
                {
                    // Opted out ("none") or an exit without a role: record the
                    // declaration and restore any still-running prior motion.
                    Stop(id);
                    _declared[id] = signature;
                    continue;
                }

                _declared[id] = signature;
                try
                {
                    Play(
                        id,
                        component.RectTransform,
                        motion.Preset,
                        motion.Duration,
                        motion.Delay,
                        motion.Distance,
                        motion.Ease,
                        motion.IsExit);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[UnityHTML Motion] Skipped '{id}' motion '{motion.Preset}': {exception.GetBaseException().Message}");
                }
            }

            _stale.Clear();
            foreach (string id in _declared.Keys)
            {
                if (!_seen.Contains(id))
                    _stale.Add(id);
            }
            for (int index = 0; index < _stale.Count; index++)
            {
                _declared.Remove(_stale[index]);
                Stop(_stale[index]);
            }
        }

        internal void Detach()
        {
            StopAll();
            _declared.Clear();
            _root = null;
        }

        private void Play(
            string id,
            RectTransform target,
            string preset,
            float duration,
            float delay,
            float distance,
            Ease ease,
            bool isExit)
        {
            if (target == null || string.IsNullOrWhiteSpace(id))
                return;

            Stop(id);
            if (_reducedMotion)
            {
                // Snap to the final state: entries rest at their declared pose,
                // exits complete instantly so close flows keep their callback.
                if (isExit)
                    ExitFinished?.Invoke(id);
                return;
            }
            duration = Mathf.Clamp(duration, 0.04f, 2f);
            delay = Mathf.Clamp(delay, 0f, 2f);
            distance = Mathf.Clamp(Mathf.Abs(distance), 0f, 160f);

            string normalizedPreset = (preset ?? string.Empty).Trim().ToLowerInvariant();
            CanvasGroup group = RequiresAlpha(normalizedPreset) ? TryEnsureCanvasGroup(target) : null;
            var motion = new ActiveMotion(target, group);
            Sequence sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetRecyclable(true)
                .SetLink(target.gameObject, LinkBehaviour.KillOnDestroy)
                .SetTarget(target);

            switch (normalizedPreset)
            {
                case "fade":
                    if (group == null)
                    {
                        sequence.Kill(false);
                        return;
                    }
                    group.alpha = 0f;
                    sequence.Append(Fade(group, motion.Alpha, duration, ease));
                    break;
                case "fade-out":
                    if (group == null)
                    {
                        sequence.Kill(false);
                        return;
                    }
                    sequence.Append(Fade(group, 0f, duration, ease));
                    break;
                case "slide-left":
                    target.anchoredPosition = motion.Position + Vector2.right * distance;
                    AppendMoveAndFade(sequence, motion, duration, ease);
                    break;
                case "slide-right":
                    target.anchoredPosition = motion.Position + Vector2.left * distance;
                    AppendMoveAndFade(sequence, motion, duration, ease);
                    break;
                case "slide-up":
                    target.anchoredPosition = motion.Position + Vector2.down * distance;
                    AppendMoveAndFade(sequence, motion, duration, ease);
                    break;
                case "slide-down":
                    target.anchoredPosition = motion.Position + Vector2.up * distance;
                    AppendMoveAndFade(sequence, motion, duration, ease);
                    break;
                case "scale":
                    target.localScale = motion.Scale * 0.94f;
                    sequence.Append(Scale(target, motion.Scale, duration, ease));
                    if (group != null)
                    {
                        group.alpha = 0f;
                        sequence.Join(Fade(group, motion.Alpha, duration * 0.8f, Ease.OutQuad));
                    }
                    break;
                case "pulse":
                    sequence.Append(Scale(target, motion.Scale * 1.035f, duration * 0.45f, Ease.OutQuad))
                        .Append(Scale(target, motion.Scale, duration * 0.55f, Ease.InOutQuad));
                    break;
                case "spin":
                    sequence.Append(target.DORotate(new Vector3(0f, 0f, -360f), duration, RotateMode.FastBeyond360)
                        .SetEase(Ease.Linear));
                    sequence.SetLoops(-1, LoopType.Restart);
                    break;
                case "pulse-loop":
                    if (group == null)
                    {
                        sequence.Kill(false);
                        return;
                    }
                    sequence.Append(Fade(group, Mathf.Max(0.1f, motion.Alpha * 0.35f), duration, Ease.InOutQuad));
                    sequence.SetLoops(-1, LoopType.Yoyo);
                    break;
                default:
                    motion.Restore();
                    sequence.Kill(false);
                    return;
            }

            if (delay > 0f)
                sequence.SetDelay(delay);

            motion.Tween = sequence;
            _active[id] = motion;
            sequence.OnKill(() =>
            {
                if (_active.TryGetValue(id, out ActiveMotion current) && ReferenceEquals(current, motion))
                    _active.Remove(id);
            });
            if (isExit)
            {
                // Guaranteed exactly-once completion: the flag converges every
                // path — OnComplete, OnKill (link-destroy), and bridge-driven
                // cancellation via CancelMotion, which invokes it directly so
                // teardown can never leave the callback hanging.
                bool exitFinished = false;
                void FinishExit()
                {
                    if (exitFinished)
                        return;
                    exitFinished = true;
                    ExitFinished?.Invoke(id);
                }
                motion.FinishExit = FinishExit;
                sequence.OnComplete(FinishExit);
                sequence.OnKill(FinishExit);
            }
        }

        private static void CancelMotion(ActiveMotion motion)
        {
            motion.Tween?.Kill(false);
            motion.Restore();
            motion.FinishExit?.Invoke();
        }

        private static void AppendMoveAndFade(Sequence sequence, ActiveMotion motion, float duration, Ease ease)
        {
            sequence.Append(Move(motion.Target, motion.Position, duration, ease));
            if (motion.Group == null)
                return;

            motion.Group.alpha = 0f;
            sequence.Join(Fade(motion.Group, motion.Alpha, duration * 0.8f, Ease.OutQuad));
        }

        private static Tweener Move(RectTransform target, Vector2 end, float duration, Ease ease)
            => DOTween.To(() => target.anchoredPosition, value => target.anchoredPosition = value, end, duration).SetEase(ease);

        private static Tweener Scale(RectTransform target, Vector3 end, float duration, Ease ease)
            => DOTween.To(() => target.localScale, value => target.localScale = value, end, duration).SetEase(ease);

        private static Tweener Fade(CanvasGroup group, float end, float duration, Ease ease)
            => DOTween.To(() => group.alpha, value => group.alpha = value, end, duration).SetEase(ease);

        private static bool RequiresAlpha(string preset)
        {
            return preset == "fade"
                || preset == "fade-out"
                || preset == "slide-left"
                || preset == "slide-right"
                || preset == "slide-up"
                || preset == "slide-down"
                || preset == "scale"
                || preset == "pulse-loop";
        }

        private static CanvasGroup TryEnsureCanvasGroup(RectTransform target)
        {
            if (target == null)
                return null;

            try
            {
                CanvasGroup group = target.GetComponent<CanvasGroup>();
                return group != null ? group : target.gameObject.AddComponent<CanvasGroup>();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UnityHTML Motion] CanvasGroup unavailable on '{target.name}': {exception.GetBaseException().Message}");
                return null;
            }
        }

        private bool TryFindTarget(string id, out RectTransform target)
        {
            target = null;
            if (_root == null || string.IsNullOrWhiteSpace(id))
                return false;

            CollectElements();
            for (int index = 0; index < _elements.Count; index++)
            {
                UGUIComponent component = _elements[index] != null ? _elements[index].Component : null;
                if (component != null && component.Id == id)
                {
                    target = component.RectTransform;
                    return target != null;
                }
            }
            return false;
        }

        private void CollectElements()
        {
            _elements.Clear();
            _root.GetComponentsInChildren(true, _elements);
        }

        private void StopAll()
        {
            if (_active.Count == 0)
                return;

            _stale.Clear();
            _stale.AddRange(_active.Keys);
            for (int index = 0; index < _stale.Count; index++)
                Stop(_stale[index]);
        }

        private static string Data(UGUIComponent component, string key)
            => component.Data.TryGetValue(key, out object value) ? value?.ToString() ?? string.Empty : string.Empty;

        private sealed class ActiveMotion
        {
            public ActiveMotion(RectTransform target, CanvasGroup group)
            {
                Target = target;
                Group = group;
                Position = target.anchoredPosition;
                Scale = target.localScale;
                Rotation = target.localEulerAngles;
                Alpha = ResolveRestingAlpha(target, group);
            }

            public RectTransform Target { get; }
            public CanvasGroup Group { get; }
            public Vector2 Position { get; }
            public Vector3 Scale { get; }
            public Vector3 Rotation { get; }
            public float Alpha { get; }
            public Tween Tween { get; set; }
            /// <summary>Exactly-once exit completion for declarative exits —
            /// invoked by tween callbacks or by CancelMotion on teardown.</summary>
            public Action FinishExit { get; set; }

            public void Restore()
            {
                if (Target != null)
                {
                    Target.anchoredPosition = Position;
                    Target.localScale = Scale;
                    Target.localEulerAngles = Rotation;
                }
                if (Group != null)
                    Group.alpha = Alpha;
            }

            // A finished tween (e.g. fade-out) can leave the CanvasGroup at alpha 0,
            // so the resting alpha is the element's computed opacity when available.
            internal static float ResolveRestingAlpha(RectTransform target, CanvasGroup group)
            {
                UGUIComponent component = target != null ? target.GetComponent<ReactElement>()?.Component : null;
                var style = component != null && !component.Destroyed ? component.ComputedStyle : null;
                return style != null ? Mathf.Clamp01(style.opacity) : group != null ? group.alpha : 1f;
            }
        }
    }
}
