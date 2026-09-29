using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Resolved motion for one element role: which preset plays on entry, which
    /// on exit, and the shared timing/distance/easing tokens. Roles pick these
    /// automatically; explicit data-motion attributes may still override any
    /// token locally, and the "none" role opts an element out entirely.
    /// </summary>
    public readonly struct UnityHtmlMotionSpec
    {
        public UnityHtmlMotionSpec(string enterPreset, string exitPreset,
            float duration, float exitDuration, float distance,
            Ease ease, Ease exitEase)
        {
            EnterPreset = enterPreset ?? string.Empty;
            ExitPreset = exitPreset ?? string.Empty;
            Duration = duration;
            ExitDuration = exitDuration;
            Distance = distance;
            Ease = ease;
            ExitEase = exitEase;
            Motionless = false;
        }

        private UnityHtmlMotionSpec(bool motionless)
        {
            EnterPreset = string.Empty;
            ExitPreset = string.Empty;
            Duration = 0f;
            ExitDuration = 0f;
            Distance = 0f;
            Ease = Ease.OutCubic;
            ExitEase = Ease.InQuad;
            Motionless = motionless;
        }

        public string EnterPreset { get; }
        public string ExitPreset { get; }
        public float Duration { get; }
        public float ExitDuration { get; }
        public float Distance { get; }
        public Ease Ease { get; }
        public Ease ExitEase { get; }
        /// <summary>True for the "none" role: the element never animates.</summary>
        public bool Motionless { get; }

        internal static readonly UnityHtmlMotionSpec None = new(true);
    }

    /// <summary>The fully resolved motion decision for one mounted element —
    /// what the bridge will actually play after role defaults and per-attribute
    /// overrides are merged.</summary>
    public readonly struct UnityHtmlDeclaredMotion
    {
        internal UnityHtmlDeclaredMotion(string preset, float duration, float delay,
            float distance, Ease ease, bool isExit)
        {
            Animated = !string.IsNullOrWhiteSpace(preset);
            Preset = preset ?? string.Empty;
            Duration = duration;
            Delay = delay;
            Distance = distance;
            Ease = ease;
            IsExit = isExit;
        }

        /// <summary>False → the element declares no motion (or opted out).</summary>
        public bool Animated { get; }
        public string Preset { get; }
        public float Duration { get; }
        public float Delay { get; }
        public float Distance { get; }
        public Ease Ease { get; }
        /// <summary>True when the motion was declared via data-motion="exit" —
        /// the bridge wires a guaranteed completion callback on these.</summary>
        public bool IsExit { get; }
    }

    /// <summary>
    /// The single motion policy for HTML surfaces. New UI declares
    /// <c>data-motion-role="panel|dialog|scrim|toast|edge-top|edge-bottom|none"</c>
    /// instead of copying preset/duration/ease values into every panel; any
    /// <c>data-motion-*</c> attribute still overrides the role locally, and
    /// <c>data-motion="exit"</c> plays the role's exit preset.
    /// </summary>
    public static class UnityHtmlMotionPolicy
    {
        /// <summary>data-motion value that resolves to the role's exit preset.</summary>
        public const string ExitToken = "exit";
        /// <summary>data-motion / data-motion-role value that disables motion.</summary>
        public const string NoneToken = "none";

        public const float DefaultDuration = 0.16f;
        public const float DefaultDistance = 20f;

        private static readonly Dictionary<string, UnityHtmlMotionSpec> Roles =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["panel"] = new UnityHtmlMotionSpec(
                    "slide-left", "fade-out", 0.16f, 0.12f, 20f,
                    Ease.OutCubic, Ease.InQuad),
                ["dialog"] = new UnityHtmlMotionSpec(
                    "scale", "fade-out", 0.18f, 0.12f, 20f,
                    Ease.OutBack, Ease.InQuad),
                ["scrim"] = new UnityHtmlMotionSpec(
                    "fade", "fade-out", 0.12f, 0.12f, 0f,
                    Ease.OutCubic, Ease.InQuad),
                ["toast"] = new UnityHtmlMotionSpec(
                    "slide-up", "fade-out", 0.14f, 0.10f, 16f,
                    Ease.OutCubic, Ease.InQuad),
                ["edge-top"] = new UnityHtmlMotionSpec(
                    "slide-down", "fade-out", 0.18f, 0.12f, 20f,
                    Ease.OutCubic, Ease.InQuad),
                ["edge-bottom"] = new UnityHtmlMotionSpec(
                    "slide-up", "fade-out", 0.18f, 0.12f, 20f,
                    Ease.OutCubic, Ease.InQuad),
                [NoneToken] = UnityHtmlMotionSpec.None,
            };

        /// <summary>Resolves a role to its spec; unknown roles return false so
        /// callers fall back to explicit attributes only. "none" resolves to a
        /// Motionless spec.</summary>
        public static bool TryResolve(string role, out UnityHtmlMotionSpec spec)
        {
            if (!string.IsNullOrWhiteSpace(role)
                && Roles.TryGetValue(role.Trim(), out spec))
            {
                return true;
            }
            spec = default;
            return false;
        }

        public static IReadOnlyCollection<string> RoleNames => Roles.Keys;

        /// <summary>Merges the declared role and per-attribute overrides into the
        /// final motion decision. Pure — the bridge applies the result verbatim.</summary>
        public static UnityHtmlDeclaredMotion ResolveDeclared(
            string role, string presetAttr,
            string durationText, string delayText, string distanceText, string easeText)
        {
            bool hasSpec = TryResolve(role, out UnityHtmlMotionSpec spec);
            bool hasExplicitPreset = !string.IsNullOrWhiteSpace(presetAttr);
            if (!hasExplicitPreset && !hasSpec)
                return default;
            if ((hasSpec && spec.Motionless)
                || string.Equals(presetAttr, NoneToken, StringComparison.OrdinalIgnoreCase))
            {
                return default;
            }

            bool exit = string.Equals(presetAttr, ExitToken, StringComparison.OrdinalIgnoreCase);
            string preset = exit
                ? (hasSpec ? spec.ExitPreset : string.Empty)
                : (hasExplicitPreset ? presetAttr : spec.EnterPreset);
            if (string.IsNullOrWhiteSpace(preset))
                return default;

            float fallbackDuration = hasSpec
                ? (exit ? spec.ExitDuration : spec.Duration)
                : DefaultDuration;
            float fallbackDistance = hasSpec ? spec.Distance : DefaultDistance;
            Ease fallbackEase = hasSpec
                ? (exit ? spec.ExitEase : spec.Ease)
                : DefaultEase(preset);
            return new UnityHtmlDeclaredMotion(
                preset,
                Number(durationText, fallbackDuration),
                Number(delayText, 0f),
                Number(distanceText, fallbackDistance),
                ResolveEase(easeText, fallbackEase),
                exit);
        }

        // Exits accelerate out (ease-in); entrances decelerate in (ease-out).
        internal static Ease DefaultEase(string preset)
            => string.Equals(preset, "fade-out", StringComparison.OrdinalIgnoreCase) ? Ease.InQuad : Ease.OutCubic;

        internal static float Number(string value, float fallback)
            => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback;

        internal static Ease ResolveEase(string value, Ease fallback)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "linear" => Ease.Linear,
                "in-quad" => Ease.InQuad,
                "in-cubic" => Ease.InCubic,
                "out-quad" => Ease.OutQuad,
                "out-cubic" => Ease.OutCubic,
                "in-out-quad" => Ease.InOutQuad,
                "out-back" => Ease.OutBack,
                _ => fallback,
            };
        }
    }
}
