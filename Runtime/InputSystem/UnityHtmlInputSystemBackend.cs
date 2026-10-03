using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Scripting;
using UnityHTML.Runtime;

// This package assembly may have no scene-type references; keep its runtime registration discoverable.
[assembly: AlwaysLinkAssembly]

namespace UnityHTML.InputSystem
{
    /// <summary>Typed optional integration: survives IL2CPP, repairs incomplete UI bindings, never edits a shared action asset.</summary>
    [Preserve]
    public static class UnityHtmlInputSystemBackend
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Register() => UnityHtmlInput.RegisterInputBackend(EnsureModule, Poll);

        public static void EnsureModule(EventSystem events)
        {
            // Respect a host's custom XR/multiplayer module. Replace only the stock legacy module.
            var modules = events.GetComponents<BaseInputModule>();
            if (modules.Any(m => m.enabled && !(m is StandaloneInputModule) && !(m is InputSystemUIInputModule))) return;
            var module = events.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = events.gameObject.AddComponent<InputSystemUIInputModule>();
            foreach (var legacy in events.GetComponents<StandaloneInputModule>()) legacy.enabled = false;
            module.enabled = true;
            if (module.point?.action == null || module.leftClick?.action == null)
            {
                var point = module.actionsAsset?.FindAction("UI/Point", false);
                var click = module.actionsAsset?.FindAction("UI/Click", false);
                if (point != null && click != null)
                {
                    if (module.point?.action == null) module.point = InputActionReference.Create(point);
                    if (module.leftClick?.action == null) module.leftClick = InputActionReference.Create(click);
                }
                else module.AssignDefaultActions();
            }
            // A disabled map can outlive a resume or scene handoff even when the module component is enabled.
            Enable(module.point); Enable(module.leftClick); Enable(module.rightClick); Enable(module.middleClick);
            Enable(module.scrollWheel); Enable(module.move); Enable(module.submit); Enable(module.cancel);
            Enable(module.trackedDevicePosition); Enable(module.trackedDeviceOrientation);
        }
        static void Enable(InputActionReference reference)
        { if (reference?.action != null && !reference.action.enabled) reference.action.Enable(); }

        public static void Poll()
        {
            var touch = Touchscreen.current;
            bool touched = false;
            if (touch != null) foreach (var finger in touch.touches)
                if (finger.press.isPressed || finger.press.wasPressedThisFrame) { touched = true; break; }
            if (touched)
                UnityHtmlInput.NotifyDevice(UnityHtmlInputDevice.Touch);
            else
            {
                var gamepad = Gamepad.current;
                var keyboard = Keyboard.current;
                var mouse = Mouse.current;
                if (gamepad != null && (gamepad.buttonSouth.wasPressedThisFrame || gamepad.buttonEast.wasPressedThisFrame
                    || gamepad.dpad.ReadValue().sqrMagnitude > .01f || gamepad.leftStick.ReadValue().sqrMagnitude > .04f))
                    UnityHtmlInput.NotifyDevice(UnityHtmlInputDevice.Gamepad);
                else if ((keyboard != null && keyboard.anyKey.wasPressedThisFrame) || (mouse != null
                    && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.delta.ReadValue().sqrMagnitude > 4)))
                    UnityHtmlInput.NotifyDevice(UnityHtmlInputDevice.KeyboardMouse);
            }
            if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame))
                UnityHtmlInput.NotifyBackRequested();
        }
    }
}
