using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ReactUnity.UGUI.Behaviours;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityHTML.Runtime;

namespace UnityHTML.Tests
{
    public sealed class UnityHtmlReconciliationTests
    {
        private GameObject _root;
        private UnityHtmlHost _host;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("HTML reconciliation", typeof(RectTransform), typeof(Canvas));
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
        public void Mount_PreservesNativeInputAndSelectionWhenAnotherRegionChanges()
        {
            const string field = "<input id='name' value='World' placeholder='Name' />";
            Mount("<view>" + field + "<text id='total'>1</text></view>");
            TMP_InputField input = Find("name").GetComponent<TMP_InputField>();
            input.SetTextWithoutNotify("A long unfinished world name");
            input.ForceLabelUpdate();
            input.textComponent.ForceMeshUpdate(true);
            input.selectionAnchorPosition = 3;
            input.selectionFocusPosition = 12;
            Vector2 offset = new Vector2(-20, 0);
            input.textComponent.rectTransform.anchoredPosition = offset;

            Mount("<view>" + field + "<text id='total'>2</text></view>");

            Assert.That(Find("name").GetComponent<TMP_InputField>(), Is.SameAs(input));
            Assert.That(input.text, Is.EqualTo("A long unfinished world name"));
            Assert.That(input.selectionAnchorPosition, Is.EqualTo(3));
            Assert.That(input.selectionFocusPosition, Is.EqualTo(12));
            Assert.That(input.textComponent.rectTransform.anchoredPosition, Is.EqualTo(offset));
            Assert.That(Find("total").GetComponent<TMP_Text>().text, Is.EqualTo("2"));
        }

        [Test]
        public void UpdateRegion_ReordersKeyedControlsAndDoesNotAccumulateCallbacks()
        {
            var bridge = new Counter();
            var globals = new Dictionary<string, object> { ["counter"] = bridge };
            const string first = "<button id='first' onClick='Globals.counter.Add(1)'><text>One</text></button>";
            const string second = "<button id='second'><text>Two</text></button>";
            Mount("<view id='list'>" + first + second + "</view>", globals);
            ReactElement element = Find("first");

            Assert.That(_host.UpdateRegion("list", second + first.Replace("Add(1)", "Add(2)")), Is.True);
            Assert.That(_host.UpdateRegion("list", second + first.Replace("Add(1)", "Add(2)")), Is.True);

            Assert.That(Find("first"), Is.SameAs(element));
            Assert.That(Find("first").transform.GetSiblingIndex(), Is.GreaterThan(Find("second").transform.GetSiblingIndex()));
            element.GetComponent<Button>().onClick.Invoke();
            Assert.That(bridge.Value, Is.EqualTo(2));
        }

        [Test]
        public void UpdateRegion_DataKeyedRowsFollowIdentityAcrossReorderAndRemoval()
        {
            // P074: select row B, remove row A, re-sort — B's element must move
            // with its data-key, keeping its class and component instance.
            const string rowA = "<button data-key='row-a' className='row'><text>Alpha</text></button>";
            const string rowB = "<button data-key='row-b' className='row selected' data-tooltip='Row B'><text>Beta</text></button>";
            const string rowC = "<button data-key='row-c' className='row'><text>Gamma</text></button>";
            Mount("<view id='list'>" + rowA + rowB + rowC + "</view>");

            Button[] before = _root.GetComponentsInChildren<Button>(true);
            Assert.That(before.Length, Is.EqualTo(3));

            Assert.That(_host.UpdateRegion("list", rowB + rowC), Is.True);

            Button[] after = _root.GetComponentsInChildren<Button>(true);
            Assert.That(after.Length, Is.EqualTo(2));
            Assert.That(after[0], Is.SameAs(before[1]),
                "Row B's element must follow its key, not stay at its old index.");
            Assert.That(after[1], Is.SameAs(before[2]));
            Assert.That(after[0].GetComponent<ReactElement>().Component.ClassName,
                Does.Contain("selected"),
                "The selected class must stay bound to row B's element.");
        }

        [Test]
        public void UpdateRegion_UnkeyedRowsMatchPositionally()
        {
            // Control case: without keys the reconciler reuses elements in
            // position order — documenting why data-key matters for rows that
            // carry focus, tooltips or motion.
            const string rowA = "<button className='row'><text>Alpha</text></button>";
            const string rowB = "<button className='row selected'><text>Beta</text></button>";
            Mount("<view id='list'>" + rowA + rowB + "</view>");
            Button[] before = _root.GetComponentsInChildren<Button>(true);

            Assert.That(_host.UpdateRegion("list", rowB), Is.True);

            Button[] after = _root.GetComponentsInChildren<Button>(true);
            Assert.That(after.Length, Is.EqualTo(1));
            Assert.That(after[0], Is.SameAs(before[0]),
                "Unkeyed rows reconcile positionally — the surviving element keeps index 0's instance.");
        }

        [Test]
        public void SetValue_CanUpdateTextAndInputWithoutReplacingEither()
        {
            const string document = "<view><input id='name' value='World' /><text id='status'>Ready</text></view>";
            Mount(document);
            ReactElement field = Find("name");
            Assert.That(_host.SetValue("name", "Kingdom"), Is.True);
            Assert.That(_host.SetValue("status", "Wood & stone"), Is.True);
            Assert.That(field.GetComponent<TMP_InputField>().text, Is.EqualTo("Kingdom"));
            Assert.That(Find("status").GetComponent<TMP_Text>().text, Is.EqualTo("Wood & stone"));
            Mount(document);
            Assert.That(Find("name"), Is.SameAs(field));
            Assert.That(field.GetComponent<TMP_InputField>().text, Is.EqualTo("World"));
        }

        [Test]
        public void UpdateRegion_RemovesNativeSubtreeInEditMode()
        {
            Mount("<view id='list'><button id='old'><text>Old</text></button></view>");
            GameObject removed = Find("old").gameObject;
            Assert.That(_host.UpdateRegion("list", "<input id='new' value='New' />"), Is.True);
            Assert.That(removed == null, Is.True);
            Assert.That(Find("new").GetComponent<TMP_InputField>().text, Is.EqualTo("New"));
        }

        [Test]
        public void Mount_NotifiesGlobalsOnlyWhenTheirValuesChange()
        {
            var globals = new Dictionary<string, object> { ["amount"] = 1 };
            Mount("<text>One</text>", globals);
            var context = (ReactUnity.UGUI.UGUIContext)typeof(UnityHtmlHost)
                .GetField("_context", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(_host);
            int notifications = 0;
            var unsubscribe = context.Globals.AddListener((key, value, record) => notifications++);
            try
            {
                Mount("<text>Two</text>", globals);
                Assert.That(notifications, Is.Zero);
                globals["amount"] = 2;
                Mount("<text>Two</text>", globals);
                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(context.Globals["amount"], Is.EqualTo(2));
            }
            finally { unsubscribe(); }
        }

        [Test]
        public void UpdateRegion_UnknownIdDoesNotModifyDocument()
        {
            Mount("<text id='status'>Ready</text>");
            ReactElement original = Find("status");
            Assert.That(_host.UpdateRegion("missing", "<text>Changed</text>"), Is.False);
            Assert.That(_host.SetValue("missing", "Changed"), Is.False);
            Assert.That(Find("status"), Is.SameAs(original));
        }

        private void Mount(string html, IReadOnlyDictionary<string, object> globals = null)
        {
            UnityHtmlMountResult result = _host.Mount(_root.GetComponent<RectTransform>(),
                new UnityHtmlDocument(html, "input { width: 320px; height: 44px; }", "Reconciliation"), globals);
            Assert.That(result.Succeeded, Is.True, result.ErrorMessage);
        }

        private ReactElement Find(string id) => _root.GetComponentsInChildren<ReactElement>(true)
            .Single(element => element.Component != null && element.Component.Id == id);

        public sealed class Counter
        {
            public int Value { get; private set; }
            public void Add(int amount) => Value += amount;
        }
    }
}
