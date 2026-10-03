using UnityEngine;

namespace UnityHTML.Runtime
{
    /// <summary>
    /// Hidden per-frame poller created by <see cref="UnityHtmlInput.Ensure"/>.
    /// Detects the active input device family and routes back presses
    /// (Escape / Android back / gamepad B) to <see cref="UnityHtmlInput"/>.
    /// </summary>
    internal sealed class UnityHtmlInputDriver : MonoBehaviour
    {
        private static UnityHtmlInputDriver _instance;
#if ENABLE_LEGACY_INPUT_MANAGER
        private Vector2 _lastMousePosition;
#endif

        internal static void EnsureInstance()
        {
            if (_instance != null)
                return;
            var existing = FindFirstObjectByType<UnityHtmlInputDriver>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }
            var go = new GameObject("UnityHTML Input Driver") { hideFlags = HideFlags.HideInHierarchy };
            if (Application.isPlaying) DontDestroyOnLoad(go);
            _instance = go.AddComponent<UnityHtmlInputDriver>();
        }

        private void Update()
        {
            if (UnityHtmlInput.PollBackend()) return;
            DetectDevice();
            DetectBack();
        }

        private void OnApplicationFocus(bool focused)
        { if (focused) UnityHtmlInput.EnsureEventSystem(); }
        private void OnApplicationPause(bool paused)
        { if (!paused) UnityHtmlInput.EnsureEventSystem(); }

        private void DetectDevice()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount > 0)
            {
                UnityHtmlInput.NotifyDevice(UnityHtmlInputDevice.Touch);
                return;
            }
            for (var b = 0; b <= 19; b++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.JoystickButton0 + b)))
                {
                    UnityHtmlInput.NotifyDevice(UnityHtmlInputDevice.Gamepad);
                    return;
                }
            }
            var mouse = (Vector2)Input.mousePosition;
            if (Input.anyKeyDown
                || Vector2.SqrMagnitude(mouse - _lastMousePosition) > 4f
                || Input.GetMouseButton(0))
            {
                UnityHtmlInput.NotifyDevice(UnityHtmlInputDevice.KeyboardMouse);
            }
            _lastMousePosition = mouse;
#endif
        }

        private void DetectBack()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            // Escape doubles as the Android hardware back button.
            if (Input.GetKeyDown(KeyCode.Escape)
                || Input.GetKeyDown(KeyCode.JoystickButton1))
                UnityHtmlInput.NotifyBackRequested();
#endif
        }
    }
}
