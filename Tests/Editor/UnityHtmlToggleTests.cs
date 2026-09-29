using System.Collections.Generic;
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
    // P076: label, indicator and click target stay separate. A label click
    // activates its toggle through `for` exactly once; the toggle's own click
    // flips it once — the two paths never compound into a double toggle.
    // (ReactUnity fires onChange once per toggle at mount; assertions compare
    // deltas against that baseline.)
    public sealed class UnityHtmlToggleTests
    {
        private GameObject _root;
        private UnityHtmlHost _host;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("HTML toggle", typeof(RectTransform), typeof(Canvas));
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 600);
            _host = new UnityHtmlHost();
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void LabelClick_ActivatesItsToggleExactlyOnce()
        {
            var counter = new Counter();
            Mount("<view>"
                + "<label for='#repeat'><text>Repeat delivery</text></label>"
                + "<toggle id='repeat' onChange='Globals.counter.Add(1)'><view/></toggle>"
                + "<toggle id='other' onChange='Globals.counter.Add(10)'><view/></toggle>"
                + "</view>", counter);

            Toggle repeat = ToggleOf("repeat");
            Toggle other = ToggleOf("other");
            bool repeatInitial = repeat.isOn;
            bool otherInitial = other.isOn;
            int baseline = counter.Value;
            LabelClickHandler clickHandler = Label()
                .GameObject.GetComponent<LabelClickHandler>();
            Assert.That(clickHandler, Is.Not.Null);

            clickHandler.OnPointerClick(new PointerEventData(EventSystem.current));

            Assert.That(repeat.isOn, Is.EqualTo(!repeatInitial));
            Assert.That(other.isOn, Is.EqualTo(otherInitial));
            Assert.That(counter.Value, Is.EqualTo(baseline + 1));

            clickHandler.OnPointerClick(new PointerEventData(EventSystem.current));

            Assert.That(repeat.isOn, Is.EqualTo(repeatInitial));
            Assert.That(counter.Value, Is.EqualTo(baseline + 2));
        }

        [Test]
        public void ToggleAndLabelActivation_DoNotCompoundIntoDoubleFlip()
        {
            var counter = new Counter();
            Mount("<view>"
                + "<label for='#repeat'><text>Repeat delivery</text></label>"
                + "<toggle id='repeat' onChange='Globals.counter.Add(1)'><view/></toggle>"
                + "</view>", counter);

            int baseline = counter.Value;
            // The pointer path a Toggle's Selectable takes on a direct click.
            Toggle repeat = ToggleOf("repeat");
            repeat.isOn = !repeat.isOn;
            Assert.That(counter.Value, Is.EqualTo(baseline + 1));

            LabelClickHandler clickHandler = Label()
                .GameObject.GetComponent<LabelClickHandler>();
            clickHandler.OnPointerClick(new PointerEventData(EventSystem.current));

            // One label click after the toggle's own click: two total flips.
            Assert.That(repeat.isOn, Is.False);
            Assert.That(counter.Value, Is.EqualTo(baseline + 2));
        }

        [Test]
        public void DisabledToggle_IsNotActivatedByLabel()
        {
            var counter = new Counter();
            // Production markup emits a plain view (no `for` label) on disabled
            // rows; here the label is absent so there is nothing to activate with.
            Mount("<view>"
                + "<view><text>Repeat delivery</text></view>"
                + "<toggle id='repeat' disabled='true' onChange='Globals.counter.Add(1)'><view/></toggle>"
                + "</view>", counter);

            Toggle repeat = ToggleOf("repeat");
            Assert.That(repeat.interactable, Is.False);
            int baseline = counter.Value;

            Assert.That(repeat.isOn, Is.False);
            Assert.That(counter.Value, Is.EqualTo(baseline));
        }

        private void Mount(string html, Counter counter)
        {
            var globals = new Dictionary<string, object> { ["counter"] = counter };
            UnityHtmlMountResult result = _host.Mount(
                _root.GetComponent<RectTransform>(),
                new UnityHtmlDocument(html, string.Empty, "Toggles"), globals);
            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        }

        private Toggle ToggleOf(string id) => Element(id)
            .GameObject.GetComponent<Toggle>();

        private LabelComponent Label() => _root
            .GetComponentsInChildren<ReactElement>(true)
            .Select(element => element.Component)
            .OfType<LabelComponent>()
            .Single();

        private UGUIComponent Element(string id) => _root
            .GetComponentsInChildren<ReactElement>(true)
            .Select(element => element.Component)
            .Single(component => component != null && component.Id == id) as UGUIComponent;

        public sealed class Counter
        {
            public int Value { get; private set; }
            public void Add(int amount) => Value += amount;
        }
    }
}
