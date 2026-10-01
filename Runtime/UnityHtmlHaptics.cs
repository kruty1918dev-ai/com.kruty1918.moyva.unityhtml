using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Pluggable haptic backend. Games on platforms with granular haptics
    /// (iOS Taptic Engine, console rumble) should replace
    /// <see cref="UnityHtmlHaptics.Provider"/> with their own implementation.
    /// </summary>
    public interface IUnityHtmlHaptics
    {
        void Play(UnityHtmlHapticLevel level);
    }

    public enum UnityHtmlHapticLevel
    {
        Light,
        Medium,
        Heavy,
        Selection,
    }

    /// <summary>
    /// Default haptics: <see cref="Handheld.Vibrate"/> on handheld devices,
    /// no-op elsewhere. Per-level granularity needs a platform plugin — the
    /// API shape is stable so swapping <see cref="Provider"/> upgrades it.
    /// </summary>
    public static class UnityHtmlHaptics
    {
        public static IUnityHtmlHaptics Provider { get; set; } = new HandheldHaptics();

        public static void Play(string level)
        {
            if (string.IsNullOrEmpty(level))
                return;
            var parsed = level.Trim().ToLowerInvariant() switch
            {
                "medium" => UnityHtmlHapticLevel.Medium,
                "heavy" => UnityHtmlHapticLevel.Heavy,
                "selection" => UnityHtmlHapticLevel.Selection,
                _ => UnityHtmlHapticLevel.Light,
            };
            Play(parsed);
        }

        public static void Play(UnityHtmlHapticLevel level)
        {
            try { Provider?.Play(level); }
            catch (System.Exception ex) { Debug.LogException(ex); }
        }

        private sealed class HandheldHaptics : IUnityHtmlHaptics
        {
            public void Play(UnityHtmlHapticLevel level)
            {
#if UNITY_ANDROID || UNITY_IOS
                if (level != UnityHtmlHapticLevel.Selection)
                    Handheld.Vibrate();
#endif
            }
        }
    }

    /// <summary>
    /// Attached by the element-effects pass to elements carrying
    /// <c>data-haptic="light|medium|heavy|selection"</c> — plays the pattern
    /// on the element's primary pointer interaction.
    /// </summary>
    internal sealed class UnityHtmlHapticTrigger : MonoBehaviour, IPointerClickHandler
    {
        internal string Level = "light";

        public void OnPointerClick(PointerEventData eventData)
            => UnityHtmlHaptics.Play(Level);
    }

    /// <summary>
    /// Script bridge: <c>Globals.haptics.Play("medium")</c> from markup JS.
    /// </summary>
    internal sealed class UnityHtmlHapticsBridge
    {
        public void Play(string level) => UnityHtmlHaptics.Play(level);
    }
}
