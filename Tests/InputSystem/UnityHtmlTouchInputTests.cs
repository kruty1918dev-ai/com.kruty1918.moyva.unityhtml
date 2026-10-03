using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityInput = UnityEngine.InputSystem.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityHTML.InputSystem;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    public class UnityHtmlTouchInputTests
    {
        [UnityTest] public IEnumerator IncompleteSerializedModuleGetsTouchBindingsWithoutChangingTheAsset()
        {
            var root = new GameObject("incomplete-input"); root.SetActive(false);
            var events = root.AddComponent<EventSystem>(); var module = root.AddComponent<InputSystemUIInputModule>();
            var asset = ScriptableObject.CreateInstance<InputActionAsset>(); var ui = asset.AddActionMap("UI");
            ui.AddAction("Point", InputActionType.PassThrough, "<Touchscreen>/touch*/position", expectedControlLayout: "Vector2");
            ui.AddAction("Click", InputActionType.PassThrough, "<Touchscreen>/touch*/press", expectedControlLayout: "Button");
            module.actionsAsset = asset; module.point = null; module.leftClick = null; string before = asset.ToJson();
            try
            {
                UnityHtmlInputSystemBackend.EnsureModule(events); root.SetActive(true); yield return null;
                Assert.AreSame(asset, module.actionsAsset); Assert.AreEqual(before, asset.ToJson());
                Assert.AreEqual("Point",module.point.action.name); Assert.AreEqual("Click",module.leftClick.action.name);
                Assert.IsTrue(module.point.action.enabled); Assert.IsTrue(module.leftClick.action.enabled);
                ui.Disable(); UnityHtmlInputSystemBackend.EnsureModule(events);
                Assert.IsTrue(module.point.action.enabled); Assert.IsTrue(module.leftClick.action.enabled);
                var point = module.point; var click = module.leftClick;
                for(int i=0;i<10;i++)UnityHtmlInputSystemBackend.EnsureModule(events);
                Assert.AreSame(point,module.point);Assert.AreSame(click,module.leftClick);
                Assert.AreEqual(1,root.GetComponents<InputSystemUIInputModule>().Length);
            }
            finally { Object.Destroy(root); Object.Destroy(asset); }
            yield return null;
        }
        [UnityTest] public IEnumerator BrokenLegacyModuleIsReplacedAndDefaultActionsListenToTouch()
        {
            var root = new GameObject("legacy-input"); root.SetActive(false);
            var events = root.AddComponent<EventSystem>(); var legacy = root.AddComponent<StandaloneInputModule>();
            var touch = UnityInput.AddDevice<Touchscreen>();
            try
            {
                UnityHtmlInputSystemBackend.EnsureModule(events); root.SetActive(true); yield return null;
                Assert.IsFalse(legacy.enabled); var module = root.GetComponent<InputSystemUIInputModule>();Assert.IsTrue(module.enabled);
                Assert.AreEqual(InputActionType.PassThrough,module.point.action.type);Assert.AreEqual(InputActionType.PassThrough,module.leftClick.action.type);
                Assert.IsTrue(module.point.action.controls.Any(c=>c.device==touch));Assert.IsTrue(module.leftClick.action.controls.Any(c=>c.device==touch));
                UnityInput.QueueStateEvent(touch,new TouchState{touchId=4,position=new Vector2(100,200),phase=UnityEngine.InputSystem.TouchPhase.Began});
                yield return null; UnityHtmlInputSystemBackend.Poll();Assert.AreEqual(UnityHtmlInputDevice.Touch,UnityHtmlInput.ActiveDevice);
                UnityInput.QueueStateEvent(touch,new TouchState{touchId=4,position=new Vector2(100,200),phase=UnityEngine.InputSystem.TouchPhase.Ended});yield return null;
            }
            finally { UnityInput.RemoveDevice(touch);Object.Destroy(root); }
            yield return null;
        }
    }
}
