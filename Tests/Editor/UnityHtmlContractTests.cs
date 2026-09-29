using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DG.Tweening;
using NUnit.Framework;
using ReactUnity.UGUI.Behaviours;
using ReactUnity.UGUI.EventHandlers;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    // P080: the README's canonical markup must mount into an empty host and run
    // the documented opening, update, closing and reduced-motion paths with no
    // hidden dependencies. ExampleMarkup mirrors the README contract example —
    // keep the two in sync.
    public sealed class UnityHtmlContractTests
    {
        private const string ExampleMarkup =
            "<view id='panel' data-motion-role='panel' data-tooltip='Contract panel'>"
            + "<text id='title'>Settings</text>"
            + "<slider id='volume' min='0' max='1' value='0.5' format='percent'"
            + " onChange='Globals.doc.Slide(event)'></slider>"
            + "<label for='#mute'><text>Mute</text></label>"
            + "<toggle id='mute' onChange='Globals.doc.Toggle(event)'><view/></toggle>"
            + "<scroll id='list'>"
            + "<view data-key='row-a'><text>Alpha</text></view>"
            + "<view data-key='row-b'><text>Beta</text></view>"
            + "</scroll>"
            + "<button id='close' onClick='Globals.doc.Close()'><text>Close</text></button>"
            + "</view>";

        // Declarative motion is play-mode only (ApplyDeclaredMotions early-outs
        // outside play); the exit a `data-motion="exit"` flip triggers resolves
        // to this internal Play — the seam UnityHtmlMotionExitTests also uses.
        private static readonly MethodInfo InternalPlay = typeof(UnityHtmlMotionBridge)
            .GetMethod("Play", BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[]
                {
                    typeof(string), typeof(RectTransform), typeof(string),
                    typeof(float), typeof(float), typeof(float),
                    typeof(Ease), typeof(bool),
                },
                null);

        private GameObject _root;
        private UnityHtmlHost _host;
        private DocBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("HTML contract", typeof(RectTransform), typeof(Canvas));
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 600);
            _host = new UnityHtmlHost();
            _bridge = new DocBridge();
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void DocumentedExample_MountsAndExercisesEveryContractPath()
        {
            Mount(ExampleMarkup);

            // Opening — role-declared enter motion, every element resolvable by
            // id, slider formatted per the documented percent contract.
            Assert.That(Find("panel"), Is.Not.Null);
            Assert.That(Find("volume").gameObject.GetComponentInChildren<Slider>(),
                Is.Not.Null);
            Assert.That(Find("volume").gameObject
                    .GetComponentsInChildren<TMP_Text>(true)
                    .Any(t => t.text == "50%"), Is.True,
                "slider must format value=0.5 as percent");
            Assert.That(Find("close").GetComponent<Button>(), Is.Not.Null);

            // Update — keyed rows reconcile by identity, not position.
            ReactElement rowB = Find("row-b");
            Assert.That(_host.UpdateRegion("list",
                "<view data-key='row-b'><text>Beta</text></view>"), Is.True);
            Assert.That(Find("row-b"), Is.SameAs(rowB));
            Assert.That(_root.GetComponentsInChildren<ReactElement>(true)
                .Count(e => e.Component?.Id == "row-a"), Is.EqualTo(0),
                "dropped keyed row must leave the tree");

            // Events — button click and label-for activation reach the bridge.
            Find("close").GetComponent<Button>().onClick.Invoke();
            Assert.That(_bridge.Closes, Is.EqualTo(1));

            int togglesBefore = _bridge.Toggles;
            bool before = Find("mute").GetComponent<Toggle>().isOn;
            var labelClick = _root.GetComponentsInChildren<ReactElement>(true)
                .Select(e => e.GetComponent<LabelClickHandler>())
                .First(h => h != null);
            labelClick.OnPointerClick(new PointerEventData(EventSystem.current));
            Assert.That(Find("mute").GetComponent<Toggle>().isOn, Is.EqualTo(!before));
            Assert.That(_bridge.Toggles, Is.EqualTo(togglesBefore + 1));

            // Unmount leaves nothing behind.
            _host.Unmount();
            Assert.That(_root.transform.childCount, Is.EqualTo(0));
        }

        [Test]
        public void DocumentedExample_ExitUnderReducedMotion_FinishesOnce()
        {
            Mount(ExampleMarkup);

            int exits = 0;
            string exited = null;
            _host.Motion.ReducedMotion = true;
            _host.Motion.ExitFinished += id => { exits++; exited = id; };

            InternalPlay.Invoke(_host.Motion, new object[]
            {
                "panel", Find("panel").gameObject.GetComponent<RectTransform>(),
                "fade-out", 0.12f, 0f, 0f, Ease.InQuad, true,
            });

            Assert.That(exits, Is.EqualTo(1),
                "a declarative exit must finish exactly once under reduced motion");
            Assert.That(exited, Is.EqualTo("panel"));
        }

        private void Mount(string html)
        {
            UnityHtmlMountResult result = _host.Mount(
                _root.GetComponent<RectTransform>(),
                new UnityHtmlDocument(html, string.Empty, "Contract"),
                new Dictionary<string, object> { ["doc"] = _bridge });
            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        }

        private ReactElement Find(string id) => _root
            .GetComponentsInChildren<ReactElement>(true)
            .SingleOrDefault(element => element.Component != null && element.Component.Id == id);

        public sealed class DocBridge
        {
            public int Closes { get; private set; }
            public int Toggles { get; private set; }
            public float LastSlide { get; private set; } = -1f;
            public void Close() => Closes++;
            public void Toggle(object value) => Toggles++;
            public void Slide(object value) => LastSlide = ToFloat(value);
            private static float ToFloat(object value)
                => value is double d ? (float)d : System.Convert.ToSingle(value);
        }
    }
}
