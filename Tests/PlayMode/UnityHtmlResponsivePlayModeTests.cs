using System;
using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    public sealed class UnityHtmlResponsivePlayModeTests
    {
        [UnityTest] public IEnumerator ResizingPreservesInputScrollAndCallbacksWithoutRemount()
        {
            var root = new GameObject("responsive-runtime", typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(800, 1200);
            using var host = new UnityHtmlHost(); var clicks = 0;
            host.NativeEventResolver = _ => (Action)(() => clicks++);
            const string html = "<view class='app'><input value='initial'/><switch checked='true'/><button onClick='click'/><scroll><view class='long'/></scroll></view>";
            const string css = ".app { width: 100%; height: 100%; } input { height: 100px; } button { height: 100px; } scroll { flex-grow: 1; } .long { height: 2600px; width: 100%; }";
            try
            {
                var result = host.Mount(rect, new UnityHtmlDocument(html, css, "responsive-runtime"));
                Assert.IsTrue(result.Succeeded, result.ErrorMessage); yield return Frames(4);
                var input = root.GetComponentInChildren<TMP_InputField>(); Assert.NotNull(input);
                var toggle = root.GetComponentInChildren<Toggle>(); var scroll = root.GetComponentInChildren<ScrollRect>();
                var button = root.GetComponentInChildren<Button>(); input.text = "still typing";
                scroll.verticalNormalizedPosition = .42f; scroll.velocity = Vector2.zero;
                var changes = 0; host.ViewportChanged += _ => changes++;
                rect.sizeDelta = new Vector2(1500, 800); yield return Frames(5);
                Assert.IsTrue(host.Viewport.IsWide); Assert.AreEqual(1, changes);
                Assert.AreSame(input, root.GetComponentInChildren<TMP_InputField>());
                Assert.AreEqual("still typing", input.text); Assert.IsTrue(toggle.isOn);
                Assert.AreEqual(.42f, scroll.verticalNormalizedPosition, .015f);
                button.onClick.Invoke(); Assert.AreEqual(1, clicks);
                rect.sizeDelta = Vector2.zero; yield return Frames(2);
                rect.sizeDelta = new Vector2(720, 1600); yield return Frames(5);
                Assert.IsFalse(host.Viewport.IsWide); Assert.AreEqual("still typing", input.text);
                button.onClick.Invoke(); Assert.AreEqual(2, clicks);
            }
            finally { host.Unmount(); UnityEngine.Object.Destroy(root); }
            yield return null;
        }
        [UnityTest] public IEnumerator SameFrameRemountKeepsAutomaticWatcherAlive()
        {
            var root = new GameObject("remount-runtime", typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(800, 1200);
            using var host = new UnityHtmlHost { NativeEventResolver = _ => (Action)(() => { }) };
            try
            {
                Assert.IsTrue(host.Mount(rect,new UnityHtmlDocument("<view/>", null,"first")).Succeeded);
                host.Unmount();
                Assert.IsTrue(host.Mount(rect,new UnityHtmlDocument("<view/>", "view { height: 100px; }","second")).Succeeded);
                yield return Frames(4); rect.sizeDelta = new Vector2(1500, 800); yield return Frames(4);
                Assert.IsTrue(host.Viewport.IsWide);
            }
            finally { host.Unmount(); UnityEngine.Object.Destroy(root); }
            yield return null;
        }
        static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
    }
}
