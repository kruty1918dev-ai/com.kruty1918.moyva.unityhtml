using System.Linq;
using NUnit.Framework;
using ReactUnity.UGUI;
using ReactUnity.UGUI.Behaviours;
using ReactUnity.UGUI.EventHandlers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    public sealed class UnityHtmlSwitchTests
    {
        GameObject _root;
        UnityHtmlHost _host;
        Counter _counter;
        [SetUp] public void Setup()
        {
            _root = new GameObject("switch root", typeof(RectTransform), typeof(Canvas));
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 600);
            _host = new UnityHtmlHost(); _counter = new Counter();
        }
        [TearDown] public void Cleanup() { _host.Dispose(); Object.DestroyImmediate(_root); }
        void Mount(bool on, bool disabled = false, int width = 124)
        {
            var result = _host.Mount(_root.GetComponent<RectTransform>(), new UnityHtmlDocument(
                "<view><label for='#s'>Breeze</label><switch id='s' checked='" + on.ToString().ToLowerInvariant()
                + "' disabled='" + disabled.ToString().ToLowerInvariant()
                + "' onChange='Globals.counter.Add(event)' /></view>",
                "switch { width:" + width + "px; height:68px; }", "switch test"),
                new System.Collections.Generic.Dictionary<string, object> { ["counter"] = _counter });
            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        }
        [Test] public void ModelAndReconciliationAreSilentAndKnobIsRound()
        {
            Mount(true); Assert.That(_counter.Value, Is.Zero);
            var toggle = _root.GetComponentInChildren<Toggle>();
            var knob = toggle.transform.Find("Knob") as RectTransform;
            Assert.That(toggle.isOn, Is.True);
            Assert.That(knob.rect.width, Is.EqualTo(knob.rect.height).Within(.1f));
            Assert.That(toggle.GetComponent<RectTransform>().rect.width, Is.GreaterThan(100));
            Assert.That(toggle.graphic, Is.Null);
            Mount(false, width: 180);
            Assert.That(_counter.Value, Is.Zero);
            Assert.That(_root.GetComponentsInChildren<UnityHtmlSwitchVisual>().Length, Is.EqualTo(1));
        }
        [Test] public void LabelAndDirectClickEmitOnceAndRespectDisabled()
        {
            Mount(false);
            var label = _root.GetComponentsInChildren<ReactElement>().Select(e => e.Component)
                .OfType<LabelComponent>().Single().GameObject.GetComponent<LabelClickHandler>();
            label.OnPointerClick(new PointerEventData(EventSystem.current));
            Assert.That(_counter.Value, Is.EqualTo(1));
            _root.GetComponentInChildren<Toggle>().isOn = false;
            Assert.That(_counter.Value, Is.EqualTo(2));
            Mount(false, true);
            label.OnPointerClick(new PointerEventData(EventSystem.current));
            Assert.That(_counter.Value, Is.EqualTo(2));
        }
        public sealed class Counter { public int Value; public void Add(bool value) => Value++; }
    }
}
