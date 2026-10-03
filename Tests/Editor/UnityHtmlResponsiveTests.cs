using System;
using System.Linq;
using NUnit.Framework;
using ReactUnity.UGUI.Behaviours;
using UnityEngine;
using UnityEngine.UI;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    public sealed class UnityHtmlResponsiveTests
    {
        GameObject _root;
        UnityHtmlHost _host;
        RectTransform Root => (RectTransform)_root.transform;
        [SetUp] public void SetUp()
        {
            _root = new GameObject("responsive", typeof(RectTransform));
            Root.sizeDelta = new Vector2(800, 1200);
            UnityHtmlEnvironment.ScreenSizeProvider = () => new Vector2(1600, 2400);
            UnityHtmlEnvironment.SafeAreaProvider = () => new Rect(0, 0, 1600, 2400);
            _host = new UnityHtmlHost { NativeEventResolver = _ => (Action)(() => { }) };
        }
        [TearDown] public void TearDown()
        {
            _host.Dispose(); UnityEngine.Object.DestroyImmediate(_root);
            UnityHtmlEnvironment.ResetOverrides();
        }
        void Mount(string html, string css = null)
        {
            var r = _host.Mount(Root, new UnityHtmlDocument(html, css, "responsive"));
            Assert.IsTrue(r.Succeeded, r.ErrorMessage);
        }
        ReactElement Element(string id) => _root.GetComponentsInChildren<ReactElement>(true).First(x => x.Component?.Id == id);
        void Tick() => _root.GetComponent<UnityHtmlEnvironmentWatcher>().Tick();

        [Test] public void SameOrientationResizeUpdatesMediaAndPreservesControlIdentity()
        {
            Mount("<view id='shell'><switch id='state' checked='true'/></view>",
                "#shell { width: 100%; height: 200px; } @media (min-width: 900px) { #shell { height: 300px; } }");
            var shell = Element("shell"); var toggle = Element("state").GetComponentInChildren<Toggle>();
            Assert.AreEqual(200, ((RectTransform)shell.transform).rect.height, .1f);
            Root.sizeDelta = new Vector2(1000, 1400); Tick();
            Assert.AreSame(shell, Element("shell")); Assert.IsTrue(toggle.isOn);
            Assert.AreEqual(300, ((RectTransform)shell.transform).rect.height, .1f);
            Assert.AreEqual(1000, _host.Viewport.Size.x, .1f);
        }
        [Test] public void WideLayoutAndViewportEventSeeCompletedGeometry()
        {
            Mount("<view id='shell' data-layout='adaptive'><view id='a'/><view id='b'/></view>",
                "#shell { width: 100%; height: 100%; } #a, #b { width: 100px; height: 100px; }");
            var events = 0;
            _host.ViewportChanged += _ => { events++; Assert.AreEqual(1500, ((RectTransform)Element("shell").transform).rect.width, .1f); };
            Root.sizeDelta = new Vector2(1500, 800); Tick();
            Assert.AreEqual(1, events); Assert.IsTrue(_host.Viewport.IsWide);
            Assert.AreEqual(Element("a").transform.localPosition.y, Element("b").transform.localPosition.y, .1f);
            Tick(); Assert.AreEqual(1, events, "Unchanged frames must not notify/reflow");
        }
        [Test] public void SafeAreaUsesLayoutUnitsAddsAuthoredPaddingAndAvoidsNestedDuplication()
        {
            UnityHtmlEnvironment.SafeAreaProvider = () => new Rect(0, 0, 1600, 2280);
            Mount("<view id='outer' data-safe-area='top'><view id='inner' data-safe-area='all'/></view>",
                "#outer { width: 100%; height: 100%; padding: 12px; } #inner { width: 100%; height: 100px; padding: 7px; }");
            Assert.AreEqual(60, _host.Viewport.SafeInsets.w, .01f);
            Assert.AreEqual(72, Element("outer").Component.Layout.PaddingTop.Value, .01f);
            Assert.AreEqual(7, Element("inner").Component.Layout.PaddingTop.Value, .01f);
            UnityHtmlEnvironment.SafeAreaProvider = () => new Rect(0, 40, 1600, 2360); Tick();
            Assert.AreEqual(12, Element("outer").Component.Layout.PaddingTop.Value, .01f);
            Assert.AreEqual(27, Element("inner").Component.Layout.PaddingBottom.Value, .01f);
        }
        [Test] public void RemovingSafeAreaRestoresAuthoredPadding()
        {
            UnityHtmlEnvironment.SafeAreaProvider = () => new Rect(0, 0, 1600, 2280);
            const string css = "#outer { width: 100%; height: 100%; padding: 12px; }";
            Mount("<view id='outer' data-safe-area='top'/>", css);
            Mount("<view id='outer'/>", css);
            Assert.AreEqual(12, Element("outer").Component.Layout.PaddingTop.Value, .01f);
        }
        [Test] public void ScreenCanvasAlreadyInsideSafeAreaDoesNotInsetAgain()
        {
            var canvas = new GameObject("screen", typeof(RectTransform), typeof(Canvas));
            try
            {
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(800, 1200);
                Root.SetParent(canvas.transform, false); Root.anchorMin = Vector2.zero; Root.anchorMax = new Vector2(1, .95f);
                Root.offsetMin = Root.offsetMax = Vector2.zero;
                UnityHtmlEnvironment.SafeAreaProvider = () => new Rect(0, 0, 1600, 2280);
                var frame = UnityHtmlViewport.Capture(Root, new Vector3[4]);
                Assert.AreEqual(0, frame.SafeInsets.w, .01f);
            }
            finally { Root.SetParent(null); UnityEngine.Object.DestroyImmediate(canvas); }
        }
        [Test] public void InvalidRootDoesNotOverwriteLastValidViewport()
        {
            Mount("<view/>"); var previous = _host.Viewport;
            Root.sizeDelta = Vector2.zero; Tick(); Assert.AreEqual(previous, _host.Viewport);
            Root.sizeDelta = new Vector2(1500, 800); Tick(); Assert.IsTrue(_host.Viewport.IsWide);
        }
        [Test] public void AuthoredSwitchSizeWinsOverPackageDefault()
        {
            Mount("<switch id='state'/>", "switch { width: 180px; height: 80px; }");
            Assert.AreEqual(180, ((RectTransform)Element("state").transform).rect.width, .1f);
            Assert.AreEqual(80, ((RectTransform)Element("state").transform).rect.height, .1f);
        }
        [Test] public void SharedCanvasOrientationLeaseRestoresOnlyAfterLastHost()
        {
            _root.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = _root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            UnityHtmlEnvironment.ScreenSizeProvider = () => new Vector2(2560, 1600);
            using var second = new UnityHtmlHost();
            var policy = UnityHtmlAdaptiveCanvas.Acquire(Root, _host);
            UnityHtmlAdaptiveCanvas.Acquire(Root, second);
            Assert.AreEqual(new Vector2(1920, 1080), scaler.referenceResolution);
            policy.Release(_host); Assert.AreEqual(new Vector2(1920, 1080), scaler.referenceResolution);
            policy.Release(second); Assert.AreEqual(new Vector2(1080, 1920), scaler.referenceResolution);
        }
        [Test] public void DesignerChangesCanvasReferenceWhileMountedArePreserved()
        {
            _root.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var policy = UnityHtmlAdaptiveCanvas.Acquire(Root, _host);
            scaler.referenceResolution = new Vector2(600, 1000); policy.Apply(); policy.Release(_host);
            Assert.AreEqual(new Vector2(600, 1000), scaler.referenceResolution);
        }
        [Test] public void NativeEventsWorkWithoutScriptAndRejectUnknownExpressions()
        {
            var clicks = 0; _host.NativeEventResolver = expr => expr == "click" ? (Action)(() => clicks++) : throw new InvalidOperationException("Unknown native callback");
            Mount("<button id='b' onClick='click'/>");
            Element("b").GetComponent<Button>().onClick.Invoke(); Assert.AreEqual(1, clicks);
            Root.sizeDelta = new Vector2(1500, 800); Tick();
            Element("b").GetComponent<Button>().onClick.Invoke(); Assert.AreEqual(2, clicks);
            var rejected = _host.Mount(Root, new UnityHtmlDocument("<button onClick='unknown'/>", null, "rejected"));
            Assert.IsFalse(rejected.Succeeded);
            var script = _host.Mount(Root, new UnityHtmlDocument("<script>throw 'unsafe';</script>", null, "rejected"));
            Assert.IsFalse(script.Succeeded);
        }
    }
}
