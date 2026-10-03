using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Input device families reported by <see cref="UnityHtmlInput"/>.
    /// </summary>
    public enum UnityHtmlInputDevice
    {
        KeyboardMouse,
        Gamepad,
        Touch,
    }

    /// <summary>
    /// Zero-config input plumbing for mounted documents.
    ///
    /// <para><see cref="Ensure"/> is invoked by <see cref="UnityHtmlHost.Mount"/>:
    /// it guarantees an <see cref="EventSystem"/> exists and picks the best
    /// input module — the Unity Input System UI module when the Input System
    /// package is installed (through an optional typed assembly), otherwise the legacy <see cref="StandaloneInputModule"/>.</para>
    ///
    /// <para>While a document is mounted, a hidden driver polls device usage
    /// once per frame: <see cref="ActiveDevice"/> tracks the most recently
    /// used family and <see cref="DeviceChanged"/> fires on transitions —
    /// the signal behind device-aware glyphs and tooltip behaviour.</para>
    ///
    /// <para><see cref="BackRequested"/> fires on Escape, the Android hardware
    /// back button (which Unity maps to Escape) and gamepad B/East. Hosts
    /// forward it into their own back pipeline (<c>Globals.ui.Back()</c>).</para>
    /// </summary>
    public static class UnityHtmlInput
    {
        static Action<EventSystem> _ensureBackend;
        static Action _pollBackend;
        /// <summary>Install a typed optional input backend. The Input System integration registers before scene load.</summary>
        public static void RegisterInputBackend(Action<EventSystem> ensure, Action poll)
        { _ensureBackend = ensure; _pollBackend = poll; }
        internal static bool PollBackend()
        { if (_pollBackend == null) return false; _pollBackend(); return true; }

        /// <summary>Most recently used device family. Starts at KeyboardMouse.</summary>
        public static UnityHtmlInputDevice ActiveDevice { get; internal set; } = UnityHtmlInputDevice.KeyboardMouse;

        /// <summary>Fired when the most recently used device family changes.</summary>
        public static event Action<UnityHtmlInputDevice> DeviceChanged;

        /// <summary>Fired on Escape, Android back or gamepad B/East.</summary>
        public static event Action BackRequested;

        /// <summary>
        /// Ensures an EventSystem with a suitable input module and the shared
        /// polling driver exist. Idempotent.
        /// </summary>
        public static void Ensure()
        {
            EnsureEventSystem();
            UnityHtmlInputDriver.EnsureInstance();
        }

        /// <summary>
        /// Creates an EventSystem when none exists, or attaches the best
        /// available input module to an existing one missing it.
        /// </summary>
        internal static void EnsureEventSystem()
        {
            var eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var go = new GameObject("EventSystem");
                eventSystem = go.AddComponent<EventSystem>();
            }

            eventSystem.enabled = true;
            if (_ensureBackend != null) { _ensureBackend(eventSystem); return; }
            var existing = eventSystem.GetComponent<BaseInputModule>();
            if (existing != null) { existing.enabled = true; return; }

            var moduleType = ResolvePreferredModuleType();
            if (moduleType != null)
                eventSystem.gameObject.AddComponent(moduleType);
            else
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
        }

        /// <summary>
        /// Prefers the Unity Input System UI module when its assembly is
        /// loaded. Reflection keeps the Input System an optional dependency:
        /// without the package this resolves to null and the legacy
        /// StandaloneInputModule is used instead.
        /// </summary>
        private static Type ResolvePreferredModuleType()
        {
            var type = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (type != null && typeof(BaseInputModule).IsAssignableFrom(type))
                return type;
            return null;
        }

        public static void NotifyDevice(UnityHtmlInputDevice device)
        {
            if (device == ActiveDevice)
                return;
            ActiveDevice = device;
            try { DeviceChanged?.Invoke(device); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        public static void NotifyBackRequested()
        {
            try { BackRequested?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}
